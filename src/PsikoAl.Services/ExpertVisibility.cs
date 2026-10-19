using Microsoft.EntityFrameworkCore;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Repositories.Abstractions;

namespace PsikoAl.Services;

/// <summary>REVIZE-2 §6: danışan bir uzman profilini yalnız ilişkisi varsa görür.</summary>
/// <remarks>
/// Danışan için izin: (a) uzman danışanın bir ilanına teklif vermiş ya da (b) danışan o
/// uzmanın davetiyle gelmiş. Aksi hâlde 404 EXPERT_NOT_FOUND — profilin VARLIĞI da
/// söylenmez. Uzman ve diğer roller için mevcut kural (onaylı profil herkese, kendi
/// profili her durumda) geçerli kalır; admin ayrı uçları kullanır.
/// </remarks>
public static class ExpertVisibility
{
    public static async Task EnsureVisibleAsync(
        IUnitOfWork unitOfWork,
        Guid expertId,
        Guid? viewerUserId,
        CancellationToken cancellationToken)
    {
        if (viewerUserId is not { } viewerId || viewerId == expertId)
        {
            return;
        }

        var viewer = await unitOfWork.Profiles.Query()
            .Where(profile => profile.Id == viewerId)
            .Select(profile => new { profile.Role, profile.ReferredByExpertId })
            .FirstOrDefaultAsync(cancellationToken);
        if (viewer is null || viewer.Role != ProfileRoles.Client || viewer.ReferredByExpertId == expertId)
        {
            return;
        }

        var hasOffered = await unitOfWork.Offers.Query().AnyAsync(
            offer => offer.ExpertId == expertId && offer.Listing!.ClientId == viewerId,
            cancellationToken);
        if (!hasOffered)
        {
            throw new DomainException(ErrorKeys.ExpertNotFound);
        }
    }
}
