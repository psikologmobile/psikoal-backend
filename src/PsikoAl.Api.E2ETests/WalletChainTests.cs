using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PsikoAl.Services.Payments;

namespace PsikoAl.Api.E2ETests;

/// <summary>
/// TL cüzdan zinciri (REVIZE-2 §1–§2, §7): hediye/hak → teklif ücreti düşer → defter ve
/// bakiye uçlarda görünür; PayTR bildirimi bakiyeyi bir kez yükler; fatura; onay ödülleri.
/// </summary>
/// <remarks>
/// Birim testleri servis sözleşmesini ölçer; burada ölçülen VERİTABANI yarısıdır: koşullu
/// UPDATE'in satır kilidi altında eşzamanlı iki teklifi sıraya sokması, ilan kilidiyle
/// ücretin teklif sayısına bağlanması, CHECK (balance_kurus >= 0), benzersiz ödül indeksleri.
/// get-token ucu bilerek çağrılmaz (gerçek PayTR'ye gider); ödeme satırı SQL ile PENDING
/// açılır ve bildirim, E2EFactory'deki sahte mağaza anahtarıyla imzalanır.
/// </remarks>
[Trait("Category", "E2E")]
[Collection(E2ECollection.Name)]
public sealed class WalletChainTests(E2EFixture fixture) : Revize2ChainBase(fixture)
{
    private readonly List<Guid> _paymentIds = [];

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        foreach (var paymentId in _paymentIds)
        {
            await E2ESql.ExecuteAsync(
                """
                delete from public.wallet_transactions where payment_id = @id;
                delete from public.wallet_payments where id = @id;
                """,
                ("id", paymentId));
        }
    }

    [Fact]
    public async Task Hediye_bakiyesiyle_gönderilen_teklif_anlık_ücreti_düşer_ve_defterde_görünür()
    {
        await AdminGiftAsync(ExpertA, amountKurus: 200_000, freeRights: 0);
        var listingId = await CreateOpenListingAsync();

        var quote = await QuoteAsync(ExpertA, listingId);
        var fee = quote.GetProperty("feeKurus").GetInt64();
        Assert.InRange(fee, 25_000, 100_000);
        Assert.Equal(0, fee % 100);
        Assert.False(quote.GetProperty("willUseFreeRight").GetBoolean());
        Assert.True(quote.GetProperty("sufficient").GetBoolean());

        var offer = await OfferAtCurrentFeeAsync(ExpertA, listingId);

        Assert.Equal(fee, offer.GetProperty("feePaidKurus").GetInt64());
        Assert.False(offer.GetProperty("usedFreeRight").GetBoolean());
        Assert.Equal("Gönderildi", offer.GetProperty("statusLabel").GetString());
        var contact = offer.GetProperty("clientContact");
        Assert.Equal("05551112233", contact.GetProperty("phone").GetString());
        Assert.Equal(SupabaseTestIdentity.ClientEmail, contact.GetProperty("email").GetString());

        var wallet = await WalletOfAsync(ExpertA);
        Assert.Equal(200_000 - fee, wallet.GetProperty("balanceKurus").GetInt64());

        var latest = (await TransactionsOfAsync(ExpertA)).First();
        Assert.Equal("OFFER_FEE", latest.GetProperty("type").GetString());
        Assert.Equal(-fee, latest.GetProperty("amountKurus").GetInt64());
        Assert.StartsWith("−₺", latest.GetProperty("amountLabel").GetString(), StringComparison.Ordinal);
        Assert.Equal(offer.GetProperty("id").GetGuid(), latest.GetProperty("offerId").GetGuid());
        Assert.Equal(listingId, latest.GetProperty("listingId").GetGuid());
    }

    [Fact]
    public async Task Ücretsiz_hak_bakiyeden_önce_kullanılır()
    {
        await AdminGiftAsync(ExpertA, amountKurus: 0, freeRights: 1);
        var listingId = await CreateOpenListingAsync();
        Assert.True((await QuoteAsync(ExpertA, listingId)).GetProperty("willUseFreeRight").GetBoolean());

        var offer = await OfferAtCurrentFeeAsync(ExpertA, listingId);

        Assert.True(offer.GetProperty("usedFreeRight").GetBoolean());
        Assert.Equal("Ücretsiz hak", offer.GetProperty("feePaidLabel").GetString());
        var wallet = await WalletOfAsync(ExpertA);
        Assert.Equal(0, wallet.GetProperty("freeOfferRights").GetInt32());
        Assert.Equal(0, wallet.GetProperty("balanceKurus").GetInt64());
        var latest = (await TransactionsOfAsync(ExpertA)).First();
        Assert.Equal("OFFER_FREE_RIGHT", latest.GetProperty("type").GetString());
        Assert.Equal("−1 hak", latest.GetProperty("freeRightsLabel").GetString());
    }

    [Fact]
    public async Task Bakiye_yetmezse_422_INSUFFICIENT_BALANCE_ve_teklif_yazılmaz()
    {
        var listingId = await CreateOpenListingAsync();
        var fee = (await QuoteAsync(ExpertA, listingId)).GetProperty("feeKurus").GetInt64();
        await SetWalletAsync(ExpertA, fee - 100, 0);

        var response = await SendOfferAsync(ExpertA, listingId, fee);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INSUFFICIENT_BALANCE", body.GetProperty("code").GetString());
        Assert.Equal(0, await CountOffersAsync(listingId));
        Assert.Equal(fee - 100, (await WalletOfAsync(ExpertA)).GetProperty("balanceKurus").GetInt64());
    }

    [Fact]
    public async Task Onaylanmayan_ücret_409_OFFER_FEE_CHANGED_ile_yeni_ücreti_döner()
    {
        await SetWalletAsync(ExpertA, 500_000, 0);
        var listingId = await CreateOpenListingAsync();
        var fee = (await QuoteAsync(ExpertA, listingId)).GetProperty("feeKurus").GetInt64();

        var response = await SendOfferAsync(ExpertA, listingId, fee + 1_000);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OFFER_FEE_CHANGED", body.GetProperty("code").GetString());
        Assert.Equal(fee, body.GetProperty("meta").GetProperty("feeKurus").GetInt64());
        Assert.StartsWith("₺", body.GetProperty("meta").GetProperty("feeLabel").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, await CountOffersAsync(listingId));
        Assert.Equal(500_000, (await WalletOfAsync(ExpertA)).GetProperty("balanceKurus").GetInt64());
    }

    // §1.2: her yeni teklif (geri çekilse de) ve her tam gün ücreti % düşürür.
    [Fact]
    public async Task Ücret_her_teklifte_ve_her_günde_düşer()
    {
        await SetWalletAsync(ExpertA, 500_000, 0);
        var listingId = await CreateOpenListingAsync();
        var first = (await QuoteAsync(ExpertA, listingId)).GetProperty("feeKurus").GetInt64();

        var offer = await OfferAtCurrentFeeAsync(ExpertA, listingId);
        (await AsAsync(ExpertA, HttpMethod.Post, $"/offers/{offer.GetProperty("id").GetGuid()}/withdraw")).EnsureSuccessStatusCode();
        var afterOffer = (await QuoteAsync(ExpertB, listingId)).GetProperty("feeKurus").GetInt64();

        await E2ESql.ExecuteAsync(
            "update public.listings set approved_at = approved_at - interval '24 hours' where id = @id;",
            ("id", listingId));
        var afterDay = (await QuoteAsync(ExpertB, listingId)).GetProperty("feeKurus").GetInt64();

        Assert.Equal(Math.Max(25_000, RoundLira(first * 0.9m)), afterOffer);
        Assert.Equal(Math.Max(25_000, RoundLira(first * 0.81m)), afterDay);
    }

    [Fact]
    public async Task Tek_ücretlik_bakiye_ile_eşzamanlı_iki_teklif_yalnız_birini_geçirir()
    {
        var first = await CreateOpenListingAsync("E2E eşzamanlı ilan bir aynı içerik");
        var second = await CreateOpenListingAsync("E2E eşzamanlı ilan bir aynı içerik");
        var fee1 = (await QuoteAsync(ExpertA, first)).GetProperty("feeKurus").GetInt64();
        var fee2 = (await QuoteAsync(ExpertA, second)).GetProperty("feeKurus").GetInt64();
        await SetWalletAsync(ExpertA, Math.Max(fee1, fee2), 0);

        var responses = await Task.WhenAll(SendOfferAsync(ExpertA, first, fee1), SendOfferAsync(ExpertA, second, fee2));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.UnprocessableEntity);
        Assert.Equal(1, await CountOffersAsync(first) + await CountOffersAsync(second));
        Assert.True((await WalletOfAsync(ExpertA)).GetProperty("balanceKurus").GetInt64() >= 0);
    }

    [Fact]
    public async Task Admin_düzeltmesi_bakiyeyi_eksiye_düşüremez_ve_gerekçe_ister()
    {
        await AdminGiftAsync(ExpertA, amountKurus: 20_000, freeRights: 0);

        var negative = await Fixture.Admin.PostAsJsonAsync(
            $"/admin/wallet/experts/{ExpertA.AuthUserId}/adjust",
            new { amountKurus = -30_000, freeRightsDelta = 0, reason = "E2E fazla düşüm" });
        var noReason = await Fixture.Admin.PostAsJsonAsync(
            $"/admin/wallet/experts/{ExpertA.AuthUserId}/adjust",
            new { amountKurus = -10_000, freeRightsDelta = 0, reason = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, negative.StatusCode);
        Assert.Equal("WALLET_BALANCE_NEGATIVE", (await negative.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noReason.StatusCode);
        Assert.Equal("WALLET_ADJUST_REASON_REQUIRED", (await noReason.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(20_000, (await WalletOfAsync(ExpertA)).GetProperty("balanceKurus").GetInt64());

        var admin = await Fixture.Admin.GetFromJsonAsync<JsonElement>($"/admin/wallet/experts/{ExpertA.AuthUserId}");
        Assert.Equal("₺200", admin.GetProperty("balanceLabel").GetString());
        Assert.Equal("ADMIN_GIFT", admin.GetProperty("transactions").GetProperty("data")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task PayTR_bildirimi_bakiyeyi_bir_kez_yükler_ve_tekrarına_yine_OK_der()
    {
        var merchantOid = await InsertPaymentAsync(50_000, "PENDING");

        var first = await PostCallbackAsync(merchantOid, "success", "50000");
        var repeat = await PostCallbackAsync(merchantOid, "success", "50000");

        Assert.Equal("OK", await first.Content.ReadAsStringAsync());
        Assert.Equal("OK", await repeat.Content.ReadAsStringAsync());
        Assert.Equal(50_000, (await WalletOfAsync(ExpertA)).GetProperty("balanceKurus").GetInt64());
        var topup = (await TransactionsOfAsync(ExpertA)).First();
        Assert.Equal("TOPUP", topup.GetProperty("type").GetString());
        Assert.Equal("+₺500", topup.GetProperty("amountLabel").GetString());
        Assert.Contains(await NotificationsOfAsync(ExpertA), row => row.GetProperty("type").GetString() == "BALANCE_TOPUP");

        var payments = await DataAsync(await AsAsync(ExpertA, HttpMethod.Get, "/wallet/payments"));
        var payment = payments.EnumerateArray().First();
        Assert.Equal("PAID", payment.GetProperty("status").GetString());
        Assert.Equal("Ödendi", payment.GetProperty("statusLabel").GetString());
        Assert.Equal(JsonValueKind.Null, payment.GetProperty("invoice").ValueKind);
    }

    [Fact]
    public async Task Hash_i_bozuk_bildirim_reddedilir_ve_bakiye_yüklemez()
    {
        var merchantOid = await InsertPaymentAsync(30_000, "PENDING");

        var response = await PostCallbackAsync(merchantOid, "success", "30000", hashOverride: "c2FodGVoYXNo");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await WalletOfAsync(ExpertA)).GetProperty("balanceKurus").GetInt64());
    }

    [Fact]
    public async Task Başarısız_ödeme_bildirimi_bakiye_yüklemez_ve_admin_listesinde_görünür()
    {
        var merchantOid = await InsertPaymentAsync(30_000, "PENDING");

        var response = await PostCallbackAsync(merchantOid, "failed", "30000");

        Assert.Equal("OK", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, (await WalletOfAsync(ExpertA)).GetProperty("balanceKurus").GetInt64());
        var list = await Fixture.Admin.GetFromJsonAsync<JsonElement>("/admin/wallet/payments?status=FAILED&perPage=100");
        Assert.Contains(list.GetProperty("data").EnumerateArray(), row => row.GetProperty("merchantOid").GetString() == merchantOid);
    }

    [Fact]
    public async Task Aralık_dışı_yükleme_422_TOPUP_AMOUNT_OUT_OF_RANGE()
    {
        var response = await AsAsync(ExpertA, HttpMethod.Post, "/payments/paytr/init", new { amountKurus = 10_000 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TOPUP_AMOUNT_OUT_OF_RANGE", body.GetProperty("code").GetString());
        Assert.Equal(25_000, body.GetProperty("meta").GetProperty("minKurus").GetInt64());
    }

    [Fact]
    public async Task Yükleme_seçenekleri_anonim_ve_ayarlardan_gelir()
    {
        var body = await Fixture.Mobile.GetFromJsonAsync<JsonElement>("/wallet/topup-options");

        var data = body.GetProperty("data");
        Assert.Equal(
            [50_000L, 100_000L, 250_000L, 500_000L],
            data.GetProperty("presets").EnumerateArray().Select(preset => preset.GetProperty("amountKurus").GetInt64()));
        Assert.Equal("₺1.000", data.GetProperty("presets")[1].GetProperty("label").GetString());
        Assert.Equal("KDV dahil", data.GetProperty("vatNote").GetString());
    }

    [Fact]
    public async Task Admin_faturayı_yükler_uzman_imzalı_bağlantıyla_indirir()
    {
        var merchantOid = await InsertPaymentAsync(100_000, "PAID");
        var paymentId = _paymentIds[^1];

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n% E2E fatura\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "e2e-fatura.pdf");
        var upload = await Fixture.Admin.PostAsync($"/admin/wallet/payments/{paymentId}/invoice", form);

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var uploaded = await upload.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("e2e-fatura.pdf", uploaded.GetProperty("invoice").GetProperty("fileName").GetString());
        Assert.Contains(await NotificationsOfAsync(ExpertA), row => row.GetProperty("type").GetString() == "INVOICE_READY");

        var invoice = await DataAsync(await AsAsync(ExpertA, HttpMethod.Get, $"/wallet/payments/{paymentId}/invoice"));
        var url = invoice.GetProperty("url").GetString();
        Assert.Contains("/storage/v1/object/sign/invoices/", url, StringComparison.Ordinal);

        using var download = new HttpClient();
        var pdf = await download.GetByteArrayAsync(url);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf), StringComparison.Ordinal);

        var foreign = await AsAsync(ExpertB, HttpMethod.Get, $"/wallet/payments/{paymentId}/invoice");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.NotNull(merchantOid);
    }

    // §2.3: ilk onayda hoş geldin hakkı; davetle gelen uzman onaylanınca davet edene hak. İkisi de bir kez.
    [Fact]
    public async Task Onay_hoş_geldin_ve_davet_hakkını_bir_kez_yazar()
    {
        await E2ESql.ExecuteAsync(
            """
            update public.experts set status = 'pending' where id = @b;
            update public.profiles set referred_by_expert_id = @a where id = @b;
            """,
            ("a", ExpertA.AuthUserId),
            ("b", ExpertB.AuthUserId));

        (await Fixture.Admin.PostAsync($"/admin/experts/{ExpertB.AuthUserId}/approve", null)).EnsureSuccessStatusCode();
        (await Fixture.Admin.PostAsync($"/admin/experts/{ExpertB.AuthUserId}/approve", null)).EnsureSuccessStatusCode();

        Assert.Equal(1, (await WalletOfAsync(ExpertB)).GetProperty("freeOfferRights").GetInt32());
        Assert.Equal(1, (await WalletOfAsync(ExpertA)).GetProperty("freeOfferRights").GetInt32());
        Assert.Contains(await NotificationsOfAsync(ExpertB), row => row.GetProperty("type").GetString() == "WELCOME_RIGHTS");
        Assert.Contains(await NotificationsOfAsync(ExpertA), row => row.GetProperty("type").GetString() == "REFERRAL_REWARD");

        var referrals = await Fixture.Admin.GetFromJsonAsync<JsonElement>("/admin/referrals?perPage=100");
        var row = referrals.GetProperty("data").EnumerateArray().Single(item => item.GetProperty("referredUserId").GetGuid() == ExpertB.AuthUserId);
        Assert.True(row.GetProperty("rewarded").GetBoolean());
        Assert.Equal(ExpertA.AuthUserId, row.GetProperty("referrerExpertId").GetGuid());
    }

    // §7 + §6: davet koduyla kayıt → davet izi; danışan yalnız davet edeni (ve teklif vereni) görür.
    [Fact]
    public async Task Davet_koduyla_kayıt_olan_danışan_davet_edeni_görür_başkasını_görmez()
    {
        var code = (await WalletOfAsync(ExpertA)).GetProperty("referralCode").GetString()!;
        Assert.Matches("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$", code);
        Assert.EndsWith($"/davet/{code}", (await WalletOfAsync(ExpertA)).GetProperty("referralLink").GetString(), StringComparison.Ordinal);

        var email = $"e2e-rev2-davet-{Guid.NewGuid():N}@psikoal.test";
        var register = await Fixture.Mobile.PostAsJsonAsync("/auth/register", new
        {
            email,
            password = "E2eDavetPass123!",
            firstName = "Davetli",
            lastName = "Danışan",
            role = "client",
            termsAccepted = true,
            healthDataConsent = true,
            referralCode = code.ToLowerInvariant(),
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var token = (await register.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens").GetProperty("accessToken").GetString()!;
        var invited = new AuthenticatedUser(Guid.Empty, email, token);

        var referrer = await DataAsync(await AsAsync(invited, HttpMethod.Get, "/me/referrer"));
        Assert.Equal(ExpertA.AuthUserId, referrer.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await AsAsync(invited, HttpMethod.Get, $"/experts/{ExpertA.AuthUserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AsAsync(invited, HttpMethod.Get, $"/experts/{ExpertB.AuthUserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AsAsync(invited, HttpMethod.Get, $"/experts/{ExpertB.AuthUserId}/reviews")).StatusCode);
    }

    [Fact]
    public async Task Geçersiz_davet_kodu_kaydı_bozmaz()
    {
        var register = await Fixture.Mobile.PostAsJsonAsync("/auth/register", new
        {
            email = $"e2e-rev2-gecersiz-{Guid.NewGuid():N}@psikoal.test",
            password = "E2eDavetPass123!",
            firstName = "Kodsuz",
            lastName = "Danışan",
            role = "client",
            termsAccepted = true,
            healthDataConsent = true,
            referralCode = "YOKBOYLE",
        });

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var token = (await register.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens").GetProperty("accessToken").GetString()!;
        var referrer = await DataAsync(await AsAsync(new AuthenticatedUser(Guid.Empty, string.Empty, token), HttpMethod.Get, "/me/referrer"));
        Assert.Equal(JsonValueKind.Null, referrer.ValueKind);
    }

    // ------------------------------------------------------------------ yardımcılar

    private static long RoundLira(decimal kurus) => (long)(decimal.Round(kurus / 100m, 0, MidpointRounding.AwayFromZero) * 100);

    private async Task AdminGiftAsync(AuthenticatedUser expert, long amountKurus, int freeRights)
    {
        var response = await Fixture.Admin.PostAsJsonAsync(
            $"/admin/wallet/experts/{expert.AuthUserId}/gift",
            new { amountKurus, freeRights, reason = "E2E başlangıç" });
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Hediye başarısız ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }
    }

    private async Task<List<JsonElement>> TransactionsOfAsync(AuthenticatedUser expert)
        => [.. (await DataAsync(await AsAsync(expert, HttpMethod.Get, "/wallet/transactions?perPage=10"))).EnumerateArray()];

    private async Task<string> InsertPaymentAsync(long amountKurus, string status)
    {
        var merchantOid = "PAE2E" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var id = Guid.NewGuid();
        await E2ESql.ExecuteAsync(
            """
            insert into public.wallet_payments (id, expert_id, merchant_oid, amount_kurus, status, completed_at)
            values (@id, @expert, @oid, @amount, @status, case when @status = 'PENDING' then null else now() end);
            """,
            ("id", id),
            ("expert", ExpertA.AuthUserId),
            ("oid", merchantOid),
            ("amount", amountKurus),
            ("status", status));
        _paymentIds.Add(id);
        return merchantOid;
    }

    private Task<HttpResponseMessage> PostCallbackAsync(
        string merchantOid,
        string status,
        string totalAmount,
        string? hashOverride = null)
    {
        var hash = hashOverride ?? PayTrHash.CallbackHash(
            merchantOid, status, totalAmount, E2EFactory.PayTrMerchantSalt, E2EFactory.PayTrMerchantKey);

        return Fixture.Mobile.PostAsync(
            "/payments/paytr/callback",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["merchant_oid"] = merchantOid,
                ["status"] = status,
                ["total_amount"] = totalAmount,
                ["hash"] = hash,
                ["payment_type"] = "card",
                ["currency"] = "TL",
                ["payment_amount"] = totalAmount,
                ["test_mode"] = "1",
            }));
    }

    private static Task<long> CountOffersAsync(Guid listingId)
        => E2ESql.ScalarLongAsync("select count(*) from public.offers where listing_id = @id;", ("id", listingId));
}
