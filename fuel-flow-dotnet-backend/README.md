# FuelFlow API

A .NET 10 Web API that imports fuel voucher data from PDF files. Renders PDF pages, detects voucher regions, parses provider-specific data, decodes QR codes, and persists results to PostgreSQL.

## Features

- **PDF import** — upload PDFs containing fuel vouchers via a multipart form endpoint
- **Automatic page rendering** — renders PDF pages at 300 DPI using PDFium (via Docnet.Core) and extracts text with PdfPig
- **Voucher region detection** — clusters words by proximity to locate individual vouchers on a page
- **Provider-specific parsing** — currently supports OKKO vouchers with fuel type, liters, expiration date, and voucher number extraction
- **QR code decoding** — decodes QR codes from cropped voucher images using ZXing.Net
- **Duplicate detection** — deduplicates by voucher number and QR payload (in-memory batch + database)
- **PostgreSQL persistence** — stores vouchers, import records, and import errors via Entity Framework Core
- **Docker support** — multi-stage Dockerfile + docker-compose with PostgreSQL 16

## Tech Stack

| Component | Technology |
|---|---|
| Runtime | .NET 10 (Web API) |
| Database | PostgreSQL 16 via Entity Framework Core + Npgsql |
| PDF Rendering | Docnet.Core (PDFium) at 300 DPI |
| PDF Text | UglyToad.PdfPig |
| QR Decoding | ZXing.Net + ZXing.Net.Bindings.ImageSharp.V2 |
| Image Processing | SixLabors.ImageSharp |
| Logging | Serilog |
| API Docs | Swagger / Swashbuckle |
| Testing | xUnit, FluentAssertions, Moq, Testcontainers |

## Architecture

The project follows a **vertical slice architecture** — code is organized by feature rather than by technical layer.

```
Features/
└── Vouchers/
    └── Import/               ← one complete use case
        ├── ImportVouchersCommand.cs   ← command + handler
        ├── VouchersController.cs      ← API endpoint
        ├── Models/                    ← entities + DTOs
        ├── Abstractions/              ← slice-specific interfaces
        └── Services/                  ← implementations
```

Each slice is self-contained with its own models, interfaces, and services. Shared infrastructure (DbContext, configuration) is cross-cutting.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker Desktop (for integration tests and docker-compose)
- PostgreSQL 16 (if running without Docker)

## Getting Started

### 1. Clone and run with Docker

```bash
git clone <repo-url>
cd fuel-flow-dotnet
docker compose up --build
```

The API is available at `http://localhost:5000` with Swagger at the root.

### 2. Run locally with a local PostgreSQL

Update `appsettings.json` `Database.ConnectionString` to point to your local instance, then:

```bash
dotnet run --project src/FuelFlow.API
```

### 3. Run tests

```bash
dotnet test tests/FuelFlow.UnitTests
dotnet test tests/FuelFlow.IntegrationTests   # requires Docker
```

## Configuration

Configuration is managed via the .NET options pattern:

```json
{
  "Database": {
    "ConnectionString": "Host=postgres;Port=5432;Database=fuelflow;Username=postgres;Password=postgres"
  }
}
```

Override via environment variables using the `__` separator:

```bash
Database__ConnectionString=Host=localhost;Port=5432;Database=fuelflow;Username=postgres;Password=postgres
```

## API

### POST /api/Vouchers/import

Upload a PDF file containing fuel vouchers.

**Request:** `multipart/form-data`

| Field | Type | Description |
|---|---|---|
| file | IFormFile | PDF file with fuel vouchers |

**Response:** `200 OK`

```json
{
  "importId": "guid",
  "imported": 5,
  "duplicates": 0,
  "failed": 0,
  "durationSeconds": 1.24
}
```

**Errors:** `400 Bad Request` — when no file, empty file, or non-PDF file is uploaded.

## Project Structure

```
src/FuelFlow.API/
├── Options/                ← strongly-typed configuration
├── Persistence/
│   ├── ApplicationDbContext.cs
│   └── Migrations/         ← EF Core migrations
├── Features/
│   └── Vouchers/Import/    ← Import feature slice
│       ├── Models/         ← entities + DTOs
│       ├── Abstractions/   ← interfaces
│       └── Services/       ← implementations
├── Program.cs
└── appsettings.json

tests/
├── FuelFlow.UnitTests/         ← unit tests (OkkoVoucherParser)
└── FuelFlow.IntegrationTests/  ← integration tests (full HTTP pipeline)
```

## Docker

The Dockerfile uses a multi-stage build:

1. **Build stage** — `mcr.microsoft.com/dotnet/sdk:10.0`, restores with `linux-x64` RID (required for Docnet.Core's native `pdfium.so`), publishes
2. **Runtime stage** — `mcr.microsoft.com/dotnet/aspnet:10.0`, installs PDFium native dependencies (`libglib2.0-0`, `libfontconfig1`, `libfreetype6`)

Run with docker-compose (includes PostgreSQL 16 with health check):

```bash
docker compose up --build
```
