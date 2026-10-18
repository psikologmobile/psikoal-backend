using Microsoft.Extensions.Logging.Abstractions;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Options;
using PsikoAl.Services.Tests.Support;

namespace PsikoAl.Services.Tests.Wallet;

// REVIZE-2 §2: cüzdan uçları ve onay ödülleri (hoş geldin + davet).
public sealed class WalletServiceTests : IDisposable
{
    private readonly WalletTestDatabase _db = new();
    private readonly FakeStorageService _storage = new();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    [Fact]
    public async Task Wallet_returns_balance_rights_code_and_link()
    {
        var expert = await _db.AddExpertAsync(balanceKurus: 125_000, freeRights: 2);

        var wallet = await CreateService().GetMineAsync(expert.Id, CancellationToken.None);

        Assert.Equal(125_000, wallet.BalanceKurus);
        Assert.Equal("₺1.250", wallet.BalanceLabel);
        Assert.Equal(2, wallet.FreeOfferRights);
        Assert.Equal(expert.ReferralCode, wallet.ReferralCode);
        Assert.Equal($"https://psikoal.test/davet/{expert.ReferralCode}", wallet.ReferralLink);
    }

    [Fact]
    public async Task Client_asking_for_a_wallet_gets_the_role_error()
    {
        var client = await _db.Database.AddProfileAsync();

        var exception = await Assert.ThrowsAsync<DomainException>(() => CreateService().GetMineAsync(client.Id, CancellationToken.None));

        Assert.Equal(ErrorKeys.ExpertRoleRequired, exception.ErrorKey);
    }

    [Fact]
    public async Task First_approval_grants_welcome_rights_once()
    {
        var expert = await _db.AddExpertAsync();

        await CreateService().GrantApprovalRewardsAsync(expert.Id, CancellationToken.None);
        await CreateService().GrantApprovalRewardsAsync(expert.Id, CancellationToken.None);

        var row = Assert.Single(_db.Context.WalletTransactions);
        Assert.Equal(WalletTransactionTypes.WelcomeRight, row.Type);
        Assert.Equal(1, row.FreeRightsDelta);
        Assert.Equal(1, row.FreeRightsAfter);
        Assert.Equal(1, _db.Wallets.StateOf(expert.Id).FreeOfferRights);
        await _notifications.Received(1).NotifyAsync(
            expert.Id, NotificationTypes.WelcomeRights, Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Welcome_rights_follow_the_setting_and_zero_disables_them()
    {
        await _db.SetSettingAsync(SystemSettingKeys.WelcomeFreeRights, "0");
        var expert = await _db.AddExpertAsync();

        await CreateService().GrantApprovalRewardsAsync(expert.Id, CancellationToken.None);

        Assert.Empty(_db.Context.WalletTransactions);
    }

    [Fact]
    public async Task Approved_referred_expert_rewards_the_referrer_once()
    {
        await _db.SetSettingAsync(SystemSettingKeys.ReferralFreeRights, "2");
        var referrer = await _db.AddExpertAsync();
        var referred = await _db.AddExpertAsync(referredBy: referrer.Id);

        await CreateService().GrantApprovalRewardsAsync(referred.Id, CancellationToken.None);
        await CreateService().GrantApprovalRewardsAsync(referred.Id, CancellationToken.None);

        var reward = Assert.Single(_db.Context.WalletTransactions, row => row.Type == WalletTransactionTypes.ReferralRight);
        Assert.Equal(referrer.Id, reward.ExpertId);
        Assert.Equal(referred.Id, reward.ReferredExpertId);
        Assert.Equal(2, reward.FreeRightsDelta);
        Assert.Equal(2, _db.Wallets.StateOf(referrer.Id).FreeOfferRights);
        await _notifications.Received(1).NotifyAsync(
            referrer.Id, NotificationTypes.ReferralReward, Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expert_without_a_referrer_gives_no_referral_reward()
    {
        var expert = await _db.AddExpertAsync();

        await CreateService().GrantApprovalRewardsAsync(expert.Id, CancellationToken.None);

        Assert.DoesNotContain(_db.Context.WalletTransactions, row => row.Type == WalletTransactionTypes.ReferralRight);
    }

    [Fact]
    public async Task Invoice_url_is_signed_for_the_owner_only()
    {
        var owner = await _db.AddExpertAsync();
        var other = await _db.AddExpertAsync();
        var payment = await AddPaymentAsync(owner.Id, invoicePath: $"{owner.Id}/p.pdf");

        var url = await CreateService().GetInvoiceUrlAsync(owner.Id, payment.Id, CancellationToken.None);
        var foreign = await Assert.ThrowsAsync<DomainException>(
            () => CreateService().GetInvoiceUrlAsync(other.Id, payment.Id, CancellationToken.None));

        Assert.Equal($"https://signed.test/invoices/{owner.Id}/p.pdf", url.Url);
        Assert.Equal(ErrorKeys.WalletPaymentNotFound, foreign.ErrorKey);
    }

    [Fact]
    public async Task Missing_invoice_is_404_INVOICE_NOT_FOUND()
    {
        var owner = await _db.AddExpertAsync();
        var payment = await AddPaymentAsync(owner.Id, invoicePath: null);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateService().GetInvoiceUrlAsync(owner.Id, payment.Id, CancellationToken.None));

        Assert.Equal(ErrorKeys.InvoiceNotFound, exception.ErrorKey);
        Assert.Empty(_storage.SignedPaths);
    }

    [Fact]
    public async Task Topup_options_follow_the_contract_defaults()
    {
        var options = await CreateService().GetTopupOptionsAsync(CancellationToken.None);

        Assert.Equal([50_000L, 100_000L, 250_000L, 500_000L], options.Presets.Select(preset => preset.AmountKurus));
        Assert.Equal("₺2.500", options.Presets[2].Label);
        Assert.Equal(25_000, options.MinKurus);
        Assert.Equal(5_000_000, options.MaxKurus);
        Assert.Equal("KDV dahil", options.VatNote);
    }

    [Fact]
    public async Task Broken_preset_setting_falls_back_to_the_defaults()
    {
        await _db.SetSettingAsync(SystemSettingKeys.TopupPresetsTry, "500,abc");

        var options = await CreateService().GetTopupOptionsAsync(CancellationToken.None);

        Assert.Equal(4, options.Presets.Count);
    }

    [Fact]
    public async Task Transactions_are_paged_newest_first_and_filterable_by_type()
    {
        var expert = await _db.AddExpertAsync();
        for (var i = 0; i < 3; i++)
        {
            _db.Context.WalletTransactions.Add(new WalletTransaction
            {
                ExpertId = expert.Id,
                AmountKurus = 10_000,
                BalanceAfterKurus = 10_000 * (i + 1),
                Type = WalletTransactionTypes.Topup,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(i),
            });
        }

        _db.Context.WalletTransactions.Add(new WalletTransaction
        {
            ExpertId = expert.Id,
            FreeRightsDelta = 1,
            FreeRightsAfter = 1,
            Type = WalletTransactionTypes.WelcomeRight,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });
        await _db.Context.SaveChangesAsync();

        var page = await CreateService().ListMyTransactionsAsync(expert.Id, 1, 2, null, CancellationToken.None);
        var welcome = await CreateService().ListMyTransactionsAsync(expert.Id, null, null, WalletTransactionTypes.WelcomeRight, CancellationToken.None);

        Assert.Equal(4, page.Total);
        Assert.Equal(2, page.Data.Count);
        Assert.Equal(30_000, page.Data[0].BalanceAfterKurus);
        Assert.Equal("+₺100", page.Data[0].AmountLabel);
        Assert.Equal("Bakiye yükleme", page.Data[0].TypeLabel);
        Assert.Equal("+1 hak", Assert.Single(welcome.Data).FreeRightsLabel);
    }

    public void Dispose() => _db.Dispose();

    private async Task<WalletPayment> AddPaymentAsync(Guid expertId, string? invoicePath)
    {
        var payment = new WalletPayment
        {
            Id = Guid.NewGuid(),
            ExpertId = expertId,
            MerchantOid = "PA" + Guid.NewGuid().ToString("N"),
            AmountKurus = 50_000,
            Status = WalletPaymentStatuses.Paid,
            InvoicePath = invoicePath,
            InvoiceFileName = invoicePath is null ? null : "fatura.pdf",
            InvoiceUploadedAt = invoicePath is null ? null : DateTimeOffset.UtcNow,
        };
        _db.Context.WalletPayments.Add(payment);
        await _db.Context.SaveChangesAsync();
        return payment;
    }

    private WalletService CreateService()
        => new(
            _db,
            _storage,
            _notifications,
            Microsoft.Extensions.Options.Options.Create(new SiteOptions { PublicUrl = "https://psikoal.test/" }),
            TimeProvider.System,
            NullLogger<WalletService>.Instance);
}
