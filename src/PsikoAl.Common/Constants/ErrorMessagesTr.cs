namespace PsikoAl.Common.Constants;

/// <summary>
/// Hata kodlarının kullanıcıya gösterilebilir Türkçe karşılıkları.
/// </summary>
/// <remarks>
/// Sözleşme kaynağı: psikoal-app / CLAUDE.md — "ApiError.message: kullanıcıya
/// gösterilebilir mesaj (TR)". DomainExceptionMiddleware eskiden `message` alanına
/// hata KODUNU yazıyordu; mobil onu olduğu gibi ekrana bastığı için kullanıcı
/// şifre ekranında "AUTH_INVALID_CREDENTIALS", ilan ekranında
/// "LISTING_MAX_ACTIVE_EXCEEDED" gibi metinler görüyordu. Tek bir ekranın değil,
/// TÜM hata yollarının sorunuydu.
///
/// Mesajlar kullanıcının ne yapacağını söyler; sistemi anlatmaz. Yalnız yönetici
/// akışlarında görünen kodlar (admin onay/red gerekçeleri gibi) da çevrilir —
/// admin de kullanıcıdır.
/// </remarks>
public static class ErrorMessagesTr
{
    private static readonly Dictionary<string, string> Messages = new(StringComparer.Ordinal)
    {
        // --- Kimlik ---
        [ErrorKeys.AuthInvalidCredentials] = "E-posta veya şifre hatalı.",
        [ErrorKeys.AuthEmailAlreadyRegistered] = "Bu e-posta adresi zaten kayıtlı. Giriş yapmayı deneyin.",
        [ErrorKeys.AuthEmailInvalid] = "Geçerli bir e-posta adresi girin.",
        [ErrorKeys.AuthWeakPassword] = "Şifre en az 8 karakter olmalı.",
        [ErrorKeys.AuthEmailNotConfirmed] = "E-posta adresiniz henüz doğrulanmadı. Gelen kutunuzu kontrol edin.",
        [ErrorKeys.AuthRateLimited] = "Çok fazla deneme yaptınız. Bir dakika sonra tekrar deneyin.",
        [ErrorKeys.AuthRefreshTokenInvalid] = "Oturumunuz sona erdi. Tekrar giriş yapın.",
        [ErrorKeys.AuthUserNotFound] = "Kullanıcı bulunamadı.",
        [ErrorKeys.AuthAccountFrozen] = "Hesabınız donduruldu. Destek ekibiyle iletişime geçin.",
        [ErrorKeys.AuthCurrentPasswordInvalid] = "Mevcut şifreniz hatalı.",
        [ErrorKeys.AuthProviderError] = "Kimlik doğrulama servisine şu an ulaşılamıyor. Biraz sonra tekrar deneyin.",
        [ErrorKeys.ResetTokenInvalid] = "Şifre sıfırlama bağlantısının süresi dolmuş ya da geçersiz. Yeni bir bağlantı isteyin.",
        [ErrorKeys.AuthSamePassword] = "Yeni şifreniz eskisiyle aynı olamaz.",
        [ErrorKeys.AccountInactive] = "Hesabınız aktif değil. Yardım için destek ekibiyle iletişime geçin.",
        [ErrorKeys.AuthTermsNotAccepted] = "Devam etmek için Kullanım Koşulları ve Gizlilik Politikası'nı kabul edin.",
        [ErrorKeys.AuthHealthDataConsentRequired] = "Devam etmek için sağlık verilerinizin işlenmesine açık rıza verin.",

        // --- Profil ---
        [ErrorKeys.ProfileNotFound] = "Profil bulunamadı.",
        [ErrorKeys.AdminUserNotFound] = "Bu işlem için yönetici yetkiniz yok.",
        [ErrorKeys.AccountDeletedImmutable] = "Silinmiş bir hesap dondurulamaz ve yeniden etkinleştirilemez.",
        [ErrorKeys.AccountStatusTransitionInvalid] = "Hesap bu işlem için uygun durumda değil. Listeyi yenileyip tekrar deneyin.",

        // --- Uzman ---
        [ErrorKeys.ExpertNotFound] = "Uzman profili bulunamadı. Profilinizi tamamlayın.",
        [ErrorKeys.ExpertProfileAlreadyExists] = "Uzman profiliniz zaten oluşturulmuş.",
        [ErrorKeys.ExpertRoleRequired] = "Bu işlem yalnızca uzman hesapları için geçerli.",
        [ErrorKeys.ExpertRejectionReasonRequired] = "Reddetme gerekçesi yazmanız gerekiyor.",
        [ErrorKeys.ExpertCertificateLimitExceeded] = "En fazla 10 sertifika yükleyebilirsiniz.",
        [ErrorKeys.CertificateNotFound] = "Sertifika bulunamadı. Listeyi yenileyip tekrar deneyin.",

        // --- Dosya ---
        [ErrorKeys.FileTypeNotAllowed] = "Bu dosya türü desteklenmiyor.",
        [ErrorKeys.FileTooLarge] = "Dosya boyutu izin verilen sınırı aşıyor.",
        [ErrorKeys.StorageUploadFailed] = "Dosya yüklenemedi. Tekrar deneyin.",

        // --- Kategori ---
        [ErrorKeys.CategoryNotFound] = "Kategori bulunamadı.",
        [ErrorKeys.CategorySlugAlreadyExists] = "Bu kısa ad başka bir kategoride kullanılıyor.",

        // --- Yorum ---
        [ErrorKeys.ReviewNotFound] = "Değerlendirme bulunamadı.",
        [ErrorKeys.ReviewAlreadyExists] = "Bu eşleşme için değerlendirmenizi zaten bıraktınız.",
        [ErrorKeys.ReviewClientRoleRequired] = "Değerlendirme yalnızca danışan hesapları tarafından bırakılabilir.",
        [ErrorKeys.ReviewRejectionReasonRequired] = "Reddetme gerekçesi yazmanız gerekiyor.",

        // --- İlan ---
        [ErrorKeys.ListingNotFound] = "İlan bulunamadı.",
        [ErrorKeys.ListingClientRoleRequired] = "İlan yalnızca danışan hesapları tarafından açılabilir.",
        [ErrorKeys.ListingMaxActiveExceeded] = "Açık ilan sınırına ulaştınız. Yeni ilan açmak için mevcut ilanlarınızdan birini kapatın.",
        [ErrorKeys.ListingNotOpen] = "Bu ilan artık açık değil.",
        [ErrorKeys.ListingNotPendingApproval] = "Bu ilan onay bekleyen durumda değil.",
        [ErrorKeys.ListingRejectionReasonRequired] = "Reddetme gerekçesi yazmanız gerekiyor.",
        [ErrorKeys.ListingReopenNotExpired] = "Yalnızca süresi dolmuş ilanlar yeniden açılabilir.",
        [ErrorKeys.ListingReopenReasonRequired] = "Yeniden açma gerekçesi yazmanız gerekiyor.",

        // --- Sistem ayarları ---
        [ErrorKeys.SystemSettingNotFound] = "Sistem ayarı bulunamadı.",

        // --- Teklif ---
        [ErrorKeys.OfferNotFound] = "Teklif bulunamadı.",
        [ErrorKeys.OfferAlreadyExists] = "Bu ilana zaten teklif gönderdiniz.",
        [ErrorKeys.OfferNotPending] = "Bu teklif artık yanıtlanabilir durumda değil.",
        [ErrorKeys.OfferExpertRoleRequired] = "Teklif yalnızca uzman hesapları tarafından gönderilebilir.",
        [ErrorKeys.OfferExpertNotApproved] = "Profiliniz onaylanmadan teklif gönderemezsiniz.",
        [ErrorKeys.OfferListingNotOpen] = "Bu ilan teklif almayı kapattı.",

        // --- Eşleşme ---
        [ErrorKeys.MatchNotFound] = "Eşleşme bulunamadı.",
        [ErrorKeys.MatchNotActive] = "Bu eşleşme zaten sonlandırılmış.",
        [ErrorKeys.MatchNotParticipant] = "Bu eşleşmeye erişim yetkiniz yok.",
        [ErrorKeys.MatchForceReleaseReasonRequired] = "Sonlandırma gerekçesi yazmanız gerekiyor.",

        // --- Bildirim ---
        [ErrorKeys.NotificationNotFound] = "Bildirim bulunamadı.",
        [ErrorKeys.NotificationTemplateNotFound] = "Bildirim şablonu bulunamadı.",
        [ErrorKeys.PushTokenNotFound] = "Cihaz kaydı bulunamadı.",

        // --- Test ---
        [ErrorKeys.AssessmentNotFound] = "Test bulunamadı.",
        [ErrorKeys.AssessmentResultNotFound] = "Test sonucu bulunamadı.",
        [ErrorKeys.AssessmentScoreRuleNotFound] = "Bu test için puanlama kuralı tanımlı değil.",
        [ErrorKeys.AssessmentAnswersInvalid] = "Cevaplarınız bu testle eşleşmiyor. Testi baştan çözüp tekrar gönderin.",
        [ErrorKeys.AssessmentResultNotOwned] = "Seçtiğiniz test sonucu hesabınıza ait değil. Kendi sonuçlarınızdan birini seçin.",

        // --- Blog ---
        [ErrorKeys.BlogNotFound] = "Blog yazısı bulunamadı.",
        [ErrorKeys.BlogSlugAlreadyExists] = "Bu kısa ad başka bir blog yazısında kullanılıyor.",
        [ErrorKeys.BlogSlugInvalid] = "Kısa ad yalnız küçük harf, rakam ve tire içerebilir.",
        [ErrorKeys.BlogNotPublishable] = "Yayınlamak için başlık, özet, içerik ve kapak görseli gerekli.",
        [ErrorKeys.BlogScheduleMustBeFuture] = "Planlanan yayın zamanı ileri bir tarih olmalı.",

        // --- Bakiye, teklif ücreti, ödeme ---
        [ErrorKeys.PaymentProviderUnavailable] = "Ödeme servisine şu an ulaşılamıyor. Biraz sonra tekrar deneyin.",
        [ErrorKeys.InsufficientBalance] = "Teklif göndermek için bakiyeniz yetersiz.",
        [ErrorKeys.OfferFeeChanged] = "Teklif ücreti güncellendi. Yeni ücreti kontrol edip tekrar gönderin.",
        [ErrorKeys.OfferNotOnListing] = "Seçtiğiniz teklif bu ilana ait değil.",
        [ErrorKeys.TopupAmountOutOfRange] = "Yükleme tutarı izin verilen aralıkta değil.",
        [ErrorKeys.TopupExpertNotApproved] = "Bakiye yüklemek için uzman profilinizin onaylanması gerekiyor.",
        [ErrorKeys.WalletPaymentNotFound] = "Ödeme bulunamadı.",
        [ErrorKeys.WalletBalanceNegative] = "Bu düzeltme uzmanın bakiyesini ya da hakkını eksiye düşürür.",
        [ErrorKeys.WalletAdjustReasonRequired] = "Düzeltme için gerekçe yazmanız gerekiyor.",
        [ErrorKeys.InvoiceNotFound] = "Bu ödeme için henüz fatura yüklenmedi.",
        [ErrorKeys.InvoicePaymentNotPaid] = "Fatura yalnız tamamlanmış ödemelere yüklenebilir.",

        // --- İlan (Revize 2) ---
        [ErrorKeys.ContactConsentRequired] = "Devam etmek için iletişim bilgilerinizin teklif veren uzmanlarla paylaşılmasına onay verin.",
        [ErrorKeys.PhoneRequired] = "Uzmanların size ulaşabilmesi için telefon numaranızı girin.",
        [ErrorKeys.ListingBudgetOutOfRange] = "Bütçeniz izin verilen aralıkta değil.",
        [ErrorKeys.ListingNotOwner] = "Bu ilan size ait değil.",

        // --- Anlaşma bildirimi ---
        [ErrorKeys.ClaimAlreadyExists] = "Bu ilan için anlaşma bildiriminizi zaten gönderdiniz.",
        [ErrorKeys.ClaimNotAllowed] = "Bu ilan için anlaşma bildirimi gönderemezsiniz.",
        [ErrorKeys.ClaimNotFound] = "Anlaşma bildirimi bulunamadı.",
        [ErrorKeys.ClaimAlreadyResolved] = "Bu bildirim zaten çözüldü.",

        // --- Genel ---
        [ErrorKeys.ValidationFailed] = "Girdiğiniz bilgileri kontrol edin.",
        [ErrorKeys.ResourceConflict] = "Bu kayıt zaten mevcut. Sayfayı yenileyip tekrar deneyin.",
        [ErrorKeys.InternalError] = "Beklenmeyen bir hata oluştu. Tekrar deneyin.",
    };

    /// <summary>
    /// Kodun Türkçe karşılığını verir; eşleşme yoksa genel bir mesaja düşer.
    /// </summary>
    /// <remarks>
    /// Kodun kendisi ASLA geri döndürülmez: yeni bir ErrorKey eklenip buraya
    /// yazılmadığında kullanıcı yine ham kod görürdü ve hata sessizce geri gelirdi.
    /// Genel mesaja düşmek, eksikliği kullanıcıya yansıtmadan gizler; eksikliği
    /// ErrorMessagesTrTests yakalar.
    /// </remarks>
    public static string For(string errorKey)
        => Messages.TryGetValue(errorKey, out var message)
            ? message
            : Messages[ErrorKeys.InternalError];
}
