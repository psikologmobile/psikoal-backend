using Microsoft.EntityFrameworkCore;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Match;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Mapping;

namespace PsikoAl.Services;

public sealed class MatchService(IUnitOfWork unitOfWork) : IMatchService
{
    public async Task<MatchListResult> ListMyAsync(Guid userId, string[]? statusFilter, CancellationToken cancellationToken)
    {
        var allMine = unitOfWork.Matches.QueryWithDetails()
            .Where(match => match.ClientId == userId || match.ExpertId == userId);

        var query = allMine;
        if (statusFilter is { Length: > 0 })
        {
            query = query.Where(match => statusFilter.Contains(match.Status));
        }

        var matches = await query.OrderByDescending(match => match.CreatedAt).ToListAsync(cancellationToken);
        var activeCount = await allMine.CountAsync(match => match.Status == MatchStatuses.Active, cancellationToken);
        var pastCount = await allMine.CountAsync(match => MatchStatuses.Past.Contains(match.Status), cancellationToken);

        var dtos = matches.Select(MatchMapper.ToMatchDto).ToList();
        return new MatchListResult(dtos, dtos.Count, activeCount, pastCount);
    }

    public async Task<MatchDto?> GetActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var match = await unitOfWork.Matches.QueryWithDetails()
            .Where(m => (m.ClientId == userId || m.ExpertId == userId) && m.Status == MatchStatuses.Active)
            .FirstOrDefaultAsync(cancellationToken);

        return match is null ? null : MatchMapper.ToMatchDto(match);
    }

    public async Task<MatchDto> GetByIdAsync(Guid matchId, Guid viewerUserId, CancellationToken cancellationToken)
    {
        var match = await unitOfWork.Matches.GetWithDetailsAsync(matchId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.MatchNotFound);

        if (match.ClientId != viewerUserId && match.ExpertId != viewerUserId)
        {
            throw new DomainException(ErrorKeys.MatchNotFound);
        }

        return MatchMapper.ToMatchDto(match);
    }
}
