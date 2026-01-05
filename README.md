# Fuel Flow & Admin Panel - Docker Setup

This project consists of a **Mobile Web App** (Client) and an **Admin Panel**, both powered by a backend and a PostgreSQL database.

## 🚀 Easy Start (Run Everything)

Prerequisites:
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) installed and running.

### 1. Setup Environment
Copy the example environment file:
```bash
cp .env.example .env
```
*(You can leave the default values in `.env` for local testing)*

### 2. Run with Docker
Open your terminal in the project root and run:
```bash
docker-compose up --build
```
*It may take a few minutes to build the images the first time.*

### 3. Access the Apps
Once the logs say "server started on port 5000", you can access:

| App | URL | Description |
|-----|-----|-------------|
| 📱 **Mobile App** | [http://localhost:5000](http://localhost:5000) | The client-side fuel purchasing app. |
| 🛡️ **Admin Panel** | [http://localhost:5001](http://localhost:5001) | The dashboard for managing stations, vouchers, etc. |

### 🛠️ Useful Commands

**Stop the apps:**
Press `Ctrl+C` in the terminal.

**Stop and remove containers (Clean Slate):**
```bash
docker-compose down
```

**Stop and remove volumes (Reset Database):**
```bash
docker-compose down -v
```

---

### Troubleshooting
- **Database Connection Error?** Wait a few seconds, the database might still be starting up. The `migrate` container handles schema updates automatically.
- **Port Conflict?** Ensure ports `5000`, `5001`, and `5433` are not used by other applications.
