# Relay operations

The production relay is a single Go process behind Caddy. It keeps only live room sockets in memory. Restarting it disconnects clients, which reconnect automatically.

## VPS requirements

- One small Linux VPS in Hong Kong or Singapore.
- Docker Engine with the Compose plugin.
- A DNS A/AAAA record pointing a domain or subdomain to the VPS.
- TCP ports 80 and 443 open.

## Deploy

Copy the repository to the VPS, then create `deploy/relay/.env` from `.env.example`.

```bash
docker compose --env-file deploy/relay/.env -f deploy/relay/compose.yaml up -d --build
curl https://remote.example.com/healthz
```

The Caddy configuration intentionally disables access logs. Do not add query-string or WebSocket-frame logging. Pairing secrets live in URL fragments and application payloads are encrypted, but IP addresses, connection timing, room identifiers, and frame sizes remain observable metadata.

## Native deployment without Docker

The release also contains a standalone `relay-linux-amd64` binary. It can run under systemd with a host-installed Caddy. Templates are available in `deploy/native`, and the full procedure is documented in `docs/local-testing.md`.

The native service sets `UPDATE_ROOT=/opt/bettergi-remote-lite/downloads`. This directory contains `latest.json` and the matching versioned installer. The relay serves them through dedicated `/updates/latest.json` and `/downloads/<installer>` routes so missing update files never fall back to the PWA shell.

## Capacity defaults

- 64 KiB maximum application frame.
- 10 application messages per second per connection with a short burst allowance.
- One PC and one phone per channel.
- 200 concurrent sockets by default.
- No offline queue and no persistence.
