namespace PsikoAl.Services.Pricing;

/// <summary>İlanın kalite puanını (0–1) hesaplayan girdiler.</summary>
public sealed record QualityInput(
    string? Title,
    string? Description,
    decimal BudgetMax,
    string? City,
    string? SessionType,
    int SpecializationCount);

/// <summary>Bileşen puanları (her biri 0–1) ve ağırlıklı toplam.</summary>
public sealed record QualityResult(
    decimal Score,
    decimal Description,
    decimal Title,
    decimal Budget,
    decimal Location,
    decimal SessionBoth,
    decimal Specialization);

/// <summary>REVIZE-2 §1.1 kalite puanı. Saf fonksiyon: aynı girdi her zaman aynı puanı verir.</summary>
/// <remarks>
/// Tablodaki tam puan koşulları:
///   açıklama ≥ DescriptionFullChars (doğrusal 0 → eşik)
///   başlık ≥ TitleFullChars (doğrusal TitleMin(10) → eşik)
///   budgetMax ≥ BudgetFullMultiplier × budget_min (doğrusal budget_min → çarpan × budget_min)
///   şehir girilmiş (0/1) · görüşme şekli yüz_yüze_online (0/1) · ≥ 2 alan (1 alan = 0.5)
/// Toplam, ağırlıkların toplamına bölünerek 0–1'e normalize edilir ve 3 ondalığa
/// yuvarlanır: ilana yazılan değer (numeric(4,3)) ile ücretin hesaplandığı değer aynı olsun.
/// </remarks>
public static class QualityScoreCalculator
{
    public const int TitleMin = 10;
    public const string SessionBoth = "yüz_yüze_online";

    public static QualityResult Calculate(QualityInput input, QualityWeights weights, int budgetMinTry)
    {
        var description = Linear(Length(input.Description), 0, weights.DescriptionFullChars);
        var title = Linear(Length(input.Title), TitleMin, weights.TitleFullChars);
        var budget = Linear(input.BudgetMax, budgetMinTry, budgetMinTry * weights.BudgetFullMultiplier);
        var location = string.IsNullOrWhiteSpace(input.City) ? 0m : 1m;
        var sessionBoth = input.SessionType == SessionBoth ? 1m : 0m;
        var specialization = input.SpecializationCount switch
        {
            >= 2 => 1m,
            1 => 0.5m,
            _ => 0m,
        };

        var weightSum = weights.WeightSum;
        var score = weightSum <= 0
            ? 0m
            : ((weights.Description * description)
               + (weights.Title * title)
               + (weights.Budget * budget)
               + (weights.Location * location)
               + (weights.SessionBoth * sessionBoth)
               + (weights.Specialization * specialization)) / weightSum;

        return new QualityResult(
            decimal.Round(Math.Clamp(score, 0m, 1m), 3, MidpointRounding.AwayFromZero),
            description,
            title,
            budget,
            location,
            sessionBoth,
            specialization);
    }

    // Eşik başlangıçtan küçük/eşitse kısmi aralık yoktur: eşiğe ulaşan tam puan alır.
    private static decimal Linear(decimal value, decimal start, decimal full)
    {
        if (full <= start)
        {
            return value >= full ? 1m : 0m;
        }

        return Math.Clamp((value - start) / (full - start), 0m, 1m);
    }

    private static int Length(string? text) => text?.Trim().Length ?? 0;
}
