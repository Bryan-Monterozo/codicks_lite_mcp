#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"
RESULTS_DIR="${CHUNK09_RESULTS_DIR:-$ROOT_DIR/.artifacts/chunk09-tests}"
REPORT_PATH="${CHUNK09_REPORT_PATH:-$ROOT_DIR/docs/chunk-09-runtime-report.md}"

mkdir -p "$RESULTS_DIR"
mkdir -p "$(dirname "$REPORT_PATH")"
rm -f "$RESULTS_DIR"/*.trx

probe_dir="$(mktemp -d "${TMPDIR:-/tmp}/codicks-lite-case-probe.XXXXXX")"
cleanup_probe() {
  rm -rf "$probe_dir"
}
trap cleanup_probe EXIT

touch "$probe_dir/CaseProbe"
touch "$probe_dir/caseprobe"
probe_count="$(find "$probe_dir" -maxdepth 1 -type f | wc -l | tr -d ' ')"

if [[ "$probe_count" == "2" ]]; then
  volume_case_behavior="case-sensitive"
else
  volume_case_behavior="case-insensitive-or-preserving"
fi

"$DOTNET_BIN" test \
  "$ROOT_DIR/tests/LocalAgent.UnitTests/LocalAgent.UnitTests.csproj" \
  --configuration Release \
  --no-build \
  --filter 'Chunk=09' \
  --logger 'trx;LogFileName=chunk09-unit.trx' \
  --results-directory "$RESULTS_DIR/unit"

"$DOTNET_BIN" test \
  "$ROOT_DIR/tests/LocalAgent.IntegrationTests/LocalAgent.IntegrationTests.csproj" \
  --configuration Release \
  --no-build \
  --filter 'Chunk=09' \
  --logger 'trx;LogFileName=chunk09-integration.trx' \
  --results-directory "$RESULTS_DIR/integration"

"$DOTNET_BIN" test \
  "$ROOT_DIR/Codicks.Lite.Mcp.slnx" \
  --configuration Release \
  --no-build

sdk_version="$("$DOTNET_BIN" --version)"
os_version="$(sw_vers -productVersion 2>/dev/null || printf 'unknown')"
architecture="$(uname -m)"
timestamp="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"

cat > "$REPORT_PATH" <<EOF
# Chunk 09 Runtime Hardening Report

- Timestamp (UTC): $timestamp
- .NET SDK: $sdk_version
- macOS: $os_version
- Architecture: $architecture
- Temporary-volume case behavior: $volume_case_behavior
- Chunk 09 unit hardening tests: PASS
- Chunk 09 MCP integration hardening tests: PASS
- Full Chunk 01-09 regression suite: PASS

## Artifacts

- Unit TRX: \`.artifacts/chunk09-tests/unit/chunk09-unit.trx\`
- Integration TRX: \`.artifacts/chunk09-tests/integration/chunk09-integration.trx\`

The filesystem case probe records the behavior of the volume used for the test
temporary directory. It does not create or mount an alternate APFS volume.
EOF

printf 'Chunk 09 hardening suite passed.\n'
printf 'Runtime report: %s\n' "$REPORT_PATH"
