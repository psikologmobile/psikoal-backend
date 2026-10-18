# Revize 2: TL bakiye, dinamik teklif ücreti, eşleşme işaretleme, davet

> Kanonik sözleşme. Ürün kararları 2026-10-16 toplantısı + 2026-10-18 müşteri mesajı +
> 2026-10-19 netleştirmeleri. `docs/PAYMENTS.md` bu belgeyle **güncellenir**; çelişkide bu
> belge geçerlidir. Mobil/web uygulama ve admin paneli bu sözleşmeye göre yazılır.

## 0. Özet: ne değişti

| Eski | Yeni |
|---|---|
| Uzman **kredisi** (`credit_balance`, kredi paketleri) | Uzman **TL bakiyesi** (kuruş, `bigint`) + **ücretsiz teklif hakkı** |
| Teklif maliyeti sabit `OFFER_CREDIT_COST` | **Dinamik teklif ücreti**: ilan kalite puanı → başlangıç ücreti; her 24 saatte ve her yeni teklifte yüzde düşüş; dönemsel admin min/max |
| Danışan bütçesi serbest | Danışan bütçesi **dönemsel genel aralıkta** (admin: min, max ya da sınırsız) |
| Danışan teklifi kabul eder / reddeder | Danışan ilanını **"X uzmanla eşleşti"** işaretler; kabul/ret uçları kalkar |
| İletişim yalnız eşleşmede | Uzman **teklif gönderdiği anda** danışanın ad soyad, telefon, e-postasını görür (ilan açarken açık rıza) |
| Eşleşme iki tarafın onayıyla sonlandırılır | Eşleşme **sonlandırılamaz** (kullanıcı ucu kalkar; admin ucu kalır) |
| Değerlendirme eşleşme bitince | Değerlendirme **eşleşir eşleşmez** açık |
| Uzman profilleri | Danışan yalnız **kendi ilanına teklif veren** ya da **kendisini davet eden** uzmanın profilini görür |
| — | **Davet linki**: her uzmanın kodu. Danışan getirirse → o danışanın ilanlarında "Davet" rozeti; uzman getirirse → onaylanınca davet edene ücretsiz teklif hakkı (otomatik) |
| — | **Hoş geldin hakkı**: uzman onaylanınca N ücretsiz teklif hakkı |
| — | **"Danışanla anlaştım" bildirimi** (şikayet): uzman ilan başına bir kez; danışana hatırlatma; admin incelemesi |
| — | **Faturalar**: admin her ödeme için PDF yükler, uzman "Faturalarım"da indirir |
| — | **Kişi bazlı işlem kaydı**: admin her kullanıcının bütün hareketlerini tarih/saatle görür |

Para birimi her yerde **kuruş** (`*Kurus`, `bigint`) ve hazır Türkçe etiket (`*Label`, ör. `"₺1.250"`).
Kuruşlu tutar gösterilmez; ücretler tam TL'ye yuvarlanır.

## 1. Fiyatlandırma dönemi (admin, dönemsel)

`pricing_periods` tablosu. Her satır bir dönem; **geçerli dönem** = `effective_from <= now()` olan en yeni
satır. Admin yeni dönem ekler (geçmişe dokunmaz). Başlangıç dönemi migration ile eklenir.

| Alan | Başlangıç | Anlam |
|---|---|---|
| `effective_from` | migration anı | Dönem başlangıcı |
| `budget_min_try` | 500 | Danışan bütçe alt sınırı (seans başı) |
| `budget_max_try` | `null` | Üst sınır; `null` = sınırsız |
| `fee_min_try` | 250 | Teklif ücretinin tabanı (dip fiyat) |
| `fee_max_try` | 1000 | Teklif ücretinin tavanı (en nitelikli, yeni ilan) |
| `daily_decay_pct` | 10 | Her tam 24 saatte düşüş (%) |
| `per_offer_decay_pct` | 10 | Her yeni teklifte düşüş (%) |
| `quality_weights` | aşağıda | Kalite puanı ağırlıkları (jsonb) |
| `note` | — | Admin notu |

### 1.1 Kalite puanı (0–1)

İlan **admin onayından geçtiği anda** hesaplanır ve ilana yazılır (`quality_score`, ve o anki dönemden
`fee_initial_kurus`, `fee_min_kurus`, `daily_decay_pct`, `per_offer_decay_pct` **anlık kopyası**). Böylece
dönem değişse de yayındaki ilanın ücret eğrisi değişmez.

Ağırlıklar (toplamı 1'e normalize edilir), başlangıç değerleri:

| Anahtar | Ağırlık | Tam puan koşulu | Kısmi |
|---|---|---|---|
| `description` | 0.30 | açıklama ≥ 400 karakter | doğrusal (0 → 400) |
| `title` | 0.10 | başlık ≥ 30 karakter | doğrusal (10 → 30) |
| `budget` | 0.25 | `budgetMax` ≥ 4 × `budget_min_try` | doğrusal (`budget_min` → 4×`budget_min`) |
| `location` | 0.15 | şehir girilmiş | 0/1 |
| `sessionBoth` | 0.10 | görüşme şekli `yüz_yüze_online` | 0/1 |
| `specialization` | 0.10 | ≥ 2 alan | 1 alan = 0.5 |

Admin ağırlıkları ve eşikleri (`descriptionFullChars`, `titleFullChars`, `budgetFullMultiplier`) dönem
içinde düzenler.

### 1.2 Teklif ücreti

```
başlangıç = fee_min + kalite × (fee_max − fee_min)                 (onay anında, 10 TL'ye yuvarlanır)
gün       = floor((şimdi − onay_zamanı) / 24 saat)
ücret     = max(fee_min, başlangıç × (1 − günlük%)^gün × (1 − teklif%)^teklif_sayısı)   (tam TL'ye yuvarla)
```

- `teklif_sayısı` = ilana gönderilmiş, **geri çekilmiş dahil** bütün teklifler (ücret ödenmiş olay sayısı).
- Uzmana **yalnız o anki ücret** gösterilir. Bir sonraki düşüşün zamanı ve tutarı **hiçbir uçta dönmez**
  (müşteri kararı: "kumar sitesi gibi olmasın").
- Hesap tek yerde: `OfferFeeCalculator` (saf fonksiyon, birim testli).

## 2. Uzman cüzdanı

### 2.1 Veri

- `experts.balance_kurus bigint >= 0`, `experts.free_offer_rights int >= 0`, `experts.referral_code text unique`
  (8 karakter, büyük harf + rakam, karışabilecek `0 O 1 I` yok).
- `profiles.referred_by_expert_id uuid null` (kayıtta kodla gelir; kendine referans olamaz).
- `wallet_transactions` (append-only, eski `credit_transactions`'ın yerine):
  `amount_kurus bigint` (işaretli, 0 olabilir), `balance_after_kurus`, `free_rights_delta int`,
  `free_rights_after int`, `type`, `listing_id?`, `offer_id?`, `payment_id?`, `admin_user_id?`,
  `referred_expert_id?`, `reason?`, `created_at`.
  `type`: `TOPUP` · `OFFER_FEE` · `OFFER_FREE_RIGHT` · `WELCOME_RIGHT` · `REFERRAL_RIGHT` ·
  `ADMIN_GIFT` · `ADMIN_ADJUST`.
- `wallet_payments` (eski `credit_payments`): `amount_kurus`, `merchant_oid`, `status` `PENDING|PAID|FAILED`,
  `provider_payload`, `invoice_path?`, `invoice_file_name?`, `invoice_uploaded_at?`, `invoice_uploaded_by?`.
- Eski `credit_*` tabloları ve `experts.credit_balance` kaldırılır (canlıda boş, 2026-10-19 doğrulandı).
- Ayarlar (`system_settings`): `TOPUP_PRESETS_TRY` = `500,1000,2500,5000`, `TOPUP_MIN_TRY` = `250`,
  `TOPUP_MAX_TRY` = `50000`, `WELCOME_FREE_RIGHTS` = `1`, `REFERRAL_FREE_RIGHTS` = `1`,
  `CLAIMS_ENABLED` = `true`, `CLAIM_ADMIN_REVIEW_DAYS` = `3`, `VAT_NOTE` = `KDV dahil`.
  `OFFER_CREDIT_COST` silinir.

### 2.2 Uçlar (uzman, Bearer)

| Uç | Yanıt `data` |
|---|---|
| `GET /wallet/me` | `{ balanceKurus, balanceLabel, freeOfferRights, referralCode, referralLink }` (`referralLink` = `${Site:PublicUrl}/davet/${code}`) |
| `GET /wallet/transactions?page&perPage&type` | `[{ id, type, typeLabel, amountKurus, amountLabel ("+₺500" / "−₺625" / ""), freeRightsDelta, freeRightsLabel ("+1 hak" / "−1 hak" / ""), balanceAfterKurus, balanceAfterLabel, description, listingId?, listingTitle?, offerId?, paymentId?, createdAt, timeLabel }]`, `meta {page,total,perPage}` (perPage ≤ 50) |
| `GET /wallet/payments?page&perPage` | `[{ id, amountKurus, amountLabel, status, statusLabel, createdAt, completedAt?, invoice: { fileName, uploadedAt } \| null }]` |
| `GET /wallet/payments/{id}/invoice` | `{ url }` (5 dk imzalı Storage adresi; fatura yoksa 404 `INVOICE_NOT_FOUND`) |
| `GET /wallet/topup-options` (anonim) | `{ presets: [{ amountKurus, label }], minKurus, maxKurus, vatNote }` |
| `POST /payments/paytr/init` | gövde `{ amountKurus }` → `{ iframeToken, merchantOid, iframeUrl }`; aralık dışı 422 `TOPUP_AMOUNT_OUT_OF_RANGE`; onaysız uzman 403 |
| `POST /payments/paytr/callback` | değişmedi (TL tutarı yükler, `TOPUP` satırı, `BALANCE_TOPUP` bildirimi) |

`typeLabel`: Bakiye yükleme · Teklif ücreti · Ücretsiz hak kullanıldı · Hoş geldin hakkı · Davet hakkı ·
Hediye · Düzeltme.

### 2.3 Haklar

- **Hoş geldin:** uzman profili admin tarafından **ilk kez onaylandığında** `WELCOME_FREE_RIGHTS` kadar hak
  (`WELCOME_RIGHT`), bildirim `WELCOME_RIGHTS`.
- **Davet:** onaylanan uzmanın `referred_by_expert_id`'si varsa ve o çift için daha önce ödül yazılmadıysa
  (benzersiz indeks) davet edene `REFERRAL_FREE_RIGHTS` hak (`REFERRAL_RIGHT`, `referred_expert_id`),
  bildirim `REFERRAL_REWARD`.
- **Teklifte:** `free_offer_rights > 0` ise önce hak kullanılır (`OFFER_FREE_RIGHT`, tutar 0); yoksa ücret
  bakiyeden düşer (`OFFER_FEE`).

## 3. İlan

### 3.1 Kurallar ucu

`GET /listings/rules` (Bearer) → `{ budgetMinTry, budgetMaxTry \| null, titleMin: 10, titleMax: 100,
descriptionMax: 2000, cityRequiredFor: ["yüz_yüze","yüz_yüze_online"], maxActive: 3 }`.

### 3.2 Oluşturma `POST /listings`

Eklenen alanlar: `city` (il adı; görüşme şekli yüz yüze içeriyorsa zorunlu), `contactConsent: true`
(zorunlu, `false`/yoksa 422 `CONTACT_CONSENT_REQUIRED`), `phone?` (profilde telefon yoksa zorunlu;
gelirse profile yazılır; yoksa 422 `PHONE_REQUIRED`). Bütçe aralık dışı 422 `LISTING_BUDGET_OUT_OF_RANGE`.
`description` en çok 2000. Rıza zamanı `listings.contact_consent_at`'e yazılır.

Danışan bir uzmanın davetiyle geldiyse (`referred_by_expert_id`) ilana `invited_expert_id` yazılır; ilan
**onaylanınca** o uzmana `INVITED_CLIENT_LISTING` bildirimi gider.

### 3.3 İlan DTO'suna eklenenler

Ortak: `city`, `qualityScore` (yalnız admin), `offerCount`, `status`.

Uzman görünümü (`GET /listings`, `GET /listings/{id}`):
`offerFeeKurus`, `offerFeeLabel` (o anki ücret), `viewerHasOffered`, `viewerOfferId?`, `invitedByViewer`
(bool), `viewerClaim: { createdAt } | null`, `clientContact: { fullName, phone, email } | null`
(yalnız `viewerHasOffered`), `matchedWithOther` (bool).
Feed süzgeçlerine `city` ve `invitedOnly=true`, sıralamaya `fee_asc` eklenir.

Danışan görünümü: `matchedExpert: { id, name } | null`,
`claims: [{ expertId, expertName, createdAt }]` (kapatılmamış "anlaştık" bildirimleri), `contactConsentAt`.

### 3.4 Eşleşti işaretleme

`POST /listings/{id}/match` gövde `{ offerId }` (danışan, ilan sahibi, ilan `OPEN`, teklif bu ilana ait ve
`PENDING`) → `{ matchId }`.

- Match `ACTIVE` oluşur; ilan `MATCHED`; seçilen teklif `ACCEPTED`; diğer `PENDING` teklifler `REJECTED`
  (`closed_reason = 'OTHER_EXPERT'`). Hepsi tek işlemde.
- Seçilen uzmana `MATCHED` (entityId = matchId), diğerlerine `LISTING_MATCHED_OTHER` (entityId = listingId).
- Hatalar: 404, 403, 409 `LISTING_NOT_OPEN`, 422 `OFFER_NOT_ON_LISTING`.

`POST /offers/{id}/accept` ve `/reject` **kaldırılır**. İlan kapatma (`/close`) ve süre dolması diğer
`PENDING` teklifleri `REJECTED` + `closed_reason = 'LISTING_CLOSED' | 'LISTING_EXPIRED'` yapar.

### 3.5 "Anlaştık" bildirimi (şikayet)

- `POST /listings/{id}/claims` (uzman) gövde `{ note? (≤300) }` → 201. Koşul: uzman bu ilana teklif vermiş,
  ilan `OPEN`, `CLAIMS_ENABLED`. İkinci kez 409 `CLAIM_ALREADY_EXISTS`; koşul yok 422 `CLAIM_NOT_ALLOWED`.
- Her bildirim danışana `CLAIM_REMINDER` (entityId = listingId).
- `POST /listings/{id}/claims/{expertId}/dismiss` (danışan, "Anlaşmadım") → hatırlatmayı kapatır.
- İlan `CLAIM_ADMIN_REVIEW_DAYS` gün içinde eşleşmezse admin "Anlaşma bildirimleri" listesinde
  "İnceleme bekliyor" olur. Admin `resolve` eder (not ile).
- Ücret iadesi yoktur.

## 4. Teklif

- `GET /offers/quote?listingId=` (uzman) → `{ feeKurus, feeLabel, balanceKurus, balanceLabel,
  freeOfferRights, willUseFreeRight, balanceAfterKurus, balanceAfterLabel, sufficient }`.
- `POST /offers` gövdesine `expectedFeeKurus` eklenir. Anlık ücret farklıysa 409 `OFFER_FEE_CHANGED`
  (`meta: { feeKurus, feeLabel }`); yetersizse 422 `INSUFFICIENT_BALANCE`. Başarı yanıtı teklif +
  `clientContact`.
- Teklif DTO'suna: `feePaidKurus`, `feePaidLabel` ("Ücretsiz hak" ya da "₺625"), `usedFreeRight`,
  `closedReason`, `statusLabel` (Gönderildi · Seçildi · Başka uzmanla eşleşti · İlan kapandı ·
  Süresi doldu · Geri çekildi), `claimed` (bool).
- Geri çekme kalır; ücret iade edilmez.

## 5. Eşleşme

- Kullanıcı `POST /match/{id}/release` ucu kaldırılır (admin ucu kalır).
- Eşleşme detayında iki taraf da karşı tarafın `phone` ve `email`'ini görür (uzman profilindeki telefon,
  auth e-postası).
- Değerlendirme (`POST /experts/{id}/reviews`) eşleşme `ACTIVE` iken açıktır.

## 6. Uzman profili görünürlüğü

`GET /experts/{id}` ve `/reviews`: çağıran **danışansa** yalnız (a) uzman danışanın bir ilanına teklif
vermişse ya da (b) danışanın `referred_by_expert_id`'si bu uzmansa 200; aksi 404 `EXPERT_NOT_FOUND`.
Uzman kendi profilini görür. (Admin ayrı uçlarla.)

`GET /me/referrer` (danışan) → `{ id, name, title, avatarUrl, initials } | null`.

## 7. Davet (referans)

- `POST /auth/register` gövdesine `referralCode?` eklenir. Geçersiz kod sessizce yok sayılır.
- Web: `/davet/{code}` sayfası kodu saklar ve kayda yönlendirir; mobil kayıt formunda "Davet kodu"
  (isteğe bağlı) alanı.
- `GET /wallet/me` kodu ve linki döner (yalnız uzman).

## 8. Bildirim türleri (yeni)

`MATCHED` (uzman) · `LISTING_MATCHED_OTHER` (uzman) · `CLAIM_REMINDER` (danışan) · `INVITED_CLIENT_LISTING`
(uzman) · `BALANCE_TOPUP` · `ADMIN_GIFT` · `WELCOME_RIGHTS` · `REFERRAL_REWARD` · `INVOICE_READY`.
`OFFER_ACCEPTED` artık kullanılmaz (yerine `MATCHED`). Hepsi `entityId` taşır (yoksa `null`).

## 9. Admin

| Sayfa | İşlev | Uç |
|---|---|---|
| Fiyatlandırma dönemleri | Liste, geçerli dönem vurgulu; yeni dönem ekle (tüm alanlar + ağırlıklar); önizleme hesaplayıcı (örnek ilan → başlangıç ücreti) | `GET/POST /admin/pricing-periods`, `POST /admin/pricing-periods/preview` |
| Bakiye ve ödemeler | Ödemeler (durum, fatura var/yok süzgeci), **fatura yükle** (PDF ≤ 10 MB) | `GET /admin/wallet/payments`, `POST /admin/wallet/payments/{id}/invoice` |
| Uzman cüzdanı | Bakiye, haklar, defter; **hediye** (bakiye ve/veya hak), **düzeltme** (gerekçe zorunlu) | `GET /admin/wallet/experts/{id}`, `POST .../gift`, `POST .../adjust` |
| Kullanıcı işlem kaydı | Kişi seç → zaman çizelgesi: kayıt, giriş, ilan, teklif (ödenen ücret/hak), eşleşme, bakiye hareketi, bildirim, anlaşma bildirimi, admin işlemleri; tarih/saat; süzgeç ve CSV dışa aktarma | `GET /admin/users/{id}/activity?from&to&kind` |
| Anlaşma bildirimleri | Açık / inceleme bekliyor / çözüldü; ilan, uzman, danışan; çözüm notu | `GET /admin/claims`, `POST /admin/claims/{id}/resolve` |
| Davetler | Kim kimi davet etti, ödül yazıldı mı | `GET /admin/referrals` |
| Ayarlar | §2.1'deki anahtarlar | mevcut |

Admin işlemleri audit log'a yazılır; finans işlemleri `finance` ya da `super_admin` rolü ister.

## 10. Kaldırılanlar

`/credits/*` uçları, kredi paketleri admin sayfası, `OFFER_CREDIT_COST`, `/offers/{id}/accept|reject`,
kullanıcı `release` ucu, uygulamadaki kredi ekranları.
