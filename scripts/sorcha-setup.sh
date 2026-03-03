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
for arg in "$@"; do
    case $arg in
        --quiet|-q) QUIET=true ;;
        --help|-h)
            echo "Usage: $0 [--quiet|-q] [--help|-h]"
            echo "  --quiet, -q  Use all defaults without prompting"
            echo "  --help, -h   Show this help message"
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

# -----------------------------------------------------------------------------
# Prerequisite checks
# -----------------------------------------------------------------------------

check_prerequisites() {
    info "Checking prerequisites..."
    local missing=0

    # Docker
    if command -v docker &> /dev/null; then
        local docker_version
        docker_version=$(docker --version 2>/dev/null | grep -oP '\d+\.\d+\.\d+' | head -1)
        success "Docker $docker_version"
    else
        error "Docker is not installed. Get it at https://docker.com/products/docker-desktop"
        missing=1
    fi

    # Docker Compose
    if docker compose version &> /dev/null; then
        local compose_version
        compose_version=$(docker compose version 2>/dev/null | grep -oP '\d+\.\d+\.\d+' | head -1)
        success "Docker Compose $compose_version"
    elif command -v docker-compose &> /dev/null; then
        success "Docker Compose (standalone)"
    else
        error "Docker Compose is not available"
        missing=1
    fi

    # Docker running
    if docker info &> /dev/null 2>&1; then
        success "Docker daemon is running"
    else
        error "Docker daemon is not running. Start Docker Desktop first."
        missing=1
    fi

    # Git (optional but useful)
    if command -v git &> /dev/null; then
        success "Git $(git --version | grep -oP '\d+\.\d+\.\d+' | head -1)"
    else
        warn "Git not found (optional, needed for development)"
    fi

    if [ $missing -ne 0 ]; then
        echo ""
        error "Missing prerequisites. Install the items above and re-run this script."
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

# Peer Network
PEER_NODE_ID=${PEER_NODE_ID}
SEED_PEER_NODE_ID=${SEED_PEER_NODE_ID}
SEED_PEER_HOST=${SEED_PEER_HOST}
SEED_PEER_PORT=${SEED_PEER_PORT}
ENVFILE

    success ".env file written"
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
    echo -e "  Email:     ${CYAN}admin@sorcha.local${NC}"
    echo -e "  Password:  ${CYAN}Dev_Pass_2025!${NC}"
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
            info "Keeping existing .env. Starting services..."
            start_services
            wait_for_health
            print_summary
            exit 0
        fi
    fi

    ask_configuration
    write_env_file
    pull_images
    start_services
    wait_for_health
    print_summary
}

main "$@"
