using System.Net;
using System.Net.Http.Json;

namespace PsikoAl.Api.ContractTests;

public sealed class ExpertContractTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public ExpertContractTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Expert_profile_endpoints_require_authentication()
    {
        var create = await _client.PostAsJsonAsync("/experts/profile", new { title = "Dr." }, CancellationToken.None);
        var update = await _client.PatchAsJsonAsync("/experts/profile", new { title = "Dr." }, CancellationToken.None);
        var get = await _client.GetAsync($"/experts/{Guid.NewGuid()}", CancellationToken.None);
        var deleteCertificate = await _client.DeleteAsync(
            $"/experts/profile/certificates?certificate={Uri.EscapeDataString($"{Guid.NewGuid()}/certificates/a.pdf")}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, update.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, deleteCertificate.StatusCode);
    }

    [Fact]
    public async Task Admin_expert_endpoints_require_authentication()
    {
        var list = await _client.GetAsync("/admin/experts", CancellationToken.None);
        var approve = await _client.PostAsJsonAsync($"/admin/experts/{Guid.NewGuid()}/approve", new { }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, approve.StatusCode);
    }
}
