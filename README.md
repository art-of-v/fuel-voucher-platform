# Fuel-Flow

Fuel-Flow is a comprehensive fuel voucher management system designed to streamline the process of acquiring, managing, and using fuel vouchers through a modern digital interface.

## System Overview

The project consists of three main components:

- **Admin Backend**: A robust Node.js/Express API that handles voucher processing, user management, and payments.
- **Admin Frontend**: A React-based dashboard for administrators to upload vouchers via OCR and manage the system.
- **Mobile Application**: A cross-platform mobile app (React/Capacitor) for users to purchase and view their fuel vouchers.

## Getting Started

### Prerequisites

- [Docker](https://www.docker.com/) and Docker Compose installed.
- A `.env` file in the root directory (refer to `.env.example`).

### Running the Application

To start the entire stack, run the following command in the project root:

```bash
docker-compose up --build
```

The services will be available at:

- **Admin Frontend**: [http://localhost:5002](http://localhost:5002)
- **Mobile App (Web View)**: [http://localhost:5001](http://localhost:5001)
- **Admin Backend API**: [http://localhost:4000](http://localhost:4000)

## Documentation

For a deeper dive into the technical details, topology, and implementation, please refer to the [ABOUT_SOLUTION.md](ABOUT_SOLUTION.md) file.
