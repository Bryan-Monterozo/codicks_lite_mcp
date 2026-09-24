#!/usr/bin/env bash
set -euo pipefail

# =============================================================================
# Codicks Lite MCP - Machine / Tunnel Configuration Manager
#
# Purpose:
#   - Keep machine-specific deployment settings in one predictable location.
#   - Keep CONTROL_PLANE_API_KEY out of agent.json and tunnel YAML.
#   - Store the runtime API key in macOS Keychain by default.
#   - Create/recreate the OpenAI tunnel-client stdio profile idempotently.
#   - Provide one command surface for configure/show/apply/doctor/run.
#
# Future installer use:
#   CODICKS_LITE_TUNNEL_ID="tunnel_..." \
#   CONTROL_PLANE_API_KEY="..." \
#   ./setup_config.sh all --non-interactive
#
# This script does NOT modify workspace permissions in agent.json.
# =============================================================================

SCRIPT_VERSION="1.2.0"
DEFAULT_PROFILE="codicks-lite-local-stdio"
DEFAULT_MCP_COMMAND="$HOME/.local/bin/codicks-lite-mcp"
DEFAULT_KEYCHAIN_SERVICE="CodicksLiteMcp.OpenAI.RuntimeApiKey"
DEFAULT_HEALTH_LISTEN_ADDR="127.0.0.1:0"

APP_ROOT="$HOME/Library/Application Support/CodicksLiteMcp"
CONFIG_DIR="$APP_ROOT/config"
CONFIG_FILE="$CONFIG_DIR/deployment.env"
PROFILE_BACKUP_DIR="$APP_ROOT/profile-backups"

XDG_CONFIG_ROOT="${XDG_CONFIG_HOME:-$HOME/.config}"
DEFAULT_TUNNEL_PROFILE_DIR="$XDG_CONFIG_ROOT/tunnel-client"

ACTION="configure"
NON_INTERACTIVE=false
FORCE=false

ARG_TUNNEL_ID=""
ARG_RUNTIME_KEY=""
ARG_PROFILE=""
ARG_MCP_COMMAND=""
ARG_HEALTH_LISTEN_ADDR=""

info() {
  printf '\n==> %s\n' "$1"
}

warn() {
  printf '\nWARNING: %s\n' "$1" >&2
}

fail() {
  printf '\nERROR: %s\n' "$1" >&2
  exit 1
}

command_exists() {
  command -v "$1" >/dev/null 2>&1
}

usage() {
  cat <<'EOF'
Codicks Lite MCP configuration manager

Usage:
  ./setup_config.sh [action] [options]

Actions:
  init        Create the private config template if it does not exist.
  configure   Configure/update values and store runtime key in Keychain.
              This is the default action.
  show        Show effective non-secret configuration and secret status.
  apply       Create/recreate the tunnel-client named profile.
  doctor      Run tunnel-client doctor using the stored runtime key.
  run         Run tunnel-client using the stored runtime key.
  all         configure -> apply -> doctor.
  edit        Open deployment.env in $EDITOR (or vi).
  help        Show this help.

Options:
  --non-interactive
      Do not prompt. Values must already exist in environment/config or flags.

  --tunnel-id VALUE
      OpenAI tunnel ID, e.g. tunnel_0123456789abcdef0123456789abcdef.

  --runtime-key VALUE
      OpenAI runtime API key. It is stored in macOS Keychain, not deployment.env.

  --profile VALUE
      tunnel-client profile name. Default: codicks-lite-local-stdio

  --mcp-command VALUE
      Local stdio MCP command. Default: ~/.local/bin/codicks-lite-mcp

  --health-listen-addr VALUE
      Local health/admin listener. Default: 127.0.0.1:0
      Port 0 lets macOS choose a free loopback port automatically.

  --force
      Recreate the tunnel profile without interactive confirmation.

Environment values are also accepted:
  CODICKS_LITE_TUNNEL_ID
  CONTROL_PLANE_TUNNEL_ID
  CONTROL_PLANE_API_KEY
  CODICKS_LITE_TUNNEL_PROFILE
  CODICKS_LITE_MCP_COMMAND

Common first-time setup:
  ./setup_config.sh all

Unattended/future one-click deployment:
  CODICKS_LITE_TUNNEL_ID="tunnel_..." \
  CONTROL_PLANE_API_KEY="..." \
  ./setup_config.sh all --non-interactive

Later use:
  ./setup_config.sh doctor
  ./setup_config.sh run
EOF
}

parse_args() {
  if [[ $# -gt 0 && "${1:-}" != --* ]]; then
    ACTION="$1"
    shift
  fi

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --non-interactive)
        NON_INTERACTIVE=true
        shift
        ;;
      --force)
        FORCE=true
        shift
        ;;
      --tunnel-id)
        [[ $# -ge 2 ]] || fail "--tunnel-id requires a value."
        ARG_TUNNEL_ID="$2"
        shift 2
        ;;
      --runtime-key)
        [[ $# -ge 2 ]] || fail "--runtime-key requires a value."
        ARG_RUNTIME_KEY="$2"
        shift 2
        ;;
      --profile)
        [[ $# -ge 2 ]] || fail "--profile requires a value."
        ARG_PROFILE="$2"
        shift 2
        ;;
      --mcp-command)
        [[ $# -ge 2 ]] || fail "--mcp-command requires a value."
        ARG_MCP_COMMAND="$2"
        shift 2
        ;;
      --health-listen-addr)
        [[ $# -ge 2 ]] || fail "--health-listen-addr requires a value."
        ARG_HEALTH_LISTEN_ADDR="$2"
        shift 2
        ;;
      -h|--help)
        ACTION="help"
        shift
        ;;
      *)
        fail "Unknown argument: $1"
        ;;
    esac
  done
}

ensure_macos() {
  [[ "$(uname -s)" == "Darwin" ]] \
    || fail "This configuration manager currently targets macOS."

  command_exists security \
    || fail "macOS Keychain command 'security' was not found."
}

ensure_private_config_dir() {
  mkdir -p "$CONFIG_DIR"
  chmod 700 "$CONFIG_DIR"
}

write_config() {
  local profile="$1"
  local tunnel_id="$2"
  local mcp_command="$3"
  local keychain_service="$4"
  local health_listen_addr="$5"

  ensure_private_config_dir

  local temporary="$CONFIG_FILE.tmp.$$"

  {
    printf '# Codicks Lite MCP machine/deployment settings\n'
    printf '# Generated by setup_config.sh %s\n' "$SCRIPT_VERSION"
    printf '# Runtime API key is intentionally NOT stored here.\n'
    printf '# It is stored in macOS Keychain under CODICKS_LITE_KEYCHAIN_SERVICE.\n\n'

    printf 'CODICKS_LITE_TUNNEL_PROFILE=%q\n' "$profile"
    printf 'CODICKS_LITE_TUNNEL_ID=%q\n' "$tunnel_id"
    printf 'CODICKS_LITE_MCP_COMMAND=%q\n' "$mcp_command"
    printf 'CODICKS_LITE_KEYCHAIN_SERVICE=%q\n' "$keychain_service"
    printf 'CODICKS_LITE_HEALTH_LISTEN_ADDR=%q\n' "$health_listen_addr"
    printf 'CODICKS_LITE_TUNNEL_PROFILE_DIR=%q\n' "$DEFAULT_TUNNEL_PROFILE_DIR"
  } > "$temporary"

  chmod 600 "$temporary"
  mv "$temporary" "$CONFIG_FILE"
}

create_template_if_missing() {
  ensure_private_config_dir

  if [[ -f "$CONFIG_FILE" ]]; then
    chmod 600 "$CONFIG_FILE"
    return 0
  fi

  info "Creating private Codicks Lite deployment configuration template"

  write_config \
    "$DEFAULT_PROFILE" \
    "tunnel_REPLACE_ME" \
    "$DEFAULT_MCP_COMMAND" \
    "$DEFAULT_KEYCHAIN_SERVICE" \
    "$DEFAULT_HEALTH_LISTEN_ADDR"

  printf 'Created:\n  %s\n' "$CONFIG_FILE"
}

load_config() {
  create_template_if_missing

  # deployment.env is created mode 0600 and intentionally uses shell assignments.
  # shellcheck disable=SC1090
  source "$CONFIG_FILE"

  CODICKS_LITE_TUNNEL_PROFILE="${CODICKS_LITE_TUNNEL_PROFILE:-$DEFAULT_PROFILE}"
  CODICKS_LITE_TUNNEL_ID="${CODICKS_LITE_TUNNEL_ID:-tunnel_REPLACE_ME}"
  CODICKS_LITE_MCP_COMMAND="${CODICKS_LITE_MCP_COMMAND:-$DEFAULT_MCP_COMMAND}"
  CODICKS_LITE_KEYCHAIN_SERVICE="${CODICKS_LITE_KEYCHAIN_SERVICE:-$DEFAULT_KEYCHAIN_SERVICE}"
  CODICKS_LITE_HEALTH_LISTEN_ADDR="${CODICKS_LITE_HEALTH_LISTEN_ADDR:-$DEFAULT_HEALTH_LISTEN_ADDR}"
  CODICKS_LITE_TUNNEL_PROFILE_DIR="${CODICKS_LITE_TUNNEL_PROFILE_DIR:-$DEFAULT_TUNNEL_PROFILE_DIR}"
}

is_placeholder() {
  local value="${1:-}"

  [[ -z "$value" ]] && return 0
  [[ "$value" == *"REPLACE_ME"* ]] && return 0
  [[ "$value" == *"YOUR_"* ]] && return 0

  return 1
}

validate_profile_name() {
  local value="$1"

  [[ "$value" =~ ^[A-Za-z0-9._-]+$ ]] \
    || fail "Invalid profile name '$value'. Use letters, numbers, dot, underscore, or hyphen."
}

validate_tunnel_id() {
  local value="$1"

  is_placeholder "$value" \
    && fail "Tunnel ID is still a placeholder."

  [[ "$value" == tunnel_* ]] \
    || fail "Tunnel ID should begin with 'tunnel_'."
}

validate_mcp_command() {
  local command_path="$1"

  is_placeholder "$command_path" \
    && fail "MCP command is still a placeholder."

  [[ "$command_path" == /* ]] \
    || fail "MCP command must be an absolute path."

  [[ -x "$command_path" ]] \
    || fail "MCP command is not executable: $command_path"
}

keychain_has_runtime_key() {
  security find-generic-password \
    -a "$USER" \
    -s "$CODICKS_LITE_KEYCHAIN_SERVICE" \
    -w >/dev/null 2>&1
}

store_runtime_key() {
  local runtime_key="$1"

  is_placeholder "$runtime_key" \
    && fail "Runtime API key is empty or still a placeholder."

  security add-generic-password \
    -U \
    -a "$USER" \
    -s "$CODICKS_LITE_KEYCHAIN_SERVICE" \
    -w "$runtime_key" >/dev/null

  printf 'Runtime API key stored in macOS Keychain service:\n  %s\n' \
    "$CODICKS_LITE_KEYCHAIN_SERVICE"
}

load_runtime_key() {
  if [[ -n "${CONTROL_PLANE_API_KEY:-}" ]] \
      && ! is_placeholder "$CONTROL_PLANE_API_KEY"; then
    printf '%s' "$CONTROL_PLANE_API_KEY"
    return 0
  fi

  security find-generic-password \
    -a "$USER" \
    -s "$CODICKS_LITE_KEYCHAIN_SERVICE" \
    -w 2>/dev/null
}

prompt_value() {
  local label="$1"
  local current="$2"
  local result=""

  printf '%s [%s]: ' "$label" "$current" >&2
  IFS= read -r result

  if [[ -z "$result" ]]; then
    printf '%s' "$current"
  else
    printf '%s' "$result"
  fi
}

prompt_runtime_key_if_needed() {
  local runtime_key="$ARG_RUNTIME_KEY"

  if [[ -z "$runtime_key" \
        && -n "${CONTROL_PLANE_API_KEY:-}" \
        && "${CONTROL_PLANE_API_KEY:-}" != *"REPLACE_ME"* ]]; then
    runtime_key="$CONTROL_PLANE_API_KEY"
  fi

  if [[ -n "$runtime_key" ]]; then
    store_runtime_key "$runtime_key"
    return 0
  fi

  if keychain_has_runtime_key; then
    printf 'Existing runtime API key found in Keychain; preserving it.\n'
    return 0
  fi

  if [[ "$NON_INTERACTIVE" == true ]]; then
    fail "No runtime API key was supplied and none exists in Keychain."
  fi

  printf 'OpenAI runtime API key (input hidden): ' >&2
  IFS= read -r -s runtime_key
  printf '\n' >&2

  [[ -n "$runtime_key" ]] \
    || fail "Runtime API key cannot be empty."

  store_runtime_key "$runtime_key"
}

configure_action() {
  ensure_macos
  load_config

  local profile="$CODICKS_LITE_TUNNEL_PROFILE"
  local tunnel_id="$CODICKS_LITE_TUNNEL_ID"
  local mcp_command="$CODICKS_LITE_MCP_COMMAND"
  local health_listen_addr="$CODICKS_LITE_HEALTH_LISTEN_ADDR"

  if [[ -n "$ARG_PROFILE" ]]; then
    profile="$ARG_PROFILE"
  elif [[ -n "${CODICKS_LITE_TUNNEL_PROFILE_OVERRIDE:-}" ]]; then
    profile="$CODICKS_LITE_TUNNEL_PROFILE_OVERRIDE"
  fi

  if [[ -n "$ARG_TUNNEL_ID" ]]; then
    tunnel_id="$ARG_TUNNEL_ID"
  elif [[ -n "${CONTROL_PLANE_TUNNEL_ID:-}" ]]; then
    tunnel_id="$CONTROL_PLANE_TUNNEL_ID"
  elif [[ -n "${CODICKS_LITE_TUNNEL_ID_OVERRIDE:-}" ]]; then
    tunnel_id="$CODICKS_LITE_TUNNEL_ID_OVERRIDE"
  fi

  if [[ -n "$ARG_MCP_COMMAND" ]]; then
    mcp_command="$ARG_MCP_COMMAND"
  elif [[ -n "${CODICKS_LITE_MCP_COMMAND_OVERRIDE:-}" ]]; then
    mcp_command="$CODICKS_LITE_MCP_COMMAND_OVERRIDE"
  fi

  if [[ -n "$ARG_HEALTH_LISTEN_ADDR" ]]; then
    health_listen_addr="$ARG_HEALTH_LISTEN_ADDR"
  elif [[ -n "${CODICKS_LITE_HEALTH_LISTEN_ADDR_OVERRIDE:-}" ]]; then
    health_listen_addr="$CODICKS_LITE_HEALTH_LISTEN_ADDR_OVERRIDE"
  fi

  if [[ "$NON_INTERACTIVE" == false ]]; then
    info "Configure Codicks Lite tunnel settings"
    printf 'Press Enter to keep the shown value.\n'

    profile="$(prompt_value "Tunnel profile" "$profile")"
    tunnel_id="$(prompt_value "Tunnel ID" "$tunnel_id")"
    mcp_command="$(prompt_value "MCP command" "$mcp_command")"
    health_listen_addr="$(prompt_value "Health listener" "$health_listen_addr")"
  fi

  validate_profile_name "$profile"
  validate_tunnel_id "$tunnel_id"
  validate_mcp_command "$mcp_command"

  write_config \
    "$profile" \
    "$tunnel_id" \
    "$mcp_command" \
    "$CODICKS_LITE_KEYCHAIN_SERVICE" \
    "$health_listen_addr"

  # Reload the just-written values before using the Keychain service.
  load_config
  prompt_runtime_key_if_needed

  info "Configuration saved"
  show_action
}

show_action() {
  ensure_macos
  load_config

  local profile_file="$CODICKS_LITE_TUNNEL_PROFILE_DIR/$CODICKS_LITE_TUNNEL_PROFILE.yaml"
  local secret_status="MISSING"

  if keychain_has_runtime_key; then
    secret_status="PRESENT IN KEYCHAIN"
  elif [[ -n "${CONTROL_PLANE_API_KEY:-}" ]]; then
    secret_status="PRESENT IN ENVIRONMENT"
  fi

  cat <<EOF

Codicks Lite deployment configuration
--------------------------------
Config file:
  $CONFIG_FILE

Tunnel profile:
  $CODICKS_LITE_TUNNEL_PROFILE

Tunnel ID:
  $CODICKS_LITE_TUNNEL_ID

MCP command:
  $CODICKS_LITE_MCP_COMMAND

Health listener:
  $CODICKS_LITE_HEALTH_LISTEN_ADDR

Tunnel profile file:
  $profile_file

Runtime key:
  $secret_status

Keychain service:
  $CODICKS_LITE_KEYCHAIN_SERVICE

Agent workspace/security config:
  $APP_ROOT/config/agent.json

Secrets are not written into deployment.env or the tunnel YAML.
EOF
}

backup_profile_if_present() {
  local profile_file="$1"

  [[ -f "$profile_file" ]] || return 0

  mkdir -p "$PROFILE_BACKUP_DIR"

  local backup="$PROFILE_BACKUP_DIR/$(basename "$profile_file").$(date +%Y%m%d-%H%M%S).bak"
  cp "$profile_file" "$backup"
  chmod 600 "$backup"

  printf 'Existing tunnel profile backed up to:\n  %s\n' "$backup"
}

apply_action() {
  ensure_macos
  load_config

  command_exists tunnel-client \
    || fail "tunnel-client is not installed or not on PATH."

  validate_profile_name "$CODICKS_LITE_TUNNEL_PROFILE"
  validate_tunnel_id "$CODICKS_LITE_TUNNEL_ID"
  validate_mcp_command "$CODICKS_LITE_MCP_COMMAND"

  local runtime_key
  runtime_key="$(load_runtime_key)" \
    || fail "No runtime API key exists in Keychain/environment."

  [[ -n "$runtime_key" ]] \
    || fail "Runtime API key is empty."

  local profile_file="$CODICKS_LITE_TUNNEL_PROFILE_DIR/$CODICKS_LITE_TUNNEL_PROFILE.yaml"

  if [[ -f "$profile_file" && "$FORCE" == false && "$NON_INTERACTIVE" == false ]]; then
    printf 'Tunnel profile already exists:\n  %s\n' "$profile_file"
    printf 'Recreate it from Codicks Lite configuration? [y/N]: ' >&2

    local answer=""
    IFS= read -r answer

    case "$answer" in
      y|Y|yes|YES)
        ;;
      *)
        printf 'Profile recreation cancelled.\n'
        return 0
        ;;
    esac
  fi

  backup_profile_if_present "$profile_file"

  info "Creating tunnel-client stdio profile"

  CONTROL_PLANE_API_KEY="$runtime_key" \
  tunnel-client init \
    --sample sample_mcp_stdio_local \
    --profile "$CODICKS_LITE_TUNNEL_PROFILE" \
    --tunnel-id "$CODICKS_LITE_TUNNEL_ID" \
    --mcp-command "$CODICKS_LITE_MCP_COMMAND" \
    --control-plane-api-key-ref "env:CONTROL_PLANE_API_KEY" \
    --health-listen-addr "$CODICKS_LITE_HEALTH_LISTEN_ADDR" \
    --force

  [[ -f "$profile_file" ]] \
    || fail "tunnel-client did not create the expected profile: $profile_file"

  chmod 600 "$profile_file"

  printf '\nCreated tunnel profile:\n  %s\n' "$profile_file"
}

doctor_action() {
  ensure_macos
  load_config

  command_exists tunnel-client \
    || fail "tunnel-client is not installed or not on PATH."

  local runtime_key
  runtime_key="$(load_runtime_key)" \
    || fail "No runtime API key exists in Keychain/environment."

  info "Running tunnel-client doctor"

  CONTROL_PLANE_API_KEY="$runtime_key" \
  tunnel-client doctor \
    --profile "$CODICKS_LITE_TUNNEL_PROFILE" \
    --explain
}

run_action() {
  ensure_macos
  load_config

  command_exists tunnel-client \
    || fail "tunnel-client is not installed or not on PATH."

  local runtime_key
  runtime_key="$(load_runtime_key)" \
    || fail "No runtime API key exists in Keychain/environment."

  info "Starting OpenAI tunnel for Codicks Lite MCP"

  export CONTROL_PLANE_API_KEY="$runtime_key"
  exec tunnel-client run \
    --profile "$CODICKS_LITE_TUNNEL_PROFILE"
}

edit_action() {
  ensure_macos
  create_template_if_missing

  local editor="${EDITOR:-vi}"
  exec "$editor" "$CONFIG_FILE"
}

all_action() {
  configure_action

  # A first-time all-run should be unattended after configuration is collected.
  FORCE=true
  apply_action
  doctor_action
}

init_action() {
  ensure_macos
  create_template_if_missing

  printf '\nTemplate ready:\n  %s\n' "$CONFIG_FILE"
  printf '\nNext:\n  ./setup_config.sh configure\n'
}

main() {
  parse_args "$@"

  case "$ACTION" in
    init)
      init_action
      ;;
    configure)
      configure_action
      ;;
    show)
      show_action
      ;;
    apply)
      apply_action
      ;;
    doctor)
      doctor_action
      ;;
    run)
      run_action
      ;;
    all)
      all_action
      ;;
    edit)
      edit_action
      ;;
    help)
      usage
      ;;
    *)
      usage >&2
      fail "Unknown action: $ACTION"
      ;;
  esac
}

main "$@"
