using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsikoAl.Common.Dtos.Payment;
using PsikoAl.Common.Dtos.Wallet;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.ContractTests;

// Sözleşme: docs/REVIZE-2.md §2.2 ve docs/PAYMENTS.md. Mobil uygulama ve web sitesi bu
// alan adlarına göre yazılıyor; servis sahte, JSON'u üreten pipeline gerçek (controller,
// { data } zarfı, camelCase, kimlik doğrulama).
public sealed class WalletContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Expert_wallet_endpoints_require_authentication()
    {
        using var client = factory.CreateClient();

        var responses = new[]
        {
            await client.GetAsync("/wallet/me", CancellationToken.None),
            await client.GetAsync("/wallet/transactions", CancellationToken.None),
            await client.GetAsync("/wallet/payments", CancellationToken.None),
            await client.GetAsync($"/wallet/payments/{Guid.NewGuid()}/invoice", CancellationToken.None),
            await client.PostAsJsonAsync("/payments/paytr/init", new { amountKurus = 50_000 }, CancellationToken.None),
            await client.GetAsync($"/offers/quote?listingId={Guid.NewGuid()}", CancellationToken.None),
            await client.GetAsync("/me/referrer", CancellationToken.None),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    [Fact]
    public async Task Admin_wallet_endpoints_require_authentication()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        var responses = new[]
        {
            await client.GetAsync("/admin/wallet/payments?status=PAID", CancellationToken.None),
            await client.GetAsync($"/admin/wallet/experts/{id}", CancellationToken.None),
            await client.PostAsJsonAsync($"/admin/wallet/experts/{id}/gift", new { amountKurus = 100 }, CancellationToken.None),
            await client.PostAsJsonAsync($"/admin/wallet/experts/{id}/adjust", new { amountKurus = 100, reason = "r" }, CancellationToken.None),
            await client.GetAsync("/admin/pricing-periods", CancellationToken.None),
            await client.GetAsync("/admin/referrals", CancellationToken.None),
            await client.GetAsync("/admin/claims", CancellationToken.None),
            await client.GetAsync($"/admin/users/{id}/activity", CancellationToken.None),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    // REVIZE-2 §10: kredi uçları kaldırıldı.
    [Theory]
    [InlineData("/credits/me")]
    [InlineData("/credits/transactions")]
    [InlineData("/credits/packages")]
    [InlineData("/admin/credits/packages")]
    public async Task Removed_credit_endpoints_are_gone(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Topup_options_are_public_and_match_the_web_contract()
    {
        using var client = CreateClient(new FakeWalletService(), new FakePaymentService(), authenticated: false);

        var body = await client.GetFromJsonAsync<JsonElement>("/wallet/topup-options", CancellationToken.None);

        var data = body.GetProperty("data");
        var preset = data.GetProperty("presets")[0];
        Assert.Equal(50_000, preset.GetProperty("amountKurus").GetInt64());
        Assert.Equal("₺500", preset.GetProperty("label").GetString());
        Assert.Equal(25_000, data.GetProperty("minKurus").GetInt64());
        Assert.Equal(5_000_000, data.GetProperty("maxKurus").GetInt64());
        Assert.Equal("KDV dahil", data.GetProperty("vatNote").GetString());
    }

    [Fact]
    public async Task Wallet_matches_the_mobile_contract()
    {
        using var client = CreateClient(new FakeWalletService(), new FakePaymentService(), authenticated: true);

        var body = await client.GetFromJsonAsync<JsonElement>("/wallet/me", CancellationToken.None);

        var data = body.GetProperty("data");
        Assert.Equal(125_000, data.GetProperty("balanceKurus").GetInt64());
        Assert.Equal("₺1.250", data.GetProperty("balanceLabel").GetString());
        Assert.Equal(1, data.GetProperty("freeOfferRights").GetInt32());
        Assert.Equal("ABCD2345", data.GetProperty("referralCode").GetString());
        Assert.Equal("https://psikoal.test/davet/ABCD2345", data.GetProperty("referralLink").GetString());
    }

    [Fact]
    public async Task Transactions_match_the_mobile_contract_and_forward_paging()
    {
        var wallet = new FakeWalletService();
        using var client = CreateClient(wallet, new FakePaymentService(), authenticated: true);

        var body = await client.GetFromJsonAsync<JsonElement>("/wallet/transactions?page=2&perPage=10&type=OFFER_FEE", CancellationToken.None);

        Assert.Equal((2, 10, "OFFER_FEE"), wallet.LastQuery);
        var meta = body.GetProperty("meta");
        Assert.Equal(2, meta.GetProperty("page").GetInt32());
        Assert.Equal(11, meta.GetProperty("total").GetInt32());
        Assert.Equal(10, meta.GetProperty("perPage").GetInt32());

        var item = Assert.Single(body.GetProperty("data").EnumerateArray());
        foreach (var field in (string[])["id", "type", "typeLabel", "amountLabel", "freeRightsLabel", "balanceAfterLabel", "description", "createdAt", "timeLabel"])
        {
            Assert.Equal(JsonValueKind.String, item.GetProperty(field).ValueKind);
        }

        Assert.Equal(-62_500, item.GetProperty("amountKurus").GetInt64());
        Assert.Equal("−₺625", item.GetProperty("amountLabel").GetString());
        Assert.Equal(0, item.GetProperty("freeRightsDelta").GetInt32());
        Assert.Equal(string.Empty, item.GetProperty("freeRightsLabel").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("paymentId").ValueKind);
        Assert.Equal("Kaygı", item.GetProperty("listingTitle").GetString());
    }

    [Fact]
    public async Task Payments_and_invoice_match_the_contract()
    {
        using var client = CreateClient(new FakeWalletService(), new FakePaymentService(), authenticated: true);

        var payments = await client.GetFromJsonAsync<JsonElement>("/wallet/payments", CancellationToken.None);
        var invoice = await client.GetFromJsonAsync<JsonElement>($"/wallet/payments/{Guid.NewGuid()}/invoice", CancellationToken.None);

        var payment = Assert.Single(payments.GetProperty("data").EnumerateArray());
        Assert.Equal("PAID", payment.GetProperty("status").GetString());
        Assert.Equal("Ödendi", payment.GetProperty("statusLabel").GetString());
        Assert.Equal("fatura.pdf", payment.GetProperty("invoice").GetProperty("fileName").GetString());
        Assert.Equal("https://signed.test/x.pdf", invoice.GetProperty("data").GetProperty("url").GetString());
    }

    [Fact]
    public async Task Payment_init_takes_an_amount_and_returns_iframe_data()
    {
        var payments = new FakePaymentService();
        using var client = CreateClient(new FakeWalletService(), payments, authenticated: true);

        var response = await client.PostAsJsonAsync("/payments/paytr/init", new { amountKurus = 100_000 }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100_000, payments.LastAmountKurus);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None)).GetProperty("data");
        Assert.Equal("tok123", data.GetProperty("iframeToken").GetString());
        Assert.Equal("PA0123ABC", data.GetProperty("merchantOid").GetString());
        Assert.Equal("https://www.paytr.com/odeme/guvenli/tok123", data.GetProperty("iframeUrl").GetString());
    }

    [Fact]
    public async Task Payment_init_rejects_a_missing_amount()
    {
        using var client = CreateClient(new FakeWalletService(), new FakePaymentService(), authenticated: true);

        var response = await client.PostAsJsonAsync("/payments/paytr/init", new { amountKurus = 0 }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(PayTrCallbackOutcome.Paid)]
    [InlineData(PayTrCallbackOutcome.Failed)]
    [InlineData(PayTrCallbackOutcome.AlreadyProcessed)]
    public async Task Callback_is_anonymous_form_post_and_answers_plain_OK(PayTrCallbackOutcome outcome)
    {
        var payments = new FakePaymentService { Outcome = outcome };
        using var client = CreateClient(new FakeWalletService(), payments, authenticated: false);

        var response = await client.PostAsync("/payments/paytr/callback", CallbackForm(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("OK", await response.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Equal("PA0123ABC", payments.LastCallback?.MerchantOid);
        Assert.Equal("success", payments.LastCallback?.Status);
        Assert.Equal("50000", payments.LastCallback?.TotalAmount);
        Assert.Equal("card", payments.LastCallback?.PaymentType);
    }

    [Theory]
    [InlineData(PayTrCallbackOutcome.InvalidHash)]
    [InlineData(PayTrCallbackOutcome.UnknownOrder)]
    public async Task Callback_does_not_answer_OK_when_the_notification_is_rejected(PayTrCallbackOutcome outcome)
    {
        using var client = CreateClient(new FakeWalletService(), new FakePaymentService { Outcome = outcome }, authenticated: false);

        var response = await client.PostAsync("/payments/paytr/callback", CallbackForm(), CancellationToken.None);

        Assert.False(response.IsSuccessStatusCode);
        Assert.NotEqual("OK", await response.Content.ReadAsStringAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Callback_with_missing_fields_is_rejected_before_the_service()
    {
        var payments = new FakePaymentService();
        using var client = CreateClient(new FakeWalletService(), payments, authenticated: false);

        var response = await client.PostAsync(
            "/payments/paytr/callback",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["merchant_oid"] = "PA1" }),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(payments.LastCallback);
    }

    private static FormUrlEncodedContent CallbackForm()
        => new(new Dictionary<string, string>
        {
            ["merchant_oid"] = "PA0123ABC",
            ["status"] = "success",
            ["total_amount"] = "50000",
            ["hash"] = "aGFzaA==",
            ["payment_type"] = "card",
            ["currency"] = "TL",
            ["payment_amount"] = "50000",
            ["test_mode"] = "1",
        });

    private HttpClient CreateClient(IWalletService wallet, IWalletPaymentService payments, bool authenticated)
    {
        var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => wallet);
                services.AddScoped(_ => payments);
                services.AddScoped<IAccountStatusService, ActiveAccountStatus>();
                services
                    .AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            }))
            .CreateClient();

        if (authenticated)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, UserId.ToString());
        }

        return client;
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string HeaderName = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HeaderName, out var userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class ActiveAccountStatus : IAccountStatusService
    {
        public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);

        public void Invalidate(Guid userId)
        {
        }
    }

    private sealed class FakeWalletService : IWalletService
    {
        public (int? Page, int? PerPage, string? Type) LastQuery { get; private set; }

        public Task<WalletDto> GetMineAsync(Guid expertUserId, CancellationToken cancellationToken)
            => Task.FromResult(new WalletDto(125_000, "₺1.250", 1, "ABCD2345", "https://psikoal.test/davet/ABCD2345"));

        public Task<WalletPageResult<WalletTransactionDto>> ListMyTransactionsAsync(
            Guid expertUserId,
            int? page,
            int? perPage,
            string? type,
            CancellationToken cancellationToken)
        {
            LastQuery = (page, perPage, type);
            var item = new WalletTransactionDto(
                Guid.NewGuid(),
                "OFFER_FEE",
                "Teklif ücreti",
                -62_500,
                "−₺625",
                0,
                string.Empty,
                37_500,
                "₺375",
                "\"Kaygı\" ilanına teklif ücreti",
                Guid.NewGuid(),
                "Kaygı",
                Guid.NewGuid(),
                null,
                DateTimeOffset.UtcNow,
                "şimdi");
            return Task.FromResult(new WalletPageResult<WalletTransactionDto>([item], page ?? 1, perPage ?? 20, 11));
        }

        public Task<WalletPageResult<WalletPaymentDto>> ListMyPaymentsAsync(
            Guid expertUserId,
            int? page,
            int? perPage,
            CancellationToken cancellationToken)
            => Task.FromResult(new WalletPageResult<WalletPaymentDto>(
                [new WalletPaymentDto(Guid.NewGuid(), 50_000, "₺500", "PAID", "Ödendi", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new WalletInvoiceDto("fatura.pdf", DateTimeOffset.UtcNow))],
                1,
                20,
                1));

        public Task<InvoiceUrlDto> GetInvoiceUrlAsync(Guid expertUserId, Guid paymentId, CancellationToken cancellationToken)
            => Task.FromResult(new InvoiceUrlDto("https://signed.test/x.pdf"));

        public Task<TopupOptionsDto> GetTopupOptionsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new TopupOptionsDto([new TopupPresetDto(50_000, "₺500")], 25_000, 5_000_000, "KDV dahil"));

        public Task GrantApprovalRewardsAsync(Guid expertId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePaymentService : IWalletPaymentService
    {
        public PayTrCallbackOutcome Outcome { get; init; } = PayTrCallbackOutcome.Paid;

        public long? LastAmountKurus { get; private set; }

        public PayTrCallbackDto? LastCallback { get; private set; }

        public Task<PayTrInitResultDto> InitPayTrAsync(
            Guid expertUserId,
            long amountKurus,
            string userIp,
            CancellationToken cancellationToken)
        {
            LastAmountKurus = amountKurus;
            return Task.FromResult(new PayTrInitResultDto("tok123", "PA0123ABC", "https://www.paytr.com/odeme/guvenli/tok123"));
        }

        public Task<PayTrCallbackOutcome> HandlePayTrCallbackAsync(PayTrCallbackDto callback, CancellationToken cancellationToken)
        {
            LastCallback = callback;
            return Task.FromResult(Outcome);
        }
    }
}
