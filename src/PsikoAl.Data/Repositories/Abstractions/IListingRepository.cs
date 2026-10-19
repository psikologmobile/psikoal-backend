using PsikoAl.Data.Entities;

namespace PsikoAl.Data.Repositories.Abstractions;

public interface IListingRepository : IRepository<Listing, Guid>
{
    Task<Listing?> GetWithClientAsync(Guid id, CancellationToken cancellationToken);

    Task<int> CountActiveByClientAsync(Guid clientId, CancellationToken cancellationToken);

    // Teklif sırasında ilan satırını kilitler (for update) ve teklif sayısını döner; ilan
    // OPEN değilse null. Aynı ilana eşzamanlı iki teklif aynı ücreti göremez: ücret teklif
    // sayısına bağlı. Çağıran transaction içinde olmalıdır.
    Task<int?> LockOpenForOfferAsync(Guid listingId, CancellationToken cancellationToken);
}
