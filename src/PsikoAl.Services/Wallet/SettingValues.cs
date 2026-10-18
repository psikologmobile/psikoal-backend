using System.Globalization;
using PsikoAl.Common.Constants;
using PsikoAl.Data.Repositories.Abstractions;

namespace PsikoAl.Services.Wallet;

/// <summary>Revize 2 ayarlarının (system_settings) tipli okuması.</summary>
/// <remarks>
/// Ayar eksik ya da bozuksa sözleşmedeki (REVIZE-2 §2.1) başlangıç değeri kullanılır:
/// yanlış bir panel girdisi ödeme ya da teklif akışını 500'e düşürmemeli. Panelde
/// kaydedilen değerler SystemSettingsService'te doğrulanır.
/// </remarks>
public static class SettingValues
{
    public static readonly IReadOnlyList<long> DefaultTopupPresetsTry = [500, 1000, 2500, 5000];
    public const long DefaultTopupMinTry = 250;
    public const long DefaultTopupMaxTry = 50_000;
    public const int DefaultWelcomeFreeRights = 1;
    public const int DefaultReferralFreeRights = 1;
    public const bool DefaultClaimsEnabled = true;
    public const int DefaultClaimAdminReviewDays = 3;
    public const string DefaultVatNote = "KDV dahil";

    public static async Task<long> GetLongAsync(
        ISystemSettingRepository settings,
        string key,
        long fallback,
        CancellationToken cancellationToken)
        => TryParseNonNegativeLong((await settings.GetAsync(key, cancellationToken))?.Value, out var value) ? value : fallback;

    public static async Task<int> GetIntAsync(
        ISystemSettingRepository settings,
        string key,
        int fallback,
        CancellationToken cancellationToken)
        => TryParseNonNegativeLong((await settings.GetAsync(key, cancellationToken))?.Value, out var value) && value <= int.MaxValue
            ? (int)value
            : fallback;

    public static async Task<bool> GetBoolAsync(
        ISystemSettingRepository settings,
        string key,
        bool fallback,
        CancellationToken cancellationToken)
        => bool.TryParse((await settings.GetAsync(key, cancellationToken))?.Value, out var value) ? value : fallback;

    public static async Task<string> GetStringAsync(
        ISystemSettingRepository settings,
        string key,
        string fallback,
        CancellationToken cancellationToken)
        => (await settings.GetAsync(key, cancellationToken))?.Value is { Length: > 0 } value ? value : fallback;

    public static async Task<IReadOnlyList<long>> GetTopupPresetsTryAsync(
        ISystemSettingRepository settings,
        CancellationToken cancellationToken)
        => TryParsePresets((await settings.GetAsync(SystemSettingKeys.TopupPresetsTry, cancellationToken))?.Value, out var presets)
            ? presets
            : DefaultTopupPresetsTry;

    public static bool TryParseNonNegativeLong(string? value, out long result)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    // "500,1000,2500,5000": en az bir pozitif tam sayı, virgülle.
    public static bool TryParsePresets(string? value, out IReadOnlyList<long> presets)
    {
        presets = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parsed = new List<long>();
        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!TryParseNonNegativeLong(part, out var amount) || amount <= 0)
            {
                return false;
            }

            parsed.Add(amount);
        }

        presets = parsed;
        return parsed.Count > 0;
    }
}
