namespace PsikoAl.Common.Dtos.Listing;

public sealed record ListingClaimDto(Guid ExpertId, string ExpertName, DateTimeOffset CreatedAt);
