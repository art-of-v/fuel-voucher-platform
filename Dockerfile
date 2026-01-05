# Build stage
FROM node:20-slim AS builder

WORKDIR /app

# Install build dependencies for canvas (Debian/Ubuntu)
RUN apt-get update && apt-get install -y \
    python3 \
    make \
    g++ \
    pkg-config \
    libcairo2-dev \
    libpango1.0-dev \
    libjpeg-dev \
    libgif-dev \
    librsvg2-dev \
    && rm -rf /var/lib/apt/lists/*

# Copy package files
COPY package*.json ./

# Install all dependencies
RUN npm ci

# Copy source code
COPY . .

# Build the application
RUN npm run build

# Backend production stage
FROM node:20-slim AS backend

WORKDIR /app

# Install runtime dependencies for canvas and curl
RUN apt-get update && apt-get install -y \
    curl \
    libcairo2 \
    libpango-1.0-0 \
    libpangocairo-1.0-0 \
    libgdk-pixbuf-2.0-0 \
    libjpeg62-turbo \
    libgif7 \
    librsvg2-2 \
    && rm -rf /var/lib/apt/lists/*

# Copy package files
COPY package*.json ./

# Install production dependencies only
# We might need build tools again if prebuilds fail, but let's try without first.
# If it fails, we will add build-essential to this stage too.
RUN npm ci --only=production

# Copy built files from builder
# Copy source code for runtime (using tsx)
COPY server ./server
RUN cat server/static.ts
# Copy client build to server/public so serveStatic finds it
COPY --from=builder /app/dist/public ./server/public
COPY shared ./shared
COPY drizzle.config.ts ./
COPY tsconfig.json ./

# Expose port
EXPOSE 5000

# Start the server with tsx (bypassing bundle issues)
CMD ["npx", "tsx", "server/index.ts"]
