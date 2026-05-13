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
#                             [--import-validator-key-file=PATH] [--validator-id=ID]
#
# --import-validator-key-file=PATH    Recover the system wallet from a
#   genesis-validator-key.json BEFORE the rest of the stack comes up. The
#   wallet-service is the only container brought up first; the mnemonic is
#   POSTed to /api/v1/wallets/system/recover; then the rest of the stack
#   starts. Implies --reset. The key file is shredded after import.
#   This is the safe ordering for federation seed nodes — without it,
#   validator-service may auto-generate the wrong system wallet (issue #461 trap).
#
# --validator-id=ID    Validator ID to bind the recovered system wallet to.
#   Must match Validator__ValidatorId on validator-service (default: local-validator).

set -euo pipefail

SORCHA_DIR="/opt/sorcha"
COMPOSE_CMD="docker compose -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml"
MAX_WAIT=300  # 5 minutes max wait for healthy services
COMPOSE_NETWORK="sorcha_sorcha-network"  # network for one-shot curl during wallet import

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
IMPORT_KEY_FILE=""
VALIDATOR_ID="local-validator"
for arg in "$@"; do
    case $arg in
        --reset) RESET=true ;;
        --skip-bootstrap) SKIP_BOOTSTRAP=true ;;
        --import-validator-key-file=*) IMPORT_KEY_FILE="${arg#*=}" ;;
        --validator-id=*) VALIDATOR_ID="${arg#*=}" ;;
        *) warn "Unknown argument: $arg" ;;
    esac
done

# --import-validator-key-file implies --reset; a fresh wallet recovery
# only makes sense against a wiped data dir.
if [ -n "$IMPORT_KEY_FILE" ]; then
    if [ "$RESET" != true ]; then
        log "--import-validator-key-file implies --reset; enabling reset"
        RESET=true
    fi
    if [ ! -f "$IMPORT_KEY_FILE" ]; then
        err "Key file not found: $IMPORT_KEY_FILE"
        exit 1
    fi
    # Sanity-check the JSON has a mnemonic field before we wipe data.
    # We don't echo anything from the file — the validator below either
    # exits 0 (mnemonic present) or non-zero (missing/invalid JSON).
    if ! python3 -c "import json,sys; d=json.load(open(sys.argv[1])); m=d['mnemonic']; sys.exit(0 if isinstance(m,str) and len(m.split())>=12 else 1)" "$IMPORT_KEY_FILE" 2>/dev/null; then
        err "Key file does not contain a valid 'mnemonic' field: $IMPORT_KEY_FILE"
        exit 1
    fi
    # Shred the key file on script exit — success or failure. The mnemonic
    # is sensitive material and there's no reason it should outlive the
    # import. `shred` is preferred (overwrites before unlink); fall back to
    # plain rm on filesystems where shred is unavailable.
    trap '[ -f "$IMPORT_KEY_FILE" ] && (shred -u "$IMPORT_KEY_FILE" 2>/dev/null || rm -f "$IMPORT_KEY_FILE") || true' EXIT
fi

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

# Wallet-first import — must run BEFORE the rest of the stack so that
# validator-service's first CreateOrRetrieveSystemWalletAsync call finds
# the recovered wallet instead of silently generating a fresh one (which
# would then have a pubkey absent from the genesis roster).
if [ -n "$IMPORT_KEY_FILE" ]; then
    log "Bringing up wallet-service ALONE for genesis-key import..."
    $COMPOSE_CMD up -d wallet-service
    ok "wallet-service started"

    log "Waiting for wallet-service to be healthy..."
    elapsed=0
    while [ $elapsed -lt $MAX_WAIT ]; do
        status=$(docker inspect --format='{{.State.Health.Status}}' sorcha-wallet-service 2>/dev/null || echo "not_found")
        if [ "$status" = "healthy" ]; then
            ok "wallet-service is healthy"
            break
        fi
        echo "  Status: $status (${elapsed}s elapsed)"
        sleep 5
        elapsed=$((elapsed + 5))
    done
    if [ "$status" != "healthy" ]; then
        err "wallet-service did not become healthy within ${MAX_WAIT}s; aborting before mnemonic POST"
        exit 1
    fi

    log "Recovering system wallet from $IMPORT_KEY_FILE (validatorId=$VALIDATOR_ID)..."
    # Read mnemonic into memory only — never to disk, never to logs.
    # We pipe the JSON body into curl via stdin so the mnemonic isn't
    # visible in `ps`/`docker inspect` args.
    MNEMONIC=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['mnemonic'])" "$IMPORT_KEY_FILE")
    RECOVER_BODY=$(python3 -c "import json,sys; print(json.dumps({'validatorId':sys.argv[1],'mnemonic':sys.argv[2],'algorithm':'ED25519'}))" "$VALIDATOR_ID" "$MNEMONIC")
    unset MNEMONIC  # zeroize the shell var, the body is what we pass on

    # One-shot curl on the compose network — wallet-service image has no shell.
    # The body is fed on stdin (-d @-) so it doesn't appear in process args.
    # Response body lands in a host-side /tmp file (mounted into the container)
    # so we can extract the address after the curl container exits.
    RECOVER_RESP_FILE=$(mktemp /tmp/recover-resp.XXXXXX)
    HTTP_STATUS=$(printf '%s' "$RECOVER_BODY" | docker run --rm -i \
        --network="$COMPOSE_NETWORK" \
        -v "$RECOVER_RESP_FILE:/recover-resp" \
        curlimages/curl:latest \
        -sk -o /recover-resp -w '%{http_code}' \
        -X POST http://wallet-service:8080/api/v1/wallets/system/recover \
        -H 'Content-Type: application/json' \
        -d @- 2>/dev/null || echo "000")
    unset RECOVER_BODY  # zeroize once curl has consumed it

    if [ "$HTTP_STATUS" = "200" ] || [ "$HTTP_STATUS" = "201" ]; then
        ok "System wallet recovered (HTTP $HTTP_STATUS)"
        # The response carries the wallet address — safe to surface, not sensitive.
        python3 -c "import json,sys; d=json.load(open(sys.argv[1])); print(f'  -> wallet address: {d.get(\"address\",\"<unknown>\")}')" "$RECOVER_RESP_FILE" 2>/dev/null || true
        rm -f "$RECOVER_RESP_FILE"
    elif [ "$HTTP_STATUS" = "409" ]; then
        rm -f "$RECOVER_RESP_FILE"
        err "System wallet already exists (HTTP 409). validator-service may have raced ahead and auto-generated the wrong wallet."
        err "Recovery requires manual SQL surgery — see .claude/skills/network-bootstrap/SKILL.md Step 5 trailer."
        exit 1
    else
        rm -f "$RECOVER_RESP_FILE"
        err "Wallet recovery failed (HTTP $HTTP_STATUS). Check wallet-service logs:"
        err "  docker compose logs wallet-service"
        exit 1
    fi
fi

# Start services (or the rest of them, if wallet-service is already up)
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
