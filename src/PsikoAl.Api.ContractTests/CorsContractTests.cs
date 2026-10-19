using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using PsikoAl.Api.Extensions;

namespace PsikoAl.Api.ContractTests;

// Web sitesi ayrı bir kökenden (Render static site / alan adı) API'yi tarayıcıyla çağırır.
// İzinli kökene preflight dahil ACAO döner; izinsiz kökene hiç CORS başlığı dönmez.
public sealed class CorsContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AllowedOrigin = "https://site.psikoal.test";
    private const string SecondOrigin = "https://www.psikoal.test";
    private const string DisallowedOrigin = "https://kotu-niyetli.test";

    private HttpClient CreateClient() =>
        factory
            .WithWebHostBuilder(builder =>
                builder.UseSetting("Cors:AllowedOrigins", $"{AllowedOrigin}/, {SecondOrigin}"))
            .CreateClient();

    private static HttpRequestMessage Preflight(string origin, string path = "/wallet/me")
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }

    [Theory]
    [InlineData(AllowedOrigin)]
    [InlineData(SecondOrigin)]
    public async Task Preflight_from_allowed_origin_succeeds(string origin)
    {
        using var client = CreateClient();

        using var response = await client.SendAsync(Preflight(origin), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        var allowedHeaders = string.Join(',', response.Headers.GetValues("Access-Control-Allow-Headers"));
        Assert.Contains("authorization", allowedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Preflight_from_disallowed_origin_gets_no_cors_headers()
    {
        using var client = CreateClient();

        using var response = await client.SendAsync(Preflight(DisallowedOrigin), CancellationToken.None);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Simple_request_from_allowed_origin_carries_acao_and_disallowed_does_not()
    {
        using var client = CreateClient();

        using var allowed = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        allowed.Headers.Add("Origin", AllowedOrigin);
        using var allowedResponse = await client.SendAsync(allowed, CancellationToken.None);

        using var denied = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        denied.Headers.Add("Origin", DisallowedOrigin);
        using var deniedResponse = await client.SendAsync(denied, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(allowedResponse.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal(HttpStatusCode.OK, deniedResponse.StatusCode);
        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Without_configuration_no_origin_is_allowed()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Preflight(AllowedOrigin), CancellationToken.None);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void Origins_are_read_from_array_and_comma_forms_and_normalized()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://a.test/",
                ["Cors:AllowedOrigins:1"] = "https://b.test; https://A.test",
            })
            .Build();

        Assert.Equal(["https://a.test", "https://b.test"], CorsSetup.ReadAllowedOrigins(configuration));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("site.test")]
    [InlineData("https://site.test/yol")]
    [InlineData("ftp://site.test")]
    public void Invalid_origin_fails_fast(string origin)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:AllowedOrigins"] = origin })
            .Build();

        Assert.Throws<InvalidOperationException>(() => CorsSetup.ReadAllowedOrigins(configuration));
    }
}
