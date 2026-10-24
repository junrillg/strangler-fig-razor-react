#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$root"
configuration=$(docker compose --profile tools config --format json)
volume=$(printf '%s' "$configuration" | jq -er '.volumes["runtime-secrets"].name')
image=strangler-fig-postgres:local
for service in gateway monolith postgres openobserve tests; do
    subpath=$(printf '%s' "$configuration" | jq -er --arg service "$service" '.services[$service].volumes[] | select(.target == "/run/secrets" and .read_only == true) | .volume.subpath')
    expected_subpath=$service
    if test "$service" = postgres; then expected_subpath=postgres-alpine; fi
    test "$subpath" = "$expected_subpath"
    case "$service" in
        gateway) uid=1654; expected=''; denied='postgres_password monolith_db_password alex_password sam_password pgadmin_password pgviewer_pgpass .env' ;;
        monolith) uid=1654; expected='monolith_db_password alex_password sam_password'; denied='postgres_password pgviewer_password pgadmin_password pgviewer_pgpass .env' ;;
        postgres) uid=70; expected='postgres_password monolith_db_password pgviewer_password'; denied='alex_password sam_password pgadmin_password pgviewer_pgpass openobserve_password .env' ;;
        openobserve) uid=1654; expected='.env'; denied='postgres_password monolith_db_password alex_password sam_password pgadmin_password pgviewer_pgpass openobserve_password' ;;
        tests) uid=1654; expected='monolith_db_password alex_password sam_password'; denied='postgres_password pgviewer_password pgadmin_password pgviewer_pgpass openobserve_password .env' ;;
    esac
    if test -f .secrets/openobserve_ingestion_token; then
        case "$service" in gateway|monolith) expected="$expected openobserve_password" ;; esac
    fi
    docker run --rm --network none --read-only --user "$uid:$uid" --cap-drop ALL --security-opt no-new-privileges \
        --mount "type=volume,source=$volume,target=/run/secrets,volume-subpath=$subpath,readonly" \
        --entrypoint /bin/sh "$image" -eu -c '
            for name in $1; do test -r "/run/secrets/$name"; done
            for name in $2; do test ! -e "/run/secrets/$name"; done
            for sibling in monolith gateway postgres openobserve tests; do test ! -e "/run/secrets/../$sibling"; done
        ' check "$expected" "$denied"
    printf '%s: isolated mount access passed.\n' "$service"
done
if printf '%s' "$configuration" | jq -e '.services.sdk.volumes[] | select(.target == "/run/secrets")' >/dev/null; then
    printf 'Build tooling must not mount runtime secrets.\n' >&2
    exit 1
fi
docker compose run --rm --no-deps --entrypoint /bin/sh sdk -eu -c 'test ! -e /run/secrets; test ! -e /workspace/.secrets/postgres_password; test -r /workspace/Strangler.slnx'
printf 'Build tooling: private source-tree credentials are masked.\n'
if printf '%s' "$configuration" | jq -e '.services["db-viewer"].volumes[]? | select(.target == "/run/secrets")' >/dev/null; then
    printf 'The database viewer must use native entered authentication, without a credential mount.\n' >&2
    exit 1
fi
printf 'Database viewer: no server-side secret mount.\n'
