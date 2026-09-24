#!/usr/bin/env bash
set -euo pipefail
PROFILE="${CODICKS_LITE_TUNNEL_PROFILE:-codicks-lite-local-stdio}"
exec tunnel-client run --profile "$PROFILE"
