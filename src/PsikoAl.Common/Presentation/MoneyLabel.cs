using System.Globalization;

namespace PsikoAl.Common.Presentation;

/// <summary>Kuruş tutarlarının ve hak sayılarının Türkçe etiketleri (REVIZE-2 §0, §2.2).</summary>
/// <remarks>
/// Kuruşlu tutar GÖSTERİLMEZ: ücretler zaten tam TL'ye yuvarlanıyor, bakiye de tam TL
/// yüklemelerden ve ücretlerden oluşuyor. Binlik ayıracı tr-TR noktasıdır ("₺1.250").
/// Eksi işareti U+2212 (−): tire (-) değil — mobil ve web aynı karakteri basar.
/// </remarks>
public static class MoneyLabel
{
    public const char MinusSign = '−';
    public const string FreeRightLabel = "Ücretsiz hak";

    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"₺1.250" — negatif tutar "−₺1.250".</summary>
    public static string Try(long kurus)
        => kurus < 0 ? $"{MinusSign}{Unsigned(-kurus)}" : Unsigned(kurus);

    /// <summary>"+₺500" / "−₺625" / "" (sıfır).</summary>
    public static string WithSign(long kurus)
        => kurus switch
        {
            > 0 => $"+{Unsigned(kurus)}",
            < 0 => $"{MinusSign}{Unsigned(-kurus)}",
            _ => string.Empty,
        };

    /// <summary>"+1 hak" / "−1 hak" / "" (sıfır).</summary>
    public static string Rights(int delta)
        => delta switch
        {
            > 0 => $"+{delta.ToString(TurkishCulture)} hak",
            < 0 => $"{MinusSign}{(-delta).ToString(TurkishCulture)} hak",
            _ => string.Empty,
        };

    /// <summary>Teklifte ödenen: "Ücretsiz hak" ya da "₺625".</summary>
    public static string FeePaid(long feePaidKurus, bool usedFreeRight)
        => usedFreeRight ? FreeRightLabel : Try(feePaidKurus);

    public static long FromTry(long amountTry) => amountTry * 100;

    private static string Unsigned(long kurus)
        => $"₺{decimal.Round(kurus / 100m, 0, MidpointRounding.AwayFromZero).ToString("#,##0", TurkishCulture)}";
}
