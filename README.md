# Fuel-Flow - Fuel Management System

Fuel-Flow is a comprehensive system for managing fuel vouchers, stations, and purchases. It consists of an Admin Panel (Web) for management and a Mobile App for users to purchase and redeem fuel.

## 🚀 Quick Start (Local Development)

The easiest way to run the entire system is using **Docker Compose**.

### Prerequisites

- Docker & Docker Compose installed
- Node.js (v18+) (opt. for local dev)

### How to Run

1. **Clone the repository** (if you haven't already).
2. **Environment Variables**:
    - The project comes with pre-configured defaults in `docker-compose.yml`.
    - If you need to change API keys (e.g., Gemini API Key), edit `docker-compose.yml` -> `admin-backend` service.
3. **Start the System**:
    Run the following command in the root directory:

    ```bash
    docker-compose up -d --build
    ```

    This will build and start the following services:
    - **PostgreSQL Database** (`postgres:16`)
    - **Admin Backend** (Node.js/Express)
    - **Admin Frontend** (React/Vite)
    - **Mobile App** (React Native/Expo Web Preview) - *Optional depending on config*

4. **Access the Applications**:
    - **Admin Panel**: [http://localhost:5002](http://localhost:5002)
    - **Backend API**: [http://localhost:4000](http://localhost:4000)
    - **Mobile App (Web)**: [http://localhost:5001](http://localhost:5001)

## 📂 Project Structure

```
Fuel-Flow/
├── admin-panel/
│   ├── backend/            # Admin Panel Backend (Node.js, Express, Drizzle ORM)
│   │   ├── src/
│   │   │   ├── infrastructure/  # DB, Storage, Gemini/OCR Services
│   │   │   ├── interfaces/      # API Routes & Controllers
│   │   │   └── ...
│   │   └── Dockerfile
│   └── frontend/           # Admin Panel Frontend (React, Vite, Tailwind)
│       ├── src/
│       │   ├── pages/           # Admin Dashboard & Import Screens
│       │   └── ...
│       └── Dockerfile
├── mobile-app/             # Fuel Flow Mobile App (React Native / Expo)
├── docker-compose.yml      # Docker orchestration config
└── README.md               # This file
```

## ✨ Features

### 1. Admin Panel

The Admin Panel is the control center for the platform.

#### 🎫 Deep Dive: Voucher Import Workflow

The **Import Vouchers** (`Імпорт талонів`) tab is a sophisticated tool powered by AI to digitize fuel vouchers from PDF or image files.

**How to Use:**

1. **File Selection**:
    - **Drag & Drop**: You can drag PDF or Image files directly onto the dashed upload area. The border turns green to indicate the drop zone is active.
    - **Click to Select**: Alternatively, click the "Select Files" button to open the system dialog.
    - *UI Feedback*: Once selected, the system displays "X файлів вибрано" (X files selected) in green text.

2. **Starting Import**:
    - Click the **"Почати імпорт"** (Start Import) button.
    - The button will show a loading spinner, and the status bar will appear showing "PROCESSING" (`ОБРОБКА`).

3. **Analysis Process (Backend)**:
    - The file is uploaded to the backend `ImportOrchestrator`.
    - **Gemini AI** analyzes the document to extract:
        - **Fuel Type** (e.g., "ДП ЄВРО", "A-95").
        - **Volume** (e.g., "10 liters").
        - **External ID**: A unique identifier printed on the voucher.
        - **QR Code**: The system scans for QR codes locally and matches them with the AI-extracted data.
    - **Duplicate Check**: Before saving, the system checks the database (via `unique` index on `provider` + `externalId`). If a voucher exists, it is **skipped** and counted as a "Duplicate".

4. **Results & Status**:
    - Once finished, the status bar updates to **COMPLETED** (`ЗАВЕРШЕНО`) or **ERROR**.
    - **Detailed Stats** are *always* displayed:
        - **Success** (`Успішно`): Number of NEW vouchers added to the database.
        - **Errors** (`Помилки`): Number of vouchers/files that failed processing.
        - **Duplicates** (`Дублікати`): Number of vouchers skipped because they were already imported.
    - *Note*: If you import the same file twice, the second run will show "Success: 0, Duplicates: [N]", indicating the system correctly protected your data.

5. **Review**:
    - Imported vouchers appear immediately in the table below.
    - You can click the **Clone/Copy** icon on an image placeholder to see the raw QR data.
    - **Delete All**: The "Видалити усі" button allows clearing the database for testing purposes.

#### Other Features

* **Stations**: Create, edit, and delete gas stations (branding, colors, logos).
- **Fuel Types**: Manage fuel types (Petrol, Diesel, Gas) and assign them to stations.
- **Packages**: Create fuel packages (e.g., "10L A-95") with discounted prices.
- **Purchases**: View all user purchases and their status.
- **QR Codes**: Manage the inventory of fuel vouchers/QR codes.

### 2. Backend API

* **Tech Stack**: Node.js, Express, Drizzle ORM, PostgreSQL.
- **Storage**:
  - **DatabaseStorage**: Production-ready storage using PostgreSQL.
- **Orchestration**: robust `ImportOrchestrator` to handle large file processing, retry logic, and concurrent analysis.

### 3. Mobile App

* **User Flow**: Login, Browse Stations, Select Fuel/Package, Purchase, View QR Code.
- **Wallet**: Users see their purchased gallons/liters and can redeem them at stations.

## 🛠 Troubleshooting

- **Import fails with "Zero vouchers detected"**:
  - Likely due to all vouchers in the file being duplicates (already imported). Check the "Duplicate" count in the UI.
- **Database Connection Error**:
  - Ensure the `fuel-admin-db` container is healthy.
  - Check `docker logs fuel-admin-backend`.

## 📜 License

Private
