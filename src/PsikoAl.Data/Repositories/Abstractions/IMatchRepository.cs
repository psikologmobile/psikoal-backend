using PsikoAl.Data.Entities;

namespace PsikoAl.Data.Repositories.Abstractions;

public sealed record MarkMatchedOutcome(Guid MatchId, Guid ExpertId, IReadOnlyList<Guid> OtherExpertIds);

public interface IMatchRepository : IRepository<Match, Guid>
{
    IQueryable<Match> QueryWithDetails();

    Task<Match?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken);

    // public.mark_listing_matched(): tek transaction'da match + ilan MATCHED + teklif
    // ACCEPTED + diğer PENDING'ler REJECTED/OTHER_EXPERT. Kural ihlali PostgresException
    // (MessageText = hata kodu) olarak gelir.
    Task<MarkMatchedOutcome> MarkListingMatchedAsync(
        Guid listingId,
        Guid offerId,
        Guid actorClientId,
        CancellationToken cancellationToken);
}
