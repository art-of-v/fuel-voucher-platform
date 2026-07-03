# Render Deployment

The dotnet backend lives under `fuel-flow-dotnet-backend/`. Render's `render.yaml` at the repo root declares all services as code.

## One-time setup

1. Log in to Render → `Blueprint` → `New Blueprint Instance`.
2. Point it at `ArtemVashchuk/fuel-voucher-platform`, branch `main`, root path `render.yaml`.
3. Render will create the `fuel-dotnet-backend` web service from `render.yaml`. **The database is NOT created by Render** — it lives on Supabase (shared with the Node backend).

## Manual env vars to set on the web service (sensitive — not auto-generated)

After the Blueprint creates the service, go to its `Environment` tab and set:

- **`Database__ConnectionString`** — Supabase `DATABASE_URL`, **URL-encoded**:
  ```
  postgresql://postgres.dfrjclroguyipgikekff:***REDACTED***@aws-1-eu-west-1.pooler.supabase.com:6543/postgres
  ```
  Encoding rules (apply to whatever your password actually is):
  - `!` → `%21`
  - `&` → `%26`
  - `#` → `%23`
  - `+` → `%2B`
  - ` ` (space) → `%20`
  - If unsure, run `node -e "console.log(encodeURIComponent('YOUR_PASSWORD'))"` to get the safe value.
- **`Monobank__Token`** — your production Monobank merchant API token.
- **`Monobank__WebhookUrl`** — e.g. `https://fuel-voucher-platform.onrender.com/api/monobank/webhook`.
- **`Monobank__PublicKey`** — Monobank's PEM public key block (used for verifying webhook signatures).
- **`RunMigrationsOnBoot`** — leave at `false` for first deploy; flip to `true` after verifying schema is OK.

## Verifying deployment

- `https://fuel-voucher-platform.onrender.com/health` should return `{"status":"ok"}`.
- `https://fuel-voucher-platform.onrender.com/swagger` returns the OpenAPI UI.

## Database migrations

By default `RunMigrationsOnBoot=false` — migrations are NOT applied automatically.

To apply manually from your dev machine:
```bash
DATABASE_URL="postgresql://..." dotnet ef database update \
  --project fuel-flow-dotnet-backend/src/FuelFlow.API \
  --startup-project fuel-flow-dotnet-backend/src/FuelFlow.API
```

### Shared Supabase caveat

**The dotnet backend and Node admin backend share the same Supabase project.** They overlap on tables (`users`, `devices`, `orders`, `fulfillments`, `stations`, `station_nodes`, `fuel_types`, `fuel_packages`). When you run EF migrations, EF may try to alter columns Node backend expects — **diff before running**. Consider running EF on a *separate database* before merging.

## Frontend (Vercel)

Vercel hosts the admin panel frontend. The frontend reads `VITE_API_URL` at build time — set it in the Vercel project env to the Render URL:

```
https://fuel-voucher-platform.onrender.com
```

Vercel project settings:
- **Root Directory**: `admin-panel/frontend`
- **Source**: `ArtemVashchuk/fuel-voucher-platform`

## Mobile (TestFlight)

The mobile app reads `EXPO_PUBLIC_API_URL` at build time. Set it to the same Render URL:

```
https://fuel-voucher-platform.onrender.com
```

---

# TestFlight via EAS

The mobile app is an Expo project. Shipping to TestFlight = EAS Build → EAS Submit → App Store Connect → TestFlight.

## One-time setup

1. **Apple Developer account** ($99/yr) with App ID registered:
   - Bundle ID: `com.artem.vashchuk.mobileappnative` (matches `app.json`).
   - Log in to <https://developer.apple.com/account/resources/identifiers/add/bundleId>.

2. **App Store Connect API key**:
   - <https://appstoreconnect.apple.com/access/integrations/api> → Keys → Generate.
   - Download the `.p8` file once, save as `mobile-app-native/secrets/asc-api-key.p8` (gitignored).
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

## Building & submitting

```bash
cd mobile-app-native
eas build --platform ios --profile production    # builds .ipa in cloud
eas submit --platform ios --latest               # pushes to App Store Connect → TestFlight
```

After the first build, you can also use `eas workflow` for CI-style pipelines.

## Distributing

- In App Store Connect → your app → TestFlight → add internal testers (Apple developer team members).
- For external testers you need an Apple review of compliance info (one-time, ~24 hours).

