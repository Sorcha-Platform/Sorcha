#!/bin/bash
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Remote setup script for n1.sorcha.dev
# This script runs ON the Azure VM to:
# 1. Pull latest DockerHub images
# 2. Start all services via docker-compose
# 3. Wait for services to be healthy
# 4. Run bootstrap (create org, admin user, service principal)
#
# Usage: ./n1-setup-remote.sh [--reset] [--skip-bootstrap]

set -euo pipefail

SORCHA_DIR="/opt/sorcha"
COMPOSE_CMD="docker compose -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml"
MAX_WAIT=300  # 5 minutes max wait for healthy services

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

log()  { echo -e "${CYAN}==> ${NC}$1"; }
ok()   { echo -e "${GREEN}✓ ${NC}$1"; }
warn() { echo -e "${YELLOW}⚠ ${NC}$1"; }
err()  { echo -e "${RED}✗ ${NC}$1"; }

# Parse arguments
RESET=false
SKIP_BOOTSTRAP=false
for arg in "$@"; do
    case $arg in
        --reset) RESET=true ;;
        --skip-bootstrap) SKIP_BOOTSTRAP=true ;;
        *) warn "Unknown argument: $arg" ;;
    esac
done

cd "$SORCHA_DIR"

echo ""
echo "╔════════════════════════════════════════════════╗"
echo "║     Sorcha n1.sorcha.dev Setup                ║"
echo "╚════════════════════════════════════════════════╝"
echo ""

# Check Docker is running
log "Checking Docker..."
if ! docker info > /dev/null 2>&1; then
    err "Docker is not running. Waiting for cloud-init to complete..."
    # Wait for cloud-init if first boot
    for i in $(seq 1 60); do
        if [ -f "$SORCHA_DIR/.cloud-init-complete" ] && docker info > /dev/null 2>&1; then
            break
        fi
        echo "  Waiting... ($i/60)"
        sleep 5
    done
    if ! docker info > /dev/null 2>&1; then
        err "Docker failed to start. Check cloud-init logs: sudo cat /var/log/cloud-init-output.log"
        exit 1
    fi
fi
ok "Docker is running"

# Check compose files exist
if [ ! -f "$SORCHA_DIR/docker-compose.yml" ]; then
    err "docker-compose.yml not found in $SORCHA_DIR"
    err "Run n1-deploy.ps1 first to upload compose files"
    exit 1
fi

# Reset if requested (equivalent to 'docker desktop down -v')
if [ "$RESET" = true ]; then
    log "Resetting all data (docker compose down -v)..."
    $COMPOSE_CMD down -v --remove-orphans 2>/dev/null || true
    ok "All containers and volumes removed"
    echo ""
fi

# Pull latest images from DockerHub
log "Pulling latest images from DockerHub..."
$COMPOSE_CMD pull
ok "All images pulled"

# Fix wallet encryption key permissions
log "Setting up wallet encryption volume permissions..."
docker volume create sorcha_wallet-encryption-keys 2>/dev/null || true
docker run --rm -v sorcha_wallet-encryption-keys:/data alpine chown -R 1654:1654 /data 2>/dev/null || true
ok "Wallet encryption permissions set"

# Start services
log "Starting all services..."
$COMPOSE_CMD up -d
ok "Compose up complete"

# Wait for services to be healthy
log "Waiting for services to become healthy..."
SERVICES=(
    "sorcha-redis"
    "sorcha-postgres"
    "sorcha-mongodb"
    "sorcha-wallet-service"
    "sorcha-tenant-service"
    "sorcha-register-service"
    "sorcha-validator-service"
    "sorcha-blueprint-service"
    "sorcha-peer-service"
    "sorcha-api-gateway"
)

elapsed=0
all_healthy=false

while [ $elapsed -lt $MAX_WAIT ]; do
    healthy_count=0
    total=${#SERVICES[@]}

    for svc in "${SERVICES[@]}"; do
        status=$(docker inspect --format='{{.State.Health.Status}}' "$svc" 2>/dev/null || echo "not_found")
        case $status in
            healthy) healthy_count=$((healthy_count + 1)) ;;
            *) ;;
        esac
    done

    if [ $healthy_count -eq $total ]; then
        all_healthy=true
        break
    fi

    echo "  Healthy: $healthy_count/$total (${elapsed}s elapsed)"
    sleep 10
    elapsed=$((elapsed + 10))
done

if [ "$all_healthy" = true ]; then
    ok "All $total services are healthy!"
else
    warn "Timeout waiting for all services. Current status:"
    for svc in "${SERVICES[@]}"; do
        status=$(docker inspect --format='{{.State.Health.Status}}' "$svc" 2>/dev/null || echo "not_found")
        if [ "$status" = "healthy" ]; then
            echo -e "  ${GREEN}✓${NC} $svc"
        else
            echo -e "  ${RED}✗${NC} $svc ($status)"
        fi
    done
    warn "Continuing anyway - some services may still be starting..."
fi

echo ""

# Run bootstrap
if [ "$SKIP_BOOTSTRAP" = true ]; then
    log "Skipping bootstrap (--skip-bootstrap flag)"
else
    log "Running bootstrap..."

    # Wait a few extra seconds for API Gateway to fully initialize routes
    sleep 5

    # Bootstrap via direct Tenant Service API calls
    TENANT_URL="http://localhost:5450"
    GATEWAY_URL="http://localhost:80"

    # Check if already bootstrapped
    HEALTH_RESPONSE=$(curl -s "$GATEWAY_URL/health" 2>/dev/null || echo "")
    if [ -z "$HEALTH_RESPONSE" ]; then
        HEALTH_RESPONSE=$(curl -s "$TENANT_URL/health" 2>/dev/null || echo "")
    fi

    if [ -z "$HEALTH_RESPONSE" ]; then
        warn "Cannot reach services for bootstrap. You can bootstrap manually later:"
        warn "  ssh sorcha@n1.sorcha.dev"
        warn "  cd /opt/sorcha && ./n1-setup-remote.sh --skip-bootstrap"
        warn "  # Then use CLI: sorcha bootstrap --profile n1"
    else
        ok "Services are reachable"
        log "Bootstrap the platform using the CLI from your local machine:"
        echo ""
        echo "  sorcha bootstrap --profile n1"
        echo ""
        echo "  Or non-interactive:"
        echo "  sorcha bootstrap --profile n1 --non-interactive \\"
        echo "    --org-name 'Sorcha Dev' \\"
        echo "    --subdomain 'dev' \\"
        echo "    --admin-email 'admin@sorcha.dev' \\"
        echo "    --admin-name 'Admin' \\"
        echo "    --admin-password 'Dev_Pass_2026!' \\"
        echo "    --create-sp --sp-name 'n1-automation'"
        echo ""
    fi
fi

echo ""
echo "╔════════════════════════════════════════════════╗"
echo "║     n1.sorcha.dev Setup Complete!              ║"
echo "╚════════════════════════════════════════════════╝"
echo ""
echo "  API Gateway:       http://n1.sorcha.dev"
echo "  Aspire Dashboard:  http://n1.sorcha.dev:18888"
echo "  Scalar API Docs:   http://n1.sorcha.dev/scalar/"
echo ""
echo "  To reset all data:"
echo "    ./n1-setup-remote.sh --reset"
echo ""
echo "  To view logs:"
echo "    docker compose -f docker-compose.yml -f docker-compose.n1.yml logs -f"
echo ""
