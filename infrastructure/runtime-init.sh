#!/bin/sh
set -eu
umask 077

install -d -m 0711 /output
for service in monolith gateway openobserve tests; do install -d -o 1654 -g 1654 -m 0700 "/output/$service"; done
install -d -o 70 -g 70 -m 0700 /output/postgres-alpine /pgdata
install -d -o 1654 -g 1654 -m 0700 /keyring /logs-data

copy_secret() {
    service=$1 name=$2 owner=$3 mode=$4 target=${5:-$2}
    test -f "/input/$name"
    test -s "/input/$name"
    test ! -L "/input/$name"
    install -o "$owner" -g "$owner" -m "$mode" "/input/$name" "/output/$service/$target.new"
    mv "/output/$service/$target.new" "/output/$service/$target"
}

copy_secret postgres-alpine postgres_password 70 0400
copy_secret postgres-alpine monolith_db_password 70 0400
copy_secret postgres-alpine pgviewer_password 70 0400
copy_secret monolith monolith_db_password 1654 0400
copy_secret monolith alex_password 1654 0400
copy_secret monolith sam_password 1654 0400
copy_secret tests monolith_db_password 1654 0400
copy_secret tests alex_password 1654 0400
copy_secret tests sam_password 1654 0400
copy_secret openobserve openobserve.env 1654 0400 .env
if test -f /input/openobserve_ingestion_token; then
    copy_secret monolith openobserve_ingestion_token 1654 0400 openobserve_password
    copy_secret gateway openobserve_ingestion_token 1654 0400 openobserve_password
fi
printf 'Restricted runtime files are ready.\n'
