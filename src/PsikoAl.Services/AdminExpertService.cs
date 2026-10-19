using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Common.Dtos.Expert.Update;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Mapping;

namespace PsikoAl.Services;

public sealed class AdminExpertService(
    IUnitOfWork unitOfWork,
    IAdminGuard adminGuard,
    INotificationService notificationService,
    IWalletService walletService) : IAdminExpertService
{
    private static readonly JsonSerializerOptions RevisionJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AdminExpertListItemDto>> ListAsync(string? status, CancellationToken cancellationToken)
    {
        var query = unitOfWork.Experts.Query();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = status == "revision_pending"
                ? query.Where(expert => expert.PendingRevision != null)
                : query.Where(expert => expert.Status == status);
        }

        return await query
            .OrderByDescending(expert => expert.CreatedAt)
            .Select(expert => new AdminExpertListItemDto(
                expert.Id,
                expert.Profile!.FirstName + " " + expert.Profile.LastName,
                expert.Profile.Email,
                expert.Title,
                expert.Status,
                expert.Profile.IsVerified,
                expert.PendingRevision != null,
                expert.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminExpertDetailDto> GetDetailAsync(Guid expertId, CancellationToken cancellationToken)
    {
        var expert = await GetRequiredExpertAsync(expertId, cancellationToken);
        var profile = expert.Profile ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        var pendingRevision = expert.PendingRevision is null
            ? null
            : JsonSerializer.Deserialize<UpdateExpertProfileDto>(expert.PendingRevision, RevisionJsonOptions);

        var rating = await unitOfWork.Reviews.GetRatingAsync(expert.Id, cancellationToken);
        var reviewCount = await unitOfWork.Reviews.GetReviewCountAsync(expert.Id, cancellationToken);

        return new AdminExpertDetailDto(
            ExpertMapper.ToExpertDto(expert, profile, rating, reviewCount, isOwnProfile: false),
            profile.Email,
            expert.RejectionReason,
            expert.ApprovedAt,
            pendingRevision);
    }

    public async Task ApproveAsync(Guid actorAuthUserId, Guid expertId, CancellationToken cancellationToken)
    {
        var actor = await GetRequiredActorAsync(actorAuthUserId, cancellationToken);
        var expert = await GetRequiredExpertAsync(expertId, cancellationToken);
        var oldSnapshot = SnapshotOf(expert);

        if (expert.Status == ExpertStatuses.Approved && expert.PendingRevision is not null)
        {
            // Bekleyen revizyon onayı: değişiklikler canlı satıra uygulanır.
            var revision = JsonSerializer.Deserialize<UpdateExpertProfileDto>(expert.PendingRevision, RevisionJsonOptions);
            if (revision is not null)
            {
                ExpertService.ApplyUpdate(expert, revision);
            }

            expert.PendingRevision = null;
        }

        expert.Status = ExpertStatuses.Approved;
        expert.RejectionReason = null;
        expert.ApprovedAt = DateTimeOffset.UtcNow;
        expert.ApprovedBy = actor.Id;

        await AddAuditAsync(actor.Id, "admin.expert_approve", expert.Id, oldSnapshot, SnapshotOf(expert), null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyAsync(
            expert.Id,
            NotificationTypes.ExpertApproved,
            new Dictionary<string, string>(),
            dataJson: null,
            cancellationToken);

        // Hoş geldin hakkı + davet edene ödül (REVIZE-2 §2.3). İkisi de idempotent: revizyon
        // onayı ya da red sonrası yeniden onay yeni hak doğurmaz.
        await walletService.GrantApprovalRewardsAsync(expert.Id, cancellationToken);
    }

    public async Task RejectAsync(Guid actorAuthUserId, Guid expertId, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(ErrorKeys.ExpertRejectionReasonRequired, "reason");
        }

        var actor = await GetRequiredActorAsync(actorAuthUserId, cancellationToken);
        var expert = await GetRequiredExpertAsync(expertId, cancellationToken);
        var oldSnapshot = SnapshotOf(expert);

        if (expert.Status == ExpertStatuses.Approved && expert.PendingRevision is not null)
        {
            // Revizyon reddi: yayındaki onaylı profil korunur, yalnızca bekleyen değişiklik düşer.
            expert.PendingRevision = null;
            expert.RejectionReason = reason;
        }
        else
        {
            expert.Status = ExpertStatuses.Rejected;
            expert.RejectionReason = reason;
        }

        await AddAuditAsync(actor.Id, "admin.expert_reject", expert.Id, oldSnapshot, SnapshotOf(expert), reason, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyAsync(
            expert.Id,
            NotificationTypes.ExpertRejected,
            new Dictionary<string, string> { ["reason"] = reason },
            dataJson: null,
            cancellationToken);
    }

    public async Task SetVerifiedAsync(Guid actorAuthUserId, Guid expertId, bool isVerified, CancellationToken cancellationToken)
    {
        var actor = await GetRequiredActorAsync(actorAuthUserId, cancellationToken);
        var expert = await GetRequiredExpertAsync(expertId, cancellationToken);
        var profile = expert.Profile ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        var oldValue = JsonSerializer.Serialize(new { isVerified = profile.IsVerified });
        profile.IsVerified = isVerified;

        await AddAuditAsync(
            actor.Id,
            "admin.expert_set_verified",
            expert.Id,
            oldValue,
            JsonSerializer.Serialize(new { isVerified }),
            null,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<AdminUser> GetRequiredActorAsync(Guid actorAuthUserId, CancellationToken cancellationToken)
        => await adminGuard.GetActiveAdminAsync(actorAuthUserId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.AdminUserNotFound);

    private async Task<Expert> GetRequiredExpertAsync(Guid expertId, CancellationToken cancellationToken)
        => await unitOfWork.Experts.GetWithProfileAsync(expertId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ExpertNotFound);

    private static string SnapshotOf(Expert expert)
        => JsonSerializer.Serialize(new
        {
            status = expert.Status,
            hasPendingRevision = expert.PendingRevision != null,
        });

    private async Task AddAuditAsync(
        Guid adminUserId,
        string action,
        Guid expertId,
        string oldValue,
        string newValue,
        string? reason,
        CancellationToken cancellationToken)
        => await unitOfWork.AuditLogs.AddAsync(
            new AuditLog
            {
                AdminUserId = adminUserId,
                ActorType = AuditActorTypes.Admin,
                Action = action,
                EntityType = "expert",
                EntityId = expertId.ToString(),
                OldValue = oldValue,
                NewValue = newValue,
                Reason = reason,
            },
            cancellationToken);
}
