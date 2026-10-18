using System.Text;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Tests.Support;

namespace PsikoAl.Services.Tests.Wallet;

// REVIZE-2 §9 finans paneli: hediye, düzeltme (gerekçe zorunlu, eksiye düşemez), fatura.
public sealed class AdminWalletServiceTests : IDisposable
{
    private readonly WalletTestDatabase _db = new();
    private readonly FakeStorageService _storage = new();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IAdminGuard _guard = Substitute.For<IAdminGuard>();
    private readonly AdminUser _admin = new() { Id = Guid.NewGuid(), AuthUserId = Guid.NewGuid(), DisplayName = "Finans", Role = AdminRoles.Finance };

    public AdminWalletServiceTests()
    {
        _guard.GetActiveAdminAsync(_admin.AuthUserId, Arg.Any<CancellationToken>()).Returns(_admin);
        _db.Context.AdminUsers.Add(_admin);
        _db.Context.SaveChanges();
    }

    [Fact]
    public async Task Gift_adds_balance_and_rights_with_ledger_audit_and_notification()
    {
        var expert = await _db.AddExpertAsync(balanceKurus: 10_000);

        var wallet = await CreateService().GiftAsync(
            _admin.AuthUserId, expert.Id, new AdminWalletGiftDto(50_000, 2, "Kampanya"), CancellationToken.None);

        Assert.Equal(60_000, wallet.BalanceKurus);
        Assert.Equal("₺600", wallet.BalanceLabel);
        Assert.Equal(2, wallet.FreeOfferRights);
        var row = Assert.Single(wallet.Transactions.Data);
        Assert.Equal(WalletTransactionTypes.AdminGift, row.Type);
        Assert.Equal("+₺500", row.AmountLabel);
        Assert.Equal("+2 hak", row.FreeRightsLabel);
        Assert.Equal("Finans", row.AdminName);
        Assert.Contains(_db.Context.AuditLogs, log => log.Action == AuditActions.WalletGift && log.EntityId == expert.Id.ToString());
        await _notifications.Received(1).NotifyAsync(
            expert.Id,
            NotificationTypes.AdminGift,
            Arg.Is<IReadOnlyDictionary<string, string>>(variables => variables["gift"] == "₺500 bakiye ve 2 ücretsiz teklif hakkı"),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-100, 0)]
    [InlineData(0, -1)]
    [InlineData(50_050, 0)]
    public async Task Invalid_gift_is_rejected(long amountKurus, int rights)
    {
        var expert = await _db.AddExpertAsync();

        var exception = await Assert.ThrowsAsync<DomainException>(() => CreateService().GiftAsync(
            _admin.AuthUserId, expert.Id, new AdminWalletGiftDto(amountKurus, rights, null), CancellationToken.None));

        Assert.Equal(ErrorKeys.ValidationFailed, exception.ErrorKey);
        Assert.Empty(_db.Context.WalletTransactions);
    }

    [Fact]
    public async Task Adjust_requires_a_reason()
    {
        var expert = await _db.AddExpertAsync(balanceKurus: 10_000);

        var exception = await Assert.ThrowsAsync<DomainException>(() => CreateService().AdjustAsync(
            _admin.AuthUserId, expert.Id, new AdminWalletAdjustDto(-5_000, 0, "  "), CancellationToken.None));

        Assert.Equal(ErrorKeys.WalletAdjustReasonRequired, exception.ErrorKey);
        Assert.Empty(_db.Context.WalletTransactions);
    }

    [Fact]
    public async Task Adjust_cannot_take_the_balance_or_rights_below_zero()
    {
        var expert = await _db.AddExpertAsync(balanceKurus: 10_000, freeRights: 1);

        var balance = await Assert.ThrowsAsync<DomainException>(() => CreateService().AdjustAsync(
            _admin.AuthUserId, expert.Id, new AdminWalletAdjustDto(-20_000, 0, "hata"), CancellationToken.None));
        var rights = await Assert.ThrowsAsync<DomainException>(() => CreateService().AdjustAsync(
            _admin.AuthUserId, expert.Id, new AdminWalletAdjustDto(0, -2, "hata"), CancellationToken.None));

        Assert.Equal(ErrorKeys.WalletBalanceNegative, balance.ErrorKey);
        Assert.Equal(ErrorKeys.WalletBalanceNegative, rights.ErrorKey);
        Assert.Equal(new Data.Repositories.Abstractions.WalletState(10_000, 1), _db.Wallets.StateOf(expert.Id));
        Assert.Empty(_db.Context.WalletTransactions);
    }

    [Fact]
    public async Task Adjust_writes_a_signed_ledger_row_with_the_reason()
    {
        var expert = await _db.AddExpertAsync(balanceKurus: 100_000, freeRights: 1);

        var wallet = await CreateService().AdjustAsync(
            _admin.AuthUserId, expert.Id, new AdminWalletAdjustDto(-62_500, -1, "Mükerrer yükleme"), CancellationToken.None);

        var row = Assert.Single(wallet.Transactions.Data);
        Assert.Equal("−₺625", row.AmountLabel);
        Assert.Equal("−1 hak", row.FreeRightsLabel);
        Assert.Equal("Düzeltme: Mükerrer yükleme", row.Description);
        Assert.Equal(37_500, wallet.BalanceKurus);
        Assert.Equal(0, wallet.FreeOfferRights);
    }

    [Fact]
    public async Task Invoice_upload_stores_the_pdf_privately_and_notifies_the_expert()
    {
        var expert = await _db.AddExpertAsync();
        var payment = await AddPaymentAsync(expert.Id, WalletPaymentStatuses.Paid);

        var result = await CreateService().UploadInvoiceAsync(
            _admin.AuthUserId, payment.Id, Pdf(), "application/pdf", "Fatura-2026.pdf", CancellationToken.None);

        Assert.Equal("Fatura-2026.pdf", result.Invoice?.FileName);
        Assert.Equal("Finans", result.Invoice?.UploadedByName);
        var stored = await _db.Context.WalletPayments.FindAsync(payment.Id);
        Assert.Equal($"{expert.Id}/{payment.Id}.pdf", stored!.InvoicePath);
        await _notifications.Received(1).NotifyAsync(
            expert.Id, NotificationTypes.InvoiceReady, Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoice_upload_rejects_a_non_pdf_body_even_with_a_pdf_content_type()
    {
        var expert = await _db.AddExpertAsync();
        var payment = await AddPaymentAsync(expert.Id, WalletPaymentStatuses.Paid);

        var exception = await Assert.ThrowsAsync<DomainException>(() => CreateService().UploadInvoiceAsync(
            _admin.AuthUserId, payment.Id, new MemoryStream(Encoding.ASCII.GetBytes("MZ-not-a-pdf")), "application/pdf", "x.pdf", CancellationToken.None));

        Assert.Equal(ErrorKeys.FileTypeNotAllowed, exception.ErrorKey);
    }

    [Fact]
    public async Task Invoice_upload_requires_a_paid_payment()
    {
        var expert = await _db.AddExpertAsync();
        var payment = await AddPaymentAsync(expert.Id, WalletPaymentStatuses.Pending);

        var exception = await Assert.ThrowsAsync<DomainException>(() => CreateService().UploadInvoiceAsync(
            _admin.AuthUserId, payment.Id, Pdf(), "application/pdf", "x.pdf", CancellationToken.None));

        Assert.Equal(ErrorKeys.InvoicePaymentNotPaid, exception.ErrorKey);
    }

    [Fact]
    public async Task Unsafe_invoice_file_name_is_replaced()
    {
        var expert = await _db.AddExpertAsync();
        var payment = await AddPaymentAsync(expert.Id, WalletPaymentStatuses.Paid);

        var result = await CreateService().UploadInvoiceAsync(
            _admin.AuthUserId, payment.Id, Pdf(), "application/pdf", "virus.exe", CancellationToken.None);

        Assert.Equal($"fatura-{payment.MerchantOid}.pdf", result.Invoice?.FileName);
    }

    [Fact]
    public async Task Payments_can_be_filtered_by_invoice_presence()
    {
        var expert = await _db.AddExpertAsync();
        var withInvoice = await AddPaymentAsync(expert.Id, WalletPaymentStatuses.Paid);
        await CreateService().UploadInvoiceAsync(_admin.AuthUserId, withInvoice.Id, Pdf(), "application/pdf", "a.pdf", CancellationToken.None);
        await AddPaymentAsync(expert.Id, WalletPaymentStatuses.Paid);

        var missing = await CreateService().ListPaymentsAsync(new AdminWalletPaymentQueryDto(null, false, null, null), CancellationToken.None);
        var present = await CreateService().ListPaymentsAsync(new AdminWalletPaymentQueryDto(WalletPaymentStatuses.Paid, true, null, null), CancellationToken.None);

        Assert.Equal(1, missing.Meta.Total);
        Assert.Null(missing.Data[0].Invoice);
        Assert.Equal(withInvoice.Id, Assert.Single(present.Data).Id);
        Assert.Equal("Ödendi", present.Data[0].StatusLabel);
    }

    public void Dispose() => _db.Dispose();

    private static MemoryStream Pdf() => new(Encoding.ASCII.GetBytes("%PDF-1.7\n%test\n"));

    private async Task<WalletPayment> AddPaymentAsync(Guid expertId, string status)
    {
        var payment = new WalletPayment
        {
            Id = Guid.NewGuid(),
            ExpertId = expertId,
            MerchantOid = "PA" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            AmountKurus = 50_000,
            Status = status,
        };
        _db.Context.WalletPayments.Add(payment);
        await _db.Context.SaveChangesAsync();
        return payment;
    }

    private AdminWalletService CreateService() => new(_db, _guard, _storage, _notifications, TimeProvider.System);
}
