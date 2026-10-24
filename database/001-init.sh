#!/bin/sh
set -eu
# psql reads secret environment variables internally; no password enters its argv or output.
MONOLITH_PASSWORD=$(cat /run/secrets/monolith_db_password)
VIEWER_PASSWORD=$(cat /run/secrets/pgviewer_password)
[ "${#MONOLITH_PASSWORD}" -ge 24 ] && [ "${#VIEWER_PASSWORD}" -ge 24 ] || exit 1
export MONOLITH_PASSWORD VIEWER_PASSWORD
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --no-psqlrc --set ON_ERROR_STOP=1 <<'SQL'
\getenv monolith_password MONOLITH_PASSWORD
\getenv viewer_password VIEWER_PASSWORD
BEGIN;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE ROLE monolith_owner NOLOGIN;
CREATE ROLE monolith_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT PASSWORD :'monolith_password';
CREATE ROLE viewer_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT PASSWORD :'viewer_password';
CREATE SCHEMA monolith AUTHORIZATION monolith_owner;
SET ROLE monolith_owner;
CREATE TABLE monolith.requisitions (
    id uuid PRIMARY KEY,
    requester_id uuid NOT NULL,
    approver_id uuid NOT NULL CHECK (approver_id <> requester_id),
    request_id uuid NOT NULL,
    create_title text NOT NULL CHECK (char_length(create_title) BETWEEN 1 AND 120),
    create_reason text NOT NULL CHECK (char_length(create_reason) BETWEEN 1 AND 1000),
    title text NOT NULL CHECK (char_length(title) BETWEEN 1 AND 120),
    reason text NOT NULL CHECK (char_length(reason) BETWEEN 1 AND 1000),
    status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'submitted', 'approved', 'rejected')),
    version integer NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (requester_id, request_id)
);
CREATE INDEX requisitions_requester_id_id ON monolith.requisitions (requester_id, id);
CREATE INDEX requisitions_approver_id_id ON monolith.requisitions (approver_id, id);
CREATE TABLE monolith.requisition_audit (
    id uuid PRIMARY KEY,
    requisition_id uuid NOT NULL REFERENCES monolith.requisitions(id),
    actor_id uuid NOT NULL,
    from_status text CHECK (from_status IS NULL OR from_status IN ('draft', 'submitted')),
    to_status text NOT NULL CHECK (to_status IN ('draft', 'submitted', 'approved', 'rejected')),
    version integer NOT NULL CHECK (version > 0),
    reason text CHECK (reason IS NULL OR char_length(reason) BETWEEN 1 AND 1000),
    occurred_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (requisition_id, version)
);
CREATE INDEX requisition_audit_requisition_id_id ON monolith.requisition_audit (requisition_id, id);
CREATE TABLE monolith.notes (
    id uuid PRIMARY KEY,
    owner_id uuid NOT NULL,
    request_id uuid NOT NULL,
    body text NOT NULL CHECK (char_length(body) BETWEEN 1 AND 2000),
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (owner_id, request_id)
);
CREATE INDEX notes_owner_id_id ON monolith.notes (owner_id, id);
RESET ROLE;
GRANT USAGE ON SCHEMA monolith TO monolith_app, viewer_app;
GRANT SELECT, INSERT, UPDATE ON monolith.requisitions TO monolith_app;
GRANT SELECT, INSERT ON monolith.requisition_audit TO monolith_app;
GRANT SELECT, INSERT ON monolith.notes TO monolith_app;
GRANT SELECT ON ALL TABLES IN SCHEMA monolith TO viewer_app;
COMMIT;
SQL
unset MONOLITH_PASSWORD VIEWER_PASSWORD
