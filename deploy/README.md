# deploy — containerized production topology

```
                    ┌──────────────────────────────────────────────┐
   internet ──TLS──►│ your reverse proxy / LB (nginx, ALB, Caddy)  │
                    └───────────────┬──────────────────┬───────────┘
                                    ▼                  ▼
                             gateway :8080      admin-web :8081
                             (YARP, CORS,        (nginx SPA)
                              rate limit)
                        ┌───────────┼───────────┐
                        ▼           ▼           ▼
                     core       operations   commerce      (internal :8080,
                    :identity   :catalog     :commerce      not published)
                    :engagement :orders      :finance
                    :mcp        :warehouse   :analytics
                                :logistics
                        └───────────┴───────────┘
                                    ▼
                        PostgreSQL 18 (managed / external;
                        `--profile local-db` for staging)
```

## Bring-up

```bash
cd deploy
cp .env.example .env       # fill in secrets — see PRODUCTION_ENV.md for the full reference
docker compose build
docker compose up -d
docker compose ps          # every service should report healthy
```

Staging with a bundled database: `docker compose --profile local-db up -d`
(then bootstrap the schema: `db/build_from_scratch.sh` + `db/tools/migrate.sh up`
pointed at the container; note the bundled `postgres:18` image lacks
pg_partman/postgis — install `postgresql-18-partman postgresql-18-postgis-3`
in it first, as `ops/backup/verify-backup.sh` does).

## Key facts

- Images: one parameterized Dockerfile builds all 4 backend hosts
  (`backend/laundryghar/Dockerfile`, `--build-arg PROJECT=...`);
  `admin-web/Dockerfile` bakes the three `VITE_*_URL`s at build time from
  `PUBLIC_API_URL` — changing the public API URL requires an image rebuild.
- Only the gateway and admin-web publish host ports. Service-to-service and
  gateway-to-service traffic stays on the private compose network.
- Health: every backend container has a Docker HEALTHCHECK on `/alive`
  (enabled by `HealthChecks__Expose=true`; the endpoints are unauthenticated,
  which is safe here because service ports are not published).
- JWT: core signs RS256 tokens (`JWT_PRIVATE_KEY`); operations/commerce fetch
  the JWKS from `http://core:8080` with `Jwt__RequireHttpsMetadata=false` —
  acceptable only because that hop never leaves the private network.
- TLS terminates at your proxy; when it forwards `X-Forwarded-*`, set
  `ForwardedHeaders__Enabled=true` on the gateway so rate limiting sees real
  client IPs.
- DB migrations are NOT run by containers at startup (deliberate — schema
  changes are an operator step): `db/tools/migrate.sh up` before rolling new
  images that expect new schema.
- Backups: wire `ops/backup/backup.sh` on the DB host / a cron container.

## CI images

`.github/workflows/release.yml` builds and pushes all five images to GHCR on
every push to `main` (tags: `latest` + commit SHA) — pull instead of building
on the server:

```bash
docker compose pull && docker compose up -d
```
