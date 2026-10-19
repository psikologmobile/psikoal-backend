namespace PsikoAl.Common.Constants;

public static class NotificationTypes
{
    public const string OfferReceived = "OFFER_RECEIVED";

    // REVIZE-2 §8: artık ÜRETİLMİYOR (yerine MATCHED). Eski kayıtlar ve şablon için duruyor.
    public const string OfferAccepted = "OFFER_ACCEPTED";
    public const string ListingExpiring = "LISTING_EXPIRING";
    public const string ListingApproved = "LISTING_APPROVED";
    public const string ListingRejected = "LISTING_REJECTED";
    public const string ExpertApproved = "EXPERT_APPROVED";
    public const string ExpertRejected = "EXPERT_REJECTED";
    public const string ReviewApproved = "REVIEW_APPROVED";
    public const string ReviewRejected = "REVIEW_REJECTED";
    public const string Matched = "MATCHED";
    public const string ListingMatchedOther = "LISTING_MATCHED_OTHER";
    public const string ClaimReminder = "CLAIM_REMINDER";
    public const string InvitedClientListing = "INVITED_CLIENT_LISTING";
    public const string BalanceTopup = "BALANCE_TOPUP";
    public const string AdminGift = "ADMIN_GIFT";
    public const string WelcomeRights = "WELCOME_RIGHTS";
    public const string ReferralReward = "REFERRAL_REWARD";
    public const string InvoiceReady = "INVOICE_READY";
    public const string System = "SYSTEM";

    public static readonly IReadOnlyList<string> All =
    [
        OfferReceived, OfferAccepted, ListingExpiring, ListingApproved, ListingRejected,
        ExpertApproved, ExpertRejected, ReviewApproved, ReviewRejected, Matched, ListingMatchedOther,
        ClaimReminder, InvitedClientListing, BalanceTopup, AdminGift, WelcomeRights, ReferralReward,
        InvoiceReady, System,
    ];
}
