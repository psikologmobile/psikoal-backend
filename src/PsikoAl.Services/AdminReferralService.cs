using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Services;

// Admin "Davetler" (REVIZE-2 §7, §9): kim kimi davet etti, ödül yazıldı mı. Kaynak
// profiles.referred_by_expert_id; ödül wallet_transactions'taki REFERRAL_RIGHT satırı.
public sealed class AdminReferralService(IUnitOfWork unitOfWork) : IAdminReferralService
{
    public async Task<AdminPagedResultDto<AdminReferralDto>> ListAsync(
        AdminReferralQueryDto query,
        CancellationToken cancellationToken)
    {
        var referrals =
            from referred in unitOfWork.Profiles.Query()
            where referred.ReferredByExpertId != null
            join referrer in unitOfWork.Experts.Query() on referred.ReferredByExpertId equals referrer.Id
            select new
            {
                ReferrerId = referrer.Id,
                ReferrerName = referrer.Profile!.FirstName + " " + referrer.Profile.LastName,
                ReferrerCode = referrer.ReferralCode,
                ReferredId = referred.Id,
                ReferredName = referred.FirstName + " " + referred.LastName,
                ReferredRole = referred.Role,
                ReferredAt = referred.CreatedAt,
                ReferredExpertStatus = unitOfWork.Experts.Query()
                    .Where(expert => expert.Id == referred.Id)
                    .Select(expert => expert.Status)
                    .FirstOrDefault(),
                RewardedAt = unitOfWork.WalletTransactions.Query()
                    .Where(transaction => transaction.Type == WalletTransactionTypes.ReferralRight
                        && transaction.ReferredExpertId == referred.Id)
                    .Select(transaction => (DateTimeOffset?)transaction.CreatedAt)
                    .FirstOrDefault(),
            };

        var (rows, meta) = await referrals
            .OrderByDescending(row => row.ReferredAt)
            .ThenBy(row => row.ReferredId)
            .ToPageAsync(query.Page, query.PerPage, cancellationToken);

        return new AdminPagedResultDto<AdminReferralDto>(
            [.. rows.Select(row => new AdminReferralDto(
                row.ReferrerId,
                row.ReferrerName.Trim(),
                row.ReferrerCode ?? string.Empty,
                row.ReferredId,
                row.ReferredName.Trim(),
                row.ReferredRole,
                row.ReferredAt,
                row.ReferredExpertStatus,
                row.RewardedAt is not null,
                row.RewardedAt))],
            meta);
    }
}
