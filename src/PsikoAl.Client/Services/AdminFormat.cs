using System.Globalization;

namespace PsikoAl.Client.Services;

/// <summary>Panelde tarih/saat ve para gösterimi tek yerde.</summary>
/// <remarks>
/// Saatler HER ZAMAN Europe/Istanbul'da gösterilir: sunucunun yerel saat dilimi (Render'da
/// UTC) admin için anlamsızdır. Para etiketleri API'den hazır gelir (*Label); burada yalnız
/// formdaki TL girdisinin kuruşa çevrimi var.
/// </remarks>
public static class AdminFormat
{
    public const string DateTimeInputFormat = "yyyy-MM-ddTHH:mm";
    public const string DateInputFormat = "yyyy-MM-dd";

    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public static DateTimeOffset ToIstanbul(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, Istanbul);

    /// <summary>"19.10.2026 14:05"</summary>
    public static string DateTime(DateTimeOffset value) => ToIstanbul(value).ToString("dd.MM.yyyy HH:mm", Turkish);

    /// <summary>"19.10.2026 14:05:09" — işlem kaydında aynı dakikadaki olaylar ayrışsın.</summary>
    public static string DateTimeWithSeconds(DateTimeOffset value) => ToIstanbul(value).ToString("dd.MM.yyyy HH:mm:ss", Turkish);

    public static string DateTime(DateTimeOffset? value) => value is { } at ? DateTime(at) : "—";

    /// <summary>datetime-local girdisini ("2026-10-19T14:05") İstanbul saati olarak yorumlar.</summary>
    public static DateTimeOffset? ParseLocalDateTime(string? value)
        => System.DateTime.TryParseExact(value, DateTimeInputFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? new DateTimeOffset(local, Istanbul.GetUtcOffset(local))
            : null;

    /// <summary>date girdisini İstanbul gününün başı (ya da <paramref name="endOfDay"/> ise sonu) yapar.</summary>
    public static DateTimeOffset? ParseLocalDate(string? value, bool endOfDay = false)
    {
        if (!System.DateTime.TryParseExact(value, DateInputFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return null;
        }

        var local = endOfDay ? day.AddDays(1).AddTicks(-1) : day;
        return new DateTimeOffset(local, Istanbul.GetUtcOffset(local));
    }

    /// <summary>"1.250" ya da "1250,50" gibi TL girdisini kuruşa çevirir (işaret kabul edilir).</summary>
    public static long? ParseTryToKurus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace('−', '-').Replace("₺", string.Empty, StringComparison.Ordinal).Trim();
        return decimal.TryParse(normalized, NumberStyles.Number, Turkish, out var amount)
            && decimal.Round(amount, 2) == amount
            ? (long)(amount * 100)
            : null;
    }

    public static string Percent(decimal value) => $"%{value.ToString("0.##", Turkish)}";
}
