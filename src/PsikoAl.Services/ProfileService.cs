using System.Text.Json;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Auth;
using PsikoAl.Common.Dtos.Auth.Update;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Mapping;

namespace PsikoAl.Services;

public sealed class ProfileService(
    IUnitOfWork unitOfWork,
    ISupabaseAdminService supabaseAdmin,
    IAccountStatusService accountStatus,
    IAccountErasureService accountErasure) : IProfileService
{
    public async Task<AuthUserDto> GetMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await GetRequiredProfileAsync(userId, cancellationToken);
        return ProfileMapper.ToAuthUserDto(profile);
    }

    // REVIZE-2 §6: danışanı davet eden uzman (yoksa null). Uzman satırı silinmişse FK null'a
    // düştüğü için sonuç da null olur.
    public async Task<ReferrerDto?> GetReferrerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await GetRequiredProfileAsync(userId, cancellationToken);
        if (profile.ReferredByExpertId is not { } expertId)
        {
            return null;
        }

        var expert = await unitOfWork.Experts.GetWithProfileAsync(expertId, cancellationToken);
        return expert is { Profile: { } expertProfile }
            ? new ReferrerDto(
                expert.Id,
                $"{expertProfile.FirstName} {expertProfile.LastName}".Trim(),
                expert.Title,
                expertProfile.AvatarUrl,
                ExpertMapper.InitialsOf(expertProfile.FirstName, expertProfile.LastName))
            : null;
    }

    public async Task<AuthUserDto> UpdateMeAsync(Guid userId, UpdateProfileDto request, CancellationToken cancellationToken)
    {
        var profile = await GetRequiredProfileAsync(userId, cancellationToken);

        if (request.FirstName is not null)
        {
            profile.FirstName = request.FirstName;
        }

        if (request.LastName is not null)
        {
            profile.LastName = request.LastName;
        }

        if (request.Phone is not null)
        {
            profile.Phone = request.Phone.Length == 0 ? null : request.Phone;
        }

        if (request.City is not null)
        {
            profile.City = request.City.Length == 0 ? null : request.City;
        }

        if (request.ShareEmail.HasValue)
        {
            profile.ShareEmail = request.ShareEmail.Value;
        }

        if (request.SharePhone.HasValue)
        {
            profile.SharePhone = request.SharePhone.Value;
        }

        if (request.ShareLocation.HasValue)
        {
            profile.ShareLocation = request.ShareLocation.Value;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ProfileMapper.ToAuthUserDto(profile);
    }

    public async Task FreezeMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await GetRequiredProfileAsync(userId, cancellationToken);
        var oldStatus = profile.Status;

        profile.Status = ProfileStatuses.Frozen;
        await supabaseAdmin.BanUserAsync(userId, cancellationToken);
        await AddStatusAuditAsync(profile.Id, "user.freeze_self", oldStatus, profile.Status, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        accountStatus.Invalidate(userId);
    }

    public Task DeleteMeAsync(Guid userId, CancellationToken cancellationToken)
        => accountErasure.EraseAsync(userId, cancellationToken);

    private async Task<Profile> GetRequiredProfileAsync(Guid userId, CancellationToken cancellationToken)
        => await unitOfWork.Profiles.GetByIdAsync(userId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ProfileNotFound);

    private async Task AddStatusAuditAsync(
        Guid profileId,
        string action,
        string oldStatus,
        string newStatus,
        CancellationToken cancellationToken)
        => await unitOfWork.AuditLogs.AddAsync(
            new AuditLog
            {
                ActorType = AuditActorTypes.User,
                Action = action,
                EntityType = "profile",
                EntityId = profileId.ToString(),
                OldValue = JsonSerializer.Serialize(new { status = oldStatus }),
                NewValue = JsonSerializer.Serialize(new { status = newStatus }),
            },
            cancellationToken);
}
