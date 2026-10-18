using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Listing.Create;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Tests.Support;

namespace PsikoAl.Services.Tests.Listings;

// REVIZE-2 §3.1–3.2: açık rıza, telefon, şehir, dönemsel bütçe aralığı, davet izi ve
// otomatik onayda ücret eğrisinin dondurulması.
public sealed class ListingCreateRulesTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    [Fact]
    public async Task Missing_contact_consent_is_rejected()
    {
        var client = await SetupAsync();

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateService().CreateAsync(client.Id, Request() with { ContactConsent = false }, CancellationToken.None));

        Assert.Equal(ErrorKeys.ContactConsentRequired, exception.ErrorKey);
        Assert.Empty(_db.Context.Listings);
    }

    [Fact]
    public async Task Phone_is_required_when_the_profile_has_none()
    {
        var client = await SetupAsync();

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateService().CreateAsync(client.Id, Request() with { Phone = null }, CancellationToken.None));

        Assert.Equal(ErrorKeys.PhoneRequired, exception.ErrorKey);
    }

    [Fact]
    public async Task Phone_from_the_request_is_written_to_the_profile_and_consent_is_timestamped()
    {
        var client = await SetupAsync();

        var listing = await CreateService().CreateAsync(client.Id, Request(), CancellationToken.None);

        Assert.Equal("05551112233", (await _db.Context.Profiles.FindAsync(client.Id))!.Phone);
        Assert.NotNull(listing.ContactConsentAt);
        Assert.NotNull(Assert.Single(_db.Context.Listings).ContactConsentAt);
    }

    [Theory]
    [InlineData("yüz_yüze")]
    [InlineData("yüz_yüze_online")]
    public async Task City_is_required_for_face_to_face_sessions(string sessionType)
    {
        var client = await SetupAsync();

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateService().CreateAsync(client.Id, Request() with { PreferredSessionType = sessionType, City = " " }, CancellationToken.None));

        Assert.Equal(ErrorKeys.ValidationFailed, exception.ErrorKey);
        Assert.Equal("city", exception.Field);
    }

    [Theory]
    [InlineData(400, 900)]   // alt sınır 500 TL
    [InlineData(500, 3_000)] // üst sınır 2.000 TL (dönemde tanımlı)
    public async Task Budget_outside_the_period_range_is_rejected(int min, int max)
    {
        var client = await SetupAsync(budgetMaxTry: 2_000);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateService().CreateAsync(client.Id, Request() with { BudgetMin = min, BudgetMax = max }, CancellationToken.None));

        Assert.Equal(ErrorKeys.ListingBudgetOutOfRange, exception.ErrorKey);
    }

    [Fact]
    public async Task Unlimited_budget_max_accepts_any_upper_bound()
    {
        var client = await SetupAsync(budgetMaxTry: null);

        var listing = await CreateService().CreateAsync(client.Id, Request() with { BudgetMax = 1_000_000 }, CancellationToken.None);

        Assert.Equal(1_000_000, listing.BudgetMax);
    }

    [Fact]
    public async Task Invited_client_listing_carries_the_inviting_expert()
    {
        var client = await SetupAsync();
        var expertId = Guid.NewGuid();
        client.ReferredByExpertId = expertId;
        await _db.Context.SaveChangesAsync();

        await CreateService().CreateAsync(client.Id, Request(), CancellationToken.None);

        Assert.Equal(expertId, Assert.Single(_db.Context.Listings).InvitedExpertId);
        await _notifications.DidNotReceiveWithAnyArgs().NotifyAsync(default, default!, default!, default, default);
    }

    // LISTING_AUTO_APPROVE: admin onayıyla aynı yayın adımı — eğri dondurulur, davet eden uzmana haber gider.
    [Fact]
    public async Task Auto_approved_listing_gets_the_fee_snapshot_and_notifies_the_inviter()
    {
        var client = await SetupAsync(autoApprove: true);
        var expertId = Guid.NewGuid();
        client.ReferredByExpertId = expertId;
        await _db.Context.SaveChangesAsync();

        var dto = await CreateService().CreateAsync(client.Id, Request(), CancellationToken.None);

        var listing = Assert.Single(_db.Context.Listings);
        Assert.Equal(ListingStatuses.Open, dto.Status);
        Assert.NotNull(listing.QualityScore);
        Assert.InRange(listing.FeeInitialKurus!.Value, 25_000, 100_000);
        Assert.Equal(25_000, listing.FeeMinKurus);
        Assert.Equal(10m, listing.DailyDecayPct);
        Assert.Equal(10m, listing.PerOfferDecayPct);
        await _notifications.Received(1).NotifyAsync(
            expertId, NotificationTypes.InvitedClientListing, Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rules_come_from_the_current_period()
    {
        await SetupAsync(budgetMaxTry: 4_000);

        var rules = await CreateService().GetRulesAsync(CancellationToken.None);

        Assert.Equal(500, rules.BudgetMinTry);
        Assert.Equal(4_000, rules.BudgetMaxTry);
        Assert.Equal(10, rules.TitleMin);
        Assert.Equal(100, rules.TitleMax);
        Assert.Equal(2_000, rules.DescriptionMax);
        Assert.Equal(["yüz_yüze", "yüz_yüze_online"], rules.CityRequiredFor);
        Assert.Equal(3, rules.MaxActive);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Profile> SetupAsync(int? budgetMaxTry = null, bool autoApprove = false)
    {
        var period = await _db.AddPricingPeriodAsync();
        period.BudgetMaxTry = budgetMaxTry;
        _db.Context.SystemSettings.AddRange(
            new SystemSetting { Key = SystemSettingKeys.ListingAutoApprove, Value = autoApprove ? "true" : "false" },
            new SystemSetting { Key = SystemSettingKeys.ListingExpireDays, Value = "30" },
            new SystemSetting { Key = SystemSettingKeys.ListingMaxActive, Value = "3" });
        await _db.Context.SaveChangesAsync();
        return await _db.AddProfileAsync();
    }

    private static CreateListingDto Request()
        => new(
            "Kaygı için destek arıyorum",
            "Açıklama",
            ["Kaygı"],
            500,
            900,
            "online",
            City: null,
            AssessmentResultId: null,
            ContactConsent: true,
            Phone: "05551112233");

    private ListingService CreateService()
        => new(_db.UnitOfWork, new AllCategoriesExistStub(), _notifications, TimeProvider.System);
}
