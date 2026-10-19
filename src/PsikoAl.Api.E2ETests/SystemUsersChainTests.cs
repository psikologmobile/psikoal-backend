using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace PsikoAl.Api.E2ETests;

/// <summary>
/// "Admin panelden ayarı değiştirdiğimde / kullanıcıyı dondurduğumda mobil tarafta
/// gerçekten davranış değişiyor mu?" sorusunun makine cevabı.
/// </summary>
/// <remarks>
/// Bu domain kategoriden farklı bir riski ölçüyor: kategori zincirinde admin bir SATIR
/// yazar, mobil o satırı okur. Burada admin bir DAVRANIŞ anahtarı yazıyor ve satırı
/// hiç okumayan uçların (POST /listings, POST /auth/login) çalışma biçimi değişiyor.
/// Zincirin kopması sessizdir: ayar kaydedilir, panel yeşil görünür, mobil taraf eski
/// gibi davranmaya devam eder. Aşağıdaki testlerin tamamı ayarın ETKİSİNİ ölçüyor,
/// kaydedildiğini değil.
/// </remarks>
/// <remarks>
/// İki ayrı okuma yolu bilinçli olarak kapsanıyor:
/// LISTING_AUTO_APPROVE + LISTING_EXPIRE_DAYS C# katmanında
/// (ListingService.CreateAsync -> unitOfWork.SystemSettings), LISTING_MAX_ACTIVE ise
/// yalnız Postgres trigger'ında (enforce_listing_max_active) okunuyor. Tek yolu test
/// etmek diğerinin kırıldığını göstermez.
/// </remarks>
[Trait("Category", "E2E")]
[Collection(E2ECollection.Name)]
public sealed class SystemUsersChainTests(E2EFixture fixture)
{
    private const string AutoApproveKey = "LISTING_AUTO_APPROVE";
    private const string ExpireDaysKey = "LISTING_EXPIRE_DAYS";
    private const string MaxActiveKey = "LISTING_MAX_ACTIVE";
    private const string MaintenanceModeKey = "MAINTENANCE_MODE";

    /// <summary>Mobil ListingSchema'nın preferredSessionType enum'u — tek doğruluk kaynağı.</summary>
    private static readonly string[] MobileSessionTypes = ["online", "yüz_yüze", "yüz_yüze_online"];

    [Fact]
    public async Task Admin_LISTING_AUTO_APPROVE_ı_açtığında_danışanın_yeni_ilanı_mobil_ilan_akışında_anında_görünür()
    {
        var previousAutoApprove = await ReadSettingAsync(AutoApproveKey);
        var previousExpireDays = await ReadSettingAsync(ExpireDaysKey);
        var categoryName = await CreateActiveCategoryAsync();
        var title = NewListingTitle();

        try
        {
            // Süre ayarı bilinçli olarak seed değerinden (30) FARKLI bir sayıya çekiliyor.
            // 30 bırakılsaydı doğru sonuç, ayar hiç okunmasa da (sabit kodlu 30 ile) çıkardı
            // ve test hiçbir şey kanıtlamazdı.
            await WriteSettingAsync(ExpireDaysKey, "7");
            await WriteSettingAsync(AutoApproveKey, "true");

            // Eklemeden ÖNCE akışta yok. Bu satır olmadan test, ilan başka bir koşumdan
            // kalmış olsa da yeşil olurdu — yani "ayar etki etti"yi değil "veri var"ı ölçerdi.
            var feedBefore = await FetchFeedTitlesAsync();
            Assert.DoesNotContain(title, feedBefore);

            var created = await CreateListingAsync(categoryName, title);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);

            var listing = await created.Content.ReadFromJsonAsync<JsonElement>();

            // Ayarın etkisi tam olarak bu iki alanda görünür: onay beklemeden OPEN doğdu
            // ve son kullanma tarihi LISTING_EXPIRE_DAYS kadar ileri kondu.
            Assert.Equal("OPEN", listing.GetProperty("status").GetString());

            var expiresAt = listing.GetProperty("expiresAt").GetDateTimeOffset();
            var expected = DateTimeOffset.UtcNow.AddDays(7);
            Assert.True(
                (expiresAt - expected).Duration() < TimeSpan.FromMinutes(5),
                $"expiresAt LISTING_EXPIRE_DAYS=7 ayarına göre {expected:O} olmalıydı, gelen: {expiresAt:O}");

            // Uygulama yeniden başlatılmadı: ayar her istekte DB'den okunuyor, önbelleklenmiyor.
            var feedAfter = await FetchFeedTitlesAsync();
            Assert.Contains(title, feedAfter);
        }
        finally
        {
            await DeleteListingsAsync(title);
            await WriteSettingAsync(AutoApproveKey, previousAutoApprove);
            await WriteSettingAsync(ExpireDaysKey, previousExpireDays);
        }
    }

    /// <summary>
    /// Ters yön: ayar kapalıyken ilan akışta YOK ama sahibinin listesinde VAR.
    /// </summary>
    /// <remarks>
    /// İkinci iddia şart. Yalnız "akışta yok" denseydi test, ilan hiç yazılmamış olsa da
    /// yeşil olurdu — yani "yazma olmadı" ile "yazma oldu, filtre dışında kaldı" ayrımını
    /// yapamazdı. Kategori domainindeki ListAllAsync/ListActiveAsync hatasının tam
    /// eşdeğeri burada ListFeedAsync (Status=='OPEN' filtreler) ile ListMyAsync
    /// (yalnız ClientId'ye bakar) arasındadır.
    /// </remarks>
    [Fact]
    public async Task Admin_LISTING_AUTO_APPROVE_kapalıyken_oluşan_ilan_mobil_akışta_görünmez_ama_kendi_ilanlarımda_durur()
    {
        var previousAutoApprove = await ReadSettingAsync(AutoApproveKey);
        var categoryName = await CreateActiveCategoryAsync();
        var title = NewListingTitle();
        Guid listingId = default;

        try
        {
            await WriteSettingAsync(AutoApproveKey, "false");

            var created = await CreateListingAsync(categoryName, title);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);

            var listing = await created.Content.ReadFromJsonAsync<JsonElement>();
            listingId = listing.GetProperty("id").GetGuid();

            Assert.Equal("PENDING_APPROVAL", listing.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, listing.GetProperty("expiresAt").ValueKind);

            var feedIds = await FetchFeedIdsAsync();
            Assert.DoesNotContain(listingId, feedIds);

            var mine = await fixture.Client.GetFromJsonAsync<JsonElement>("/listings/my");
            var mineIds = mine.GetProperty("data")
                .EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid())
                .ToArray();
            Assert.Contains(listingId, mineIds);

            // PENDING_APPROVAL ilan da "aktif" sayılır (ListingStatuses.Active). activeCount
            // mobilin "kaç ilan hakkım kaldı" ekranını besliyor; sıfır dönseydi kullanıcı
            // limitini aştığını hiç göremezdi.
            Assert.True(mine.GetProperty("meta").GetProperty("activeCount").GetInt32() >= 1);

            var mineListing = mine.GetProperty("data")
                .EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == listingId);
            AssertMatchesMobileListingSchema(mineListing);
        }
        finally
        {
            await DeleteListingsAsync(title);
            await WriteSettingAsync(AutoApproveKey, previousAutoApprove);
        }
    }

    /// <summary>
    /// Ayarın Postgres trigger'ında okunan kolu ve trigger hatasının 422'ye çevrilmesi.
    /// </summary>
    /// <remarks>
    /// LISTING_MAX_ACTIVE'i C# hiç okumaz; kontrol enforce_listing_max_active
    /// trigger'ında (20260806193609_listings.sql). Yani bu test iki köprüyü birden
    /// doğruluyor: admin PATCH'inin trigger tarafından AYNI istek içinde görülmesi ve
    /// P0001 'LISTING_MAX_ACTIVE_EXCEEDED' hatasının ListingService'te yakalanıp mobilin
    /// beklediği 422 + kod gövdesine dönüşmesi. Köprü kopsa istek 500 olurdu ve mobil
    /// kullanıcı "limitini aştın" yerine genel hata görürdü.
    /// </remarks>
    [Fact]
    public async Task Admin_LISTING_MAX_ACTIVE_ı_bire_düşürdüğünde_danışanın_ikinci_ilanı_422_LISTING_MAX_ACTIVE_EXCEEDED_ile_reddedilir()
    {
        var previousMaxActive = await ReadSettingAsync(MaxActiveKey);
        var previousAutoApprove = await ReadSettingAsync(AutoApproveKey);
        var categoryName = await CreateActiveCategoryAsync();
        var firstTitle = NewListingTitle();
        var secondTitle = NewListingTitle();

        try
        {
            // Önkoşul, gevşetilmeden ölçülüyor: limiti 1 yapmanın anlamlı olması için
            // paylaşılan danışanın aktif ilanı OLMAMALI. Kalıntı varsa burada patlamak,
            // limiti "kalıntı+1" yapıp testi sessizce anlamsızlaştırmaktan iyidir.
            var baseline = await fixture.Client.GetFromJsonAsync<JsonElement>("/listings/my");
            var baselineActive = baseline.GetProperty("meta").GetProperty("activeCount").GetInt32();
            Assert.True(
                baselineActive == 0,
                $"Paylaşılan danışanın {baselineActive} aktif ilanı var. Bir test ürettiği "
                + "ilanı temizlememiş; limit 1'e çekilince ilk ilan bile reddedileceği için "
                + "bu senaryo ölçüm yapamaz.");

            await WriteSettingAsync(AutoApproveKey, "false");
            await WriteSettingAsync(MaxActiveKey, "1");

            var first = await CreateListingAsync(categoryName, firstTitle);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            var second = await CreateListingAsync(categoryName, secondTitle);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);

            var error = await second.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("LISTING_MAX_ACTIVE_EXCEEDED", error.GetProperty("code").GetString());

            // Reddedilen isteğin YAN ETKİSİ de olmamalı: satır insert edilip sonra
            // hata dönseydi kullanıcı "hata aldım ama ilanım duruyor" durumuna düşerdi.
            var after = await fixture.Client.GetFromJsonAsync<JsonElement>("/listings/my");
            Assert.Equal(1, after.GetProperty("meta").GetProperty("activeCount").GetInt32());
        }
        finally
        {
            // İkinci başlık da siliniyor: reddedilmesi BEKLENEN ilan yine de yazıldıysa
            // (aranan hata tam olarak bu) satır burada temizlenmezse kalıcı olur.
            await DeleteListingsAsync(firstTitle, secondTitle);
            await WriteSettingAsync(MaxActiveKey, previousMaxActive);
            await WriteSettingAsync(AutoApproveKey, previousAutoApprove);
        }
    }

    /// <summary>
    /// Kullanıcı durumu zinciri: admin dondurur -> mobil giriş kapanır -> aktifleştirilince açılır.
    /// </summary>
    /// <remarks>
    /// Çift yön şart: yalnız dondurma test edilseydi, ban'ın kalıcı kalması (unfreeze'in
    /// GoTrue'ya hiç gitmemesi) fark edilmezdi.
    /// </remarks>
    /// <remarks>
    /// Paylaşılan e2e-client hesabı BİLE BİLE kullanılmıyor: GoTrue ban'ı temizlenmeden
    /// kalırsa fixture'ın login'i çöker ve tüm suite kırmızıya döner. Bu yüzden tek
    /// kullanımlık bir hesap üretilip sonunda auth.users'tan siliniyor.
    /// </remarks>
    [Fact]
    public async Task Admin_dondurduğu_kullanıcı_mobil_girişte_AUTH_ACCOUNT_FROZEN_alır_aktifleştirilince_yeniden_girebilir()
    {
        var email = $"e2e-freeze-{Guid.NewGuid():N}@psikoal.test";
        const string password = "E2eFreezePass123!";

        try
        {
            var registered = await fixture.Mobile.PostAsJsonAsync(
                "/auth/register",
                new
                {
                    email,
                    password,
                    firstName = "E2E",
                    lastName = "Dondurma",
                    role = "client",
                    termsAccepted = true,
                    healthDataConsent = true,
                });
            Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

            var session = await registered.Content.ReadFromJsonAsync<JsonElement>();
            var userId = session.GetProperty("user").GetProperty("id").GetGuid();
            var accessToken = session.GetProperty("tokens").GetProperty("accessToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(accessToken));

            // Dondurmadan ÖNCE 'active'. Bu okuma olmadan sonraki 'frozen' iddiası,
            // hesap zaten dondurulmuş doğsa da yeşil olurdu.
            Assert.Equal("active", await ReadUserStatusAsync(email, userId));

            var freeze = await fixture.Admin.PostAsJsonAsync(
                $"/admin/users/{userId}/freeze",
                new { reason = "E2E dondurma senaryosu" });
            Assert.Equal(HttpStatusCode.OK, freeze.StatusCode);
            Assert.True((await freeze.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());

            Assert.Equal("frozen", await ReadUserStatusAsync(email, userId));

            var blocked = await fixture.Mobile.PostAsJsonAsync("/auth/login", new { email, password });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);

            // DİKKAT: bu kod AuthService'in profil kontrolünden GELMİYOR. GoTrue login'i
            // ban yüzünden önce patlıyor ve SupabaseAuthService "user_banned" hata kodunu
            // AUTH_ACCOUNT_FROZEN'a eşliyor. Profil kontrolü hiç çalışmıyor — eşleme
            // silinse bile profiles.status 'frozen' kalırdı ama mobil AUTH_PROVIDER_ERROR
            // görürdü, yani bu iddia gerçekten eşlemeyi ölçüyor.
            var blockedBody = await blocked.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("AUTH_ACCOUNT_FROZEN", blockedBody.GetProperty("code").GetString());

            await AssertFrozenTokenIsRejectedAsync(accessToken!);

            var unfreeze = await fixture.Admin.PostAsJsonAsync(
                $"/admin/users/{userId}/unfreeze",
                new { reason = "E2E temizlik" });
            Assert.Equal(HttpStatusCode.OK, unfreeze.StatusCode);

            Assert.Equal("active", await ReadUserStatusAsync(email, userId));

            var reopened = await fixture.Mobile.PostAsJsonAsync("/auth/login", new { email, password });
            Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);

            var reopenedBody = await reopened.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(string.IsNullOrWhiteSpace(
                reopenedBody.GetProperty("tokens").GetProperty("accessToken").GetString()));
        }
        finally
        {
            await DeleteAuthUserAsync(email);
        }
    }

    /// <summary>
    /// Yetki reddi yan etkisiz olmalı: 403 dönmesi ayarın yazılmadığı anlamına gelmez.
    /// </summary>
    /// <remarks>
    /// Yalnız durum kodu kontrol edilseydi, controller önce servisi çağırıp sonra 403
    /// döndüren bir sıralamada da test yeşil kalırdı. Bu yüzden reddedilen PATCH'ten
    /// sonra ayar admin token'ıyla YENİDEN okunuyor.
    /// </remarks>
    [Fact]
    public async Task Danışan_token_ı_ile_sistem_ayarı_uçlarına_erişilemez_ve_hiçbir_ayar_değişmez()
    {
        var before = await ReadSettingAsync(AutoApproveKey);

        var list = await fixture.Client.GetAsync("/admin/system-settings");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        // Kasten mevcut değerin TERSİ yazılmaya çalışılıyor; aynı değer denenseydi
        // "değişmedi" iddiası kendiliğinden doğru çıkar ve hiçbir şey kanıtlamazdı.
        var flipped = string.Equals(before, "true", StringComparison.OrdinalIgnoreCase) ? "false" : "true";
        var patch = await fixture.Client.PatchAsJsonAsync(
            $"/admin/system-settings/{AutoApproveKey}",
            new { value = flipped });
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);

        Assert.Equal(before, await ReadSettingAsync(AutoApproveKey));
    }

    /// <summary>
    /// SAPMA TESTİ: sistem ayarı ucunda hiçbir doğrulama yok.
    /// </summary>
    /// <remarks>
    /// UpdateSystemSettingDto için FluentValidation validator'ı YOK (PsikoAl.Api/Validators
    /// altında karşılığı yok) ve servis de değeri anahtarın tipine göre kontrol etmiyor.
    /// Aşağıdaki iddia bugünkü GERÇEĞİ sabitliyor: bool bekleyen bir anahtara serbest
    /// metin yazılabiliyor ve uç 200 dönüyor.
    ///
    /// Sonucu şudur: bir sonraki POST /listings çağrısında
    /// SystemSettingRepository.GetBoolAsync -> bool.Parse FormatException fırlatır;
    /// DomainExceptionMiddleware yalnız DomainException yakaladığı için istek 500'e düşer.
    /// (Aynı şey LISTING_MAX_ACTIVE="abc" için trigger'daki value::int cast'inde 22P02
    /// olarak yaşanır ve ListingService'in catch'i yalnız MessageText ==
    /// 'LISTING_MAX_ACTIVE_EXCEEDED' koşuluna baktığından oraya da düşmez.)
    ///
    /// Bu test 500'ü BİLEREK tetiklemiyor: hatalı ayarla ilan denemesi, ayarın geri
    /// yazılamadığı bir çökme durumunda tüm suite'i bozacak bir yan etki riski taşır.
    /// Doğrulama eklendiğinde bu test kırmızıya döner ve beklenti 422'ye çekilmelidir —
    /// dosyanın niyeti tam olarak o anı görünür kılmaktır.
    /// </remarks>
    [Fact]
    public async Task Sistem_ayarı_ucu_bool_bekleyen_anahtara_serbest_metni_doğrulamadan_kaydeder()
    {
        var previous = await ReadSettingAsync(AutoApproveKey);
        const string garbage = "kesinlikle-bir-bool-degil";

        try
        {
            var patch = await fixture.Admin.PatchAsJsonAsync(
                $"/admin/system-settings/{AutoApproveKey}",
                new { value = garbage });

            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

            var updated = await patch.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(garbage, updated.GetProperty("value").GetString());

            // Yalnız yanıt gövdesine bakmak yetmez: değerin gerçekten kalıcı olduğunu
            // bağımsız bir okumayla doğrula.
            Assert.Equal(garbage, await ReadSettingAsync(AutoApproveKey));
        }
        finally
        {
            await WriteSettingAsync(AutoApproveKey, previous);
        }
    }

    /// <summary>
    /// SAPMA TESTİ: MAINTENANCE_MODE ölü bir ayar — açılınca hiçbir şey olmuyor.
    /// </summary>
    /// <remarks>
    /// Anahtar bootstrap.sql'de seed ediliyor, SystemSettingKeys.MaintenanceMode olarak
    /// duruyor ve admin panelde düzenlenebiliyor; ancak çözümde HİÇBİR servis okumuyor.
    /// Aynı durum PACKAGE_VALID_DAYS / PACKAGE_MIN_SESSIONS / PACKAGE_MAX_SESSIONS için
    /// de geçerli. Admin bakım modunu açar, mobil uygulama hiçbir şey fark etmez.
    ///
    /// Bu test o boşluğu davranışla ölçüyor: ayar 'true' iken mobil uçlar tıkır tıkır
    /// çalışmaya devam ediyor. Bakım modu gerçekten uygulandığı gün bu test kırmızıya
    /// döner ve beklenti 503'e çekilmelidir.
    /// </remarks>
    [Fact]
    public async Task MAINTENANCE_MODE_açık_olsa_bile_mobil_uçlar_normal_çalışmaya_devam_eder()
    {
        var previous = await ReadSettingAsync(MaintenanceModeKey);

        try
        {
            await WriteSettingAsync(MaintenanceModeKey, "true");
            Assert.Equal("true", await ReadSettingAsync(MaintenanceModeKey));

            var feed = await fixture.Client.GetAsync("/listings");
            Assert.Equal(HttpStatusCode.OK, feed.StatusCode);

            var me = await fixture.Client.GetAsync("/auth/me");
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);

            var categories = await fixture.Mobile.GetAsync("/categories");
            Assert.Equal(HttpStatusCode.OK, categories.StatusCode);
        }
        finally
        {
            await WriteSettingAsync(MaintenanceModeKey, previous);
        }
    }

    /// <summary>
    /// Hesap silme kişisel veriyi gerçekten kaldırır: test sonucu gider, e-posta GoTrue'da
    /// anonimleşir, eski token 401 ACCOUNT_INACTIVE alır, eski e-postayla giriş yapılamaz.
    /// </summary>
    [Fact]
    public async Task Hesap_silme_kişisel_veriyi_kaldırır_ve_oturumu_kapatır()
    {
        var email = $"e2e-delete-{Guid.NewGuid():N}@psikoal.test";
        const string password = "E2eDeletePass123!";
        Guid userId = Guid.Empty;

        try
        {
            var registered = await fixture.Mobile.PostAsJsonAsync(
                "/auth/register",
                new
                {
                    email,
                    password,
                    firstName = "E2E",
                    lastName = "Silme",
                    role = "client",
                    termsAccepted = true,
                    healthDataConsent = true,
                });
            Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
            var session = await registered.Content.ReadFromJsonAsync<JsonElement>();
            userId = session.GetProperty("user").GetProperty("id").GetGuid();
            var accessToken = session.GetProperty("tokens").GetProperty("accessToken").GetString() ?? string.Empty;

            await ExecuteAsync(
                """
                insert into public.assessment_results (assessment_id, user_id, score, level, summary)
                select id, @id, 0, 'low', 'E2E silme' from public.assessments order by sort_order limit 1;
                """,
                userId);

            using (var delete = new HttpRequestMessage(HttpMethod.Delete, "/auth/me"))
            {
                delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                Assert.Equal(HttpStatusCode.NoContent, (await fixture.Mobile.SendAsync(delete)).StatusCode);
            }

            using (var me = new HttpRequestMessage(HttpMethod.Get, "/auth/me"))
            {
                me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var response = await fixture.Mobile.SendAsync(me);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal(
                    "ACCOUNT_INACTIVE",
                    (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            }

            var login = await fixture.Mobile.PostAsJsonAsync("/auth/login", new { email, password });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, login.StatusCode);

            Assert.Equal(0L, await CountAsync("select count(*) from public.assessment_results where user_id = @id;", userId));
            Assert.Equal(0L, await CountAsync("select count(*) from auth.users where id = @id and email not like 'deleted-%';", userId));
            Assert.Equal(1L, await CountAsync("select count(*) from public.profiles where id = @id and status = 'deleted';", userId));
        }
        finally
        {
            await ExecuteAsync("delete from auth.users where id = @id;", userId);
        }
    }

    private static async Task ExecuteAsync(string sql, Guid id)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(LocalStack.PostgresConnectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<long> CountAsync(string sql, Guid id)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(LocalStack.PostgresConnectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(CancellationToken.None) is long count ? count : -1;
    }

    /// <summary>
    /// Dondurulan kullanıcının ELİNDEKİ access token artık 401 ACCOUNT_INACTIVE alır.
    /// </summary>
    /// <remarks>
    /// Sözleşme §4. GoTrue ban'ı yalnız YENİ token üretimini (login/refresh) engelliyor;
    /// eskiden dondurulan kullanıcı token ömrü dolana kadar tüm [Authorize] uçlarında
    /// çalışmaya devam ediyordu. AccountStatusMiddleware istek başına profiles.status
    /// bakıyor (30 sn önbellek, dondurma anında düşürülüyor — bu yüzden beklemeden ölçülür).
    /// Anonim uçlar etkilenmemeli: aynı eski token'la GET /assessment hâlâ 401 almaz.
    /// </remarks>
    private async Task AssertFrozenTokenIsRejectedAsync(string accessToken)
    {
        // Token, isteğin ÜZERİNE konuyor; fixture.Mobile'ın varsayılan başlığı
        // değiştirilmiyor. İstemci paylaşılan bir nesne — üzerine yazılsaydı sonraki
        // testler token'sız sandıkları bir istemciyle koşardı.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var me = await fixture.Mobile.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        var body = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ACCOUNT_INACTIVE", body.GetProperty("code").GetString());

        using var anonymousRequest = new HttpRequestMessage(HttpMethod.Get, "/assessment");
        anonymousRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var anonymous = await fixture.Mobile.SendAsync(anonymousRequest);
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
    }

    /// <summary>
    /// Mobil ListingSchema ile alan alan karşılaştırma.
    /// </summary>
    /// <remarks>
    /// Kaynak: native-atomic/src/domains/listing/schemas/listing.schema.ts.
    /// Alan adı ya da tipi kayarsa mobil taraf ListingSchema.parse ile patlar; HTTP 200
    /// döndüğü için hiçbir durum-kodu testi bunu yakalamaz.
    /// </remarks>
    private static void AssertMatchesMobileListingSchema(JsonElement listing)
    {
        foreach (var field in (string[])["id", "clientId", "clientDisplayName", "title", "description", "preferredSessionType", "status", "budgetLabel", "createdAt", "createdAtRelative"])
        {
            Assert.Equal(JsonValueKind.String, listing.GetProperty(field).ValueKind);
        }

        Assert.Equal(JsonValueKind.Object, listing.GetProperty("client").ValueKind);
        Assert.Equal(JsonValueKind.Array, listing.GetProperty("specialization").ValueKind);

        foreach (var field in (string[])["budgetMin", "budgetMax", "offerCount"])
        {
            Assert.Equal(JsonValueKind.Number, listing.GetProperty(field).ValueKind);
        }

        var viewerHasOffered = listing.GetProperty("viewerHasOffered").ValueKind;
        Assert.True(
            viewerHasOffered is JsonValueKind.True or JsonValueKind.False,
            $"viewerHasOffered boolean olmalı, gelen: {viewerHasOffered}");

        // Zod: preferredSessionType enum'u üç değerle sınırlı ('fark_etmez' preprocess ile
        // 'yüz_yüze_online'a çevrilir). Backend'in bunun dışında bir değer üretmesi mobilde
        // parse hatası demektir.
        var sessionType = listing.GetProperty("preferredSessionType").GetString()!;
        Assert.Contains(sessionType, MobileSessionTypes);

        // Zod: expiresAt .nullable() — null MEŞRU, alanın var olması şart.
        Assert.True(
            listing.GetProperty("expiresAt").ValueKind is JsonValueKind.String or JsonValueKind.Null);

        // Zod: rejectionReason .nullable().optional() — null meşru.
        Assert.True(
            listing.GetProperty("rejectionReason").ValueKind is JsonValueKind.String or JsonValueKind.Null);

        // SAPMA — city: mobil şema z.string().optional(). Zod'da .optional() UNDEFINED'ı
        // kabul eder, null'u ETMEZ; null için .nullable() gerekir. Bu ilan şehirsiz
        // oluşturuldu ve Program.cs'te DefaultIgnoreCondition ayarlanmadığı için gövdeye
        // "city": null yazılıyor. Yani şehirsiz her ilan mobilde ListingSchema.parse
        // hatası verir. Aşağıdaki satır bugünkü gerçeği sabitliyor: alan VAR ve NULL.
        // Doğru çözüm ya backend'in alanı atlaması ya da Zod'un .nullable() olmasıdır;
        // hangisi yapılırsa bu satır kırmızıya döner ve karar bilinçli verilmiş olur.
        Assert.Equal(JsonValueKind.Null, listing.GetProperty("city").ValueKind);

        // SAPMA — viewerOfferId: z.string().uuid().optional() ama backend Guid? için
        // "viewerOfferId": null yazıyor. Aynı .optional()/null uyuşmazlığı.
        Assert.Equal(JsonValueKind.Null, listing.GetProperty("viewerOfferId").ValueKind);

        // SAPMA — assessmentResult: z.object({...}).optional() ama backend null yazıyor.
        Assert.Equal(JsonValueKind.Null, listing.GetProperty("assessmentResult").ValueKind);
    }

    private async Task<string> ReadSettingAsync(string key)
    {
        var settings = await fixture.Admin.GetFromJsonAsync<JsonElement>("/admin/system-settings");
        return settings.EnumerateArray()
            .Single(setting => setting.GetProperty("key").GetString() == key)
            .GetProperty("value")
            .GetString()!;
    }

    private async Task WriteSettingAsync(string key, string value)
    {
        var response = await fixture.Admin.PatchAsJsonAsync(
            $"/admin/system-settings/{key}",
            new { value });
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> ReadUserStatusAsync(string email, Guid expectedId)
    {
        var users = await fixture.Admin.GetFromJsonAsync<JsonElement>(
            $"/admin/users?search={Uri.EscapeDataString(email)}");

        // Liste sayfalı zarf döner: { data: [...], meta }.
        var user = users.GetProperty("data").EnumerateArray()
            .Single(item => string.Equals(
                item.GetProperty("email").GetString(),
                email,
                StringComparison.OrdinalIgnoreCase));

        // Kayıt yanıtındaki id ile admin listesindeki id aynı olmalı: profiles.id doğrudan
        // auth.users.id'dir. Ayrışsalardı freeze isteği yanlış satırı dondururdu.
        Assert.Equal(expectedId, user.GetProperty("id").GetGuid());

        return user.GetProperty("status").GetString()!;
    }

    /// <summary>İlan için zorunlu olan aktif kategoriyi üretir ve ADINI döndürür.</summary>
    /// <remarks>
    /// ListingService.CreateAsync, Specialization'daki her değerin aktif bir kategorinin
    /// ADI olmasını şart koşuyor (AllActiveNamesExistAsync) — slug değil, ad.
    /// Temizlik gerekmiyor: fixture.NewCategorySlug() slug'ı zaten TrackCategory'ye
    /// yazıyor ve DisposeAsync siliyor.
    /// </remarks>
    private async Task<string> CreateActiveCategoryAsync()
    {
        var slug = fixture.NewCategorySlug();
        var response = await fixture.Admin.PostAsJsonAsync(
            "/admin/categories",
            new
            {
                slug,
                name = $"E2E {slug}",
                icon = "brain",
                summary = "E2E testinin ürettiği geçici kategori.",
                description = "Bu kayıt otomatik testler tarafından üretilir ve silinir.",
                blogTag = slug,
                assessmentCategory = (string?)null,
            });
        response.EnsureSuccessStatusCode();
        return $"E2E {slug}";
    }

    private Task<HttpResponseMessage> CreateListingAsync(string categoryName, string title)
        => fixture.Client.PostAsJsonAsync(
            "/listings",
            new
            {
                title,
                description = "E2E testinin ürettiği geçici ilan.",
                specialization = new[] { categoryName },
                budgetMin = 500m,
                budgetMax = 1500m,
                preferredSessionType = "online",
                contactConsent = true,
                phone = "05551112233",

                // Şehir BİLEREK gönderilmiyor: mobil şemadaki city .optional()/null
                // uyuşmazlığı ancak şehirsiz bir ilanla ölçülebilir.
                city = (string?)null,
                assessmentResultId = (Guid?)null,
            });

    private static string NewListingTitle()
        => $"E2E ilan {Guid.NewGuid():N}";

    private async Task<string[]> FetchFeedTitlesAsync()
    {
        var feed = await fixture.Client.GetFromJsonAsync<JsonElement>("/listings");
        return [.. feed.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("title").GetString()!)];
    }

    private async Task<Guid[]> FetchFeedIdsAsync()
    {
        var feed = await fixture.Client.GetFromJsonAsync<JsonElement>("/listings");
        return [.. feed.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
    }

    /// <summary>Üretilen ilan satırlarını doğrudan DB'den kaldırır.</summary>
    /// <remarks>
    /// API üzerinden YAPILAMAZ: silme ucu yok ve POST /listings/{id}/close yalnız OPEN
    /// ilanları kabul ediyor (CloseAsync -> ListingNotOpen), yani PENDING_APPROVAL ilan
    /// hiçbir uçla temizlenemez. Kalan satır LISTING_MAX_ACTIVE testinin sıfır önkoşulunu
    /// bozar ve bir sonraki koşum kırmızı olur.
    ///
    /// Silme id ile DEĞİL başlık ile yapılıyor: başlıklar zaten Guid'den üretiliyor, yani
    /// benzersiz. Id ile silinseydi, "reddedilmeliydi ama yazıldı" senaryosunda (yani tam
    /// da bu testlerin aradığı hatada) yanıt gövdesi olmadığı için id bilinemez ve satır
    /// kalıcı olarak sızardı.
    /// </remarks>
    private static async Task DeleteListingsAsync(params string[] titles)
    {
        if (titles.Length == 0)
        {
            return;
        }

        await using var dataSource = new NpgsqlDataSourceBuilder(LocalStack.PostgresConnectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "delete from public.listings where title = any(@titles);";
        command.Parameters.AddWithValue("titles", titles);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    /// <summary>Tek kullanımlık hesabı auth.users'tan siler; profiles satırı cascade ile gider.</summary>
    /// <remarks>
    /// Silme, kalan GoTrue ban'ını da beraberinde götürür. Bu yüzden test ortasında
    /// unfreeze patlasa bile bir sonraki koşum etkilenmez. Desen:
    /// SupabaseTestIdentity.EnsureAdminRowAsync — doğrudan Postgres, RLS baypası kasıtlı.
    /// </remarks>
    private static async Task DeleteAuthUserAsync(string email)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(LocalStack.PostgresConnectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "delete from auth.users where email = @email;";
        command.Parameters.AddWithValue("email", email);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
