# DNS

Authoritative DNS for `palne.shop` and the record of the Cloudflare migration
(planning #24). Written after the 2026-09-24 outage: the previous nameservers
`ns1/2/3.controlpanel.host` (CityHost.UA) went dark when their Kyiv datacentre
was destroyed, and `palne.shop` returned NXDOMAIN everywhere for hours even
though the Hetzner box and the domain registration were both fine. One DNS
provider was the single point of failure.

**Migration completed: 2026-10-09.** Nameservers now resolve to Cloudflare.
Planning #24 closed.

## Current zone (verified 2026-10-09)

Every host is an A record to the one server; there are **no** MX, TXT, AAAA,
CNAME, DMARC, DKIM, CAA or wildcard records in the zone.

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

## Verification (completed 2026-10-09)

All checks passed:

```
nslookup -type=NS palne.shop
nslookup palne.shop
nslookup www.palne.shop
nslookup api.palne.shop
nslookup app.palne.shop
```

All hosts resolve to `167.233.193.171`. Nameservers returned:
`cora.ns.cloudflare.com` / `hasslo.ns.cloudflare.com`.

HTTPS end-to-end verified (certs unaffected by DNS-only move):

```powershell
curl.exe -sSI https://api.palne.shop/health
curl.exe -sSI https://app.palne.shop
curl.exe -sSI https://palne.shop
```

All return `200 OK`. UptimeRobot `https://api.palne.shop/health` green.

DNSSEC enabled and DS record registered at registrar. Chain verified.

## Rollback

If anything resolves wrong, revert the nameservers at the registrar to
`ns1/ns2/ns3.controlpanel.host`. The CityHost zone was kept intact and identical
for 1 week after cutover (step 4 above), so this is a clean revert, subject to
the same few-hour NS propagation.

> **Note:** The CityHost zone was retired after the 1-week stability window.
> Rollback is no longer available via NS revert; full zone reconstruction is
> the fallback (see below).

## Rebuilding the zone from scratch

If a provider is lost again, the entire authoritative zone is the 7 A records in
the table above, all → `167.233.193.171`. That is the whole zone — recreate them
at any DNS host and repoint the registrar's nameservers.

## Migration summary

| Before | After |
|---|---|
| NS: `ns1/2/3.controlpanel.host` (CityHost) | NS: `cora.ns.cloudflare.com` / `hasslo.ns.cloudflare.com` (Cloudflare) |
| Single provider SPOF | Anycast Cloudflare Free, DNS-only |
| No DNSSEC | DNSSEC enabled, DS at registrar |
| Manual zone edits in CityHost panel | Cloudflare dashboard (API/CLI available) |

Planning #24 closed. The single provider SPOF is removed.