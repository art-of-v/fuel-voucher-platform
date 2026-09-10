# Deploying FuelFlow to DigitalOcean (cheap, single server)

This is a start-to-finish guide written for someone who does not do devops. Copy and paste
the commands in order. Every command that runs **on the server** is marked `[server]`; every
command that runs **on your own laptop** is marked `[laptop]`.

This supersedes the retired Render/Vercel/Supabase setup. `render.yaml` at the repo root is a
legacy leftover kept only for reference — the Droplet is the deployment target, and this guide
plus `DIGITALOCEAN_OPERATIONS.md` are the runbooks.

Set aside about 90 minutes for the first run, most of which is waiting for builds.

---

## 1. What you're building, and what it costs

Everything runs on **one DigitalOcean server** (a "Droplet") using Docker Compose. Six
containers sit on a private network, and only one of them is reachable from the internet:

| Container | What it does | Public? |
|---|---|---|
| `caddy` | Handles HTTPS certificates and routes traffic to the right container | Yes — ports 80/443 |
| `dotnet-backend` | Your .NET API. Hangfire background jobs run inside it, so there's no separate worker to pay for | No (only via Caddy) |
| `admin-frontend` | The React admin dashboard on nginx | No (only via Caddy) |
| `website-frontend` | The palne.shop marketing site (static Next.js build) on nginx | No (only via Caddy) |
| `postgres` | Your database, stored on the server's disk | No — localhost only |
| `redis` | Cache | No — localhost only |

**Cost:** roughly **$12/month** for a 2 GB / 1 vCPU Droplet, plus about **$2.40/month** if you
turn on DigitalOcean's weekly server backups (recommended). HTTPS certificates are free.
The alternative — DigitalOcean App Platform with a managed database and managed Redis — would
be closer to $35–45/month, which is why we're not using it.

> Prices are from memory and change over time. Check the current numbers on DigitalOcean's
> pricing page before you commit.

**Why not the $6/month 1 GB Droplet?** Your PDF voucher import uses pdfium, which is
memory-hungry, and Postgres wants RAM too. 1 GB will work most days and then fail during a
big import, which is the worst kind of failure. The extra $6 buys you a quiet life. We also
add swap space in step 3 as a safety net.

---

## 2. Collect these before you start

Have all of this ready in a text file. The stack will refuse to boot if anything is missing —
that's deliberate, it's your app protecting you from starting up half-configured.

- [ ] **A DigitalOcean account** with a payment method
- [ ] **A domain name** and access to its DNS settings. You'll use two subdomains:
      one for the admin dashboard (e.g. `app.yourdomain.com`) and one for the API
      (e.g. `api.yourdomain.com`)
- [ ] **An SSH key** on your laptop. Check with `ls ~/.ssh/id_*.pub`; if there's nothing,
      run `ssh-keygen -t ed25519` and press Enter through the prompts
- [ ] **Twilio** Account SID, Auth Token, and phone number — required, because SMS login
      codes are how real users sign in
- [ ] **Monobank** merchant API token and webhook public key, plus access to the Monobank
      merchant dashboard so you can change the webhook URL
- [ ] **Access to your GitHub repo** from the server (step 3.6 covers this)

You do **not** need anything from Supabase. Your database starts empty and the app's own
migrations build the schema on first boot, including the seeded stations, fuel types,
fuel packages, and the `Admin` role.

### First: push the deploy files

The server gets the config by cloning your repo, so `deploy/` has to be committed **before**
you start. The `.sh` scripts also need their executable bit set, which Git tracks separately
on Windows:

```bash
# [laptop] — from C:\Projects\FuelFlow
git add deploy docs/DEPLOY_DIGITALOCEAN.md
git update-index --chmod=+x deploy/backup.sh deploy/restore.sh
git commit -m "Add DigitalOcean deployment stack"
git push
```

Only `.env.production.example` is committed — the real `deploy/.env` with your secrets is
git-ignored and never leaves the server.

---

## 3. Create and prepare the server

### 3.1 Create the Droplet

In the DigitalOcean control panel: **Create → Droplets**, then choose:

- **Region:** Frankfurt or Amsterdam (closest to Ukraine — lowest latency for your users)
- **Image:** Ubuntu 24.04 (LTS) x64
- **Type:** Basic → Regular (SSD)
- **Size:** 2 GB RAM / 1 vCPU / 50 GB SSD (~$12/mo)
- **Authentication:** SSH key → add the contents of your `~/.ssh/id_ed25519.pub`
- **Hostname:** `fuelflow-prod`
- **Backups:** tick "Weekly backups" if you want the +20% safety net (recommended)

Click Create, wait ~1 minute, then copy the Droplet's **public IPv4 address**.

### 3.2 Log in

```bash
# [laptop] — replace with your Droplet's IP
ssh root@YOUR_DROPLET_IP
```

Everything from here until step 5 happens on the server.

### 3.3 Add swap and basic updates

Swap is emergency memory on disk. It's slow, but it stops the server from killing your app
during a heavy PDF import.

```bash
# [server]
apt update && apt upgrade -y

fallocate -l 2G /swapfile
chmod 600 /swapfile
mkswap /swapfile
swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab

# confirm: you should see 2.0Gi of swap
free -h
```

### 3.4 Close every port except SSH and web traffic

```bash
# [server]
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable
ufw status
```

Your database is **not** in that list, and that's correct — nothing outside the server can
reach Postgres or Redis.

### 3.5 Install Docker

```bash
# [server]
curl -fsSL https://get.docker.com | sh

# confirm both work
docker --version
docker compose version
```

### 3.6 Get your code onto the server

If the repo is private, create a read-only deploy key:

```bash
# [server]
ssh-keygen -t ed25519 -C "fuelflow-droplet" -f ~/.ssh/id_ed25519 -N ""
cat ~/.ssh/id_ed25519.pub
```

Copy that output, then in GitHub go to **your repo → Settings → Deploy keys → Add deploy key**,
paste it, leave "Allow write access" **unchecked**, and save. Then:

```bash
# [server]
cd ~
git clone git@github.com:ArtemVashchuk/fuel-voucher-platform.git FuelFlow
cd FuelFlow && ls
```

(If the repo is public, `git clone https://github.com/ArtemVashchuk/fuel-voucher-platform.git FuelFlow`
works with no key at all.)

---

## 4. Point your domain at the server

In your domain registrar's DNS settings, add two **A records**:

| Type | Name | Value | TTL |
|---|---|---|---|
| A | `app` | your Droplet IP | 300 (or lowest allowed) |
| A | `api` | your Droplet IP | 300 |

**Do this before step 6.** Caddy asks Let's Encrypt for HTTPS certificates by proving it
controls those domains, which only works once DNS resolves to your server. Check it from
your laptop:

```bash
# [laptop]
dig +short app.yourdomain.com
dig +short api.yourdomain.com
```

Both should print your Droplet's IP. DNS changes usually take a few minutes; occasionally
up to an hour. Wait for it — this is the single most common reason a first deploy fails.

---

## 5. Fill in your configuration

```bash
# [server]
cd ~/FuelFlow/deploy
cp .env.production.example .env
chmod 600 .env

# generate three strong passwords and keep them somewhere safe
openssl rand -base64 36   # -> POSTGRES_PASSWORD
openssl rand -hex 32      # -> REDIS_PASSWORD  (hex on purpose — see note below)
openssl rand -base64 36   # -> JWT_SECRET

# make the backup scripts executable, in case Git didn't carry the bit over
chmod +x backup.sh restore.sh

nano .env
```

Fill in every value. In `nano`, save with `Ctrl+O`, `Enter`, then exit with `Ctrl+X`.

Things that trip people up:

- `ROOT_DOMAIN` and `API_DOMAIN` take bare hostnames — `app.yourdomain.com`, not
  `https://app.yourdomain.com/`
- `MARKETING_DOMAIN` takes the bare hostname of the static site too (e.g. `palne.shop`).
  The support page on that domain POSTs to the API, so the API must list it in
  `AllowedHosts`/CORS — the compose file wires this up from this one variable
- `JWT_SECRET` must be **at least 32 characters** or the API refuses to start
- **`REDIS_PASSWORD` must be hex, not base64.** A `/` in that password breaks the app's
  Redis connection parsing and the API will crash-loop. `openssl rand -hex 32` avoids it
- `TWILIO_*` must be real. The API validates the SID and token on boot because the dev login
  bypass is off. It does **not** validate the phone number, so a typo there surfaces later as
  "codes never arrive" rather than a startup error
- `MONOBANK_PUBLIC_KEY` must be the real key, not a placeholder — also validated on boot
- `SUPPORT_MAIL_*` powers the palne.shop/support contact form (mail goes to
  `SUPPORT_MAIL_TO_EMAIL`). For Gmail: enable 2FA on the account, create an **App Password**
  (Google Account → Security → 2-Step Verification → App passwords) and put those 16
  characters into `SUPPORT_MAIL_PASSWORD` — the real account password is always rejected.
  Leaving username/password empty disables email delivery; submissions are still stored in
  the `support_messages` table (check with the query in section 12)
- This file contains all your secrets, is `chmod 600`, and is **git-ignored**. Never commit it

---

## 6. First launch

```bash
# [server]
cd ~/FuelFlow/deploy
docker compose --env-file .env -f docker-compose.prod.yml up -d --build
```

The first build compiles the .NET API and the React admin app and will take **5–15 minutes**
on a 2 GB Droplet. Later deploys are much faster because Docker caches the layers.

Watch it come up:

```bash
# [server]
docker compose --env-file .env -f docker-compose.prod.yml ps
docker compose --env-file .env -f docker-compose.prod.yml logs -f dotnet-backend
```

In the backend log you want to see the database migrations run and then Hangfire start.
Press `Ctrl+C` to stop following the log (that does not stop the app).

### Smoke test

```bash
# [laptop]
curl -i https://api.yourdomain.com/health
```

You want `HTTP/2 200`. That endpoint actually pings the database, so a 200 means the API and
Postgres are talking to each other. The `https://` working at all means your certificate was
issued successfully.

Then open `https://app.yourdomain.com` in a browser — you should get the admin login screen
with a valid padlock.

Two things you might expect to see and won't, both intentional: **Swagger is disabled in
production**, and **`/hangfire` is not reachable from a browser at all** (the dashboard
requires an Admin JWT bearer token, which a browser doesn't send — so there's no login screen
to find). Nothing sensitive is hanging open.

---

## 7. Make yourself an admin

The database seeds the `Admin` *role*, but no admin *user* — nothing knows your phone number
yet. So: log in through the app or the admin dashboard once with your real phone number to
create your user record, then promote it.

```bash
# [server] — use the same phone format your app stores, e.g. +380671234567
# (swap "fuelflow" for your POSTGRES_USER / POSTGRES_DB if you changed them in .env)
docker exec -it fuelflow-postgres psql -U fuelflow -d fuelflow -c \
  "UPDATE users SET role_id = 'a0000000-0000-0000-0000-000000000001' WHERE phone_number = '+380671234567' AND is_deleted = false;"
```

It should print `UPDATE 1`. If it prints `UPDATE 0`, the phone number doesn't match what's
stored — check with:

```bash
# [server]
docker exec -it fuelflow-postgres psql -U fuelflow -d fuelflow -c \
  "SELECT id, phone_number, role_id FROM users ORDER BY created_at_utc DESC LIMIT 5;"
```

Log out and back in afterwards, so your new token carries the Admin role.

---

## 8. Point the mobile app at the new API

Right now the app has `https://fuel-voucher-platform.onrender.com` baked into its bundle. It
must be changed before you hand the app to pilot customers.

Because `mobile/.env` is git-ignored, it isn't uploaded to EAS's build servers — so the most
reliable fix is to put the URL in `eas.json`, which *is* committed. It's just a public URL,
not a secret.

```bash
# [laptop]
cd C:\Projects\FuelFlow\mobile
```

In `eas.json`, add an `env` block to each build profile:

```json
"preview": {
  "distribution": "internal",
  "channel": "preview",
  "env": { "EXPO_PUBLIC_API_URL": "https://api.yourdomain.com" },
  "ios": { "simulator": true, "bundleIdentifier": "com.artem.vashchuk.mobileappnative" }
},
"production": {
  "distribution": "app-store",
  "channel": "production",
  "env": { "EXPO_PUBLIC_API_URL": "https://api.yourdomain.com" },
  "ios": { ... }
}
```

Also update your local `mobile/.env` to the same value so your own testing matches.

Then publish. Since you have no installed user base to protect, the cleanest path is a fresh
build for your pilot customers:

```bash
# [laptop]
npx eas build --profile production --platform ios
# and/or
npx eas build --profile production --platform android
```

Heads-up on iOS: `eas.json` still has `ascAppId: "REPLACE_WITH_APP_STORE_CONNECT_APP_ID"` and
`appleTeamId: "REPLACE_WITH_APPLE_TEAM_ID"` in both the `production` build profile and the
`submit` block. Fill those in from App Store Connect, or submission will fail after the build
has already spent its time.

If someone has already installed a previous build and you just want to redirect them without
a new install, push an over-the-air update instead — no App Store review needed:

```bash
# [laptop]
npx eas update --branch production --message "Point app at new API domain"
```

OTA updates only reach installs on the same `runtimeVersion` (currently `1.0.0`) and channel,
so verify on a real device that the app is hitting the new domain before you trust it.

---

## 9. Switch Monobank over, then test with real money

This is the step where a mistake costs actual money, so do it deliberately.

The webhook URL lives in **two** places and both must be updated:

1. `Monobank__WebhookUrl` in the stack — already handled, it's derived from `API_DOMAIN`
   in your `.env`
2. The **Monobank merchant dashboard** — set the callback/webhook URL to
   `https://api.yourdomain.com/api/monobank/webhook`

If you only do the first, payments will succeed and vouchers will never be delivered, because
Monobank will keep calling the old Render address.

Then run one real end-to-end purchase yourself, for the smallest amount your app allows, on a
real phone with the new build:

- [ ] SMS login code arrives (proves Twilio works)
- [ ] Payment screen opens in Monobank
- [ ] After paying, the app reopens via the `fuelflow://payment-result` deep link
- [ ] The voucher and its QR code appear in the app
- [ ] The order shows as paid in the admin dashboard
- [ ] `docker compose ... logs dotnet-backend | grep -i monobank` shows the webhook arriving

Only after all six pass should you invite your pilot customers.

---

## 10. Turn on nightly database backups

Your database now lives on this one server, so backups are your responsibility.

```bash
# [server]
cd ~/FuelFlow/deploy
./backup.sh                      # run once by hand to prove it works
ls -lh /root/fuelflow-backups
```

Then schedule it. Run `crontab -e` (pick nano if asked) and add:

```
20 3 * * * cd /root/FuelFlow/deploy && ./backup.sh >> /var/log/fuelflow-backup.log 2>&1
```

That's 03:20 server time every night, keeping 14 days of dumps.

**Important caveat:** those dumps sit on the same server as the database, so they protect you
from "I deleted the wrong rows" but not from "the server died." Cover that second case by
enabling DigitalOcean's weekly Droplet backups, or by copying the dumps off the box
(`rclone`, `scp` from a cron job on your laptop, DO Spaces).

To restore: `./restore.sh /root/fuelflow-backups/fuelflow_YYYY-MM-DD_HHMMSS.dump`.
Practice this once now, while nothing is at stake.

---

## 11. Shut down the old hosting

Once the pilot has run for a few days on DigitalOcean and you're confident:

- [ ] Delete the Render web service
- [ ] Delete the Vercel project (or keep it as a redirect to the new admin domain)
- [ ] Delete the Supabase project — rotating its database password first is good hygiene on
      general principle, though the audit's history sweep found no live Supabase credential in
      this repository (only redacted placeholders)
- [ ] Remove `https://fuel-voucher-platform.onrender.com` from any remaining config or docs

---

## 12. Everyday operations

All of these run on the server, from `~/FuelFlow/deploy`. To save typing, set up a shortcut:

```bash
# [server]
echo "alias ff='docker compose --env-file /root/FuelFlow/deploy/.env -f /root/FuelFlow/deploy/docker-compose.prod.yml'" >> ~/.bashrc
source ~/.bashrc
```

Now:

```bash
ff ps                          # what's running
ff logs -f dotnet-backend      # follow API logs
ff logs --tail=200 caddy       # certificate / routing problems
ff restart dotnet-backend      # restart just the API
ff down                        # stop everything (data volumes survive)
ff up -d                       # start everything again
```

### Support form messages

Messages from the palne.shop/support page land in the `support_messages` table before any
email is attempted, so they survive SMTP outages. To review them:

```bash
# [server] (swap "fuelflow" for your POSTGRES_USER / POSTGRES_DB if you changed them in .env)
docker exec -it fuelflow-postgres psql -U fuelflow -d fuelflow -c \
  "SELECT created_at_utc, email, left(message, 60) AS message, email_sent_at_utc, send_error
   FROM support_messages ORDER BY created_at_utc DESC LIMIT 20;"
```

`send_error` non-null with `email_sent_at_utc` null means the row is stored but the email
never went out (SMTP credentials wrong, Gmail blocking, etc.) — fix the cause and re-send
manually, or read the message right here.

### App Review / QA test phone

The app has no username/password login — users sign in with a phone number and a 6-digit
code. Apple's reviewers must be able to sign in without receiving an SMS, so any phone
listed in `AUTH_TEST_PHONES` (in your `.env`) gets a fixed code and no SMS is ever sent:

```bash
# [server]
cd ~/FuelFlow/deploy
echo "AUTH_TEST_PHONES=+380991234567=427135" >> .env   # your number and a random code
docker compose --env-file .env -f docker-compose.prod.yml up -d dotnet-backend
```

Phone = international format with a leading `+` and no spaces (it must match the number
exactly as it is stored after normalization). Code = exactly six digits. The code never
expires — treat it as a permanent password for that account: pick a random one, use a
dedicated number rather than a real user's, and rotate it if it leaks.

Hand both values to Apple in App Review Information → Sign-In Information (username =
phone number, password = code) and add a note that no SMS will arrive — the reviewer just
types the code. The backend logs `TEST PHONE: OTP issued for allowlisted test number` on
every such login, so review logins are visible in `ff logs dotnet-backend`.

**Deploy new code:**

```bash
# [server]
cd ~/FuelFlow && git pull
cd deploy && ff up -d --build
```

Database migrations apply automatically when the API boots. Expect ~30–60 seconds of downtime
while the new container starts.

**Check the database:**

```bash
docker exec -it fuelflow-postgres psql -U fuelflow -d fuelflow
# then: \dt to list tables, \q to quit
```

**Check server health:**

```bash
free -h          # RAM and swap
df -h /          # disk space
docker stats     # per-container CPU/RAM (Ctrl+C to exit)
```

**Free up disk space** (old Docker images pile up after a few deploys):

```bash
docker system prune -af
```

That's safe — it removes unused images and build cache, never your named volumes.

---

## 13. About device signatures on checkout

`TODO.md` carries a scary warning that the backend "must NOT be redeployed until the app build
is released, else store installs get 401." It's worth understanding why that **doesn't** apply
to this deploy, so you don't disable a security control out of caution.

The backend requires a cryptographic signature on `/api/purchases` and `/api/purchases/bulk`,
and the current mobile app **already sends one** (`mobile/src/core/api/apiClient.ts` signs both
endpoints). The warning was about old builds already installed from the store, still running
unsigned. You don't have those — your pilot customers install a fresh build. So signatures stay
**on**, which is what you want on the endpoint that takes money.

One real failure mode to watch for: the app silently skips signing if the device has no
keypair yet. So during your step 9 test purchase, use a **freshly installed** app on a real
device — that's the path that exercises key generation. If checkout returns 401 there, it's a
key-generation problem in the app, not a server misconfiguration.

**Emergency switch.** If that happens mid-pilot and you need customers buying again while you
debug, add this to the backend `environment:` block and redeploy:

```yaml
DeviceAuth__Enabled: "false"
```

Treat it as a hotfix measured in hours, not days — it disables anti-tamper protection on your
payment endpoints.

What does **not** work, in case you find it suggested somewhere: overriding
`DeviceAuth__RequireSignatureForEndpoints__0` / `__1`. Those entries are defined as C# list
defaults, and .NET's configuration binder *appends* to a list rather than replacing it — so
the two real endpoints stay enforced no matter what you set. `DeviceAuth__Enabled` is the only
switch that actually works.

---

## 14. When something goes wrong

| Symptom | Likely cause | Fix |
|---|---|---|
| Browser shows a certificate warning | DNS wasn't pointing at the server when Caddy tried to get a certificate | Confirm `dig +short app.yourdomain.com` returns your IP, then `ff restart caddy` and watch `ff logs caddy` |
| `curl https://api.../health` times out | Firewall or Caddy not running | `ufw status` (80 and 443 must be allowed), `ff ps` |
| Backend container keeps restarting | A missing or invalid setting — this is the app refusing to boot misconfigured | `ff logs dotnet-backend`; the exception names the setting. Usual suspects: `JWT_SECRET` under 32 chars, empty Twilio values, placeholder Monobank key |
| Backend logs an SSL/connection error to Postgres | The connection string lost `SSL Mode=Disable` | It must stay in `Database__ConnectionString`; without it the app auto-appends `SSL Mode=Require`, which the local Postgres doesn't offer |
| Admin dashboard loads but every API call 404s or fails | Compose service names were renamed | The admin's nginx proxies to the name `dotnet-backend` — keep the service key exactly that |
| Login codes never arrive | Twilio credentials, balance, or number | Check the Twilio console error log |
| Payment succeeds, no voucher appears | Monobank webhook still pointing at Render | Fix the URL in the Monobank dashboard, then use the app's reconciliation feature to settle missed callbacks |
| Checkout returns 401 | The device has no signing keypair, so the app sent an unsigned request | Reinstall the app fresh on a real device and retry. To unblock customers while you debug, set `DeviceAuth__Enabled: "false"` (see section 13) |
| Backend crash-loops right after you change the Redis password | A `/` in the password | Regenerate with `openssl rand -hex 32`, update `.env`, `ff up -d` |
| Build killed partway through | Out of memory | Confirm swap is on (`free -h`); if it persists, resize the Droplet up one size temporarily |
| PDF voucher import fails or the API dies during import | Memory | Same as above — swap, or a bigger Droplet |
| Everything is slow, `df -h` near 100% | Old Docker images | `docker system prune -af` |

**The one command to run before asking for help:**

```bash
ff ps && ff logs --tail=100 dotnet-backend
```

Nearly every failure in this stack explains itself in those hundred lines.

---

## Appendix — TestFlight via EAS (first-time iOS setup)

The mobile app is an Expo project. Shipping to TestFlight = EAS Build → EAS Submit → App Store Connect → TestFlight.

### One-time setup

1. **Apple Developer account** ($99/yr) with App ID registered:
   - Bundle ID: `com.artem.vashchuk.mobileappnative` (matches `app.json`).
   - Log in to <https://developer.apple.com/account/resources/identifiers/add/bundleId>.

2. **App Store Connect API key**:
   - <https://appstoreconnect.apple.com/access/integrations/api> → Keys → Generate.
   - Download the `.p8` file once, save as `mobile/secrets/asc-api-key.p8` (gitignored).
   - Note the **Key ID** and **Issuer ID**.

3. **Install & login to EAS CLI:**
   ```bash
   npm install -g eas-cli
   eas login
   ```

4. **Fill `eas.json`:**
   - Replace `REPLACE_WITH_APPLE_TEAM_ID` with your 10-char Apple Team ID (`developer.apple.com/account/membership`).
   - Replace `REPLACE_WITH_APP_STORE_CONNECT_APP_ID` with the numeric App ID (App Store Connect → App → App Information).
   - Make sure `secrets/asc-api-key.p8` is in place.

### Building & submitting

```bash
cd mobile
eas build --platform ios --profile production    # builds .ipa in cloud
eas submit --platform ios --latest               # pushes to App Store Connect → TestFlight
```

### Distributing

- In App Store Connect → your app → TestFlight → add internal testers (Apple developer team members).
- For external testers you need an Apple review of compliance info (one-time, ~24 hours).
