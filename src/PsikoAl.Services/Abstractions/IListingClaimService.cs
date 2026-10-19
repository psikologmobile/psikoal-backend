using PsikoAl.Common.Dtos.Claim;

namespace PsikoAl.Services.Abstractions;

public interface IListingClaimService
{
    Task<ClaimDto> CreateAsync(Guid expertUserId, Guid listingId, string? note, CancellationToken cancellationToken);

    Task<ClaimDto> DismissAsync(Guid clientUserId, Guid listingId, Guid expertId, CancellationToken cancellationToken);
}
