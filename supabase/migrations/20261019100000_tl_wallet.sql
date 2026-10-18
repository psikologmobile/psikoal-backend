-- tl_wallet: uzman kredisi → TL bakiye + ücretsiz teklif hakkı + dönemsel fiyatlandırma
-- Kaynak: docs/REVIZE-2.md §1, §2, §7 (kanonik), docs/PAYMENTS.md
--
-- BİLİNÇLİ İSTİSNA — additive-only kuralı burada çiğneniyor: credit_* tabloları,
-- experts.credit_balance ve OFFER_CREDIT_COST SİLİNİR. Gerekçe: kredi modeli canlıya
-- hiç kullanılmadan gitti; tablolar canlıda BOŞ (2026-10-19 doğrulandı) ve sözleşme
-- (REVIZE-2 §2.1) kaldırılmalarını istiyor. Eski mobil istemci /credits/* uçlarını
-- yalnız uzman ekranlarında çağırıyordu; o ekranlar aynı sürümde kalkıyor.
--
-- Para birimi her yerde KURUŞ (bigint). Bakiye experts satırında tek yerde tutulur ve
-- yalnız koşullu UPDATE ile değişir (`... where balance_kurus + @delta >= 0`); CHECK
-- kısıtları ikinci savunma hattıdır. wallet_transactions bakiyenin append-only defteridir.

-- ---------------------------------------------------------------- eski kredi modeli

drop table if exists public.credit_transactions;
drop table if exists public.credit_payments;
drop table if exists public.credit_packages;
drop function if exists public.prevent_credit_transaction_update();

alter table public.experts drop constraint if exists experts_credit_balance_non_negative;
alter table public.experts drop column if exists credit_balance;

delete from public.system_settings where key = 'OFFER_CREDIT_COST';
delete from public.notification_templates where type = 'CREDIT_PURCHASED';

-- ---------------------------------------------------------------- dönemsel fiyatlandırma

-- Her satır bir dönem; GEÇERLİ dönem = effective_from <= now() olan en yeni satır.
-- Admin yeni dönem ekler, geçmişe dokunmaz (satırlar güncellenmez). Yayındaki ilanın
-- ücret eğrisi onay anında ilana kopyalandığı için dönem değişse de eğri değişmez.
create table if not exists public.pricing_periods (
  id uuid primary key default gen_random_uuid(),
  effective_from timestamptz not null default now(),
  budget_min_try int not null check (budget_min_try > 0),
  budget_max_try int check (budget_max_try is null or budget_max_try >= budget_min_try),
  fee_min_try int not null check (fee_min_try >= 0),
  fee_max_try int not null,
  daily_decay_pct numeric(5, 2) not null check (daily_decay_pct between 0 and 100),
  per_offer_decay_pct numeric(5, 2) not null check (per_offer_decay_pct between 0 and 100),
  quality_weights jsonb not null,
  note text,
  created_by uuid references public.admin_users (id) on delete set null,
  created_at timestamptz not null default now(),
  constraint pricing_periods_fee_range check (fee_max_try >= fee_min_try)
);

create index if not exists idx_pricing_periods_effective_from on public.pricing_periods (effective_from desc);

alter table public.pricing_periods enable row level security;

-- Başlangıç dönemi (REVIZE-2 §1). Sabit id: migration tekrar koşarsa ikinci satır doğmaz.
insert into public.pricing_periods (
  id, effective_from, budget_min_try, budget_max_try, fee_min_try, fee_max_try,
  daily_decay_pct, per_offer_decay_pct, quality_weights, note)
values (
  '00000000-0000-4000-8000-000000000001', now(), 500, null, 250, 1000, 10, 10,
  '{"description":0.30,"title":0.10,"budget":0.25,"location":0.15,"sessionBoth":0.10,"specialization":0.10,"descriptionFullChars":400,"titleFullChars":30,"budgetFullMultiplier":4}'::jsonb,
  'Başlangıç dönemi (REVIZE-2)')
on conflict (id) do nothing;

-- ---------------------------------------------------------------- uzman cüzdanı

alter table public.experts add column if not exists balance_kurus bigint not null default 0;
alter table public.experts add column if not exists free_offer_rights int not null default 0;
alter table public.experts add column if not exists referral_code text;

do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'experts_balance_kurus_non_negative') then
    alter table public.experts add constraint experts_balance_kurus_non_negative check (balance_kurus >= 0);
  end if;
  if not exists (select 1 from pg_constraint where conname = 'experts_free_offer_rights_non_negative') then
    alter table public.experts add constraint experts_free_offer_rights_non_negative check (free_offer_rights >= 0);
  end if;
  if not exists (select 1 from pg_constraint where conname = 'experts_referral_code_format') then
    alter table public.experts add constraint experts_referral_code_format
      check (referral_code ~ '^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$');
  end if;
end;
$$;

-- Davet kodu: 8 karakter, büyük harf + rakam; karışabilecek 0 O 1 I yok (sözlü/elle
-- yazılan kodda hata payı). Kodu veritabanı üretir: uzman satırı API dışında (seed,
-- E2E betikleri) açıldığında da kodsuz kalmasın.
create or replace function public.generate_referral_code()
returns text
language plpgsql
volatile
set search_path = public
as $$
declare
  alphabet constant text := 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  code text;
begin
  loop
    code := '';
    for i in 1..8 loop
      code := code || substr(alphabet, 1 + floor(random() * length(alphabet))::int, 1);
    end loop;
    exit when not exists (select 1 from public.experts where referral_code = code);
  end loop;
  return code;
end;
$$;

create or replace function public.set_expert_referral_code()
returns trigger
language plpgsql
set search_path = public
as $$
begin
  if new.referral_code is null then
    new.referral_code := public.generate_referral_code();
  end if;
  return new;
end;
$$;

revoke execute on function public.generate_referral_code() from public, anon, authenticated;
revoke execute on function public.set_expert_referral_code() from public, anon, authenticated;

drop trigger if exists trg_experts_referral_code on public.experts;
create trigger trg_experts_referral_code
  before insert on public.experts
  for each row execute function public.set_expert_referral_code();

-- Mevcut uzmanlara geriye dönük kod (satır satır: aynı UPDATE içinde benzersizlik
-- kontrolü öncekileri göremezdi).
do $$
declare
  r record;
begin
  for r in select id from public.experts where referral_code is null loop
    update public.experts set referral_code = public.generate_referral_code() where id = r.id;
  end loop;
end;
$$;

alter table public.experts alter column referral_code set not null;
create unique index if not exists uq_experts_referral_code on public.experts (referral_code);

-- Davet edenin izi: kayıtta kodla gelir, kendine referans olamaz. Uzman silinirse iz düşer.
alter table public.profiles add column if not exists referred_by_expert_id uuid
  references public.experts (id) on delete set null;

do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'profiles_referral_not_self') then
    alter table public.profiles add constraint profiles_referral_not_self check (referred_by_expert_id <> id);
  end if;
end;
$$;

create index if not exists idx_profiles_referred_by on public.profiles (referred_by_expert_id)
  where referred_by_expert_id is not null;

-- merchant_oid PayTR şartı: yalnız harf+rakam, en çok 64 karakter. provider_payload
-- hassas alan TAŞIMAZ (hash, kart bilgisi yok). Fatura PDF'i private `invoices`
-- bucket'ında; satır yalnız yolunu tutar.
create table if not exists public.wallet_payments (
  id uuid primary key default gen_random_uuid(),
  expert_id uuid not null references public.experts (id) on delete cascade,
  merchant_oid text not null unique check (merchant_oid ~ '^[A-Za-z0-9]{1,64}$'),
  amount_kurus bigint not null check (amount_kurus > 0),
  status text not null default 'PENDING' check (status in ('PENDING', 'PAID', 'FAILED')),
  provider_payload jsonb,
  invoice_path text,
  invoice_file_name text,
  invoice_uploaded_at timestamptz,
  invoice_uploaded_by uuid references public.admin_users (id) on delete set null,
  created_at timestamptz not null default now(),
  completed_at timestamptz
);

create index if not exists idx_wallet_payments_expert on public.wallet_payments (expert_id, created_at desc);
create index if not exists idx_wallet_payments_status_created on public.wallet_payments (status, created_at desc);

alter table public.wallet_payments enable row level security;

create table if not exists public.wallet_transactions (
  id uuid primary key default gen_random_uuid(),
  expert_id uuid not null references public.experts (id) on delete cascade,
  amount_kurus bigint not null default 0,
  balance_after_kurus bigint not null check (balance_after_kurus >= 0),
  free_rights_delta int not null default 0,
  free_rights_after int not null check (free_rights_after >= 0),
  type text not null check (type in (
    'TOPUP', 'OFFER_FEE', 'OFFER_FREE_RIGHT', 'WELCOME_RIGHT', 'REFERRAL_RIGHT', 'ADMIN_GIFT', 'ADMIN_ADJUST')),
  listing_id uuid references public.listings (id) on delete set null,
  offer_id uuid references public.offers (id) on delete set null,
  payment_id uuid references public.wallet_payments (id) on delete set null,
  admin_user_id uuid references public.admin_users (id) on delete set null,
  referred_expert_id uuid references public.experts (id) on delete set null,
  reason text,
  created_at timestamptz not null default now(),
  -- Hiçbir şeyi değiştirmeyen satır deftere yazılmaz.
  constraint wallet_transactions_changes_something check (amount_kurus <> 0 or free_rights_delta <> 0),
  constraint wallet_transactions_adjust_has_reason
    check (type <> 'ADMIN_ADJUST' or (reason is not null and length(btrim(reason)) > 0))
);

create index if not exists idx_wallet_transactions_expert_created
  on public.wallet_transactions (expert_id, created_at desc);

-- Idempotency'nin veritabanı katmanı. Asıl korumalar servisteki koşullu geçişlerdir
-- (PENDING → PAID, "daha önce yazılmış mı"); bu indeksler o koşullar bir gün
-- gevşerse aynı olayın iki kez para/hak yazmasını imkânsız kılar.
create unique index if not exists uq_wallet_transactions_topup_payment
  on public.wallet_transactions (payment_id) where type = 'TOPUP';
create unique index if not exists uq_wallet_transactions_offer
  on public.wallet_transactions (offer_id) where type in ('OFFER_FEE', 'OFFER_FREE_RIGHT');
create unique index if not exists uq_wallet_transactions_referral
  on public.wallet_transactions (referred_expert_id) where type = 'REFERRAL_RIGHT';
create unique index if not exists uq_wallet_transactions_welcome
  on public.wallet_transactions (expert_id) where type = 'WELCOME_RIGHT';

-- Tek izinli UPDATE: bağlı kaydın silinmesiyle FK'nin `on delete set null` ile boşalması.
-- Tutar, hak, bakiye, tür, gerekçe ve zaman DEĞİŞEMEZ; bir FK'ye yeni değer yazılamaz.
create or replace function public.prevent_wallet_transaction_update()
returns trigger
language plpgsql
set search_path = public
as $$
begin
  if new.id = old.id
     and new.expert_id = old.expert_id
     and new.amount_kurus = old.amount_kurus
     and new.balance_after_kurus = old.balance_after_kurus
     and new.free_rights_delta = old.free_rights_delta
     and new.free_rights_after = old.free_rights_after
     and new.type = old.type
     and new.reason is not distinct from old.reason
     and new.created_at = old.created_at
     and (new.listing_id is not distinct from old.listing_id or new.listing_id is null)
     and (new.offer_id is not distinct from old.offer_id or new.offer_id is null)
     and (new.payment_id is not distinct from old.payment_id or new.payment_id is null)
     and (new.admin_user_id is not distinct from old.admin_user_id or new.admin_user_id is null)
     and (new.referred_expert_id is not distinct from old.referred_expert_id or new.referred_expert_id is null)
  then
    return new;
  end if;

  raise exception 'wallet_transactions is append-only';
end;
$$;

revoke execute on function public.prevent_wallet_transaction_update() from public, anon, authenticated;

drop trigger if exists trg_wallet_transactions_no_update on public.wallet_transactions;
create trigger trg_wallet_transactions_no_update
  before update on public.wallet_transactions
  for each row execute function public.prevent_wallet_transaction_update();

alter table public.wallet_transactions enable row level security;

-- 20261014100200_revoke_postgrest_table_access ile tutarlı: API `postgres` rolüyle
-- bağlanır, PostgREST kullanılmaz. Politika BİLEREK yok.
revoke all on public.pricing_periods from anon, authenticated;
revoke all on public.wallet_payments from anon, authenticated;
revoke all on public.wallet_transactions from anon, authenticated;

-- ---------------------------------------------------------------- ayarlar

insert into public.system_settings (key, value, description) values
  ('TOPUP_PRESETS_TRY', '500,1000,2500,5000', 'Web ödeme sayfasındaki hazır yükleme tutarları (TL, virgülle)'),
  ('TOPUP_MIN_TRY', '250', 'En küçük bakiye yükleme tutarı (TL)'),
  ('TOPUP_MAX_TRY', '50000', 'En büyük bakiye yükleme tutarı (TL)'),
  ('WELCOME_FREE_RIGHTS', '1', 'Uzman ilk kez onaylandığında verilen ücretsiz teklif hakkı'),
  ('REFERRAL_FREE_RIGHTS', '1', 'Davet ettiği uzman onaylanınca davet edene verilen ücretsiz teklif hakkı'),
  ('CLAIMS_ENABLED', 'true', 'Uzmanlar "Danışanla anlaştım" bildirimi gönderebilsin mi'),
  ('CLAIM_ADMIN_REVIEW_DAYS', '3', 'Anlaşma bildirimi bu kadar gün içinde eşleşmeye dönmezse admin incelemesine düşer'),
  ('VAT_NOTE', 'KDV dahil', 'Yükleme tutarlarının yanında gösterilen vergi notu')
on conflict (key) do nothing;

-- ---------------------------------------------------------------- faturalar

-- Private bucket: uzman faturasını yalnız API'nin ürettiği 5 dakikalık imzalı adresle
-- indirir. Kullanıcı bazlı storage politikası BİLEREK yok — yazma da okuma da
-- service_role (RLS'i aşar) üzerinden.
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('invoices', 'invoices', false, 10485760, array['application/pdf'])
on conflict (id) do nothing;

-- ---------------------------------------------------------------- bildirim şablonları

insert into public.notification_templates (type, title, body, in_app_enabled, push_enabled, email_enabled) values
  ('BALANCE_TOPUP',   'Bakiyeniz yüklendi',          '{amount} bakiyenize eklendi. Güncel bakiyeniz: {balance}.', true, true, false),
  ('ADMIN_GIFT',      'Hesabınıza hediye tanımlandı', '{gift} hesabınıza tanımlandı.', true, true, false),
  ('WELCOME_RIGHTS',  'Hoş geldiniz',                'Profiliniz onaylandı; {rights} ücretsiz teklif hakkı hesabınıza tanımlandı.', true, true, false),
  ('REFERRAL_REWARD', 'Davetiniz kazandırdı',        'Davet ettiğiniz uzman onaylandı; {rights} ücretsiz teklif hakkı kazandınız.', true, true, false),
  ('INVOICE_READY',   'Faturanız hazır',             '{amount} tutarındaki ödemenizin faturası "Faturalarım" bölümünde.', true, true, false)
on conflict (type) do nothing;
