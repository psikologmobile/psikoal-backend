using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Services.Wallet;

namespace PsikoAl.Services.Tests.Admin;

// Revize 2 admin kuralları: anlaşma bildirimi durumu, işlem kaydı türleri, ayar ve dönem doğrulaması.
public sealed class Revize2AdminRulesTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resolved_claim_is_resolved_regardless_of_age()
        => Assert.Equal(
            ClaimStatuses.Resolved,
            AdminClaimService.StatusOf(Claim(Cutoff.AddDays(-10), resolved: true), ListingStatuses.Open, Cutoff));

    [Fact]
    public void Old_claim_on_an_unmatched_listing_waits_for_review()
        => Assert.Equal(
            ClaimStatuses.ReviewPending,
            AdminClaimService.StatusOf(Claim(Cutoff.AddHours(-1)), ListingStatuses.Open, Cutoff));

    [Fact]
    public void Old_claim_on_a_matched_listing_stays_open()
        => Assert.Equal(
            ClaimStatuses.Open,
            AdminClaimService.StatusOf(Claim(Cutoff.AddDays(-5)), ListingStatuses.Matched, Cutoff));

    [Fact]
    public void Fresh_claim_is_open()
        => Assert.Equal(
            ClaimStatuses.Open,
            AdminClaimService.StatusOf(Claim(Cutoff.AddHours(1)), ListingStatuses.Open, Cutoff));

    [Fact]
    public void Activity_kinds_default_to_all_and_accept_a_comma_list()
    {
        Assert.Equal(ActivityKinds.All.Count, AdminActivityService.ParseKinds(null).Count);
        Assert.Equal(new HashSet<string> { ActivityKinds.Offer, ActivityKinds.Wallet }, AdminActivityService.ParseKinds("offer, WALLET"));
    }

    [Fact]
    public void Unknown_activity_kind_is_rejected()
    {
        var exception = Assert.Throws<DomainException>(() => AdminActivityService.ParseKinds("OFFER,PAYMENTS"));

        Assert.Equal("kind", exception.Field);
    }

    [Theory]
    [InlineData(SystemSettingKeys.TopupPresetsTry, "500,1000,2500,5000", true)]
    [InlineData(SystemSettingKeys.TopupPresetsTry, "500,,1000", false)]
    [InlineData(SystemSettingKeys.TopupPresetsTry, "0,500", false)]
    [InlineData(SystemSettingKeys.TopupMinTry, "250", true)]
    [InlineData(SystemSettingKeys.TopupMinTry, "-5", false)]
    [InlineData(SystemSettingKeys.TopupMaxTry, "0", false)]
    [InlineData(SystemSettingKeys.WelcomeFreeRights, "0", true)]
    [InlineData(SystemSettingKeys.WelcomeFreeRights, "1000", false)]
    [InlineData(SystemSettingKeys.ReferralFreeRights, "1.5", false)]
    [InlineData(SystemSettingKeys.ClaimsEnabled, "false", true)]
    [InlineData(SystemSettingKeys.ClaimsEnabled, "evet", false)]
    [InlineData(SystemSettingKeys.ClaimAdminReviewDays, "3", true)]
    [InlineData(SystemSettingKeys.VatNote, "", false)]
    [InlineData(SystemSettingKeys.ListingExpireDays, "anything", true)]
    public void Revize2_settings_are_validated_before_saving(string key, string value, bool valid)
    {
        if (valid)
        {
            SystemSettingsService.EnsureValidValue(key, value);
        }
        else
        {
            Assert.Throws<DomainException>(() => SystemSettingsService.EnsureValidValue(key, value));
        }
    }

    [Fact]
    public void Preset_parser_returns_the_amounts_in_order()
    {
        Assert.True(SettingValues.TryParsePresets(" 500, 1000 ,2500", out var presets));
        Assert.Equal([500L, 1000L, 2500L], presets);
    }

    [Theory]
    [InlineData(0, null, 250, 1000, "budgetMinTry")]
    [InlineData(500, 400, 250, 1000, "budgetMaxTry")]
    [InlineData(500, null, 300, 200, "feeMaxTry")]
    [InlineData(500, null, -1, 200, "feeMinTry")]
    public void Pricing_period_ranges_are_validated(int budgetMin, int? budgetMax, int feeMin, int feeMax, string field)
    {
        var exception = Assert.Throws<DomainException>(
            () => AdminPricingService.EnsureValid(Period() with
            {
                BudgetMinTry = budgetMin,
                BudgetMaxTry = budgetMax,
                FeeMinTry = feeMin,
                FeeMaxTry = feeMax,
            }));

        Assert.Equal(field, exception.Field);
    }

    [Fact]
    public void Pricing_period_needs_some_weight_and_sane_percentages()
    {
        Assert.Throws<DomainException>(() => AdminPricingService.EnsureValid(Period() with { DailyDecayPct = 101 }));
        Assert.Throws<DomainException>(() => AdminPricingService.EnsureValid(Period() with
        {
            QualityWeights = Period().QualityWeights with { Description = 0, Title = 0, Budget = 0, Location = 0, SessionBoth = 0, Specialization = 0 },
        }));
        AdminPricingService.EnsureValid(Period());
    }

    private static CreatePricingPeriodDto Period()
        => new(
            null,
            500,
            null,
            250,
            1000,
            10,
            10,
            PsikoAl.Services.Pricing.QualityWeights.Default.ToDto(),
            null);

    private static ListingClaim Claim(DateTimeOffset createdAt, bool resolved = false)
        => new()
        {
            Id = Guid.NewGuid(),
            ListingId = Guid.NewGuid(),
            ExpertId = Guid.NewGuid(),
            CreatedAt = createdAt,
            ResolvedAt = resolved ? createdAt.AddDays(1) : null,
        };
}
