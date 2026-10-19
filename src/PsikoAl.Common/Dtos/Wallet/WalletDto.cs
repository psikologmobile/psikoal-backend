namespace PsikoAl.Common.Dtos.Wallet;

public sealed record WalletDto(
    long BalanceKurus,
    string BalanceLabel,
    int FreeOfferRights,
    string ReferralCode,
    string ReferralLink);
