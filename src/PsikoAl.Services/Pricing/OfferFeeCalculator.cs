namespace PsikoAl.Services.Pricing;

/// <summary>İlanın onay anında dondurulan ücret eğrisi (listings.fee_* kolonları).</summary>
public sealed record FeeCurve(long InitialKurus, long MinKurus, decimal DailyDecayPct, decimal PerOfferDecayPct);

/// <summary>REVIZE-2 §1.2 teklif ücreti — tek hesap yeri, saf fonksiyon.</summary>
/// <remarks>
/// <code>
/// başlangıç = fee_min + kalite × (fee_max − fee_min)     (10 TL'ye yuvarlanır)
/// gün       = floor((şimdi − onay) / 24 saat)
/// ücret     = max(fee_min, başlangıç × (1 − günlük%)^gün × (1 − teklif%)^teklif)   (tam TL)
/// </code>
/// Yuvarlama: yarım değerler yukarı (AwayFromZero). Tutarlar kuruş; çıktı her zaman
/// tam TL'nin katıdır. Bir sonraki düşüşün zamanı/tutarı BİLEREK hesaplanmaz ve hiçbir
/// uçta dönmez (müşteri kararı, "kumar sitesi gibi olmasın").
/// </remarks>
public static class OfferFeeCalculator
{
    private const long KurusPerTry = 100;
    private const long InitialRoundingKurus = 10 * KurusPerTry;

    public static long InitialFeeKurus(decimal qualityScore, long feeMinKurus, long feeMaxKurus)
    {
        var quality = Math.Clamp(qualityScore, 0m, 1m);
        var raw = feeMinKurus + (quality * (feeMaxKurus - feeMinKurus));
        var rounded = RoundTo(raw, InitialRoundingKurus);

        // 10 TL'ye yuvarlama, min/max 10'un katı değilse aralığın dışına taşabilir.
        return Math.Clamp(rounded, feeMinKurus, Math.Max(feeMinKurus, feeMaxKurus));
    }

    public static long CurrentFeeKurus(FeeCurve curve, DateTimeOffset approvedAt, DateTimeOffset now, int offerCount)
    {
        var days = ElapsedDays(approvedAt, now);
        var dailyFactor = Factor(curve.DailyDecayPct);
        var offerFactor = Factor(curve.PerOfferDecayPct);

        decimal value = curve.InitialKurus;

        // Döngü decimal üs için (Math.Pow double'dır; ,5 sınırında yuvarlamayı kaydırır).
        // Değer tabana indiğinde daha fazla çarpmanın anlamı yok — sonuç zaten fee_min.
        for (var i = 0; i < days && value > curve.MinKurus; i++)
        {
            value *= dailyFactor;
        }

        for (var i = 0; i < Math.Max(0, offerCount) && value > curve.MinKurus; i++)
        {
            value *= offerFactor;
        }

        return Math.Max(curve.MinKurus, RoundTo(value, KurusPerTry));
    }

    public static int ElapsedDays(DateTimeOffset approvedAt, DateTimeOffset now)
        => now <= approvedAt ? 0 : (int)Math.Floor((now - approvedAt).TotalHours / 24d);

    private static decimal Factor(decimal pct) => 1m - (Math.Clamp(pct, 0m, 100m) / 100m);

    private static long RoundTo(decimal value, long step)
        => (long)(decimal.Round(value / step, 0, MidpointRounding.AwayFromZero) * step);
}
