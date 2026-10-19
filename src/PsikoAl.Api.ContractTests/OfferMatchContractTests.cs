using System.Net;
using System.Net.Http.Json;

namespace PsikoAl.Api.ContractTests;

public sealed class OfferMatchContractTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public OfferMatchContractTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Offer_endpoints_require_authentication()
    {
        var my = await _client.GetAsync("/offers/my", CancellationToken.None);
        var detail = await _client.GetAsync($"/offers/{Guid.NewGuid()}", CancellationToken.None);
        var create = await _client.PostAsJsonAsync("/offers", new { listingId = Guid.NewGuid() }, CancellationToken.None);
        var quote = await _client.GetAsync($"/offers/quote?listingId={Guid.NewGuid()}", CancellationToken.None);
        var markMatched = await _client.PostAsJsonAsync($"/listings/{Guid.NewGuid()}/match", new { offerId = Guid.NewGuid() }, CancellationToken.None);
        var claim = await _client.PostAsJsonAsync($"/listings/{Guid.NewGuid()}/claims", new { note = "x" }, CancellationToken.None);
        var dismiss = await _client.PostAsync($"/listings/{Guid.NewGuid()}/claims/{Guid.NewGuid()}/dismiss", null, CancellationToken.None);
        var rules = await _client.GetAsync("/listings/rules", CancellationToken.None);
        var withdraw = await _client.PostAsync($"/offers/{Guid.NewGuid()}/withdraw", null, CancellationToken.None);
        var forListing = await _client.GetAsync($"/listings/{Guid.NewGuid()}/offers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, my.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, quote.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, markMatched.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, claim.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, dismiss.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rules.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withdraw.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forListing.StatusCode);
    }

    [Fact]
    public async Task Match_endpoints_require_authentication()
    {
        var active = await _client.GetAsync("/match/active", CancellationToken.None);
        var list = await _client.GetAsync("/matches", CancellationToken.None);
        var detail = await _client.GetAsync($"/matches/{Guid.NewGuid()}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, active.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
    }

    // REVIZE-2 §3.4, §5, §10: kabul/ret ve kullanıcı sonlandırma uçları kaldırıldı.
    [Theory]
    [InlineData("/offers/{0}/accept")]
    [InlineData("/offers/{0}/reject")]
    [InlineData("/match/{0}/release")]
    public async Task Removed_offer_and_match_actions_are_gone(string template)
    {
        var response = await _client.PostAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Guid.NewGuid()), null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_match_endpoints_require_authentication()
    {
        var list = await _client.GetAsync("/admin/matches", CancellationToken.None);
        var forceRelease = await _client.PostAsJsonAsync(
            $"/admin/matches/{Guid.NewGuid()}/force-release",
            new { targetStatus = "RELEASED", reason = "test" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forceRelease.StatusCode);
    }
}
