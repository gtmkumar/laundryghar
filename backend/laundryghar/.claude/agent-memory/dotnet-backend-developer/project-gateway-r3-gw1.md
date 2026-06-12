---
name: project-gateway-r3-gw1
description: R3-GW-1 API Gateway (laundryghar.Gateway, :8080) — design decisions and invariants the security review signed off on
metadata:
  type: project
---

R3-GW-1 delivered 2026-06-12: YARP 2.3.0 gateway project at `laundryghar.Gateway` listening on :8080 in dev.

Key invariants security review signed off on — do not change without a re-review:
- **Forward, never re-issue**: Authorization bearer and X-Brand-Id pass through untouched. Services validate RS256 via Identity JWKS. Gateway does not touch tokens.
- **YARP forwards X-Forwarded-For/Proto/Host by default** — no explicit transform needed. Identity's rate limiter and OAuth URL construction depend on these.
- **CORS at the gateway only** (dev: :5173 + :5174 with credentials). Per-service CORS remains for direct-port access; gateway becomes the single CORS point when clients adopt it.
- **Global rate limiter**: 300 req/min per client IP, fixed-window, partitioned using X-Forwarded-For leftmost IP. Auth paths hit Identity's own stricter limiter on top of this backstop.
- **Path-prefix routing with prefix stripped**: `/identity/{**catch-all}` → :5050/`{catch-all}`, etc. No URL-shape change for clients — only the host changes.
- **Aggregate health** at `GET /health/services`: fans out to each `/health/ready` in parallel (3 s timeout each via named HttpClient); returns 200 all-healthy, 207 if any degraded.

**Why:** additive design — per-service ports stay active. Clients migrate by changing base URL only.

Cluster destinations are config-driven (`Gateway:Clusters:{name}:Destinations:primary:Address`) with dev defaults in appsettings.json and env-var overrides injected by AppHost. This is the correct override path for prod (real hostnames via secrets manager).

**How to apply:** any future gateway middleware or transform must honour the forward-not-reissue invariant. Rate limiter policy name is `GlobalPerIp`; CORS policy name is `GatewayCors`. Health fan-out lives in `HealthServicesEndpoint.cs`.
