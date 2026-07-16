# syntax=docker/dockerfile:1
# ─────────────────────────────────────────────────────────────────────────────
# admin-web — Vite/React SPA served by nginx.
# Build context: admin-web (this directory).
#
# The three VITE_* API base URLs are baked in at build time (Vite inlines
# import.meta.env). Point them at the gateway's path prefixes:
#
#   docker build \
#     --build-arg VITE_IDENTITY_URL=https://api.example.com/identity \
#     --build-arg VITE_CATALOG_URL=https://api.example.com/catalog \
#     --build-arg VITE_ORDERS_URL=https://api.example.com/orders \
#     -t laundryghar-admin-web .
# ─────────────────────────────────────────────────────────────────────────────

FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

ARG VITE_IDENTITY_URL
ARG VITE_CATALOG_URL
ARG VITE_ORDERS_URL
ENV VITE_IDENTITY_URL=$VITE_IDENTITY_URL \
    VITE_CATALOG_URL=$VITE_CATALOG_URL \
    VITE_ORDERS_URL=$VITE_ORDERS_URL

RUN npm run build

FROM nginx:alpine AS runtime
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
    CMD wget -q --spider http://localhost:80/ || exit 1
