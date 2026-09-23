-- DeGoogle Kit — run in the Supabase SQL editor (free plan is fine).
-- Auth is email + password (Authentication → Providers → Email).
-- Free accounts store plan + checklist in public.progress (RLS: own row only).
-- Prefer an EU region (Frankfurt or Ireland) for GDPR transfers.
-- Enable Email provider. Email confirmation is more accurate (Art. 5) than turning it off.
-- Accept the Supabase DPA in the dashboard (Settings → Legal).
-- Do not put the service role key in the Windows app.

create table if not exists public.keepalive (
  id int primary key default 1 check (id = 1),
  touched_at timestamptz not null default now()
);

insert into public.keepalive (id) values (1)
on conflict (id) do nothing;

alter table public.keepalive enable row level security;

drop policy if exists keepalive_select on public.keepalive;
create policy keepalive_select on public.keepalive
  for select to anon, authenticated
  using (true);

drop policy if exists keepalive_touch on public.keepalive;
create policy keepalive_touch on public.keepalive
  for update to anon, authenticated
  using (id = 1)
  with check (id = 1);

grant select, update on table public.keepalive to anon, authenticated;

create or replace function public.touch_keepalive()
returns timestamptz
language sql
security invoker
set search_path = public
as $$
  update public.keepalive
     set touched_at = now()
   where id = 1
  returning touched_at;
$$;

grant execute on function public.touch_keepalive() to anon, authenticated;

create table if not exists public.progress (
  user_id uuid primary key references auth.users (id) on delete cascade,
  guide_done jsonb not null default '[]'::jsonb,
  plan jsonb not null default '{}'::jsonb,
  trial_started_at timestamptz,
  updated_at timestamptz not null default now()
);

alter table public.progress enable row level security;

drop policy if exists progress_own on public.progress;
create policy progress_own on public.progress
  for all to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());

grant select, insert, update, delete on table public.progress to authenticated;

comment on table public.progress is
  'Free-tier backup: guide checklist + plan + whether this account started the Pro trial. No API keys, Takeout files, or license keys.';

create table if not exists public.licenses (
  user_id uuid primary key references auth.users (id) on delete cascade,
  sku text not null,
  seats int not null default 1,
  license_key text not null,
  stripe_sid text,
  updated_at timestamptz not null default now()
);

create index if not exists licenses_stripe_sid_idx on public.licenses (stripe_sid);

alter table public.licenses enable row level security;

drop policy if exists licenses_select_own on public.licenses;
create policy licenses_select_own on public.licenses
  for select to authenticated
  using (user_id = auth.uid());

-- Clients cannot write licenses. The license-api uses the service role after Stripe says paid.

grant select on table public.licenses to authenticated;

-- Household invites (website account). Owner with sku family/household invites by email.
-- Invitees get hashed one-time website login codes until they claim a PC code.
-- Unclaimed invitees never receive license_key via household_* RPCs (data minimization).
-- Full SQL also applied via Supabase migrations household_invites_login_codes + household_rpc_functions.

create table if not exists public.household_invites (
  id uuid primary key default gen_random_uuid(),
  owner_user_id uuid not null references auth.users (id) on delete cascade,
  invitee_email text not null,
  invite_token text not null unique,
  status text not null default 'invited'
    check (status in ('invited', 'accepted', 'claimed', 'revoked')),
  accepted_user_id uuid references auth.users (id) on delete set null,
  created_at timestamptz not null default now(),
  accepted_at timestamptz,
  claimed_at timestamptz,
  revoked_at timestamptz,
  constraint household_invites_email_owner_unique unique (owner_user_id, invitee_email)
);

create table if not exists public.household_login_codes (
  id uuid primary key default gen_random_uuid(),
  invite_id uuid not null references public.household_invites (id) on delete cascade,
  code_hash text not null,
  expires_at timestamptz not null,
  consumed_at timestamptz,
  created_at timestamptz not null default now()
);

create table if not exists public.household_device_claims (
  id uuid primary key default gen_random_uuid(),
  invite_id uuid not null unique references public.household_invites (id) on delete cascade,
  seat_index int not null check (seat_index >= 1),
  device_label text,
  pc_code_hash text not null,
  claimed_at timestamptz not null default now(),
  revoked_at timestamptz
);

comment on table public.household_invites is
  'Household seat invites. RPCs: household_invite, household_list_invites, household_accept_invite, household_issue_login_code, household_claim_pc_code, household_get_activation.';

-- Website also uses this same project for site_events / support_messages / user_roles
-- (visit + download counters, contact form, admin). Applied via Supabase migration
-- site_admin_tables_on_degoogle. Do not create a second Supabase for the marketing site.

-- GDPR Art. 15/20 + 17 — same RPCs as the website MyDataPanel (must stay in sync).
create or replace function public.export_my_data()
returns jsonb language plpgsql stable security definer set search_path = public, auth as $$
declare uid uuid := auth.uid(); em text;
begin
  if uid is null then raise exception 'Not signed in'; end if;
  select email into em from auth.users where id = uid;
  return jsonb_build_object(
    'exported_at', now(),
    'controller', 'Opolyonix Corp, CVR 43410369, Denmark',
    'account', (select jsonb_build_object('id', id, 'email', email, 'created_at', created_at,
                 'last_sign_in_at', last_sign_in_at, 'email_confirmed_at', email_confirmed_at)
                from auth.users where id = uid),
    'progress', (select to_jsonb(p) from public.progress p where p.user_id = uid),
    'licenses', coalesce((select jsonb_agg(to_jsonb(l)) from public.licenses l where l.user_id = uid), '[]'),
    'household_invites_sent', coalesce((select jsonb_agg(jsonb_build_object('invitee_email', invitee_email,
        'status', status, 'created_at', created_at, 'accepted_at', accepted_at, 'claimed_at', claimed_at,
        'revoked_at', revoked_at)) from public.household_invites where owner_user_id = uid), '[]'),
    'household_memberships', coalesce((select jsonb_agg(jsonb_build_object('status', status,
        'created_at', created_at, 'accepted_at', accepted_at, 'claimed_at', claimed_at))
        from public.household_invites where accepted_user_id = uid or lower(invitee_email) = lower(em)), '[]'),
    'support_messages', coalesce((select jsonb_agg(jsonb_build_object('name', name, 'message', message,
        'created_at', created_at)) from public.support_messages where lower(email) = lower(em)), '[]'),
    'roles', coalesce((select jsonb_agg(role) from public.user_roles where user_id = uid), '[]')
  );
end $$;
revoke all on function public.export_my_data() from public, anon;
grant execute on function public.export_my_data() to authenticated;

create or replace function public.delete_my_account()
returns void language plpgsql security definer set search_path = public, auth as $$
declare uid uuid := auth.uid(); em text;
begin
  if uid is null then raise exception 'Not signed in'; end if;
  select email into em from auth.users where id = uid;
  delete from public.support_messages where lower(email) = lower(em);
  delete from public.household_invites where lower(invitee_email) = lower(em) and owner_user_id <> uid;
  delete from auth.users where id = uid;
end $$;
revoke all on function public.delete_my_account() from public, anon;
grant execute on function public.delete_my_account() to authenticated;
