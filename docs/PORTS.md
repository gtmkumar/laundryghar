# Port map

Every port this repo binds, and why it is that number. Written 2026-08-25 after a live-test pass
found three port faults on a developer machine that runs several projects at once.

**Rule:** a port here is *reserved*, not *hoped for*. If a service can silently move when its port is
busy, it has no entry in this file — it has a bug. Pin it, then add it.

## Local development

| Service | Port | Pinned by |
|---|---|---|
| core.WebApi (standalone) | **5056** | `scripts/run-stack.sh`, `core.WebApi/Properties/launchSettings.json` |
| operations.WebApi (standalone) | **5015** | `scripts/run-stack.sh`, `operations.WebApi/Properties/launchSettings.json` |
| commerce.WebApi (standalone) | **5242** | `scripts/run-stack.sh`, `commerce.WebApi/Properties/launchSettings.json` |
| admin-web (Vite) | **5174** | `admin-web/vite.config.ts` — `port` + `strictPort` |
| pos-web (Vite) | **5175** | `pos-web/vite.config.ts` — `port` + `strictPort` |
| customer-mobile Metro | **9091** | `scripts/run-stack.sh` (`METRO_PORT`) |
| rider-mobile Metro | **9092** | passed to `expo start --port` |
| PostgreSQL | 5432 | local install, not ours |

The standalone hosts are what `run-stack.sh` starts and what the `admin-web` / `pos-web` dev
proxies target. This is the topology to use for web work — **not** the AppHost below.

**pos-web was a fourth casualty of the 8080 problem.** Its `.env` pointed every service at
`http://localhost:8080/<prefix>`, so in local dev the POS was talking to another project's uvicorn
container and getting 404s for everything. It now has the same Vite proxy admin-web uses, and a
gitignored `.env.local` with relative prefixes. The tracked `.env` is untouched — it is the gateway
wiring for a real deployment, where 8080 is correct.

## AppHost (Aspire) — reserved block 5300–5303

| Resource | Port |
|---|---|
| gateway (YARP) | **5300** |
| core | **5301** |
| operations | **5302** |
| commerce | **5303** |

Pinned in `backend/laundryghar/laundryghar.AppHost/AppHost.cs`, mirrored in
`laundryghar.Gateway/appsettings.json` and `customer-mobile/src/constants/config.ts`
(`GATEWAY_PORT`). Change one, change all four.

### Why these moved off 5050 / 5002 / 5005 / 8080

They sat in a range other projects on a dev machine actively use. Measured on this machine, the
`quaeris*` containers publish **5003, 5004, 5007, 5090, 5433, 8080, 9197, 32800** — so the old
AppHost ports were living in the gaps between somebody else's services.

**8080 was not a near miss, it was a live collision.** `quaerisdocumentsearch-docsearch-1` publishes
`0.0.0.0:8080->80/tcp`, and a request to `localhost:8080` is answered by *uvicorn*:

```
$ curl -i http://localhost:8080/          # AppHost not even running
HTTP/1.1 404 Not Found
server: uvicorn
{"detail":"Not Found"}
```

That is the cause of the note in `customer-mobile/scripts/run-device.sh` — *"the AppHost gateway
(:8080) is not reliable for local dev and returns 502s"*. The gateway was never being reached at
all; another project's container was answering for it. The workaround (bypass the gateway, talk to
the standalone hosts, `adb reverse` each port) has been in the repo ever since.

53xx was chosen because the whole range is unclaimed on this machine, so the block cannot drift into
a neighbour's ports the way 500x did.

## Production / Docker — unchanged

`deploy/docker-compose.yml` keeps **8080** as the container-internal port for core, operations,
commerce and the gateway, published as `${GATEWAY_PORT:-8080}`. Inside a container network 8080
collides with nothing and is the conventional choice. **Only the host binding was ever the problem.**

## Ports this repo must never kill

`run-stack.sh` `kill -9`s whatever holds the ports it lists, so that list is load-bearing.

- **5000** — macOS **ControlCenter** (AirPlay Receiver). It was in the kill list and nothing this
  repo has ever bound it; every start SIGKILLed a system service for no reason. Removed.
- **8081** — another project's Metro. Already documented in the script; Metro uses 9091 here.

`free_ports` now also checks the process name and refuses to kill anything that is not a
dotnet/node/expo process of ours, printing a warning instead.
