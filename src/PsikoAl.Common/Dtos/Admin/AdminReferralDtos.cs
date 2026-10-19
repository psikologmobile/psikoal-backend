namespace PsikoAl.Common.Dtos.Admin;

public sealed record AdminReferralQueryDto(int? Page, int? PerPage);

// ReferredExpertStatus yalnız davet edilen uzmansa dolu; Rewarded davet hakkının
// (REFERRAL_RIGHT) yazılıp yazılmadığıdır — danışan davetlerinde ödül yoktur.
public sealed record AdminReferralDto(
    Guid ReferrerExpertId,
    string ReferrerName,
    string ReferrerCode,
    Guid ReferredUserId,
    string ReferredName,
    string ReferredRole,
    DateTimeOffset ReferredAt,
    string? ReferredExpertStatus,
    bool Rewarded,
    DateTimeOffset? RewardedAt);
