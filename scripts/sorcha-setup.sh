#!/usr/bin/env bash
# =============================================================================
# Sorcha Platform — Interactive Setup Script
# =============================================================================
#
# Sets up a Sorcha instance by:
#   1. Checking prerequisites (Docker)
#   2. Asking configuration questions
#   3. Generating a .env file
#   4. Pulling the latest Docker images
#   5. Starting all services
#   6. Running bootstrap (admin user, sample data)
#
# Usage:
#   ./scripts/sorcha-setup.sh           # Interactive setup
#   ./scripts/sorcha-setup.sh --quiet   # Use all defaults, no prompts
#
# =============================================================================

set -euo pipefail

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m' # No Color

QUIET=false
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$PROJECT_DIR/.env"

# Parse arguments
CONFIG_ONLY=false

for arg in "$@"; do
    case $arg in
        --quiet|-q) QUIET=true ;;
        --config-only) CONFIG_ONLY=true ;;
        --help|-h)
            echo "Usage: $0 [--quiet|-q] [--config-only] [--help|-h]"
            echo "  --quiet, -q     Use all defaults without prompting"
            echo "  --config-only   Generate .env + certificates only; do not pull or start."
            echo "                  For nodes that bring the stack up with their own compose"
            echo "                  file set (n1 stacks n1/seed/ports/smtp overrides), where a"
            echo "                  bare 'docker compose up -d' would start the WRONG stack."
            echo "  --help, -h      Show this help message"
            exit 0
            ;;
    esac
done

# -----------------------------------------------------------------------------
# Helper functions
# -----------------------------------------------------------------------------

banner() {
    echo ""
    echo -e "${CYAN}╔══════════════════════════════════════════════════════════╗${NC}"
    echo -e "${CYAN}║${NC}  ${BOLD}Sorcha Platform Setup${NC}                                  ${CYAN}║${NC}"
    echo -e "${CYAN}║${NC}  Distributed Ledger for Secure Data Flow Orchestration  ${CYAN}║${NC}"
    echo -e "${CYAN}╚══════════════════════════════════════════════════════════╝${NC}"
    echo ""
}

info()    { echo -e "${BLUE}[INFO]${NC} $1"; }
success() { echo -e "${GREEN}[OK]${NC}   $1"; }
warn()    { echo -e "${YELLOW}[WARN]${NC} $1"; }
error()   { echo -e "${RED}[ERROR]${NC} $1"; }

ask() {
    local prompt="$1"
    local default="$2"
    local var_name="$3"

    if [ "$QUIET" = true ]; then
        eval "$var_name='$default'"
        return
    fi

    if [ -n "$default" ]; then
        echo -ne "${BOLD}$prompt${NC} [${default}]: "
    else
        echo -ne "${BOLD}$prompt${NC}: "
    fi

    local answer
    read -r answer
    if [ -z "$answer" ]; then
        eval "$var_name='$default'"
    else
        eval "$var_name='$answer'"
    fi
}

ask_yes_no() {
    local prompt="$1"
    local default="$2"
    local var_name="$3"

    if [ "$QUIET" = true ]; then
        eval "$var_name='$default'"
        return
    fi

    local hint="y/n"
    [ "$default" = "y" ] && hint="Y/n"
    [ "$default" = "n" ] && hint="y/N"

    echo -ne "${BOLD}$prompt${NC} [$hint]: "
    local answer
    read -r answer
    answer="${answer:-$default}"
    answer=$(echo "$answer" | tr '[:upper:]' '[:lower:]')

    if [[ "$answer" == "y" || "$answer" == "yes" ]]; then
        eval "$var_name='y'"
    else
        eval "$var_name='n'"
    fi
}

generate_jwt_key() {
    # Generate a 256-bit base64 key
    if command -v openssl &> /dev/null; then
        openssl rand -base64 32
    elif command -v python3 &> /dev/null; then
        python3 -c "import secrets, base64; print(base64.b64encode(secrets.token_bytes(32)).decode())"
    else
        # Fallback: read from /dev/urandom
        head -c 32 /dev/urandom | base64
    fi
}

generate_service_secret() {
    # Per-deploy service-to-service auth secret (issue #1412). Same
    # openssl/python3/urandom fallback chain as generate_jwt_key(), but the
    # output is filtered down to URL-safe alphanumeric characters and
    # truncated to ~32 chars so the value drops into docker-compose / .env
    # with no quoting, padding or '+/=' surprises. 48 raw bytes are requested
    # so there is always enough alnum material left after filtering.
    local raw
    if command -v openssl &> /dev/null; then
        raw=$(openssl rand -base64 48)
    elif command -v python3 &> /dev/null; then
        raw=$(python3 -c "import secrets, base64; print(base64.b64encode(secrets.token_bytes(48)).decode())")
    else
        # Fallback: read from /dev/urandom
        raw=$(head -c 48 /dev/urandom | base64)
    fi
    echo "$raw" | tr -dc 'A-Za-z0-9' | cut -c1-32
}

# -----------------------------------------------------------------------------
# Prerequisite checks
# -----------------------------------------------------------------------------

# -----------------------------------------------------------------------------
# Prerequisite check helpers (spec 117 FR-032 / FR-033 — T092)
#
# Every required check returns 0 on success, non-zero on failure, and emits the
# spec-mandated single-line error format on failure:
#   [sorcha-setup] missing prerequisite: <name> (≥ <version>); install via <link>
# Optional checks emit a [WARN] line and return 0.
#
# Exposed for unit testing (tests/scripts/sorcha-setup.bats — T091): when
# SORCHA_SETUP_LIB_ONLY=1 is set the script exits before main(), leaving the
# helper functions defined for the bats runner to invoke.
# -----------------------------------------------------------------------------

emit_missing_prereq() {
    # Args: <name> <min-version> <install-link>
    echo "[sorcha-setup] missing prerequisite: $1 (≥ $2); install via $3" >&2
}

check_docker_installed() {
    if command -v docker &> /dev/null; then
        local docker_version
        docker_version=$(docker --version 2>/dev/null | grep -oP '\d+\.\d+\.\d+' | head -1)
        success "Docker ${docker_version:-detected}"
        return 0
    fi
    emit_missing_prereq "docker" "24.0" "https://docs.docker.com/engine/install/"
    return 1
}

check_docker_daemon_running() {
    if docker info &> /dev/null 2>&1; then
        success "Docker daemon is running"
        return 0
    fi
    emit_missing_prereq "docker-daemon" "running" "start your Docker engine (Docker Desktop on Win/macOS, 'systemctl start docker' on Linux)"
    return 1
}

check_docker_compose_v2() {
    if docker compose version &> /dev/null; then
        local compose_version
        compose_version=$(docker compose version 2>/dev/null | grep -oP '\d+\.\d+\.\d+' | head -1)
        # Gate on Compose v2 (v1 standalone is past EOL).
        if [ -n "$compose_version" ]; then
            local major
            major=$(echo "$compose_version" | cut -d. -f1)
            if [ "${major:-0}" -ge 2 ]; then
                success "Docker Compose ${compose_version}"
                return 0
            fi
        fi
    fi
    emit_missing_prereq "docker-compose-v2" "2.0" "https://docs.docker.com/compose/install/ (the v1 standalone 'docker-compose' is end-of-life — install Compose v2 plugin)"
    return 1
}

check_port_available() {
    # Args: <port>
    local port="$1"
    # Try multiple probes — different distros ship different tools.
    if command -v ss &> /dev/null; then
        if ss -tlnH 2>/dev/null | grep -qE "[:.]${port}\\s"; then
            emit_missing_prereq "port-${port}-free" "free" "stop the process bound to port ${port} (try 'ss -tlnp | grep :${port}')"
            return 1
        fi
    elif command -v netstat &> /dev/null; then
        if netstat -tlnH 2>/dev/null | grep -qE "[:.]${port}\\s"; then
            emit_missing_prereq "port-${port}-free" "free" "stop the process bound to port ${port}"
            return 1
        fi
    elif command -v lsof &> /dev/null; then
        if lsof -nP -iTCP:"${port}" -sTCP:LISTEN &> /dev/null; then
            emit_missing_prereq "port-${port}-free" "free" "stop the process bound to port ${port} (try 'lsof -nP -iTCP:${port}')"
            return 1
        fi
    else
        # No probe available — soft-pass with a notice rather than failing.
        warn "no port-probe tool (ss/netstat/lsof) available; cannot verify port ${port} is free"
        return 0
    fi
    success "Port ${port} is available"
    return 0
}

check_openssl_or_python() {
    # JWT signing-key generation (line ~120 in this script) needs one of:
    # openssl, python3 (with secrets module), or a working /dev/urandom.
    if command -v openssl &> /dev/null; then
        success "OpenSSL $(openssl version 2>/dev/null | awk '{print $2}')"
        return 0
    fi
    if command -v python3 &> /dev/null; then
        success "Python3 $(python3 --version 2>/dev/null | awk '{print $2}')"
        return 0
    fi
    if [ -r /dev/urandom ]; then
        warn "neither openssl nor python3 found; falling back to /dev/urandom for JWT key generation"
        return 0
    fi
    emit_missing_prereq "openssl-or-python3" "any" "https://www.openssl.org/source/ or https://www.python.org/downloads/ (used to generate the JWT signing key)"
    return 1
}

check_git() {
    # Optional — needed for development, not setup. Emits a [WARN] only.
    if command -v git &> /dev/null; then
        success "Git $(git --version | grep -oP '\d+\.\d+\.\d+' | head -1)"
        return 0
    fi
    warn "Git not found — optional for setup; required for clone-and-contribute. Install via https://git-scm.com/downloads"
    return 0
}

check_powershell() {
    # Optional — required for the PowerShell walkthroughs (TradeFinance, AssuredIdentity)
    # but not for the basic gateway+services boot path. Emits a [WARN] only.
    if command -v pwsh &> /dev/null; then
        local pwsh_version
        pwsh_version=$(pwsh --version 2>/dev/null | grep -oP '\d+\.\d+\.\d+' | head -1)
        success "PowerShell ${pwsh_version:-detected}"
        return 0
    fi
    warn "PowerShell 7.5+ not found — optional for setup; required to run walkthroughs/. Install via https://learn.microsoft.com/powershell/scripting/install/installing-powershell"
    return 0
}

check_prerequisites() {
    info "Checking prerequisites..."
    local missing=0

    # Required checks — increment $missing on failure, do not exit early so the
    # operator sees every gap in one pass.
    check_docker_installed       || missing=$((missing + 1))
    check_docker_daemon_running  || missing=$((missing + 1))
    check_docker_compose_v2      || missing=$((missing + 1))
    check_port_available 80      || missing=$((missing + 1))
    check_port_available 443     || missing=$((missing + 1))
    check_port_available 8080    || missing=$((missing + 1))
    check_openssl_or_python      || missing=$((missing + 1))

    # Optional checks — never increment $missing.
    check_git
    check_powershell

    if [ $missing -ne 0 ]; then
        echo ""
        echo "[sorcha-setup] $missing required prerequisite(s) missing — see lines above" >&2
        exit 1
    fi

    echo ""
}

# -----------------------------------------------------------------------------
# Configuration questions
# -----------------------------------------------------------------------------

ask_configuration() {
    echo -e "${BOLD}Configuration${NC}"
    echo "Answer the questions below to configure your Sorcha instance."
    echo "Press Enter to accept the default value shown in brackets."
    echo ""

    # Installation name
    ask "Installation name (hostname or domain)" "localhost" INSTALLATION_NAME

    # JWT key
    local default_jwt_key
    default_jwt_key=$(generate_jwt_key)
    if [ "$QUIET" = true ]; then
        JWT_SIGNING_KEY="$default_jwt_key"
        info "Generated JWT signing key"
    else
        echo ""
        echo -e "${BOLD}JWT Signing Key${NC}"
        echo "A 256-bit key for signing authentication tokens."
        echo "A secure random key has been generated for you."
        ask_yes_no "Use the generated key?" "y" USE_GENERATED_KEY
        if [ "$USE_GENERATED_KEY" = "y" ]; then
            JWT_SIGNING_KEY="$default_jwt_key"
            success "Using generated JWT key"
        else
            ask "Enter your JWT signing key (base64, 32+ bytes)" "" JWT_SIGNING_KEY
        fi
    fi

    # Database credentials
    echo ""
    echo -e "${BOLD}Database Credentials${NC}"
    ask "PostgreSQL username" "sorcha" POSTGRES_USER
    ask "PostgreSQL password" "sorcha_dev_password" POSTGRES_PASSWORD
    ask "MongoDB username" "sorcha" MONGO_USERNAME
    ask "MongoDB password" "sorcha_dev_password" MONGO_PASSWORD

    # Redis
    echo ""
    ask "Redis password (leave empty for no auth)" "" REDIS_PASSWORD

    # Environment
    echo ""
    ask "Environment (Development/Staging/Production)" "Development" ASPNETCORE_ENVIRONMENT

    # AI features
    echo ""
    echo -e "${BOLD}AI Integration (Optional)${NC}"
    echo "Sorcha can use Claude AI for interactive blueprint design."
    ask "Anthropic API key (leave empty to skip)" "" ANTHROPIC_API_KEY

    # Transactional email (Feature 112) — verification / invite / welcome emails.
    echo ""
    echo -e "${BOLD}Transactional Email (Optional)${NC}"
    echo "The Tenant Service sends verification / invite / welcome emails."
    echo "Leave the connection string empty for local dev (emails are skipped, not sent)."
    ask "Azure Communication Services email connection string (leave empty to skip)" "" ACS_EMAIL_CONNECTION_STRING
    if [ -n "$ACS_EMAIL_CONNECTION_STRING" ]; then
        ask "From address for outbound email" "noreply@sorcha.dev" EMAIL_FROM_ADDRESS
    else
        EMAIL_FROM_ADDRESS="noreply@sorcha.dev"
    fi
    EMAIL_FROM_NAME="Sorcha Platform"
    # Base URL used to build links in email bodies — derive from the installation name.
    if [ "$INSTALLATION_NAME" = "localhost" ]; then
        EMAIL_BASE_URL="http://localhost"
    else
        EMAIL_BASE_URL="https://${INSTALLATION_NAME}"
    fi

    # Peer network
    echo ""
    echo -e "${BOLD}Peer Network (Optional)${NC}"
    ask "Peer node ID" "local-peer.sorcha.dev" PEER_NODE_ID
    ask_yes_no "Connect to a seed peer?" "n" HAS_SEED_PEER
    if [ "$HAS_SEED_PEER" = "y" ]; then
        ask "Seed peer node ID" "" SEED_PEER_NODE_ID
        ask "Seed peer hostname/IP" "" SEED_PEER_HOST
        ask "Seed peer port" "50051" SEED_PEER_PORT
    else
        SEED_PEER_NODE_ID=""
        SEED_PEER_HOST=""
        SEED_PEER_PORT="50051"
    fi

    echo ""
}

# -----------------------------------------------------------------------------
# Generate .env file
# -----------------------------------------------------------------------------

write_env_file() {
    info "Generating .env file..."

    # Per-deploy service-to-service auth secrets (issue #1412) — generated
    # fresh unless already present in the environment (e.g. an exported
    # override), mirroring how JWT_SIGNING_KEY is produced above. Read by
    # both the client env (ServiceAuth__ClientSecret) and the Tenant seed
    # (Seed__ServicePrincipals__{clientId}) in docker-compose.yml, so client
    # and server always agree on a value unique to this installation and
    # never committed to source control.
    : "${BLUEPRINT_SERVICE_SECRET:=$(generate_service_secret)}"
    : "${WALLET_SERVICE_SECRET:=$(generate_service_secret)}"
    : "${REGISTER_SERVICE_SECRET:=$(generate_service_secret)}"
    : "${PEER_SERVICE_SECRET:=$(generate_service_secret)}"
    : "${VALIDATOR_SERVICE_SECRET:=$(generate_service_secret)}"
    : "${TENANT_SERVICE_SECRET:=$(generate_service_secret)}"
    : "${HAIP_SERVICE_SECRET:=$(generate_service_secret)}"
    : "${VERIFIER_SERVICE_SECRET:=$(generate_service_secret)}"

    # F191 (#1420): PFX password for the workload-identity certificates provisioned by
    # `sorcha workload-ca init` (see ensure_workload_certs below).
    : "${WORKLOAD_CERT_PASSWORD:=$(generate_service_secret)}"

    # Seed admin password (issues #1409 / #1434). In Development the Tenant
    # Service uses its committed dev default when AdminPassword is empty; in
    # Production/Staging DatabaseInitializer fail-closes at startup unless it is
    # set, so generate a strong one for any non-Development install. It is shown
    # once in the final summary and is not recoverable afterwards.
    SEED_ADMIN_EMAIL="${SEED_ADMIN_EMAIL:-admin@sorcha.local}"
    if [ "$ASPNETCORE_ENVIRONMENT" != "Development" ]; then
        : "${SEED_ADMIN_PASSWORD:=$(generate_service_secret)}"
    fi

    if [ -f "$ENV_FILE" ]; then
        local backup="$ENV_FILE.backup.$(date +%Y%m%d-%H%M%S)"
        cp "$ENV_FILE" "$backup"
        warn "Existing .env backed up to $(basename "$backup")"
    fi

    cat > "$ENV_FILE" << ENVFILE
# Sorcha Platform Configuration
# Generated by sorcha-setup.sh on $(date '+%Y-%m-%d %H:%M:%S')
# DO NOT COMMIT THIS FILE TO SOURCE CONTROL

# Installation Identity
INSTALLATION_NAME=${INSTALLATION_NAME}

# JWT Configuration (256-bit key)
JWT_SIGNING_KEY=${JWT_SIGNING_KEY}

# Database Credentials
POSTGRES_USER=${POSTGRES_USER}
POSTGRES_PASSWORD=${POSTGRES_PASSWORD}
MONGO_USERNAME=${MONGO_USERNAME}
MONGO_PASSWORD=${MONGO_PASSWORD}

# Redis Configuration
REDIS_PASSWORD=${REDIS_PASSWORD}

# Runtime Environment
ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT}

# AI Integration
ANTHROPIC_API_KEY=${ANTHROPIC_API_KEY}

# Transactional Email (Feature 112 — leave the ACS connection string empty for no email in dev)
ACS_EMAIL_CONNECTION_STRING=${ACS_EMAIL_CONNECTION_STRING:-}
EMAIL_BASE_URL=${EMAIL_BASE_URL:-http://localhost}
EMAIL_FROM_ADDRESS=${EMAIL_FROM_ADDRESS:-noreply@sorcha.dev}
EMAIL_FROM_NAME=${EMAIL_FROM_NAME:-Sorcha Platform}

# Social Login OAuth (optional — fill in to enable Google / GitHub sign-in;
# see docs/guides/SOCIAL-LOGIN-SETUP.md)
GOOGLE_OAUTH_CLIENT_ID=${GOOGLE_OAUTH_CLIENT_ID:-}
GOOGLE_OAUTH_CLIENT_SECRET=${GOOGLE_OAUTH_CLIENT_SECRET:-}
GITHUB_OAUTH_CLIENT_ID=${GITHUB_OAUTH_CLIENT_ID:-}
GITHUB_OAUTH_CLIENT_SECRET=${GITHUB_OAUTH_CLIENT_SECRET:-}

# Peer Network
PEER_NODE_ID=${PEER_NODE_ID}
PEER_PUBLIC_ADDRESS=${PEER_PUBLIC_ADDRESS:-}
SEED_PEER_NODE_ID=${SEED_PEER_NODE_ID}
SEED_PEER_HOST=${SEED_PEER_HOST}
SEED_PEER_PORT=${SEED_PEER_PORT}
SEED_PEER_ENABLE_TLS=${SEED_PEER_ENABLE_TLS:-false}

# Per-Deploy Service Authentication Secrets (issue #1412)
# Unique to this installation, generated by sorcha-setup.sh — never a
# committed literal. Consumed by both the client-side ServiceAuth__ClientSecret
# env vars and the Tenant Service's Seed__ServicePrincipals__{clientId} seed
# config in docker-compose.yml, so client and server always agree.
BLUEPRINT_SERVICE_SECRET=${BLUEPRINT_SERVICE_SECRET}
WALLET_SERVICE_SECRET=${WALLET_SERVICE_SECRET}
REGISTER_SERVICE_SECRET=${REGISTER_SERVICE_SECRET}
PEER_SERVICE_SECRET=${PEER_SERVICE_SECRET}
VALIDATOR_SERVICE_SECRET=${VALIDATOR_SERVICE_SECRET}
TENANT_SERVICE_SECRET=${TENANT_SERVICE_SECRET}
HAIP_SERVICE_SECRET=${HAIP_SERVICE_SECRET}
VERIFIER_SERVICE_SECRET=${VERIFIER_SERVICE_SECRET}

# Seed admin credentials (issues #1409 / #1434). Development uses the Tenant
# Service dev default when AdminPassword is empty; Production/Staging fail-closed
# at startup unless it is set, so setup generates one above for non-Development
# installs. Read as Seed:AdminEmail / Seed:AdminPassword by DatabaseInitializer.
SEED_ADMIN_EMAIL=${SEED_ADMIN_EMAIL:-admin@sorcha.local}
SEED_ADMIN_PASSWORD=${SEED_ADMIN_PASSWORD:-}

# Workload-identity certificate password (F191 / #1420). The base64 certificate
# material itself is appended below by ensure_workload_certs.
WORKLOAD_CERT_PASSWORD=${WORKLOAD_CERT_PASSWORD}
ENVFILE

    success ".env file written"
}

# -----------------------------------------------------------------------------
# Dev HTTPS certificate generation
# -----------------------------------------------------------------------------

ensure_dev_cert() {
    # docker-compose mounts ./docker/certs:/https into api-gateway, which is
    # configured to bind https://+:8443 with /https/aspnetapp.pfx. The .pfx
    # files are .gitignored (private keys must never be committed), so a fresh
    # clone has an empty docker/certs/ directory and the api-gateway crashes
    # on startup with FileNotFoundException. This step generates a self-signed
    # dev cert on Linux/macOS using openssl — matches the existing Windows
    # script scripts/generate-dev-cert.ps1. Idempotent.
    local cert_dir="$PROJECT_DIR/docker/certs"
    local pfx="$cert_dir/aspnetapp.pfx"
    local password="SorchaDev2025"   # matches docker-compose api-gateway env

    if [ -f "$pfx" ]; then
        success "Dev certificate already present at $pfx"
        return 0
    fi

    info "Generating self-signed dev certificate for HTTPS..."
    mkdir -p "$cert_dir"

    if ! command -v openssl &> /dev/null; then
        warn "openssl not installed — api-gateway HTTPS endpoint will fail to bind. Install openssl or set ASPNETCORE_URLS=http://+:8080 only."
        return 0
    fi

    local tmp_key tmp_crt tmp_cnf
    tmp_key=$(mktemp)
    tmp_crt=$(mktemp)
    tmp_cnf=$(mktemp)

    cat > "$tmp_cnf" <<'EOF'
[req]
distinguished_name = req_dn
x509_extensions    = v3_ca
prompt             = no
[req_dn]
CN = localhost
[v3_ca]
keyUsage         = critical, digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth
subjectAltName   = @alt_names
[alt_names]
DNS.1 = localhost
DNS.2 = api-gateway
DNS.3 = sorcha-ui-web
IP.1  = 127.0.0.1
EOF

    openssl req -x509 -newkey rsa:2048 -days 730 -nodes \
        -keyout "$tmp_key" -out "$tmp_crt" -config "$tmp_cnf" \
        -extensions v3_ca > /dev/null 2>&1

    openssl pkcs12 -export -out "$pfx" \
        -inkey "$tmp_key" -in "$tmp_crt" \
        -password "pass:$password" > /dev/null 2>&1

    rm -f "$tmp_key" "$tmp_crt" "$tmp_cnf"

    if [ ! -f "$pfx" ]; then
        warn "openssl did not produce $pfx — api-gateway HTTPS bind will fail"
        return 0
    fi

    # openssl creates the .pfx 0600 (owner-only). The api-gateway / ui-web
    # containers run hardened as a non-root uid and mount ./docker/certs read-only,
    # so a 0600 file owned by the host user is unreadable inside the container —
    # the gateway then dies at startup with "Access to '/https/aspnetapp.pfx' is
    # denied". Make the dev cert world-readable (it is a throwaway self-signed
    # dev key, never a production secret).
    chmod 0644 "$pfx" 2>/dev/null || true

    success "Generated dev certificate at $pfx"
}

# -----------------------------------------------------------------------------
# Workload-identity certificates (F191 / #1420)
# -----------------------------------------------------------------------------

# Base64-encode a file to one line (GNU base64 has -w0; macOS/BSD base64 does not).
b64_oneline() {
    base64 -w0 "$1" 2>/dev/null || base64 "$1" | tr -d '\n'
}

ensure_workload_certs() {
    # Provision the per-installation Workload CA + per-service certificates via the CLI
    # (never duplicated cert logic here), then deliver them into .env as base64 —
    # the same per-deploy delivery model as the service secrets. docker-compose reads
    # them via ${X_WORKLOAD_CERT:-} with EMPTY defaults, so a failure here degrades to
    # the shared-secret path (loudly), never to a broken deployment.
    local cert_dir="$PROJECT_DIR/config/workload-certs"
    mkdir -p "$cert_dir"

    # Password: from the environment, else the freshly-written .env (keep-existing path).
    if [ -z "${WORKLOAD_CERT_PASSWORD:-}" ] && [ -f "$ENV_FILE" ]; then
        WORKLOAD_CERT_PASSWORD=$(grep '^WORKLOAD_CERT_PASSWORD=' "$ENV_FILE" | head -1 | cut -d= -f2-)
    fi
    if [ -z "${WORKLOAD_CERT_PASSWORD:-}" ]; then
        warn "WORKLOAD_CERT_PASSWORD not available (older .env?) — skipping workload certificates; services will authenticate with shared secrets. Re-run setup and overwrite .env to enable certificate auth."
        return 0
    fi

    info "Provisioning workload-identity certificates (sorcha workload-ca init)..."
    local provisioned=false
    if command -v sorcha &> /dev/null; then
        if sorcha workload-ca init --dir "$cert_dir" --installation "$INSTALLATION_NAME" --password "$WORKLOAD_CERT_PASSWORD"; then
            provisioned=true
        fi
    fi
    if [ "$provisioned" = false ]; then
        if docker run --rm -v "$cert_dir":/certs sorchadev/cli:latest \
            workload-ca init --dir /certs --installation "$INSTALLATION_NAME" --password "$WORKLOAD_CERT_PASSWORD"; then
            provisioned=true
        fi
    fi
    if [ "$provisioned" = false ]; then
        warn "Could not run 'sorcha workload-ca' (no local CLI and the sorchadev/cli image is unavailable) — skipping workload certificates; services will authenticate with shared secrets."
        return 0
    fi

    # Verify the expected artifacts exist before wiring them into .env.
    local bundle="$cert_dir/ca/bundle.pem"
    local server_pfx="$cert_dir/server/tenant-service.pfx"
    if [ ! -f "$bundle" ] || [ ! -f "$server_pfx" ]; then
        warn "workload-ca init did not produce the expected artifacts — skipping .env delivery"
        return 0
    fi

    # Replace any previous generated block, then append the fresh one (idempotent re-runs).
    if grep -q '^# --- F191 workload identity' "$ENV_FILE" 2>/dev/null; then
        sed -i.bak '/^# --- F191 workload identity/,/^# --- end F191 workload identity/d' "$ENV_FILE" && rm -f "$ENV_FILE.bak"
    fi

    {
        echo "# --- F191 workload identity (generated by sorcha-setup.sh; do not edit) ---"
        echo "WORKLOAD_TRUST_BUNDLE=$(b64_oneline "$bundle")"
        echo "TENANT_WORKLOAD_SERVER_CERT=$(b64_oneline "$server_pfx")"
        local svc pfx
        for svc in \
            "BLUEPRINT:service-blueprint" \
            "WALLET:service-wallet" \
            "REGISTER:register-service" \
            "PEER:service-peer" \
            "VALIDATOR:validator-service" \
            "TENANT:tenant-service" \
            "HAIP:service-haip" \
            "VERIFIER:service-verifier"; do
            pfx="$cert_dir/services/${svc#*:}.pfx"
            if [ -f "$pfx" ]; then
                echo "${svc%%:*}_WORKLOAD_CERT=$(b64_oneline "$pfx")"
            fi
        done
        echo "# --- end F191 workload identity ---"
    } >> "$ENV_FILE"

    success "Workload certificates provisioned ($cert_dir) and delivered via .env"
}

# -----------------------------------------------------------------------------
# Pull and start services
# -----------------------------------------------------------------------------

pull_images() {
    info "Pulling latest Docker images..."
    cd "$PROJECT_DIR"

    if docker compose pull 2>/dev/null; then
        success "Images pulled"
    elif docker-compose pull 2>/dev/null; then
        success "Images pulled"
    else
        warn "Could not pull images — will build locally"
    fi
}

start_services() {
    info "Starting Sorcha services..."
    cd "$PROJECT_DIR"

    if docker compose up -d 2>/dev/null; then
        true
    elif docker-compose up -d 2>/dev/null; then
        true
    else
        error "Failed to start services"
        exit 1
    fi

    success "Services started"
}

wait_for_health() {
    info "Waiting for services to be ready..."
    local max_attempts=30
    local attempt=0

    while [ $attempt -lt $max_attempts ]; do
        if curl -sf http://localhost/api/health > /dev/null 2>&1; then
            success "All services healthy"
            return 0
        fi
        attempt=$((attempt + 1))
        echo -ne "\r  Waiting... ($attempt/$max_attempts)"
        sleep 2
    done

    echo ""
    warn "Some services may still be starting. Check: docker compose logs -f"
    return 1
}

# -----------------------------------------------------------------------------
# Print summary
# -----------------------------------------------------------------------------

print_summary() {
    echo ""
    echo -e "${CYAN}╔══════════════════════════════════════════════════════════╗${NC}"
    echo -e "${CYAN}║${NC}  ${GREEN}${BOLD}Setup Complete!${NC}                                        ${CYAN}║${NC}"
    echo -e "${CYAN}╚══════════════════════════════════════════════════════════╝${NC}"
    echo ""
    echo -e "${BOLD}Access Points:${NC}"
    echo -e "  Sorcha UI          ${CYAN}http://localhost/app${NC}"
    echo -e "  API Gateway        ${CYAN}http://localhost/${NC}"
    echo -e "  API Documentation  ${CYAN}http://localhost/scalar/${NC}"
    echo -e "  Health Check       ${CYAN}http://localhost/api/health${NC}"
    echo -e "  Aspire Dashboard   ${CYAN}http://localhost:18888${NC}"
    echo ""
    echo -e "${BOLD}Default Login:${NC}"
    if [ "$ASPNETCORE_ENVIRONMENT" != "Development" ] && [ -n "${SEED_ADMIN_PASSWORD:-}" ]; then
        echo -e "  Email:     ${CYAN}${SEED_ADMIN_EMAIL:-admin@sorcha.local}${NC}"
        echo -e "  Password:  ${CYAN}${SEED_ADMIN_PASSWORD}${NC}"
        echo -e "             (generated for this ${ASPNETCORE_ENVIRONMENT} install — save it now;"
        echo -e "              it is not recoverable. Change it on first login.)"
    else
        echo -e "  Email:     ${CYAN}admin@sorcha.local${NC}"
        echo -e "  Password:  ${CYAN}Dev_Pass_2025!${NC}  (development default)"
    fi
    echo ""
    echo -e "${BOLD}Useful Commands:${NC}"
    echo -e "  View logs:         ${CYAN}docker compose logs -f${NC}"
    echo -e "  Stop services:     ${CYAN}docker compose down${NC}"
    echo -e "  Restart services:  ${CYAN}docker compose restart${NC}"
    echo -e "  Full reset:        ${CYAN}docker compose down -v && docker compose up -d${NC}"
    echo ""
    echo -e "${BOLD}Documentation:${NC}"
    echo -e "  README             ${CYAN}README.md${NC}"
    echo -e "  Docker Guide       ${CYAN}docs/DOCKER-QUICK-START.md${NC}"
    echo -e "  Authentication     ${CYAN}docs/AUTHENTICATION-SETUP.md${NC}"
    echo -e "  Development        ${CYAN}DEVELOPMENT.md${NC}"
    echo ""
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------

main() {
    banner
    check_prerequisites

    if [ "$QUIET" = false ] && [ -f "$ENV_FILE" ]; then
        warn "An .env file already exists."
        ask_yes_no "Overwrite with new configuration?" "n" OVERWRITE
        if [ "$OVERWRITE" != "y" ]; then
            ensure_dev_cert
            ensure_workload_certs
            if [ "$CONFIG_ONLY" = true ]; then
                info "Keeping existing .env. Configuration refreshed; not starting (--config-only)."
                exit 0
            fi
            info "Keeping existing .env. Starting services..."
            start_services
            wait_for_health
            print_summary
            exit 0
        fi
    fi

    ask_configuration
    write_env_file
    ensure_dev_cert
    ensure_workload_certs

    # --config-only stops here, with .env and the F191 workload certificates in place.
    # This is the whole reason the flag exists: cert provisioning (ensure_workload_certs)
    # is the one step a node cannot sensibly hand-roll, and before this flag a node that
    # starts its own compose stack had no way to reach it without also triggering a bare
    # `docker compose up -d` against the base file. n1 grew a bespoke on-box
    # f191-provision.sh for exactly that reason; this is the supported route instead.
    if [ "$CONFIG_ONLY" = true ]; then
        echo ""
        success "Configuration complete (--config-only): .env and workload certificates are in place."
        echo "  Bring the stack up with this node's own compose file set, e.g.:"
        echo "    docker compose -f docker-compose.yml -f docker-compose.n1.yml \\"
        echo "                   -f docker-compose.seed.yml -f docker-compose.ports.yml up -d"
        exit 0
    fi

    pull_images
    start_services
    if ! wait_for_health; then
        # Audit T010 finding — main() previously ignored wait_for_health's
        # return code, so the success summary printed even when /api/health
        # never came up. Honour the return code and exit non-zero so an
        # AI-driven setup detects the failure (FR-032).
        echo ""
        error "Gateway did not become healthy in the allotted window. Inspect 'docker compose logs -f' for detail."
        exit 1
    fi
    print_summary
    echo ""
    echo "[sorcha-setup] success — gateway reachable at http://localhost. Verify with: curl -s http://localhost/api/health"
}

# T091 — when sourced for unit tests (bats) with SORCHA_SETUP_LIB_ONLY=1,
# define helpers but skip main() so the test runner can invoke them in isolation.
if [ "${SORCHA_SETUP_LIB_ONLY:-0}" != "1" ]; then
    main "$@"
fi
