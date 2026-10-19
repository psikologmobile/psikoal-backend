using Microsoft.Extensions.Logging.Abstractions;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Payment;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Options;
using PsikoAl.Services.Payments;

namespace PsikoAl.Services.Tests.Wallet;

public sealed class WalletPaymentServiceTests
{
    private const string MerchantKey = "TESTKEY123";
    private const string MerchantSalt = "TESTSALT456";
    private const string MerchantOid = "PA0123ABC";

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IExpertRepository _experts = Substitute.For<IExpertRepository>();
    private readonly IWalletPaymentRepository _payments = Substitute.For<IWalletPaymentRepository>();
    private readonly IExpertWalletRepository _wallets = Substitute.For<IExpertWalletRepository>();
    private readonly IWalletTransactionRepository _ledger = Substitute.For<IWalletTransactionRepository>();
    private readonly ISystemSettingRepository _settings;
    private readonly IUnitOfWorkTransaction _transaction = Substitute.For<IUnitOfWorkTransaction>();
    private readonly IPayTrClient _payTr = Substitute.For<IPayTrClient>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly List<WalletTransaction> _ledgerRows = [];
    private readonly List<WalletPayment> _addedPayments = [];
    private readonly PayTrOptions _options = new()
    {
        MerchantId = "123456",
        MerchantKey = MerchantKey,
        MerchantSalt = MerchantSalt,
        OkUrl = "https://psikoal.test/ok",
        FailUrl = "https://psikoal.test/fail",
    };

    private readonly Guid _expertId = Guid.NewGuid();
    private readonly WalletPayment _pending;

    public WalletPaymentServiceTests()
    {
        _pending = new WalletPayment
        {
            Id = Guid.NewGuid(),
            ExpertId = _expertId,
            MerchantOid = MerchantOid,
            AmountKurus = 50_000,
            Status = WalletPaymentStatuses.Pending,
        };

        _unitOfWork.Experts.Returns(_experts);
        _unitOfWork.WalletPayments.Returns(_payments);
        _unitOfWork.ExpertWallets.Returns(_wallets);
        _unitOfWork.WalletTransactions.Returns(_ledger);
        _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(_transaction);
        _ledger.AddAsync(Arg.Do<WalletTransaction>(_ledgerRows.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _payments.AddAsync(Arg.Do<WalletPayment>(_addedPayments.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _payments.GetByMerchantOidAsync(MerchantOid, Arg.Any<CancellationToken>()).Returns(_pending);
        _payments.TryCompleteAsync(_pending.Id, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _wallets.TryApplyAsync(_expertId, 50_000, 0, Arg.Any<CancellationToken>()).Returns(new WalletState(320_000, 2));
        _settings = Substitute.For<ISystemSettingRepository>();
        _unitOfWork.SystemSettings.Returns(_settings);
        _experts.GetWithProfileAsync(_expertId, Arg.Any<CancellationToken>()).Returns(new Expert
        {
            Id = _expertId,
            Title = "Klinik Psikolog",
            Status = ExpertStatuses.Approved,
            Profile = new Profile { Id = _expertId, Email = "uzman@example.test", FirstName = "Elif", LastName = "Kaya" },
        });
    }

    [Fact]
    public async Task Successful_callback_credits_once_with_purchase_ledger_and_notification()
    {
        var outcome = await CreateService().HandlePayTrCallbackAsync(Callback("success", "50000"), CancellationToken.None);

        Assert.Equal(PayTrCallbackOutcome.Paid, outcome);
        await _payments.Received(1).TryCompleteAsync(
            _pending.Id, WalletPaymentStatuses.Paid, Arg.Any<string?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _wallets.Received(1).TryApplyAsync(_expertId, 50_000, 0, Arg.Any<CancellationToken>());
        var row = Assert.Single(_ledgerRows);
        Assert.Equal(WalletTransactionTypes.Topup, row.Type);
        Assert.Equal(50_000, row.AmountKurus);
        Assert.Equal(320_000, row.BalanceAfterKurus);
        Assert.Equal(_pending.Id, row.PaymentId);
        await _transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _notifications.Received(1).NotifyAsync(
            _expertId,
            NotificationTypes.BalanceTopup,
            Arg.Is<IReadOnlyDictionary<string, string>>(variables => variables["amount"] == "₺500" && variables["balance"] == "₺3.200"),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Provider_payload_never_stores_the_hash()
    {
        string? storedPayload = null;
        _payments.TryCompleteAsync(_pending.Id, Arg.Any<string>(), Arg.Do<string?>(payload => storedPayload = payload), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var callback = Callback("success", "50000");

        await CreateService().HandlePayTrCallbackAsync(callback, CancellationToken.None);

        Assert.NotNull(storedPayload);
        Assert.DoesNotContain(callback.Hash, storedPayload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_notification_for_a_paid_order_does_not_credit_again()
    {
        _pending.Status = WalletPaymentStatuses.Paid;

        var outcome = await CreateService().HandlePayTrCallbackAsync(Callback("success", "50000"), CancellationToken.None);

        Assert.Equal(PayTrCallbackOutcome.AlreadyProcessed, outcome);
        await AssertNoCreditAsync();
    }

    [Fact]
    public async Task Concurrent_duplicate_that_loses_the_status_transition_does_not_credit()
    {
        _payments.TryCompleteAsync(_pending.Id, WalletPaymentStatuses.Paid, Arg.Any<string?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var outcome = await CreateService().HandlePayTrCallbackAsync(Callback("success", "50000"), CancellationToken.None);

        Assert.Equal(PayTrCallbackOutcome.AlreadyProcessed, outcome);
        await AssertNoCreditAsync();
        await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bad_hash_is_rejected_before_touching_the_database()
    {
        var forged = Callback("success", "50000") with { Hash = "c2FodGU=" };

        var outcome = await CreateService().HandlePayTrCallbackAsync(forged, CancellationToken.None);

        Assert.Equal(PayTrCallbackOutcome.InvalidHash, outcome);
        await _payments.DidNotReceiveWithAnyArgs().GetByMerchantOidAsync(default!, default);
        await AssertNoCreditAsync();
    }

    [Fact]
    public async Task Hash_over_a_different_amount_is_rejected()
    {
        var tampered = Callback("success", "50000") with { TotalAmount = "99999999" };

        var outcome = await CreateService().HandlePayTrCallbackAsync(tampered, CancellationToken.None);

        Assert.Equal(PayTrCallbackOutcome.InvalidHash, outcome);
        await AssertNoCreditAsync();
    }

    [Fact]
    public async Task Unconfigured_provider_accepts_no_callback()
    {
        _options.MerchantKey = null;

        var outcome = await CreateService().HandlePayTrCallbackAsync(Callback("success", "50000"), CancellationToken.None);

        Assert.Equal(PayTrCallbackOutcome.InvalidHash, outcome);
    }

    [Fact]
    public async Task Failed_status_marks_the_payment_failed_and_gives_no_credit()
    {
        var outcome = await CreateService().HandlePayTrCallbackAsync(Callback("failed", "50000"), CancellationToken.None);

        Assert.Equal(PayTrCallbackOutcome.Failed, outcome);
        await _payments.Received(1).TryCompleteAsync(
            _pending.Id, WalletPaymentStatuses.Failed, Arg.Any<string?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await AssertNoCreditAsync();
    }

    [Fact]
    public async Task Underpaid_success_is_treated_as_failed()
    {
        var outcome = await CreateService().HandlePayTrCallbackAsync(Callback("success", "100"), CancellationToken.None);

        Assert.Equal(PayTrCallbackOutcome.Failed, outcome);
        await AssertNoCreditAsync();
    }

    [Fact]
    public async Task Unknown_order_is_not_acknowledged()
    {
        var callback = Callback("success", "50000", merchantOid: "PAYOKBOYLE");

        var outcome = await CreateService().HandlePayTrCallbackAsync(callback, CancellationToken.None);

        Assert.Equal(PayTrCallbackOutcome.UnknownOrder, outcome);
        await AssertNoCreditAsync();
    }

    [Fact]
    public async Task Init_creates_a_pending_alphanumeric_order_and_returns_the_iframe_url()
    {
        PayTrTokenRequest? sent = null;
        _payTr.GetIframeTokenAsync(Arg.Do<PayTrTokenRequest>(request => sent = request), Arg.Any<CancellationToken>())
            .Returns(PayTrTokenResult.Success("tok123"));

        var result = await CreateService().InitPayTrAsync(_expertId, 50_000, "85.105.10.20", CancellationToken.None);

        var payment = Assert.Single(_addedPayments);
        Assert.Equal(WalletPaymentStatuses.Pending, payment.Status);
        Assert.Matches("^[A-Za-z0-9]{1,64}$", payment.MerchantOid);

        Assert.Equal(50_000, payment.AmountKurus);
        Assert.Equal("tok123", result.IframeToken);
        Assert.Equal(payment.MerchantOid, result.MerchantOid);
        Assert.Equal("https://www.paytr.com/odeme/guvenli/tok123", result.IframeUrl);
        Assert.NotNull(sent);
        Assert.Equal(50000, sent.PaymentAmountKurus);
        Assert.Equal("85.105.10.20", sent.UserIp);
        Assert.Equal("uzman@example.test", sent.Email);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Init_marks_the_order_failed_when_PayTR_gives_no_token()
    {
        _payTr.GetIframeTokenAsync(Arg.Any<PayTrTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(PayTrTokenResult.Failure("Zorunlu alan degeri gecersiz"));

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateService().InitPayTrAsync(_expertId, 50_000, "85.105.10.20", CancellationToken.None));

        Assert.Equal(ErrorKeys.PaymentProviderUnavailable, exception.ErrorKey);
        await _payments.Received(1).TryCompleteAsync(
            Arg.Any<Guid>(), WalletPaymentStatuses.Failed, Arg.Any<string?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // Varsayılan aralık 250–50.000 TL (TOPUP_MIN_TRY / TOPUP_MAX_TRY); kuruşlu tutar yok.
    [Theory]
    [InlineData(24_900)]
    [InlineData(5_000_100)]
    [InlineData(50_050)]
    [InlineData(0)]
    public async Task Init_rejects_an_amount_outside_the_topup_range(long amountKurus)
    {
        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateService().InitPayTrAsync(_expertId, amountKurus, "85.105.10.20", CancellationToken.None));

        Assert.Equal(ErrorKeys.TopupAmountOutOfRange, exception.ErrorKey);
        Assert.Equal("amountKurus", exception.Field);
        Assert.Empty(_addedPayments);
        await _payTr.DidNotReceiveWithAnyArgs().GetIframeTokenAsync(default!, default);
    }

    [Fact]
    public async Task Init_uses_the_range_from_system_settings()
    {
        _settings.GetAsync(SystemSettingKeys.TopupMinTry, Arg.Any<CancellationToken>())
            .Returns(new SystemSetting { Key = SystemSettingKeys.TopupMinTry, Value = "1000" });
        _payTr.GetIframeTokenAsync(Arg.Any<PayTrTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(PayTrTokenResult.Success("tok123"));

        var tooSmall = await Assert.ThrowsAsync<DomainException>(
            () => CreateService().InitPayTrAsync(_expertId, 50_000, "85.105.10.20", CancellationToken.None));
        await CreateService().InitPayTrAsync(_expertId, 100_000, "85.105.10.20", CancellationToken.None);

        Assert.Equal(ErrorKeys.TopupAmountOutOfRange, tooSmall.ErrorKey);
        Assert.Equal(100_000, Assert.Single(_addedPayments).AmountKurus);
    }

    [Fact]
    public async Task Init_requires_an_approved_expert()
    {
        _experts.GetWithProfileAsync(_expertId, Arg.Any<CancellationToken>()).Returns(new Expert
        {
            Id = _expertId,
            Title = "Klinik Psikolog",
            Status = ExpertStatuses.Pending,
            Profile = new Profile { Id = _expertId, Email = "uzman@example.test" },
        });

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateService().InitPayTrAsync(_expertId, 50_000, "85.105.10.20", CancellationToken.None));

        Assert.Equal(ErrorKeys.TopupExpertNotApproved, exception.ErrorKey);
        Assert.Empty(_addedPayments);
    }

    [Fact]
    public async Task Init_without_PayTR_credentials_is_unavailable()
    {
        _options.MerchantSalt = string.Empty;

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateService().InitPayTrAsync(_expertId, 50_000, "85.105.10.20", CancellationToken.None));

        Assert.Equal(ErrorKeys.PaymentProviderUnavailable, exception.ErrorKey);
        await _payTr.DidNotReceiveWithAnyArgs().GetIframeTokenAsync(default!, default);
    }

    private static PayTrCallbackDto Callback(string status, string totalAmount, string merchantOid = MerchantOid)
        => new(
            merchantOid,
            status,
            totalAmount,
            PayTrHash.CallbackHash(merchantOid, status, totalAmount, MerchantSalt, MerchantKey),
            status == "failed" ? "0" : null,
            status == "failed" ? "Kart limiti yetersiz" : null,
            "1",
            "card",
            "TL",
            "50000");

    private WalletPaymentService CreateService()
        => new(
            _unitOfWork,
            _payTr,
            _notifications,
            Microsoft.Extensions.Options.Options.Create(_options),
            TimeProvider.System,
            NullLogger<WalletPaymentService>.Instance);

    private async Task AssertNoCreditAsync()
    {
        await _wallets.DidNotReceiveWithAnyArgs().TryApplyAsync(default, default, default, default);
        Assert.Empty(_ledgerRows);
        await _notifications.DidNotReceiveWithAnyArgs().NotifyAsync(default, default!, default!, default, default);
    }
}
