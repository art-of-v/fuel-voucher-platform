// A one-page relay for the WOG pump-price sheet, for the days when WOG's own host refuses to serve
// this deployment.
//
// WHY IT EXISTS (measured 2026-10-29, from the production host 167.233.193.171): `wog.ua` began
// answering `204` with an empty body for every rendered page - `/`, `/ua/`, `/ua/fuels/`,
// `/ua/news/`, `/contacts` - while the same page answered `200` with 114 850 bytes to a laptop.
// `api.wog.ua`, which `robots.txt` and `sitemap.xml` redirect to, answered `204` as well, so the
// block covers the domain rather than one route. Static files still came through
// (`/favicon.ico` -> `200`, 15 086 bytes), and user agent, HTTP version, `Accept` and the TLS stack
// changed nothing - the only variable that mattered was the source address. Hence a relay: the same
// page, fetched from an address WOG has not blocked.
//
// WHAT IT IS NOT: an open proxy. It fetches one constant URL and refuses every other path, so the
// URL leaking costs nothing beyond public data that is already public.
//
// DEPLOYMENT: by hand in the Cloudflare dashboard - Workers & Pages -> Create -> Worker, paste this
// file, Deploy. The account has no CI path for Workers, and this is a fallback for one feed, not
// infrastructure worth a pipeline. Then point the backend at it:
//
//     UPGWOG_PRICE_WOG_ENDPOINT=https://<worker>.<account>.workers.dev/ua/fuels/
//
// Nothing in the application changes: the endpoint is the only place that says where the sheet
// comes from, and `UpgWogPriceClient` keeps reading the same HTML through the same parser.

const UPSTREAM = "https://wog.ua/ua/fuels/";
const USER_AGENT = "FuelFlow price sync (https://github.com/art-of-v/fuel-voucher-platform)";

export default {
  async fetch(request) {
    const { pathname } = new URL(request.url);
    if (pathname !== "/ua/fuels/") {
      return new Response("This relay serves one page: /ua/fuels/\n", { status: 404 });
    }

    const upstream = await fetch(UPSTREAM, {
      headers: { "user-agent": USER_AGENT, accept: "text/html" },
      // Best effort: a re-run inside the TTL costs WOG nothing. Prices change about once a day, so
      // even a stale minute would be safe - the freshness gauge, not this cache, is the alarm.
      cf: { cacheTtl: 300, cacheEverything: true },
    });

    const body = await upstream.text();

    // A refusal is passed through with its own status and no body. Turning it into a 502 here would
    // hide the one message that names the problem: the backend reports "204 with an empty body", and
    // that is precisely the sentence that told us the site was refusing the address.
    if (!upstream.ok || body.trim() === "") {
      // `null`, not `""`: a 204 may not carry a body at all, and constructing one throws, which
      // would turn the site's refusal into a Worker crash with no status to read.
      return new Response(null, { status: upstream.status });
    }

    return new Response(body, {
      headers: {
        "content-type": "text/html; charset=utf-8",
        // The upstream page is private/no-store; ours must not be cached by anything in between.
        "cache-control": "no-store",
      },
    });
  },
};
