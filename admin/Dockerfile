# Build stage
FROM node:20-alpine@sha256:fb4cd12c85ee03686f6af5362a0b0d56d50c58a04632e6c0fb8363f609372293 AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# Sentry DSN is baked into the static bundle at build time (Vite inlines VITE_* env
# vars). Optional: an empty value means Sentry is never initialised (src/lib/sentry.ts).
# The DSN is a write-only ingest key meant to live in client code, not an account secret.
ARG VITE_SENTRY_DSN=""
ENV VITE_SENTRY_DSN=$VITE_SENTRY_DSN
RUN npm run build

# Runtime stage: serve the static bundle with nginx (not the Vite dev server)
FROM nginx:alpine@sha256:4a73073bd557c65b759505da037898b61f1be6cbcc3c2c3aeac22d2a470c1752
COPY --from=build /app/dist /usr/share/nginx/html
# A TEMPLATE, not a static file: the same image serves production and staging, and
# they have different backend names. The nginx image substitutes ${BACKEND_HOST} into it
# at container start; NGINX_ENVSUBST_FILTER is set in the compose file, because envsubst
# with no filter would also eat nginx's own $variables.
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
EXPOSE 5000
CMD ["nginx", "-g", "daemon off;"]