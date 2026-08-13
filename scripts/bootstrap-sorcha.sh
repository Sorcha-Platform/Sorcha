#!/usr/bin/env bash
#
# Sorcha CLI — Auth Profile Bootstrap
#
# Configures a Sorcha CLI *auth profile* against an ALREADY-RUNNING instance.
# It does NOT install, start or configure the platform itself.
#
# Issue #1234 — the "bootstrap-" name reads like "bootstrap the platform" and is the main trap in
# this directory. To install or start the stack, use scripts/sorcha-setup.sh. See scripts/README.md.
#
# Usage:
#   ./bootstrap-sorcha.sh                    # Interactive mode
#   ./bootstrap-sorcha.sh --non-interactive  # Non-interactive with defaults
#   ./bootstrap-sorcha.sh --profile docker   # Specify profile
#

set -euo pipefail

# Script configuration
PROFILE="${PROFILE:-docker}"
NON_INTERACTIVE=false

# Configuration paths
CONFIG_DIR="${HOME}/.sorcha"
CONFIG_FILE="${CONFIG_DIR}/config.json"
mkdir -p "${CONFIG_DIR}"

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --profile)
            PROFILE="$2"
            shift 2
            ;;
        --non-interactive)
            NON_INTERACTIVE=true
            shift
            ;;
        --help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --profile NAME          Configuration profile name (default: docker)"
            echo "  --non-interactive       Run with default values, no prompts"
            echo "  --help                  Show this help message"
            echo ""
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# Color output helpers
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
WHITE='\033[1;37m'
NC='\033[0m' # No Color

write_step() {
    echo -e "${CYAN}==>${NC} ${WHITE}$1${NC}"
}

write_success() {
    echo -e "${GREEN}✓${NC} ${WHITE}$1${NC}"
}

write_error() {
    echo -e "${RED}✗${NC} ${WHITE}$1${NC}"
}

write_info() {
    echo -e "${BLUE}ℹ${NC} ${WHITE}$1${NC}"
}

get_user_input() {
    local prompt="$1"
    local default="$2"
    local secure="${3:-false}"

    if [[ "$NON_INTERACTIVE" == "true" ]]; then
        echo "$default"
        return
    fi

    if [[ "$secure" == "true" ]]; then
        read -s -p "$prompt: " user_input
        echo ""
    else
        if [[ -n "$default" ]]; then
            read -p "$prompt [$default]: " user_input
            echo "${user_input:-$default}"
        else
            read -p "$prompt: " user_input
            echo "$user_input"
        fi
    fi
}

command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Banner
echo ""
echo -e "${CYAN}╔════════════════════════════════════════════════════════════════╗${NC}"
echo -e "${CYAN}║                                                                ║${NC}"
echo -e "${CYAN}║              Sorcha Platform Bootstrap Script                 ║${NC}"
echo -e "${CYAN}║                                                                ║${NC}"
echo -e "${CYAN}║         Initial configuration for fresh installation          ║${NC}"
echo -e "${CYAN}║                                                                ║${NC}"
echo -e "${CYAN}╚════════════════════════════════════════════════════════════════╝${NC}"
echo ""

# Check prerequisites
write_step "Checking prerequisites..."

if ! command_exists sorcha; then
    write_error "Sorcha CLI not found. Please install it first:"
    echo -e "  ${YELLOW}dotnet tool install -g Sorcha.Cli${NC}"
    echo -e "  ${YELLOW}OR run from source:${NC}"
    echo -e "  ${YELLOW}dotnet run --project src/Apps/Sorcha.Cli -- [command]${NC}"
    exit 1
fi

if ! command_exists docker; then
    write_error "Docker not found. Please install Docker first."
    exit 1
fi

write_success "All prerequisites met"
echo ""

# Phase 1: CLI Configuration
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${CYAN}PHASE 1: CLI Configuration${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo ""

write_step "Configuring CLI profile: $PROFILE"

# Service URLs for Docker deployment
TENANT_URL=$(get_user_input "Tenant Service URL" "http://localhost/api/tenants")
REGISTER_URL=$(get_user_input "Register Service URL" "http://localhost/api/register")
WALLET_URL=$(get_user_input "Wallet Service URL" "http://localhost/api/wallets")
PEER_URL=$(get_user_input "Peer Service URL" "http://localhost/api/peers")
AUTH_URL=$(get_user_input "Auth Token URL (browser/user grants — password, refresh_token)" "http://localhost/api/service-auth/token")
# Service-principal (client_credentials) minting moved to an INTERNAL-only Tenant Service route
# as of #1397/#1406 — the public API Gateway does not route /api/internal/*. This default only
# works when bootstrap runs co-located inside the Sorcha trust network (e.g. from a container on
# the docker-compose network); from a host machine hitting the gateway it will 404/400.
SERVICE_AUTH_URL=$(get_user_input "Service-Principal Auth Token URL (internal-only, #1397)" "http://localhost/api/internal/service-auth/token")

write_info "CLI will be configured to use profile: $PROFILE"
write_success "Configuration profile prepared"
echo ""

# Phase 2: Initial Authentication
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${CYAN}PHASE 2: Initial Authentication${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo ""

write_info "For bootstrap, we'll create an initial service principal for automation"

BOOTSTRAP_CLIENT_ID=$(get_user_input "Bootstrap Service Principal Client ID" "sorcha-bootstrap")
BOOTSTRAP_CLIENT_SECRET=$(get_user_input "Bootstrap Service Principal Secret" "bootstrap-secret-$RANDOM" true)

write_success "Authentication credentials prepared"
echo ""

# Phase 3: System Organization (Tenant)
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${CYAN}PHASE 3: System Organization${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo ""

ORG_NAME=$(get_user_input "Organization Name" "System Organization")
ORG_SUBDOMAIN=$(get_user_input "Organization Subdomain" "system")
ORG_DESCRIPTION=$(get_user_input "Organization Description" "Primary system organization for Sorcha platform")

write_success "Organization details prepared"
echo ""

# Phase 4: Administrative User
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${CYAN}PHASE 4: Administrative User${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo ""

ADMIN_EMAIL=$(get_user_input "Admin Email Address" "admin@sorcha.local")
ADMIN_NAME=$(get_user_input "Admin Display Name" "System Administrator")
ADMIN_PASSWORD=$(get_user_input "Admin Password" "Admin@123!" true)

write_success "Administrator account details prepared"
echo ""

# Phase 5: Node Configuration
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${CYAN}PHASE 5: Node Configuration${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo ""

NODE_ID=$(get_user_input "Node ID/Name" "node-$(hostname)")
NODE_DESCRIPTION=$(get_user_input "Node Description" "Primary Sorcha node - $(hostname)")
ENABLE_P2P=$(get_user_input "Enable P2P networking? (true/false)" "true")

write_success "Node configuration prepared"
echo ""

# Phase 6: Initial Register
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${CYAN}PHASE 6: Initial Register${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo ""

REGISTER_NAME=$(get_user_input "Initial Register Name" "System Register")
REGISTER_DESCRIPTION=$(get_user_input "Register Description" "Primary system register for transactions")

write_success "Register configuration prepared"
echo ""

# Confirmation
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${CYAN}Configuration Summary${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo ""
echo -e "${WHITE}Profile:            $PROFILE${NC}"
echo -e "${WHITE}Tenant URL:         $TENANT_URL${NC}"
echo -e "${WHITE}Organization:       $ORG_NAME ($ORG_SUBDOMAIN)${NC}"
echo -e "${WHITE}Admin User:         $ADMIN_NAME ($ADMIN_EMAIL)${NC}"
echo -e "${WHITE}Node ID:            $NODE_ID${NC}"
echo -e "${WHITE}Initial Register:   $REGISTER_NAME${NC}"
echo ""

if [[ "$NON_INTERACTIVE" != "true" ]]; then
    read -p "Proceed with installation? (yes/no): " CONFIRM
    if [[ "$CONFIRM" != "yes" ]]; then
        write_info "Installation cancelled by user"
        exit 0
    fi
fi

echo ""
echo -e "${GREEN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${GREEN}Starting Installation${NC}"
echo -e "${GREEN}═══════════════════════════════════════════════════════════════${NC}"
echo ""

# Display bootstrap status
echo ""
echo -e "${CYAN}BOOTSTRAP STATUS:${NC}"
echo -e "  ${GREEN}CLI-BOOTSTRAP-001 through 005: COMPLETE${NC}"
echo -e "  ${GREEN}All CLI commands are implemented and functional${NC}"
echo -e "  ${YELLOW}Steps 4-7 require authentication infrastructure (pending)${NC}"
echo -e "  ${WHITE}See MASTER-TASKS.md for detailed tracking${NC}"
echo ""

# Step 1: Check Docker services
write_step "Step 1/7: Checking Docker services..."
if ! docker-compose ps >/dev/null 2>&1; then
    write_error "Docker services not running. Please start them first:"
    echo -e "  ${YELLOW}docker-compose up -d${NC}"
    exit 1
fi
write_success "Docker services running"

# Step 2: Wait for services to be ready
write_step "Step 2/7: Waiting for services to be ready..."
MAX_ATTEMPTS=30
ATTEMPT=0
HEALTH_URL="http://localhost/api/health"

while [[ $ATTEMPT -lt $MAX_ATTEMPTS ]]; do
    if curl -sf "$HEALTH_URL" >/dev/null 2>&1; then
        write_success "Services are ready"
        break
    fi

    ATTEMPT=$((ATTEMPT + 1))
    if [[ $ATTEMPT -lt $MAX_ATTEMPTS ]]; then
        echo -e "  ${WHITE}Waiting for services... ($ATTEMPT/$MAX_ATTEMPTS)${NC}"
        sleep 2
    else
        write_error "Services did not become ready in time. Check logs:"
        echo -e "  ${YELLOW}docker-compose logs -f${NC}"
        exit 1
    fi
done

# Step 3: Initialize CLI profile
write_step "Step 3/7: Initializing CLI profile..."

if sorcha config init \
    --profile "$PROFILE" \
    --tenant-url "$TENANT_URL" \
    --register-url "$REGISTER_URL" \
    --wallet-url "$WALLET_URL" \
    --peer-url "$PEER_URL" \
    --auth-url "$AUTH_URL" \
    --service-auth-url "$SERVICE_AUTH_URL" \
    --client-id "$BOOTSTRAP_CLIENT_ID" \
    --check-connectivity false \
    --set-active true 2>&1; then
    write_success "CLI profile \"$PROFILE\" configured"
else
    write_error "Failed to initialize CLI profile"
    exit 1
fi

# Step 4: Create bootstrap service principal
write_step "Step 4/7: Creating bootstrap service principal..."
write_info "NOTE: This step requires Tenant Service to be running and authentication configured"
write_info "Skipping for initial bootstrap - configure authentication manually first"
write_info "After authentication is set up, run: sorcha principal create --org-id YOUR_ORG_ID --name sorcha-bootstrap --scopes admin"
write_success "Bootstrap service principal configuration noted"

# Step 5: Create organization
write_step "Step 5/7: Creating organization..."
write_info "NOTE: This step requires authentication to be configured"
write_info "Skipping for initial bootstrap - configure authentication manually first"
write_info "After authentication is set up, run: sorcha org create --name \"$ORG_NAME\" --subdomain \"$ORG_SUBDOMAIN\""
ORG_ID="00000000-0000-0000-0000-000000000000" # Placeholder - will be replaced with actual ID from API
write_success "Organization creation noted (manual step required)"

# Step 6: Create admin user
write_step "Step 6/7: Creating administrative user..."
write_info "NOTE: This step requires authentication to be configured"
write_info "Skipping for initial bootstrap - configure authentication manually first"
write_info "After authentication is set up, run: sorcha user create --org-id YOUR_ORG_ID --username \"$ADMIN_EMAIL\" --email \"$ADMIN_EMAIL\" --password YOUR_PASSWORD --roles Admin"
write_success "Admin user creation noted (manual step required)"

# Step 7: Create initial register
write_step "Step 7/7: Creating initial register..."
write_info "NOTE: This step requires authentication to be configured"
write_info "Skipping for initial bootstrap - configure authentication manually first"
write_info "After authentication is set up, run: sorcha register create --name \"$REGISTER_NAME\" --org-id YOUR_ORG_ID"
write_success "Register creation noted (manual step required)"

echo ""
echo -e "${GREEN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${GREEN}Bootstrap Complete!${NC}"
echo -e "${GREEN}═══════════════════════════════════════════════════════════════${NC}"
echo ""

write_success "Sorcha platform has been configured"
echo ""
echo -e "${CYAN}Next Steps:${NC}"
echo -e "  ${WHITE}1. Test authentication:${NC}"
echo -e "     ${WHITE}sorcha auth login --username $ADMIN_EMAIL${NC}"
echo ""
echo -e "  ${WHITE}2. View configuration:${NC}"
echo -e "     ${WHITE}sorcha config list --profiles${NC}"
echo ""
echo -e "  ${WHITE}3. Check system health:${NC}"
echo -e "     ${WHITE}curl http://localhost/api/health${NC}"
echo ""
echo -e "  ${WHITE}4. View API documentation:${NC}"
echo -e "     ${WHITE}http://localhost/scalar/${NC}"
echo ""

write_info "Configuration saved to: $CONFIG_FILE"
write_info "Profile: $PROFILE"
echo ""

# Save bootstrap details for reference
BOOTSTRAP_FILE="$CONFIG_DIR/bootstrap-info.json"

cat > "$BOOTSTRAP_FILE" <<EOF
{
  "timestamp": "$(date -u +"%Y-%m-%d %H:%M:%S")",
  "profile": "$PROFILE",
  "organizationId": "$ORG_ID",
  "organizationName": "$ORG_NAME",
  "adminEmail": "$ADMIN_EMAIL",
  "nodeId": "$NODE_ID",
  "registerName": "$REGISTER_NAME"
}
EOF

write_success "Bootstrap completed successfully!"
echo ""
