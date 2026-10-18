# Ödemeler — Uzman TL Bakiyesi ve Dinamik Teklif Ücreti

> Kanonik kaynak: `docs/REVIZE-2.md` (2026-10-19). Bu belge onun ödeme/cüzdan kısmının
> uygulama ayrıntısıdır; çelişkide REVIZE-2 geçerlidir. 2026-10-14'teki **kredi modeli**
> (kredi paketleri, `credit_balance`, `OFFER_CREDIT_COST`, `/credits/*`) kaldırıldı.

## Model

- **Danışandan para alınmaz.** Uzman bir ilana **teklif gönderirken** o anki **teklif ücretini**
  öder. Ücret ilanın kalitesine, yaşına ve aldığı teklif sayısına göre düşer (aşağıda).
- Uzmanın **TL bakiyesi** (kuruş, `bigint`) ve **ücretsiz teklif hakkı** vardır. Teklifte önce hak
  kullanılır (`OFFER_FREE_RIGHT`, tutar 0); hak yoksa ücret bakiyeden düşer (`OFFER_FEE`).
- Ödenen ücret **iade edilmez** (teklif geri çekilse, ilan kapansa, başka uzmanla eşleşse de).
  Telafi gerekirse finans yetkilisi **hediye** ya da **düzeltme** yapar (audit log'a düşer).
- Haklar: uzman profili admin tarafından **ilk kez onaylandığında** `WELCOME_FREE_RIGHTS` hak;
  davetle gelen uzman onaylanınca davet edene `REFERRAL_FREE_RIGHTS` hak (çift başına bir kez).
- Bakiye **yalnız web sitesinden** yüklenir (PayTR iFrame API). Uzman–danışan arasındaki seans
  ödemesi platformu ilgilendirmez.
- Tutarlar API'de her yerde **kuruş** (`*Kurus`) + hazır Türkçe etiket (`*Label`). Etiketlerde
  kuruş gösterilmez, binlik ayıracı nokta: `"₺1.250"`; işaretli etiketler `"+₺500"` / `"−₺625"`
  (eksi işareti U+2212) / `""`; hak etiketleri `"+1 hak"` / `"−1 hak"` / `""`.

## App Store / Google Play kuralı

Mobil uygulamada yükleme butonu, ödeme sayfasına bağlantı, fiyat listesi ya da "web'den
yüklerseniz…" yönlendirmesi **yoktur**. Uygulama yalnız şunları gösterir:

- bakiye ve hak (`GET /wallet/me`),
- hareketler (`GET /wallet/transactions`) ve faturalar (`GET /wallet/payments`),
- teklif öncesi ücret özeti (`GET /offers/quote`),
- düz metin: **"Bakiye yüklemek için web sitemizi ziyaret edin."** (tıklanabilir bağlantı değil).

Bakiye yetmezse `POST /offers` 422 `INSUFFICIENT_BALANCE` döner.

## Teklif ücreti

Hesap tek yerde: `PsikoAl.Services.Pricing.OfferFeeCalculator` (saf fonksiyon, birim testli).

```
başlangıç = fee_min + kalite × (fee_max − fee_min)                     (10 TL'ye yuvarlanır)
gün       = floor((şimdi − onay_zamanı) / 24 saat)
ücret     = max(fee_min, başlangıç × (1 − günlük%)^gün × (1 − teklif%)^teklif_sayısı)   (tam TL)
```

- Yuvarlama yarım değerde yukarı. `teklif_sayısı` = ilana gönderilmiş bütün teklifler (geri
  çekilenler dahil; `listings.offer_count`).
- Kalite puanı (0–1) `QualityScoreCalculator`'da (REVIZE-2 §1.1 ağırlıkları, dönemden okunur).
- İlan **admin onayından geçtiği anda** (ya da `LISTING_AUTO_APPROVE` ile oluşturulurken) kalite
  puanı ve o anki dönemin `fee_initial/min`, günlük/teklif yüzdeleri ilana **kopyalanır**
  (`listings.quality_score`, `fee_initial_kurus`, `fee_min_kurus`, `daily_decay_pct`,
  `per_offer_decay_pct`). Dönem değişse de yayındaki ilanın eğrisi değişmez. Revize 2 öncesi
  onaylanmış (kopyası olmayan) ilanların eğrisi geçerli dönemden aynı formülle türetilir.
- Uzmana **yalnız o anki ücret** gösterilir; bir sonraki düşüşün zamanı/tutarı hiçbir uçta dönmez.

### Fiyatlandırma dönemi (`pricing_periods`)

Geçerli dönem = `effective_from <= now()` olan en yeni satır. Admin yeni dönem **ekler**,
geçmiş dönemler düzenlenmez. Başlangıç dönemi migration ile gelir: bütçe alt sınırı 500 TL,
üst sınır yok, ücret 250–1000 TL, günlük %10, teklif başına %10.

## Veri

| Tablo / kolon | Not |
|---|---|
| `experts.balance_kurus` | `bigint`, `CHECK >= 0`; yalnız koşullu UPDATE ile değişir (EF bu kolonu yazmaz) |
| `experts.free_offer_rights` | `int`, `CHECK >= 0`; aynı koşullu UPDATE |
| `experts.referral_code` | 8 karakter, `A–Z 2–9` (0 O 1 I yok), benzersiz; insert trigger'ı üretir |
| `profiles.referred_by_expert_id` | Kayıtta davet koduyla dolar; kendine referans olamaz |
| `pricing_periods` | Dönemsel bütçe aralığı, ücret aralığı, yüzdeler, `quality_weights` (jsonb) |
| `wallet_payments` | `merchant_oid` (benzersiz, harf+rakam), `amount_kurus`, `status` `PENDING \| PAID \| FAILED`, `provider_payload` (hash/kart yok), `invoice_*` |
| `wallet_transactions` | Defter, append-only: `amount_kurus` (işaretli), `balance_after_kurus`, `free_rights_delta`, `free_rights_after`, `type`, `listing_id?`, `offer_id?`, `payment_id?`, `admin_user_id?`, `referred_expert_id?`, `reason?` |
| Storage `invoices` | Private bucket, yalnız PDF, ≤ 10 MB; yol `{expertId}/{paymentId}.pdf` |

Defter türleri: `TOPUP` · `OFFER_FEE` · `OFFER_FREE_RIGHT` · `WELCOME_RIGHT` · `REFERRAL_RIGHT` ·
`ADMIN_GIFT` · `ADMIN_ADJUST`. Idempotency'nin veritabanı katmanı (benzersiz kısmi indeksler):
ödeme başına tek `TOPUP`, teklif başına tek `OFFER_FEE`/`OFFER_FREE_RIGHT`, davet edilen uzman
başına tek `REFERRAL_RIGHT`, uzman başına tek `WELCOME_RIGHT`. `ADMIN_ADJUST` gerekçesiz yazılamaz.

Migration'lar: `supabase/migrations/20261019100000_tl_wallet.sql`,
`20261019100100_listing_fee_match_claims.sql`.

## Ayarlar (`system_settings`)

| Anahtar | Başlangıç | Not |
|---|---|---|
| `TOPUP_PRESETS_TRY` | `500,1000,2500,5000` | Web ödeme sayfasının hazır tutarları |
| `TOPUP_MIN_TRY` / `TOPUP_MAX_TRY` | `250` / `50000` | Yükleme aralığı (tam TL) |
| `WELCOME_FREE_RIGHTS` | `1` | 0 = kapalı |
| `REFERRAL_FREE_RIGHTS` | `1` | 0 = kapalı |
| `CLAIMS_ENABLED` | `true` | "Danışanla anlaştım" bildirimi |
| `CLAIM_ADMIN_REVIEW_DAYS` | `3` | Admin "İnceleme bekliyor" eşiği |
| `VAT_NOTE` | `KDV dahil` | Yükleme tutarlarının yanındaki not |

Panelden kaydedilen değerler tipine göre doğrulanır (bozuk değer 422). Ayar eksik/bozuksa
akış bu tablodaki başlangıç değerine düşer (ödeme ya da teklif 500'e düşmez).

## Uçlar

Hata gövdesi her yerde `{ code, message, field?, meta? }`. Yeni kullanıcı uçları `{ data }` zarfında.

### Uzman (mobil + web) — `Authorization: Bearer <token>`

| Uç | Yanıt `data` |
|---|---|
| `GET /wallet/me` | `{ balanceKurus, balanceLabel, freeOfferRights, referralCode, referralLink }` — `referralLink` = `${Site__PublicUrl}/davet/${kod}` |
| `GET /wallet/transactions?page&perPage&type` | `[{ id, type, typeLabel, amountKurus, amountLabel, freeRightsDelta, freeRightsLabel, balanceAfterKurus, balanceAfterLabel, description, listingId?, listingTitle?, offerId?, paymentId?, createdAt, timeLabel }]` + `meta { page, total, perPage }` (perPage ≤ 50, en yeni önce) |
| `GET /wallet/payments?page&perPage` | `[{ id, amountKurus, amountLabel, status, statusLabel, createdAt, completedAt?, invoice: { fileName, uploadedAt } \| null }]` + `meta` |
| `GET /wallet/payments/{id}/invoice` | `{ url }` — 5 dakikalık imzalı Storage adresi; fatura yoksa 404 `INVOICE_NOT_FOUND`, başkasının ödemesi 404 `WALLET_PAYMENT_NOT_FOUND` |
| `GET /offers/quote?listingId=` | `{ feeKurus, feeLabel, balanceKurus, balanceLabel, freeOfferRights, willUseFreeRight, balanceAfterKurus, balanceAfterLabel, sufficient }` |

`typeLabel`: Bakiye yükleme · Teklif ücreti · Ücretsiz hak kullanıldı · Hoş geldin hakkı ·
Davet hakkı · Hediye · Düzeltme. `statusLabel` (ödeme): Bekliyor · Ödendi · Başarısız.

### Web sitesi ödeme akışı

| Uç | Yetki | Gövde / yanıt |
|---|---|---|
| `GET /wallet/topup-options` | anonim | `{ data: { presets: [{ amountKurus, label }], minKurus, maxKurus, vatNote } }` |
| `POST /payments/paytr/init` | Bearer, **onaylı** uzman | `{ amountKurus }` → `{ data: { iframeToken, merchantOid, iframeUrl } }`; aralık dışı ya da kuruşlu tutar 422 `TOPUP_AMOUNT_OUT_OF_RANGE` (`meta { minKurus, maxKurus }`); onaysız uzman 403 `TOPUP_EXPERT_NOT_APPROVED` |
| `POST /payments/paytr/callback` | anonim, PayTR sunucusu | `application/x-www-form-urlencoded`; yanıt düz metin `OK` |

Web sitesinin yapacakları:

1. Uzman web sitesinde **aynı hesapla** giriş yapar (`POST /auth/login`).
2. `GET /wallet/topup-options` ile hazır tutarları ve aralığı gösterir (serbest tutar da girilebilir).
3. `POST /payments/paytr/init` çağrısını **tarayıcıdan** yapar (PayTR `user_ip` için kullanıcının
   gerçek IP'si gerekir; BFF kullanılırsa `X-Forwarded-For` iletilmeli ve API'nin
   `ForwardedHeaders__KnownProxies` listesine eklenmeli).
4. Dönen `iframeUrl`'i `<iframe>` içinde açar.
5. Kullanıcı `PayTR__OkUrl` / `PayTR__FailUrl` sayfasına döner. **Yönlendirme ödemenin kanıtı
   değildir**: bakiye yalnız PayTR'nin sunucudan sunucuya bildirimiyle (callback) yüklenir; başarı
   sayfası bakiyeyi `GET /wallet/me` ile birkaç saniye aralıkla yoklar.
6. `/davet/{kod}` sayfası kodu saklar ve kayıt formuna (`POST /auth/register` → `referralCode`) taşır.

Yükleme tamamlanınca uzmana uygulama içi + push bildirimi gider (`BALANCE_TOPUP`).

### Callback davranışı

- Hash: `base64(HMAC-SHA256(merchant_oid + merchant_salt + status + total_amount, merchant_key))`.
  Geçersizse 400 `INVALID_HASH` (düz metin) — `OK` **dönmez**.
- Bilinmeyen `merchant_oid` → 404 `UNKNOWN_ORDER` (log'a hata düşer).
- `status=success` ve `total_amount >= amount_kurus` → `PENDING → PAID`, bakiyeye TL + `TOPUP`
  defter satırı, tek transaction; ardından `BALANCE_TOPUP` bildirimi. `total_amount` küçükse
  ödeme `FAILED` sayılır ve log'a hata düşer.
- `status=failed` → `FAILED`, bakiye yok.
- **Idempotent:** `PENDING`'den çıkış koşullu UPDATE'tir; ikinci (ya da eşzamanlı) bildirim
  bakiyeyi tekrar yüklemez ve yine `OK` alır. Ayrıca ödeme başına tek `TOPUP` benzersiz indeksle korunur.

Kaynak: PayTR iFrame API [1. adım](https://dev.paytr.com/iframe-api/iframe-api-1-adim),
[2. adım (Bildirim URL)](https://dev.paytr.com/iframe-api/iframe-api-2-adim).

### Admin (finans) — `AdminFinance` policy: `super_admin`, `finance`

Admin uçları panelin mevcut sözleşmesini izler: tek nesne doğrudan, sayfalı listeler
`{ data, meta: { page, perPage, total, totalPages } }`.

| Uç | Not |
|---|---|
| `GET /admin/pricing-periods?page&perPage` | Dönemler, `isCurrent` işaretli |
| `POST /admin/pricing-periods` | `{ effectiveFrom?, budgetMinTry, budgetMaxTry?, feeMinTry, feeMaxTry, dailyDecayPct, perOfferDecayPct, qualityWeights: { description, title, budget, location, sessionBoth, specialization, descriptionFullChars, titleFullChars, budgetFullMultiplier }, note? }`; geçmiş tarih verilemez |
| `POST /admin/pricing-periods/preview` | `{ period?, listing: { title?, description?, budgetMax, city?, sessionType, specializationCount } }` → `{ qualityScore, breakdown, initialFeeKurus, initialFeeLabel, feeMin*, feeMax*, schedule: [{ days, offers, feeKurus, feeLabel }] }` (`period` yoksa geçerli dönem; `schedule` gün 0/1/3/7/14 × teklif 0/1/3/5 ızgarası, yalnız admin önizlemesi) |
| `GET /admin/wallet/payments?status&hasInvoice&page&perPage` | Ödemeler + uzman + `invoice` |
| `POST /admin/wallet/payments/{id}/invoice` | multipart `file` (PDF, ≤ 10 MB, imza `%PDF` kontrolü); yalnız `PAID`; tekrar yükleme üzerine yazar; uzmana `INVOICE_READY` |
| `GET /admin/wallet/payments/{id}/invoice` | `{ url }` (5 dk imzalı adres, panelde görüntüleme/indirme); fatura yoksa 404 `INVOICE_NOT_FOUND` |
| `GET /admin/wallet/experts/{id}?page&perPage` | `{ expertId, fullName, email, balanceKurus, balanceLabel, freeOfferRights, referralCode, transactions: { data, meta } }` |
| `POST /admin/wallet/experts/{id}/gift` | `{ amountKurus ≥ 0 (tam TL), freeRights ≥ 0, reason? }` (en az biri > 0) → aynı yanıt; uzmana `ADMIN_GIFT` |
| `POST /admin/wallet/experts/{id}/adjust` | `{ amountKurus (işaretli), freeRightsDelta (işaretli), reason (zorunlu) }`; eksiye düşerse 422 `WALLET_BALANCE_NEGATIVE` |
| `GET /admin/referrals?page&perPage` | Kim kimi davet etti, ödül yazıldı mı |

Tüm yazma işlemleri `audit_logs`'a düşer (`admin.wallet_gift`, `admin.wallet_adjust`,
`admin.invoice_upload`, `admin.pricing_period_create`). Ayarlar `PATCH /admin/system-settings/{key}`
(yalnız `super_admin`).

## Teklif gönderimi (atomiklik)

`POST /offers` gövdesi `expectedFeeKurus` taşır (uzmanın `/offers/quote`'ta gördüğü ücret).
Kurallar (uzman onaylı, ilan açık, aynı ilana tek teklif) **önce** çalışır. Sonra tek transaction'da:

1. İlan satırı kilitlenir (`select offer_count ... for update`): ücret teklif sayısına bağlı,
   eşzamanlı iki teklif aynı ücreti göremez.
2. Anlık ücret `expectedFeeKurus`'tan farklıysa (ya da alan yoksa) hiçbir şey yazılmadan
   409 `OFFER_FEE_CHANGED`, `meta: { feeKurus, feeLabel }`.
3. Hak varsa `free_offer_rights - 1`, yoksa
   `update experts set balance_kurus = balance_kurus - @fee where id = @id and balance_kurus - @fee >= 0`
   — satır dönmezse 422 `INSUFFICIENT_BALANCE`.
4. Teklif (`fee_paid_kurus`, `used_free_right`) + defter satırı + `offer_count + 1`.

Teklif yazılamazsa (eşzamanlı ikinci teklif benzersizlik kısıtına takılırsa) düşüm geri alınır.
Başarı yanıtı teklif + `clientContact` (danışanın ad soyad, telefon, e-posta — ilan açarken verdiği
açık rızaya dayanır).

## Hata kodları

| Kod | HTTP | Mesaj |
|---|---|---|
| `INSUFFICIENT_BALANCE` | 422 | Teklif göndermek için bakiyeniz yetersiz. |
| `OFFER_FEE_CHANGED` | 409 | Teklif ücreti güncellendi. Yeni ücreti kontrol edip tekrar gönderin. |
| `TOPUP_AMOUNT_OUT_OF_RANGE` | 422 | Yükleme tutarı izin verilen aralıkta değil. |
| `TOPUP_EXPERT_NOT_APPROVED` | 403 | Bakiye yüklemek için uzman profilinizin onaylanması gerekiyor. |
| `WALLET_PAYMENT_NOT_FOUND` | 404 | Ödeme bulunamadı. |
| `INVOICE_NOT_FOUND` | 404 | Bu ödeme için henüz fatura yüklenmedi. |
| `INVOICE_PAYMENT_NOT_PAID` | 422 | Fatura yalnız tamamlanmış ödemelere yüklenebilir. |
| `WALLET_BALANCE_NEGATIVE` | 422 | Bu düzeltme uzmanın bakiyesini ya da hakkını eksiye düşürür. |
| `WALLET_ADJUST_REASON_REQUIRED` | 422 | Düzeltme için gerekçe yazmanız gerekiyor. |
| `PAYMENT_PROVIDER_UNAVAILABLE` | 422 | Ödeme servisine şu an ulaşılamıyor. Biraz sonra tekrar deneyin. |
| `EXPERT_ROLE_REQUIRED` | 403 | Danışan hesabı cüzdan ya da ödeme uçlarını çağırırsa |

Gövde doğrulama hataları (ör. `amountKurus` ≤ 0) mevcut kural gereği 400 ValidationProblemDetails
döner. Rate limit: `init` kullanıcı başına 10/dk, `callback` IP başına 300/dk, fatura yükleme 20/dk (429).

## Yapılandırma

| Anahtar | Not |
|---|---|
| `PayTR__MerchantId` | Mağaza No |
| `PayTR__MerchantKey` | Mağaza Parola — gizli, yalnız ortam değişkeni |
| `PayTR__MerchantSalt` | Mağaza Gizli Anahtar — gizli, yalnız ortam değişkeni |
| `PayTR__TestMode` | `true` → PayTR `test_mode=1` (+ `debug_on=1`) |
| `PayTR__OkUrl` / `PayTR__FailUrl` | Web sitesinin dönüş sayfaları (tam adres) |
| `Site__PublicUrl` | Web sitesinin kök adresi; davet linki `${Site__PublicUrl}/davet/${kod}` |

Anahtarlar appsettings dosyalarına yazılmaz. PayTR üçlüsü boşsa ödeme uçları
`PAYMENT_PROVIDER_UNAVAILABLE` döner, callback her bildirimi reddeder. Render kurulumu:
`docs/DEPLOY.md`.

## PayTR Mağaza Paneli ayarları

1. **Bildirim URL'si** (Destek & Kurulum → Ayarlar → Bildirim URL):
   `https://<api-alan-adı>/payments/paytr/callback` — ör.
   `https://psikoal-api.onrender.com/payments/paytr/callback`. HTTPS zorunlu; adres oturum
   ya da giriş istememeli (uç anonimdir).
2. Entegrasyon bilgileri (Mağaza No / Parola / Gizli Anahtar) → Render ortam değişkenleri.
3. **Test modu:** canlı mağazada `PayTR__TestMode=true` iken PayTR'nin test kartlarıyla
   uçtan uca deneme yapılır; canlı satışa geçerken `false`.
4. `merchant_ok_url` / `merchant_fail_url` her istekte API'den gider (`PayTR__OkUrl`,
   `PayTR__FailUrl`); panelde ayrıca girilmez.
5. Taksit kapalı gönderilir (`no_installment=1`).
