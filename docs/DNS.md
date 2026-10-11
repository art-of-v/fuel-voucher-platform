# DNS

Authoritative DNS for `palne.shop` and the record of the Cloudflare migration
(planning #24). Written after the 2026-09-24 outage: the previous nameservers
`ns1/2/3.controlpanel.host` (CityHost.UA) went dark when their Kyiv datacentre
was destroyed, and `palne.shop` returned NXDOMAIN everywhere for hours even
though the Hetzner box and the domain registration were both fine. One DNS
provider was the single point of failure.

**Migration done: the zone is now served by Cloudflare** (verified 2026-10-10 -
`cora.ns.cloudflare.com` / `hasslo.ns.cloudflare.com`). Planning #24 is closed.

> **What this document does and does not vouch for.** The zone contents, the
> nameservers, the DNSSEC chain and the state of the old CityHost zone were all
> checked live on 2026-10-11 and are reproduced as found. The **cutover date is
> still unknown** — nothing records when the nameservers were switched, and it is
> flagged rather than guessed. The claim that the zone holds no MX / TXT / AAAA /
> CNAME / DMARC / DKIM / CAA / wildcard records comes from the pre-migration
> inventory and was **not** re-enumerated, since a resolver cannot enumerate a
> zone. An earlier revision of this file asserted the DNSSEC and CityHost state
> without checking either; both turned out to be different from what it said.

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

## DNSSEC — enabled (verified 2026-10-11)

The DS record is published at the `.shop` registry and matches what Cloudflare
serves, so the chain delegates:

```
keytag 2371 · algorithm 13 (ECDSA P-256) · digest type 2 (SHA-256)
97a82a0cb8718b78a33190e06664127a449e992efbd5daeecb7d6b462e60d17e
```

Re-check it at any time:

```powershell
Resolve-DnsName palne.shop -Type DS
```

> A caveat worth knowing: a plain `Resolve-DnsName` may time out or return
> nothing even when DNSSEC is fine, depending on which resolver you hit. The
> authoritative answer is Cloudflare's own DoH endpoint, which is what the check
> above was confirmed against:
>
> ```powershell
> Invoke-RestMethod 'https://cloudflare-dns.com/dns-query?name=palne.shop&type=DS' -Headers @{accept='application/dns-json'}
> ```
>
> A revision of this file earlier reported DNSSEC as unverified because a
> `Resolve-DnsName` call timed out. It is on — and the timeout was the resolver's,
> not the zone's.

## Rollback — the old nameservers are NOT a working fallback (verified 2026-10-11)

**Do not revert the nameservers to CityHost.** The zone is still there, but it is
empty, and an NS revert would take every host down.

`ns1.controlpanel.host` still answers and still holds the zone:

```
SOA  ns1.controlpanel.host / support.cityhost.com.ua · serial 26
NS   ns1 · ns2 · ns3 .controlpanel.host
```

But **none of the seven hosts has an A record there any more** — every one of
`palne.shop`, `www`, `api`, `app`, `staging`, `staging-api` and `staging-app`
returns NO A when queried directly against `ns1.controlpanel.host`.

So the cutover plan's step 4 ("leave the CityHost zone intact for a week as a
fallback") did not survive contact with reality: the zone exists as a shell with
its records removed. Pointing the registrar back at it would resolve nothing.

**The only working recovery is rebuilding the zone** (below), which takes minutes
because the whole zone is seven A records. Anyone reaching for "just put the old
NS back" during an incident will take the site down instead of restoring it —
that is why this is stated rather than left for someone to discover.

## Rebuilding the zone from scratch

If a provider is lost again, the entire authoritative zone is the 7 A records in
the table above, all → `167.233.193.171`. That is the whole zone — recreate them
at any DNS host and repoint the registrar's nameservers.

## Migration summary

| Before | After |
|---|---|
| NS: `ns1/2/3.controlpanel.host` (CityHost) | NS: `cora.ns.cloudflare.com` / `hasslo.ns.cloudflare.com` (Cloudflare) |
| Single provider SPOF | Anycast Cloudflare Free, DNS-only |
| No DNSSEC | DNSSEC enabled (DS keytag 2371, verified 2026-10-11) |
| CityHost zone kept as a 1-week fallback | Zone still exists but **holds no records** — NS revert is not a usable fallback |
| Manual zone edits in CityHost panel | Cloudflare dashboard (API/CLI available) |

Planning #24 closed. The single provider SPOF is removed.