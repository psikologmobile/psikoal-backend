using PsikoAl.Common.Dtos.Offer;
using PsikoAl.Common.Dtos.Offer.Create;

namespace PsikoAl.Services.Abstractions;

public interface IOfferService
{
    Task<OfferQuoteDto> QuoteAsync(Guid expertUserId, Guid listingId, CancellationToken cancellationToken);

    Task<OfferDto> CreateAsync(Guid expertUserId, CreateOfferDto request, CancellationToken cancellationToken);

    Task<OfferListResult> ListMyAsync(Guid expertUserId, string? status, CancellationToken cancellationToken);

    Task<OfferListResult> ListForListingAsync(Guid listingId, Guid viewerUserId, CancellationToken cancellationToken);

    Task<OfferDto> GetByIdAsync(Guid offerId, Guid viewerUserId, CancellationToken cancellationToken);

    Task<OfferDto> WithdrawAsync(Guid expertUserId, Guid offerId, CancellationToken cancellationToken);
}
