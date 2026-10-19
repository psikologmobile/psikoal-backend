using Microsoft.EntityFrameworkCore;
using PsikoAl.Common.Constants;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;

namespace PsikoAl.Data.Repositories;

public sealed class OfferRepository(AppDbContext dbContext)
    : Repository<Offer, Guid>(dbContext), IOfferRepository
{
    public IQueryable<Offer> QueryWithDetails()
        => DbContext.Offers
            .AsNoTracking()
            .Include(offer => offer.Listing)
            .ThenInclude(listing => listing!.Client)
            .Include(offer => offer.Expert)
            .ThenInclude(expert => expert!.Profile);

    public Task<Offer?> GetWithListingAndExpertAsync(Guid id, CancellationToken cancellationToken)
        => DbContext.Offers
            .Include(offer => offer.Listing)
            .ThenInclude(listing => listing!.Client)
            .Include(offer => offer.Expert)
            .ThenInclude(expert => expert!.Profile)
            .FirstOrDefaultAsync(offer => offer.Id == id, cancellationToken);

    public Task<bool> ExistsForListingAndExpertAsync(Guid listingId, Guid expertId, CancellationToken cancellationToken)
        => DbContext.Offers.AnyAsync(
            offer => offer.ListingId == listingId && offer.ExpertId == expertId,
            cancellationToken);

    public Task IncrementListingOfferCountAsync(Guid listingId, CancellationToken cancellationToken)
        => DbContext.Listings
            .Where(listing => listing.Id == listingId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(listing => listing.OfferCount, listing => listing.OfferCount + 1),
                cancellationToken);

    public async Task<IReadOnlyList<Guid>> RejectPendingForListingAsync(
        Guid listingId,
        string closedReason,
        CancellationToken cancellationToken)
    {
        var pending = DbContext.Offers.Where(offer => offer.ListingId == listingId && offer.Status == OfferStatuses.Pending);
        var expertIds = await pending.Select(offer => offer.ExpertId).ToListAsync(cancellationToken);
        await pending.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(offer => offer.Status, OfferStatuses.Rejected)
                .SetProperty(offer => offer.ClosedReason, closedReason),
            cancellationToken);
        return expertIds;
    }
}
