namespace PsikoAl.Common.Dtos.Admin;

public sealed record AdminWalletPaymentQueryDto(string? Status, bool? HasInvoice, int? Page, int? PerPage);

public sealed record AdminInvoiceDto(string FileName, DateTimeOffset UploadedAt, string? UploadedByName);

public sealed record AdminWalletPaymentDto(
    Guid Id,
    string MerchantOid,
    Guid ExpertId,
    string ExpertName,
    string ExpertEmail,
    long AmountKurus,
    string AmountLabel,
    string Status,
    string StatusLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    AdminInvoiceDto? Invoice);

public sealed record AdminWalletTransactionDto(
    Guid Id,
    string Type,
    string TypeLabel,
    long AmountKurus,
    string AmountLabel,
    int FreeRightsDelta,
    string FreeRightsLabel,
    long BalanceAfterKurus,
    string BalanceAfterLabel,
    int FreeRightsAfter,
    string Description,
    string? Reason,
    Guid? ListingId,
    Guid? OfferId,
    Guid? PaymentId,
    string? AdminName,
    DateTimeOffset CreatedAt);

public sealed record AdminExpertWalletDto(
    Guid ExpertId,
    string FullName,
    string Email,
    long BalanceKurus,
    string BalanceLabel,
    int FreeOfferRights,
    string ReferralCode,
    AdminPagedResultDto<AdminWalletTransactionDto> Transactions);

// Hediye: bakiye ve/veya hak; ikisi de >= 0 ve en az biri > 0. Gerekçe isteğe bağlı.
public sealed record AdminWalletGiftDto(long AmountKurus, int FreeRights, string? Reason);

// Düzeltme: işaretli tutar ve hak; en az biri ≠ 0; gerekçe ZORUNLU.
public sealed record AdminWalletAdjustDto(long AmountKurus, int FreeRightsDelta, string? Reason);
