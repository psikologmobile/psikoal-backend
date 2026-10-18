using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PsikoAl.Api.E2ETests;

/// <summary>Revize 2 zincirlerinin ortak kurulumu: iki onaylı uzman, bir kategori, ilan/teklif yardımcıları.</summary>
/// <remarks>
/// Uzman hesapları koşum boyunca paylaşılır (GoTrue login limiti). Her testten önce
/// bakiye ve hak SIFIRLANIR ve bu iki uzmanın defter satırları silinir: defter
/// append-only'dir (UPDATE yasak) ama DELETE testin mutlak değer ölçebilmesi için
/// serbest; "uzman başına tek hoş geldin hakkı" gibi benzersiz indeksler de böylece
/// her testte temiz başlar.
/// </remarks>
public abstract class Revize2ChainBase(E2EFixture fixture) : IAsyncLifetime
{
    private const string ExpertAEmail = "e2e-rev2-expert-a@psikoal.test";
    private const string ExpertBEmail = "e2e-rev2-expert-b@psikoal.test";
    private const string ExpertPassword = "E2eRev2ExpertPass123!";

    private static Task<AuthenticatedUser>? _sharedA;
    private static Task<AuthenticatedUser>? _sharedB;
    private static Task<AuthenticatedUser>? _sharedClient;

    private readonly List<Guid> _listingIds = [];

    private protected E2EFixture Fixture { get; } = fixture;

    private protected AuthenticatedUser ExpertA { get; private set; } = null!;

    private protected AuthenticatedUser ExpertB { get; private set; } = null!;

    private protected Guid ClientId { get; private set; }

    private protected string Specialization { get; private set; } = string.Empty;

    public virtual async Task InitializeAsync()
    {
        Specialization = await CreateSpecializationCategoryAsync();
        ExpertA = await (_sharedA ??= SupabaseTestIdentity.EnsureUserAsync(ExpertAEmail, ExpertPassword, CancellationToken.None));
        ExpertB = await (_sharedB ??= SupabaseTestIdentity.EnsureUserAsync(ExpertBEmail, ExpertPassword, CancellationToken.None));
        ClientId = (await (_sharedClient ??= SupabaseTestIdentity.EnsureClientAsync(CancellationToken.None))).AuthUserId;

        foreach (var (expert, lastName) in new[] { (ExpertA, "Birinci"), (ExpertB, "İkinci") })
        {
            await E2ESql.ExecuteAsync(
                """
                update public.profiles
                   set role = 'expert', first_name = 'E2E', last_name = @last, phone = '05550001122', referred_by_expert_id = null
                 where id = @id;
                insert into public.experts (id, title, specializations, status)
                values (@id, 'E2E Klinik Psikolog', array[@spec], 'approved')
                on conflict (id) do update
                  set status = 'approved', specializations = array[@spec], balance_kurus = 0, free_offer_rights = 0;
                update public.experts set balance_kurus = 0, free_offer_rights = 0 where id = @id;
                delete from public.wallet_transactions where expert_id = @id or referred_expert_id = @id;
                delete from public.listing_claims where expert_id = @id;
                """,
                ("id", expert.AuthUserId),
                ("last", lastName),
                ("spec", Specialization));
        }

        // Danışanın davet izi testten teste taşınmasın.
        await E2ESql.ExecuteAsync(
            "update public.profiles set referred_by_expert_id = null, phone = '05551112233' where id = @id;",
            ("id", ClientId));
    }

    public virtual async Task DisposeAsync()
    {
        foreach (var listingId in _listingIds)
        {
            await E2ESql.ExecuteAsync(
                """
                delete from public.reviews where match_id in (select id from public.matches where listing_id = @id);
                update public.offers set match_id = null where listing_id = @id;
                delete from public.matches where listing_id = @id;
                delete from public.listing_claims where listing_id = @id;
                delete from public.offers where listing_id = @id;
                delete from public.listings where id = @id;
                """,
                ("id", listingId));
        }
    }

    private protected async Task<Guid> CreateOpenListingAsync(string? title = null)
    {
        var response = await Fixture.Client.PostAsJsonAsync(
            "/listings",
            new
            {
                title = title ?? $"E2E Revize 2 ilanı {Guid.NewGuid():N}"[..40],
                description = "E2E Revize 2 zincirinin ürettiği geçici ilan. Danışan iletişim paylaşımına onay verdi.",
                specialization = new[] { Specialization },
                budgetMin = 750m,
                budgetMax = 1500m,
                preferredSessionType = "online",
                city = "İstanbul",
                assessmentResultId = (Guid?)null,
                contactConsent = true,
                phone = "05551112233",
            });
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"İlan oluşturulamadı ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }

        var listingId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _listingIds.Add(listingId);
        (await Fixture.Admin.PostAsync($"/admin/listings/{listingId}/approve", null)).EnsureSuccessStatusCode();
        return listingId;
    }

    private protected Task<HttpResponseMessage> AsAsync(AuthenticatedUser user, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return Fixture.Mobile.SendAsync(request);
    }

    private protected static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Beklenmeyen yanıt ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private protected async Task<JsonElement> QuoteAsync(AuthenticatedUser expert, Guid listingId)
        => await DataAsync(await AsAsync(expert, HttpMethod.Get, $"/offers/quote?listingId={listingId}"));

    private protected async Task<HttpResponseMessage> SendOfferAsync(AuthenticatedUser expert, Guid listingId, long? expectedFeeKurus)
        => await AsAsync(expert, HttpMethod.Post, "/offers", new
        {
            listingId,
            title = "E2E Revize 2 teklifi",
            price = 1000m,
            sessionType = "online",
            description = (string?)null,
            expectedFeeKurus,
        });

    /// <summary>Anlık ücreti okuyup onunla teklif gönderir; teklif gövdesini döner.</summary>
    private protected async Task<JsonElement> OfferAtCurrentFeeAsync(AuthenticatedUser expert, Guid listingId)
    {
        var fee = (await QuoteAsync(expert, listingId)).GetProperty("feeKurus").GetInt64();
        var response = await SendOfferAsync(expert, listingId, fee);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Teklif gönderilemedi ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private protected static async Task SetWalletAsync(AuthenticatedUser expert, long balanceKurus, int freeRights)
        => await E2ESql.ExecuteAsync(
            "update public.experts set balance_kurus = @balance, free_offer_rights = @rights where id = @id;",
            ("id", expert.AuthUserId),
            ("balance", balanceKurus),
            ("rights", freeRights));

    private protected async Task<JsonElement> WalletOfAsync(AuthenticatedUser expert)
        => await DataAsync(await AsAsync(expert, HttpMethod.Get, "/wallet/me"));

    private protected async Task<List<JsonElement>> NotificationsOfAsync(AuthenticatedUser user)
        => [.. (await DataAsync(await AsAsync(user, HttpMethod.Get, "/notifications"))).EnumerateArray()];

    private async Task<string> CreateSpecializationCategoryAsync()
    {
        var slug = Fixture.NewCategorySlug();
        var name = $"E2E {slug}";
        var response = await Fixture.Admin.PostAsJsonAsync(
            "/admin/categories",
            new
            {
                slug,
                name,
                icon = "brain",
                summary = "E2E Revize 2 testinin ürettiği geçici kategori.",
                description = "Bu kayıt otomatik testler tarafından üretilir ve silinir.",
                blogTag = slug,
                assessmentCategory = (string?)null,
            });
        response.EnsureSuccessStatusCode();
        return name;
    }
}
