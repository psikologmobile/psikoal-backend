using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;

namespace PsikoAl.Data.Repositories;

public sealed class MatchRepository(AppDbContext dbContext)
    : Repository<Match, Guid>(dbContext), IMatchRepository
{
    public IQueryable<Match> QueryWithDetails()
        => DbContext.Matches
            .AsNoTracking()
            .Include(match => match.Listing)
            .Include(match => match.AcceptedOffer)
            .Include(match => match.Client)
            .Include(match => match.Expert)
            .ThenInclude(expert => expert!.Profile);

    public Task<Match?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken)
        => DbContext.Matches
            .Include(match => match.Listing)
            .Include(match => match.AcceptedOffer)
            .Include(match => match.Client)
            .Include(match => match.Expert)
            .ThenInclude(expert => expert!.Profile)
            .FirstOrDefaultAsync(match => match.Id == id, cancellationToken);

    // Ham ADO: fonksiyon üç kolon (biri uuid[]) döndürüyor.
    public async Task<MarkMatchedOutcome> MarkListingMatchedAsync(
        Guid listingId,
        Guid offerId,
        Guid actorClientId,
        CancellationToken cancellationToken)
    {
        var connection = DbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await DbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "select match_id, expert_id, other_expert_ids from public.mark_listing_matched(@listing, @offer, @client)";
        command.Parameters.Add(new NpgsqlParameter("listing", listingId));
        command.Parameters.Add(new NpgsqlParameter("offer", offerId));
        command.Parameters.Add(new NpgsqlParameter("client", actorClientId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("mark_listing_matched satır döndürmedi.");
        }

        return new MarkMatchedOutcome(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? [] : reader.GetFieldValue<Guid[]>(2));
    }
}
