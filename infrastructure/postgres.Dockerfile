# PostgreSQL is unchanged; update the supported distribution package and remove an unused privilege-switching tool.
FROM postgres:18.6-alpine3.24@sha256:898c60cb390d1085bc97bdd6a1ae968757421df394b071848b52b1c09c89d6a5
RUN apk add --no-cache --upgrade brotli-libs=1.2.1-r0 \
    && rm /usr/local/bin/gosu
# runtime-init provisions the data directory before this unprivileged entrypoint runs.
USER 70:70
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=12 CMD pg_isready --username="${POSTGRES_USER:-postgres}" --dbname="${POSTGRES_DB:-strangler}"
