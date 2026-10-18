using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PsikoAl.Services.Options;
using PsikoAl.Services.Payments;

namespace PsikoAl.Services.Tests.Wallet;

// Beklenen değerler BAĞIMSIZ bir uygulamayla (Node.js crypto, 2026-10-14) üretildi:
//   createHmac("sha256", key).update(hashStr, "utf8").digest("base64")
// Tanım: https://dev.paytr.com/iframe-api/iframe-api-1-adim ve .../iframe-api-2-adim.
// Bir alanın sırası ya da salt'ın yeri kayarsa PayTR token'ı reddeder ve bildirimleri
// "bad hash" ile düşürürüz; bu testler o kaymayı yakalar.
public sealed class PayTrHashTests
{
    private const string MerchantKey = "TESTKEY123";
    private const string MerchantSalt = "TESTSALT456";

    // [["Başlangıç (20 kredi)","500.00",1]] — UTF-8 JSON, base64.
    private const string Basket = "W1siQmHFn2xhbmfEscOnICgyMCBrcmVkaSkiLCI1MDAuMDAiLDFdXQ==";

    private static readonly PayTrTokenFields Fields = new(
        "123456",
        "85.105.10.20",
        "PA0123ABC",
        "uzman@example.test",
        "50000",
        Basket,
        "1",
        "0",
        "TL",
        "1");

    [Fact]
    public void Iframe_token_matches_independent_implementation()
        => Assert.Equal(
            "JQM7ORlftRn6lEjLRKb2/vg0IfosLpEL528/otxNjzI=",
            PayTrHash.IframeToken(Fields, MerchantSalt, MerchantKey));

    [Fact]
    public void Iframe_token_hash_input_follows_documented_field_order()
        => Assert.Equal(
            "12345685.105.10.20PA0123ABCuzman@example.test50000" + Basket + "10TL1" + MerchantSalt,
            Fields.HashInput(MerchantSalt));

    [Fact]
    public void Callback_hash_matches_independent_implementation()
        => Assert.Equal(
            "fixaVWhuRQNOcM8SKPqF1+TpPE5Nl0pbba+2HfD2PbQ=",
            PayTrHash.CallbackHash("PA0123ABC", "success", "50000", MerchantSalt, MerchantKey));

    [Fact]
    public void Callback_with_correct_hash_is_valid()
        => Assert.True(PayTrHash.IsValidCallback(
            "PA0123ABC", "success", "50000", "fixaVWhuRQNOcM8SKPqF1+TpPE5Nl0pbba+2HfD2PbQ=", MerchantSalt, MerchantKey));

    [Theory]
    [InlineData("PA0123ABC", "success", "50001")]
    [InlineData("PA0123ABC", "failed", "50000")]
    [InlineData("PA0123ABD", "success", "50000")]
    public void Callback_with_tampered_field_is_rejected(string merchantOid, string status, string totalAmount)
        => Assert.False(PayTrHash.IsValidCallback(
            merchantOid, status, totalAmount, "fixaVWhuRQNOcM8SKPqF1+TpPE5Nl0pbba+2HfD2PbQ=", MerchantSalt, MerchantKey));

    [Fact]
    public void Callback_signed_with_another_key_is_rejected()
    {
        var forged = PayTrHash.CallbackHash("PA0123ABC", "success", "50000", MerchantSalt, "BASKA-ANAHTAR");

        Assert.False(PayTrHash.IsValidCallback("PA0123ABC", "success", "50000", forged, MerchantSalt, MerchantKey));
    }

    [Fact]
    public void Get_token_form_carries_a_token_computed_over_its_own_values()
    {
        var options = new PayTrOptions
        {
            MerchantId = "123456",
            MerchantKey = MerchantKey,
            MerchantSalt = MerchantSalt,
            TestMode = true,
            OkUrl = "https://psikoal.test/kredi/basarili",
            FailUrl = "https://psikoal.test/kredi/hata",
        };
        var request = new PayTrTokenRequest(
            "85.105.10.20",
            "PA0123ABC",
            "uzman@example.test",
            50000,
            [new PayTrBasketItem("Başlangıç (20 kredi)", 500m, 1)],
            "Ayşe Yılmaz",
            "İstanbul",
            "05551112233");

        var form = PayTrClient.BuildForm(request, options);

        var hashInput = string.Concat(
            form["merchant_id"], form["user_ip"], form["merchant_oid"], form["email"], form["payment_amount"],
            form["user_basket"], form["no_installment"], form["max_installment"], form["currency"], form["test_mode"],
            MerchantSalt);
        var expected = Convert.ToBase64String(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(MerchantKey), Encoding.UTF8.GetBytes(hashInput)));

        Assert.Equal(expected, form["paytr_token"]);
        Assert.Equal("50000", form["payment_amount"]);
        Assert.Equal("TL", form["currency"]);
        Assert.Equal("1", form["test_mode"]);
        Assert.Equal("https://psikoal.test/kredi/basarili", form["merchant_ok_url"]);
        Assert.Equal("https://psikoal.test/kredi/hata", form["merchant_fail_url"]);
        foreach (var field in (string[])["user_name", "user_address", "user_phone", "timeout_limit", "lang", "debug_on"])
        {
            Assert.True(form.ContainsKey(field), $"{field} alanı yok.");
        }
    }

    [Fact]
    public void Basket_is_base64_json_rows_of_name_price_text_and_quantity()
    {
        var encoded = PayTrClient.EncodeBasket([new PayTrBasketItem("Başlangıç (20 kredi)", 500m, 1)]);

        using var json = JsonDocument.Parse(Convert.FromBase64String(encoded));
        var row = Assert.Single(json.RootElement.EnumerateArray()).EnumerateArray().ToList();
        Assert.Equal("Başlangıç (20 kredi)", row[0].GetString());
        Assert.Equal("500.00", row[1].GetString());
        Assert.Equal(1, row[2].GetInt32());
    }
}
