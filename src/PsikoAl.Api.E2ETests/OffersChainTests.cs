using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace PsikoAl.Api.E2ETests;

/// <summary>
/// "Uzman teklif gönderdiğinde danışan onu ilanında görüyor mu?" sorusunun makine cevabı.
/// </summary>
/// <remarks>
/// Zincir: uzman token -> POST /offers -> Postgres -> danışan token ->
/// GET /listings/{id}/offers -> mobil uygulamanın OfferSchema'sı.
///
/// Bu domainin ZİNCİR TESTİ YOKTU ve boşluk gerçek bir hataya yol açtı: mobil
/// OfferSchema'da <c>matchId</c> alanı <c>.optional()</c> idi, backend ise nullable
/// alanları <c>null</c> olarak YAZIYOR. Zod 4'te <c>.optional()</c> null'ı kabul
/// etmediği için tek bir null, listenin TAMAMININ safeParse'ını düşürüyordu ve
/// danışan, ilanına iki teklif gelmişken "Henüz teklif gelmedi" görüyordu.
/// HTTP 200 dönüyor, istisna atılmıyor, hiçbir durum-kodu testi bunu göremiyordu.
/// Aşağıdaki şema testi tam o boşluğu kapatıyor.
/// </remarks>
/// <remarks>
/// <c>Category=E2E</c> trait'i şart: CI'daki birim test adımı
/// <c>--filter "Category!=E2E"</c> ile bu projeyi dışlıyor.
/// </remarks>
[Trait("Category", "E2E")]
[Collection(E2ECollection.Name)]
public sealed class OffersChainTests(E2EFixture fixture) : IAsyncLifetime
{
    // ExpertsChainTests'in hesabından AYRI bir e-posta bilinçli: o dosya her testte
    // kendi uzman SATIRINI siliyor. Aynı hesabı paylaşsaydık iki dosya aynı anda
    // koştuğunda belirti rastgele EXPERT_NOT_FOUND olur ve sözleşme hatası sanılırdı.
    private const string ExpertEmail = "e2e-offer-expert@psikoal.test";
    private const string ExpertPassword = "E2eOfferExpertPass123!";

    private const string Bio =
        "E2E teklif zinciri testinin ürettiği geçici uzman profili. Bu metin yalnızca "
        + "CreateExpertProfileDtoValidator'ın 50-1000 karakter aralığını karşılamak için var.";

    /// <summary>Koşum boyunca paylaşılan uzman kimliği (GoTrue rate limitine takılmamak için).</summary>
    private static Task<(Guid Id, string Token)>? _sharedIdentity;

    private Guid _expertId;
    private string _expertToken = string.Empty;
    private string _specialization = string.Empty;

    /// <summary>Üretilen teklif ve ilan kimlikleri — DisposeAsync bunları siler.</summary>
    private readonly List<Guid> _listingIds = [];

    public async Task InitializeAsync()
    {
        _specialization = await CreateSpecializationCategoryAsync();

        var identity = await (_sharedIdentity ??= CreateApprovedExpertAsync());
        _expertId = identity.Id;
        _expertToken = identity.Token;

        // Profil her testte yeniden onaylı duruma çekiliyor ve bol bakiye veriliyor
        // (teklif ücret harcar; bakiye WalletChainTests'in konusu, bu dosyanın değil): başka bir test
        // (ya da önceki bir koşum) status'u bozmuş olabilir ve o zaman belirti
        // "teklif gönderilemedi" olur — sözleşme hatası değil, kurulum kaymasıdır.
        await ExecuteSqlAsync(
            """
            update public.experts
               set status = 'approved', specializations = array[@spec], balance_kurus = 100000000, free_offer_rights = 0
             where id = @id;
            """,
            _expertId,
            _specialization);
    }

    public async Task DisposeAsync()
    {
        foreach (var listingId in _listingIds)
        {
            await ExecuteSqlAsync(
                """
                delete from public.offers where listing_id = @id;
                delete from public.listings where id = @id;
                """,
                listingId);
        }
    }

    /// <summary>
    /// Ana zincir: uzman teklif gönderir, danışan onu ilanında görür.
    /// </summary>
    [Fact]
    public async Task Uzmanın_gönderdiği_teklif_danışanın_ilan_detayında_görünür()
    {
        var listingId = await CreateOpenListingAsync();

        // Göndermeden ÖNCE boş olduğunu doğrula. Bu satır olmadan test, teklif
        // zaten var olduğu için de yeşil olurdu — yani hiçbir şey kanıtlamazdı.
        Assert.Empty(await ListOffersAsync(listingId));

        var created = await CreateOfferAsync(listingId, price: 1250m, title: "E2E teklif");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var offer = Assert.Single(await ListOffersAsync(listingId));

        Assert.Equal(listingId, offer.GetProperty("listingId").GetGuid());
        Assert.Equal(_expertId, offer.GetProperty("expertId").GetGuid());
        Assert.Equal("PENDING", offer.GetProperty("status").GetString());
        Assert.Equal(1250m, offer.GetProperty("price").GetDecimal());

        // Danışan uzmanın kim olduğunu görmek ZORUNDA: "Uzmanı İncele" akışı
        // bu gömülü nesneden besleniyor. Boş gelirse ekran teklifi gösterir ama
        // uzmana geçilemez ve hata hiçbir yerde görünmez.
        var expert = offer.GetProperty("expert");
        Assert.Equal(_expertId, expert.GetProperty("id").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(expert.GetProperty("name").GetString()));
    }

    /// <summary>
    /// SÖZLEŞME: yanıt mobil uygulamanın OfferSchema'sına uyar.
    /// </summary>
    /// <remarks>
    /// Kaynak: psikoal-app / src/domains/offer/schemas/offer.schema.ts.
    /// Ayrı bir test olmasının sebebi E2E.md'deki kural: sapmayı ölçen sonda,
    /// zincirin bayrak testinin İÇİNE konmaz — sonda fırlatırsa altındaki
    /// iddialar hiç koşmaz ve zincirin tek pozitif kanıtı kaybolur.
    /// </remarks>
    [Fact]
    public async Task Mobil_teklif_yanıtı_uygulamanın_şemasına_uyar()
    {
        var listingId = await CreateOpenListingAsync();
        (await CreateOfferAsync(listingId, price: 990m, title: null)).EnsureSuccessStatusCode();

        var offer = Assert.Single(await ListOffersAsync(listingId));

        // Zorunlu alanlar: biri eksik/yanlış tipteyse safeParse düşer ve kullanıcı
        // tek satır değil KOMPLE boş liste görür.
        AssertKind(offer, "id", JsonValueKind.String);
        AssertKind(offer, "listingId", JsonValueKind.String);
        AssertKind(offer, "expertId", JsonValueKind.String);
        AssertKind(offer, "status", JsonValueKind.String);
        AssertKind(offer, "createdAt", JsonValueKind.String);
        Assert.Equal(JsonValueKind.Number, offer.GetProperty("price").ValueKind);

        // description mobil şemada .default('') — alanın BULUNMASI şart değil ama
        // varsa string olmalı.
        if (offer.TryGetProperty("description", out var description))
        {
            Assert.True(description.ValueKind is JsonValueKind.String or JsonValueKind.Null);
        }

        // Backend nullable alanları null YAZIYOR (Program.cs'te DefaultIgnoreCondition
        // bilinçli olarak ayarlanmıyor — bkz. docs/E2E.md). Mobil şema bu yüzden
        // .nullable().optional() olmak zorunda. Aşağıdaki iddialar o sözleşmeyi
        // ölçüyor; backend'in null yazma davranışını değil.
        AssertNullableOptional(
            offer,
            "matchId",
            JsonValueKind.String,
            "offer.schema.ts: matchId: z.string().uuid().nullable().optional() — "
            + "kabul edilmemiş teklifte OfferMapper null geçiriyor. Bu alan bir dönem "
            + "yalnız .optional() idi ve TÜM teklif listesini düşürüyordu.");

        AssertNullableOptional(
            offer,
            "title",
            JsonValueKind.String,
            "offer.schema.ts: title: z.string().max(100).nullable().optional() — "
            + "başlıksız teklif gönderilebiliyor (alan opsiyonel).");
    }

    /// <summary>
    /// NEGATİF: aynı uzman aynı ilana ikinci teklifi gönderemez.
    /// </summary>
    /// <remarks>
    /// İş kuralı: "Bir uzman aynı ilana sadece 1 teklif gönderebilir" (CLAUDE.md).
    /// Muhafız düşseydi mutlu-yol testlerinin hepsi yine yeşil kalır, ama danışan
    /// aynı uzmandan üst üste teklifler görürdü.
    /// </remarks>
    [Fact]
    public async Task Aynı_ilana_ikinci_teklif_409_OFFER_ALREADY_EXISTS_ile_reddedilir()
    {
        var listingId = await CreateOpenListingAsync();
        (await CreateOfferAsync(listingId, price: 1000m, title: "E2E ilk teklif")).EnsureSuccessStatusCode();

        var second = await CreateOfferAsync(listingId, price: 1100m, title: "E2E ikinci teklif");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OFFER_ALREADY_EXISTS", body.GetProperty("code").GetString());

        // Liste tek teklifte kalmalı: reddedilen istek yan etki BIRAKMAMALI.
        Assert.Single(await ListOffersAsync(listingId));
    }

    /// <summary>
    /// NEGATİF: onay bekleyen ilana teklif gönderilemez.
    /// </summary>
    /// <remarks>
    /// Admin onayı olmadan uzman feed'ine düşmemiş bir ilana teklif gitmesi,
    /// moderasyon kapısının tamamen baypas edildiği anlamına gelir.
    /// </remarks>
    [Fact]
    public async Task Onay_bekleyen_ilana_teklif_gönderilemez()
    {
        var listingId = await CreateListingAsync();
        _listingIds.Add(listingId);

        var response = await CreateOfferAsync(listingId, price: 1000m, title: "E2E onaysız");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OFFER_LISTING_NOT_OPEN", body.GetProperty("code").GetString());
    }

    /// <summary>
    /// NEGATİF: onaylanmamış uzman teklif gönderemez.
    /// </summary>
    /// <remarks>
    /// Mobil tarafta bir kapı var (useExpertApprovalGate) ama yorumu açıkça
    /// "backend son sözü söyler" diyor — yani gerçek muhafız burada. Mobil kapı
    /// bir gün gevşetilirse bu test tek savunma hattı olur.
    /// </remarks>
    [Fact]
    public async Task Onaylanmamış_uzman_teklif_gönderemez()
    {
        var listingId = await CreateOpenListingAsync();

        await ExecuteSqlAsync("update public.experts set status = 'pending' where id = @id;", _expertId);
        try
        {
            var response = await CreateOfferAsync(listingId, price: 1000m, title: "E2E onaysız uzman");

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("OFFER_EXPERT_NOT_APPROVED", body.GetProperty("code").GetString());
            Assert.Empty(await ListOffersAsync(listingId));
        }
        finally
        {
            // Durum ÖNCE geri yazılır: burada patlanırsa paylaşılan uzman hesabı
            // 'pending' kalır ve BAŞKA dosyaların testleri alakasız sebeplerle kırmızıya döner.
            await ExecuteSqlAsync(
                "update public.experts set status = 'approved' where id = @id;",
                _expertId);
        }
    }

    /// <summary>
    /// NEGATİF: danışan token'ı teklif gönderemez.
    /// </summary>
    /// <remarks>
    /// Geçerli bir token'la ölçülüyor: token'sız istekten daha güçlü bir kanıt,
    /// çünkü kimlik doğrulamanın değil YETKİLENDİRMENİN çalıştığını gösteriyor.
    /// </remarks>
    [Fact]
    public async Task Danışan_token_ile_teklif_gönderilemez()
    {
        var listingId = await CreateOpenListingAsync();

        var response = await fixture.Client.PostAsJsonAsync(
            "/offers",
            new
            {
                listingId,
                title = "E2E yetkisiz teklif",
                price = 1000m,
                sessionType = "online",
                description = (string?)null,
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OFFER_EXPERT_ROLE_REQUIRED", body.GetProperty("code").GetString());
        Assert.Empty(await ListOffersAsync(listingId));
    }

    // ------------------------------------------------------------------ yardımcılar

    private static void AssertKind(JsonElement item, string property, JsonValueKind expected)
    {
        Assert.True(item.TryGetProperty(property, out var value), $"'{property}' alanı yok.");
        Assert.Equal(expected, value.ValueKind);
    }

    /// <summary>Alan ya beklenen tipte ya null olmalı — ama BULUNMALI.</summary>
    /// <remarks>
    /// Alanın hiç bulunmaması da geçerli sayılmıyor: mobil şema <c>.optional()</c>
    /// olduğu için parse geçer, ama backend'in alanı sessizce düşürmesi sözleşme
    /// değişikliğidir ve fark edilmeden yayılır.
    /// </remarks>
    private static void AssertNullableOptional(
        JsonElement item,
        string property,
        JsonValueKind expected,
        string because)
    {
        Assert.True(item.TryGetProperty(property, out var value), $"'{property}' alanı yok. {because}");
        Assert.True(
            value.ValueKind == expected || value.ValueKind == JsonValueKind.Null,
            $"'{property}' beklenmeyen tipte: {value.ValueKind}. {because}");
    }

    // `Task` döndürmek YETMEZ: `using var request` metot dönerken dispose edilir ve
    // TestHost gövdeyi okumaya çalıştığında ObjectDisposedException atar. Bu yüzden
    // istek burada await ediliyor.
    //
    // Revize 2: teklif, uzmanın onayladığı anlık ücreti (expectedFeeKurus) taşır; önce
    // /offers/quote okunur. Quote hata dönerse (ilan açık değil, uzman onaysız) ücret 0
    // gönderilir — o durumda kural ihlali ücret kontrolünden ÖNCE yakalanır.
    private async Task<HttpResponseMessage> CreateOfferAsync(Guid listingId, decimal price, string? title)
    {
        var expectedFeeKurus = await QuoteFeeAsync(listingId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/offers")
        {
            Content = JsonContent.Create(new
            {
                listingId,
                title,
                price,
                sessionType = "online",
                description = (string?)null,
                expectedFeeKurus,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _expertToken);

        return await fixture.Mobile.SendAsync(request);
    }

    private async Task<long> QuoteFeeAsync(Guid listingId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/offers/quote?listingId={listingId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _expertToken);
        var response = await fixture.Mobile.SendAsync(request);
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("feeKurus").GetInt64()
            : 0;
    }

    private async Task<List<JsonElement>> ListOffersAsync(Guid listingId)
    {
        var response = await fixture.Client.GetAsync($"/listings/{listingId}/offers");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return [.. body.GetProperty("data").EnumerateArray()];
    }

    /// <summary>Danışan adına PENDING_APPROVAL bir ilan açar.</summary>
    private async Task<Guid> CreateListingAsync()
    {
        var response = await fixture.Client.PostAsJsonAsync(
            "/listings",
            new
            {
                // Başlık hem benzersiz hem de CreateListingDtoValidator'ın 10-100 karakter
                // aralığında (sabit 17 + 32 haneli guid = 49).
                title = $"E2E teklif ilanı {Guid.NewGuid():N}",
                description = "E2E teklif zinciri testinin ürettiği geçici ilan.",
                specialization = new[] { _specialization },
                budgetMin = 750m,
                budgetMax = 1500m,
                preferredSessionType = "online",
                contactConsent = true,
                phone = "05551112233",
                city = "İstanbul",
                assessmentResultId = (Guid?)null,
            });

        // EnsureSuccessStatusCode gövdeyi yutar; LISTING_MAX_ACTIVE_EXCEEDED gibi
        // kurulum hataları teşhis için şart olduğundan gövde elle okunuyor.
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"İlan oluşturulamadı ({(int)response.StatusCode}): "
                + await response.Content.ReadAsStringAsync());
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>İlanı açar ve admin onayıyla OPEN'a çeker.</summary>
    private async Task<Guid> CreateOpenListingAsync()
    {
        var listingId = await CreateListingAsync();
        _listingIds.Add(listingId);

        (await fixture.Admin.PostAsync($"/admin/listings/{listingId}/approve", null))
            .EnsureSuccessStatusCode();

        return listingId;
    }

    /// <summary>Uzmanlık alanı olarak kullanılacak aktif bir kategori üretir.</summary>
    /// <remarks>
    /// Uzmanlık doğrulaması slug değil kategori ADI üzerinden yapılıyor
    /// (CategoryRepository.AllActiveNamesExistAsync). Slug gönderilirse profil
    /// oluşturma 422 verir ve bu, sözleşme hatası değil kurulum hatası olurdu.
    /// </remarks>
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
                summary = "E2E teklif testinin ürettiği geçici kategori.",
                description = "Bu kayıt otomatik testler tarafından üretilir ve silinir.",
                blogTag = slug,
                assessmentCategory = (string?)null,
            });
        response.EnsureSuccessStatusCode();

        return name;
    }

    private async Task<(Guid Id, string Token)> CreateApprovedExpertAsync()
    {
        var expertId = await EnsureExpertAuthUserAsync();

        // handle_new_user trigger'ı YALNIZCA auth.users INSERT'inde koşar. Hesap
        // önceki bir koşumda metadata'sız üretilmişse profiles.role 'client' kalır
        // ve bu koşum sessizce 422 OFFER_EXPERT_ROLE_REQUIRED alırdı.
        await ExecuteSqlAsync(
            """
            update public.profiles
               set role = 'expert', first_name = 'E2E', last_name = 'Teklifçi'
             where id = @id;
            """,
            expertId);

        var token = await SignInAsync(ExpertEmail, ExpertPassword);

        // Profil yoksa oluştur; varsa dokunma. POST ikinci koşumda 422
        // EXPERT_PROFILE_ALREADY_EXISTS döner ve bu beklenen hâldir.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/experts/profile")
        {
            Content = JsonContent.Create(new
            {
                title = "E2E Klinik Psikolog",
                specializations = new[] { _specialization },
                experienceYears = 7,
                bio = Bio,
                avatarUrl = (string?)null,
                education = (string?)null,
                cvUrl = (string?)null,
                certificates = Array.Empty<string>(),
                personalWebsite = (string?)null,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var profile = await fixture.Mobile.SendAsync(request);
        if (profile.StatusCode != HttpStatusCode.OK)
        {
            var body = await profile.Content.ReadFromJsonAsync<JsonElement>();
            var code = body.TryGetProperty("code", out var value) ? value.GetString() : null;
            if (code != "EXPERT_PROFILE_ALREADY_EXISTS")
            {
                throw new InvalidOperationException(
                    $"Uzman profili oluşturulamadı ({(int)profile.StatusCode}): {code}");
            }
        }

        return (expertId, token);
    }

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
                user_metadata = new { role = "expert", first_name = "E2E", last_name = "Teklifçi" },
            });

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<JsonElement>();
            return created.GetProperty("id").GetGuid();
        }

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

    private static async Task ExecuteSqlAsync(string sql, Guid id, string? spec = null)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(LocalStack.PostgresConnectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", id);
        if (spec is not null)
        {
            command.Parameters.AddWithValue("spec", spec);
        }

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
