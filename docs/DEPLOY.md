# Production Deploy — Render + Supabase

Bu belge API'nin ve admin panelinin canlıya nasıl çıktığını anlatır. Altyapı kodda:

| Dosya | Ne yapar |
|---|---|
| `render.yaml` | Render Blueprint: `psikoal-api` + `psikoal-admin` (Docker, Frankfurt, ücretsiz plan) |
| `src/PsikoAl.Api/Dockerfile` | API imajı (build bağlamı repo kökü) |
| `src/PsikoAl.Client/Dockerfile` | Admin paneli imajı (Blazor Server) |
| `.github/workflows/deploy.yml` | Elle tetiklenen deploy: şifreli yedek → `db push` → Render deploy hook'ları → `/health` |
| `.github/workflows/backup.yml` | Haftalık şifreli şema + veri yedeği (30 gün) |

## Neden bu sıra

Render'da otomatik deploy **kapalı** (`autoDeploy: false`). Migration'lar forward-only ve
additive yazılır; yeni API sürümü yeni kolonlara güvendiği için şema her zaman **önce**
güncellenir. Otomatik deploy açık olsaydı API, migration'dan önce canlıya çıkabilirdi.

## İlk kurulum (bir kez)

### 1. Supabase (prod projesi)

1. **Migration tarihçesini onar** — prod şeması bir dönem Dashboard/MCP'den elle uygulandı.
   `supabase migration list --linked` ile Local/Remote sütunlarını karşılaştır; uzak tarafta
   olup yerelde olmayan sürümler için `supabase migration repair --status reverted <sürüm>`,
   elle uygulanmış ama kayıtsız olanlar için `--status applied <sürüm>`. Onarılmadan ilk
   `db push` hata verir ya da migration'ı ikinci kez uygular.
2. **Auth ayarları** (Dashboard → Authentication):
   - *URL Configuration* → Site URL: ürünün web adresi; Redirect URLs'e `psikoal://reset-password` ekle.
   - *Email Templates*: `supabase/templates/*.html` içeriğini ve konu satırlarını yapıştır
     (prod hâlâ İngilizce). `supabase config push` **kullanma** — `config.toml` lokal
     değerler içeriyor (`site_url` localhost, e-posta onayı kapalı, şifre 6 karakter).
   - *Providers → Email*: "Confirm email" açık, minimum şifre 8, `letters_digits`.
3. **Bağlantı dizesi**: Render dışa IPv6 çıkışını desteklemiyor, Supabase'in doğrudan
   bağlantısı (`db.<ref>.supabase.co`) yalnız IPv6. Dashboard → Connect → **Session pooler**
   (port 5432) dizesini kullan. Transaction pooler (6543) EF Core'un hazırlanmış
   ifadeleriyle uyumsuzdur.
4. Prod'da test hesabı olmadığını doğrula: `uzman@psikoal.com`, `danisan@psikoal.com`,
   `e2e-*@psikoal.test`, `demo-*@psikoal.test`. `scripts/seed-demo.mjs` ve E2E testleri
   prod'a **asla** yönlendirilmez.

### 2. Render

1. Render → New → **Blueprint** → bu repo, dal `main`. `render.yaml` iki servisi oluşturur.
2. `sync: false` değişkenlerini Dashboard'dan gir:

   | Servis | Anahtar | Değer |
   |---|---|---|
   | psikoal-api | `ConnectionStrings__Postgres` | Session pooler bilgileri, **Npgsql biçiminde** (Supabase'in verdiği `postgresql://` URI'si çalışmaz): `Host=aws-0-<bölge>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<ref>;Password=<parola>;SSL Mode=Require` |
   | psikoal-api | `Supabase__Url` | `https://<ref>.supabase.co` |
   | psikoal-api | `Supabase__AnonKey` | Dashboard → API Keys |
   | psikoal-api | `Supabase__ServiceRoleKey` | Dashboard → API Keys (gizli) |
   | psikoal-api | `Brevo__ApiKey` | Boş bırakılabilir — bildirim e-postası şu an kapalı |
   | psikoal-api | `PayTR__MerchantId` | PayTR Mağaza Paneli → Destek & Kurulum → Entegrasyon Bilgileri → Mağaza No |
   | psikoal-api | `PayTR__MerchantKey` | Aynı ekran → Mağaza Parola (gizli) |
   | psikoal-api | `PayTR__MerchantSalt` | Aynı ekran → Mağaza Gizli Anahtar (gizli) |
   | psikoal-api | `PayTR__TestMode` | Blueprint'te `true`; canlı satışa geçerken `false` |
   | psikoal-api | `PayTR__OkUrl` | Web sitesinin ödeme başarılı sayfası, ör. `https://psikoal.com/bakiye/basarili` |
   | psikoal-api | `PayTR__FailUrl` | Web sitesinin ödeme başarısız sayfası, ör. `https://psikoal.com/bakiye/hata` |
   | psikoal-api | `Site__PublicUrl` | Web sitesinin kök adresi, ör. `https://psikoal.com` — uzman davet linki `/davet/<kod>` bundan üretilir |

   PayTR anahtarları boşsa API çalışır, yalnız bakiye yükleme uçları
   `PAYMENT_PROVIDER_UNAVAILABLE` döner. PayTR panel ayarları (Bildirim URL'si dahil):
   `docs/PAYMENTS.md`.
   | psikoal-admin | `Api__BaseUrl` | API'nin public adresi, ör. `https://psikoal-api.onrender.com` |

3. Her iki servis için Settings → **Deploy Hook** URL'sini kopyala (GitHub sırrı olacak).
4. Özel alan adı bağlanacaksa (ör. `api.psikoal.com`, `admin.psikoal.com`) Render → Custom
   Domains; sonra mobil uygulamanın `EXPO_PUBLIC_API_URL`'i ve panelin `Api__BaseUrl`'i bu adrese çevrilir.

### 3. GitHub

Repo → Settings → Environments → **production** oluştur (isteğe bağlı: onaylayıcı ekle).
Bu ortamın sırları:

| Sır | Nereden |
|---|---|
| `SUPABASE_ACCESS_TOKEN` | supabase.com → Account → Access Tokens |
| `SUPABASE_DB_PASSWORD` | Prod veritabanı parolası |
| `SUPABASE_PROJECT_REF` | Prod proje ref'i |
| `RENDER_DEPLOY_HOOK_API` | Render → psikoal-api → Deploy Hook |
| `RENDER_DEPLOY_HOOK_ADMIN` | Render → psikoal-admin → Deploy Hook |
| `BACKUP_PASSPHRASE` | Uzun rastgele parola — **parola yöneticisinde de sakla**, yoksa yedek açılamaz |

Ortam değişkeni (Variables): `PRODUCTION_API_URL` = API'nin public adresi.

## Her deploy

1. İş dalı → `dev` → `main` terfisi (bkz. `docs/conventions/branching.md`).
2. Actions → **Deploy (production)** → Run workflow (dal: `main`).
3. Workflow: şifreli yedek → bekleyen migration listesi → `db push` → API ve panel deploy → `/health` 200.

Render yeni sürümü sağlık kontrolü (`/health`, veritabanını da yoklar) geçmeden canlıya
almaz; başarısız sürümde eski sürüm çalışmaya devam eder.

## Geri alma

- **Uygulama:** Render → servis → Events → önceki deploy → *Rollback*. Migration'lar additive
  olduğu için eski API sürümü yeni şemayla çalışır.
- **Veri:** son yedeği indir (Actions → ilgili koşu → artifact), sonra:

  ```bash
  gpg --decrypt psikoal-backup-YYYYMMDD.tar.gz.gpg | tar -xzf -
  psql "<session pooler dizesi>" -f schema.sql   # yalnız boş bir projeye
  psql "<session pooler dizesi>" -f data.sql
  ```

  Geri yüklemeyi önce ayrı (staging) bir Supabase projesinde prova et.

## Render ücretsiz planı ve uyanık tutma

İki servis de ücretsiz planda. Ücretsiz plan 15 dakika istek gelmeyen servisi uyutur;
ilk istek ~1 dakika bekler. API, Supabase'teki `keep_alive_ping` cron işiyle her 10
dakikada bir pinglenir (migration `20261015080000_keep_alive.sql`):

1. Admin paneli → **Ayarlar** → `KEEP_ALIVE_URL` = `https://<api-adresi>/health`
2. `KEEP_ALIVE_ENABLED` = `true`

**Admin paneline ping ATILMAZ:** Render ücretsiz planı ayda 750 saati tüm ücretsiz servisler
arasında paylaştırır; 7/24 uyanık tek servis ~744 saat harcar. Panel açılırken bir kez
bekler, sorun değil. Kullanıcı sayısı arttığında API'yi Starter'a geçirin
(`render.yaml` → `plan: starter`) ve `KEEP_ALIVE_ENABLED` = `false` yapın.

## Bilinen sınırlar

- **Admin paneli tek örnek çalışmalı.** Blazor Server oturumu sunucu belleğinde (SignalR
  devresi); birden fazla örnek yapışkan oturum ister. Data Protection anahtarları diske
  yazılmıyor (`DataProtection:KeysPath` boş), bu yüzden her deploy'da adminler yeniden
  giriş yapar. Kalıcılık istenirse Render Disk bağlanıp `DataProtection__KeysPath` verilir
  (disk, sıfır kesintili deploy'u kapatır).
- **Push kuyruğu bellekte.** Deploy anında kuyrukta bekleyen push bildirimleri kaybolabilir
  (uygulama içi bildirim kaydı veritabanında olduğu için kullanıcı yine görür).
- **X-Forwarded-For:** Render'ın vekil zinciri `ForwardLimit=1` ile okunur. İlk deploy'dan
  sonra rate limit'in kullanıcı IP'sine göre çalıştığını loglardan doğrula.
- **Staging yok.** Önerilen: ikinci bir Supabase projesi + Render'da `preview` ortamı.
