using PsikoAl.Common.Dtos.Auth;
using PsikoAl.Common.Dtos.Auth.Update;

namespace PsikoAl.Services.Abstractions;

public interface IProfileService
{
    Task<AuthUserDto> GetMeAsync(Guid userId, CancellationToken cancellationToken);

    Task<ReferrerDto?> GetReferrerAsync(Guid userId, CancellationToken cancellationToken);

    Task<AuthUserDto> UpdateMeAsync(Guid userId, UpdateProfileDto request, CancellationToken cancellationToken);

    Task FreezeMeAsync(Guid userId, CancellationToken cancellationToken);

    Task DeleteMeAsync(Guid userId, CancellationToken cancellationToken);
}
