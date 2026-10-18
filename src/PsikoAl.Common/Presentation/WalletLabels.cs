using PsikoAl.Common.Constants;

namespace PsikoAl.Common.Presentation;

public static class WalletLabels
{
    private static readonly Dictionary<string, string> TypeLabels = new(StringComparer.Ordinal)
    {
        [WalletTransactionTypes.Topup] = "Bakiye yükleme",
        [WalletTransactionTypes.OfferFee] = "Teklif ücreti",
        [WalletTransactionTypes.OfferFreeRight] = "Ücretsiz hak kullanıldı",
        [WalletTransactionTypes.WelcomeRight] = "Hoş geldin hakkı",
        [WalletTransactionTypes.ReferralRight] = "Davet hakkı",
        [WalletTransactionTypes.AdminGift] = "Hediye",
        [WalletTransactionTypes.AdminAdjust] = "Düzeltme",
    };

    private static readonly Dictionary<string, string> PaymentStatusLabels = new(StringComparer.Ordinal)
    {
        [WalletPaymentStatuses.Pending] = "Bekliyor",
        [WalletPaymentStatuses.Paid] = "Ödendi",
        [WalletPaymentStatuses.Failed] = "Başarısız",
    };

    public static string TypeLabel(string type) => TypeLabels.GetValueOrDefault(type, type);

    public static string PaymentStatusLabel(string status) => PaymentStatusLabels.GetValueOrDefault(status, status);

    /// <summary>Defter satırının Türkçe açıklaması (uzmanın "Hareketler" ekranı).</summary>
    public static string Description(string type, string? listingTitle, string? reason)
        => type switch
        {
            WalletTransactionTypes.Topup => "Bakiye yüklendi",
            WalletTransactionTypes.OfferFee => listingTitle is null
                ? "Teklif ücreti"
                : $"\"{listingTitle}\" ilanına teklif ücreti",
            WalletTransactionTypes.OfferFreeRight => listingTitle is null
                ? "Ücretsiz hakla teklif"
                : $"\"{listingTitle}\" ilanına ücretsiz hakla teklif",
            WalletTransactionTypes.WelcomeRight => "Profiliniz onaylandı, hoş geldin hakkı tanımlandı",
            WalletTransactionTypes.ReferralRight => "Davet ettiğiniz uzman onaylandı",
            WalletTransactionTypes.AdminGift => string.IsNullOrWhiteSpace(reason) ? "Hediye" : $"Hediye: {reason}",
            WalletTransactionTypes.AdminAdjust => string.IsNullOrWhiteSpace(reason) ? "Düzeltme" : $"Düzeltme: {reason}",
            _ => TypeLabel(type),
        };
}
