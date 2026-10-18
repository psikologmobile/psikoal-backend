namespace PsikoAl.Common.Dtos.Claim;

public sealed record ClaimDto(
    Guid Id,
    Guid ListingId,
    Guid ExpertId,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DismissedAt);
