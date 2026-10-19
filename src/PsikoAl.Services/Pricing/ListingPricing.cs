using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;

namespace PsikoAl.Services.Pricing;

/// <summary>İlanın ücret eğrisini dondurma ve anlık ücretini okuma.</summary>
/// <remarks>
/// Eğri ilan admin onayından geçtiği anda (ya da LISTING_AUTO_APPROVE ile oluşturulurken)
/// o anki dönemden kopyalanır. Revize 2'den ÖNCE onaylanmış ilanlarda kopya yoktur; onlar
/// için eğri geçerli dönemden aynı formülle türetilir (yazılmaz — okuma yolu veri değiştirmez).
/// </remarks>
public static class ListingPricing
{
    public const long KurusPerTry = 100;

    public static async Task<PricingPeriod> GetCurrentPeriodAsync(
        IUnitOfWork unitOfWork,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => await unitOfWork.PricingPeriods.GetCurrentAsync(now, cancellationToken)
            ?? throw new InvalidOperationException("Geçerli fiyatlandırma dönemi yok (pricing_periods boş).");

    public static QualityInput InputOf(Listing listing)
        => new(
            listing.Title,
            listing.Description,
            listing.BudgetMax,
            listing.City,
            listing.PreferredSessionType,
            listing.Specialization.Count);

    public static void ApplySnapshot(Listing listing, PricingPeriod period)
    {
        var curve = CurveFor(listing, period, out var qualityScore);
        listing.QualityScore = qualityScore;
        listing.FeeInitialKurus = curve.InitialKurus;
        listing.FeeMinKurus = curve.MinKurus;
        listing.DailyDecayPct = curve.DailyDecayPct;
        listing.PerOfferDecayPct = curve.PerOfferDecayPct;
    }

    public static FeeCurve CurveOf(Listing listing, PricingPeriod fallbackPeriod)
        => listing is { FeeInitialKurus: { } initial, FeeMinKurus: { } min, DailyDecayPct: { } daily, PerOfferDecayPct: { } perOffer }
            ? new FeeCurve(initial, min, daily, perOffer)
            : CurveFor(listing, fallbackPeriod, out _);

    public static long CurrentFeeKurus(Listing listing, PricingPeriod fallbackPeriod, DateTimeOffset now)
        => OfferFeeCalculator.CurrentFeeKurus(
            CurveOf(listing, fallbackPeriod),
            listing.ApprovedAt ?? listing.CreatedAt,
            now,
            listing.OfferCount);

    public static long CurrentFeeKurus(Listing listing, PricingPeriod fallbackPeriod, DateTimeOffset now, int offerCount)
        => OfferFeeCalculator.CurrentFeeKurus(
            CurveOf(listing, fallbackPeriod),
            listing.ApprovedAt ?? listing.CreatedAt,
            now,
            offerCount);

    private static FeeCurve CurveFor(Listing listing, PricingPeriod period, out decimal qualityScore)
    {
        var weights = QualityWeights.Parse(period.QualityWeights);
        qualityScore = QualityScoreCalculator.Calculate(InputOf(listing), weights, period.BudgetMinTry).Score;
        var feeMin = period.FeeMinTry * KurusPerTry;
        var feeMax = period.FeeMaxTry * KurusPerTry;
        return new FeeCurve(
            OfferFeeCalculator.InitialFeeKurus(qualityScore, feeMin, feeMax),
            feeMin,
            period.DailyDecayPct,
            period.PerOfferDecayPct);
    }
}
