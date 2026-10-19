using PsikoAl.Common.Constants;

namespace PsikoAl.Data.Entities;

public sealed class Offer
{
    public Guid Id { get; set; }

    public Guid ListingId { get; set; }

    public Guid ExpertId { get; set; }

    public string? Title { get; set; }

    public decimal Price { get; set; }

    public string Description { get; set; } = string.Empty;

    public required string SessionType { get; set; }

    public string Status { get; set; } = OfferStatuses.Pending;

    public Guid? MatchId { get; set; }

    public long FeePaidKurus { get; set; }

    public bool UsedFreeRight { get; set; }

    public string? ClosedReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Listing? Listing { get; set; }

    public Expert? Expert { get; set; }
}
