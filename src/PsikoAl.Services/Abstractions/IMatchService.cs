using PsikoAl.Common.Dtos.Match;

namespace PsikoAl.Services.Abstractions;

public interface IMatchService
{
    Task<MatchListResult> ListMyAsync(Guid userId, string[]? statusFilter, CancellationToken cancellationToken);

    Task<MatchDto?> GetActiveAsync(Guid userId, CancellationToken cancellationToken);

    Task<MatchDto> GetByIdAsync(Guid matchId, Guid viewerUserId, CancellationToken cancellationToken);
}
