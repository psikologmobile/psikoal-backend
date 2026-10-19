# PsikoAl — Gerçek Backend + Admin Panel Yol Haritası

## Context

Expo frontend (native-atomic) tamamen mock API (mock-db) üzerinde çalışıyor; tüm hook'lar gerçek REST sözleşmesine Zod doğrulamalı yazılmış. Şimdi gerçek altyapı kurulacak: **Supabase** (Postgres + Auth + Roles + Storage + e-posta) ve **C# backend + Blazor Server admin panel** (`csharp-clean-code` skill'i standartlarıyla). Prensip: Supabase'in etinden sütünden faydalan (RLS, trigger, DB fonksiyonları, transaction, storage, e-posta) — ama hesaplamalar C# backend'de; frontend hiçbir şey hesaplamaz. Landing page en sona (kapsam dışı).

Sözleşme kaynakları: `docs/BACKEND_REQUIREMENTS.md`, `docs/ADMIN_PANEL_REQUIREMENTS.md` (her `mock-db/data/*.json` = bir tablo + yeni tablolar), davranış referansı `mock-db/handlers/`, contract kaynağı `native-atomic/src/domains/*/schemas/` (Zod).

## Verilen Kararlar

| Karar | Seçim |
|---|---|
| Frontend auth | **C# proxy**: frontend mevcut `/auth/*` endpoint'lerini aynen kullanır; C# arkada Supabase Auth (GoTrue Admin API) çağırır, Supabase JWT üretir/yeniler. Frontend auth domain'i değişmez. |
| Admin panel | **Blazor Server** — aynı solution'da, Application katmanını doğrudan kullanır |
| Repo düzeni | **Ayrı repo `psikoal-backend`**: `/supabase` (SQL migration'lar) + `/src` (C# solution) + CI. Frontend `psikoal-app` reposunda kalır. |
| İlk dilim | **Auth + Profiller** (en riskli entegrasyon en önce) |
| DB erişimi | **EF Core (Npgsql)** ORM — sorgu/transaction için. Şema kaynağı **Supabase CLI SQL migration'ları** (RLS, trigger, fonksiyon SQL'de birinci sınıf); EF migrations kapalı, entity'ler elle/scaffold. Entity↔şema sapması CI'da scaffold-diff uyum testiyle yakalanır. *(Kullanıcıyla konuşuldu ve onaylandı.)* |
| Geliştirme kolaylığı | **Supabase MCP kurulumu önerilir** (kullanıcı kuracak) — migration uygulama, tablo inceleme, SQL çalıştırmayı benim doğrudan yapabilmem için. ORM zaten EF Core; MCP ek geliştirme konforu. |

## Postgres ↔ C# sınırı

- **Postgres'te**: çok tablolu atomik değişimler (örn. `accept_offer()` DB fonksiyonu: teklif kabul + diğer PENDING'ler REJECTED + listing→MATCHED + match insert, tek transaction), CHECK/UNIQUE constraint'ler ("aynı ilana 1 teklif", LISTING_MAX_ACTIVE), RLS politikaları, `pg_cron` ile ilan expiry (30 gün), rating agregasyonu (onaylı yorumlardan view/fonksiyon), `auth.users` insert trigger'ı ile profil satırı.
- **C#'ta**: response envelope + computed sunum alanları (maskeleme `"Zeynep Y."`, `initials`, `budgetLabel`, `timeLabel`/relative time, meta sayaçları), assessment skorlama (`assessment_score_rules` tablosundan), PayTR iFrame entegrasyonu + bildirim (callback) doğrulaması, şablonlu bildirim/e-posta orkestrasyonu, audit log yazımı, admin iş akışları.

## Fazlar

### Phase 0 — Temel Mimari (Foundation) → Review 0
1. GitHub repo: `psikoal-backend` (kullanıcı `gh` ile açar/açtırır). Yapı: `/supabase/migrations`, `/src`, `/.github/workflows`.
2. Supabase projesi + Storage bucket'ları (`avatars`, `documents`, `blog-media`) — kullanıcı Supabase hesabında oluşturur; ben SQL/policy'leri yazarım.
3. **`csharp-clean-code` skill'i yüklenerek** solution iskeleti: `PsikoAl.Domain` / `PsikoAl.Application` / `PsikoAl.Infrastructure` (EF Core + Supabase servisleri) / `PsikoAl.Api` (REST) / `PsikoAl.AdminWeb` (Blazor Server) + test projeleri.
4. İlk (`bootstrap`) migration: `system_settings`, `audit_logs`, `admin_users` + ortak enum/extension'lar.
5. Api tarafında Supabase JWT doğrulama + `/auth/login|register|refresh` proxy'sinin iskeleti + `whoami` ucu (uçtan uca token akışı kanıtı).
6. CI: build + test + migration lint. Contract-test iskeleti: frontend Zod şemalarından türetilen yanıt doğrulaması (her dilimin geçiş kriteri).

### Feature Dilimleri (her biri: **DB migration → C# API → Admin panel → Frontend bağlama → Review**)
1. **Auth + Profiller** — users/profiles tabloları, RLS, `/auth/*` proxy tam seti (login, register, refresh, freeze, delete, change/forgot password, PATCH me), avatar upload (Storage), Admin: kullanıcı listesi + dondurma/soft delete (KVKK anonimleştirme). Frontend: `EXPO_PUBLIC_API_URL` gerçek API'ye, mock kapalı smoke test.
2. **Uzman onboarding + versiyonlu onay** — experts tablosu + `pendingRevision`, belge yükleme (CV/sertifika → Storage; frontend'deki "yakında" stub'ları gerçeğe bağlanır), Admin: onay kuyruğu + belge görüntüleyici + alan diff + isVerified/acceptsOffers.
3. **Kategoriler + Keşfet + Yorum moderasyonu** — categories (17 kayıt, tek kaynak), reviews + `status` + rating recalc, Admin: kategori CRUD + yorum kuyruğu.
4. **İlan akışı + onay** — listings + `PENDING_APPROVAL→OPEN→MATCHED→CLOSED/EXPIRED` state machine, pg_cron expiry, LISTING_MAX_ACTIVE constraint, feed filtre/sıralama, Admin: ilan onay kuyruğu + system_settings ekranı. Frontend: `PENDING_APPROVAL/REJECTED_BY_ADMIN` durumları şemalara eklenir.
5. **Teklif + Eşleşme** — offers/matches, `accept_offer()` DB fonksiyonu (tek transaction), iki taraflı release onayı (`clientReleasedAt`/`expertReleasedAt` — mock'taki tek taraflı davranış düzeltilir), eşleşme öncesi ad maskeleme (uzmana tam ad hiç gönderilmez), Admin: teklif iptal + ihtilaf çözümü (zorla RELEASED/COMPLETED).
6. **Bildirimler + e-posta** — notifications(+userId), `notification_templates`, olay tabanlı üretim (outbox pattern: offer accept vb. dilim-5 olayları burada bağlanır), Expo push kaydı, Admin: şablon CRUD + manuel/segment gönderim. **E-posta iki ayrı yoldan gider ve bu bilinçlidir:** auth e-postaları (şifre sıfırlama, doğrulama, e-posta değişikliği) **Supabase GoTrue**'dan — şablonlar `supabase/config.toml` + `supabase/templates/*.html` içinde Türkçeleştirilmiştir; bildirim e-postaları ise **Brevo**'dan (`BrevoEmailService`). Brevo, dilim 6 uygulanırken eklendi (commit `11d342a`); seçim gerekçesi hiçbir yerde belgelenmemiş — bu satır yalnız BUGÜNKÜ durumu kaydediyor, kararın nedenini değil. **Bugünkü durum:** Brevo kodu hazır ama DEVREDE DEĞİL — `Brevo:ApiKey` boş ve on `notification_templates` satırının hepsinde `email_enabled = false`. Yani bildirimler yalnız in-app + push olarak gidiyor.
7. **Assessment** — assessments + `assessment_score_rules` (skorlama DB kuralından C#'ta), sonuçlar kullanıcıya bağlı, Admin: test/soru/puan kuralı CRUD.
8. **Uzman kredisi (Armut modeli)** — *model 2026-10-14'te değişti; eski danışan ödemesi (cüzdan, seans paketi, Iyzico 3DS, iade kuyruğu) terk edildi.* Danışandan para alınmaz; uzman teklif gönderirken `OFFER_CREDIT_COST` kadar kredi harcar (iade yok, başlangıç kredisi yok). `experts.credit_balance` + `credit_packages` + `credit_payments` + append-only `credit_transactions` defteri; teklif + koşullu düşüm + defter tek transaction'da. Kredi yalnız web sitesinden PayTR iFrame API ile alınır (`/payments/paytr/init` + idempotent `/payments/paytr/callback`); mobilde satın alma yolu yok. Admin: finans rolü (`AdminFinance`), kredi paketleri CRUD, satın almalar, uzman defteri + gerekçeli elle düzeltme. Sözleşme: `docs/PAYMENTS.md`.
   - **Durum (2026-10-15):** backend + admin paneli tamam (migration `20261015100000_expert_credits.sql`). Bekleyen: web sitesinin ödeme sayfası, PayTR mağaza başvurusu ve panel ayarları, mobil uygulamada bakiye/geçmiş ekranı.
   - **Revize 2 (2026-10-19):** kredi modeli yerine TL bakiye + ücretsiz teklif hakkı + dinamik teklif ücreti (dönemsel fiyatlandırma, kalite puanı), eşleşti işaretleme, "danışanla anlaştım" bildirimi, davet kodu, faturalar ve kişi bazlı işlem kaydı. Migration'lar `20261019100000_tl_wallet.sql`, `20261019100100_listing_fee_match_claims.sql`; API + testler tamam. Bekleyen: admin paneli sayfaları, mobil/web ekranları. Sözleşme: `docs/REVIZE-2.md`.
9. **İçerik** — blogs (draft/published/scheduled, Storage görselleri), suggestions, `ui_content` (+ `GET /ui-content`, frontend'e cache'li bağlama).
   - **Durum (2026-10-14):** Blog **tamam** — migration `20261014110000_blogs.sql` (`blogs` + `blog_likes`), `GET /blogs` (`category`, `page`, `perPage`, `limit`), `GET /blogs/{slug}`, `POST /blogs/{slug}/like` (toggle, oturum zorunlu), `/admin/blogs` CRUD + yayınla/yayından kaldır/planla + kapak yükleme (blog-media), audit log, admin paneli **Blog** sayfası. Plânlı yayın cron'suz: `published_at <= now()` sorgu anında. Uygulama tarafında şema değişikliği gerekmiyor.
   - **Ertelendi:** `suggestions` ve `ui_content` v1'de yok (ürün kararı 2026-10-14: öneriler uygulamadan sökülüyor).
10. **Dashboard + sertleştirme** — admin dashboard (onay sayaçları, huni, kredi satışları), audit log görüntüleyici, RLS/negatif test taraması, `PRODUCTION_CHECKLIST.md` uygulanması (mock söküm), prod yayın. *(Landing page bundan sonra ayrı iş.)*

Her dilim sonunda **Review checkpoint**: contract testleri yeşil + frontend'de ilgili ekranların gerçek API ile smoke testi + kullanıcı onayı alınmadan sonraki dilime geçilmez.

## Riskler
- **Auth üçgeni** (Expo ↔ C# ↔ Supabase JWT/refresh) → dilim 1'de öne alındı; proxy'de refresh kuyruğu davranışı frontend interceptor'ıyla birebir test edilir.
- **service_role RLS baypası** → backend service_role kullanacaksa RLS'e ayrıca negatif testler yazılır.
- **Mock ↔ gerçek sözleşme sapması** → her dilimde Zod contract testi geçiş kriteri.
- **Offer-accept yarış durumu** → DB fonksiyonu + row lock ile çözülür.
- **PayTR bildirim güvenilirliği** → hash doğrulama + koşullu PENDING→PAID geçişi (idempotent) + `payment_id` başına tek PURCHASE satırı.
- **pg_cron plan kısıtı** → alternatif: Supabase Edge Function scheduled / hosted C# background service.

## Kullanıcının Kuracakları (benim yazacağım talimatlarla)
- GitHub `psikoal-backend` reposu, Supabase projesi + API anahtarları, **Supabase MCP** (geliştirme konforu için — önerim bu; ORM olarak zaten EF Core kullanılacak), PayTR mağaza hesabı (web sitesi ödeme sayfasından önce).

## Doğrulama
Her dilimde: (1) `dotnet build` + unit testler, (2) migration'ın temiz Supabase'e uygulanabilirliği, (3) Zod contract testleri, (4) frontend'in ilgili ekranlarının gerçek API ile mock kapalıyken smoke testi, (5) admin panelde ilgili akışın elle doğrulanması.
