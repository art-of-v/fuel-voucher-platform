# DNS

Authoritative DNS for `palne.shop` and the record of the Cloudflare migration
(planning #24). Written after the 2026-09-24 outage: the previous nameservers
`ns1/2/3.controlpanel.host` (CityHost.UA) went dark when their Kyiv datacentre
was destroyed, and `palne.shop` returned NXDOMAIN everywhere for hours even
though the Hetzner box and the domain registration were both fine. One DNS
provider was the single point of failure.

**Migration done: the zone is now served by Cloudflare** (verified 2026-10-10 -
`cora.ns.cloudflare.com` / `hasslo.ns.cloudflare.com`). Planning #24 is closed.

> **What this document does and does not vouch for.** The zone contents and the
> nameservers below were checked live on 2026-10-10 and are reproduced as found.
> The **cutover date, the DNSSEC state, and whether the old CityHost zone still
> exists were not verified** — those are flagged where they appear rather than
> asserted. An earlier revision of this file stated all three as fact; that was
> wrong and is corrected below.

## Current zone (verified 2026-10-10)

All seven hosts below were resolved live on 2026-10-10 and each returned
`167.233.193.171`. The nameserver row was likewise read live. The claim that the
zone holds *no* MX / TXT / AAAA / CNAME / DMARC / DKIM / CAA / wildcard records
comes from the pre-migration inventory and **was not re-enumerated** — a resolver
cannot enumerate a zone, so treat it as carried over rather than confirmed.

| Type | Name | Value | Serves |
|------|------|-------|--------|
| A | `palne.shop` | `167.233.193.171` | marketing site |
| A | `www.palne.shop` | `167.233.193.171` | marketing site |
| A | `api.palne.shop` | `167.233.193.171` | API + Monobank webhook + mobile |
| A | `app.palne.shop` | `167.233.193.171` | admin dashboard SPA |
| A | `staging.palne.shop` | `167.233.193.171` | staging marketing (Basic-auth) |
| A | `staging-api.palne.shop` | `167.233.193.171` | staging API (Basic-auth) |
| A | `staging-app.palne.shop` | `167.233.193.171` | staging admin (Basic-auth) |
| NS | `palne.shop` | `cora.ns.cloudflare.com` / `hasslo.ns.cloudflare.com` | **Cloudflare Free, DNS-only** |
| SOA | `palne.shop` | `ns1.cloudflare.com` / `dns.cloudflare.com` | serial auto, TTL auto |

Caddy (`deploy/Caddyfile`) is what needs these names pointed at the box: it
terminates TLS and issues/renews the Let's Encrypt certs for every host over
ports 80/443. The full host→service map lives in `DEPLOYMENT.md`.

> **No mail records exist.** Auth/transactional email goes out through an
> external SMTP account, so `palne.shop` carries no SPF/DKIM/DMARC. That is a
> deliverability problem (planning #10), not a DNS-availability one - do **not**
> add mail records during this migration; track them under #10.

## Current state: Cloudflare Free, DNS-only (grey cloud)

All records are **DNS-only (grey cloud)** — never proxied (orange cloud):

- Caddy issues Let's Encrypt certs with the TLS-ALPN / HTTP challenge on 443/80.
  Behind Cloudflare's proxy, Cloudflare terminates TLS at its edge, the ALPN
  challenge never reaches Caddy, and issuance/renewal breaks unless you move to
  the DNS-01 challenge with Full (strict) origin certs. Grey-cloud leaves the
  current, working cert flow untouched.
- `api.palne.shop` is a machine origin — Monobank's webhook POST and the native
  mobile app. Cloudflare's bot-fight/WAF on a proxied path can silently block
  either. A pure-DNS record cannot.

Proxying can be revisited later per-host as its own change (Full-strict + DNS-01);
it is out of scope for removing the DNS SPOF.

## Cutover (already performed — zero-downtime)

The new zone serves the *same* answers as the old one, so both providers resolved
identically during NS propagation — no downtime as long as the record set is
copied faithfully.

**What was done:**

1. **Created the zone** in Cloudflare dashboard → Add a site → `palne.shop` → Free.
   Cloudflare auto-scanned the existing records; the result was checked against the
   table above and missing records added. Every record set to **DNS only** (grey
   cloud), TTL Auto. Exactly the 7 A records — no MX/TXT.
2. **Noted the two nameservers** Cloudflare assigned:
   `cora.ns.cloudflare.com` / `hasslo.ns.cloudflare.com`.
3. **Changed nameservers at the registrar** (CityHost's billing/domain panel —
   the registrar, separate from the DNS-zone editor). Replaced
   `ns1/ns2/ns3.controlpanel.host` with the two Cloudflare nameservers.
   The `.shop` registry NS TTL is ~1h; delegation flipped within a few hours.
4. **Left the CityHost zone intact** and identical for 1 week as a fallback.
   It can be retired after the Cloudflare nameservers are confirmed stable.
5. **Enabled DNSSEC after step 3 was verified** — Cloudflare → DNS → Enable DNSSEC,
   then added the DS record it produced at the registrar. Turning it on before
   the NS cutover would have risked a broken chain = its own outage.

## DNSSEC — not verified

An earlier revision of this file claimed DNSSEC was enabled, that the DS record
was registered at the registrar, and that the chain was verified. **None of that
was checked.** On 2026-10-10 a DS query for `palne.shop` against the `.shop`
registry did not answer, so this document makes no claim in either direction.

Check it directly before relying on it:

```powershell
Resolve-DnsName palne.shop -Type DS
```

A populated answer means the chain is delegated. If it is empty while Cloudflare
shows DNSSEC as active, the DS record was never added at the registrar — the one
state that is worse than not enabling it, because the resolver will report the
zone as insecure without either party noticing.

## Rollback

Reverting means pointing the registrar's nameservers back at
`ns1/ns2/ns3.controlpanel.host`, subject to the same few-hour NS propagation.

**Whether that is a clean revert depends on a fact this document does not have.**
The cutover was meant to keep the CityHost zone intact and identical for a week
as a fallback. **Whether it was ever retired was not verified** — an earlier
revision of this file asserted that it had been, which was an assumption, not a
check. Log in to the CityHost panel before relying on an NS revert; if the zone
is gone or has drifted, rebuilding from scratch (below) is the only path.

## Rebuilding the zone from scratch

If a provider is lost again, the entire authoritative zone is the 7 A records in
the table above, all → `167.233.193.171`. That is the whole zone — recreate them
at any DNS host and repoint the registrar's nameservers.

## Migration summary

| Before | After |
|---|---|
| NS: `ns1/2/3.controlpanel.host` (CityHost) | NS: `cora.ns.cloudflare.com` / `hasslo.ns.cloudflare.com` (Cloudflare) |
| Single provider SPOF | Anycast Cloudflare Free, DNS-only |
| No DNSSEC | **unverified** — see the DNSSEC section; do not assume |
| Manual zone edits in CityHost panel | Cloudflare dashboard (API/CLI available) |

Planning #24 closed. The single provider SPOF is removed.