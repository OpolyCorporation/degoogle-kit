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
  'Free-tier backup: guide checklist + plan only. No API keys, Takeout files, or license keys.';

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
