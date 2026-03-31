#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Sorcha Clean Installation Verification
# =======================================
# Verifies that a Sorcha Docker deployment is fully operational.
# Run from project root: ./scripts/check-clean-install.sh
#
# Exit codes: 0 = all checks passed, 1 = one or more checks failed, 2 = Docker not available
#
# Optional: GATEWAY_PORT=8880 ./scripts/check-clean-install.sh
#   (for n1 where Caddy occupies port 80)

set -uo pipefail

PASS=0
FAIL=0
WARN=0
GATEWAY_PORT="${GATEWAY_PORT:-80}"

pass() { echo "  [PASS] $1"; ((PASS++)) || true; }
fail() { echo "  [FAIL] $1"; ((FAIL++)) || true; }
warn() { echo "  [WARN] $1"; ((WARN++)) || true; }
section() { echo ""; echo "=== $1 ==="; }

# Safe grep -c that always returns a clean integer
count_grep() {
  local result
  result=$(grep -c "$@" 2>/dev/null || true)
  echo "${result:-0}" | head -1 | tr -d '[:space:]'
}

# Safe docker log grep count
count_docker_log() {
  local container="$1" pattern="$2"
  local result
  result=$(docker logs "$container" 2>&1 | grep -ci "$pattern" 2>/dev/null || true)
  echo "${result:-0}" | head -1 | tr -d '[:space:]'
}

# ---------------------------------------------------------------------------
section "Phase 0: Docker Pre-Check"
# ---------------------------------------------------------------------------

# Check Docker daemon is running
if ! docker info > /dev/null 2>&1; then
  echo "  [FATAL] Docker daemon is not running!"
  echo ""
  echo "  Start Docker Desktop or the Docker service, then re-run this script."
  exit 2
fi
pass "Docker daemon running"

# Check if any Sorcha containers exist at all
sorcha_count=$(docker ps --format "{{.Names}}" 2>/dev/null | grep -c "^sorcha-" || echo "0")
sorcha_count=$(echo "$sorcha_count" | head -1 | tr -d '[:space:]')

if [ "$sorcha_count" -eq 0 ]; then
  echo "  [FATAL] No Sorcha containers are running!"
  echo ""
  echo "  Start the stack with: docker-compose up -d"
  echo "  Then re-run this script."
  exit 2
fi
pass "$sorcha_count Sorcha containers detected"

# ---------------------------------------------------------------------------
section "Phase 1: Containers Running"
# ---------------------------------------------------------------------------

EXPECTED_CONTAINERS=(
  sorcha-redis
  sorcha-postgres
  sorcha-mongodb
  sorcha-aspire-dashboard
  sorcha-blueprint-service
  sorcha-wallet-service
  sorcha-register-service
  sorcha-tenant-service
  sorcha-peer-service
  sorcha-validator-service
  sorcha-ui-web
  sorcha-api-gateway
)

running_containers=$(docker ps --format "{{.Names}}" 2>/dev/null || true)

for c in "${EXPECTED_CONTAINERS[@]}"; do
  if echo "$running_containers" | grep -q "^${c}$"; then
    restarts=$(docker inspect --format='{{.RestartCount}}' "$c" 2>/dev/null || echo "0")
    if [ "$restarts" -gt 3 ]; then
      fail "$c running but restart count=$restarts (possible crash loop)"
    else
      pass "$c running"
    fi
  else
    fail "$c NOT running"
  fi
done

# Check health status for containers that have healthchecks
for c in sorcha-redis sorcha-postgres sorcha-mongodb sorcha-blueprint-service sorcha-wallet-service sorcha-register-service sorcha-tenant-service sorcha-peer-service sorcha-validator-service sorcha-api-gateway; do
  if echo "$running_containers" | grep -q "^${c}$"; then
    health=$(docker inspect --format='{{.State.Health.Status}}' "$c" 2>/dev/null || echo "none")
    if [ "$health" = "healthy" ]; then
      pass "$c health: healthy"
    elif [ "$health" = "none" ]; then
      true  # No healthcheck configured
    else
      fail "$c health: $health"
    fi
  fi
done

# ---------------------------------------------------------------------------
section "Phase 2: Infrastructure Services"
# ---------------------------------------------------------------------------

if docker exec sorcha-redis redis-cli ping 2>/dev/null | grep -q "PONG"; then
  pass "Redis responding (PONG)"
else
  fail "Redis not responding"
fi

if docker exec sorcha-postgres pg_isready -U sorcha 2>/dev/null | grep -q "accepting"; then
  pass "PostgreSQL accepting connections"
else
  fail "PostgreSQL not ready"
fi

if docker exec sorcha-mongodb mongosh -u sorcha -p sorcha_dev_password --authenticationDatabase admin --eval "db.adminCommand('ping')" --quiet 2>/dev/null | grep -q "ok"; then
  pass "MongoDB responding (ping ok)"
else
  fail "MongoDB not responding"
fi

# ---------------------------------------------------------------------------
section "Phase 3: Health Endpoints"
# ---------------------------------------------------------------------------

check_health() {
  local name="$1" url="$2"
  local status
  status=$(curl -sf -o /dev/null -w "%{http_code}" "$url" 2>/dev/null || echo "000")
  if [ "$status" = "200" ]; then
    pass "$name /health → 200"
  else
    fail "$name /health → $status"
  fi
}

check_health "API Gateway" "http://localhost:${GATEWAY_PORT}/health"
check_health "Blueprint Service" "http://localhost:5000/health"
check_health "Register Service" "http://localhost:5380/health"
check_health "Tenant Service" "http://localhost:5450/health"
check_health "Validator Service" "http://localhost:5800/health"

# Wallet has no host port — check internally
# Use MSYS_NO_PATHCONV to prevent Git Bash from mangling /bin/busybox path on Windows
if MSYS_NO_PATHCONV=1 docker exec sorcha-wallet-service /bin/busybox wget -qO- http://localhost:8080/health 2>/dev/null | grep -qi "healthy"; then
  pass "Wallet Service /health → healthy"
else
  fail "Wallet Service /health check failed"
fi

# Aggregated health via gateway
agg_status=$(curl -s "http://localhost:${GATEWAY_PORT}/api/health" 2>/dev/null | grep -o '"status":"[^"]*"' | head -1 || echo "")
if echo "$agg_status" | grep -qi "healthy"; then
  pass "Aggregated health: healthy"
else
  fail "Aggregated health: $agg_status"
fi

# ---------------------------------------------------------------------------
section "Phase 4: Database Seeding (Tenant → PostgreSQL)"
# ---------------------------------------------------------------------------

check_pg_count() {
  local label="$1" table="$2" expected="$3" db="${4:-sorcha}"
  local count
  count=$(docker exec sorcha-postgres psql -U sorcha -d "$db" -t -A -c "SELECT COUNT(*) FROM \"$table\";" 2>/dev/null || echo "0")
  count=$(echo "$count" | head -1 | tr -d '[:space:]')
  if [ "$count" -ge "$expected" ] 2>/dev/null; then
    pass "$label: $count (expected >= $expected)"
  else
    fail "$label: $count (expected >= $expected)"
  fi
}

check_pg_count "Organizations" "Organizations" 2 "sorcha_tenant"
check_pg_count "PlatformUsers" "PlatformUsers" 1 "sorcha_tenant"
check_pg_count "UserIdentities" "UserIdentities" 1 "sorcha_tenant"
check_pg_count "ServicePrincipals" "ServicePrincipals" 6 "sorcha_tenant"
check_pg_count "PlatformSettings" "PlatformSettings" 1 "sorcha_tenant"
check_pg_count "OrgMemberships" "PlatformUserOrgMemberships" 1 "sorcha_tenant"

# Check well-known org IDs
sysadmin_org=$(docker exec sorcha-postgres psql -U sorcha -d sorcha_tenant -t -A -c \
  "SELECT COUNT(*) FROM \"Organizations\" WHERE \"Id\" = '00000000-0000-0000-0000-000000000001';" 2>/dev/null || echo "0")
sysadmin_org=$(echo "$sysadmin_org" | head -1 | tr -d '[:space:]')
if [ "$sysadmin_org" -ge 1 ] 2>/dev/null; then
  pass "System Admin org exists"
else
  fail "System Admin org MISSING"
fi

public_org=$(docker exec sorcha-postgres psql -U sorcha -d sorcha_tenant -t -A -c \
  "SELECT COUNT(*) FROM \"Organizations\" WHERE \"Id\" = '00000000-0000-0000-0000-000000000002';" 2>/dev/null || echo "0")
public_org=$(echo "$public_org" | head -1 | tr -d '[:space:]')
if [ "$public_org" -ge 1 ] 2>/dev/null; then
  pass "Public org exists"
else
  fail "Public org MISSING"
fi

# ---------------------------------------------------------------------------
section "Phase 5: System Wallet (Validator → Wallet)"
# ---------------------------------------------------------------------------

wallet_log=$(docker logs sorcha-validator-service 2>&1 | grep -i "system wallet" | tail -1 || echo "")
if echo "$wallet_log" | grep -qi "wallet"; then
  pass "System wallet initialised (log found)"
else
  fail "System wallet initialisation log not found"
fi

wallet_count=$(curl -s "http://localhost:${GATEWAY_PORT}/api/dashboard" 2>/dev/null | grep -o '"totalWallets":[0-9]*' | grep -o '[0-9]*' || echo "0")
wallet_count=$(echo "$wallet_count" | head -1 | tr -d '[:space:]')
if [ -n "$wallet_count" ] && [ "$wallet_count" -ge 1 ] 2>/dev/null; then
  pass "Wallet count: $wallet_count"
else
  fail "Wallet count: ${wallet_count:-0} (expected >= 1)"
fi

# ---------------------------------------------------------------------------
section "Phase 6: System Register (Register → MongoDB)"
# ---------------------------------------------------------------------------

sys_reg=$(docker exec sorcha-mongodb mongosh -u sorcha -p sorcha_dev_password --authenticationDatabase admin --quiet --eval "
  db = db.getSiblingDB('sorcha_register_registry');
  var r = db.registers.findOne({_id: 'aebf26362e079087571ac0932d4db973'});
  if (r) { print('HEIGHT=' + r.Height + ' STATUS=' + r.Status); }
  else { print('MISSING'); }
" 2>/dev/null || echo "MISSING")

if echo "$sys_reg" | grep -q "MISSING"; then
  fail "System register NOT found in MongoDB"
else
  height=$(echo "$sys_reg" | grep -o 'HEIGHT=[0-9]*' | grep -o '[0-9]*' || echo "0")
  status_val=$(echo "$sys_reg" | grep -o 'STATUS=[^ ]*' | cut -d= -f2 || echo "unknown")
  if [ -n "$height" ] && [ "$height" -ge 1 ] 2>/dev/null; then
    pass "System register exists, Height=$height, Status=$status_val"
  else
    fail "System register Height=${height:-0} (expected >= 1)"
  fi
fi

register_count=$(curl -s "http://localhost:5380/api/stats" 2>/dev/null | grep -o '"registerCount":[0-9]*' | grep -o '[0-9]*' || echo "0")
register_count=$(echo "$register_count" | head -1 | tr -d '[:space:]')
if [ -n "$register_count" ] && [ "$register_count" -ge 1 ] 2>/dev/null; then
  pass "Total registers: $register_count"
else
  fail "Total registers: ${register_count:-0} (expected >= 1)"
fi

# ---------------------------------------------------------------------------
section "Phase 7: Blueprints Published"
# ---------------------------------------------------------------------------

bp_seeded=$(count_docker_log "sorcha-register-service" "seeded successfully")
if [ "$bp_seeded" -ge 3 ] 2>/dev/null; then
  pass "Blueprint seeding: $bp_seeded blueprints seeded"
else
  warn "Blueprint seeding: $bp_seeded seeded log entries (expected 3, may be from prior boot)"
fi

bp_count=$(curl -s "http://localhost:5000/api/stats" 2>/dev/null | grep -o '"blueprintCount":[0-9]*' | grep -o '[0-9]*' || echo "0")
bp_count=$(echo "$bp_count" | head -1 | tr -d '[:space:]')
if [ -n "$bp_count" ] && [ "$bp_count" -ge 3 ] 2>/dev/null; then
  pass "Published blueprint count: $bp_count"
else
  # Published blueprints require recovery from register — may be 0 on fresh start
  warn "Published blueprint count: ${bp_count:-0} (recovery may still be in progress)"
fi

bp_recovery=$(count_docker_log "sorcha-blueprint-service" "recovery")
if [ "$bp_recovery" -ge 1 ] 2>/dev/null; then
  pass "Blueprint recovery ran"
else
  warn "Blueprint recovery log not found"
fi

# ---------------------------------------------------------------------------
section "Phase 8: Schema Library (Blueprint → PostgreSQL)"
# ---------------------------------------------------------------------------

# Schema library is in-memory/Redis, not EF Core — check via startup log
schema_started=$(count_docker_log "sorcha-blueprint-service" "Schema index refresh")
if [ "$schema_started" -ge 1 ] 2>/dev/null; then
  pass "Schema index refresh service running"
else
  warn "Schema index refresh service log not found"
fi

# Blueprint templates are in EF Core (blueprint schema)
template_count=$(docker exec sorcha-postgres psql -U sorcha -d sorcha_blueprint -t -A -c \
  "SELECT COUNT(*) FROM blueprint.\"BlueprintTemplates\";" 2>/dev/null || echo "0")
template_count=$(echo "$template_count" | head -1 | tr -d '[:space:]')
if [ -n "$template_count" ] && [ "$template_count" -ge 1 ] 2>/dev/null; then
  pass "Blueprint templates: $template_count seeded"
else
  fail "Blueprint templates: ${template_count:-0} (expected >= 1)"
fi

# ---------------------------------------------------------------------------
section "Phase 9: Register Sync Health"
# ---------------------------------------------------------------------------

sync_status=$(curl -s "http://localhost:5380/health/sync" 2>/dev/null \
  | grep -o '"status":"[^"]*"' | head -1 | grep -o '"[^"]*"$' | tr -d '"' || echo "unknown")
if [ "$sync_status" = "synced" ]; then
  pass "Register sync: synced"
elif [ "$sync_status" = "recovering" ] || [ "$sync_status" = "stalled" ]; then
  warn "Register sync: $sync_status (may be transient on fresh start)"
else
  fail "Register sync: $sync_status"
fi

# ---------------------------------------------------------------------------
section "Phase 10: Peer Network"
# ---------------------------------------------------------------------------

peer_health=$(curl -s "http://localhost:${GATEWAY_PORT}/api/peer/health" 2>/dev/null || echo "{}")
total_peers=$(echo "$peer_health" | grep -o '"totalPeers":[0-9]*' | grep -o '[0-9]*' || echo "0")
total_peers=$(echo "$total_peers" | head -1 | tr -d '[:space:]')
healthy_peers=$(echo "$peer_health" | grep -o '"healthyPeers":[0-9]*' | grep -o '[0-9]*' || echo "0")
healthy_peers=$(echo "$healthy_peers" | head -1 | tr -d '[:space:]')

pass "Peer network: ${total_peers:-0} total, ${healthy_peers:-0} healthy"

reverse_stream=$(count_docker_log "sorcha-peer-service" "Reverse stream established")
if [ "$reverse_stream" -ge 1 ] 2>/dev/null; then
  pass "Reverse stream: established"
else
  warn "Reverse stream: not established (may be standalone node)"
fi

# ---------------------------------------------------------------------------
section "Phase 11: Service Logs — Error Check"
# ---------------------------------------------------------------------------

SERVICES=(blueprint-service wallet-service register-service tenant-service peer-service validator-service api-gateway)

for svc in "${SERVICES[@]}"; do
  err_count=$(docker logs "sorcha-$svc" --tail 300 2>&1 | grep -cE " ERR\] | FAT\] " || true)
  err_count=$(echo "${err_count:-0}" | head -1 | tr -d '[:space:]')
  if [ "$err_count" -eq 0 ] 2>/dev/null; then
    pass "$svc: no errors"
  else
    fail "$svc: $err_count error(s) in last 300 log lines"
    docker logs "sorcha-$svc" --tail 300 2>&1 | grep -E " ERR\] | FAT\] " | head -3 | sed 's/^/    /'
  fi
done

# ---------------------------------------------------------------------------
section "Phase 12: Startup Completion Markers"
# ---------------------------------------------------------------------------

check_log() {
  local svc="$1" pattern="$2" label="$3"
  # Use grep -c to count matches; capture count to avoid pipefail issues
  local match_count
  match_count=$(docker logs "sorcha-$svc" 2>&1 | grep -Eci "$pattern" || echo "0")
  match_count=$(echo "$match_count" | head -1 | tr -d '[:space:]')
  if [ "$match_count" -ge 1 ] 2>/dev/null; then
    pass "$label"
  else
    warn "$label — log marker not found (may have rotated)"
  fi
}

check_log "tenant-service" "Database initialization completed" "Tenant DB initialised"
check_log "register-service" "bootstrap completed|System register created" "System register bootstrapped"
check_log "blueprint-service" "migrations applied|Blueprint.*migration|Applying Blueprint" "Blueprint DB migrated"
check_log "wallet-service" "migrations applied|Wallet.*migration|Applying Wallet" "Wallet DB migrated"
check_log "validator-service" "system wallet|Initializing system wallet" "Validator wallet initialised"
check_log "peer-service" "heartbeat.*started|P2P.*started|heartbeat service started" "Peer heartbeat started"

# ---------------------------------------------------------------------------
section "Phase 13: Dashboard Stats Consistency"
# ---------------------------------------------------------------------------

dashboard=$(curl -s "http://localhost:${GATEWAY_PORT}/api/dashboard" 2>/dev/null || echo "{}")

check_dashboard() {
  local label="$1" field="$2"
  local val
  val=$(echo "$dashboard" | grep -o "\"${field}\":[0-9]*" | grep -o '[0-9]*' | head -1 || echo "0")
  val=$(echo "$val" | head -1 | tr -d '[:space:]')
  if [ -n "$val" ] && [ "$val" -ge 1 ] 2>/dev/null; then
    pass "Dashboard $label: $val"
  else
    warn "Dashboard $label: ${val:-0}"
  fi
}

check_dashboard "wallets" "totalWallets"
check_dashboard "registers" "totalRegisters"
check_dashboard "tenants" "totalTenants"
check_dashboard "blueprints" "totalBlueprints"

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
echo ""
echo "==========================================="
echo " Results: $PASS passed, $FAIL failed, $WARN warnings"
echo "==========================================="

if [ "$FAIL" -gt 0 ]; then
  echo " STATUS: UNHEALTHY — $FAIL check(s) failed"
  exit 1
else
  if [ "$WARN" -gt 0 ]; then
    echo " STATUS: HEALTHY (with $WARN warning(s))"
  else
    echo " STATUS: HEALTHY"
  fi
  exit 0
fi
