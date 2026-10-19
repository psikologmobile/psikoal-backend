using PsikoAl.Data.Entities;

namespace PsikoAl.Data.Repositories.Abstractions;

public interface IOfferRepository : IRepository<Offer, Guid>
{
    IQueryable<Offer> QueryWithDetails();

    Task<Offer?> GetWithListingAndExpertAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsForListingAndExpertAsync(Guid listingId, Guid expertId, CancellationToken cancellationToken);

    Task IncrementListingOfferCountAsync(Guid listingId, CancellationToken cancellationToken);

    // İlan kapanınca bekleyen teklifler REJECTED + closed_reason; etkilenen uzmanlar döner.
    Task<IReadOnlyList<Guid>> RejectPendingForListingAsync(Guid listingId, string closedReason, CancellationToken cancellationToken);
}
