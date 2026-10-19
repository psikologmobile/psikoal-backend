using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Offer.Create;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Pricing;
using PsikoAl.Services.Tests.Support;

namespace PsikoAl.Services.Tests.Wallet;

// Teklif ücreti (REVIZE-2 §1.2, §2.3, §4). Atomikliğin VERİTABANI yarısı (satır kilidi,
// koşullu UPDATE, transaction geri alma) E2E katmanında ölçülür; burada servis sözleşmesi:
// kurallar ÖNCE, ücret onayı (expectedFeeKurus) düşümden ÖNCE, önce ücretsiz hak, yetmezse
// hiçbir şey yazılmaz.
public sealed class OfferFeeChargeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IExpertRepository _experts = Substitute.For<IExpertRepository>();
    private readonly IListingRepository _listings = Substitute.For<IListingRepository>();
    private readonly IOfferRepository _offers = Substitute.For<IOfferRepository>();
    private readonly IReviewRepository _reviews = Substitute.For<IReviewRepository>();
    private readonly IPricingPeriodRepository _periods = Substitute.For<IPricingPeriodRepository>();
    private readonly IWalletTransactionRepository _ledger = Substitute.For<IWalletTransactionRepository>();
    private readonly TestDatabase _database = new();
    private readonly FakeExpertWalletRepository _wallets = new();
    private readonly IUnitOfWorkTransaction _transaction = Substitute.For<IUnitOfWorkTransaction>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly List<WalletTransaction> _ledgerRows = [];
    private readonly List<Offer> _addedOffers = [];
    private readonly OfferService _service;

    private readonly Guid _expertId = Guid.NewGuid();
    private readonly Guid _offerId = Guid.NewGuid();

    // Eğri: başlangıç 1000 TL, taban 250 TL, %10/%10. Onaydan 1 gün sonra, 0 teklif → 900 TL.
    private readonly Listing _listing = new()
    {
        Id = Guid.NewGuid(),
        ClientId = Guid.NewGuid(),
        Title = "Kaygı için destek",
        Description = "d",
        PreferredSessionType = "online",
        Status = ListingStatuses.Open,
        ApprovedAt = Now.AddDays(-1).AddMinutes(-5),
        FeeInitialKurus = 100_000,
        FeeMinKurus = 25_000,
        DailyDecayPct = 10,
        PerOfferDecayPct = 10,
    };

    public OfferFeeChargeTests()
    {
        _unitOfWork.Experts.Returns(_experts);
        _unitOfWork.Listings.Returns(_listings);
        _unitOfWork.Offers.Returns(_offers);
        _unitOfWork.Reviews.Returns(_reviews);
        _unitOfWork.PricingPeriods.Returns(_periods);
        _unitOfWork.WalletTransactions.Returns(_ledger);
        _unitOfWork.ListingClaims.Returns(_database.UnitOfWork.ListingClaims);
        _unitOfWork.ExpertWallets.Returns(_wallets);
        _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(_transaction);

        _periods.GetCurrentAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new PricingPeriod
        {
            Id = Guid.NewGuid(),
            BudgetMinTry = 500,
            FeeMinTry = 250,
            FeeMaxTry = 1000,
            DailyDecayPct = 10,
            PerOfferDecayPct = 10,
            QualityWeights = QualityWeights.Default.ToJson(),
        });
        _experts.GetWithProfileAsync(_expertId, Arg.Any<CancellationToken>()).Returns(ApprovedExpert(ExpertStatuses.Approved));
        _listings.GetByIdAsync(_listing.Id, Arg.Any<CancellationToken>()).Returns(_listing);
        _listings.LockOpenForOfferAsync(_listing.Id, Arg.Any<CancellationToken>()).Returns(0);
        _offers.ExistsForListingAndExpertAsync(_listing.Id, _expertId, Arg.Any<CancellationToken>()).Returns(false);
        _offers.AddAsync(Arg.Do<Offer>(offer =>
            {
                offer.Id = _offerId;
                offer.Listing = _listing;
                _addedOffers.Add(offer);
            }), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _offers.GetWithListingAndExpertAsync(_offerId, Arg.Any<CancellationToken>()).Returns(_ => _addedOffers.Single());
        _ledger.AddAsync(Arg.Do<WalletTransaction>(_ledgerRows.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _service = new OfferService(_unitOfWork, _notifications, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task Fee_is_debited_from_the_balance_and_written_with_the_offer_in_one_transaction()
    {
        _wallets.Set(_expertId, 200_000, 0);

        var offer = await _service.CreateAsync(_expertId, Request(expectedFeeKurus: 90_000), CancellationToken.None);

        var row = Assert.Single(_ledgerRows);
        Assert.Equal(WalletTransactionTypes.OfferFee, row.Type);
        Assert.Equal(-90_000, row.AmountKurus);
        Assert.Equal(110_000, row.BalanceAfterKurus);
        Assert.Equal(_offerId, row.OfferId);
        Assert.Equal(_listing.Id, row.ListingId);
        Assert.Equal(90_000, offer.FeePaidKurus);
        Assert.Equal("₺900", offer.FeePaidLabel);
        Assert.False(offer.UsedFreeRight);
        Assert.Equal(new WalletState(110_000, 0), _wallets.StateOf(_expertId));

        Received.InOrder(() =>
        {
            _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>());
            _listings.LockOpenForOfferAsync(_listing.Id, Arg.Any<CancellationToken>());
            _offers.AddAsync(Arg.Any<Offer>(), Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _offers.IncrementListingOfferCountAsync(_listing.Id, Arg.Any<CancellationToken>());
            _transaction.CommitAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Free_right_is_used_before_the_balance()
    {
        _wallets.Set(_expertId, 200_000, 2);

        var offer = await _service.CreateAsync(_expertId, Request(expectedFeeKurus: 90_000), CancellationToken.None);

        var row = Assert.Single(_ledgerRows);
        Assert.Equal(WalletTransactionTypes.OfferFreeRight, row.Type);
        Assert.Equal(0, row.AmountKurus);
        Assert.Equal(-1, row.FreeRightsDelta);
        Assert.Equal(1, row.FreeRightsAfter);
        Assert.Equal(new WalletState(200_000, 1), _wallets.StateOf(_expertId));
        Assert.True(offer.UsedFreeRight);
        Assert.Equal(0, offer.FeePaidKurus);
        Assert.Equal("Ücretsiz hak", offer.FeePaidLabel);
    }

    [Fact]
    public async Task Free_right_works_even_with_an_empty_balance()
    {
        _wallets.Set(_expertId, 0, 1);

        await _service.CreateAsync(_expertId, Request(expectedFeeKurus: 90_000), CancellationToken.None);

        Assert.Equal(WalletTransactionTypes.OfferFreeRight, Assert.Single(_ledgerRows).Type);
        await _transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Insufficient_balance_rejects_without_committing()
    {
        _wallets.Set(_expertId, 89_900, 0);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _service.CreateAsync(_expertId, Request(expectedFeeKurus: 90_000), CancellationToken.None));

        Assert.Equal(ErrorKeys.InsufficientBalance, exception.ErrorKey);
        Assert.Empty(_ledgerRows);
        Assert.Equal(new WalletState(89_900, 0), _wallets.StateOf(_expertId));
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _transaction.Received(1).DisposeAsync();
        await _notifications.DidNotReceiveWithAnyArgs().NotifyAsync(default, default!, default!, default, default);
    }

    // Uzman 950 TL gördü ama bu arada bir teklif daha geldi: ücret 810 TL. Görmediği bir
    // ücret ödenmez; 409 yeni ücreti meta'da taşır.
    [Fact]
    public async Task Changed_fee_is_rejected_with_the_new_fee_and_nothing_is_written()
    {
        _wallets.Set(_expertId, 200_000, 0);
        _listings.LockOpenForOfferAsync(_listing.Id, Arg.Any<CancellationToken>()).Returns(1);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _service.CreateAsync(_expertId, Request(expectedFeeKurus: 90_000), CancellationToken.None));

        Assert.Equal(ErrorKeys.OfferFeeChanged, exception.ErrorKey);
        Assert.Equal("expectedFeeKurus", exception.Field);
        Assert.Equal("{ feeKurus = 81000, feeLabel = ₺810 }", exception.Meta?.ToString());
        Assert.Empty(_ledgerRows);
        Assert.Empty(_addedOffers);
        Assert.Equal(new WalletState(200_000, 0), _wallets.StateOf(_expertId));
    }

    [Fact]
    public async Task Missing_expected_fee_is_treated_as_a_changed_fee()
    {
        _wallets.Set(_expertId, 200_000, 0);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _service.CreateAsync(_expertId, Request(expectedFeeKurus: null), CancellationToken.None));

        Assert.Equal(ErrorKeys.OfferFeeChanged, exception.ErrorKey);
        Assert.Empty(_ledgerRows);
    }

    [Fact]
    public async Task Listing_closed_between_the_check_and_the_lock_is_rejected()
    {
        _wallets.Set(_expertId, 200_000, 0);
        _listings.LockOpenForOfferAsync(_listing.Id, Arg.Any<CancellationToken>()).Returns((int?)null);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _service.CreateAsync(_expertId, Request(expectedFeeKurus: 90_000), CancellationToken.None));

        Assert.Equal(ErrorKeys.OfferListingNotOpen, exception.ErrorKey);
        Assert.Empty(_ledgerRows);
    }

    [Fact]
    public async Task Duplicate_offer_rule_runs_before_any_charge()
    {
        _wallets.Set(_expertId, 200_000, 0);
        _offers.ExistsForListingAndExpertAsync(_listing.Id, _expertId, Arg.Any<CancellationToken>()).Returns(true);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _service.CreateAsync(_expertId, Request(expectedFeeKurus: 90_000), CancellationToken.None));

        Assert.Equal(ErrorKeys.OfferAlreadyExists, exception.ErrorKey);
        await AssertNoChargeAttemptedAsync();
    }

    [Fact]
    public async Task Closed_listing_rule_runs_before_any_charge()
    {
        _listing.Status = ListingStatuses.Matched;

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _service.CreateAsync(_expertId, Request(expectedFeeKurus: 90_000), CancellationToken.None));

        Assert.Equal(ErrorKeys.OfferListingNotOpen, exception.ErrorKey);
        await AssertNoChargeAttemptedAsync();
    }

    [Fact]
    public async Task Unapproved_expert_rule_runs_before_any_charge()
    {
        _experts.GetWithProfileAsync(_expertId, Arg.Any<CancellationToken>()).Returns(ApprovedExpert(ExpertStatuses.Pending));

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _service.CreateAsync(_expertId, Request(expectedFeeKurus: 90_000), CancellationToken.None));

        Assert.Equal(ErrorKeys.OfferExpertNotApproved, exception.ErrorKey);
        await AssertNoChargeAttemptedAsync();
    }

    [Fact]
    public async Task Quote_shows_the_current_fee_and_the_balance_after()
    {
        _wallets.Set(_expertId, 200_000, 0);
        _listing.OfferCount = 1;

        var quote = await _service.QuoteAsync(_expertId, _listing.Id, CancellationToken.None);

        Assert.Equal(81_000, quote.FeeKurus);
        Assert.Equal("₺810", quote.FeeLabel);
        Assert.Equal("₺2.000", quote.BalanceLabel);
        Assert.False(quote.WillUseFreeRight);
        Assert.Equal(119_000, quote.BalanceAfterKurus);
        Assert.Equal("₺1.190", quote.BalanceAfterLabel);
        Assert.True(quote.Sufficient);
    }

    [Fact]
    public async Task Quote_with_a_free_right_keeps_the_balance()
    {
        _wallets.Set(_expertId, 0, 1);

        var quote = await _service.QuoteAsync(_expertId, _listing.Id, CancellationToken.None);

        Assert.True(quote.WillUseFreeRight);
        Assert.True(quote.Sufficient);
        Assert.Equal(0, quote.BalanceAfterKurus);
    }

    [Fact]
    public async Task Quote_reports_an_insufficient_balance()
    {
        _wallets.Set(_expertId, 10_000, 0);

        var quote = await _service.QuoteAsync(_expertId, _listing.Id, CancellationToken.None);

        Assert.False(quote.Sufficient);
        Assert.Equal(0, quote.BalanceAfterKurus);
    }

    // Revize 2 öncesi onaylanmış ilan: kopya yok, eğri geçerli dönemden türetilir.
    [Fact]
    public async Task Listing_without_a_snapshot_uses_the_current_period()
    {
        _wallets.Set(_expertId, 200_000, 0);
        _listing.FeeInitialKurus = null;
        _listing.FeeMinKurus = null;
        _listing.ApprovedAt = Now;

        var quote = await _service.QuoteAsync(_expertId, _listing.Id, CancellationToken.None);

        Assert.InRange(quote.FeeKurus, 25_000, 100_000);
        Assert.Equal(0, quote.FeeKurus % 1_000);
    }

    public void Dispose() => _database.Dispose();

    private Expert ApprovedExpert(string status) => new()
    {
        Id = _expertId,
        Title = "Klinik Psikolog",
        Status = status,
        Profile = new Profile { Id = _expertId, Email = "uzman@example.test", FirstName = "Elif", LastName = "Kaya" },
    };

    private CreateOfferDto Request(long? expectedFeeKurus) => new(_listing.Id, "Teklif", 1000m, "online", null, expectedFeeKurus);

    private async Task AssertNoChargeAttemptedAsync()
    {
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _offers.DidNotReceive().AddAsync(Arg.Any<Offer>(), Arg.Any<CancellationToken>());
        Assert.Empty(_ledgerRows);
    }
}
