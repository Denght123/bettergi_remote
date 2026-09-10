#!/usr/bin/env bash
set -Eeuo pipefail

release="${1:-0.3.1}"
app_root="/opt/bettergi-remote-lite"
incoming="/tmp/bgrl-deploy-${release}"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup="${app_root}/backups/${stamp}-${release}"

test -x "${incoming}/relay"
test -f "${incoming}/web/index.html"
test -f "${incoming}/deploy/bettergi-remote-lite.service"

mkdir -p "${backup}"
cp -a "${app_root}/relay" "${backup}/relay"
cp -a "${app_root}/web" "${backup}/web"
cp -a /etc/systemd/system/bettergi-remote-lite.service "${backup}/bettergi-remote-lite.service"

rollback() {
  echo "Deployment failed; restoring ${backup}" >&2
  systemctl stop bettergi-remote-lite.service || true
  install -o bettergi-remote-lite -g bettergi-remote-lite -m 0755 "${backup}/relay" "${app_root}/relay"
  rm -rf "${app_root}/web"
  cp -a "${backup}/web" "${app_root}/web"
  install -o root -g root -m 0644 "${backup}/bettergi-remote-lite.service" /etc/systemd/system/bettergi-remote-lite.service
  systemctl daemon-reload
  systemctl start bettergi-remote-lite.service || true
}
trap rollback ERR

systemctl stop bettergi-remote-lite.service
install -o bettergi-remote-lite -g bettergi-remote-lite -m 0755 "${incoming}/relay" "${app_root}/relay"
rm -rf "${app_root}/web.next"
cp -a "${incoming}/web" "${app_root}/web.next"
chown -R bettergi-remote-lite:bettergi-remote-lite "${app_root}/web.next"
rm -rf "${app_root}/web"
mv "${app_root}/web.next" "${app_root}/web"
install -o root -g root -m 0644 "${incoming}/deploy/bettergi-remote-lite.service" /etc/systemd/system/bettergi-remote-lite.service

systemctl daemon-reload
nginx -t
systemctl start bettergi-remote-lite.service

for _ in $(seq 1 20); do
  if curl -fsS --max-time 2 http://127.0.0.1:8080/healthz >/dev/null; then
    break
  fi
  sleep 0.25
done

curl -fsS --max-time 5 http://127.0.0.1:8080/healthz
systemctl is-active --quiet bettergi-remote-lite.service
ss -lnt | grep -q '127.0.0.1:8080'

trap - ERR
rm -rf "${incoming}"
echo
echo "DEPLOYED_RELEASE=${release}"
echo "BACKUP_DIRECTORY=${backup}"
