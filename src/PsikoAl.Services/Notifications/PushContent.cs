using System.Text.Json;
using PsikoAl.Common.Constants;

namespace PsikoAl.Services.Notifications;

/// <summary>Kilit ekranında görünen push metni ve dokunuş verisi.</summary>
/// <remarks>
/// Sözleşme §7: push gövdesi sağlık bilgisi sızdırmaz. Uygulama içi bildirim kaydı
/// şablondan ayrıntılı üretilir (uzman adı, ilan başlığı), ama push metni türe göre
/// GENEL bir cümledir — ilan başlığı ("Depresyon için destek arıyorum") kilit ekranında
/// görünmez. `data` yalnız { type, entityId } taşır; uygulama yönlendirmeyi bundan yapar.
/// SYSTEM türü yöneticinin elle yazdığı duyurudur ve olduğu gibi gider.
/// </remarks>
public static class PushContent
{
    private static readonly (string Title, string Body) Fallback = ("Psikosirius", "Yeni bir bildiriminiz var.");

    private static readonly Dictionary<string, (string Title, string Body)> GenericTexts = new(StringComparer.Ordinal)
    {
        [NotificationTypes.OfferReceived] = ("Yeni teklif", "İlanınıza yeni bir teklif geldi."),
        [NotificationTypes.OfferAccepted] = ("Teklifiniz kabul edildi", "Bir teklifiniz kabul edildi. Ayrıntılar uygulamada."),
        [NotificationTypes.ListingExpiring] = ("İlan süresi doluyor", "İlanlarınızdan birinin süresi dolmak üzere."),
        [NotificationTypes.ListingApproved] = ("İlanınız yayında", "İlanınız onaylandı ve uzmanlara açıldı."),
        [NotificationTypes.ListingRejected] = ("İlan güncellemesi", "İlanınızla ilgili bir güncelleme var."),
        [NotificationTypes.ExpertApproved] = ("Profiliniz onaylandı", "Uzman profiliniz onaylandı."),
        [NotificationTypes.ExpertRejected] = ("Profil güncellemesi", "Uzman profilinizle ilgili bir güncelleme var."),
        [NotificationTypes.ReviewApproved] = ("Değerlendirme güncellemesi", "Değerlendirmenizle ilgili bir güncelleme var."),
        [NotificationTypes.ReviewRejected] = ("Değerlendirme güncellemesi", "Değerlendirmenizle ilgili bir güncelleme var."),
        [NotificationTypes.Matched] = ("Yeni eşleşme", "Bir danışan sizinle eşleştiğini bildirdi."),
        [NotificationTypes.ListingMatchedOther] = ("İlan güncellemesi", "Teklif verdiğiniz bir ilan başka bir uzmanla eşleşti."),
        [NotificationTypes.ClaimReminder] = ("Hatırlatma", "Bir uzman sizinle anlaştığını bildirdi. Ayrıntılar uygulamada."),
        [NotificationTypes.InvitedClientListing] = ("Davetiniz ilan açtı", "Davet ettiğiniz bir danışanın ilanı yayında."),
        [NotificationTypes.BalanceTopup] = ("Bakiyeniz yüklendi", "Bakiye yüklemeniz tamamlandı."),
        [NotificationTypes.AdminGift] = ("Hediye", "Hesabınıza bir hediye tanımlandı."),
        [NotificationTypes.WelcomeRights] = ("Hoş geldiniz", "Ücretsiz teklif hakkınız hesabınıza tanımlandı."),
        [NotificationTypes.ReferralReward] = ("Davetiniz kazandırdı", "Ücretsiz teklif hakkı kazandınız."),
        [NotificationTypes.InvoiceReady] = ("Faturanız hazır", "Bir ödemenizin faturası yüklendi."),
    };

    public static (string Title, string Body) TextFor(string type, string renderedTitle, string renderedBody)
        => type == NotificationTypes.System
            ? (renderedTitle, renderedBody)
            : GenericTexts.GetValueOrDefault(type, Fallback);

    /// <summary>{ type, entityId } — entityId bildirim verisindeki ilk kimliktir (listingId, matchId...).</summary>
    public static string DataFor(string type, string? dataJson)
        => JsonSerializer.Serialize(new { type, entityId = EntityIdFrom(dataJson) });

    // Bildirimin hedef kaydı: veri nesnesindeki ilk metin değeri (offerId, listingId, matchId…).
    // Push ve uygulama içi liste aynı kuralı kullanır; dokunuş iki yerden de aynı ekrana gider.
    public static string? EntityIdFrom(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return document.RootElement.EnumerateObject()
                .Where(property => property.Value.ValueKind == JsonValueKind.String)
                .Select(property => property.Value.GetString())
                .FirstOrDefault();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
