#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Sorcha desktop-node deployer (Linux / macOS).
# Pulls the published images and brings up a single-node Sorcha stack sized for
# ~10-20 users. Pairs with docker/desktop/docker-compose.desktop.yml.
#
# Usage:
#   ./scripts/deploy-desktop.sh                 # pull + up + health + bootstrap
#   ./scripts/deploy-desktop.sh --tag 2.412.1   # pin an image tag
#   ./scripts/deploy-desktop.sh --skip-bootstrap
#   ./scripts/deploy-desktop.sh --down          # stop the node
#   ./scripts/deploy-desktop.sh --down --purge  # stop and DELETE all data volumes
set -euo pipefail

# --- locate the bundle (works whether run from repo root or elsewhere) -------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUNDLE_DIR="${SORCHA_BUNDLE_DIR:-$SCRIPT_DIR/../docker/desktop}"
BUNDLE_DIR="$(cd "$BUNDLE_DIR" && pwd)"
COMPOSE_FILE="$BUNDLE_DIR/docker-compose.desktop.yml"
ENV_FILE="$BUNDLE_DIR/.env"

TAG=""; SKIP_BOOTSTRAP=0; DO_DOWN=0; PURGE=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag) TAG="$2"; shift 2 ;;
    --skip-bootstrap) SKIP_BOOTSTRAP=1; shift ;;
    --down) DO_DOWN=1; shift ;;
    --purge) PURGE=1; shift ;;
    -h|--help) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown arg: $1" >&2; exit 2 ;;
  esac
done

c_red=$'\e[31m'; c_grn=$'\e[32m'; c_ylw=$'\e[33m'; c_off=$'\e[0m'
say()  { printf '%s\n' "$*"; }
ok()   { printf '%s✓%s %s\n' "$c_grn" "$c_off" "$*"; }
warn() { printf '%s!%s %s\n' "$c_ylw" "$c_off" "$*"; }
die()  { printf '%s✗ %s%s\n' "$c_red" "$*" "$c_off" >&2; exit 1; }

# --- 1. resolve the container runtime + compose command ----------------------
# License-free first (Podman), then Docker/Rancher, then legacy docker-compose.
detect_compose() {
  if command -v podman >/dev/null 2>&1 && podman compose version >/dev/null 2>&1; then
    RUNTIME=podman; COMPOSE=(podman compose)
  elif command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
    RUNTIME=docker; COMPOSE=(docker compose)
  elif command -v docker-compose >/dev/null 2>&1; then
    RUNTIME=docker; COMPOSE=(docker-compose)
  else
    die "No container runtime found. Install Rancher Desktop, Podman, or Docker, then re-run.
   Podman:  https://podman.io/docs/installation
   Rancher: https://rancherdesktop.io  (free, no Docker Desktop licence)"
  fi
  ok "Using runtime: $RUNTIME (${COMPOSE[*]})"
}

compose() { "${COMPOSE[@]}" -f "$COMPOSE_FILE" --env-file "$ENV_FILE" "$@"; }

# --- 2. resource preflight ---------------------------------------------------
# Gates on total RAM/CPU available to the runtime. Under WSL2/Podman-machine the
# reported figure is the VM's allocation, which is exactly what containers get.
preflight_resources() {
  local mem_gb cpu
  if [[ "$(uname)" == "Darwin" ]]; then
    mem_gb=$(( $(sysctl -n hw.memsize) / 1024 / 1024 / 1024 ))
    cpu=$(sysctl -n hw.ncpu)
  else
    mem_gb=$(awk '/MemTotal/ {printf "%d", $2/1024/1024}' /proc/meminfo)
    cpu=$(nproc)
  fi
  say "Detected: ${mem_gb} GB RAM, ${cpu} vCPU available to the runtime."

  # Policy: this scope (core + citizen wallet) needs ~6 GB / 4 vCPU minimum.
  if (( mem_gb < 6 )) || (( cpu < 4 )); then
    warn "Below the recommended 6 GB RAM / 4 vCPU floor for 10-20 users."
    warn "The node may run but could swap or stall under load."
    if [[ -t 0 ]]; then
      read -r -p "Continue anyway? [y/N] " a; [[ "$a" =~ ^[Yy]$ ]] || die "Aborted on resources."
    fi
  else
    ok "Resources meet the 6 GB / 4 vCPU floor."
  fi
}

# --- 3. ensure .env (generate secrets on first run) --------------------------
gen_secret_b64() { openssl rand -base64 32; }
gen_secret_alnum() { LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 32; }

ensure_env() {
  if [[ -f "$ENV_FILE" ]]; then ok ".env present"; return; fi
  warn "No .env found — generating one with fresh secrets."
  cp "$BUNDLE_DIR/.env.desktop.example" "$ENV_FILE"
  local jwt pgp mgp
  jwt="$(gen_secret_b64)"; pgp="$(gen_secret_alnum)"; mgp="$(gen_secret_alnum)"
  # Per-service ServiceAuth client secrets (#1423) — one per internal service
  # principal; alphanumeric so they're safe unquoted in .env and in compose
  # ${VAR:?...} interpolation.
  local wallet_s tenant_s register_s validator_s haip_s peer_s blueprint_s
  wallet_s="$(gen_secret_alnum)"; tenant_s="$(gen_secret_alnum)"; register_s="$(gen_secret_alnum)"
  validator_s="$(gen_secret_alnum)"; haip_s="$(gen_secret_alnum)"; peer_s="$(gen_secret_alnum)"
  blueprint_s="$(gen_secret_alnum)"
  # Mongo password is embedded in a connection URI, so keep it alphanumeric.
  sed -i.bak \
    -e "s|^JWT_SIGNING_KEY=.*|JWT_SIGNING_KEY=${jwt}|" \
    -e "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=${pgp}|" \
    -e "s|^MONGO_PASSWORD=.*|MONGO_PASSWORD=${mgp}|" \
    -e "s|^WALLET_SERVICE_SECRET=.*|WALLET_SERVICE_SECRET=${wallet_s}|" \
    -e "s|^TENANT_SERVICE_SECRET=.*|TENANT_SERVICE_SECRET=${tenant_s}|" \
    -e "s|^REGISTER_SERVICE_SECRET=.*|REGISTER_SERVICE_SECRET=${register_s}|" \
    -e "s|^VALIDATOR_SERVICE_SECRET=.*|VALIDATOR_SERVICE_SECRET=${validator_s}|" \
    -e "s|^HAIP_SERVICE_SECRET=.*|HAIP_SERVICE_SECRET=${haip_s}|" \
    -e "s|^PEER_SERVICE_SECRET=.*|PEER_SERVICE_SECRET=${peer_s}|" \
    -e "s|^BLUEPRINT_SERVICE_SECRET=.*|BLUEPRINT_SERVICE_SECRET=${blueprint_s}|" \
    "$ENV_FILE" && rm -f "$ENV_FILE.bak"
  chmod 600 "$ENV_FILE"
  ok "Generated $ENV_FILE (secrets written, mode 600)."
}

# --- 4. health gate ----------------------------------------------------------
http_port() { grep -E '^SORCHA_HTTP_PORT=' "$ENV_FILE" | tail -1 | cut -d= -f2 | tr -d '"'; }

wait_for_health() {
  local port; port="$(http_port)"; port="${port:-8880}"
  local url="http://localhost:${port}/health"
  say "Waiting for the gateway to become healthy at ${url} ..."
  local deadline=$(( SECONDS + 300 ))
  until curl -fsS -o /dev/null "$url" 2>/dev/null; do
    (( SECONDS < deadline )) || { compose ps; die "Gateway not healthy after 5 min. See logs: $(printf '%q ' "${COMPOSE[@]}") -f \"$COMPOSE_FILE\" logs"; }
    sleep 5; printf '.'
  done
  printf '\n'; ok "Gateway healthy."
}

# --- 5. bootstrap ------------------------------------------------------------
# The system register auto-bootstraps (SystemRegister__BootstrapMode=Auto). The
# admin org + first user are provisioned by the repo bootstrap flow when this is
# run from a source checkout. On a source-less node we print guided steps — wire
# your confirmed tenant registration endpoint into bootstrap_node() to fully
# automate. (The bootstrap CLI is still beta; see scripts/README-BOOTSTRAP.md.)
bootstrap_node() {
  local port; port="$(http_port)"; port="${port:-8880}"
  if [[ -x "$SCRIPT_DIR/bootstrap-sorcha.sh" ]]; then
    say "Running repo bootstrap (bootstrap-sorcha.sh) ..."
    SORCHA_GATEWAY="http://localhost:${port}" "$SCRIPT_DIR/bootstrap-sorcha.sh" --non-interactive \
      || warn "Bootstrap script exited non-zero — finish admin setup manually (see below)."
  else
    warn "No bootstrap-sorcha.sh on this machine (source-less node)."
    say  "Finish setup by registering the first admin via the UI:"
    say  "   open http://localhost:${port}/app  →  Register"
  fi
}

# --- main --------------------------------------------------------------------
[[ -f "$COMPOSE_FILE" ]] || die "Compose file not found: $COMPOSE_FILE"
detect_compose

if (( DO_DOWN )); then
  if (( PURGE )); then
    warn "Tearing down AND deleting all data volumes."
    compose down -v
  else
    compose down
  fi
  ok "Node stopped."; exit 0
fi

preflight_resources
ensure_env
[[ -n "$TAG" ]] && export SORCHA_IMAGE_TAG="$TAG"

say "Pulling images ..."; compose pull
say "Starting the node ..."; compose up -d
wait_for_health
(( SKIP_BOOTSTRAP )) || bootstrap_node

port="$(http_port)"; port="${port:-8880}"
echo
ok "Sorcha desktop node is up."
say "   Web UI .......... http://localhost:${port}/app"
say "   Citizen wallet .. http://localhost:${port}/wallet"
say "   API health ...... http://localhost:${port}/health"
say "   Stop ............ ./scripts/deploy-desktop.sh --down"
