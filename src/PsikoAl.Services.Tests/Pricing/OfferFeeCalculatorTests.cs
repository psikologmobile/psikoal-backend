using PsikoAl.Services.Pricing;

namespace PsikoAl.Services.Tests.Pricing;

// Sözleşme: docs/REVIZE-2.md §1.2. Başlangıç dönemi: fee_min 250 TL, fee_max 1000 TL,
// günlük %10, teklif başına %10. Başlangıç ücreti 10 TL'ye, anlık ücret tam TL'ye yuvarlanır
// (yarım yukarı); ücret hiçbir zaman fee_min'in altına inmez.
public sealed class OfferFeeCalculatorTests
{
    private static readonly DateTimeOffset ApprovedAt = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly FeeCurve Curve = new(InitialKurus: 100_000, MinKurus: 25_000, DailyDecayPct: 10, PerOfferDecayPct: 10);

    [Theory]
    [InlineData("0", 25_000)]
    [InlineData("1", 100_000)]
    [InlineData("0.5", 63_000)]   // 625 TL → 10 TL'ye yuvarlanınca 630 TL
    [InlineData("0.333", 50_000)] // 499,75 TL → 500 TL
    [InlineData("0.1", 33_000)]   // 325 TL → 330 TL (yarım yukarı)
    public void Initial_fee_is_linear_in_quality_and_rounded_to_ten_lira(string quality, long expectedKurus)
        => Assert.Equal(
            expectedKurus,
            OfferFeeCalculator.InitialFeeKurus(decimal.Parse(quality, System.Globalization.CultureInfo.InvariantCulture), 25_000, 100_000));

    [Theory]
    [InlineData("-0.5", 25_000)]
    [InlineData("1.7", 100_000)]
    public void Initial_fee_clamps_an_out_of_range_quality(string quality, long expectedKurus)
        => Assert.Equal(
            expectedKurus,
            OfferFeeCalculator.InitialFeeKurus(decimal.Parse(quality, System.Globalization.CultureInfo.InvariantCulture), 25_000, 100_000));

    // fee_min 10 TL'nin katı değilse yuvarlama tabanın altına inemez.
    [Fact]
    public void Initial_fee_never_rounds_below_the_floor()
        => Assert.Equal(25_050, OfferFeeCalculator.InitialFeeKurus(0m, 25_050, 100_000));

    [Fact]
    public void Initial_fee_never_rounds_above_the_ceiling()
        => Assert.Equal(100_600, OfferFeeCalculator.InitialFeeKurus(1m, 25_000, 100_600));

    [Theory]
    [InlineData(0, 0, 100_000)]
    [InlineData(24, 0, 90_000)]
    [InlineData(48, 0, 81_000)]
    [InlineData(0, 1, 90_000)]
    [InlineData(0, 2, 81_000)]
    [InlineData(24, 1, 81_000)]
    [InlineData(72, 2, 59_000)] // 1000 × 0,9³ × 0,9² = 590,49 → 590 TL
    public void Current_fee_decays_per_full_day_and_per_offer(int hours, int offers, long expectedKurus)
        => Assert.Equal(expectedKurus, OfferFeeCalculator.CurrentFeeKurus(Curve, ApprovedAt, ApprovedAt.AddHours(hours), offers));

    [Fact]
    public void Only_full_24_hour_periods_count()
    {
        Assert.Equal(100_000, OfferFeeCalculator.CurrentFeeKurus(Curve, ApprovedAt, ApprovedAt.AddHours(23).AddMinutes(59), 0));
        Assert.Equal(0, OfferFeeCalculator.ElapsedDays(ApprovedAt, ApprovedAt.AddHours(23.99)));
        Assert.Equal(1, OfferFeeCalculator.ElapsedDays(ApprovedAt, ApprovedAt.AddHours(24)));
        Assert.Equal(2, OfferFeeCalculator.ElapsedDays(ApprovedAt, ApprovedAt.AddHours(71.9)));
    }

    [Fact]
    public void Current_fee_is_rounded_to_whole_lira()
    {
        var curve = Curve with { InitialKurus = 63_000 };

        Assert.Equal(56_700, OfferFeeCalculator.CurrentFeeKurus(curve, ApprovedAt, ApprovedAt.AddDays(1), 0));
        Assert.Equal(51_000, OfferFeeCalculator.CurrentFeeKurus(curve, ApprovedAt, ApprovedAt.AddDays(1), 1)); // 510,30 → 510
        Assert.Equal(45_900, OfferFeeCalculator.CurrentFeeKurus(curve, ApprovedAt, ApprovedAt.AddDays(3), 0)); // 459,27 → 459
    }

    [Fact]
    public void Half_lira_rounds_up()
        => Assert.Equal(10_100, OfferFeeCalculator.CurrentFeeKurus(new FeeCurve(10_050, 0, 0, 0), ApprovedAt, ApprovedAt, 0));

    [Theory]
    [InlineData(30, 0)]
    [InlineData(0, 50)]
    [InlineData(400, 400)]
    public void Current_fee_never_goes_below_the_floor(int days, int offers)
        => Assert.Equal(25_000, OfferFeeCalculator.CurrentFeeKurus(Curve, ApprovedAt, ApprovedAt.AddDays(days), offers));

    [Fact]
    public void Zero_decay_keeps_the_initial_fee()
        => Assert.Equal(
            100_000,
            OfferFeeCalculator.CurrentFeeKurus(Curve with { DailyDecayPct = 0, PerOfferDecayPct = 0 }, ApprovedAt, ApprovedAt.AddDays(20), 9));

    [Fact]
    public void Full_decay_drops_straight_to_the_floor()
        => Assert.Equal(
            25_000,
            OfferFeeCalculator.CurrentFeeKurus(Curve with { PerOfferDecayPct = 100 }, ApprovedAt, ApprovedAt, 1));

    [Fact]
    public void Clock_before_approval_counts_as_day_zero()
        => Assert.Equal(100_000, OfferFeeCalculator.CurrentFeeKurus(Curve, ApprovedAt, ApprovedAt.AddDays(-3), 0));

    [Fact]
    public void Result_is_always_a_whole_lira_amount()
    {
        var curve = new FeeCurve(77_700, 12_345, 7.5m, 3.3m);
        for (var day = 0; day < 40; day++)
        {
            for (var offers = 0; offers < 15; offers++)
            {
                var fee = OfferFeeCalculator.CurrentFeeKurus(curve, ApprovedAt, ApprovedAt.AddDays(day), offers);
                Assert.True(fee == 12_345 || fee % 100 == 0, $"gün {day}, teklif {offers}: {fee}");
                Assert.InRange(fee, 12_345, 77_700);
            }
        }
    }
}
