#!/usr/bin/env bash
set -euo pipefail

PROFILE="${CODICKS_LITE_TUNNEL_PROFILE:-codicks-lite-local-stdio}"
TUNNEL_ID="${CODICKS_LITE_TUNNEL_ID:-}"
MCP_COMMAND="${CODICKS_LITE_MCP_COMMAND:-$HOME/.local/bin/codicks-lite-mcp}"

if ! command -v tunnel-client >/dev/null 2>&1; then
  echo "ERROR: tunnel-client is not installed or not on PATH." >&2
  echo "Download the current tunnel-client from OpenAI Platform tunnel settings or the official openai/tunnel-client release page." >&2
  exit 1
fi

if [[ -z "$TUNNEL_ID" ]]; then
  echo "ERROR: set CODICKS_LITE_TUNNEL_ID to your OpenAI tunnel ID." >&2
  echo 'Example: export CODICKS_LITE_TUNNEL_ID="tunnel_..."' >&2
  exit 1
fi

if [[ -z "${CONTROL_PLANE_API_KEY:-}" ]]; then
  echo "ERROR: set CONTROL_PLANE_API_KEY to the runtime API key used by tunnel-client." >&2
  exit 1
fi

if [[ ! -x "$MCP_COMMAND" ]]; then
  echo "ERROR: MCP command is not executable: $MCP_COMMAND" >&2
  echo "Run ./install.sh first, or set CODICKS_LITE_MCP_COMMAND." >&2
  exit 1
fi

echo "Configuring tunnel-client profile: $PROFILE"
tunnel-client init \
  --sample sample_mcp_stdio_local \
  --profile "$PROFILE" \
  --tunnel-id "$TUNNEL_ID" \
  --mcp-command "$MCP_COMMAND"

echo
echo "Running tunnel diagnostics..."
tunnel-client doctor --profile "$PROFILE" --explain

echo
echo "Configured. Start it with:"
echo "  tunnel-client run --profile $PROFILE"
