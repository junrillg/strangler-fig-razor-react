#!/bin/sh
set -eu

help() {
    printf '%s\n' \
        'Usage: make [target]' \
        '' \
        'help          List commands without invoking tools or loading configuration.' \
        'setup         Create ignored, owner-only local credentials; preserve existing values.' \
        'telemetry-setup  Start the log viewer and provision its isolated ingestion-only credential.' \
        'up            Build and start the application and viewers; wait for readiness.' \
        'down          Stop this lab; preserve every named volume.' \
        'restart       Restart this lab and wait; preserve data and authentication keys.' \
        'logs          Follow bounded container logs (Ctrl-C stops following).' \
        'sample        Sign in as synthetic alex, create and submit a requisition.' \
        'build         Build the production application images.' \
        'test          Run backend tests against an isolated PostgreSQL project.' \
        'check         Run script, format, analyzer, tests, build and security gates.' \
        'format-check  Verify .NET formatting using the pinned container SDK.' \
        'source-check  Restore locked dependencies and enforce release-build diagnostics.' \
        'security      Scan source/configuration, dependencies, Git history and images.' \
        'script-test   Run shell syntax and isolated CLI behavior checks.' \
        'mount-check   Verify real per-service secret mount boundaries without reading values.' \
        '' \
        'Prerequisites: POSIX sh, Make, Docker with Compose v2, openssl, curl, jq, git.' \
        'Variables: COMPOSE_PROJECT_NAME=strangler-fig; APP_PORT=5190;' \
        '           DB_VIEWER_PORT=5191; LOGS_PORT=5192. Ports bind only to 127.0.0.1.' \
        'No command deletes volumes. Credentials are files under .secrets; values are never printed.'
}

case "${1:-help}" in
    help|--help|-h) help; exit 0 ;;
    setup|telemetry-setup|up|down|restart|logs|sample|build|test|check|format-check|source-check|security|script-test|mount-check) action=$1 ;;
    *) printf 'Unknown target. Run make help.\n' >&2; exit 2 ;;
esac
test "$#" -eq 1 || { printf 'Expected one target.\n' >&2; exit 2; }

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$root"
COMPOSE_PROJECT_NAME=${COMPOSE_PROJECT_NAME:-strangler-fig}
case "$COMPOSE_PROJECT_NAME" in
    ''|[!a-z0-9]*|*[!a-z0-9_-]*) printf 'Invalid Compose project name.\n' >&2; exit 2 ;;
esac
export COMPOSE_PROJECT_NAME

compose() { docker compose "$@"; }
sdk() { compose build sdk; compose run --rm --no-deps sdk "$@"; }

setup() {
    umask 077
    test ! -L .secrets
    test ! -L .local
    mkdir -p .secrets .local/nuget
    chmod 700 .secrets .local .local/nuget
    for name in postgres_password monolith_db_password pgviewer_password alex_password sam_password openobserve_password; do
        file=".secrets/$name"
        test ! -L "$file"
        if test ! -e "$file"; then
            temporary=$(mktemp .secrets/.new.XXXXXX)
            if ! random_password=$(openssl rand -hex 32); then rm -f "$temporary"; return 1; fi
            printf '%sAa1!\n' "$random_password" > "$temporary"
            if ! ln "$temporary" "$file" 2>/dev/null; then test -f "$file" || { rm -f "$temporary"; return 1; }; fi
            rm -f "$temporary"
        fi
        test -f "$file"
        test -s "$file"
        chmod 600 "$file"
    done
    for name in openobserve.env; do
        test ! -L ".secrets/$name"
        temporary=$(mktemp .secrets/.new.XXXXXX)
        printf 'ZO_ROOT_USER_EMAIL=logs@lab.example\nZO_ROOT_USER_PASSWORD=%s\n' "$(cat .secrets/openobserve_password)" > "$temporary"
        if test -e ".secrets/$name"; then
            if ! cmp -s "$temporary" ".secrets/$name"; then
                rm -f "$temporary"
                printf 'Derived credential file differs: %s. Reconcile it without changing active passwords.\n' "$name" >&2
                return 1
            fi
        else
            if ! ln "$temporary" ".secrets/$name" 2>/dev/null; then rm -f "$temporary"; return 1; fi
        fi
        rm -f "$temporary"
        chmod 600 ".secrets/$name"
    done
    printf 'Local credential files are ready; existing values were preserved.\n'
}

wait_http() {
    url=$1
    attempt=0
    until curl --fail --silent --max-time 3 "$url" >/dev/null; do
        attempt=$((attempt + 1))
        if test "$attempt" -ge 30; then printf 'Readiness failed: %s\n' "$url" >&2; return 1; fi
        sleep 1
    done
}

ready() {
    wait_http "http://127.0.0.1:${APP_PORT:-5190}/health/ready"
    wait_http "http://127.0.0.1:${DB_VIEWER_PORT:-5191}/"
    wait_http "http://127.0.0.1:${LOGS_PORT:-5192}/healthz"
    printf 'Application: http://app.localhost:%s\nAdminer: http://db.localhost:%s\nLogs: http://logs.localhost:%s\n' "${APP_PORT:-5190}" "${DB_VIEWER_PORT:-5191}" "${LOGS_PORT:-5192}"
}

test_backend() (
    setup
    COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME}-tests"
    export COMPOSE_PROJECT_NAME
    trap 'result=$?; trap - 0; if ! compose down --timeout 20; then test "$result" -ne 0 || result=1; fi; exit "$result"' 0
    compose up --detach --wait --wait-timeout 120 postgres
    compose build tests
    compose run --rm --no-deps tests test --project tests/Backend.Tests/Backend.Tests.csproj --configuration Release
)

sample() (
    setup
    umask 077
    temporary=$(mktemp -d "${TMPDIR:-/tmp}/strangler-sample.XXXXXX")
    trap 'rm -rf "$temporary"' 0
    base="http://127.0.0.1:${APP_PORT:-5190}"
    csrf() {
        curl --fail --silent --show-error --max-time 10 --cookie "$temporary/cookies" --cookie-jar "$temporary/cookies" "$base/api/antiforgery" > "$temporary/token.json"
        token=$(jq -er '.token | select(type == "string" and length > 0)' "$temporary/token.json")
        printf 'X-CSRF-TOKEN: %s\n' "$token" > "$temporary/headers"
    }
    csrf
    jq -n --rawfile password .secrets/alex_password '{username:"alex",password:($password|rtrimstr("\n"))}' > "$temporary/request.json"
    curl --fail --silent --show-error --max-time 10 --cookie "$temporary/cookies" --cookie-jar "$temporary/cookies" --header "@$temporary/headers" --header 'Content-Type: application/json' --data-binary "@$temporary/request.json" "$base/api/session" > /dev/null
    csrf
    random=$(openssl rand -hex 16)
    request_id=$(printf '%s' "$random" | sed 's/^\(........\)\(....\)\(....\)\(....\)\(............\)$/\1-\2-\3-\4-\5/')
    jq -n --arg requestId "$request_id" '{requestId:$requestId,title:"Synthetic platform role",reason:"Synthetic replacement request"}' > "$temporary/request.json"
    curl --fail --silent --show-error --max-time 10 --cookie "$temporary/cookies" --header "@$temporary/headers" --header 'Content-Type: application/json' --data-binary "@$temporary/request.json" "$base/api/requisitions" > "$temporary/created.json"
    id=$(jq -er '.id' "$temporary/created.json")
    version=$(jq -er '.version' "$temporary/created.json")
    jq -n --argjson version "$version" '{version:$version}' > "$temporary/request.json"
    curl --fail --silent --show-error --max-time 10 --cookie "$temporary/cookies" --header "@$temporary/headers" --header 'Content-Type: application/json' --data-binary "@$temporary/request.json" "$base/api/requisitions/$id/submit" > "$temporary/submitted.json"
    jq -e '.status == "submitted" and (.id | type) == "string"' "$temporary/submitted.json" >/dev/null
    jq '{id,status,version}' "$temporary/submitted.json"
)

provision_telemetry() (
    token_file=.secrets/openobserve_ingestion_token
    test ! -L "$token_file"
    if test -f "$token_file"; then
        LC_ALL=C grep -Eq '^o2oi_[[:alnum:]]{32}$' "$token_file"
        return
    fi
    umask 077
    temporary=$(mktemp -d "${TMPDIR:-/tmp}/strangler-telemetry.XXXXXX")
    trap 'rm -rf "$temporary"' 0
    base="http://127.0.0.1:${LOGS_PORT:-5192}"
    wait_http "$base/healthz"
    encoded=$(printf 'logs@lab.example:%s' "$(cat .secrets/openobserve_password)" | openssl base64 -A)
    printf 'Authorization: Basic %s\n' "$encoded" > "$temporary/header"
    curl --fail --silent --show-error --max-time 10 --header "@$temporary/header" "$base/api/default/ingestion-tokens" > "$temporary/list.json"
    count=$(jq -er '[.data[] | select(.name == "lab-runtime")] | length' "$temporary/list.json")
    case "$count" in
        0)
            printf '%s\n' '{"name":"lab-runtime","description":"Local application telemetry"}' > "$temporary/request.json"
            curl --fail --silent --show-error --max-time 10 --header "@$temporary/header" --header 'Content-Type: application/json' --data-binary "@$temporary/request.json" "$base/api/default/ingestion-tokens" > "$temporary/token.json"
            jq -er '.data | select(.enabled != false) | .token' "$temporary/token.json" > "$temporary/token"
            ;;
        1) jq -er '.data[] | select(.name == "lab-runtime" and .enabled != false) | .token' "$temporary/list.json" > "$temporary/token" ;;
        *) printf 'Ambiguous telemetry credential; reconcile it before startup.\n' >&2; return 1 ;;
    esac
    LC_ALL=C grep -Eq '^o2oi_[[:alnum:]]{32}$' "$temporary/token"
    staged=$(mktemp .secrets/.new.XXXXXX)
    cp "$temporary/token" "$staged"
    chmod 600 "$staged"
    if ! ln "$staged" "$token_file" 2>/dev/null; then
        if ! cmp -s "$staged" "$token_file"; then rm -f "$staged"; return 1; fi
    fi
    rm -f "$staged"
    printf 'Ingestion-only telemetry credential is ready.\n'
)

security() (
    ./scripts/check-private-paths.sh
    setup
    umask 077
    private_dir="${XDG_CACHE_HOME:-$HOME/.cache}/strangler-fig"
    cache_dir="$private_dir/trivy"
    mkdir -p "$cache_dir"
    chmod 700 "$private_dir" "$cache_dir"
    report_dir=$(mktemp -d "$private_dir/reports.XXXXXX")
    trap 'if test -n "${archive:-}"; then rm -f "$archive"; fi' 0
    trivy_image=aquasec/trivy:0.76.1@sha256:790fd66d1697395d8693d1acaa1ea702406a20f109b6fc7bac8db623eca6c06c
    leaks_image=ghcr.io/gitleaks/gitleaks:v8.30.1@sha256:c00b6bd0aeb3071cbcb79009cb16a60dd9e0a7c60e2be9ab65d25e6bc8abbb7f
    scanner_user="$(id -u):$(id -g)"
    for scan in tree history; do
        if test "$scan" = tree; then
            set -- dir /source --config /source/infrastructure/gitleaks.toml
        else
            set -- git /source --log-opts=--all
        fi
        result=0
        docker run --rm --user "$scanner_user" --cap-drop ALL --security-opt no-new-privileges -e HOME=/tmp \
            -e GIT_CONFIG_COUNT=1 -e GIT_CONFIG_KEY_0=safe.directory -e GIT_CONFIG_VALUE_0=/source \
            -v "$root:/source:ro" --tmpfs /source/.secrets:ro,mode=0555,noexec,nosuid,size=1m \
            "$leaks_image" "$@" --redact --no-banner --no-color > "$report_dir/gitleaks-$scan.log" 2>&1 || result=$?
        cat "$report_dir/gitleaks-$scan.log"
        test "$result" -eq 0
        ./scripts/check-gitleaks-log.sh "$scan" "$report_dir/gitleaks-$scan.log"
    done
    failed=0
    docker run --rm --user "$scanner_user" --memory 1g --cpus 1 --cap-drop ALL --security-opt no-new-privileges -e HOME=/tmp -v "$root:/source:ro" --tmpfs /source/.secrets:ro,mode=0555,noexec,nosuid,size=1m -v "$cache_dir:/cache" -v "$report_dir:/reports" "$trivy_image" filesystem --cache-dir /cache --parallel 1 --include-dev-deps --scanners vuln,misconfig,secret --format json --output /reports/source.json --skip-dirs /source/.git,/source/.secrets,/source/.local /source
    if ! ./scripts/assess-scan.sh "$report_dir/source.json"; then failed=1; fi
    compose --profile tools config --images > "$report_dir/images.txt"
    images=$(sort -u "$report_dir/images.txt")
    for image in $images; do
        if ! docker image inspect "$image" >/dev/null 2>&1; then
            case "$image" in
                *:local) printf 'Build the application images before scanning.\n' >&2; return 1 ;;
                *) docker pull "$image" ;;
            esac
        fi
        filename=$(printf '%s' "$image" | tr '/:@' '___')
        archive="$report_dir/$filename.tar"
        docker image save --output "$archive" "$image"
        test -s "$archive"
        docker run --rm --user "$scanner_user" --memory 1g --cpus 1 --cap-drop ALL --security-opt no-new-privileges -e HOME=/tmp -v "$report_dir:/reports" -v "$cache_dir:/cache" "$trivy_image" image --cache-dir /cache --parallel 1 --input "/reports/$filename.tar" --format json --output "/reports/$filename.json"
        rm "$archive"
        archive=
        if ! ./scripts/assess-scan.sh "$report_dir/$filename.json"; then failed=1; fi
    done
    printf 'Private full-severity reports: %s\nLower-severity findings still require source review.\n' "$report_dir"
    test "$failed" -eq 0
)

case "$action" in
    setup) setup ;;
    telemetry-setup)
        setup
        compose up --detach --wait --wait-timeout 120 openobserve
        provision_telemetry
        compose run --rm --no-deps runtime-init
        ;;
    up) "$0" telemetry-setup; compose up --build --detach --wait --wait-timeout 180; ready ;;
    down) compose down --timeout 20 ;;
    restart) setup; compose stop --timeout 20; compose up --detach --wait --wait-timeout 180; ready ;;
    logs) compose logs --follow --tail 100 ;;
    sample) sample ;;
    build) setup; compose build ;;
    test) test_backend ;;
    format-check) setup; sdk format --verify-no-changes --no-restore ;;
    source-check) setup; sdk restore --locked-mode -p:TreatWarningsAsErrors=true -p:NuGetAudit=true -p:NuGetAuditMode=all; sdk build --configuration Release --no-restore --warnaserror ;;
    security) security ;;
    script-test) ./scripts/test-cli.sh ;;
    mount-check) setup; compose run --rm --no-deps runtime-init; ./scripts/test-secret-mounts.sh ;;
    check)
        ./scripts/test-cli.sh
        setup
        compose config --quiet
        "$0" source-check
        "$0" format-check
        "$0" test
        "$0" mount-check
        "$0" build
        "$0" security
        ;;
esac
