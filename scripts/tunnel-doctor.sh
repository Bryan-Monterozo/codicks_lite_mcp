#!/usr/bin/env bash
set -euo pipefail

PROFILE="${CODICKS_LITE_TUNNEL_PROFILE:-codicks-lite-local-stdio}"

if ! command -v tunnel-client >/dev/null 2>&1; then
  echo "tunnel-client is not installed or not on PATH." >&2
  exit 1
fi

exec tunnel-client doctor \
  --profile "$PROFILE" \
  --explain
