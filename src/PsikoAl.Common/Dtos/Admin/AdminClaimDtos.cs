namespace PsikoAl.Common.Dtos.Admin;

public sealed record AdminClaimQueryDto(string? Status, int? Page, int? PerPage);

public sealed record AdminClaimDto(
    Guid Id,
    Guid ListingId,
    string ListingTitle,
    string ListingStatus,
    Guid ExpertId,
    string ExpertName,
    Guid ClientId,
    string ClientName,
    string? Note,
    string Status,
    string StatusLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DismissedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedByName,
    string? ResolutionNote);

public sealed record AdminResolveClaimDto(string? Note);
