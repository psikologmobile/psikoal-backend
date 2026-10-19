using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Options;

namespace PsikoAl.Services.Payments;

// PayTR iFrame API 1. adım: sunucudan get-token çağrısı.
// Belge: https://dev.paytr.com/iframe-api/iframe-api-1-adim
// Taksit kapalı (no_installment=1): bakiye yüklemeleri küçük tutarlı, vade farkı istenmiyor.
public sealed class PayTrClient(
    HttpClient httpClient,
    IOptions<PayTrOptions> options,
    ILogger<PayTrClient> logger) : IPayTrClient
{
    public const string GetTokenPath = "odeme/api/get-token";
    public const string Currency = "TL";
    public const string NoInstallment = "1";
    public const string MaxInstallment = "0";

    private const int UserNameMaxLength = 60;
    private const int UserAddressMaxLength = 400;
    private const int UserPhoneMaxLength = 20;

    public async Task<PayTrTokenResult> GetIframeTokenAsync(PayTrTokenRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            return PayTrTokenResult.Failure("PayTR yapılandırılmamış.");
        }

        var form = BuildForm(request, settings);

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await httpClient.PostAsync(GetTokenPath, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("PayTR get-token HTTP {StatusCode} döndü (oid: {MerchantOid})", (int)response.StatusCode, request.MerchantOid);
                return PayTrTokenResult.Failure($"HTTP {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadFromJsonAsync<GetTokenResponse>(cancellationToken);
            if (body is { Status: "success", Token: { Length: > 0 } token })
            {
                return PayTrTokenResult.Success(token);
            }

            logger.LogWarning("PayTR token vermedi (oid: {MerchantOid}): {Reason}", request.MerchantOid, body?.Reason);
            return PayTrTokenResult.Failure(body?.Reason ?? "Bilinmeyen yanıt");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning(exception, "PayTR get-token çağrısı başarısız (oid: {MerchantOid})", request.MerchantOid);
            return PayTrTokenResult.Failure(exception.GetType().Name);
        }
    }

    public static Dictionary<string, string> BuildForm(PayTrTokenRequest request, PayTrOptions settings)
    {
        var fields = new PayTrTokenFields(
            settings.MerchantId ?? string.Empty,
            request.UserIp,
            request.MerchantOid,
            request.Email,
            request.PaymentAmountKurus.ToString(CultureInfo.InvariantCulture),
            EncodeBasket(request.Basket),
            NoInstallment,
            MaxInstallment,
            Currency,
            settings.TestMode ? "1" : "0");

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["merchant_id"] = fields.MerchantId,
            ["user_ip"] = fields.UserIp,
            ["merchant_oid"] = fields.MerchantOid,
            ["email"] = fields.Email,
            ["payment_amount"] = fields.PaymentAmount,
            ["paytr_token"] = PayTrHash.IframeToken(fields, settings.MerchantSalt ?? string.Empty, settings.MerchantKey ?? string.Empty),
            ["user_basket"] = fields.UserBasket,
            ["debug_on"] = fields.TestMode,
            ["no_installment"] = fields.NoInstallment,
            ["max_installment"] = fields.MaxInstallment,
            ["user_name"] = Truncate(request.UserName, UserNameMaxLength),
            ["user_address"] = Truncate(request.UserAddress, UserAddressMaxLength),
            ["user_phone"] = Truncate(request.UserPhone, UserPhoneMaxLength),
            ["merchant_ok_url"] = settings.OkUrl ?? string.Empty,
            ["merchant_fail_url"] = settings.FailUrl ?? string.Empty,
            ["timeout_limit"] = settings.TimeoutLimitMinutes.ToString(CultureInfo.InvariantCulture),
            ["currency"] = fields.Currency,
            ["test_mode"] = fields.TestMode,
            ["lang"] = "tr",
        };
    }

    // [[ad, birim fiyat (metin, "500.00"), adet]] → JSON → base64.
    public static string EncodeBasket(IReadOnlyList<PayTrBasketItem> basket)
    {
        var rows = basket
            .Select(item => new object[]
            {
                item.Name,
                item.UnitPriceTry.ToString("0.00", CultureInfo.InvariantCulture),
                item.Quantity,
            })
            .ToArray();

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows)));
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record GetTokenResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("token")] string? Token,
        [property: JsonPropertyName("reason")] string? Reason);
}
