# DNS

Authoritative DNS for `palne.shop` and the runbook for the Cloudflare migration
(planning #24). Written after the 2026-09-24 outage: the previous nameservers
`ns1/2/3.controlpanel.host` (CityHost.UA) went dark when their Kyiv datacentre
was destroyed, and `palne.shop` returned NXDOMAIN everywhere for hours even
though the Hetzner box and the domain registration were both fine. One DNS
provider was the single point of failure.

## Current zone (verified 2026-09-25)

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
| NS | `palne.shop` | `ns1/ns2/ns3.controlpanel.host` | **being replaced** |
| SOA | `palne.shop` | `ns1.controlpanel.host` / `support.cityhost.com.ua` | serial 19, TTL 86400 |

Caddy (`deploy/Caddyfile`) is what needs these names pointed at the box: it
terminates TLS and issues/renews the Let's Encrypt certs for every host over
ports 80/443. The full host→service map lives in `DEPLOYMENT.md`.

> **No mail records exist.** Auth/transactional email goes out through an
> external SMTP account, so `palne.shop` carries no SPF/DKIM/DMARC. That is a
> deliverability problem (planning #10), not a DNS-availability one — do **not**
> add mail records during this migration; track them under #10.

## Target: Cloudflare Free, DNS-only

Cloudflare's free plan serves the zone from its anycast nameservers — that alone
removes the single-provider SPOF, which is the whole point of #24.

**Keep every record DNS-only (grey cloud), never proxied (orange cloud):**

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

## Cutover (zero-downtime when records match exactly)

The new zone serves the *same* answers as the old one, so both providers resolve
identically during NS propagation — no downtime as long as the record set is
copied faithfully.

1. **Create the zone.** Cloudflare dashboard → Add a site → `palne.shop` → Free.
   Cloudflare auto-scans the existing records; check the result against the table
   above and add whatever it missed. Set **every** record to **DNS only** (grey
   cloud), TTL Auto. Expect exactly the 7 A records — no MX/TXT.
2. **Note the two nameservers** Cloudflare assigns (e.g.
   `xxx.ns.cloudflare.com` / `yyy.ns.cloudflare.com`).
3. **Change nameservers at the registrar** — the panel where `palne.shop` is
   *registered* (CityHost's billing/domain panel; that is the registrar, separate
   from the DNS-zone editor). Replace `ns1/ns2/ns3.controlpanel.host` with the two
   Cloudflare nameservers. The `.shop` registry NS TTL is ~1h, so delegation flips
   within a few hours; cached stragglers clear within 24–48h.
4. **Leave the CityHost zone intact** and identical for ~1 week as a fallback.
   Retire it only after the Cloudflare nameservers are confirmed stable.
5. **Enable DNSSEC after step 3 is verified — not during.** Cloudflare → DNS →
   Enable DNSSEC, then add the DS record it produces at the registrar. Turning it
   on before the NS cutover is stable risks a broken chain = its own outage.

## Verify after cutover

Run until the nameservers read Cloudflare and every host still answers
`167.233.193.171`:

```
nslookup -type=NS palne.shop
nslookup palne.shop
nslookup www.palne.shop
nslookup api.palne.shop
nslookup app.palne.shop
```

Then confirm HTTPS end-to-end (certs are unaffected by a DNS-only move, but
check). On Windows use `curl.exe` — bare `curl` in PowerShell is an alias for
`Invoke-WebRequest` and rejects these flags:

```
curl.exe -sSI https://api.palne.shop/health
curl.exe -sSI https://app.palne.shop
curl.exe -sSI https://palne.shop
```

`https://api.palne.shop/health` is what UptimeRobot watches — a green check there
is the external all-clear.

## Rollback

If anything resolves wrong, revert the nameservers at the registrar to
`ns1/ns2/ns3.controlpanel.host`. The CityHost zone is still live (cutover step
4), so this is a clean revert, subject to the same few-hour NS propagation.

## Rebuilding the zone from scratch

If a provider is lost again, the entire authoritative zone is the 7 A records in
the table above, all → `167.233.193.171`. That is the whole zone — recreate them
at any DNS host and repoint the registrar's nameservers.
