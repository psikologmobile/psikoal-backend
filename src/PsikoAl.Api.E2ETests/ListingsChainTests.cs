using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace PsikoAl.Api.E2ETests;

/// <summary>
/// "Admin onaylamadan ilan uzmanın feed'ine düşüyor mu?" sorusunun makine cevabı.
/// </summary>
/// <remarks>
/// Zincir: danışan token -> POST /listings -> Postgres (status=PENDING_APPROVAL) ->
/// admin token -> POST /admin/listings/{id}/approve -> GET /listings ->
/// mobil uygulamanın Zod şeması.
/// İlan onayı bilinçli seçildi: AYNI tablo iki uçtan FARKLI filtreyle okunuyor
/// (AdminListingService.ListAsync durum filtresi verilmezse TÜMÜNÜ, ListingService.ListFeedAsync
/// yalnız <c>Status == OPEN</c> olanları) ve aradaki geçişi bir durum makinesi belirliyor.
/// Bu, "onaylanmamış ilan uzmana göründü" sızıntısının doğal yuvasıdır — ve bu sızıntı
/// hiçbir HTTP status koduyla belli olmaz.
/// </remarks>
/// <remarks>
/// <c>Category=E2E</c> trait'i şart: mevcut <c>build-test</c> job'u
/// <c>dotnet test src/PsikoAl.slnx</c> koşuyor ve bu proje solution'a dahil.
/// Trait olmadan CI'daki birim test adımı lokal Supabase stack'i olmadığı için
/// kırmızı olurdu.
/// </remarks>
[Trait("Category", "E2E")]
[Collection(E2ECollection.Name)]
public sealed class ListingsChainTests(E2EFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// Her testin ÖNCESİNDE danışanın tüm ilanlarını siler.
    /// </summary>
    /// <remarks>
    /// Bu satırlar temizlenmezse test sınıfı ikinci koşumda komple çöker, üstelik
    /// sözleşmeyle ilgisi olmayan bir sebeple: LISTING_MAX_ACTIVE bootstrap'ta '3' ve
    /// trg_listings_max_active trigger'ı INSERT öncesi PENDING_APPROVAL+OPEN toplamını
    /// sayıyor (supabase/migrations/20260806193609_listings.sql). Tüm senaryolar AYNI
    /// e2e danışan hesabını paylaştığı için dördüncü ilan LISTING_MAX_ACTIVE_EXCEEDED
    /// ile patlar. İlan silme ucu YOK — bu yüzden doğrudan Postgres.
    /// Temizlik hem Initialize hem Dispose'ta: Dispose kaçırılırsa (koşum iptali,
    /// süreç ölümü) bir sonraki koşumu Initialize kurtarır.
    /// </remarks>
    public Task InitializeAsync() => PurgeClientListingsAsync();

    public Task DisposeAsync() => PurgeClientListingsAsync();

    /// <summary>
    /// Onay bekleyen ilan admin listesinde VAR, uzman feed'inde YOK.
    /// </summary>
    /// <remarks>
    /// Zincirin "eklemeden ÖNCE yok olduğunu doğrula" halkası budur: bu test olmadan
    /// <see cref="Admin_onayladığı_ilan_mobil_feed_listesinde_görünür"/> ilan zaten
    /// feed'de olduğu için de yeşil olurdu — yani hiçbir şey kanıtlamazdı.
    /// Aynı tablonun iki uçtan farklı filtreyle okunduğunu doğrudan kilitler.
    /// </remarks>
    [Fact]
    public async Task Onaylanmamış_ilan_mobil_feed_listesinde_görünmez()
    {
        // Ön koşul VARSAYILMAZ, ÖLÇÜLÜR. LISTING_AUTO_APPROVE global bir ayar ve
        // SystemSettingRepository önbellek kullanmıyor, her çağrıda DB'den okuyor.
        // Başka bir test ya da elle yapılan bir PATCH bunu 'true' yapsaydı ilan OPEN
        // doğardı ve buradaki tüm onay senaryoları SESSİZCE anlamsızlaşırdı.
        Assert.Equal("false", await ReadSystemSettingAsync("LISTING_AUTO_APPROVE"));

        var listing = await CreateClientListingAsync();

        // Backend ilanı PENDING_APPROVAL doğuruyor; mock-db (native-atomic/mock-db/handlers/
        // listing.handlers.js) ise 201 + 'OPEN' dönüyor. Sahte veriyle çalışan mobil UI
        // "ilanın yayında" varsayımına düşerse gerçek backend'de yanlış ekran gösterir.
        Assert.Equal("PENDING_APPROVAL", listing.Status);

        var pending = await FetchAdminListingsAsync("PENDING_APPROVAL");
        Assert.Contains(pending, item => item.GetProperty("id").GetGuid() == listing.Id);

        var feed = await FetchFeedAsync();
        Assert.DoesNotContain(feed, item => item.GetProperty("id").GetGuid() == listing.Id);
    }

    /// <summary>
    /// Onay zincirinin tamamı: admin ucu yazdı -> durum OPEN oldu -> feed'e düştü.
    /// </summary>
    /// <remarks>
    /// Halkalardan biri kopsa (admin ucu kaydetmedi, ApproveAsync OPEN'a çevirmedi,
    /// feed filtresi değişti) kırmızı olur. expiresAt ayrıca doğrulanıyor çünkü mobil
    /// karttaki "kalan süre" tamamen bu alana bağlı: ApproveAsync onu doldurmayı
    /// unutsa ilan yine feed'de görünür, testin geri kalanı yeşil kalır ve mobil
    /// tarafta süre alanı sessizce boşalırdı.
    /// </remarks>
    [Fact]
    public async Task Admin_onayladığı_ilan_mobil_feed_listesinde_görünür()
    {
        var listing = await CreateClientListingAsync();

        // ÖNCE yok olduğunu doğrula — approve çağrısının gerçekten bir şey değiştirdiğini
        // kanıtlayan tek satır bu.
        var feedBefore = await FetchFeedAsync();
        Assert.DoesNotContain(feedBefore, item => item.GetProperty("id").GetGuid() == listing.Id);

        var expireDays = int.Parse(
            await ReadSystemSettingAsync("LISTING_EXPIRE_DAYS"),
            CultureInfo.InvariantCulture);

        var beforeApprove = DateTimeOffset.UtcNow;
        var approve = await fixture.Admin.PostAsync($"/admin/listings/{listing.Id}/approve", null);
        var afterApprove = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var detail = await fixture.Admin.GetFromJsonAsync<JsonElement>($"/admin/listings/{listing.Id}");
        Assert.Equal("OPEN", detail.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, detail.GetProperty("approvedAt").ValueKind);
        Assert.Null(detail.GetProperty("rejectionReason").GetString());

        // expiresAt = onay ANI + LISTING_EXPIRE_DAYS. Sabit bir tarihe değil, çağrıyı
        // saran zaman aralığına göre ölçülüyor; aksi hâlde test saat farkına takılırdı.
        var expiresAt = detail.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(
            expiresAt,
            beforeApprove.AddDays(expireDays).AddSeconds(-5),
            afterApprove.AddDays(expireDays).AddSeconds(5));

        var feedAfter = await FetchFeedAsync();
        var inFeed = feedAfter.Single(item => item.GetProperty("id").GetGuid() == listing.Id);
        Assert.Equal("OPEN", inFeed.GetProperty("status").GetString());
    }

    /// <summary>
    /// TERS YÖN: reddedilen ilan uzman tarafına hiçbir uçtan sızmaz, sahibine ise sebebiyle döner.
    /// </summary>
    /// <remarks>
    /// Üç madde birlikte kanıtlıyor; tek başına feed kontrolü (b) ve (c)'yi kaçırırdı:
    /// (a) feed'de yok, (b) sahibi /listings/my?status=REJECTED_BY_ADMIN ile ilanı ve
    /// gönderilen red sebebini görüyor, (c) sahibi OLMAYAN bir token doğrudan
    /// /listings/{id} çağırdığında 404 LISTING_NOT_FOUND alıyor — ListingService.GetByIdAsync
    /// "OPEN değil ve sahibi değil -> bulunamadı" maskelemesi. (c) düşseydi uzman, id'yi
    /// tahmin ederek reddedilmiş ilanın tüm içeriğini okuyabilirdi.
    /// </remarks>
    [Fact]
    public async Task Admin_reddettiği_ilan_mobil_feed_listesine_düşmez_ama_sahibi_red_sebebini_görür()
    {
        var listing = await CreateClientListingAsync();
        const string reason = "E2E: ilan metni topluluk kurallarına aykırı.";

        var reject = await fixture.Admin.PostAsJsonAsync(
            $"/admin/listings/{listing.Id}/reject",
            new { reason });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        // (a)
        var feed = await FetchFeedAsync();
        Assert.DoesNotContain(feed, item => item.GetProperty("id").GetGuid() == listing.Id);

        // (b) — sahibinin kendi ucu. Sarmalın meta.activeCount alanı mobil şemada
        // ZORUNLU (native-atomic/src/domains/listing/api/useMyListingsQuery.ts), o yüzden
        // varlığı burada da ölçülüyor.
        var mine = await fixture.Client.GetFromJsonAsync<JsonElement>(
            "/listings/my?status=REJECTED_BY_ADMIN");
        Assert.Equal(JsonValueKind.Number, mine.GetProperty("meta").GetProperty("activeCount").ValueKind);

        var mineItem = mine.GetProperty("data")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == listing.Id);
        Assert.Equal("REJECTED_BY_ADMIN", mineItem.GetProperty("status").GetString());
        Assert.Equal(reason, mineItem.GetProperty("rejectionReason").GetString());

        // (c) — fixture.Admin gerçek ve FARKLI bir auth kullanıcısı; /listings ucunda
        // rol ayrıcalığı yok, yalnızca "sahibi mi" sorusu sorulur.
        var foreign = await fixture.Admin.GetAsync($"/listings/{listing.Id}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("LISTING_NOT_FOUND", await ErrorCodeAsync(foreign));
    }

    /// <summary>
    /// Mobil feed yanıtı, uygulamanın Zod şemasıyla alan alan aynı olmalı.
    /// </summary>
    /// <remarks>
    /// Kaynak: native-atomic/src/domains/listing/schemas/listing.schema.ts (ListingSchema) ve
    /// native-atomic/src/domains/listing/api/useListingListQuery.ts (ListingListResponseSchema).
    /// Alan adı, tipi veya sarmal değişirse mobil taraf <c>safeParse</c> hatasıyla BOŞ LİSTE
    /// gösterir — HTTP 200 döndüğü için hiçbir status-kodu testi bunu yakalamaz.
    ///
    /// BİLİNEN ÜÇ SAPMA — bu test bugün KIRMIZI olmalı ve öyle kalmalı:
    /// zod 4'te <c>.optional()</c> yalnız <c>undefined</c>'ı kabul eder, <c>null</c>'ı
    /// REDDEDER. Program.cs'te DefaultIgnoreCondition ayarlanmadığı için ListingDto'nun
    /// null alanları JSON'a <c>"alan": null</c> olarak yazılıyor:
    ///   1) viewerOfferId — ListFeedAsync her kayda viewerOfferId: null geçiriyor,
    ///      şema z.string().uuid().optional().
    ///   2) assessmentResult — ListFeedAsync ToListingDto'yu bu parametre olmadan
    ///      çağırıyor (varsayılan null), şema z.object({...}).optional().
    ///   3) city — kolon nullable, CreateListingDtoValidator kural koymuyor, şema
    ///      z.string().optional().
    /// Yani feed yanıtı bugün mobilde HER ZAMAN parse hatası veriyor. mock-db'de
    /// görünmemesinin sebebi listing.handlers.js'in bu alanları undefined bırakması —
    /// JSON.stringify undefined alanı tamamen atıyor.
    /// Çözüm ya mobil şemaya .nullable() eklemek ya da API'nin null alanları atlaması;
    /// hangisi seçilirse sözleşme orada kilitlenir. Test o karara kadar sapmayı ölçer.
    /// </remarks>
    [Fact]
    public async Task Mobil_ilan_feed_yanıtı_uygulamanın_şemasına_uyar()
    {
        // city GÖNDERİLMİYOR. Her senaryo şehri doldursaydı city sapması GİZLENİRDİ:
        // mobil CreateListingSchema'da city zaten .optional(), yani şehirsiz ilan
        // uygulamanın normal akışı.
        var listing = await CreateClientListingAsync(city: null);
        (await fixture.Admin.PostAsync($"/admin/listings/{listing.Id}/approve", null))
            .EnsureSuccessStatusCode();

        var raw = await fixture.Client.GetFromJsonAsync<JsonElement>("/listings");

        // Sarmal: { data: [...], meta: { page, total, perPage } }
        Assert.Equal(JsonValueKind.Array, raw.GetProperty("data").ValueKind);
        var meta = raw.GetProperty("meta");
        Assert.Equal(JsonValueKind.Number, meta.GetProperty("page").ValueKind);
        Assert.Equal(JsonValueKind.Number, meta.GetProperty("total").ValueKind);
        Assert.Equal(JsonValueKind.Number, meta.GetProperty("perPage").ValueKind);

        var item = raw.GetProperty("data")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("id").GetGuid() == listing.Id);

        foreach (var field in (string[])["id", "clientId", "title", "description", "preferredSessionType", "status", "budgetLabel", "createdAt", "createdAtRelative"])
        {
            Assert.Equal(JsonValueKind.String, item.GetProperty(field).ValueKind);
        }

        Assert.Equal(JsonValueKind.Number, item.GetProperty("budgetMin").ValueKind);
        Assert.Equal(JsonValueKind.Number, item.GetProperty("budgetMax").ValueKind);
        Assert.Equal(JsonValueKind.Number, item.GetProperty("offerCount").ValueKind);
        Assert.True(item.GetProperty("offerCount").GetInt32() >= 0);
        Assert.True(item.GetProperty("budgetMin").GetDecimal() > 0);
        Assert.True(item.GetProperty("budgetMax").GetDecimal() > 0);

        var viewerHasOffered = item.GetProperty("viewerHasOffered").ValueKind;
        Assert.True(
            viewerHasOffered is JsonValueKind.True or JsonValueKind.False,
            $"viewerHasOffered boolean olmalı, gelen: {viewerHasOffered}");

        // title 10-100, description max 500 — şemadaki uzunluk kuralları gövdede de geçerli.
        Assert.InRange(item.GetProperty("title").GetString()!.Length, 10, 100);
        Assert.True(item.GetProperty("description").GetString()!.Length <= 500);

        Assert.Equal(JsonValueKind.Array, item.GetProperty("specialization").ValueKind);
        Assert.All(
            item.GetProperty("specialization").EnumerateArray(),
            value => Assert.Equal(JsonValueKind.String, value.ValueKind));

        // z.enum(['online','yüz_yüze','yüz_yüze_online']) — preprocess yalnız 'fark_etmez'i
        // çevirir, backend başka bir değer üretirse mobil parse patlar.
        Assert.Contains(
            item.GetProperty("preferredSessionType").GetString()!,
            (string[])["online", "yüz_yüze", "yüz_yüze_online"]);

        // ListingStatusSchema'nın altı değeri — ListingStatuses.cs ve tablonun check
        // constraint'i ile birebir aynı olmalı.
        Assert.Contains(
            item.GetProperty("status").GetString()!,
            (string[])["PENDING_APPROVAL", "OPEN", "MATCHED", "CLOSED", "EXPIRED", "REJECTED_BY_ADMIN"]);

        // expiresAt şemada .nullable() — null MEŞRU, alanın kendisi zorunlu.
        var expiresAt = item.GetProperty("expiresAt").ValueKind;
        Assert.True(
            expiresAt is JsonValueKind.String or JsonValueKind.Null,
            $"expiresAt string|null olmalı, gelen: {expiresAt}");

        // createdAt / expiresAt 'Z' sonekli olmalı: z.string().datetime() ofset kabul etmez.
        // UtcDateTimeOffsetJsonConverter tam bu yüzden yazıldı; sessizce kaldırılırsa
        // "+03:00" biçimi döner ve mobil parse patlar.
        Assert.EndsWith("Z", item.GetProperty("createdAt").GetString(), StringComparison.Ordinal);

        // client nesnesi: şemada .optional(), ama backend HER ZAMAN gönderiyor.
        var client = item.GetProperty("client");
        Assert.Equal(JsonValueKind.Object, client.ValueKind);
        Assert.Equal(JsonValueKind.String, client.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.String, client.GetProperty("initials").ValueKind);
        Assert.Equal(JsonValueKind.String, client.GetProperty("createdAt").ValueKind);

        // avatarUrl şemada .url().nullable().optional() — null meşru, ama dolu ise
        // MUTLAKA mutlak URL olmalı; göreli yol dönerse zod reddeder.
        var avatarUrl = client.GetProperty("avatarUrl");
        Assert.True(
            avatarUrl.ValueKind is JsonValueKind.Null
            || Uri.IsWellFormedUriString(avatarUrl.GetString(), UriKind.Absolute),
            $"avatarUrl null ya da mutlak URL olmalı, gelen: {avatarUrl}");

        // clientDisplayName şemada .optional(); backend maskelenmiş ad gönderiyor.
        // Boş string MEŞRU: e2e danışan profilinde first_name/last_name boş.
        Assert.Equal(JsonValueKind.String, item.GetProperty("clientDisplayName").ValueKind);

        // rejectionReason şemada .nullable().optional() — null burada SORUN DEĞİL.
        // Aşağıdaki üç alanla farkı tam olarak bu: onlarda .nullable() YOK.
        Assert.True(
            item.GetProperty("rejectionReason").ValueKind is JsonValueKind.String or JsonValueKind.Null);

        // Aşağıdaki üç alan şehirsiz/tekliften bağımsız bir feed kaydında null gelir.
        // Mobil şema bunları .nullable().optional() ile karşılıyor; iddia o sözleşmeyi
        // ölçüyor, backend'in null yazma davranışını değil.
        AssertNullableOptional(
            item,
            "city",
            JsonValueKind.String,
            "listing.schema.ts: city: z.string().nullable().optional()");

        AssertNullableOptional(
            item,
            "viewerOfferId",
            JsonValueKind.String,
            "listing.schema.ts: viewerOfferId: z.string().uuid().nullable().optional() — "
            + "ListingService.ListFeedAsync her kayda null geçiriyor.");

        AssertNullableOptional(
            item,
            "assessmentResult",
            JsonValueKind.Object,
            "listing.schema.ts: assessmentResult: z.object({...}).nullable().optional() — "
            + "ListFeedAsync ToListingDto'yu assessmentResult parametresi olmadan çağırıyor.");
    }

    /// <summary>
    /// NEGATİF / durum makinesi: reddedilmiş ilan ikinci bir tıkla onaylanamaz.
    /// </summary>
    /// <remarks>
    /// AdminListingService.ApproveAsync'teki <c>listing.Status != PendingApproval</c>
    /// muhafızının gerçekten çalıştığını kanıtlar. Muhafız düşseydi reddedilmiş bir ilan
    /// tek bir yanlış tıklamayla uzman feed'ine düşerdi ve mutlu-yol testlerinin hepsi
    /// yine yeşil kalırdı.
    /// rejection_reason'ın hâlâ dolu olması muhafızın ERKEN döndüğünün ikinci kanıtı:
    /// ApproveAsync bir adım ilerleseydi RejectionReason'ı null'a çekerdi.
    /// </remarks>
    [Fact]
    public async Task Reddedilmiş_ilan_admin_tarafından_ikinci_kez_onaylanamaz()
    {
        var listing = await CreateClientListingAsync();
        const string reason = "E2E: bütçe aralığı gerçekçi değil.";

        (await fixture.Admin.PostAsJsonAsync($"/admin/listings/{listing.Id}/reject", new { reason }))
            .EnsureSuccessStatusCode();

        var secondApprove = await fixture.Admin.PostAsync($"/admin/listings/{listing.Id}/approve", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, secondApprove.StatusCode);
        Assert.Equal("LISTING_NOT_PENDING_APPROVAL", await ErrorCodeAsync(secondApprove));

        var detail = await fixture.Admin.GetFromJsonAsync<JsonElement>($"/admin/listings/{listing.Id}");
        Assert.Equal("REJECTED_BY_ADMIN", detail.GetProperty("status").GetString());
        Assert.Equal(reason, detail.GetProperty("rejectionReason").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("approvedAt").ValueKind);

        var feed = await FetchFeedAsync();
        Assert.DoesNotContain(feed, item => item.GetProperty("id").GetGuid() == listing.Id);
    }

    /// <summary>
    /// <c>.nullable().optional()</c> tanımlı alanlar için ortak iddia:
    /// alan hiç olmayabilir, null olabilir, varsa beklenen tipte olmalıdır.
    /// </summary>
    /// <remarks>
    /// SÖZLEŞME KARARI — bu yardımcı o kararın TEK sahibidir.
    /// zod 4'te <c>.optional()</c> yalnız <c>undefined</c>'ı kabul eder; <c>null</c>
    /// parse hatasıdır. Backend ise null yazıyor: Program.cs'te
    /// <c>DefaultIgnoreCondition</c> ayarlı DEĞİL ve ayarlanamaz — CategorySchema'daki
    /// <c>assessmentCategory: z.string().nullable()</c> (optional DEĞİL) alanın JSON'da
    /// BULUNMASINI şart koşuyor, yani null'ları atlamak kategori zincirini kırardı.
    ///
    /// Bu yüzden yön mobil tarafta düzeltildi: city / viewerOfferId / assessmentResult
    /// artık <c>.nullable().optional()</c> (listing.schema.ts). Aynı sözleşmeyi ölçen
    /// SystemUsersChainTests bu üç alanın null geldiğini doğruluyor; iki dosya ARTIK
    /// AYNI yöne bakıyor. Buradaki iddiayı gevşetip null'ı yasaklamaya dönerseniz o
    /// dosya kırmızıya döner — sapmanın sahibi burasıdır, kararı burada değiştirin.
    /// </remarks>
    private static void AssertNullableOptional(
        JsonElement item,
        string field,
        JsonValueKind expectedKind,
        string schemaNote)
    {
        if (!item.TryGetProperty(field, out var value))
        {
            return;
        }

        Assert.True(
            value.ValueKind == expectedKind || value.ValueKind == JsonValueKind.Null,
            $"'{field}' ya JSON'da hiç bulunmamalı, ya null, ya da {expectedKind} olmalı; "
            + $"gelen: {value.ValueKind}.{Environment.NewLine}{schemaNote}");
    }

    /// <summary>
    /// Danışan hesabıyla gerçek bir ilan açar ve id'siyle durumunu döner.
    /// </summary>
    /// <remarks>
    /// specialization SLUG değil, AKTİF kategorinin ADI olmak zorunda:
    /// ListingService.CreateAsync, CategoryRepository.AllActiveNamesExistAsync ile
    /// doğruluyor, aksi hâlde 422 VALIDATION_FAILED/'specialization' döner. Bu yüzden
    /// her ilan için önce kendi kategorisini açıyoruz — seed'li bir kategoriye
    /// yaslansaydık test, seed değiştiği gün sözleşmeyle ilgisiz bir sebeple kırılırdı.
    /// Kategori temizliğini paylaşılan E2EFixture yapıyor (slug NewCategorySlug ile takipli).
    /// </remarks>
    private async Task<CreatedListing> CreateClientListingAsync(string? city = "İstanbul")
    {
        var categoryName = await CreateCategoryAsync();

        // Başlık hem benzersiz hem de mobil şemanın min(10)/max(100) aralığında.
        var title = $"E2E ilan {Guid.NewGuid():N}";

        var response = await fixture.Client.PostAsJsonAsync(
            "/listings",
            new
            {
                title,
                description = "E2E testinin ürettiği geçici ilan. Koşum sonunda silinir.",
                specialization = new[] { categoryName },
                budgetMin = 750m,
                budgetMax = 1500m,
                preferredSessionType = "online",
                contactConsent = true,
                phone = "05551112233",
                city,
                assessmentResultId = (Guid?)null,
            });

        // EnsureSuccessStatusCode gövdeyi yutar; hata kodu (örn. LISTING_MAX_ACTIVE_EXCEEDED)
        // teşhis için şart olduğundan elle okunuyor.
        if (response.StatusCode != HttpStatusCode.OK)
        {
            Assert.Fail(
                $"İlan üretilemedi ({(int)response.StatusCode}): "
                + await response.Content.ReadAsStringAsync());
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new CreatedListing(
            body.GetProperty("id").GetGuid(),
            body.GetProperty("status").GetString()!);
    }

    /// <summary>Aktif bir kategori açar ve ADINI döner (specialization bu adı ister).</summary>
    private async Task<string> CreateCategoryAsync()
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
                summary = "E2E ilan testinin ürettiği geçici kategori.",
                description = "Bu kayıt otomatik testler tarafından üretilir ve silinir.",
                blogTag = slug,
                assessmentCategory = (string?)null,
            });
        response.EnsureSuccessStatusCode();

        return name;
    }

    private async Task<JsonElement[]> FetchFeedAsync()
    {
        // Feed ucu [Authorize] taşıyor — kategori zincirindeki gibi anonim OKUNMAZ.
        // Rol kontrolü yok, danışan token'ı yeterli.
        var raw = await fixture.Client.GetFromJsonAsync<JsonElement>("/listings");
        return [.. raw.GetProperty("data").EnumerateArray()];
    }

    /// <summary>Admin listesi sayfalı döner: { data, meta: { page, perPage, total } }.</summary>
    private async Task<JsonElement[]> FetchAdminListingsAsync(string status)
    {
        var raw = await fixture.Admin.GetFromJsonAsync<JsonElement>($"/admin/listings?status={status}&perPage=100");
        return [.. raw.GetProperty("data").EnumerateArray()];
    }

    private async Task<string> ReadSystemSettingAsync(string key)
    {
        var settings = await fixture.Admin.GetFromJsonAsync<JsonElement>("/admin/system-settings");
        var setting = settings.EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == key);
        return setting.GetProperty("value").GetString()!;
    }

    /// <summary>
    /// Başkasının test sonucu ilana bağlanamaz (sözleşme §5).
    /// </summary>
    /// <remarks>
    /// Sonuç admin hesabına ait olarak SQL ile üretiliyor — API'nin anonim gönderimi
    /// sahipsiz sonuç doğurur ve o da reddedilir, ama "başkasına ait" kolu ancak sahibi
    /// dolu bir satırla ölçülebilir. Reddedilen istek ilan yazmamalı.
    /// </remarks>
    [Fact]
    public async Task Başkasına_ait_test_sonucu_ilana_bağlanamaz()
    {
        var categoryName = await CreateCategoryAsync();
        var foreignResultId = await InsertAdminOwnedAssessmentResultAsync();
        try
        {
            var response = await fixture.Client.PostAsJsonAsync(
                "/listings",
                new
                {
                    title = $"E2E ilan {Guid.NewGuid():N}",
                    description = "E2E sahiplik testi.",
                    specialization = new[] { categoryName },
                    budgetMin = 750m,
                    budgetMax = 1500m,
                    preferredSessionType = "online",
                    contactConsent = true,
                    phone = "05551112233",
                    city = (string?)null,
                    assessmentResultId = foreignResultId,
                });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("ASSESSMENT_RESULT_NOT_OWNED", await ErrorCodeAsync(response));
        }
        finally
        {
            await ExecuteSqlAsync("delete from public.assessment_results where id = @id;", foreignResultId);
        }
    }

    private static async Task<Guid> InsertAdminOwnedAssessmentResultAsync()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(LocalStack.PostgresConnectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into public.assessment_results (assessment_id, user_id, score, level, summary)
            select (select id from public.assessments order by sort_order limit 1),
                   (select id from public.profiles where email = @email),
                   0, 'low', 'E2E sahiplik testi'
            returning id;
            """;
        command.Parameters.AddWithValue("email", SupabaseTestIdentity.AdminEmail);
        return await command.ExecuteScalarAsync(CancellationToken.None) is Guid id
            ? id
            : throw new InvalidOperationException("Sahipli test sonucu üretilemedi.");
    }

    private static async Task ExecuteSqlAsync(string sql, Guid id)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(LocalStack.PostgresConnectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    /// <summary>DomainExceptionMiddleware'in ürettiği ApiErrorDto.Code alanı.</summary>
    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("code").GetString();
    }

    private static async Task PurgeClientListingsAsync()
    {
        // Doğrudan postgres superuser bağlantısı; RLS baypası kasıtlı ve testin kendi
        // kurulum adımına ait — desen SupabaseTestIdentity.EnsureAdminRowAsync ile aynı.
        // Bildirimler de siliniyor: ApproveAsync/RejectAsync notificationService.NotifyAsync
        // çağırıyor ve satırlar listings silinince FK ile GİTMEZ (data kolonunda yalnız
        // JSON id var, ilişki yok). Zararsız ama biriktirmenin de anlamı yok.
        await using var dataSource = new NpgsqlDataSourceBuilder(LocalStack.PostgresConnectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            delete from public.listings
            where client_id = (select id from public.profiles where email = @email);

            delete from public.notifications
            where user_id = (select id from public.profiles where email = @email)
              and type in ('LISTING_APPROVED', 'LISTING_REJECTED');
            """;
        command.Parameters.AddWithValue("email", SupabaseTestIdentity.ClientEmail);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private sealed record CreatedListing(Guid Id, string Status);
}
