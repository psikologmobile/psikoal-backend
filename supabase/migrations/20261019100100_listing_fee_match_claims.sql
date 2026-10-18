-- listing_fee_match_claims: dinamik teklif ücreti, "eşleşti" işaretleme, davet ve
-- "danışanla anlaştım" bildirimleri
-- Kaynak: docs/REVIZE-2.md §1.1, §3, §4, §8
--
-- Additive: yeni kolonlar nullable ya da varsayılanlı. accept_offer() kaldırılır: kabul/ret
-- uçları sözleşmeden çıktı (REVIZE-2 §3.4), yerini mark_listing_matched() alır.

-- ---------------------------------------------------------------- ilan

alter table public.listings add column if not exists contact_consent_at timestamptz;
alter table public.listings add column if not exists quality_score numeric(4, 3)
  check (quality_score is null or quality_score between 0 and 1);
-- Onay anının ücret eğrisi (dönem değişse de yayındaki ilanın eğrisi değişmesin diye kopya).
alter table public.listings add column if not exists fee_initial_kurus bigint check (fee_initial_kurus is null or fee_initial_kurus >= 0);
alter table public.listings add column if not exists fee_min_kurus bigint check (fee_min_kurus is null or fee_min_kurus >= 0);
alter table public.listings add column if not exists daily_decay_pct numeric(5, 2);
alter table public.listings add column if not exists per_offer_decay_pct numeric(5, 2);
alter table public.listings add column if not exists invited_expert_id uuid
  references public.experts (id) on delete set null;

create index if not exists idx_listings_invited_expert on public.listings (invited_expert_id)
  where invited_expert_id is not null;

-- ---------------------------------------------------------------- teklif

alter table public.offers add column if not exists fee_paid_kurus bigint not null default 0;
alter table public.offers add column if not exists used_free_right boolean not null default false;
alter table public.offers add column if not exists closed_reason text;

do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'offers_fee_paid_non_negative') then
    alter table public.offers add constraint offers_fee_paid_non_negative check (fee_paid_kurus >= 0);
  end if;
  if not exists (select 1 from pg_constraint where conname = 'offers_closed_reason_valid') then
    alter table public.offers add constraint offers_closed_reason_valid
      check (closed_reason is null or closed_reason in ('OTHER_EXPERT', 'LISTING_CLOSED', 'LISTING_EXPIRED'));
  end if;
end;
$$;

-- ---------------------------------------------------------------- eşleşti işaretleme

drop function if exists public.accept_offer(uuid, uuid);

-- Danışan ilanını "X uzmanla eşleşti" işaretler. Tek transaction: match ACTIVE, ilan
-- MATCHED, seçilen teklif ACCEPTED, diğer PENDING teklifler REJECTED/OTHER_EXPERT.
-- Satır kilitleri (for update) eşzamanlı iki işaretlemeyi ve aynı anda gelen teklifi
-- sıraya sokar. Hata kodları C# tarafında domain koduna çevrilir.
create or replace function public.mark_listing_matched(
  p_listing_id uuid,
  p_offer_id uuid,
  p_actor_client_id uuid)
returns table (match_id uuid, expert_id uuid, other_expert_ids uuid[])
language plpgsql
set search_path = public
as $$
#variable_conflict use_column
declare
  v_listing public.listings%rowtype;
  v_offer public.offers%rowtype;
  v_match_id uuid;
  v_others uuid[];
begin
  select * into v_listing from public.listings where id = p_listing_id for update;
  if not found then
    raise exception 'LISTING_NOT_FOUND' using errcode = 'P0001';
  end if;

  if v_listing.client_id <> p_actor_client_id then
    raise exception 'LISTING_NOT_OWNER' using errcode = 'P0001';
  end if;

  if v_listing.status <> 'OPEN' then
    raise exception 'LISTING_NOT_OPEN' using errcode = 'P0001';
  end if;

  select * into v_offer from public.offers where id = p_offer_id for update;
  if not found or v_offer.listing_id <> p_listing_id then
    raise exception 'OFFER_NOT_ON_LISTING' using errcode = 'P0001';
  end if;

  if v_offer.status <> 'PENDING' then
    raise exception 'OFFER_NOT_PENDING' using errcode = 'P0001';
  end if;

  v_match_id := gen_random_uuid();

  with closed as (
    update public.offers
       set status = 'REJECTED', closed_reason = 'OTHER_EXPERT'
     where listing_id = p_listing_id and id <> p_offer_id and status = 'PENDING'
    returning offers.expert_id
  )
  select coalesce(array_agg(closed.expert_id), '{}') into v_others from closed;

  update public.listings set status = 'MATCHED' where id = p_listing_id;

  -- matches satırı, offers.match_id'nin işaret edeceği FK hedefinden önce var olmalı.
  insert into public.matches (id, listing_id, accepted_offer_id, client_id, expert_id, status)
  values (v_match_id, p_listing_id, p_offer_id, v_listing.client_id, v_offer.expert_id, 'ACTIVE');

  update public.offers set status = 'ACCEPTED', match_id = v_match_id where id = p_offer_id;

  return query select v_match_id, v_offer.expert_id, v_others;
end;
$$;

revoke execute on function public.mark_listing_matched(uuid, uuid, uuid) from public, anon, authenticated;

-- ---------------------------------------------------------------- süre dolması

-- İlan süresi dolunca bekleyen teklifler de kapanır (REJECTED / LISTING_EXPIRED):
-- uzmanın "Tekliflerim" ekranında sonsuza dek "Gönderildi" görünmesinler.
create or replace function public.expire_open_listings()
returns void
language plpgsql
set search_path = public
as $$
begin
  with expired as (
    update public.listings
       set status = 'EXPIRED'
     where status = 'OPEN' and expires_at < now()
    returning id
  )
  update public.offers
     set status = 'REJECTED', closed_reason = 'LISTING_EXPIRED'
   where status = 'PENDING' and listing_id in (select id from expired);
end;
$$;

revoke execute on function public.expire_open_listings() from public, anon, authenticated;

-- cron.schedule aynı adla çağrılınca mevcut işi günceller (idempotent).
select cron.schedule('expire_open_listings', '0 * * * *', 'select public.expire_open_listings()');

-- ---------------------------------------------------------------- "anlaştık" bildirimi

-- Uzman ilan başına bir kez "Danışanla anlaştım" der. Danışan "Anlaşmadım" ile
-- hatırlatmayı kapatır (dismissed_at); admin çözüm notuyla kapatır (resolved_*).
create table if not exists public.listing_claims (
  id uuid primary key default gen_random_uuid(),
  listing_id uuid not null references public.listings (id) on delete cascade,
  expert_id uuid not null references public.experts (id) on delete cascade,
  note text check (note is null or length(note) <= 300),
  created_at timestamptz not null default now(),
  dismissed_at timestamptz,
  resolved_at timestamptz,
  resolved_by uuid references public.admin_users (id) on delete set null,
  resolution_note text
);

create unique index if not exists uq_listing_claims_listing_expert on public.listing_claims (listing_id, expert_id);
create index if not exists idx_listing_claims_created on public.listing_claims (created_at desc);
create index if not exists idx_listing_claims_expert on public.listing_claims (expert_id);

alter table public.listing_claims enable row level security;
revoke all on public.listing_claims from anon, authenticated;

-- ---------------------------------------------------------------- bildirim şablonları

insert into public.notification_templates (type, title, body, in_app_enabled, push_enabled, email_enabled) values
  ('MATCHED',                'Danışan sizinle eşleşti',     '{clientName}, "{listingTitle}" ilanı için sizinle eşleştiğini bildirdi.', true, true, false),
  ('LISTING_MATCHED_OTHER',  'İlan başka uzmanla eşleşti',  '"{listingTitle}" ilanı başka bir uzmanla eşleşti.', true, true, false),
  ('CLAIM_REMINDER',         'Uzmanla anlaştınız mı?',      '{expertName}, "{listingTitle}" ilanınız için sizinle anlaştığını bildirdi. Anlaştıysanız ilanınızı eşleşti olarak işaretleyin.', true, true, false),
  ('INVITED_CLIENT_LISTING', 'Davet ettiğiniz danışan ilan açtı', 'Davetinizle gelen danışan "{listingTitle}" ilanını yayınladı.', true, true, false)
on conflict (type) do nothing;
