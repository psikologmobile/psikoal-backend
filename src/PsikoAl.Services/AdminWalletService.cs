using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Common.Dtos.Wallet;
using PsikoAl.Common.Exceptions;
using PsikoAl.Common.Presentation;
using PsikoAl.Common.Validation;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Mapping;
using PsikoAl.Services.Wallet;

namespace PsikoAl.Services;

// Finans paneli (AdminPolicies.Finance, REVIZE-2 §9): ödemeler ve faturalar, uzman
// cüzdanı, hediye ve düzeltme. Her yazma işlemi audit log'a düşer; hiçbir işlem bakiyeyi
// ya da hakkı eksiye düşüremez (koşullu UPDATE + CHECK).
public sealed class AdminWalletService(
    IUnitOfWork unitOfWork,
    IAdminGuard adminGuard,
    ISupabaseStorageService storage,
    INotificationService notificationService,
    TimeProvider timeProvider) : IAdminWalletService
{
    private const string ExpertWalletEntityType = "expert_wallet";
    private const string PaymentEntityType = "wallet_payment";
    private const string PdfContentType = "application/pdf";

    public async Task<AdminPagedResultDto<AdminWalletPaymentDto>> ListPaymentsAsync(
        AdminWalletPaymentQueryDto query,
        CancellationToken cancellationToken)
    {
        var payments = unitOfWork.WalletPayments.Query();
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            payments = payments.Where(payment => payment.Status == query.Status);
        }

        if (query.HasInvoice is { } hasInvoice)
        {
            payments = hasInvoice
                ? payments.Where(payment => payment.InvoicePath != null)
                : payments.Where(payment => payment.InvoicePath == null);
        }

        var (rows, meta) = await payments
            .OrderByDescending(payment => payment.CreatedAt)
            .ThenByDescending(payment => payment.Id)
            .Select(payment => new
            {
                Payment = payment,
                ExpertName = payment.Expert != null && payment.Expert.Profile != null
                    ? payment.Expert.Profile.FirstName + " " + payment.Expert.Profile.LastName
                    : string.Empty,
                ExpertEmail = payment.Expert != null && payment.Expert.Profile != null ? payment.Expert.Profile.Email : string.Empty,
                UploaderName = payment.InvoiceUploader != null ? payment.InvoiceUploader.DisplayName : null,
            })
            .ToPageAsync(query.Page, query.PerPage, cancellationToken);

        return new AdminPagedResultDto<AdminWalletPaymentDto>(
            [.. rows.Select(row => ToPaymentDto(row.Payment, row.ExpertName, row.ExpertEmail, row.UploaderName))],
            meta);
    }

    // Fatura yalnız PAID ödemeye yüklenir; tekrar yükleme öncekinin üzerine yazar (aynı yol).
    // İçerik PDF imzasıyla (magic bytes) doğrulanır — Content-Type başlığına güvenilmez.
    public async Task<AdminWalletPaymentDto> UploadInvoiceAsync(
        Guid actorAuthUserId,
        Guid paymentId,
        Stream content,
        string contentType,
        string? fileName,
        CancellationToken cancellationToken)
    {
        var actor = await GetRequiredActorAsync(actorAuthUserId, cancellationToken);
        var payment = await unitOfWork.WalletPayments.GetByIdAsync(paymentId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.WalletPaymentNotFound);

        if (payment.Status != WalletPaymentStatuses.Paid)
        {
            throw new DomainException(ErrorKeys.InvoicePaymentNotPaid);
        }

        var validated = await ValidatePdfAsync(content, contentType, cancellationToken);
        var path = $"{payment.ExpertId}/{payment.Id}.pdf";
        await storage.UploadAsync(StorageBuckets.Invoices, path, validated, PdfContentType, isPublicBucket: false, cancellationToken);

        var hadInvoice = payment.InvoicePath is not null;
        payment.InvoicePath = path;
        payment.InvoiceFileName = SafeFileName(fileName, payment);
        payment.InvoiceUploadedAt = timeProvider.GetUtcNow();
        payment.InvoiceUploadedBy = actor.Id;

        await AddAuditAsync(
            actor.Id,
            AuditActions.InvoiceUpload,
            PaymentEntityType,
            payment.Id.ToString(),
            null,
            JsonSerializer.Serialize(new { fileName = payment.InvoiceFileName, replaced = hadInvoice }),
            null,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyAsync(
            payment.ExpertId,
            NotificationTypes.InvoiceReady,
            new Dictionary<string, string> { ["amount"] = MoneyLabel.Try(payment.AmountKurus) },
            JsonSerializer.Serialize(new { paymentId = payment.Id.ToString() }),
            cancellationToken);

        var expert = await unitOfWork.Experts.GetWithProfileAsync(payment.ExpertId, cancellationToken);
        return ToPaymentDto(
            payment,
            expert?.Profile is { } profile ? $"{profile.FirstName} {profile.LastName}" : string.Empty,
            expert?.Profile?.Email ?? string.Empty,
            actor.DisplayName);
    }

    // Admin faturayı görüntülemek için uzmanla aynı kısa ömürlü imzalı adresi alır.
    public async Task<InvoiceUrlDto> GetInvoiceUrlAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await unitOfWork.WalletPayments.GetByIdAsync(paymentId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.WalletPaymentNotFound);

        if (payment.InvoicePath is not { Length: > 0 } path)
        {
            throw new DomainException(ErrorKeys.InvoiceNotFound);
        }

        var url = await storage.CreateSignedUrlAsync(
            StorageBuckets.Invoices, path, WalletService.InvoiceUrlLifetime, cancellationToken);
        return new InvoiceUrlDto(url);
    }

    public async Task<AdminExpertWalletDto> GetExpertWalletAsync(
        Guid expertId,
        int? page,
        int? perPage,
        CancellationToken cancellationToken)
    {
        var expert = await unitOfWork.Experts.GetWithProfileAsync(expertId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ExpertNotFound);
        var state = await unitOfWork.ExpertWallets.GetAsync(expertId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ExpertNotFound);

        var (rows, meta) = await unitOfWork.WalletTransactions.QueryForExpert(expertId)
            .Select(transaction => new WalletTransactionReadModel(
                transaction,
                transaction.Listing != null ? transaction.Listing.Title : null,
                transaction.AdminUser != null ? transaction.AdminUser.DisplayName : null))
            .ToPageAsync(page, perPage, cancellationToken);

        var profile = expert.Profile;
        return new AdminExpertWalletDto(
            expert.Id,
            profile is null ? string.Empty : $"{profile.FirstName} {profile.LastName}".Trim(),
            profile?.Email ?? string.Empty,
            state.BalanceKurus,
            MoneyLabel.Try(state.BalanceKurus),
            state.FreeOfferRights,
            expert.ReferralCode ?? string.Empty,
            new AdminPagedResultDto<AdminWalletTransactionDto>([.. rows.Select(WalletMapper.ToAdminTransactionDto)], meta));
    }

    public async Task<AdminExpertWalletDto> GiftAsync(
        Guid actorAuthUserId,
        Guid expertId,
        AdminWalletGiftDto request,
        CancellationToken cancellationToken)
    {
        var actor = await GetRequiredActorAsync(actorAuthUserId, cancellationToken);
        if (request.AmountKurus < 0 || request.AmountKurus > WalletFieldLimits.AdminMaxAmountKurus || request.AmountKurus % 100 != 0)
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "amountKurus");
        }

        if (request.FreeRights < 0 || request.FreeRights > WalletFieldLimits.AdminMaxFreeRights)
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "freeRights");
        }

        if (request.AmountKurus == 0 && request.FreeRights == 0)
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "amountKurus");
        }

        var reason = NormalizeReason(request.Reason);
        await RecordAdminEntryAsync(
            actor,
            new WalletTransaction
            {
                ExpertId = expertId,
                AmountKurus = request.AmountKurus,
                FreeRightsDelta = request.FreeRights,
                Type = WalletTransactionTypes.AdminGift,
                AdminUserId = actor.Id,
                Reason = reason,
            },
            AuditActions.WalletGift,
            cancellationToken);

        await notificationService.NotifyAsync(
            expertId,
            NotificationTypes.AdminGift,
            new Dictionary<string, string> { ["gift"] = GiftText(request.AmountKurus, request.FreeRights) },
            dataJson: null,
            cancellationToken);

        return await GetExpertWalletAsync(expertId, page: 1, perPage: null, cancellationToken);
    }

    public async Task<AdminExpertWalletDto> AdjustAsync(
        Guid actorAuthUserId,
        Guid expertId,
        AdminWalletAdjustDto request,
        CancellationToken cancellationToken)
    {
        var actor = await GetRequiredActorAsync(actorAuthUserId, cancellationToken);
        var reason = NormalizeReason(request.Reason)
            ?? throw new DomainException(ErrorKeys.WalletAdjustReasonRequired, "reason");

        if (Math.Abs(request.AmountKurus) > WalletFieldLimits.AdminMaxAmountKurus || request.AmountKurus % 100 != 0)
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "amountKurus");
        }

        if (Math.Abs(request.FreeRightsDelta) > WalletFieldLimits.AdminMaxFreeRights)
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "freeRightsDelta");
        }

        if (request.AmountKurus == 0 && request.FreeRightsDelta == 0)
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "amountKurus");
        }

        await RecordAdminEntryAsync(
            actor,
            new WalletTransaction
            {
                ExpertId = expertId,
                AmountKurus = request.AmountKurus,
                FreeRightsDelta = request.FreeRightsDelta,
                Type = WalletTransactionTypes.AdminAdjust,
                AdminUserId = actor.Id,
                Reason = reason,
            },
            AuditActions.WalletAdjust,
            cancellationToken);

        return await GetExpertWalletAsync(expertId, page: 1, perPage: null, cancellationToken);
    }

    private async Task RecordAdminEntryAsync(
        AdminUser actor,
        WalletTransaction entry,
        string auditAction,
        CancellationToken cancellationToken)
    {
        if (!await unitOfWork.Experts.Query().AnyAsync(expert => expert.Id == entry.ExpertId, cancellationToken))
        {
            throw new DomainException(ErrorKeys.ExpertNotFound);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var written = await WalletLedger.TryRecordAsync(unitOfWork, entry, cancellationToken)
            ?? throw new DomainException(ErrorKeys.WalletBalanceNegative, "amountKurus");

        await AddAuditAsync(
            actor.Id,
            auditAction,
            ExpertWalletEntityType,
            entry.ExpertId.ToString(),
            JsonSerializer.Serialize(new
            {
                balanceKurus = written.BalanceAfterKurus - entry.AmountKurus,
                freeOfferRights = written.FreeRightsAfter - entry.FreeRightsDelta,
            }),
            JsonSerializer.Serialize(new
            {
                balanceKurus = written.BalanceAfterKurus,
                freeOfferRights = written.FreeRightsAfter,
                amountKurus = entry.AmountKurus,
                freeRightsDelta = entry.FreeRightsDelta,
            }),
            entry.Reason,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static string GiftText(long amountKurus, int freeRights)
    {
        var parts = new List<string>(2);
        if (amountKurus > 0)
        {
            parts.Add($"{MoneyLabel.Try(amountKurus)} bakiye");
        }

        if (freeRights > 0)
        {
            parts.Add($"{freeRights.ToString(CultureInfo.InvariantCulture)} ücretsiz teklif hakkı");
        }

        return string.Join(" ve ", parts);
    }

    private static string? NormalizeReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > WalletFieldLimits.ReasonMaxLength
            ? throw new DomainException(ErrorKeys.ValidationFailed, "reason")
            : trimmed;
    }

    // İndirilen dosyanın adı: istemcinin verdiği ad yalnız görüntü içindir; yol her zaman
    // sunucuda üretilir. Ad yoksa ya da tehlikeli karakter içeriyorsa üretilmiş ad kullanılır.
    private static string SafeFileName(string? fileName, WalletPayment payment)
    {
        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        var invalid = Path.GetInvalidFileNameChars();
        if (name.Length is 0 or > 120 || name.IndexOfAny(invalid) >= 0 || !name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return $"fatura-{payment.MerchantOid}.pdf";
        }

        return name;
    }

    private static async Task<Stream> ValidatePdfAsync(Stream content, string contentType, CancellationToken cancellationToken)
    {
        if (contentType != PdfContentType)
        {
            throw new DomainException(ErrorKeys.FileTypeNotAllowed, "file");
        }

        var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, cancellationToken);
        if (buffered.Length > WalletFieldLimits.InvoiceMaxBytes)
        {
            throw new DomainException(ErrorKeys.FileTooLarge, "file");
        }

        if (!FileSignatures.Matches(PdfContentType, buffered.GetBuffer().AsSpan(0, (int)Math.Min(buffered.Length, 8))))
        {
            throw new DomainException(ErrorKeys.FileTypeNotAllowed, "file");
        }

        buffered.Position = 0;
        return buffered;
    }

    private static AdminWalletPaymentDto ToPaymentDto(
        WalletPayment payment,
        string expertName,
        string expertEmail,
        string? uploaderName)
        => new(
            payment.Id,
            payment.MerchantOid,
            payment.ExpertId,
            expertName.Trim(),
            expertEmail,
            payment.AmountKurus,
            MoneyLabel.Try(payment.AmountKurus),
            payment.Status,
            WalletLabels.PaymentStatusLabel(payment.Status),
            payment.CreatedAt,
            payment.CompletedAt,
            payment is { InvoicePath: not null, InvoiceFileName: { } fileName, InvoiceUploadedAt: { } uploadedAt }
                ? new AdminInvoiceDto(fileName, uploadedAt, uploaderName)
                : null);

    private async Task<AdminUser> GetRequiredActorAsync(Guid actorAuthUserId, CancellationToken cancellationToken)
        => await adminGuard.GetActiveAdminAsync(actorAuthUserId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.AdminUserNotFound);

    private Task AddAuditAsync(
        Guid adminUserId,
        string action,
        string entityType,
        string entityId,
        string? oldValue,
        string? newValue,
        string? reason,
        CancellationToken cancellationToken)
        => unitOfWork.AuditLogs.AddAsync(
            new AuditLog
            {
                AdminUserId = adminUserId,
                ActorType = AuditActorTypes.Admin,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                OldValue = oldValue,
                NewValue = newValue,
                Reason = reason,
            },
            cancellationToken);
}
