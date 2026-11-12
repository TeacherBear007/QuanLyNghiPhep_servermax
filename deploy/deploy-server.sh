#!/usr/bin/env bash
set -Eeuo pipefail

phase=${1:?Thiếu bước triển khai}
stage=${2:?Thiếu thư mục upload}
domain=${3:?Thiếu tên miền}
value=${4:-}
[[ $EUID -eq 0 && -d $stage && $stage =~ ^/tmp/qlhvpromax\.[A-Za-z0-9]+$ ]]
[[ ${#domain} -le 253 && $domain =~ ^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]([a-z0-9-]{0,61}[a-z0-9])?$ ]]

app_dir=/opt/qlhvpromax
nginx_source=$app_dir/nginx-source
site=/etc/nginx/sites-available/qlhvpromax
enabled=/etc/nginx/sites-enabled/qlhvpromax
tls=/etc/nginx/qlhvpromax/tls.conf
webroot=/var/www/qlhvpromax
cert_dir=/etc/letsencrypt/live/qlhvpromax-$domain

install -d -m 0755 "$app_dir"
# Ngăn hai tiến trình cùng ghi site hoặc đổi container trên một máy.
exec 9>"$app_dir/deploy.lock"
flock 9

render_nginx() {
    local source=$1 candidate=$2
    sed "s/__QLHV_DOMAIN__/$domain/g" "$source" > "$candidate/site"
    if [[ -s $cert_dir/fullchain.pem && -s $cert_dir/privkey.pem ]]; then
        cat > "$candidate/tls" <<TLS
listen 443 ssl;
listen [::]:443 ssl;
ssl_certificate $cert_dir/fullchain.pem;
ssl_certificate_key $cert_dir/privkey.pem;
ssl_protocols TLSv1.2 TLSv1.3;
TLS
    fi
}

apply_nginx() {
    local candidate=$1
    if [[ -e $enabled || -L $enabled ]]; then
        if [[ ! -L $enabled || $(readlink -f "$enabled") != "$site" ]]; then
            echo "Tên qlhvpromax trong sites-enabled đang được dùng bởi cấu hình khác." >&2
            return 1
        fi
    fi
    if [[ -L $enabled ]] && cmp -s "$candidate/site" "$site"; then
        if { [[ ! -e $candidate/tls && ! -e $tls ]]; } || cmp -s "$candidate/tls" "$tls"; then
            echo "Nginx giữ nguyên cấu hình hiện tại."
            nginx -t
            return
        fi
    fi

    local had_link=0
    [[ ! -L $enabled ]] || had_link=1
    [[ ! -e $site ]] || cp -p "$site" "$candidate/old-site"
    [[ ! -e $tls ]] || cp -p "$tls" "$candidate/old-tls"
    install -m 0644 "$candidate/site" "$site"
    if [[ -e $candidate/tls ]]; then
        install -m 0644 "$candidate/tls" "$tls"
    else
        rm -f -- "$tls"
    fi
    if (( had_link == 0 )); then ln -s "$site" "$enabled"; fi
    if nginx -t && systemctl reload nginx; then
        echo "Đã nạp cấu hình Nginx qlhvpromax."
        return
    fi

    echo "Nginx từ chối cấu hình mới. Khôi phục cấu hình trước đó." >&2
    if [[ -e $candidate/old-site ]]; then cp -p "$candidate/old-site" "$site"; else rm -f -- "$site"; fi
    if [[ -e $candidate/old-tls ]]; then cp -p "$candidate/old-tls" "$tls"; else rm -f -- "$tls"; fi
    if (( had_link == 0 )); then rm -f -- "$enabled"; fi
    nginx -t && systemctl reload nginx
    return 1
}

compose() {
    local release=$1
    shift
    docker compose -p qlhvpromax --env-file "$release/release.env" -f "$release/docker-compose.yml" "$@"
}

case $phase in
    setup-nginx)
        systemctl enable --now docker
        docker info >/dev/null
        docker compose version
        if ! command -v nginx >/dev/null || ! command -v certbot >/dev/null || ! command -v curl >/dev/null; then
            apt-get update
            DEBIAN_FRONTEND=noninteractive apt-get install -y nginx certbot curl
        fi
        install -d -m 0755 /etc/nginx/qlhvpromax "$webroot/.well-known/acme-challenge"
        systemctl enable --now nginx
        source=$nginx_source
        [[ ! -f $stage/qlhvpromax ]] || source=$stage/qlhvpromax
        [[ -f $source ]]
        candidate=$(mktemp -d)
        trap 'rm -rf -- "$candidate"' EXIT
        render_nginx "$source" "$candidate"
        apply_nginx "$candidate"
        if [[ $source != "$nginx_source" ]]; then
            install -m 0644 "$source" "$nginx_source"
        fi
        ;;
    install-certificate)
        email=$value
        [[ $email =~ ^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$ ]]
        hook=/etc/letsencrypt/renewal-hooks/deploy/qlhvpromax
        install -d -m 0755 "$(dirname "$hook")"
        cat > "$hook" <<'HOOK'
#!/bin/sh
set -eu
if [ -n "${RENEWED_LINEAGE:-}" ] && grep -Fq -- "$RENEWED_LINEAGE/fullchain.pem" /etc/nginx/qlhvpromax/tls.conf 2>/dev/null; then
    nginx -t
    systemctl reload nginx
fi
HOOK
        chmod 0755 "$hook"
        certbot certonly --webroot -w "$webroot" --cert-name "qlhvpromax-$domain" \
            -d "$domain" --email="$email" --agree-tos --non-interactive --keep-until-expiring
        candidate=$(mktemp -d)
        trap 'rm -rf -- "$candidate"' EXIT
        render_nginx "$nginx_source" "$candidate"
        [[ -s $candidate/tls ]]
        apply_nginx "$candidate"
        systemctl enable --now certbot.timer
        ;;
    release)
        tag=$value
        [[ $tag =~ ^[0-9a-f]{40}$ ]]
        install -d -m 0755 "$app_dir/releases"
        release=$(mktemp -d "$app_dir/releases/$tag.XXXXXX")
        install -m 0644 "$stage/docker-compose.yml" "$release/docker-compose.yml"
        printf 'QLHV_DOMAIN=%s\nQLHV_IMAGE_TAG=%s\n' "$domain" "$tag" > "$release/release.env"
        # Tùy chỉnh subnet nếu server đã dùng dải mặc định cho một ứng dụng khác.
        if [[ -f $app_dir/network.env ]]; then cat "$app_dir/network.env" >> "$release/release.env"; fi
        previous=$(readlink -f "$app_dir/current" || true)

        gzip -dc "$stage/image.tar.gz" | docker image load
        docker image inspect "qlhvpromax:$tag" >/dev/null
        compose "$release" config -q
        if compose "$release" up -d --no-build --pull never --wait --wait-timeout 90 \
            && [[ $(curl -fsS --max-time 10 http://127.0.0.1:10999/healthz) == ok ]] \
            && curl -fsS --max-time 15 --resolve "$domain:443:127.0.0.1" "https://$domain/" -o /dev/null; then
            ln -sfn "$release" "$app_dir/current.next"
            mv -Tf "$app_dir/current.next" "$app_dir/current"
            compose "$release" ps
        else
            compose "$release" logs --tail 40 >&2 || true
            if [[ -f $previous/release.env ]]; then
                echo "Bản mới không đạt health check. Khởi động lại bản trước." >&2
                compose "$previous" up -d --no-build --pull never --wait --wait-timeout 90
            else
                compose "$release" stop
            fi
            exit 1
        fi
        ;;
    *)
        echo "Bước triển khai không hợp lệ: $phase" >&2
        exit 1
        ;;
esac
