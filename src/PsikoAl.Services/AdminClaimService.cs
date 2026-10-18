using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Wallet;

namespace PsikoAl.Services;

// Admin "Anlaşma bildirimleri" (REVIZE-2 §3.5, §9). Durum TÜRETİLİR (ClaimStatuses):
// bildirimden bu yana CLAIM_ADMIN_REVIEW_DAYS geçtiği hâlde ilan eşleşmediyse
// "İnceleme bekliyor". Ayar değişince liste geriye dönük de yeniden hesaplanır.
public sealed class AdminClaimService(
    IUnitOfWork unitOfWork,
    IAdminGuard adminGuard,
    TimeProvider timeProvider) : IAdminClaimService
{
    private static readonly Dictionary<string, string> StatusLabels = new(StringComparer.Ordinal)
    {
        [ClaimStatuses.Open] = "Açık",
        [ClaimStatuses.ReviewPending] = "İnceleme bekliyor",
        [ClaimStatuses.Resolved] = "Çözüldü",
    };

    public async Task<AdminPagedResultDto<AdminClaimDto>> ListAsync(AdminClaimQueryDto query, CancellationToken cancellationToken)
    {
        var cutoff = await ReviewCutoffAsync(cancellationToken);
        var claims = unitOfWork.ListingClaims.Query();

        claims = query.Status switch
        {
            ClaimStatuses.Resolved => claims.Where(claim => claim.ResolvedAt != null),
            ClaimStatuses.ReviewPending => claims.Where(IsReviewPending(cutoff)),
            ClaimStatuses.Open => claims.Where(claim => claim.ResolvedAt == null).Where(Not(IsReviewPending(cutoff))),
            _ => claims,
        };

        var (rows, meta) = await claims
            .OrderByDescending(claim => claim.CreatedAt)
            .ThenByDescending(claim => claim.Id)
            .Select(claim => new
            {
                Claim = claim,
                ListingTitle = claim.Listing!.Title,
                ListingStatus = claim.Listing.Status,
                ClientId = claim.Listing.ClientId,
                ClientName = claim.Listing.Client!.FirstName + " " + claim.Listing.Client.LastName,
                ExpertName = claim.Expert!.Profile!.FirstName + " " + claim.Expert.Profile.LastName,
                ResolverName = claim.Resolver != null ? claim.Resolver.DisplayName : null,
            })
            .ToPageAsync(query.Page, query.PerPage, cancellationToken);

        return new AdminPagedResultDto<AdminClaimDto>(
            [.. rows.Select(row =>
            {
                var status = StatusOf(row.Claim, row.ListingStatus, cutoff);
                return new AdminClaimDto(
                    row.Claim.Id,
                    row.Claim.ListingId,
                    row.ListingTitle,
                    row.ListingStatus,
                    row.Claim.ExpertId,
                    row.ExpertName.Trim(),
                    row.ClientId,
                    row.ClientName.Trim(),
                    row.Claim.Note,
                    status,
                    StatusLabels[status],
                    row.Claim.CreatedAt,
                    row.Claim.DismissedAt,
                    row.Claim.ResolvedAt,
                    row.ResolverName,
                    row.Claim.ResolutionNote);
            })],
            meta);
    }

    public async Task ResolveAsync(Guid actorAuthUserId, Guid claimId, string? note, CancellationToken cancellationToken)
    {
        var actor = await adminGuard.GetActiveAdminAsync(actorAuthUserId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.AdminUserNotFound);

        var resolution = note?.Trim();
        if (string.IsNullOrEmpty(resolution) || resolution.Length > WalletFieldLimits.ReasonMaxLength)
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "note");
        }

        var claim = await unitOfWork.ListingClaims.GetByIdAsync(claimId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ClaimNotFound);

        if (claim.ResolvedAt is not null)
        {
            throw new DomainException(ErrorKeys.ClaimAlreadyResolved);
        }

        claim.ResolvedAt = timeProvider.GetUtcNow();
        claim.ResolvedBy = actor.Id;
        claim.ResolutionNote = resolution;

        await unitOfWork.AuditLogs.AddAsync(
            new AuditLog
            {
                AdminUserId = actor.Id,
                ActorType = AuditActorTypes.Admin,
                Action = AuditActions.ClaimResolve,
                EntityType = "listing_claim",
                EntityId = claim.Id.ToString(),
                NewValue = JsonSerializer.Serialize(new { listingId = claim.ListingId, expertId = claim.ExpertId }),
                Reason = resolution,
            },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    internal static string StatusOf(ListingClaim claim, string listingStatus, DateTimeOffset cutoff)
    {
        if (claim.ResolvedAt is not null)
        {
            return ClaimStatuses.Resolved;
        }

        return listingStatus != ListingStatuses.Matched && claim.CreatedAt <= cutoff
            ? ClaimStatuses.ReviewPending
            : ClaimStatuses.Open;
    }

    private async Task<DateTimeOffset> ReviewCutoffAsync(CancellationToken cancellationToken)
    {
        var days = await SettingValues.GetIntAsync(
            unitOfWork.SystemSettings,
            SystemSettingKeys.ClaimAdminReviewDays,
            SettingValues.DefaultClaimAdminReviewDays,
            cancellationToken);
        return timeProvider.GetUtcNow().AddDays(-days);
    }

    private static Expression<Func<ListingClaim, bool>> IsReviewPending(DateTimeOffset cutoff)
        => claim => claim.ResolvedAt == null
            && claim.Listing!.Status != ListingStatuses.Matched
            && claim.CreatedAt <= cutoff;

    private static Expression<Func<ListingClaim, bool>> Not(Expression<Func<ListingClaim, bool>> predicate)
        => Expression.Lambda<Func<ListingClaim, bool>>(Expression.Not(predicate.Body), predicate.Parameters);
}
