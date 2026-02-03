# FuelFlow Infrastructure

Azure infrastructure as code using CDKTF (Cloud Development Kit for Terraform) with C#.

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Azure (West Europe)                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐       │
│  │   App Service   │────▶│   PostgreSQL    │     │  Log Analytics  │       │
│  │   B1 (Node.js)  │     │  Flexible B1ms  │     │   (30 days)     │       │
│  └────────┬────────┘     └─────────────────┘     └────────┬────────┘       │
│           │                                               │                │
│           ├─────────────▶┌─────────────────┐              │                │
│           │              │  Redis Basic C0 │              │                │
│           │              └─────────────────┘              │                │
│           │                                               │                │
│           ├─────────────▶┌─────────────────┐              │                │
│           │              │   Key Vault     │              │                │
│           │              └─────────────────┘              │                │
│           │                                               │                │
│           └─────────────▶┌─────────────────┐              │                │
│                          │  Blob Storage   │              │                │
│                          └─────────────────┘              │                │
│                                                           │                │
│  ┌─────────────────┐     ┌─────────────────┐              │                │
│  │  App Insights   │────▶│  Azure Monitor  │◀─────────────┘                │
│  └─────────────────┘     │    (Alerts)     │                               │
│                          └─────────────────┘                               │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Resources & Estimated Cost

| Resource | SKU | Monthly Cost |
|----------|-----|--------------|
| App Service Plan | B1 (1 vCPU, 1.75 GB) | ~$13 |
| PostgreSQL Flexible | B1ms (1 vCore, 2 GB, 32 GB SSD) | ~$12 |
| Azure Cache for Redis | Basic C0 (250 MB) | ~$13 |
| Storage Account | Standard LRS | ~$0.50 |
| Key Vault | Standard | ~$1 |
| Application Insights | Free tier | Free |
| Log Analytics | Free tier (30 days) | Free |
| **Total** | | **~$39-42/month** |

---

## Step-by-Step Production Setup

### Step 1: Create Azure Account

1. Go to **https://azure.microsoft.com/free/**
2. Click **"Start free"** and sign in with Microsoft account
3. Complete registration (credit card required, but won't be charged for free tier)
4. After signup, go to **https://portal.azure.com**

### Step 2: Create Azure Subscription

1. In Azure Portal, search for **"Subscriptions"**
2. Click **"+ Add"**
3. Select **"Pay-As-You-Go"** (or use existing subscription)
4. Complete the setup
5. **Copy your Subscription ID** (looks like: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`)

### Step 3: Create Azure Service Principal

This allows GitHub Actions to deploy to Azure.

1. Open **Azure Cloud Shell** (click `>_` icon in Azure Portal top bar)
2. Run this command (replace `<SUBSCRIPTION_ID>` with your actual ID):

```bash
az ad sp create-for-rbac \
  --name "fuelflow-github-actions" \
  --role contributor \
  --scopes /subscriptions/<SUBSCRIPTION_ID> \
  --sdk-auth
```

3. **Copy the entire JSON output** - you'll need it for GitHub:

```json
{
  "clientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "clientSecret": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "subscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "tenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "activeDirectoryEndpointUrl": "https://login.microsoftonline.com",
  "resourceManagerEndpointUrl": "https://management.azure.com/",
  ...
}
```

### Step 4: Create Stripe Account

1. Go to **https://dashboard.stripe.com/register**
2. Create account and verify email
3. Go to **https://dashboard.stripe.com/apikeys**
4. Copy:
   - **Publishable key**: `pk_test_...` (or `pk_live_...` for production)
   - **Secret key**: `sk_test_...` (or `sk_live_...` for production)

5. Set up Webhook:
   - Go to **https://dashboard.stripe.com/webhooks**
   - Click **"Add endpoint"**
   - URL: `https://fuelflow-prod-app.azurewebsites.net/api/webhooks/stripe`
   - Events to select:
     - `checkout.session.completed`
     - `payment_intent.succeeded`
     - `payment_intent.payment_failed`
   - Click **"Add endpoint"**
   - **Copy the Signing secret**: `whsec_...`

### Step 5: Create Google Gemini API Key

1. Go to **https://aistudio.google.com/app/apikey**
2. Sign in with Google account
3. Click **"Create API Key"**
4. Select a project (or create new)
5. **Copy the API key**: `AIzaSy...`

### Step 6: Create Twilio Account

1. Go to **https://www.twilio.com/try-twilio**
2. Sign up and verify your phone number
3. Go to **https://console.twilio.com**
4. From the dashboard, copy:
   - **Account SID**: `AC...`
   - **Auth Token**: (click "Show" to reveal)

5. Get a phone number:
   - Go to **Phone Numbers → Manage → Buy a number**
   - Select a number with SMS capability
   - **Copy the phone number**: `+1234567890`

### Step 7: Generate Application Secrets

Generate two random 64-character hex strings for session and encryption keys.

**Option A: Using OpenSSL (if available)**
```bash
openssl rand -hex 32
```

**Option B: Using PowerShell**
```powershell
-join ((1..32) | ForEach-Object { '{0:x2}' -f (Get-Random -Maximum 256) })
```

**Option C: Using Online Generator**
- Go to https://generate-random.org/string-generator
- Length: 64, Characters: Hex (0-9, a-f)
- Generate two different strings

Save these as:
- **SESSION_SECRET**: First generated string
- **QR_ENCRYPTION_KEY**: Second generated string

### Step 8: Configure GitHub Secrets

1. Go to your GitHub repository
2. Click **Settings** → **Secrets and variables** → **Actions**
3. Click **"New repository secret"** for each:

| Secret Name | Where to Get Value |
|-------------|-------------------|
| `AZURE_CREDENTIALS` | Entire JSON from Step 3 |
| `AZURE_SUBSCRIPTION_ID` | subscriptionId from the JSON in Step 3 |
| `POSTGRES_ADMIN_PASSWORD` | Create a strong password (16+ chars, mixed case, numbers, symbols) |
| `STRIPE_SECRET_KEY` | Stripe Dashboard → API Keys → Secret key |
| `STRIPE_PUBLISHABLE_KEY` | Stripe Dashboard → API Keys → Publishable key |
| `STRIPE_WEBHOOK_SECRET` | Stripe Dashboard → Webhooks → Signing secret |
| `GEMINI_API_KEY` | Google AI Studio → API Key |
| `TWILIO_ACCOUNT_SID` | Twilio Console → Account SID |
| `TWILIO_AUTH_TOKEN` | Twilio Console → Auth Token |
| `TWILIO_PHONE_NUMBER` | Twilio Console → Your phone number (with +) |
| `SESSION_SECRET` | Generated hex string from Step 7 |
| `QR_ENCRYPTION_KEY` | Generated hex string from Step 7 |
| `ALERT_EMAIL` | Your email for receiving alerts |

**Screenshot reference for GitHub Secrets:**
```
Repository → Settings → Secrets and variables → Actions → New repository secret

┌─────────────────────────────────────────┐
│ Name: AZURE_CREDENTIALS                 │
│ Secret: {"clientId":"xxx",...}          │
│ [Add secret]                            │
└─────────────────────────────────────────┘
```

### Step 9: Create GitHub Environment (Optional but Recommended)

This adds a manual approval step before deploying.

1. Go to **Settings** → **Environments**
2. Click **"New environment"**
3. Name: `production`
4. Click **"Configure environment"**
5. Check **"Required reviewers"** and add yourself
6. Click **"Save protection rules"**

### Step 10: Deploy

The workflow has three trigger options:

#### Option A: Create a Release (Recommended for production)

1. Go to **Code** → **Releases** → **Create a new release**
2. Tag: `v1.0.0` (or your version)
3. Title: `Release v1.0.0`
4. Click **"Publish release"**
5. This triggers: Build → Test → Deploy Infrastructure → Deploy App

#### Option B: Manual Trigger

1. Go to **Actions** tab in your repository
2. Click **"Build, Test & Deploy"** workflow
3. Click **"Run workflow"**
4. Configure options:
   - **Deploy to production**: ✅ Check this to deploy
   - **Skip infrastructure deployment**: ✅ Check to skip infra (deploy app only)
5. Click **"Run workflow"**

#### Option C: Push to Main (Build & Test only)

- Push changes to `main` branch
- This only runs Build & Test (no deployment)
- Use this for CI validation

---

## Workflow Overview

```
┌──────────────────────────────────────────────────────────────────────────┐
│                        Build, Test & Deploy                              │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Push to main          Release/Manual                                    │
│       │                      │                                           │
│       ▼                      ▼                                           │
│  ┌─────────┐           ┌─────────┐                                       │
│  │  Build  │           │  Build  │                                       │
│  │  Test   │           │  Test   │                                       │
│  └─────────┘           └────┬────┘                                       │
│       │                     │                                            │
│       ▼                     ▼                                            │
│     Done              ┌──────────────┐                                   │
│                       │ skip_infra?  │                                   │
│                       └──────┬───────┘                                   │
│                              │                                           │
│                    ┌─────────┴─────────┐                                 │
│                    │                   │                                 │
│                    ▼ No                ▼ Yes                             │
│            ┌───────────────┐    ┌───────────────┐                        │
│            │ Deploy Infra  │    │    (skip)     │                        │
│            └───────┬───────┘    └───────┬───────┘                        │
│                    │                    │                                │
│                    └────────┬───────────┘                                │
│                             │                                            │
│                             ▼                                            │
│                    ┌───────────────┐                                     │
│                    │  Deploy App   │                                     │
│                    │ + Migrations  │                                     │
│                    └───────┬───────┘                                     │
│                            │                                             │
│                            ▼                                             │
│                    ┌───────────────┐                                     │
│                    │ Health Check  │                                     │
│                    └───────┬───────┘                                     │
│                            │                                             │
│                            ▼                                             │
│                    ┌───────────────┐                                     │
│                    │    Notify     │                                     │
│                    └───────────────┘                                     │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Post-Deployment Steps

### 1. Verify Infrastructure Created

After deployment, check Azure Portal:
- **Resource Group**: `fuelflow-prod-rg`
- **App Service**: `fuelflow-prod-app`
- **PostgreSQL**: `fuelflow-prod-db`
- **Redis**: `fuelflow-prod-redis`
- **Storage**: `fuelflowprodstorage`
- **Key Vault**: `fuelflowprodkv`

### 2. Get App Service URL

Your application will be available at:
```
https://fuelflow-prod-app.azurewebsites.net
```

### 3. Update Stripe Webhook (if URL changed)

1. Go to **https://dashboard.stripe.com/webhooks**
2. Click on your webhook endpoint
3. Update URL if needed to match your App Service URL

### 4. Verify Database Migrations

Check that migrations ran successfully:
1. Azure Portal → App Service → **Log stream**
2. Look for migration logs during startup

---

## Managing Infrastructure

### Redeploy App Only (Skip Infrastructure)

Use when you only changed application code:

1. Go to **Actions** → **Build, Test & Deploy**
2. Click **"Run workflow"**
3. Check:
   - ✅ Deploy to production
   - ✅ Skip infrastructure deployment
4. Click **"Run workflow"**

### Full Redeploy (Infrastructure + App)

Use when you changed infrastructure code in `infra/`:

1. Go to **Actions** → **Build, Test & Deploy**
2. Click **"Run workflow"**
3. Check:
   - ✅ Deploy to production
   - ❌ Skip infrastructure deployment (leave unchecked)
4. Click **"Run workflow"**

### View Deployment Status

1. Go to **GitHub → Actions**
2. Click on the latest **"Build, Test & Deploy"** workflow run
3. View logs for each job:
   - Build & Test
   - Deploy Infrastructure
   - Deploy Application
   - Notify

---

## Troubleshooting

### "Authorization failed" in GitHub Actions

1. Verify `AZURE_CREDENTIALS` secret contains valid JSON
2. Check Service Principal hasn't expired
3. Re-create Service Principal if needed (Step 3)

### "Resource provider not registered"

Run in Azure Cloud Shell:
```bash
az provider register --namespace Microsoft.Web
az provider register --namespace Microsoft.DBforPostgreSQL
az provider register --namespace Microsoft.Cache
az provider register --namespace Microsoft.Storage
az provider register --namespace Microsoft.KeyVault
az provider register --namespace Microsoft.Insights
```

### App Service shows "Application Error"

1. Check logs in Azure Portal:
   - App Service → **Log stream**
   - App Service → **Diagnose and solve problems**

2. Common causes:
   - Missing environment variables
   - Database connection failed
   - Wrong Node.js version

### Health Check Fails

The health check retries for 3 minutes. If it still fails:
1. Check app logs for startup errors
2. Verify database migrations completed
3. Check Key Vault secrets are accessible

### View Key Vault Secrets

```bash
# List secrets
az keyvault secret list --vault-name fuelflowprodkv --query "[].name" -o tsv

# Get specific secret
az keyvault secret show --vault-name fuelflowprodkv --name PostgresConnectionString --query value -o tsv
```

---

## Security Notes

- **Never commit secrets** to repository
- **Rotate secrets** every 90 days
- **Use production Stripe keys** only after testing with test keys
- **PostgreSQL password** should be 16+ characters with mixed case, numbers, and symbols
- **Review Key Vault access logs** monthly in Azure Portal

---

## Quick Reference

| Resource | URL / Command |
|----------|---------------|
| App Service | https://fuelflow-prod-app.azurewebsites.net |
| Azure Portal | https://portal.azure.com |
| Stripe Dashboard | https://dashboard.stripe.com |
| Twilio Console | https://console.twilio.com |
| Google AI Studio | https://aistudio.google.com |
| GitHub Actions | Your repo → Actions → Build, Test & Deploy |

| Azure CLI Commands | Description |
|-------------------|-------------|
| `az login` | Login to Azure |
| `az account show` | Show current subscription |
| `az webapp log tail --name fuelflow-prod-app --resource-group fuelflow-prod-rg` | Stream app logs |
| `az group list -o table` | List resource groups |
