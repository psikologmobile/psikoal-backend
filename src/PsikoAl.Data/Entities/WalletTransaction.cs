namespace PsikoAl.Data.Entities;

public sealed class WalletTransaction
{
    public Guid Id { get; set; }

    public Guid ExpertId { get; set; }

    public long AmountKurus { get; set; }

    public long BalanceAfterKurus { get; set; }

    public int FreeRightsDelta { get; set; }

    public int FreeRightsAfter { get; set; }

    public required string Type { get; set; }

    public Guid? ListingId { get; set; }

    public Guid? OfferId { get; set; }

    public Guid? PaymentId { get; set; }

    public Guid? AdminUserId { get; set; }

    public Guid? ReferredExpertId { get; set; }

    public string? Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Listing? Listing { get; set; }

    public AdminUser? AdminUser { get; set; }
}
