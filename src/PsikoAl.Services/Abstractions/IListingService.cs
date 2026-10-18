using PsikoAl.Common.Dtos.Listing;
using PsikoAl.Common.Dtos.Listing.Create;

namespace PsikoAl.Services.Abstractions;

// City: tam eşleşme (büyük/küçük harf duyarsız). InvitedOnly: yalnız görüntüleyen uzmanın
// davetiyle gelen danışanların ilanları. Sort: budget_desc | budget_asc | offer_asc | fee_asc | (yeni önce).
public sealed record ListingFeedFilters(
    IReadOnlyList<string>? Specialization,
    IReadOnlyList<string>? SessionType,
    decimal? BudgetMin,
    decimal? BudgetMax,
    string? Sort,
    string? City = null,
    bool InvitedOnly = false);

public sealed record ListingListResult(IReadOnlyList<ListingDto> Data, int Total, int? ActiveCount);

public interface IListingService
{
    Task<ListingRulesDto> GetRulesAsync(CancellationToken cancellationToken);

    Task<ListingDto> CreateAsync(Guid clientUserId, CreateListingDto request, CancellationToken cancellationToken);

    Task<ListingListResult> ListFeedAsync(Guid viewerUserId, ListingFeedFilters filters, CancellationToken cancellationToken);

    Task<ListingListResult> ListMyAsync(Guid clientUserId, string? status, CancellationToken cancellationToken);

    Task<ListingDto> GetByIdAsync(Guid listingId, Guid viewerUserId, CancellationToken cancellationToken);

    Task<ListingDto> CloseAsync(Guid clientUserId, Guid listingId, CancellationToken cancellationToken);

    Task<MarkMatchedResultDto> MarkMatchedAsync(Guid clientUserId, Guid listingId, Guid offerId, CancellationToken cancellationToken);
}
