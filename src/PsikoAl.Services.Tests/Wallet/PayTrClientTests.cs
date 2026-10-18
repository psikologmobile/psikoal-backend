using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PsikoAl.Services.Options;
using PsikoAl.Services.Payments;

namespace PsikoAl.Services.Tests.Wallet;

public sealed class PayTrClientTests
{
    private static readonly PayTrTokenRequest Request = new(
        "85.105.10.20",
        "PA0123ABC",
        "uzman@example.test",
        50000,
        [new PayTrBasketItem("PsikoAl bakiye yükleme (₺500)", 500m, 1)],
        "Ayşe Yılmaz",
        "İstanbul",
        "05551112233");

    private static readonly PayTrOptions Configured = new()
    {
        MerchantId = "123456",
        MerchantKey = "TESTKEY123",
        MerchantSalt = "TESTSALT456",
        OkUrl = "https://psikoal.test/ok",
        FailUrl = "https://psikoal.test/fail",
    };

    [Fact]
    public async Task Success_response_returns_the_token_and_posts_the_form_to_get_token()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"status":"success","token":"abc123"}""");

        var result = await CreateClient(handler, Configured).GetIframeTokenAsync(Request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("abc123", result.Token);
        Assert.Equal("https://www.paytr.com/odeme/api/get-token", handler.LastUri?.ToString());
        Assert.Contains("merchant_oid=PA0123ABC", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("paytr_token=", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_response_returns_the_reason()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"status":"failed","reason":"Zorunlu alan degeri gecersiz: merchant_id"}""");

        var result = await CreateClient(handler, Configured).GetIframeTokenAsync(Request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("merchant_id", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Network_failure_is_reported_not_thrown()
    {
        var handler = new StubHandler(new HttpRequestException("bağlantı yok"));

        var result = await CreateClient(handler, Configured).GetIframeTokenAsync(Request, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Missing_credentials_never_call_PayTR()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"status":"success","token":"abc123"}""");

        var result = await CreateClient(handler, new PayTrOptions()).GetIframeTokenAsync(Request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(handler.LastUri);
    }

    private static PayTrClient CreateClient(HttpMessageHandler handler, PayTrOptions options)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://www.paytr.com/") },
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<PayTrClient>.Instance);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body = string.Empty;
        private readonly Exception? _exception;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public StubHandler(Exception exception) => _exception = exception;

        public Uri? LastUri { get; private set; }

        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (_exception is not null)
            {
                throw _exception;
            }

            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }
}
