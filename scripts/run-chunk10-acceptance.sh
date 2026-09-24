#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"
RESULTS_DIR="${CHUNK10_RESULTS_DIR:-$ROOT_DIR/.artifacts/chunk10-acceptance}"

mkdir -p "$RESULTS_DIR"

"$DOTNET_BIN" test \
  "$ROOT_DIR/tests/LocalAgent.IntegrationTests/LocalAgent.IntegrationTests.csproj" \
  --configuration Release \
  --no-build \
  --filter "Chunk=10" \
  --logger "trx;LogFileName=chunk10-local-acceptance.trx" \
  --results-directory "$RESULTS_DIR"
