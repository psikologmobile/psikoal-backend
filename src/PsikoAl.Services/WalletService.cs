using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Wallet;
using PsikoAl.Common.Exceptions;
using PsikoAl.Common.Presentation;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Mapping;
using PsikoAl.Services.Options;
using PsikoAl.Services.Wallet;

namespace PsikoAl.Services;

// Uzman cüzdanı (REVIZE-2 §2). Mobil uygulama bakiye, hak, hareket ve faturaları
// GÖSTERİR; yükleme yalnız web sitesinden (App Store / Play kuralı, docs/PAYMENTS.md).
public sealed class WalletService(
    IUnitOfWork unitOfWork,
    ISupabaseStorageService storage,
    INotificationService notificationService,
    IOptions<SiteOptions> siteOptions,
    TimeProvider timeProvider,
    ILogger<WalletService> logger) : IWalletService
{
    public static readonly TimeSpan InvoiceUrlLifetime = TimeSpan.FromMinutes(5);
    private const string UniqueViolationSqlState = "23505";

    public async Task<WalletDto> GetMineAsync(Guid expertUserId, CancellationToken cancellationToken)
    {
        var expert = await GetRequiredExpertAsync(expertUserId, cancellationToken);
        var state = await unitOfWork.ExpertWallets.GetAsync(expertUserId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ExpertNotFound);
        var code = expert.ReferralCode ?? string.Empty;

        return new WalletDto(
            state.BalanceKurus,
            MoneyLabel.Try(state.BalanceKurus),
            state.FreeOfferRights,
            code,
            siteOptions.Value.ReferralLink(code));
    }

    public async Task<WalletPageResult<WalletTransactionDto>> ListMyTransactionsAsync(
        Guid expertUserId,
        int? page,
        int? perPage,
        string? type,
        CancellationToken cancellationToken)
    {
        await GetRequiredExpertAsync(expertUserId, cancellationToken);
        var (effectivePage, effectivePerPage) = Paging(page, perPage);

        var query = unitOfWork.WalletTransactions.QueryForExpert(expertUserId);
        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(transaction => transaction.Type == type);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip((effectivePage - 1) * effectivePerPage)
            .Take(effectivePerPage)
            .Select(transaction => new WalletTransactionReadModel(
                transaction,
                transaction.Listing != null ? transaction.Listing.Title : null,
                transaction.AdminUser != null ? transaction.AdminUser.DisplayName : null))
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        return new WalletPageResult<WalletTransactionDto>(
            [.. rows.Select(row => WalletMapper.ToTransactionDto(row, now))],
            effectivePage,
            effectivePerPage,
            total);
    }

    public async Task<WalletPageResult<WalletPaymentDto>> ListMyPaymentsAsync(
        Guid expertUserId,
        int? page,
        int? perPage,
        CancellationToken cancellationToken)
    {
        await GetRequiredExpertAsync(expertUserId, cancellationToken);
        var (effectivePage, effectivePerPage) = Paging(page, perPage);

        var query = unitOfWork.WalletPayments.Query()
            .Where(payment => payment.ExpertId == expertUserId)
            .OrderByDescending(payment => payment.CreatedAt)
            .ThenByDescending(payment => payment.Id);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip((effectivePage - 1) * effectivePerPage)
            .Take(effectivePerPage)
            .ToListAsync(cancellationToken);

        return new WalletPageResult<WalletPaymentDto>(
            [.. rows.Select(WalletMapper.ToPaymentDto)],
            effectivePage,
            effectivePerPage,
            total);
    }

    // Başka uzmanın ödemesi 404 döner (varlığı sızmasın). Fatura private bucket'ta;
    // yalnız 5 dakikalık imzalı adres verilir.
    public async Task<InvoiceUrlDto> GetInvoiceUrlAsync(Guid expertUserId, Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await unitOfWork.WalletPayments.GetByIdAsync(paymentId, cancellationToken);
        if (payment is null || payment.ExpertId != expertUserId)
        {
            throw new DomainException(ErrorKeys.WalletPaymentNotFound);
        }

        if (payment.InvoicePath is not { Length: > 0 } path)
        {
            throw new DomainException(ErrorKeys.InvoiceNotFound);
        }

        var url = await storage.CreateSignedUrlAsync(StorageBuckets.Invoices, path, InvoiceUrlLifetime, cancellationToken);
        return new InvoiceUrlDto(url);
    }

    public async Task<TopupOptionsDto> GetTopupOptionsAsync(CancellationToken cancellationToken)
    {
        var (minKurus, maxKurus) = await TopupRangeAsync(unitOfWork.SystemSettings, cancellationToken);
        var presets = await SettingValues.GetTopupPresetsTryAsync(unitOfWork.SystemSettings, cancellationToken);
        var vatNote = await SettingValues.GetStringAsync(
            unitOfWork.SystemSettings, SystemSettingKeys.VatNote, SettingValues.DefaultVatNote, cancellationToken);

        return new TopupOptionsDto(
            [.. presets.Select(MoneyLabel.FromTry).Select(kurus => new TopupPresetDto(kurus, MoneyLabel.Try(kurus)))],
            minKurus,
            maxKurus,
            vatNote);
    }

    public static async Task<(long MinKurus, long MaxKurus)> TopupRangeAsync(
        ISystemSettingRepository settings,
        CancellationToken cancellationToken)
    {
        var min = await SettingValues.GetLongAsync(settings, SystemSettingKeys.TopupMinTry, SettingValues.DefaultTopupMinTry, cancellationToken);
        var max = await SettingValues.GetLongAsync(settings, SystemSettingKeys.TopupMaxTry, SettingValues.DefaultTopupMaxTry, cancellationToken);
        return (MoneyLabel.FromTry(min), MoneyLabel.FromTry(Math.Max(min, max)));
    }

    public async Task GrantApprovalRewardsAsync(Guid expertId, CancellationToken cancellationToken)
    {
        await GrantWelcomeRightsAsync(expertId, cancellationToken);
        await GrantReferralRewardAsync(expertId, cancellationToken);
    }

    // "İlk kez onaylandığında": daha önce WELCOME_RIGHT yazıldıysa (revizyon onayı, red
    // sonrası yeniden onay) yeni hak doğmaz. Veritabanında uzman başına tek WELCOME_RIGHT
    // benzersiz indeksle de korunur.
    private async Task GrantWelcomeRightsAsync(Guid expertId, CancellationToken cancellationToken)
    {
        var rights = await SettingValues.GetIntAsync(
            unitOfWork.SystemSettings, SystemSettingKeys.WelcomeFreeRights, SettingValues.DefaultWelcomeFreeRights, cancellationToken);
        if (rights <= 0
            || await unitOfWork.WalletTransactions.Query().AnyAsync(
                transaction => transaction.ExpertId == expertId && transaction.Type == WalletTransactionTypes.WelcomeRight,
                cancellationToken))
        {
            return;
        }

        var written = await RecordRewardAsync(
            new WalletTransaction { ExpertId = expertId, FreeRightsDelta = rights, Type = WalletTransactionTypes.WelcomeRight },
            cancellationToken);
        if (written)
        {
            await notificationService.NotifyAsync(
                expertId,
                NotificationTypes.WelcomeRights,
                new Dictionary<string, string> { ["rights"] = rights.ToString(CultureInfo.InvariantCulture) },
                dataJson: null,
                cancellationToken);
        }
    }

    private async Task GrantReferralRewardAsync(Guid expertId, CancellationToken cancellationToken)
    {
        var referrerId = await unitOfWork.Profiles.Query()
            .Where(profile => profile.Id == expertId)
            .Select(profile => profile.ReferredByExpertId)
            .FirstOrDefaultAsync(cancellationToken);
        if (referrerId is not { } referrer || referrer == expertId)
        {
            return;
        }

        var rights = await SettingValues.GetIntAsync(
            unitOfWork.SystemSettings, SystemSettingKeys.ReferralFreeRights, SettingValues.DefaultReferralFreeRights, cancellationToken);
        if (rights <= 0
            || !await unitOfWork.Experts.Query().AnyAsync(expert => expert.Id == referrer, cancellationToken)
            || await unitOfWork.WalletTransactions.Query().AnyAsync(
                transaction => transaction.Type == WalletTransactionTypes.ReferralRight && transaction.ReferredExpertId == expertId,
                cancellationToken))
        {
            return;
        }

        var written = await RecordRewardAsync(
            new WalletTransaction
            {
                ExpertId = referrer,
                FreeRightsDelta = rights,
                Type = WalletTransactionTypes.ReferralRight,
                ReferredExpertId = expertId,
            },
            cancellationToken);
        if (written)
        {
            await notificationService.NotifyAsync(
                referrer,
                NotificationTypes.ReferralReward,
                new Dictionary<string, string> { ["rights"] = rights.ToString(CultureInfo.InvariantCulture) },
                JsonSerializer.Serialize(new { expertId = expertId.ToString() }),
                cancellationToken);
        }
    }

    // Eşzamanlı iki onay "daha önce yazıldı mı" kontrolünü birlikte geçerse benzersiz indeks
    // ikincisini durdurur; o yarış bir hata değil, ödül zaten yazılmıştır.
    private async Task<bool> RecordRewardAsync(WalletTransaction entry, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            if (await WalletLedger.TryRecordAsync(unitOfWork, entry, cancellationToken) is null)
            {
                return false;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState })
        {
            logger.LogInformation("Ödül zaten yazılmış; eşzamanlı onay yarışı (benzersiz indeks).");
            return false;
        }
    }

    private async Task<Expert> GetRequiredExpertAsync(Guid expertUserId, CancellationToken cancellationToken)
        => await unitOfWork.Experts.GetByIdAsync(expertUserId, cancellationToken)
            ?? throw await ExpertAccess.MissingExpertErrorAsync(
                unitOfWork, expertUserId, ErrorKeys.ExpertRoleRequired, cancellationToken);

    private static (int Page, int PerPage) Paging(int? page, int? perPage)
        => (Math.Max(1, page ?? 1),
            Math.Clamp(perPage ?? WalletFieldLimits.TransactionsDefaultPerPage, 1, WalletFieldLimits.TransactionsMaxPerPage));
}
