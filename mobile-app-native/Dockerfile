FROM node:20-alpine AS builder

WORKDIR /app

RUN apk add --no-cache python3 make g++

COPY package.json ./
RUN rm -f package-lock.json
RUN npm install --legacy-peer-deps

COPY . .

# Export web build
RUN npx expo export --platform web --clear && \
    sed -i 's/<script/<script type="module"/g' dist/index.html && \
    sed -i 's/<\/head>/<link rel="preconnect" href="https:\/\/fonts.googleapis.com"><link rel="preconnect" href="https:\/\/fonts.gstatic.com" crossorigin><link href="https:\/\/fonts.googleapis.com\/css2?family=Inter:wght@400;700\&family=Rajdhani:wght@400;600;700\&display=swap" rel="stylesheet"><\/head>/g' dist/index.html

FROM nginx:alpine
COPY --from=builder /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 5000
CMD ["nginx", "-g", "daemon off;"]
