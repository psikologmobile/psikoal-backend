namespace PsikoAl.Data.Entities;

public sealed class ListingClaim
{
    public Guid Id { get; set; }

    public Guid ListingId { get; set; }

    public Guid ExpertId { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DismissedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public Guid? ResolvedBy { get; set; }

    public string? ResolutionNote { get; set; }

    public Listing? Listing { get; set; }

    public Expert? Expert { get; set; }

    public AdminUser? Resolver { get; set; }
}
