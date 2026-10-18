using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PsikoAl.Api.Middleware;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Category;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.ContractTests;

public sealed class ErrorHandlingContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>Beklenmeyen hata gövdesiz 500 değil, ApiErrorDto INTERNAL_ERROR döner.</summary>
    [Fact]
    public async Task Unexpected_failure_returns_api_error_body()
    {
        var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(
                services => services.AddScoped<ICategoryService, ExplodingCategoryService>()))
            .CreateClient();

        var response = await client.GetAsync("/categories", CancellationToken.None);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(ErrorKeys.InternalError, body.GetProperty("code").GetString());
        Assert.Equal(ErrorMessagesTr.For(ErrorKeys.InternalError), body.GetProperty("message").GetString());
        Assert.DoesNotContain(ExplodingCategoryService.Secret, body.GetRawText(), StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("trace-id"));
    }

    [Theory]
    [InlineData(ErrorKeys.ListingNotFound, 404)]
    [InlineData(ErrorKeys.AssessmentResultNotFound, 404)]
    [InlineData(ErrorKeys.ExpertNotFound, 404)]
    [InlineData(ErrorKeys.MatchNotParticipant, 403)]
    [InlineData(ErrorKeys.OfferExpertRoleRequired, 403)]
    [InlineData(ErrorKeys.AdminUserNotFound, 403)]
    [InlineData(ErrorKeys.OfferAlreadyExists, 409)]
    [InlineData(ErrorKeys.ResourceConflict, 409)]
    [InlineData(ErrorKeys.ListingMaxActiveExceeded, 422)]
    [InlineData(ErrorKeys.AuthInvalidCredentials, 422)]
    [InlineData(ErrorKeys.AuthEmailAlreadyRegistered, 422)]
    [InlineData(ErrorKeys.ValidationFailed, 422)]
    [InlineData(ErrorKeys.InsufficientBalance, 422)]
    [InlineData(ErrorKeys.OfferFeeChanged, 409)]
    [InlineData(ErrorKeys.ListingNotOpen, 409)]
    [InlineData(ErrorKeys.ListingNotOwner, 403)]
    [InlineData(ErrorKeys.OfferNotOnListing, 422)]
    [InlineData(ErrorKeys.ClaimAlreadyExists, 409)]
    [InlineData(ErrorKeys.ClaimNotAllowed, 422)]
    [InlineData(ErrorKeys.ContactConsentRequired, 422)]
    [InlineData(ErrorKeys.PhoneRequired, 422)]
    [InlineData(ErrorKeys.ListingBudgetOutOfRange, 422)]
    [InlineData(ErrorKeys.TopupAmountOutOfRange, 422)]
    [InlineData(ErrorKeys.InvoiceNotFound, 404)]
    [InlineData(ErrorKeys.WalletBalanceNegative, 422)]
    [InlineData(ErrorKeys.TopupExpertNotApproved, 403)]
    [InlineData(ErrorKeys.PaymentProviderUnavailable, 422)]
    [InlineData(ErrorKeys.ExpertRoleRequired, 403)]
    public void Domain_error_keys_map_to_http_status(string errorKey, int expectedStatus)
        => Assert.Equal(expectedStatus, DomainErrorStatusCodes.For(errorKey));

    [Theory]
    [InlineData("uq_offers_listing_expert", ErrorKeys.OfferAlreadyExists)]
    [InlineData("uq_reviews_client_match", ErrorKeys.ReviewAlreadyExists)]
    [InlineData("experts_pkey", ErrorKeys.ExpertProfileAlreadyExists)]
    [InlineData("categories_slug_key", ErrorKeys.CategorySlugAlreadyExists)]
    [InlineData("blogs_slug_key", ErrorKeys.BlogSlugAlreadyExists)]
    [InlineData("uq_listing_claims_listing_expert", ErrorKeys.ClaimAlreadyExists)]
    [InlineData("some_future_constraint", ErrorKeys.ResourceConflict)]
    [InlineData(null, ErrorKeys.ResourceConflict)]
    public void Unique_violations_map_to_domain_error_keys(string? constraintName, string expectedKey)
        => Assert.Equal(expectedKey, UniqueViolationErrorKeys.For(constraintName));

    private sealed class ExplodingCategoryService : ICategoryService
    {
        public const string Secret = "connection-string-sizintisi";

        public Task<IReadOnlyList<CategoryDto>> ListActiveAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException(Secret);

        public Task<CategoryDetailDto> GetBySlugAsync(string slug, CancellationToken cancellationToken)
            => throw new InvalidOperationException(Secret);

        public Task<bool> AllActiveNamesExistAsync(IEnumerable<string> names, CancellationToken cancellationToken)
            => throw new InvalidOperationException(Secret);
    }
}
