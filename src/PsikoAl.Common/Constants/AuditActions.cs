namespace PsikoAl.Common.Constants;

public static class AuditActions
{
    public const string WalletGift = "admin.wallet_gift";
    public const string WalletAdjust = "admin.wallet_adjust";
    public const string InvoiceUpload = "admin.invoice_upload";
    public const string PricingPeriodCreate = "admin.pricing_period_create";
    public const string ClaimResolve = "admin.claim_resolve";
    public const string NotificationSend = "admin.notification_send";
    public const string NotificationTemplateUpdate = "admin.notification_template_update";
    public const string ReviewUnpublish = "admin.review_unpublish";

    // Kullanıcının kendi eylemi (actor_type = 'user'); admin işlem kaydındaki "giriş" olayları.
    public const string UserLogin = "user.login";
}
