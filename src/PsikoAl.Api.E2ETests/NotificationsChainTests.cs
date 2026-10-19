using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace PsikoAl.Api.E2ETests;

/// <summary>
/// "Admin panelden duyuru gönderdiğimde mobilde bildirim olarak düşüyor mu?" sorusunun
/// makine cevabı.
/// </summary>
/// <remarks>
/// Zincir: admin token -> POST /admin/notifications/send -> AdminNotificationService ->
/// NotificationService.NotifyAsync -> notification_templates süzgeci -> public.notifications
/// -> GET /notifications (danışan token'ı) -> mobil uygulamanın Zod şeması.
/// Bu domain bilinçli seçildi: YAZMA yolu şablonla filtreleniyor (template null,
/// !IsActive ya da !InAppEnabled ise NotifyAsync sessizce return ediyor), OKUMA yolu
/// (NotificationService.ListMyAsync) ise yalnız user_id'ye bakıyor. Yani kategorideki
/// ListAllAsync/ListActiveAsync asimetrisinin buradaki karşılığı: "admin gönderdi,
/// kullanıcı görmüyor" hatasının doğal yuvası.
/// </remarks>
/// <remarks>
/// <c>Category=E2E</c> trait'i şart: CI'daki birim test adımı
/// <c>--filter "Category!=E2E"</c> ile bu projeyi dışlıyor; lokal Supabase stack'i
/// olmadan koşarsa altyapı yüzünden kırmızı olurdu.
/// </remarks>
[Trait("Category", "E2E")]
[Collection(E2ECollection.Name)]
public sealed class NotificationsChainTests(E2EFixture fixture) : IAsyncLifetime
{
    /// <summary>Mobil taraftaki <c>NotificationTypeSchema</c> enum'unun birebir kopyası.</summary>
    /// <remarks>
    /// Kaynak: native-atomic/src/domains/notification/schemas/notification.schema.ts.
    /// Backend'e yeni bir NotificationTypes sabiti eklenip mobil enum güncellenmezse
    /// tek bilinmeyen tip TÜM yanıtın safeParse'ını düşürür (useNotificationsQuery
    /// throw ediyor) — kullanıcı tek satır değil, KOMPLE boş liste görür. Bu dizi o
    /// sessiz kırılmayı derleme değil, test zamanında yakalasın diye burada duruyor.
    /// </remarks>
    private static readonly string[] MobileNotificationTypes =
    [
        "OFFER_RECEIVED", "OFFER_ACCEPTED", "LISTING_EXPIRING", "LISTING_APPROVED",
        "LISTING_REJECTED", "EXPERT_APPROVED", "EXPERT_REJECTED", "REVIEW_APPROVED",
        "REVIEW_REJECTED", "SYSTEM",

        // REVIZE-2 §8: mobil enum bu türlerle genişletilmeli.
        "MATCHED", "LISTING_MATCHED_OTHER", "CLAIM_REMINDER", "INVITED_CLIENT_LISTING", "BALANCE_TOPUP",
        "ADMIN_GIFT", "WELCOME_RIGHTS", "REFERRAL_REWARD", "INVOICE_READY",
    ];

    /// <summary>Bu testin ürettiği bildirim gövdeleri — DisposeAsync bunları siler.</summary>
    private readonly List<string> _markers = [];

    private Guid _clientUserId;

    /// <summary>
    /// Danışanın profil kimliğini alır.
    /// </summary>
    /// <remarks>
    /// targetType="user" gönderimi için profil id'si şart ve API'de bunu öğrenmenin
    /// tek yolu /auth/whoami. E2EFixture'a alan eklemek yerine buradan okunuyor:
    /// paylaşılan dosyaya dokunmamak, başka domainlerin testlerini kırma riskini sıfırlar.
    /// Segment yayını (targetType="segment", role="client") BİLEREK kullanılmıyor:
    /// handle_new_user trigger'ı e2e ADMIN hesabına da role='client' profil açıyor,
    /// yani segment yayını admin hesabına da düşer ve test kendi çöpünü üretirdi.
    /// </remarks>
    public async Task InitializeAsync()
    {
        var whoami = await fixture.Client.GetFromJsonAsync<JsonElement>("/auth/whoami");
        _clientUserId = Guid.Parse(whoami.GetProperty("userId").GetString()!);
    }

    /// <summary>Üretilen bildirim satırlarını DB'den kaldırır.</summary>
    /// <remarks>
    /// Bildirim silme ucu YOK; temizlik doğrudan Postgres'ten yapılmak zorunda
    /// (desen: SupabaseTestIdentity.EnsureAdminRowAsync). Silme anahtarı gövdedeki
    /// benzersiz işaret: böylece stack'te birikmiş gerçek bildirimlere dokunulmaz.
    /// Temizlenmeyen satırlar bir sonraki koşumun unreadCount delta ölçümünü
    /// bozmazdı ama okunmamış sayaç testini yavaşça şişirir ve teşhisi zorlaştırırdı.
    /// </remarks>
    public async Task DisposeAsync()
    {
        if (_markers.Count == 0)
        {
            return;
        }

        await using var dataSource = new NpgsqlDataSourceBuilder(LocalStack.PostgresConnectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "delete from public.notifications where body = any(@bodies);";
        command.Parameters.AddWithValue("bodies", _markers.ToArray());
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Admin_gönderdiği_duyuru_danışanın_bildirim_listesinde_görünür()
    {
        var marker = NewMarker();
        const string sentTitle = "E2E duyuru başlığı";

        // Göndermeden ÖNCE yok olduğunu doğrula. Bu satır olmadan test, kayıt zaten
        // var olduğu için de yeşil olurdu - yani hiçbir şey kanıtlamazdı.
        Assert.DoesNotContain(await ListNotificationsAsync(), item => BodyOf(item) == marker);

        var response = await SendAsync(fixture.Admin, sentTitle, marker);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // recipientCount == 1: hedef çözümlemesi (ResolveSingleUserAsync) danışanın
        // profiles satırını gerçekten buldu. 0 dönseydi test "görünmedi" diye değil,
        // "hedef bulunamadı" diye kırmızı olur ve teşhis burada başlardı.
        var sendBody = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, sendBody.GetProperty("recipientCount").GetInt32());

        var notification = Assert.Single(
            await ListNotificationsAsync(),
            item => BodyOf(item) == marker);

        Assert.Equal("SYSTEM", notification.GetProperty("type").GetString());
        Assert.False(notification.GetProperty("read").GetBoolean());

        // Admin'in yazdığı başlık mobilde GÖRÜNMEK ZORUNDA. Bu iddia eskiden tersini
        // söylüyordu: SYSTEM şablonunun başlığı sabit 'Duyuru' olduğu ve
        // AdminNotificationService yalnız variables["body"] kurduğu için panelde ne
        // yazılırsa yazılsın mobilde "Duyuru" görünüyordu. Blazor sayfası başlık
        // alanını zorunlu tuttuğundan kullanıcıya "başlığım gönderildi" izlenimi
        // veriliyordu. Düzeltme iki parçalı ve ikisi de şart:
        //   20260821193000_system_notification_title.sql  → şablon başlığı '{title}'
        //   AdminNotificationService.SendAsync            → variables["title"]
        // Yalnız biri uygulanırsa burası kırmızı olur; iddia ikisini birden bağlıyor.
        Assert.Equal(sentTitle, notification.GetProperty("title").GetString());

        var systemTemplate = await GetSystemTemplateAsync();
        Assert.Equal("{title}", systemTemplate.GetProperty("title").GetString());

        // Gövde de admin metnine eşit: '{body}' yer tutucusu Render ile doldurulmuş
        // demektir. Başlık ve gövdenin İKİSİNİN birden geçmesi zincirin tam kanıtı.
        Assert.Equal(marker, notification.GetProperty("body").GetString());
    }

    /// <summary>
    /// Mobil rozetleri besleyen <c>meta.unreadCount</c> alanının gerçekten canlı olduğunu ölçer.
    /// </summary>
    /// <remarks>
    /// Kaynak: native-atomic/src/domains/notification/hooks/useUnreadNotificationCount.ts
    /// (<c>data?.meta.unreadCount ?? 0</c>) — ClientHomeScreen ve matches sekmesindeki
    /// rozet tam bu sayıyı gösteriyor. Sayaç ile liste AYRI sorgular
    /// (NotificationRepository.CountUnreadAsync vs. ListMyAsync); ikisi ayrışırsa
    /// kullanıcı "3 bildirim var" rozeti görüp boş liste açar.
    /// Sabit bir sayı değil DELTA ölçülüyor: aynı stack'te birikmiş bildirimler testi kırmasın.
    /// </remarks>
    [Fact]
    public async Task Admin_bildirim_gönderdiğinde_mobil_okunmamış_sayacı_artar_okununca_geri_düşer()
    {
        var marker = NewMarker();

        var before = await UnreadCountAsync();

        (await SendAsync(fixture.Admin, "E2E sayaç", marker)).EnsureSuccessStatusCode();

        Assert.Equal(before + 1, await UnreadCountAsync());

        var notification = Assert.Single(
            await ListNotificationsAsync(),
            item => BodyOf(item) == marker);
        var id = notification.GetProperty("id").GetGuid();

        var read = await fixture.Client.PostAsJsonAsync($"/notifications/{id}/read", new { });
        read.EnsureSuccessStatusCode();

        // Sayacın başlangıca DÖNMESİ, artışın gerçekten bu kayıttan geldiğini kanıtlıyor.
        Assert.Equal(before, await UnreadCountAsync());

        // Liste ile sayacın aynı gerçeği anlatması: kaydın kendisi de read=true olmalı.
        var afterRead = Assert.Single(
            await ListNotificationsAsync(),
            item => BodyOf(item) == marker);
        Assert.True(
            afterRead.GetProperty("read").GetBoolean(),
            "Sayaç düştü ama kayıt hâlâ okunmamış görünüyor: liste ile sayaç ayrışmış.");
    }

    /// <summary>
    /// Mobil uçtaki yanıt gövdesi, uygulamanın Zod şemasıyla alan alan aynı olmalı.
    /// </summary>
    /// <remarks>
    /// Kaynak: native-atomic/src/domains/notification/schemas/notification.schema.ts ve
    /// native-atomic/src/domains/notification/api/useNotificationsQuery.ts.
    /// Alan adı, tip veya sarmal değişirse mobil taraf `safeParse` başarısızlığında
    /// THROW ediyor — kullanıcı tek bozuk satır değil KOMPLE boş liste görür.
    /// HTTP 200 döndüğü için hiçbir status-kodu testi bunu yakalamaz; bu test o boşluğu kapatıyor.
    /// </remarks>
    [Fact]
    public async Task Mobil_bildirim_yanıtı_uygulamanın_şemasına_uyar()
    {
        // Şemayı boş liste üzerinde doğrulamak hiçbir şey kanıtlamaz: data[] elemanı
        // olmadan NotificationSchema hiç çalışmaz. En az bir kaydı test kendisi üretiyor.
        var marker = NewMarker();
        (await SendAsync(fixture.Admin, "E2E şema", marker)).EnsureSuccessStatusCode();

        var raw = await fixture.Client.GetFromJsonAsync<JsonElement>("/notifications");

        // Sarmal: { data: [...], meta: { page, total, perPage, unreadCount } }
        Assert.Equal(JsonValueKind.Array, raw.GetProperty("data").ValueKind);
        var meta = raw.GetProperty("meta");
        foreach (var field in (string[])["page", "total", "perPage", "unreadCount"])
        {
            Assert.Equal(JsonValueKind.Number, meta.GetProperty(field).ValueKind);
        }

        // unreadCount mobilde z.number().int().min(0) — negatif ya da kesirli değer
        // şemayı düşürürdü.
        var unreadCount = meta.GetProperty("unreadCount").GetInt32();
        Assert.True(unreadCount >= 0, $"unreadCount negatif olamaz, gelen: {unreadCount}");

        // SAPMA PİNİ — sayfalama yok, perPage semantik olarak bozuk.
        // NotificationsController.ListMy meta'yı { page = 1, total, perPage = total }
        // olarak kuruyor; ayrıca NotificationListResult.Total = dtos.Count, yani DB
        // sayımı değil dönen listenin uzunluğu. Mobil Zod sadece z.number() beklediği
        // için parse GEÇER: sapma sessiz. "perPage = sayfa boyutu" varsayan bir mobil
        // sonsuz kaydırma yazılırsa burada patlar; o güne kadar gerçeği belgeliyoruz.
        Assert.Equal(1, meta.GetProperty("page").GetInt32());
        Assert.Equal(meta.GetProperty("total").GetInt32(), meta.GetProperty("perPage").GetInt32());
        Assert.Equal(raw.GetProperty("data").GetArrayLength(), meta.GetProperty("total").GetInt32());

        var notification = raw.GetProperty("data")
            .EnumerateArray()
            .Single(item => BodyOf(item) == marker);

        foreach (var field in (string[])["id", "type", "title", "body", "timeLabel"])
        {
            Assert.Equal(JsonValueKind.String, notification.GetProperty(field).ValueKind);
        }

        // read mobilde z.boolean() — JSON'da true/false olmak zorunda. Yeni üretilen
        // kayıt okunmamış olduğu için değeri de false.
        var read = notification.GetProperty("read").ValueKind;
        Assert.True(
            read is JsonValueKind.True or JsonValueKind.False,
            $"read boolean olmalı, gelen: {read}");
        Assert.False(notification.GetProperty("read").GetBoolean());

        // createdAt mobilde z.string().datetime(); Zod'un varsayılanı '+00:00' ofsetini
        // REDDEDER, yalnız 'Z' sonekini kabul eder. Formatı
        // PsikoAl.Api/Serialization/UtcDateTimeOffsetJsonConverter zorluyor
        // (Program.cs ile kayıtlı). Converter kaydı düşerse burası kırmızı olur —
        // aksi hâlde mobilde tüm bildirim listesi sessizce boşalırdı.
        var createdAt = notification.GetProperty("createdAt");
        Assert.Equal(JsonValueKind.String, createdAt.ValueKind);
        Assert.EndsWith("Z", createdAt.GetString(), StringComparison.Ordinal);

        // Tip kontrolü SADECE kendi kaydımızda değil, listenin TAMAMINDA yapılıyor:
        // mobil enum tek bilinmeyen tipte bile tüm yanıtı düşürüyor.
        foreach (var item in raw.GetProperty("data").EnumerateArray())
        {
            var type = item.GetProperty("type").GetString()!;
            Assert.Contains(type, MobileNotificationTypes);
        }
    }

    /// <summary>
    /// Şablonu kapatılan duyuru "gönderildi" der ama kullanıcıya hiç ulaşmaz.
    /// </summary>
    /// <remarks>
    /// TERS YÖN. Diğer testler zincirin çalıştığını gösteriyor; bu test zincirin
    /// NEREDEN koptuğunu sabitliyor. İki sapma birden kanıtlanıyor:
    /// (1) Yazma yolu şablonla filtreli — NotifyAsync, InAppEnabled false ise
    ///     notifications satırını HİÇ yazmıyor ve sessizce dönüyor.
    /// (2) recipientCount YALAN SÖYLÜYOR — AdminNotificationService.SendAsync dönüşü
    ///     targetUserIds.Count, yani HEDEF sayısı; gerçekten yazılan bildirim sayısı
    ///     değil. Sıfır bildirim yazıldığı hâlde Blazor sayfası "1 kullanıcıya bildirim
    ///     gönderildi" yazıyor. Bu iddiayı gevşetmiyoruz: 200 + recipientCount == 1
    ///     BEKLENİYOR, çünkü bugünkü gerçek bu.
    /// </remarks>
    [Fact]
    public async Task Şablonu_pasifleştirilen_duyuru_gönderilse_bile_danışanın_listesinde_görünmez()
    {
        var marker = NewMarker();
        var original = await GetSystemTemplateAsync();

        // Ön koşul: şablon şu an açık olmalı, yoksa test hiçbir şey kanıtlamaz —
        // zaten kapalı bir şablonda "görünmedi" sonucu bedava gelirdi.
        Assert.True(
            original.GetProperty("inAppEnabled").GetBoolean(),
            "SYSTEM şablonu in_app kapalı başladı: önceki bir koşum geri yazmayı tamamlayamamış olabilir.");
        Assert.True(
            original.GetProperty("isActive").GetBoolean(),
            "SYSTEM şablonu pasif başladı: önceki bir koşum geri yazmayı tamamlayamamış olabilir.");

        try
        {
            var patch = await fixture.Admin.PatchAsJsonAsync(
                "/admin/notification-templates/SYSTEM",
                TemplatePayload(original, inAppEnabled: false));
            patch.EnsureSuccessStatusCode();

            var response = await SendAsync(fixture.Admin, "E2E kapalı şablon", marker);

            // Admin paneli başarı görüyor...
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var sendBody = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, sendBody.GetProperty("recipientCount").GetInt32());

            // ...ama kullanıcıya hiçbir şey ulaşmıyor.
            Assert.DoesNotContain(await ListNotificationsAsync(), item => BodyOf(item) == marker);
        }
        finally
        {
            // PATCH TAM DEĞİŞTİRME (partial değil): tüm alanlar gönderilmezse şablon
            // bozulur. Geri yazma finally'de, çünkü yukarıdaki iddialardan biri
            // patlarsa kapalı kalan SYSTEM şablonu bu dosyanın DİĞER testlerini de
            // sessizce kırmızı yapardı - ve teşhis "bildirim gitmiyor" olurdu.
            var restore = await fixture.Admin.PatchAsJsonAsync(
                "/admin/notification-templates/SYSTEM",
                TemplatePayload(original));
            restore.EnsureSuccessStatusCode();
        }

        // Geri yazmanın gerçekten tuttuğunu doğrula: finally'nin 200 dönmesi şablonun
        // eski hâline geldiğini kanıtlamaz, alanları tek tek okumak kanıtlar.
        var restored = await GetSystemTemplateAsync();
        Assert.Equal(original.GetProperty("title").GetString(), restored.GetProperty("title").GetString());
        Assert.Equal(original.GetProperty("body").GetString(), restored.GetProperty("body").GetString());
        Assert.Equal(original.GetProperty("pushEnabled").GetBoolean(), restored.GetProperty("pushEnabled").GetBoolean());
        Assert.Equal(original.GetProperty("emailEnabled").GetBoolean(), restored.GetProperty("emailEnabled").GetBoolean());
        Assert.True(
            restored.GetProperty("inAppEnabled").GetBoolean(),
            "SYSTEM şablonu açık hâline dönmedi: sonraki testler sessizce bildirimsiz kalır.");
        Assert.True(
            restored.GetProperty("isActive").GetBoolean(),
            "SYSTEM şablonu aktif hâline dönmedi: sonraki testler sessizce bildirimsiz kalır.");
    }

    /// <summary>
    /// Danışan token'ı duyuru gönderemez ve denemesi hiçbir iz bırakmaz.
    /// </summary>
    /// <remarks>
    /// Bu test olmadan diğerleri sahte yeşil üretebilirdi: AdminRequirementHandler
    /// yanlışlıkla herkesi geçirse "gönderdim, göründü" testleri yine geçerdi.
    /// 401 değil 403 bekleniyor — kimlik doğru (imzası doğrulanmış gerçek Supabase
    /// token'ı), eksik olan YETKİ (admin_users satırı yok).
    /// İkinci iddia daha güçlü: 403 dönerken yan etkinin de yazılmadığını ölçüyor.
    /// Yalnız status koduna bakan bir test, isteği reddedip bildirimi yine de yazan
    /// bir sıralama hatasını kaçırırdı.
    /// </remarks>
    [Fact]
    public async Task Danışan_token_ile_bildirim_gönderilemez_ve_kendi_listesi_değişmez()
    {
        var marker = NewMarker();
        var before = await NotificationIdsAsync();

        var response = await SendAsync(fixture.Client, "E2E yetkisiz duyuru", marker);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Sayı değil KİMLİK kümesi karşılaştırılıyor: bir satır eklenirken başka biri
        // silinseydi adet eşitliği yanıltırdı.
        Assert.Equal(before, await NotificationIdsAsync());
        Assert.DoesNotContain(await ListNotificationsAsync(), item => BodyOf(item) == marker);
    }

    /// <summary>
    /// Çakışmayan, silinebilir bir gövde işareti üretir.
    /// </summary>
    /// <remarks>
    /// İşaret doğrudan gövdenin KENDİSİ: SYSTEM şablonunun gövdesi '{body}' olduğu için
    /// notifications.body tam olarak bu metne eşit oluyor. Böylece temizlik SQL'i
    /// yalnız testin ürettiği satırlara dokunuyor, stack'teki gerçek bildirimlere değil.
    /// </remarks>
    private string NewMarker()
    {
        var marker = $"E2E duyuru {Guid.NewGuid():N}";
        _markers.Add(marker);
        return marker;
    }

    private Task<HttpResponseMessage> SendAsync(HttpClient client, string title, string body)
        => client.PostAsJsonAsync(
            "/admin/notifications/send",
            new
            {
                targetType = "user",
                userId = _clientUserId,
                title,
                body,
            });

    private async Task<JsonElement[]> ListNotificationsAsync()
    {
        // GET /notifications [Authorize] — fixture.Mobile (token'sız) 401 alır,
        // bu yüzden danışan istemcisi kullanılıyor.
        var raw = await fixture.Client.GetFromJsonAsync<JsonElement>("/notifications");
        return [.. raw.GetProperty("data").EnumerateArray()];
    }

    private async Task<Guid[]> NotificationIdsAsync()
        => [.. (await ListNotificationsAsync()).Select(item => item.GetProperty("id").GetGuid())];

    private async Task<int> UnreadCountAsync()
    {
        var raw = await fixture.Client.GetFromJsonAsync<JsonElement>("/notifications");
        return raw.GetProperty("meta").GetProperty("unreadCount").GetInt32();
    }

    private async Task<JsonElement> GetSystemTemplateAsync()
    {
        var templates = await fixture.Admin.GetFromJsonAsync<JsonElement>("/admin/notification-templates");
        return templates.EnumerateArray()
            .Single(template => template.GetProperty("type").GetString() == "SYSTEM");
    }

    private static object TemplatePayload(JsonElement template, bool? inAppEnabled = null) => new
    {
        title = template.GetProperty("title").GetString(),
        body = template.GetProperty("body").GetString(),
        htmlBody = template.GetProperty("htmlBody").GetString(),
        pushEnabled = template.GetProperty("pushEnabled").GetBoolean(),
        emailEnabled = template.GetProperty("emailEnabled").GetBoolean(),
        inAppEnabled = inAppEnabled ?? template.GetProperty("inAppEnabled").GetBoolean(),
        isActive = template.GetProperty("isActive").GetBoolean(),
    };

    private static string? BodyOf(JsonElement notification) => notification.GetProperty("body").GetString();
}
