#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$root"
for file in scripts/*.sh infrastructure/*.sh; do sh -n "$file"; done
umask 077
temporary=$(mktemp -d "${TMPDIR:-/tmp}/strangler-cli-test.XXXXXX")
trap 'rm -rf "$temporary"' 0
mkdir -p "$temporary/project/scripts" "$temporary/bin"
cp scripts/lab.sh "$temporary/project/scripts/lab.sh"
for tool in docker openssl curl jq git; do
    printf '#!/bin/sh\nprintf "unexpected tool invocation\\n" >&2\nexit 97\n' > "$temporary/bin/$tool"
    chmod +x "$temporary/bin/$tool"
done
(
    cd "$temporary/project"
    PATH="$temporary/bin:$PATH" make --file "$root/Makefile" help >/dev/null
    test ! -e .secrets && test ! -e .local
    if ./scripts/lab.sh unknown >/dev/null 2>&1; then exit 1; fi
    ./scripts/lab.sh setup >/dev/null
    cp .secrets/alex_password "$temporary/original"
    ./scripts/lab.sh setup >/dev/null
    cmp "$temporary/original" .secrets/alex_password
    for file in .secrets/*; do
        test -f "$file" && test -s "$file" && test ! -L "$file"
        permissions=$(ls -l "$file")
        case "$permissions" in -rw-------*) ;; *) printf 'Secret permissions failed.\n' >&2; exit 1 ;; esac
        case "$file" in
            *_password)
                test "$(wc -l < "$file" | tr -d ' ')" -eq 1
                LC_ALL=C grep -Eq '[a-z]' "$file"
                LC_ALL=C grep -Eq '[A-Z]' "$file"
                LC_ALL=C grep -Eq '[0-9]' "$file"
                LC_ALL=C grep -Eq '[^a-zA-Z0-9]' "$file"
                ;;
        esac
    done
    rm .secrets/sam_password
    ln -s "$temporary/original" .secrets/sam_password
    if ./scripts/lab.sh setup >/dev/null 2>&1; then printf 'Symlink rejection failed.\n' >&2; exit 1; fi
)
printf '%s\n' '{"SchemaVersion":2,"Results":[{"Vulnerabilities":[{"Severity":"LOW"}]}]}' > "$temporary/scan.json"
./scripts/assess-scan.sh "$temporary/scan.json" >/dev/null
printf '%s\n' '{"SchemaVersion":2,"Results":[{"Vulnerabilities":[{"Severity":"HIGH"}]}]}' > "$temporary/scan.json"
if ./scripts/assess-scan.sh "$temporary/scan.json" >/dev/null; then printf 'Vulnerability gate failed.\n' >&2; exit 1; fi
printf '%s\n' '{"SchemaVersion":2,"Results":[{"Secrets":[{"Severity":"LOW"}]}]}' > "$temporary/scan.json"
if ./scripts/assess-scan.sh "$temporary/scan.json" >/dev/null; then printf 'Secret gate failed.\n' >&2; exit 1; fi
printf '%s\n' '{}' > "$temporary/scan.json"
if ./scripts/assess-scan.sh "$temporary/scan.json" >/dev/null; then printf 'Malformed report gate failed.\n' >&2; exit 1; fi
printf '#!/bin/sh\nprintf ".secrets/private\\n"\n' > "$temporary/bin/git"
if PATH="$temporary/bin:$PATH" ./scripts/check-private-paths.sh >/dev/null 2>&1; then printf 'Private artifact gate failed.\n' >&2; exit 1; fi
printf '#!/bin/sh\nexit 0\n' > "$temporary/bin/git"
PATH="$temporary/bin:$PATH" ./scripts/check-private-paths.sh
printf 'INF 2 commits scanned.\nINF no leaks found.\n' > "$temporary/gitleaks.log"
./scripts/check-gitleaks-log.sh history "$temporary/gitleaks.log"
printf 'ERR fatal: dubious ownership\nINF 0 commits scanned.\nINF no leaks found.\n' > "$temporary/gitleaks.log"
if ./scripts/check-gitleaks-log.sh history "$temporary/gitleaks.log" >/dev/null 2>&1; then printf 'False-clean secret scan gate failed.\n' >&2; exit 1; fi
printf 'INF no leaks found.\n' > "$temporary/gitleaks.log"
if ./scripts/check-gitleaks-log.sh history "$temporary/gitleaks.log" >/dev/null 2>&1; then printf 'Missing history evidence gate failed.\n' >&2; exit 1; fi
rm "$temporary/bin/openssl" "$temporary/bin/jq" "$temporary/bin/git"
cat > "$temporary/bin/curl" <<'MOCK_CURL'
#!/bin/sh
for endpoint do :; done
case "$endpoint" in
    */ingestion-tokens)
        case " $* " in
            *' --data-binary '*)
                mock_token=$(printf 'o2oi_%032d' 0)
                printf '{"data":{"token":"%s","enabled":true}}\n' "$mock_token"
                printf 'created\n' >> "$LAB_TEST_TRACE"
                ;;
            *) printf '%s\n' '{"data":[]}' ;;
        esac
        ;;
    *) printf '{}\n' ;;
esac
MOCK_CURL
printf '#!/bin/sh\nexit 0\n' > "$temporary/bin/docker"
(
    cd "$temporary/project"
    rm .secrets/sam_password
    LAB_TEST_TRACE="$temporary/trace" PATH="$temporary/bin:$PATH" ./scripts/lab.sh up >/dev/null
    LC_ALL=C grep -Eq '^o2oi_[[:alnum:]]{32}$' .secrets/openobserve_ingestion_token
    test "$(wc -l < "$temporary/trace" | tr -d ' ')" -eq 1
    cat > "$temporary/bin/curl" <<'CACHED_CURL'
#!/bin/sh
for endpoint do :; done
case "$endpoint" in */ingestion-tokens) exit 97 ;; *) printf '{}\n' ;; esac
CACHED_CURL
    PATH="$temporary/bin:$PATH" ./scripts/lab.sh up >/dev/null
    printf 'invalid\n' > .secrets/openobserve_ingestion_token
    if PATH="$temporary/bin:$PATH" ./scripts/lab.sh up >/dev/null 2>&1; then printf 'Telemetry token validation failed.\n' >&2; exit 1; fi
)
printf 'Shell syntax, inert help, preserved credentials, permissions and symlink rejection passed.\n'
