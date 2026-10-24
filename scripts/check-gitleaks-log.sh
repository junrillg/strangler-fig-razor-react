#!/bin/sh
set -eu
test "$#" -eq 2
case "$1" in tree|history) ;; *) exit 2 ;; esac
if grep -Eq 'ERR|FTL|permission denied|(^|[[:space:]])0 commits scanned' "$2"; then
    printf 'Secret scanning reported an operational error; its clean result is invalid.\n' >&2
    exit 1
fi
if test "$1" = history && ! grep -Eq '[[:space:]][1-9][0-9]* commits? scanned' "$2"; then
    printf 'Secret scanning did not establish that Git history was examined.\n' >&2
    exit 1
fi
