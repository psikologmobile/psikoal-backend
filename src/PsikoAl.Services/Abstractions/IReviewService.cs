using PsikoAl.Common.Dtos.Review;
using PsikoAl.Common.Dtos.Review.Create;

namespace PsikoAl.Services.Abstractions;

public interface IReviewService
{
    Task<ReviewDto> CreateAsync(Guid clientUserId, Guid expertId, CreateReviewDto request, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewDto>> ListApprovedForExpertAsync(Guid expertId, Guid? viewerUserId, CancellationToken cancellationToken);
}
