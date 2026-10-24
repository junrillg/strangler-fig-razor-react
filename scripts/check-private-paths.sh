#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$root"
forbidden=$(git ls-files -- .secrets .local architecture-labs profile.json tracker.json .env \
    ':(glob)**/.secrets/**' ':(glob)**/.local/**' ':(glob)**/architecture-labs/**' \
    ':(glob)**/profile.json' ':(glob)**/tracker.json' ':(glob)**/.env')
if test -n "$forbidden"; then
    printf 'Tracked private artifacts are forbidden. Remove them from the index before publication.\n' >&2
    exit 1
fi
