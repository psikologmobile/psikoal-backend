using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PsikoAl.Api.E2ETests;

/// <summary>
/// Eşleşti işaretleme ve "danışanla anlaştım" zinciri (REVIZE-2 §3.3–§3.5, §5, §6, §9).
/// </summary>
/// <remarks>
/// mark_listing_matched() Postgres fonksiyonu tek transaction'da match + ilan MATCHED +
/// seçilen teklif ACCEPTED + diğer PENDING teklifler REJECTED/OTHER_EXPERT yazar; birim
/// testleri bunu göremez, burada uçtan uca ölçülür.
/// </remarks>
[Trait("Category", "E2E")]
[Collection(E2ECollection.Name)]
public sealed class MatchClaimChainTests(E2EFixture fixture) : Revize2ChainBase(fixture)
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await SetWalletAsync(ExpertA, 0, 5);
        await SetWalletAsync(ExpertB, 0, 5);
    }

    [Fact]
    public async Task Danışan_ilanı_eşleşti_işaretler_diğer_teklifler_kapanır_ve_iki_taraf_bilgilendirilir()
    {
        var listingId = await CreateOpenListingAsync();
        var offerA = (await OfferAtCurrentFeeAsync(ExpertA, listingId)).GetProperty("id").GetGuid();
        var offerB = (await OfferAtCurrentFeeAsync(ExpertB, listingId)).GetProperty("id").GetGuid();

        var marked = await Fixture.Client.PostAsJsonAsync($"/listings/{listingId}/match", new { offerId = offerA });

        var matchId = (await DataAsync(marked)).GetProperty("matchId").GetGuid();

        var chosen = await (await AsAsync(ExpertA, HttpMethod.Get, $"/offers/{offerA}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ACCEPTED", chosen.GetProperty("status").GetString());
        Assert.Equal("Seçildi", chosen.GetProperty("statusLabel").GetString());
        Assert.Equal(matchId, chosen.GetProperty("matchId").GetGuid());

        var other = await (await AsAsync(ExpertB, HttpMethod.Get, $"/offers/{offerB}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("REJECTED", other.GetProperty("status").GetString());
        Assert.Equal("OTHER_EXPERT", other.GetProperty("closedReason").GetString());
        Assert.Equal("Başka uzmanla eşleşti", other.GetProperty("statusLabel").GetString());

        var listingForB = await (await AsAsync(ExpertB, HttpMethod.Get, $"/listings/{listingId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MATCHED", listingForB.GetProperty("status").GetString());
        Assert.True(listingForB.GetProperty("matchedWithOther").GetBoolean());

        var listingForClient = await Fixture.Client.GetFromJsonAsync<JsonElement>($"/listings/{listingId}");
        Assert.Equal(ExpertA.AuthUserId, listingForClient.GetProperty("matchedExpert").GetProperty("id").GetGuid());

        var matched = (await NotificationsOfAsync(ExpertA)).First(row => row.GetProperty("type").GetString() == "MATCHED");
        Assert.Equal(matchId.ToString(), matched.GetProperty("entityId").GetString());
        var matchedOther = (await NotificationsOfAsync(ExpertB)).First(row => row.GetProperty("type").GetString() == "LISTING_MATCHED_OTHER");
        Assert.Equal(listingId.ToString(), matchedOther.GetProperty("entityId").GetString());

        // §5: iki taraf da karşı tarafın telefonunu ve e-postasını görür.
        var matchForExpert = await (await AsAsync(ExpertA, HttpMethod.Get, $"/matches/{matchId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ACTIVE", matchForExpert.GetProperty("status").GetString());
        Assert.Equal("05551112233", matchForExpert.GetProperty("client").GetProperty("phone").GetString());
        Assert.Equal(SupabaseTestIdentity.ClientEmail, matchForExpert.GetProperty("client").GetProperty("email").GetString());
        var matchForClient = await Fixture.Client.GetFromJsonAsync<JsonElement>($"/matches/{matchId}");
        Assert.Equal("05550001122", matchForClient.GetProperty("expert").GetProperty("phone").GetString());
        Assert.Equal(ExpertA.Email, matchForClient.GetProperty("expert").GetProperty("email").GetString());

        // §5: değerlendirme eşleşme ACTIVE iken açık.
        var review = await Fixture.Client.PostAsJsonAsync(
            $"/experts/{ExpertA.AuthUserId}/reviews",
            new { matchId, rating = 5, comment = "E2E Revize 2 değerlendirmesi", sessionType = "online" });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);

        // İkinci işaretleme: ilan artık açık değil.
        var again = await Fixture.Client.PostAsJsonAsync($"/listings/{listingId}/match", new { offerId = offerB });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("LISTING_NOT_OPEN", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Başka_ilanın_teklifiyle_işaretleme_422_OFFER_NOT_ON_LISTING()
    {
        var listingId = await CreateOpenListingAsync();
        var otherListingId = await CreateOpenListingAsync();
        var foreignOffer = (await OfferAtCurrentFeeAsync(ExpertA, otherListingId)).GetProperty("id").GetGuid();

        var response = await Fixture.Client.PostAsJsonAsync($"/listings/{listingId}/match", new { offerId = foreignOffer });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("OFFER_NOT_ON_LISTING", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal("OPEN", (await Fixture.Client.GetFromJsonAsync<JsonElement>($"/listings/{listingId}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task İlan_sahibi_olmayan_işaretleyemez_403_ve_olmayan_ilan_404()
    {
        var listingId = await CreateOpenListingAsync();
        var offer = (await OfferAtCurrentFeeAsync(ExpertA, listingId)).GetProperty("id").GetGuid();

        var notOwner = await AsAsync(ExpertA, HttpMethod.Post, $"/listings/{listingId}/match", new { offerId = offer });
        var missing = await Fixture.Client.PostAsJsonAsync($"/listings/{Guid.NewGuid()}/match", new { offerId = offer });

        Assert.Equal(HttpStatusCode.Forbidden, notOwner.StatusCode);
        Assert.Equal("LISTING_NOT_OWNER", (await notOwner.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Kapatılan_ilanın_bekleyen_teklifleri_LISTING_CLOSED_olur()
    {
        var listingId = await CreateOpenListingAsync();
        var offer = (await OfferAtCurrentFeeAsync(ExpertA, listingId)).GetProperty("id").GetGuid();

        (await Fixture.Client.PostAsync($"/listings/{listingId}/close", null)).EnsureSuccessStatusCode();

        var closed = await (await AsAsync(ExpertA, HttpMethod.Get, $"/offers/{offer}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("REJECTED", closed.GetProperty("status").GetString());
        Assert.Equal("LISTING_CLOSED", closed.GetProperty("closedReason").GetString());
        Assert.Equal("İlan kapandı", closed.GetProperty("statusLabel").GetString());
    }

    // pg_cron'un saatlik işi public.expire_open_listings(): süresi dolan ilanın bekleyen
    // teklifleri REJECTED / LISTING_EXPIRED olur. Cron beklenmez, fonksiyon doğrudan çağrılır.
    [Fact]
    public async Task Süresi_dolan_ilanın_bekleyen_teklifleri_LISTING_EXPIRED_olur()
    {
        var listingId = await CreateOpenListingAsync();
        var offer = (await OfferAtCurrentFeeAsync(ExpertA, listingId)).GetProperty("id").GetGuid();

        await E2ESql.ExecuteAsync(
            """
            update public.listings set expires_at = now() - interval '1 minute' where id = @id;
            select public.expire_open_listings();
            """,
            ("id", listingId));

        var expired = await (await AsAsync(ExpertA, HttpMethod.Get, $"/offers/{offer}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("REJECTED", expired.GetProperty("status").GetString());
        Assert.Equal("LISTING_EXPIRED", expired.GetProperty("closedReason").GetString());
        Assert.Equal("Süresi doldu", expired.GetProperty("statusLabel").GetString());
    }

    [Fact]
    public async Task Uzman_teklif_verince_iletişimi_ve_ücreti_görür_danışan_uzman_profilini_görür()
    {
        var listingId = await CreateOpenListingAsync();

        var before = await (await AsAsync(ExpertA, HttpMethod.Get, $"/listings/{listingId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, before.GetProperty("clientContact").ValueKind);
        Assert.StartsWith("₺", before.GetProperty("offerFeeLabel").GetString(), StringComparison.Ordinal);
        var unrelated = await Fixture.Client.GetAsync($"/experts/{ExpertA.AuthUserId}");
        Assert.Equal(HttpStatusCode.NotFound, unrelated.StatusCode);

        await OfferAtCurrentFeeAsync(ExpertA, listingId);

        var after = await (await AsAsync(ExpertA, HttpMethod.Get, $"/listings/{listingId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(after.GetProperty("viewerHasOffered").GetBoolean());
        Assert.Equal("05551112233", after.GetProperty("clientContact").GetProperty("phone").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Fixture.Client.GetAsync($"/experts/{ExpertA.AuthUserId}")).StatusCode);

        var feed = await DataAsync(await AsAsync(ExpertA, HttpMethod.Get, "/listings?sort=fee_asc&city=İstanbul"));
        var item = feed.EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == listingId);
        Assert.True(item.GetProperty("viewerHasOffered").GetBoolean());
        Assert.Equal(JsonValueKind.Number, item.GetProperty("offerFeeKurus").ValueKind);
    }

    [Fact]
    public async Task Anlaşma_bildirimi_bir_kez_gönderilir_danışan_kapatır_admin_inceler_ve_çözer()
    {
        var listingId = await CreateOpenListingAsync();
        await OfferAtCurrentFeeAsync(ExpertA, listingId);

        var created = await AsAsync(ExpertA, HttpMethod.Post, $"/listings/{listingId}/claims", new { note = "Telefonda anlaştık." });
        var duplicate = await AsAsync(ExpertA, HttpMethod.Post, $"/listings/{listingId}/claims", new { note = "Tekrar" });
        var noOffer = await AsAsync(ExpertB, HttpMethod.Post, $"/listings/{listingId}/claims", new { note = (string?)null });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var claimId = (await DataAsync(created)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("CLAIM_ALREADY_EXISTS", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noOffer.StatusCode);
        Assert.Equal("CLAIM_NOT_ALLOWED", (await noOffer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var expertView = await (await AsAsync(ExpertA, HttpMethod.Get, $"/listings/{listingId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.String, expertView.GetProperty("viewerClaim").GetProperty("createdAt").ValueKind);

        var clientView = await Fixture.Client.GetFromJsonAsync<JsonElement>($"/listings/{listingId}");
        var claim = Assert.Single(clientView.GetProperty("claims").EnumerateArray());
        Assert.Equal(ExpertA.AuthUserId, claim.GetProperty("expertId").GetGuid());

        (await Fixture.Client.PostAsync($"/listings/{listingId}/claims/{ExpertA.AuthUserId}/dismiss", null)).EnsureSuccessStatusCode();
        Assert.Empty((await Fixture.Client.GetFromJsonAsync<JsonElement>($"/listings/{listingId}")).GetProperty("claims").EnumerateArray());

        Assert.Contains(await AdminClaimsAsync("OPEN"), row => row.GetProperty("id").GetGuid() == claimId);

        await E2ESql.ExecuteAsync(
            "update public.listing_claims set created_at = now() - interval '4 days' where id = @id;",
            ("id", claimId));
        var pending = (await AdminClaimsAsync("REVIEW_PENDING")).Single(row => row.GetProperty("id").GetGuid() == claimId);
        Assert.Equal("İnceleme bekliyor", pending.GetProperty("statusLabel").GetString());
        Assert.Equal(JsonValueKind.String, pending.GetProperty("dismissedAt").ValueKind);

        var resolve = await Fixture.Admin.PostAsJsonAsync($"/admin/claims/{claimId}/resolve", new { note = "Danışanla görüşüldü." });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        Assert.Contains(await AdminClaimsAsync("RESOLVED"), row => row.GetProperty("id").GetGuid() == claimId);

        var activity = await Fixture.Admin.GetFromJsonAsync<JsonElement>($"/admin/users/{ExpertA.AuthUserId}/activity?kind=CLAIM,OFFER,WALLET");
        var kinds = activity.GetProperty("data").EnumerateArray().Select(row => row.GetProperty("kind").GetString()).ToHashSet();
        Assert.Contains("CLAIM", kinds);
        Assert.Contains("OFFER", kinds);
        Assert.Contains("WALLET", kinds);
        Assert.True(activity.GetProperty("meta").GetProperty("total").GetInt32() >= 3);
    }

    private async Task<List<JsonElement>> AdminClaimsAsync(string status)
        => [.. (await Fixture.Admin.GetFromJsonAsync<JsonElement>($"/admin/claims?status={status}&perPage=100")).GetProperty("data").EnumerateArray()];
}
