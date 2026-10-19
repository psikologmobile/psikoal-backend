namespace PsikoAl.Common.Dtos.Wallet;

public sealed record WalletTransactionDto(
    Guid Id,
    string Type,
    string TypeLabel,
    long AmountKurus,
    string AmountLabel,
    int FreeRightsDelta,
    string FreeRightsLabel,
    long BalanceAfterKurus,
    string BalanceAfterLabel,
    string Description,
    Guid? ListingId,
    string? ListingTitle,
    Guid? OfferId,
    Guid? PaymentId,
    DateTimeOffset CreatedAt,
    string TimeLabel);
