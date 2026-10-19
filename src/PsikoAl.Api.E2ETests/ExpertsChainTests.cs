using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace PsikoAl.Api.E2ETests;

/// <summary>
/// "Admin uzmanı onayladığında mobil uygulamada gerçekten yayına çıkıyor mu?" sorusunun
/// makine cevabı.
/// </summary>
/// <remarks>
/// Zincir: uzman token -> POST /experts/profile -> Postgres -> admin token ->
/// POST /admin/experts/{id}/approve -> danışan token -> GET /experts/{id} ->
/// mobil uygulamanın ExpertSchema'sı.
/// Uzman onayı kategori zincirinden iki yönden daha kırılgan, testlerin sebebi bu:
/// (1) admin ucu ile mobil ucu AYNI satırı farklı filtreyle okuyor —
///     AdminExpertService.ListAsync filtresiz Experts.Query(), ExpertService.GetByIdAsync
///     ise "status != approved && viewer != expertId" olduğunda EXPERT_NOT_FOUND fırlatıyor;
/// (2) tek bir "onay" kavramı üç ayrı yere yazılıyor: experts.status, experts.pending_revision
///     ve profiles.is_verified. Bu üçünün birbirine karışması mobil tarafta ya sahte rozet
///     ya da sessizce yayından düşme üretir.
/// </remarks>
/// <remarks>
/// <c>Category=E2E</c> trait'i şart: <c>build-test</c> job'u tüm solution'ı koşuyor ve bu
/// proje ona dahil; trait olmadan lokal Supabase stack'i bulunmayan birim adımı kırmızı olurdu.
/// </remarks>
[Trait("Category", "E2E")]
[Collection(E2ECollection.Name)]
public sealed class ExpertsChainTests(E2EFixture fixture) : IAsyncLifetime
{
    // Sabit e-posta bilinçli — SupabaseTestIdentity'deki gerekçenin aynısı: her koşumda
    // yeni auth kullanıcısı üretmek auth.users'ı şişirir ve GoTrue'nun kayıt/login
    // limitine takılır. Kullanıcı bir kez üretilir, sonraki koşumlarda bulunur.
    //
    // DİKKAT: Bu çift E2EFixture'da YOK. Fixture yalnız Admin ve Client kimliği taşıyor ve
    // fixture.Client'ın profiles.role değeri 'client' — POST /experts/profile onu
    // 422 EXPERT_ROLE_REQUIRED ile reddeder. Doğru yeri SupabaseTestIdentity idi
    // (EnsureExpertAsync + ExpertEmail/ExpertPassword) ama orası PAYLAŞILAN dosya;
    // bu görevde değiştirilmedi, kimlik burada yerel olarak üretiliyor.
    private const string ExpertEmail = "e2e-expert@psikoal.test";
    private const string ExpertPassword = "E2eExpertPass123!";

    private const string SeedTitle = "E2E Klinik Psikolog";

    // Bio 50-1000 karakter aralığında olmak zorunda (CreateExpertProfileDtoValidator).
    private const string SeedBio =
        "Bu biyografi yalnızca uçtan uca testler tarafından üretilmiştir ve koşum sonunda silinir.";

    // Kimlik KOŞUM BAŞINA bir kez üretilir, test başına değil: xUnit her test metodu için
    // yeni bir sınıf örneği kurar, yani InitializeAsync beş kez koşar. Her seferinde GoTrue'ya
    // kullanıcı-üret + şifre-sabitle + login gitseydi tek koşum ~20 auth isteği üretir ve
    // testler sözleşme değil rate limit yüzünden kırmızı olurdu.
    private static Task<(Guid Id, string Token)>? _sharedIdentity;

    private string _expertToken = null!;
    private Guid _expertId;
    private string _specialization = null!;

    /// <summary>Uzmanlık alanı olarak kullanılacak AKTİF kategori + gerçek uzman kimliği.</summary>
    /// <remarks>
    /// Uzmanlık alanı doğrulaması slug üzerinden değil kategori ADI üzerinden yapılıyor
    /// (CategoryRepository.AllActiveNamesExistAsync: IsActive && Contains(category.Name)).
    /// Slug gönderilseydi profil oluşturma 422 VALIDATION_FAILED verirdi ve bu, sözleşme
    /// hatası değil kurulum hatası olurdu.
    /// </remarks>
    public async Task InitializeAsync()
    {
        _specialization = await CreateSpecializationCategoryAsync();

        var identity = await (_sharedIdentity ??= CreateExpertIdentityAsync());
        _expertId = identity.Id;
        _expertToken = identity.Token;
    }

    private static async Task<(Guid Id, string Token)> CreateExpertIdentityAsync()
    {
        var expertId = await EnsureExpertAuthUserAsync();

        // handle_new_user trigger'ı YALNIZCA auth.users INSERT'inde koşar. Hesap önceki bir
        // koşumda metadata'sız üretilmişse profiles.role 'client' kalır ve bu koşum sessizce
        // 422 EXPERT_ROLE_REQUIRED alırdı. Rol (ve ad/soyad) bu yüzden ayrıca sabitleniyor —
        // ad/soyad da sabitleniyor çünkü ExpertMapper.InitialsOf onlardan türüyor ve
        // şema testinde "EU" olarak doğrulanıyor.
        await ExecuteSqlAsync(
            """
            update public.profiles
               set role = 'expert', first_name = 'E2E', last_name = 'Uzman'
             where id = @id;
            """,
            expertId);

        return (expertId, await SignInAsync(ExpertEmail, ExpertPassword));
    }

    /// <summary>Üretilen uzman satırını ve bildirimlerini kaldırır.</summary>
    /// <remarks>
    /// auth.users ve profiles satırı BİLEREK bırakılır: sabit e-posta mantığının tamamı
    /// GoTrue rate limitine takılmamak için var, kullanıcıyı silmek onu boşa çıkarırdı.
    /// audit_logs da temizlenemez — trg_audit_logs_no_update tetikleyicisi tabloyu
    /// append-only yapıyor; o satırların benzersizlik kısıtı olmadığı için sonraki koşumu
    /// bozmazlar.
    /// </remarks>
    public Task DisposeAsync() => DeleteExpertRowAsync();

    /// <summary>
    /// Zincirin ana halkası: onaydan önce yok, onaydan sonra mobil şemaya birebir uygun.
    /// </summary>
    /// <remarks>
    /// "Önce yok" satırı olmadan bu test hiçbir şey kanıtlamazdı: uzman zaten onaylı olsa
    /// da yeşil olurdu. İkili okuma aynı satırın iki farklı filtreden geçtiğini gösteriyor —
    /// admin tarafı satırı görüyor, mobil taraf onay öncesi görmüyor.
    /// </remarks>
    [Fact]
    public async Task Admin_onaylamadan_önce_uzman_mobil_uçta_yok_onaydan_sonra_şemasına_uygun_şekilde_görünür()
    {
        // GEÇERLİ değerlerle seed. Bozuk URL sondası bilinçli olarak AYRI bir teste
        // taşındı (Geçersiz_URL_ile_uzman_profili_kaydedilemez): sonda burada durduğu
        // sürece AssertMobileExpertSchema fırlatıyordu ve altındaki hiçbir iddia —
        // status, acceptsOffers, isVerified — hiç koşmuyordu. Yani zincirin tek pozitif
        // kanıtı kalıcı olarak kırmızıydı ve hiçbir şey doğrulamıyordu.
        await ResetExpertAsync(
            personalWebsite: "https://psikoal.com",
            avatarUrl: "https://cdn.psikoal.test/avatar.png");

        var before = await fixture.Client.GetAsync($"/experts/{_expertId}");
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        Assert.Equal("EXPERT_NOT_FOUND", await ReadErrorCodeAsync(before));

        var approve = await fixture.Admin.PostAsync($"/admin/experts/{_expertId}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var approveBody = await approve.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(approveBody.GetProperty("success").GetBoolean());

        var after = await fixture.Client.GetAsync($"/experts/{_expertId}");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        var expert = await after.Content.ReadFromJsonAsync<JsonElement>();

        AssertMobileExpertSchema(expert);
        Assert.Equal("approved", expert.GetProperty("status").GetString());

        // acceptsOffers bir KOLON DEĞİL: ExpertMapper.ToExpertDto onu
        // "expert.Status == ExpertStatuses.Approved" olarak hesaplıyor. Mobil şemada
        // bağımsız bir boolean gibi durduğu için, ileride gerçek bir "teklif almayı kapattım"
        // bayrağı eklenirse mobil sessizce true görmeye devam eder. Bu satır türevin
        // bugünkü anlamını sabitliyor.
        Assert.True(expert.GetProperty("acceptsOffers").GetBoolean());

        // Onay rozete DOKUNMAZ (bkz. dördüncü test) — burada da doğrulanıyor ki
        // "onaylandı" ile "doğrulandı" tek yanıtta bile karışmasın.
        Assert.False(expert.GetProperty("isVerified").GetBoolean());
    }

    /// <summary>
    /// Ters yön: red uzmanı yayından düşürür ama uzmanın kendi ekranını kapatmaz.
    /// </summary>
    /// <remarks>
    /// İkinci yarı şart: GetByIdAsync'teki "viewerUserId != expertId" kaçamak yolu,
    /// mobildeki useExpertApprovalGate.ts hook'unun TEK besleyicisi. O koşul yanlışlıkla
    /// kaldırılırsa reddedilen uzman kendi ekranında sonsuz hata görür ve red sebebini
    /// hiç öğrenemez — yani "neden reddedildim" sorusunun cevabı uygulamada kaybolur.
    /// </remarks>
    [Fact]
    public async Task Reddedilen_uzman_danışanın_gözünden_kaybolur_ama_kendi_profilini_görmeye_devam_eder()
    {
        await ResetExpertAsync();
        await ApproveAsync();

        // Reddin bir şeyi GERÇEKTEN düşürdüğünü kanıtlamak için önce görünür olduğunu ölç.
        var visible = await fixture.Client.GetAsync($"/experts/{_expertId}");
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);

        // Sebepsiz red bilinçli olarak reddediliyor (RejectAsync ilk satırı) ve admin
        // panelindeki ReasonDialog bu sözleşmeye güveniyor: sebep alanı boş bırakılabilseydi
        // uzmana "reddedildi: " diye içi boş bir bildirim giderdi.
        var emptyReason = await fixture.Admin.PostAsJsonAsync(
            $"/admin/experts/{_expertId}/reject",
            new { reason = "" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, emptyReason.StatusCode);
        var emptyReasonBody = await emptyReason.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("EXPERT_REJECTION_REASON_REQUIRED", emptyReasonBody.GetProperty("code").GetString());
        Assert.Equal("reason", emptyReasonBody.GetProperty("field").GetString());

        // Başarısız red YAN ETKİ BIRAKMAMALI — istisna satıra dokunulmadan önce fırlıyor.
        var stillVisible = await fixture.Client.GetAsync($"/experts/{_expertId}");
        Assert.Equal(HttpStatusCode.OK, stillVisible.StatusCode);

        const string reason = "E2E: belgeler eksik.";
        var reject = await fixture.Admin.PostAsJsonAsync(
            $"/admin/experts/{_expertId}/reject",
            new { reason });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        var afterReject = await fixture.Client.GetAsync($"/experts/{_expertId}");
        Assert.Equal(HttpStatusCode.NotFound, afterReject.StatusCode);
        Assert.Equal("EXPERT_NOT_FOUND", await ReadErrorCodeAsync(afterReject));

        var ownView = await ExpertSendAsync(HttpMethod.Get, $"/experts/{_expertId}");
        Assert.Equal(HttpStatusCode.OK, ownView.StatusCode);
        var ownProfile = await ownView.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rejected", ownProfile.GetProperty("status").GetString());
        Assert.False(ownProfile.GetProperty("acceptsOffers").GetBoolean());

        // Uzman gerekçeyi kendi profilinde okur — bildirim silinse de cevap kaybolmaz.
        Assert.Equal(reason, ownProfile.GetProperty("rejectionReason").GetString());

        // Sebep gerçekten SAKLANDI mı: admin paneli detay panelinde bunu gösteriyor.
        var detail = await fixture.Admin.GetFromJsonAsync<JsonElement>($"/admin/experts/{_expertId}");
        Assert.Equal(reason, detail.GetProperty("rejectionReason").GetString());

        // Ve sebep uzmanın telefonuna ULAŞTI mı. EXPERT_REJECTED şablonu
        // 20260806212735_notifications.sql içinde in_app_enabled=true ile seedli;
        // şablon pasifleştirilse NotifyAsync sessizce return eder ve red bildirimi
        // hiç üretilmezdi — hiçbir HTTP durumu bunu göstermez.
        var notifications = await ExpertSendAsync(HttpMethod.Get, "/notifications");
        Assert.Equal(HttpStatusCode.OK, notifications.StatusCode);
        var notificationBody = await notifications.Content.ReadFromJsonAsync<JsonElement>();
        var rejectedNotification = notificationBody.GetProperty("data")
            .EnumerateArray()
            .Single(item => item.GetProperty("type").GetString() == "EXPERT_REJECTED");
        Assert.Contains(reason, rejectedNotification.GetProperty("body").GetString());
    }

    /// <summary>
    /// Versiyonlu onayın (experts.pending_revision) tek kanıtlı testi.
    /// </summary>
    /// <remarks>
    /// Onaylı uzmanın kendi güncellemesi CANLI satıra yazılmaz; admin onaylayana kadar
    /// mobil taraf eski hâli görür. Birisi PATCH'i "sadeleştirip" doğrudan satıra yazacak
    /// olsa bu test anında kırmızı olur — aksi hâlde denetlenmemiş içerik sessizce yayına
    /// çıkardı ve kimse fark etmezdi.
    /// </remarks>
    [Fact]
    public async Task Onaylı_uzmanın_revizyonu_admin_onaylayana_kadar_mobil_uçta_eski_hâliyle_kalır()
    {
        await ResetExpertAsync();
        await ApproveAsync();

        const string revisedTitle = "E2E Uzman Klinik Psikolog";

        var patch = await ExpertSendAsync(HttpMethod.Patch, "/experts/profile", new { title = revisedTitle });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        // PATCH'in KENDİ yanıtı bile canlı satırı döndürür — uzman kendi ekranında
        // "kaydedildi" sanıp yeni değeri görmemeli, çünkü henüz yayında değil.
        var patchBody = await patch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(SeedTitle, patchBody.GetProperty("title").GetString());

        var duringRevision = await fixture.Client.GetFromJsonAsync<JsonElement>($"/experts/{_expertId}");
        Assert.Equal(SeedTitle, duringRevision.GetProperty("title").GetString());
        Assert.Equal("approved", duringRevision.GetProperty("status").GetString());

        // "revision_pending" bir STATUS DEĞERİ DEĞİL: AdminExpertService.ListAsync onu
        // "PendingRevision != null" filtresine çeviriyor. Aynı uzmanın hem bu listede hem
        // status=approved listesinde bulunması bunu kanıtlıyor — ve mobil uçta bu durumun
        // hiçbir karşılığı olmadığını (uzman hâlâ 'approved' göründüğünü) gösteriyor.
        var revisionList = await fixture.Admin.GetFromJsonAsync<JsonElement>("/admin/experts?status=revision_pending");
        var listed = revisionList.EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == _expertId);
        Assert.True(listed.GetProperty("hasPendingRevision").GetBoolean());
        Assert.Equal("approved", listed.GetProperty("status").GetString());

        var approvedList = await fixture.Admin.GetFromJsonAsync<JsonElement>("/admin/experts?status=approved");
        Assert.Contains(approvedList.EnumerateArray(), item => item.GetProperty("id").GetGuid() == _expertId);

        // Admin panelindeki karşılaştırma paneli bu iki alanı yan yana gösteriyor:
        // canlı değer Current'ta, bekleyen değer PendingRevision'da.
        var detail = await fixture.Admin.GetFromJsonAsync<JsonElement>($"/admin/experts/{_expertId}");
        Assert.Equal(SeedTitle, detail.GetProperty("current").GetProperty("title").GetString());
        Assert.Equal(revisedTitle, detail.GetProperty("pendingRevision").GetProperty("title").GetString());

        await ApproveAsync();

        var afterApproval = await fixture.Client.GetFromJsonAsync<JsonElement>($"/experts/{_expertId}");
        Assert.Equal(revisedTitle, afterApproval.GetProperty("title").GetString());

        // Revizyon uygulandıktan sonra pending_revision null'lanmalı; null'lanmazsa uzman
        // sonsuza dek "revizyon bekliyor" listesinde kalır ve admin aynı değişikliği
        // her gün yeniden onaylar.
        var clearedList = await fixture.Admin.GetFromJsonAsync<JsonElement>("/admin/experts?status=revision_pending");
        Assert.DoesNotContain(clearedList.EnumerateArray(), item => item.GetProperty("id").GetGuid() == _expertId);
    }

    /// <summary>
    /// Onay ile doğrulama rozeti İKİ AYRI TABLOYA yazılır ve mobil uçta iki ayrı alandır.
    /// </summary>
    /// <remarks>
    /// Onay experts.status'ü, rozet profiles.is_verified'i değiştirir: ApproveAsync rozete
    /// HİÇ dokunmaz, SetVerifiedAsync ise yalnız profile.IsVerified'e dokunur. İkisini
    /// birleştiren bir "sadeleştirme" mobilde ya sahte rozet (onaylanan herkes doğrulanmış
    /// görünür) ya da sessiz yayından düşme (rozet kaldırılınca uzman kaybolur) üretirdi.
    /// </remarks>
    [Fact]
    public async Task Onay_ile_doğrulama_rozeti_mobil_uçta_birbirinden_bağımsız_iki_alan_olarak_gelir()
    {
        await ResetExpertAsync();
        await ApproveAsync();

        var afterApproval = await fixture.Client.GetFromJsonAsync<JsonElement>($"/experts/{_expertId}");
        Assert.Equal("approved", afterApproval.GetProperty("status").GetString());
        Assert.False(afterApproval.GetProperty("isVerified").GetBoolean());

        var verify = await fixture.Admin.PostAsJsonAsync(
            $"/admin/experts/{_expertId}/verify",
            new { isVerified = true });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var verified = await fixture.Client.GetFromJsonAsync<JsonElement>($"/experts/{_expertId}");
        Assert.True(verified.GetProperty("isVerified").GetBoolean());
        Assert.Equal("approved", verified.GetProperty("status").GetString());

        // Ters yön: rozeti geri almak uzmanı YAYINDAN DÜŞÜRMEZ. Bu satır olmadan
        // "verify=false artık uzmanı gizlesin" gibi bir değişiklik sessizce geçerdi.
        var unverify = await fixture.Admin.PostAsJsonAsync(
            $"/admin/experts/{_expertId}/verify",
            new { isVerified = false });
        Assert.Equal(HttpStatusCode.OK, unverify.StatusCode);

        var stillPublished = await fixture.Client.GetAsync($"/experts/{_expertId}");
        Assert.Equal(HttpStatusCode.OK, stillPublished.StatusCode);
        var stillPublishedBody = await stillPublished.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(stillPublishedBody.GetProperty("isVerified").GetBoolean());
        Assert.Equal("approved", stillPublishedBody.GetProperty("status").GetString());
    }

    /// <summary>
    /// Yukarıdaki dört testin yeşil olma sebebi gerçek yetki; kapıların açık olması değil.
    /// </summary>
    /// <remarks>
    /// Bu test olmadan diğerleri SAHTE YEŞİL üretebilirdi: AdminRequirementHandler herkesi
    /// geçirse de "onayladım, göründü" testleri geçerdi. Üç ayrı kapı ölçülüyor —
    /// yetkilendirme (danışan admin ucuna giremez), kimlik doğrulama (tokensız okuma yok:
    /// ONAYLI uzman bile herkese açık değil) ve rol kapısı (danışan uzman profili açamaz).
    /// </remarks>
    [Fact]
    public async Task Danışan_token_ile_uzman_onaylanamaz_ve_tokensız_uzman_profili_okunamaz()
    {
        await ResetExpertAsync();
        await ApproveAsync();

        // Danışanın token'ı geçerli ve imzası doğrulanmış; eksik olan tek şey admin_users
        // satırı. 403 = kimlik tanındı ama yetki yok; 401 gelseydi ölçtüğümüz şey
        // authorization değil authentication olurdu.
        var approveAsClient = await fixture.Client.PostAsync($"/admin/experts/{_expertId}/approve", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, approveAsClient.StatusCode);

        // Uzman ONAYLI olmasına rağmen tokensız okunamaz: ExpertsController'daki okuma
        // uçları [Authorize] taşıyor. "Onaylandı = herkese açık" varsayımı burada kapanıyor.
        var anonymousRead = await fixture.Mobile.GetAsync($"/experts/{_expertId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);

        // Rol kapısı: profiles.role='client' olan hesap uzman profili açamaz. Kontrol
        // satır yazılmadan ÖNCE koştuğu için bu çağrı temizlenecek veri de bırakmaz.
        var profileAsClient = await fixture.Client.PostAsJsonAsync("/experts/profile", NewProfilePayload());
        Assert.Equal(HttpStatusCode.Forbidden, profileAsClient.StatusCode);
        Assert.Equal("EXPERT_ROLE_REQUIRED", await ReadErrorCodeAsync(profileAsClient));
    }

    /// <summary>
    /// Mobil uygulamanın ExpertSchema'sını alan alan doğrular.
    /// </summary>
    /// <remarks>
    /// Kaynak: native-atomic/src/domains/expert/schemas/expert.schema.ts -> ExpertSchema,
    /// tüketen hook: native-atomic/src/domains/expert/api/useExpertProfileQuery.ts
    /// (axios res.data doğrudan ExpertSchema.parse'a veriliyor).
    /// </remarks>
    private void AssertMobileExpertSchema(JsonElement expert)
    {
        Assert.Equal(JsonValueKind.Object, expert.ValueKind);

        // /categories'teki { data, meta } sarmalı burada YOK. Sarmal eklenirse mobil parse
        // patlar; bu satır o farkı sabitliyor.
        Assert.False(
            expert.TryGetProperty("data", out _),
            "GET /experts/{id} yanıtı sarmalsız olmalı; mobil hook res.data'yı doğrudan parse ediyor.");

        // id: z.string().uuid()
        Assert.True(Guid.TryParse(expert.GetProperty("id").GetString(), out var id));
        Assert.Equal(_expertId, id);

        // z.string() olan alanlar.
        foreach (var field in (string[])["firstName", "lastName", "title", "bio", "status"])
        {
            Assert.Equal(JsonValueKind.String, expert.GetProperty(field).ValueKind);
        }

        // status: z.enum(['pending','approved','rejected']) — mobil uç bu üçlünün
        // dışındaki hiçbir değeri kabul etmez. Admin panelindeki 'revision_pending'
        // bir status DEĞERİ olmadığı için buraya asla sızmamalı.
        var status = expert.GetProperty("status").GetString();
        Assert.True(
            status is "pending" or "approved" or "rejected",
            $"status mobilde z.enum(['pending','approved','rejected']); gelen: {status}");

        // initials mobilde z.string().optional() ama backend her zaman gönderiyor ve
        // ExpertMapper.InitialsOf ad/soyaddan türetiyor. Backend bu alanı düşürse mobil
        // sessizce kendi getInitials()'ına düşerdi — fark edilmesi imkânsız bir sapma.
        Assert.Equal("EU", expert.GetProperty("initials").GetString());

        // specializations: z.array(z.string()) — ve testin ürettiği kategori adını içermeli.
        var specializations = expert.GetProperty("specializations");
        Assert.Equal(JsonValueKind.Array, specializations.ValueKind);
        Assert.All(specializations.EnumerateArray(), item => Assert.Equal(JsonValueKind.String, item.ValueKind));
        Assert.Contains(specializations.EnumerateArray(), item => item.GetString() == _specialization);

        // certificates: z.array(z.string()).optional()
        var certificates = expert.GetProperty("certificates");
        Assert.Equal(JsonValueKind.Array, certificates.ValueKind);
        Assert.All(certificates.EnumerateArray(), item => Assert.Equal(JsonValueKind.String, item.ValueKind));

        // experienceYears / rating / reviewCount: z.number(), rating 0..5, reviewCount int>=0.
        Assert.Equal(JsonValueKind.Number, expert.GetProperty("experienceYears").ValueKind);
        Assert.InRange(expert.GetProperty("experienceYears").GetInt32(), 0, 50);
        Assert.Equal(JsonValueKind.Number, expert.GetProperty("rating").ValueKind);
        Assert.InRange(expert.GetProperty("rating").GetDouble(), 0d, 5d);
        Assert.Equal(JsonValueKind.Number, expert.GetProperty("reviewCount").ValueKind);
        Assert.True(expert.GetProperty("reviewCount").GetInt32() >= 0);

        // isVerified / acceptsOffers: z.boolean() — null ya da 0/1 gelirse mobil parse patlar.
        AssertBoolean(expert, "isVerified");
        AssertBoolean(expert, "acceptsOffers");

        // education / cvUrl: z.string().nullable().optional()
        AssertStringOrNull(expert, "education");

        // cvUrl ANLAM SAPMASI taşıyor: backend buraya URL değil özel bucket'taki STORAGE
        // YOLUNU yazıyor ('{userId}/cv.pdf', UploadService.UploadCvAsync isPublicBucket:false).
        // Mobil şema düz string kabul ettiği için parse GEÇER — yani Zod bunu asla yakalayamaz —
        // ama mobilde o yolu imzalayacak bir uç yok; imzalama yalnız admin tarafında
        // (GET /admin/experts/{id}/documents). Tip doğru, ANLAM yanlış.
        AssertStringOrNull(expert, "cvUrl");

        // avatarUrl mobilde z.string().url().nullable() — .optional() YOK, yani anahtarın
        // BULUNMASI zorunlu; personalWebsite ise .optional(). İkisi de null olabilir ama
        // null değilse GEÇERLİ URL olmak zorunda. Backend'de ikisi için de doğrulama yok.
        Assert.True(expert.TryGetProperty("avatarUrl", out _), "avatarUrl anahtarı mobil şemada zorunlu.");
        AssertNullOrAbsoluteUrl(expert, "avatarUrl");
        AssertNullOrAbsoluteUrl(expert, "personalWebsite");
    }

    private static void AssertBoolean(JsonElement expert, string field)
    {
        var kind = expert.GetProperty(field).ValueKind;
        Assert.True(
            kind is JsonValueKind.True or JsonValueKind.False,
            $"{field} mobil tarafta z.boolean(); gelen tür: {kind}");
    }

    private static void AssertStringOrNull(JsonElement expert, string field)
    {
        var kind = expert.GetProperty(field).ValueKind;
        Assert.True(
            kind is JsonValueKind.String or JsonValueKind.Null,
            $"{field} string|null olmalı, gelen: {kind}");
    }

    private static void AssertNullOrAbsoluteUrl(JsonElement expert, string field)
    {
        var value = expert.GetProperty(field);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        Assert.Equal(JsonValueKind.String, value.ValueKind);
        var raw = value.GetString()!;
        var parsed = Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        Assert.True(
            parsed,
            $"{field} mobilde z.string().url() ile parse ediliyor; '{raw}' bu kuralı geçmez. "
            + "Backend bu alanı hiçbir validator'dan geçirmiyor, dolayısıyla değer HTTP 200 ile "
            + "kaydedilip mobilde ExpertSchema.parse() hatasına, yani boş ekrana dönüşür.");
    }

    /// <summary>Uzman satırını silip sıfırdan PENDING olarak yeniden üretir.</summary>
    /// <remarks>
    /// Testler statüyü değiştirdiği için ortak bir uzman satırı paylaşılamaz: sıra bağımlılığı
    /// olurdu ve tek başına koşan test farklı sonuç verirdi. Yeni AUTH KULLANICISI üretmek ise
    /// GoTrue login limitine takılacağı için tercih edilmiyor — kimlik sabit, satır her testte yeni.
    /// </remarks>
    /// <summary>
    /// Mobilin URL beklediği alanlara göreli/şemasız değer YAZILAMAZ.
    /// </summary>
    /// <remarks>
    /// Bu test gerçek bir açığın kapatılmasının siperi. Önce durum şuydu: mobil şema
    /// avatarUrl ve personalWebsite için z.string().url() istiyor ama backend'de bu
    /// alanları doğrulayan HİÇBİR kural yoktu (CreateExpertProfileDtoValidator yalnız
    /// Title/Specializations/ExperienceYears/Bio'ya bakıyordu; UpdateExpertProfileDto
    /// için validator DOSYASI HİÇ YOKTU, yani PATCH tamamen doğrulamasızdı).
    /// 'psikoal.com' ve 'avatar.png' bir uzmanın onboarding'de yazabileceği en olağan
    /// değerler; ikisi de HTTP 200 ile kaydediliyordu. Sonuç mobilde ExpertSchema.parse()
    /// hatası, yani app/expert/[id].tsx'te BOŞ EKRAN — hiçbir status-kodu testi görmezdi.
    ///
    /// Çözüm: UrlRuleExtensions.MustBeAbsoluteHttpUrl + UpdateExpertProfileDtoValidator.
    /// PATCH ayrıca kontrol ediliyor, çünkü açığın asıl geniş tarafı orasıydı.
    /// </remarks>
    [Fact]
    public async Task Geçersiz_URL_ile_uzman_profili_kaydedilemez()
    {
        await DeleteExpertRowAsync();

        var create = await ExpertSendAsync(
            HttpMethod.Post,
            "/experts/profile",
            NewProfilePayload(personalWebsite: "psikoal.com", avatarUrl: "avatar.png"));

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);

        // Profil kaydedilmemiş olmalı: yalnız 400 dönüp satırı yazmak, sapmayı
        // gizleyen en sinsi biçim olurdu. "Kendi profilini oku" diye ayrı bir uç YOK
        // (ExpertsController: POST/PATCH profile + GET {id}); uzman kendi id'sini
        // okuyor — satır olmadığında 404 EXPERT_NOT_FOUND döner.
        var afterFailedCreate = await ExpertSendAsync(HttpMethod.Get, $"/experts/{_expertId}");
        Assert.Equal(HttpStatusCode.NotFound, afterFailedCreate.StatusCode);
        Assert.Equal("EXPERT_NOT_FOUND", await ReadErrorCodeAsync(afterFailedCreate));

        // PATCH yolu ayrıca kapatılmış olmalı: geçerli profil açıp sonra bozuk URL ile
        // güncellemeyi dene. Bu adım olmadan Create kapısı yeşilken PATCH açık kalırdı.
        await ResetExpertAsync(personalWebsite: "https://psikoal.com");

        var patch = await ExpertSendAsync(
            HttpMethod.Patch,
            "/experts/profile",
            new { personalWebsite = "psikoal.com" });

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    /// <summary>
    /// Sertifika yükle → profilde gör → aynı değerle sil → Storage'da da gitmiş olsun.
    /// </summary>
    /// <remarks>
    /// İstemci silinecek sertifikayı ExpertDto.certificates'teki değerle seçer; yükleme
    /// ucu ile DTO'nun aynı biçimi (Storage yolu) döndürmesi bu zincirin tek varsayımı.
    /// Biri DTO'ya imzalı URL koysaydı istemci artık hiçbir sertifikayı silemezdi.
    /// Storage kontrolü service_role ile yapılır: liste güncellenip dosya kalsaydı uzman
    /// "sildim" sanar ama belge admin imzalamasıyla açılabilir kalırdı.
    /// </remarks>
    [Fact]
    public async Task Uzman_yüklediği_sertifikayı_profilde_dönen_değerle_siler()
    {
        await ResetExpertAsync();

        using var form = new MultipartFormDataContent();
        var pdf = new ByteArrayContent("%PDF-1.7 e2e sertifika"u8.ToArray());
        pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(pdf, "file", "sertifika.pdf");
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/experts/profile/certificates") { Content = form };
        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _expertToken);
        var upload = await fixture.Mobile.SendAsync(uploadRequest);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var ownProfile = await (await ExpertSendAsync(HttpMethod.Get, $"/experts/{_expertId}")).Content.ReadFromJsonAsync<JsonElement>();
        var certificate = Assert.Single(ownProfile.GetProperty("certificates").EnumerateArray()).GetString()!;
        Assert.StartsWith($"{_expertId}/certificates/", certificate, StringComparison.Ordinal);
        Assert.True(await StorageObjectExistsAsync(certificate));

        var deleteUrl = $"/experts/profile/certificates?certificate={Uri.EscapeDataString(certificate)}";
        var delete = await ExpertSendAsync(HttpMethod.Delete, deleteUrl);
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        var deleteBody = await delete.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(deleteBody.GetProperty("certificates").EnumerateArray());
        Assert.False(await StorageObjectExistsAsync(certificate));

        // İkinci deneme (çift dokunuş, bayat ekran) sessizce 200 değil 404 almalı.
        var again = await ExpertSendAsync(HttpMethod.Delete, deleteUrl);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("CERTIFICATE_NOT_FOUND", await ReadErrorCodeAsync(again));
    }

    private static async Task<bool> StorageObjectExistsAsync(string path)
    {
        using var storage = new HttpClient { BaseAddress = new Uri(LocalStack.SupabaseUrl.TrimEnd('/') + "/") };
        storage.DefaultRequestHeaders.Add("apikey", LocalStack.ServiceRoleKey);
        storage.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LocalStack.ServiceRoleKey);
        using var response = await storage.GetAsync($"storage/v1/object/authenticated/documents/{path}");
        return response.IsSuccessStatusCode;
    }

    private async Task ResetExpertAsync(string? personalWebsite = null, string? avatarUrl = null)
    {
        await DeleteExpertRowAsync();

        var created = await ExpertSendAsync(
            HttpMethod.Post,
            "/experts/profile",
            NewProfilePayload(personalWebsite, avatarUrl));

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();

        // Yeni satır 'pending' doğmalı. Doğmasaydı bütün "onaydan önce yok" iddiaları
        // dayanaksız kalırdı — kurulumun kendisi testin varsayımını taşıyor.
        Assert.Equal("pending", body.GetProperty("status").GetString());
        Assert.Equal(_expertId, body.GetProperty("id").GetGuid());

        // REVIZE-2 §6: danışan yalnız ilişkili olduğu uzmanı görür. Bu dosyanın ölçtüğü
        // şey onay/revizyon görünürlüğü olduğu için danışan bu uzmanın davetiyle gelmiş
        // sayılır (ilişki kuralının kendisi Revize2 zincirlerinde ölçülüyor).
        var clientId = (await SupabaseTestIdentity.EnsureClientAsync(CancellationToken.None)).AuthUserId;
        await ExecuteSqlAsync(
            $"update public.profiles set referred_by_expert_id = @id where id = '{clientId}';",
            _expertId);
    }

    private object NewProfilePayload(string? personalWebsite = null, string? avatarUrl = null) => new
    {
        title = SeedTitle,
        specializations = (string[])[_specialization],
        experienceYears = 7,
        bio = SeedBio,
        avatarUrl,
        education = "E2E Üniversitesi",
        cvUrl = (string?)null,
        certificates = Array.Empty<string>(),
        personalWebsite,
    };

    private async Task ApproveAsync()
    {
        var response = await fixture.Admin.PostAsync($"/admin/experts/{_expertId}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> ExpertSendAsync(HttpMethod method, string url, object? body = null)
    {
        // fixture.Mobile tokensız istemci ama BaseAddress'i test host'una bakıyor; token'ı
        // isteğin kendisine koymak istemciyi kirletmez — E2EFixture'a uzman istemcisi eklemek
        // PAYLAŞILAN dosyayı değiştirmek olurdu.
        using var request = new HttpRequestMessage(method, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _expertToken);
        return await fixture.Mobile.SendAsync(request);
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        // DomainExceptionMiddleware her DomainException'ı ApiErrorDto{code,message,field} yapıyor;
        // durum kodu (404/403/409/422) türü söyler ama tek başına yetmez: 422 aynı zamanda
        // VALIDATION_FAILED'in de kodu, yani "yanlış sebeple yeşil" tuzağı tam burada.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("code").GetString();
    }

    /// <summary>Uzmanlık alanı doğrulamasının geçmesi için aktif bir kategori üretir.</summary>
    private async Task<string> CreateSpecializationCategoryAsync()
    {
        var slug = fixture.NewCategorySlug();
        var name = $"E2E {slug}";

        var response = await fixture.Admin.PostAsJsonAsync(
            "/admin/categories",
            new
            {
                slug,
                name,
                icon = "brain",
                summary = "E2E uzman testinin ürettiği geçici kategori.",
                description = "Bu kayıt otomatik testler tarafından üretilir ve silinir.",
                blogTag = slug,
                assessmentCategory = (string?)null,
            });

        response.EnsureSuccessStatusCode();

        // Kategori temizliğini E2EFixture yapıyor (NewCategorySlug slug'ı kaydediyor).
        return name;
    }

    /// <summary>GoTrue'da uzman rolüne sahip gerçek bir kullanıcı garanti eder.</summary>
    /// <remarks>
    /// SupabaseTestIdentity'deki EnsureAuthUserAsync ile aynı desen; oradaki metotlar private
    /// olduğu ve o dosya PAYLAŞILAN olduğu için kopyalandı. user_metadata.role burada şart:
    /// handle_new_user trigger'ı profiles.role'u raw_user_meta_data->>'role' değerinden okuyor.
    /// </remarks>
    private static async Task<Guid> EnsureExpertAuthUserAsync()
    {
        using var client = CreateSupabaseClient(LocalStack.ServiceRoleKey);

        var response = await client.PostAsJsonAsync(
            "auth/v1/admin/users",
            new
            {
                email = ExpertEmail,
                password = ExpertPassword,
                email_confirm = true,
                user_metadata = new { role = "expert", first_name = "E2E", last_name = "Uzman" },
            });

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<JsonElement>();
            return created.GetProperty("id").GetGuid();
        }

        // Kullanıcı zaten varsa GoTrue 422 döner — ikinci koşumun normal hâli.
        var existing = await FindAuthUserIdAsync(client, ExpertEmail)
            ?? throw new InvalidOperationException(
                "GoTrue uzman kullanıcısı üretemedi ve mevcut kullanıcı da bulunamadı. "
                + $"Durum: {(int)response.StatusCode}, gövde: "
                + await response.Content.ReadAsStringAsync());

        var reset = await client.PutAsJsonAsync(
            $"auth/v1/admin/users/{existing}",
            new { password = ExpertPassword, email_confirm = true });
        reset.EnsureSuccessStatusCode();

        return existing;
    }

    private static async Task<Guid?> FindAuthUserIdAsync(HttpClient client, string email)
    {
        var response = await client.GetAsync($"auth/v1/admin/users?filter={Uri.EscapeDataString(email)}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (!body.TryGetProperty("users", out var users))
        {
            return null;
        }

        foreach (var user in users.EnumerateArray())
        {
            if (string.Equals(user.GetProperty("email").GetString(), email, StringComparison.OrdinalIgnoreCase))
            {
                return user.GetProperty("id").GetGuid();
            }
        }

        return null;
    }

    private static async Task<string> SignInAsync(string email, string password)
    {
        using var client = CreateSupabaseClient(LocalStack.AnonKey);

        var response = await client.PostAsJsonAsync("auth/v1/token?grant_type=password", new { email, password });
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GoTrue uzman login'i başarısız ({(int)response.StatusCode}): "
                + await response.Content.ReadAsStringAsync());
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("access_token boş döndü.");
    }

    private static HttpClient CreateSupabaseClient(string apiKey)
    {
        var client = new HttpClient { BaseAddress = new Uri(LocalStack.SupabaseUrl.TrimEnd('/') + "/") };
        client.DefaultRequestHeaders.Add("apikey", apiKey);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    /// <summary>Uzman satırını, bildirimlerini ve profildeki test artıklarını siler.</summary>
    /// <remarks>
    /// Uzman silme UCU YOK; temizlik doğrudan Postgres'ten yapılıyor (deseni
    /// SupabaseTestIdentity.EnsureAdminRowAsync ile aynı). profiles satırı korunuyor ama
    /// testin dokunduğu alanlar (is_verified, avatar_url) sıfırlanıyor — aksi hâlde bir
    /// sonraki koşum "rozet zaten true" ile başlar ve dördüncü test hiçbir şey kanıtlamaz.
    /// </remarks>
    private Task DeleteExpertRowAsync()
        => ExecuteSqlAsync(
            """
            delete from public.notifications where user_id = @id;
            delete from public.experts where id = @id;
            update public.profiles
               set role = 'expert', first_name = 'E2E', last_name = 'Uzman',
                   is_verified = false, avatar_url = null
             where id = @id;
            """,
            _expertId);

    private static async Task ExecuteSqlAsync(string sql, Guid id)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(LocalStack.PostgresConnectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
