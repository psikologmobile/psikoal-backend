namespace PsikoAl.Common.Dtos.Offer;

// REVIZE-2 §4: yalnız ŞU ANKİ ücret. Bir sonraki düşüşün zamanı/tutarı bilerek yok.
public sealed record OfferQuoteDto(
    long FeeKurus,
    string FeeLabel,
    long BalanceKurus,
    string BalanceLabel,
    int FreeOfferRights,
    bool WillUseFreeRight,
    long BalanceAfterKurus,
    string BalanceAfterLabel,
    bool Sufficient);
