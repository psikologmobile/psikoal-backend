namespace PsikoAl.Common.Constants;

// Admin "Kullanıcı işlem kaydı" zaman çizelgesindeki olay türleri (REVIZE-2 §9).
public static class ActivityKinds
{
    public const string Registration = "REGISTRATION";
    public const string Login = "LOGIN";
    public const string Listing = "LISTING";
    public const string Offer = "OFFER";
    public const string Match = "MATCH";
    public const string Wallet = "WALLET";
    public const string Notification = "NOTIFICATION";
    public const string Claim = "CLAIM";
    public const string Admin = "ADMIN";

    public static readonly IReadOnlyList<string> All =
        [Registration, Login, Listing, Offer, Match, Wallet, Notification, Claim, Admin];
}
