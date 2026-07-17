# ops/tunnels — public URLs for the dev stack + VPS-hosted stack (trywavio.in)

Self-hosted ngrok equivalent, using the existing VPS (187.127.154.205) instead
of a third-party tunnel service. Two independent things live here:

1. **Dev tunnels** — expose your laptop's `run-stack.sh` services
   (core/operations/commerce/admin-web/Metro) at `https://dev-*.trywavio.in`,
   via [frp](https://github.com/fatedier/frp): `frpc` on your laptop dials out
   to `frps` on the VPS, no inbound port-forwarding needed on your side.
2. **VPS-hosted stack** — nginx vhosts + certs for the `deploy/docker-compose.yml`
   stack, once it's actually deployed on this VPS (`api.trywavio.in`,
   `admin.trywavio.in`). This is the "reverse proxy / LB" `deploy/README.md`
   calls out as still missing.
3. **Other local projects** — same frp tunnel, ngrok-style bare-prefix
   subdomains for unrelated local services: `laundryghar.trywavio.in`→:5080,
   `docsolt.trywavio.in`→:5069, `snapaccount.trywavio.in`→:5070.

```
laptop (run-stack.sh)          VPS (187.127.154.205)
  core :5056  ──┐                ┌─ frps :7000 (control, public)
  ops  :5015  ──┼── frpc ───────►│  vhostHTTPPort :7100 (loopback only)
  commerce:5242 │                │        │
  admin-web:5174│                │        ▼
  metro    :8081┘                │  nginx :80/:443 ── dev-*.trywavio.in
  (other projects)               │                     + laundryghar/docsolt/
  :5080 / :5069 / :5070 ─────────┘                     snapaccount .trywavio.in
                                  │
                                  │  docker-compose: gateway:8080, admin-web:8081
                                  └─ nginx :80/:443 ── api. / admin. .trywavio.in
```

DNS (already added, A records → 187.127.154.205, TTL 300):
`dev-core`, `dev-ops`, `dev-commerce`, `dev-admin`, `dev-metro`, `api`, `admin`,
`laundryghar`, `docsolt`, `snapaccount` — all `*.trywavio.in`. Root (`@`) and
`www` were left untouched.

## VPS setup (run these yourself over SSH — see note below on access)

```bash
# 1. Install frp (check github.com/fatedier/frp/releases for the current version/arch)
FRP_VERSION=0.61.1
curl -LO https://github.com/fatedier/frp/releases/download/v${FRP_VERSION}/frp_${FRP_VERSION}_linux_amd64.tar.gz
tar xzf frp_${FRP_VERSION}_linux_amd64.tar.gz
sudo install -m 755 frp_${FRP_VERSION}_linux_amd64/frps /usr/local/bin/frps
sudo mkdir -p /etc/frp

# 2. Config — copy frps.toml from this repo, generate a real token, deploy it
scp ops/tunnels/frps.toml vps:/tmp/frps.local.toml
ssh vps 'sudo mv /tmp/frps.local.toml /etc/frp/frps.local.toml'
# on the VPS: replace CHANGE_ME_LONG_RANDOM_TOKEN with `openssl rand -hex 32`
sudo useradd --system --no-create-home frp || true

# 3. systemd service
scp ops/tunnels/frps.service vps:/tmp/frps.service
ssh vps 'sudo mv /tmp/frps.service /etc/systemd/system/frps.service && \
          sudo systemctl daemon-reload && \
          sudo systemctl enable --now frps && \
          sudo systemctl status frps --no-pager'

# 4. Firewall — open the control port, do NOT open vhostHTTPPort (7100, loopback-only)
sudo ufw allow 7000/tcp
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# 5. nginx + vhosts
sudo apt install -y nginx certbot python3-certbot-nginx
scp ops/tunnels/nginx-trywavio.conf vps:/tmp/trywavio.conf
ssh vps 'sudo mv /tmp/trywavio.conf /etc/nginx/sites-available/trywavio.conf && \
          sudo ln -sf /etc/nginx/sites-available/trywavio.conf /etc/nginx/sites-enabled/ && \
          sudo nginx -t && sudo systemctl reload nginx'

# 6. TLS certs (one cert covering every subdomain in the nginx vhost; skip
#    api/admin until that stack is actually deployed on this box if you'd
#    rather wait — they already resolve via DNS so certbot will issue fine
#    either way, just drop -d api.trywavio.in -d admin.trywavio.in if not)
sudo certbot --nginx \
  -d dev-core.trywavio.in -d dev-ops.trywavio.in -d dev-commerce.trywavio.in \
  -d dev-admin.trywavio.in -d dev-metro.trywavio.in \
  -d laundryghar.trywavio.in -d docsolt.trywavio.in -d snapaccount.trywavio.in \
  -d api.trywavio.in -d admin.trywavio.in
```

## Laptop setup

```bash
brew install frp
cp ops/tunnels/frpc.toml ops/tunnels/frpc.local.toml   # gitignored (*.local)
# edit frpc.local.toml: set the same token as frps.local.toml on the VPS

bash scripts/run-tunnels.sh          # start — alongside scripts/run-stack.sh
bash scripts/run-tunnels.sh stop     # stop
```

## VPS access

You chose to run the server-side commands yourself rather than sharing SSH
access. All commands above are copy-paste ready; `vps` in the `scp`/`ssh`
examples is whatever your local SSH alias/host for 187.127.154.205 is (add
one to `~/.ssh/config` if you don't have it, or replace `vps` with
`user@187.127.154.205` directly).

## Notes

- **Never commit real tokens** — `frps.toml`/`frpc.toml` here are templates
  with a placeholder; the deployed copies (`frps.local.toml` on the VPS,
  `frpc.local.toml` on your laptop) hold the real secret and match the
  project's `*.local` gitignore convention (same pattern as `Keys/` in
  `run-stack.sh`).
- `vhostHTTPPort` (7100) must stay off the public firewall — it's only
  reachable via nginx's loopback proxy_pass, which is what keeps the dev
  tunnels behind real TLS certs instead of raw HTTP.
- If `run-stack.sh` ever changes its ports, update `ops/tunnels/frpc.toml`
  (and `frpc.local.toml`) to match — nothing here reads them dynamically.
- The `api.`/`admin.` nginx blocks assume `deploy/docker-compose.yml` is
  running directly on this VPS with the default `GATEWAY_PORT=8080` /
  `ADMIN_WEB_PORT=8081`. If that stack lives elsewhere, point those two
  `proxy_pass` targets at wherever it actually runs instead.
