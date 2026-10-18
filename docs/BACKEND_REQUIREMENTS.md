# Backend Gereksinimleri

> Bu doküman, frontend'in **hiçbir iş mantığı hesaplamaması** prensibiyle yapılan refactoring sonucunda backend'in karşılaması gereken sözleşmeyi tanımlar. `mock-db/handlers/` altındaki handler'lar bu sözleşmenin çalışan referans implementasyonudur — gerçek backend birebir bu şekilde davranmalıdır.

## Genel Kurallar

- Tüm liste yanıtları `{ data: [...], meta: { page, total, perPage, ...ekstra } }` zarfındadır.
- Hata yanıtları `{ code, message, field? }` şeklindedir (bkz. CLAUDE.md §6).
- Tarihler ISO-8601; göreli etiketler (`"2 gün önce"`) **server'da** hesaplanıp hazır string olarak döner.
- İsim maskeleme (`"Zeynep Yılmaz" → "Zeynep Y."`) ve avatar baş harfleri (`initials`) **server'da** üretilir. Gerçek backend, eşleşme öncesi uzman görünümlerinde danışanın tam adını **hiç göndermemelidir** (mock'ta oturum ayrımı olmadığından ikisi birlikte dönüyor).

## Endpoint Sözleşmesi (mock'ta implement edilen)

### Listing
| Endpoint | Ek alanlar / parametreler |
|---|---|
| `GET /listings` | Param: `specialization[]`, `sessionType[]`, `budgetMin`, `budgetMax`, `sort` (`newest\|budget_desc\|budget_asc\|offer_asc`). Yanıtta her listing: `budgetLabel` ("₺500 – ₺900"), `clientDisplayName` (maskelenmiş), `client.initials`, `createdAtRelative` |
| `GET /listings/my` | Param: `status`. `meta.activeCount` (OPEN sayısı) |
| `GET /listings/:id` | `viewerHasOffered: boolean`, `viewerOfferId?: string` (istek yapan uzmanın bu ilana teklifi) |
| `GET /listings/:id/offers` | Teklifler `serveOffer` zenginleştirmesiyle döner |

### Offer
| Endpoint | Ek alanlar / parametreler |
|---|---|
| `GET /offers/my` | Param: `status`. `meta.pendingCount`. Her offer: `createdAtRelative`, `expert.initials`, `listing.clientDisplayName`, `listing.client.initials` |
| `POST /offers` | `expert` embed'i (name/title/rating) server'da auth kullanıcısının uzman kaydından doldurulur — frontend göndermez |

### Match
| Endpoint | Ek alanlar / parametreler |
|---|---|
| `GET /matches` | Param: `status[]` (`ACTIVE` / `COMPLETED,RELEASED`). `meta.activeCount`, `meta.pastCount`. Her match: `client.initials`, `expert.initials`, `createdAtRelative`, embedded `listing` (budgetLabel dahil) + `offer` |

### Expert
- `rating` ve `reviewCount` **her yanıtta reviews tablosundan hesaplanır** (statik alan tutulmaz).
- `initials`, `acceptsOffers` (= `status === 'approved'`) alanları döner. Frontend teklif uygunluğunu `role === 'client' && expert.acceptsOffers` ile belirler.
- `GET /experts/:id/reviews` → her review'a `createdAtRelative`.

### Uzman kredisi (eski "Payment" bölümünün yerine — 2026-10-14)
- Danışan ödemesi (cüzdan, seans paketi, `/payment/*`, Iyzico) **terk edildi**; mock'taki `/payment/packages`, `/payment/wallet`, `/payment/checkout/initiate` uçları gerçek backend'de **yazılmayacak** ve uygulamadan sökülür.
- **Revize 2 (2026-10-19):** kredi modeli kaldırıldı. Uzman TL bakiyesi + ücretsiz teklif hakkı; teklif gönderirken o anki **dinamik teklif ücreti** öder. Bakiye yalnız web sitesinden PayTR ile yüklenir. Mobil `GET /wallet/me`, `/wallet/transactions`, `/wallet/payments`, `/offers/quote` kullanır; yükleme yolu yoktur (App Store kuralı).
- `POST /offers` gövdesinde `expectedFeeKurus`; ücret değiştiyse 409 `OFFER_FEE_CHANGED`, bakiye yetmezse 422 `INSUFFICIENT_BALANCE`.
- Kabul/ret uçları kalktı: danışan `POST /listings/{id}/match` ile "eşleşti" işaretler.
- Kanonik sözleşme: **`docs/REVIZE-2.md`**; ödeme/cüzdan ayrıntısı: **`docs/PAYMENTS.md`**.

### Blog
- `GET /blogs` → param `category`, `limit` (ana sayfa 3 öğe için `limit=3` kullanır). Her blog: `liked` (oturum kullanıcısı beğenmiş mi), `likeCount`.
- `POST /blogs/:slug/like` → toggle; yanıt `{ likeCount, liked }` — frontend optimistic artırma/azaltma yapmaz.
- **Uygulandı (2026-10-14).** Sözleşme `native-atomic/src/domains/blog/schemas/blog.schema.ts`:
  - `GET /blogs` (oturumsuz da açık; Bearer varsa `liked` doldurulur) → `{ data: BlogListItem[], meta: { page, total, perPage } }`. Parametreler: `category` (kategori `blogTag`'i ya da serbest etiket), `page` (vars. 1), `perPage` (vars. 20, en çok 50), `limit` (verilirse `page=1`, `perPage=limit`). Sıralama `publishedAt` azalan.
  - `GET /blogs/{slug}` → `Blog` (`content` dahil). Mock gibi id ile de çağrılabilir. Görünmeyen/olmayan yazı → `BLOG_NOT_FOUND`.
  - `POST /blogs/{slug}/like` → `[Authorize]`, toggle, `{ likeCount, liked }`.
  - Alanlar sunucuda hesaplanır: `readingTime` (içerik kelime sayısı / 200, yukarı yuvarlanır, en az 1), `categories` (birincil kategorinin `blogTag`'i + ek etiketler, tekrarsız), `coverImage` (blog-media public URL), `publishedAt` (`Z` sonekli ISO).
  - Görünürlük: `status = published` ya da (`status = scheduled` ve `published_at <= now()`); kapak zorunlu. Taslaklar yalnız `/admin/blogs`'ta görünür.

### Assessment
- `GET /assessment` → her test `questionCount` içerir.
- `GET /assessment/results/my` → `{ data, meta.total }`, `createdAt` desc sıralı (ilk eleman = son sonuç).
- `POST /assessment/submit` → skorlama tamamen server'da (`score`, `level`, `summary`, `suggestions`).

### Notification (yeni)
- `GET /notifications` → `{ data, meta.unreadCount }`. Her bildirim: `id`, `type` (`OFFER_RECEIVED | OFFER_ACCEPTED | LISTING_EXPIRING | SYSTEM`), `title`, `body`, `createdAt`, `timeLabel` (server'da hesaplanan göreli zaman), `read`.

### Category
- `GET /categories/:slug` → `expertCount`, `completedMatchCount` server'da hesaplanır. Uzman `specializations` değerleri kategori adlarıyla birebir aynı sözlükten gelir (alias eşlemesi kaldırıldı — veri normalize edildi).

### Client
- Tüm client yanıtlarında `initials`.

## KVKK ve Güvenlik Sertleştirmesi (2026-10-14, ortak sözleşme)

- **Kayıt rızası:** `POST /auth/register` `termsAccepted` ve `healthDataConsent` alanlarını
  zorunlu tutar; ikisi de `true` değilse 400 ValidationProblemDetails. Rıza zamanı ve metin
  sürümü (`Legal:ConsentVersion`) `profiles.terms_accepted_at / health_data_consent_at /
  consent_version` kolonlarına yazılır.
- **Şifre sıfırlama:** `forgot-password` GoTrue `recover` çağrısına `redirect_to`
  (`Auth:PasswordResetRedirectUrl`) ekler. `POST /auth/reset-password { accessToken,
  newPassword }` → 204; geçersiz/süresi dolmuş token 422 `RESET_TOKEN_INVALID`.
- **Hata gövdesi değişmedi** (`{ code, message, field? }`), durum kodu türü söyler:
  `*_NOT_FOUND` → 404, rol/katılımcı kapıları → 403, `*_ALREADY_EXISTS` ve DB benzersizlik
  ihlali → 409, diğer iş kuralları ve `AUTH_*` → 422, beklenmeyen hata → 500 `INTERNAL_ERROR`
  (Türkçe mesaj, `trace-id` başlığı), rate limit → 429 `AUTH_RATE_LIMITED`.
- **Pasif hesap:** dondurulmuş/silinmiş kullanıcının geçerli token'ı yetki isteyen uçlarda
  401 `ACCOUNT_INACTIVE` alır (profiles.status, 30 sn önbellek). Anonim uçlar etkilenmez.
- **Test sonucu:** `GET /assessment/results/{id}` sahibi olan sonucu yalnız sahibine açar
  (aksi 404). `POST /listings` başkasının sonucunu kabul etmez (422
  `ASSESSMENT_RESULT_NOT_OWNED`). `POST /assessment/submit` cevapları testin sorularına göre
  doğrular (422 `ASSESSMENT_ANSWERS_INVALID`), puanı sunucu hesaplar, `email` saklanmaz.
- **Hesap silme:** GoTrue kullanıcısı anonimleştirilip banlanır (hard delete yok — cascade
  karşı tarafın geçmişini silerdi); test sonuçları, bildirimler, push token'ları silinir;
  ilanlar anonimleşir/kapanır, bekleyen teklifler çekilir, aktif eşleşmeler RELEASED olur;
  uzman belgeleri ve Storage klasörleri temizlenir.
- **Uzman belgeleri:** `cvUrl`/`certificates` profil uçlarında yok sayılır (yalnız yükleme
  uçları yazar); admin imzalaması yalnız `{expertId}/` altını imzalar; en çok 10 sertifika.
- **Push:** kilit ekranı metni türe göre geneldir; `data` = `{ type, entityId }`.
- **Paylaşım:** `shareEmail/sharePhone/shareLocation` yeni hesaplarda varsayılan `false`.
- **PostgREST:** public tabloları anon/authenticated rollerine kapalı; tek giriş noktası API.

## Frontend'de Kalan Salt Görsel Formatlama
(İstenirse backend'e taşınabilir; iş mantığı içermez.)

- `₺` para formatı: `price.toLocaleString('tr-TR')` (Chip/summary gösterimleri)
- Mutlak tarih yazımı: `core/utils/formatDate.ts` (`short | long | time | dayMonth | dayMonthShort`)
- `rating.toFixed(1)` — sayı gösterim hassasiyeti
- `getInitials`/`getFullName` — yalnızca lokal auth kullanıcısı (server verisi olmayan) için fallback
- Durum → renk/ikon/etiket eşlemeleri: `LISTING_STATUS_CONFIG`, `OFFER_STATUS_CONFIG`, `MATCH_STATUS_CONFIG`, `RESULT_LEVEL_CONFIG`, `NOTIFICATION_TYPE_CONFIG`, `SESSION_TYPE_LABELS` (sunum konfigürasyonu)
- Yıldız dolu/yarım/boş türetimi (`RatingRow`) ve quiz ilerleme çubuğu (`useAssessmentEngine`) — saf UI durumu

## Bilinen Veri Borçları (gerçek backend'de düzeltilecek)
- Mock offer/match embed'lerindeki uzman adları ("Dr. Ayşe Kaya") experts tablosuyla ünvan/id bazında tam tutarlı değil (77777… id'li uzman experts.json'da yok). Gerçek backend join ile üretir.
- `CreateListingForm` ve expert onboarding formları manuel validasyon kullanıyor; `CreateListingSchema`/`ExpertOnboardingSchema` Zod şemalarıyla React Hook Form'a taşınması önerilir (mesaj tekilleştirme).

*Son güncelleme: 18 Temmuz 2026*
