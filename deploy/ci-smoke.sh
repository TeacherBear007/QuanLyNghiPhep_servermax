#!/usr/bin/env bash
set -Eeuo pipefail
export COMPOSE_PROJECT_NAME=qlhvpromax-ci
scratch=$(mktemp -d)
nginx_container=qlhvpromax-ci-nginx

cleanup() {
    if [[ $1 != 0 ]]; then
        docker compose logs --tail 50 >&2 || true
        docker logs "$nginx_container" >&2 || true
    fi
    docker rm -f "$nginx_container" >/dev/null 2>&1 || true
    docker compose down --volumes >/dev/null 2>&1 || true
    rm -rf -- "$scratch"
}
trap 'cleanup "$?"' EXIT

docker compose config -q
docker compose up -d --build --wait --wait-timeout 90
[[ $(curl -fsS --max-time 5 http://127.0.0.1:10999/healthz) == ok ]]
# Kiểm tra có tài khoản quản trị Production và mật khẩu không phải mật khẩu demo.
docker compose exec -T qlhvpromax sh -c 'test -s /app/AppData/admin-ban-dau.txt && ! grep -q "Demo@123" /app/AppData/admin-ban-dau.txt'

# Chứng chỉ thử dùng riêng trong CI. Certbot cấp chứng chỉ thật trên server.
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj '/CN=localhost' \
    -addext 'subjectAltName=DNS:localhost' -keyout "$scratch/privkey.pem" -out "$scratch/fullchain.pem" 2>/dev/null
mkdir "$scratch/include"
cat > "$scratch/include/tls.conf" <<'TLS'
listen 18443 ssl;
listen [::]:18443 ssl;
ssl_certificate /ci/fullchain.pem;
ssl_certificate_key /ci/privkey.pem;
TLS
sed -e 's/__QLHV_DOMAIN__/localhost/g' -e 's/:80;/:18080;/g' -e 's/listen 80;/listen 18080;/' \
    qlhvpromax > "$scratch/default.conf"

docker run -d --name "$nginx_container" --network host \
    -v "$scratch:/ci:ro" -v "$scratch/default.conf:/etc/nginx/conf.d/default.conf:ro" \
    -v "$scratch/include:/etc/nginx/qlhvpromax:ro" nginx:stable >/dev/null
docker exec "$nginx_container" nginx -t

[[ $(curl -sS --retry 5 --retry-connrefused --max-time 5 -o /dev/null -w '%{http_code}' http://localhost:18080/) == 308 ]]
[[ $(curl -fsS --cacert "$scratch/fullchain.pem" --max-time 5 https://localhost:18443/healthz) == ok ]]
curl -fsS --cacert "$scratch/fullchain.pem" --max-time 5 https://localhost:18443/ -o "$scratch/login.html"
grep -q 'id="login-form"' "$scratch/login.html"
NODE_EXTRA_CA_CERTS="$scratch/fullchain.pem" node --input-type=module <<'JS'
const endpoint = 'https://localhost:18443/_blazor';
const response = await fetch(endpoint + '/negotiate?negotiateVersion=1', {
    method: 'POST',
    signal: AbortSignal.timeout(5000)
});
if (!response.ok) throw new Error('Blazor negotiate failed: ' + response.status);
const { connectionToken } = await response.json();
if (!connectionToken) throw new Error('SignalR did not issue a connection token');
const socket = new WebSocket(endpoint.replace('https:', 'wss:') + '?id=' + encodeURIComponent(connectionToken));
const timeout = setTimeout(() => {
    socket.close();
    throw new Error('Timed out waiting for WebSocket Upgrade');
}, 5000);
await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', () => reject(new Error('WebSocket Upgrade failed')), { once: true });
});
clearTimeout(timeout);
socket.close();
console.log('HTTPS and Blazor WebSocket Upgrade passed');
JS
