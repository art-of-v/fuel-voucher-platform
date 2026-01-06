FROM node:22-slim
RUN apt-get update && apt-get install -y \

    make \
    g++ \
    pkg-config \
    libcairo2-dev \
    libpango1.0-dev \
    libjpeg-dev \
    libgif-dev \
    librsvg2-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY package.json ./
RUN npm install
COPY . .
RUN npm run build
RUN mkdir uploads
EXPOSE 4000
CMD ["npm", "start"]
