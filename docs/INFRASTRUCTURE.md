# FuelFlow Infrastructure Architecture

## Overview

FuelFlow uses Azure cloud infrastructure managed via CDKTF (Cloud Development Kit for Terraform) with C#. This document describes the production infrastructure architecture, resource specifications, and operational considerations.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Azure (West Europe)                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │                         Resource Group                                │  │
│  │                        fuelflow-prod-rg                               │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│  ┌─────────────────┐                          ┌─────────────────┐          │
│  │   App Service   │─────────────────────────▶│   PostgreSQL    │          │
│  │   Plan (B1)     │                          │  Flexible B1ms  │          │
│  │                 │                          │   Version 16    │          │
│  │  ┌───────────┐  │                          │   32 GB SSD     │          │
│  │  │ Linux Web │  │                          └────────┬────────┘          │
│  │  │   App     │  │                                   │                   │
│  │  │ Node.js20 │  │                                   │                   │
│  │  └───────────┘  │                                   │                   │
│  └────────┬────────┘                                   │                   │
│           │                                            │                   │
│           │         ┌─────────────────┐                │                   │
│           ├────────▶│  Azure Cache    │                │                   │
│           │         │  for Redis      │                │                   │
│           │         │  Basic C0       │                │                   │
│           │         └─────────────────┘                │                   │
│           │                                            │                   │
│           │         ┌─────────────────┐                │                   │
│           ├────────▶│   Key Vault     │◀───────────────┘                   │
│           │         │   (Secrets)     │                                    │
│           │         └─────────────────┘                                    │
│           │                                                                │
│           │         ┌─────────────────┐                                    │
│           └────────▶│  Blob Storage   │                                    │
│                     │  (Voucher PDFs) │                                    │
│                     └─────────────────┘                                    │
│                                                                            │
│  ┌─────────────────┐         ┌─────────────────┐                          │
│  │  App Insights   │────────▶│  Log Analytics  │                          │
│  │  (Telemetry)    │         │   (30 days)     │                          │
│  └─────────────────┘         └─────────────────┘                          │
│                                                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │                    Azure Monitor Alerts                              │  │
│  │              (5xx errors, CPU, Memory, Response Time)                │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│                                                                            │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Data Flow

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Mobile    │────▶│   App       │────▶│  PostgreSQL │     │   Blob      │
│   App       │     │   Service   │     │  (Orders,   │     │   Storage   │
│   (React)   │     │  (Node.js)  │     │   Users,    │     │  (Voucher   │
└─────────────┘     │             │     │   Vouchers) │     │   PDFs)     │
                    │             │     └─────────────┘     └─────────────┘
┌─────────────┐     │             │            │                  ▲
│   Admin     │────▶│             │            │                  │
│   Panel     │     │             │────────────┼──────────────────┘
│   (React)   │     │             │            │           (PDF Upload)
└─────────────┘     │             │            │
                    │             │     ┌─────────────┐
                    │             │────▶│   Redis     │
                    │             │     │  (Streams)  │
                    └─────────────┘     │  Event Bus  │
                           │            └──────┬──────┘
                           │                   │
                           │            ┌──────▼──────┐
                           │            │ Fulfillment │
                           └───────────▶│  Consumer   │
                                        │ (In-process)│
                                        └─────────────┘
```

## Event-Driven Fulfillment Flow

```
1. User Purchase Flow:
   ┌────────┐    ┌─────────┐    ┌──────────┐    ┌───────────┐
   │ Mobile │───▶│ Checkout│───▶│  Create  │───▶│  Publish  │
   │  App   │    │   API   │    │  Order   │    │  Event    │
   └────────┘    └─────────┘    └──────────┘    └─────┬─────┘
                                                      │
                      ┌───────────────────────────────┤
                      ▼                               ▼
               ┌─────────────┐                ┌─────────────┐
               │ Redis Stream│                │  DB Outbox  │
               │  (Primary)  │                │ (Fallback)  │
               └──────┬──────┘                └──────┬──────┘
                      │                              │
                      └──────────────┬───────────────┘
                                     ▼
                              ┌─────────────┐
                              │ Fulfillment │
                              │  Consumer   │
                              └──────┬──────┘
                                     ▼
                              ┌─────────────┐
                              │   Assign    │
                              │   Voucher   │
                              │  (FIFO +    │
                              │  Row Lock)  │
                              └──────┬──────┘
                                     ▼
                              ┌─────────────┐
                              │   Update    │
                              │   Order     │
                              │  FULFILLED  │
                              └─────────────┘

2. Voucher Import Flow:
   ┌────────┐    ┌─────────┐    ┌──────────┐    ┌───────────┐
   │ Admin  │───▶│ Upload  │───▶│  Blob    │───▶│  Gemini   │
   │ Panel  │    │  PDF    │    │ Storage  │    │  AI OCR   │
   └────────┘    └─────────┘    └──────────┘    └─────┬─────┘
                                                      │
                                               ┌──────▼──────┐
                                               │   Create    │
                                               │  Vouchers   │
                                               └──────┬──────┘
                                                      │
                                               ┌──────▼──────┐
                                               │   Publish   │
                                               │ VOUCHERS_   │
                                               │  IMPORTED   │
                                               └──────┬──────┘
                                                      │
                                               ┌──────▼──────┐
                                               │  Backfill   │
                                               │   Pending   │
                                               │   Orders    │
                                               └─────────────┘
```

## Resource Specifications

| Resource | SKU/Tier | Specs | Monthly Cost |
|----------|----------|-------|--------------|
| App Service Plan | B1 | 1 vCPU, 1.75 GB RAM, Linux | ~$13 |
| App Service | - | Node.js 20 LTS, HTTPS only | (included) |
| PostgreSQL Flexible | B_Standard_B1ms | 1 vCore, 2 GB RAM, 32 GB SSD | ~$12 |
| Azure Cache for Redis | Basic C0 | 250 MB, No SLA | ~$13 |
| Storage Account | Standard LRS | Hot tier, 10 GB estimated | ~$0.50 |
| Key Vault | Standard | RBAC authorization | ~$1 |
| Application Insights | Free tier | 5 GB/month ingestion | Free |
| Log Analytics | Free tier | 5 GB/month, 30 days retention | Free |
| **Total** | | | **~$39-42/month** |

## Security Architecture

### Network Security
- App Service: HTTPS only, TLS 1.2 minimum
- PostgreSQL: Firewall rules (Azure services + allowed IPs)
- Redis: Private endpoint within Azure (VNet optional)
- Blob Storage: Private access, SAS tokens for uploads

### Secret Management
```
┌─────────────────────────────────────────────────────────────────┐
│                        Key Vault                                 │
├─────────────────────────────────────────────────────────────────┤
│  PostgresConnectionString   ───▶ Host, User, Password, SSL     │
│  RedisConnectionString      ───▶ Redis hostname + key          │
│  StripeSecretKey            ───▶ Stripe API secret             │
│  StripeWebhookSecret        ───▶ Webhook signature verification│
│  GeminiApiKey               ───▶ Google AI API key             │
│  TwilioAccountSid           ───▶ Twilio account                │
│  TwilioAuthToken            ───▶ Twilio auth                   │
│  TwilioPhoneNumber          ───▶ SMS sender number             │
│  SessionSecret              ───▶ Express session encryption    │
│  QrEncryptionKey            ───▶ Voucher QR data encryption    │
│  StorageConnectionString    ───▶ Blob storage access           │
└─────────────────────────────────────────────────────────────────┘
```

### Access Control
- App Service uses **Managed Identity** (System Assigned)
- Key Vault access via **RBAC** (Key Vault Secrets User role)
- No credentials stored in code or config files

## Monitoring & Alerting

### Application Insights
- Automatic telemetry collection
- Request/response tracking
- Dependency tracking (DB, Redis, external APIs)
- Custom events for business metrics

### Configured Alerts

| Alert | Severity | Condition | Action |
|-------|----------|-----------|--------|
| HTTP 5xx Errors | Critical (0) | > 10 in 5 min | Email |
| High CPU | Warning (2) | > 80% for 5 min | Email |
| High Memory | Warning (2) | > 85% for 5 min | Email |
| Slow Response | Warning (2) | Avg > 2s for 5 min | Email |
| DB Connection Failed | Critical (0) | > 5 in 5 min | Email |
| Redis Connection Failed | Critical (0) | Any failure | Email |

## Deployment Architecture

### CI/CD Flow
```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   GitHub    │────▶│   GitHub    │────▶│   Azure     │
│   Push/PR   │     │   Actions   │     │   Deploy    │
└─────────────┘     └─────────────┘     └─────────────┘
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
        ┌─────────┐  ┌─────────┐  ┌─────────┐
        │  Build  │  │  Test   │  │ Deploy  │
        │  (npm)  │  │ (jest)  │  │ (az)    │
        └─────────┘  └─────────┘  └─────────┘
```

### Infrastructure Deployment
```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   CDKTF     │────▶│  Terraform  │────▶│   Azure     │
│   Synth     │     │   Plan      │     │   Apply     │
└─────────────┘     └─────────────┘     └─────────────┘
```

## Backup & Recovery

### PostgreSQL
- Automated backups: 7 days retention
- Point-in-time restore capability
- Geo-redundant backup: Disabled (cost optimization)

### Blob Storage
- Soft delete: 7 days
- Versioning: Disabled (cost optimization)
- LRS redundancy (3 copies within region)

### Recovery Time Objectives
- Database: < 1 hour (point-in-time restore)
- Application: < 5 minutes (redeploy from GitHub)
- Full disaster recovery: < 2 hours

## Scaling Considerations

### Current Setup (No Auto-scaling)
- Single B1 instance handles ~100-500 concurrent users
- Database handles ~100 connections
- Redis handles ~1000 ops/sec

### Future Scaling Path
1. **Vertical**: Upgrade to B2/B3 for more CPU/RAM
2. **Horizontal**: Enable auto-scaling (1-3 instances)
3. **Database**: Upgrade to General Purpose tier
4. **Redis**: Upgrade to Standard C1 for SLA

## Cost Optimization

### Implemented
- B1 tier (sufficient for MVP)
- Basic Redis (no SLA but cost-effective)
- Free tier monitoring
- Single region deployment

### Future Optimizations
- Reserved instances (1-year commitment = 30% savings)
- Azure Hybrid Benefit (if applicable)
- Right-sizing based on actual usage metrics

## Environment Variables Reference

| Variable | Source | Description |
|----------|--------|-------------|
| `NODE_ENV` | App Setting | Environment (production) |
| `PORT` | App Setting | Server port (4000) |
| `DATABASE_URL` | Key Vault | PostgreSQL connection string |
| `REDIS_URL` | Key Vault | Redis connection string |
| `SESSION_SECRET` | Key Vault | Express session secret |
| `STRIPE_SECRET_KEY` | Key Vault | Stripe API key |
| `STRIPE_PUBLISHABLE_KEY` | App Setting | Stripe public key |
| `STRIPE_WEBHOOK_SECRET` | Key Vault | Stripe webhook secret |
| `GEMINI_API_KEY` | Key Vault | Google AI API key |
| `TWILIO_ACCOUNT_SID` | Key Vault | Twilio account |
| `TWILIO_AUTH_TOKEN` | Key Vault | Twilio auth token |
| `TWILIO_PHONE_NUMBER` | Key Vault | Twilio phone number |
| `QR_ENCRYPTION_KEY` | Key Vault | QR data encryption key |
| `AZURE_STORAGE_CONNECTION_STRING` | Key Vault | Blob storage connection |

## Related Documentation

- [ARCHITECTURE.md](./ARCHITECTURE.md) - Application architecture
- [ABOUT_SOLUTION.md](../ABOUT_SOLUTION.md) - Solution overview
- [infra/README.md](../infra/README.md) - Infrastructure deployment guide
