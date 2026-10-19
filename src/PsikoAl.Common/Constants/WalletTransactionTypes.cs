namespace PsikoAl.Common.Constants;

// wallet_transactions.type — veritabanındaki CHECK kısıtıyla birebir.
public static class WalletTransactionTypes
{
    public const string Topup = "TOPUP";
    public const string OfferFee = "OFFER_FEE";
    public const string OfferFreeRight = "OFFER_FREE_RIGHT";
    public const string WelcomeRight = "WELCOME_RIGHT";
    public const string ReferralRight = "REFERRAL_RIGHT";
    public const string AdminGift = "ADMIN_GIFT";
    public const string AdminAdjust = "ADMIN_ADJUST";

    public static readonly IReadOnlyList<string> All =
        [Topup, OfferFee, OfferFreeRight, WelcomeRight, ReferralRight, AdminGift, AdminAdjust];
}
