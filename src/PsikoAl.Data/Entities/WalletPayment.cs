using PsikoAl.Common.Constants;

namespace PsikoAl.Data.Entities;

public sealed class WalletPayment
{
    public Guid Id { get; set; }

    public Guid ExpertId { get; set; }

    public required string MerchantOid { get; set; }

    public long AmountKurus { get; set; }

    public string Status { get; set; } = WalletPaymentStatuses.Pending;

    public string? ProviderPayload { get; set; }

    public string? InvoicePath { get; set; }

    public string? InvoiceFileName { get; set; }

    public DateTimeOffset? InvoiceUploadedAt { get; set; }

    public Guid? InvoiceUploadedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public Expert? Expert { get; set; }

    public AdminUser? InvoiceUploader { get; set; }
}
