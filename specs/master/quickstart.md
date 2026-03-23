# Quickstart: Register Subscriptions

## Phase 1 — Build & Test

```bash
# Build affected projects
dotnet build src/Services/Sorcha.Tenant.Service/
dotnet build src/Services/Sorcha.Register.Service/
dotnet build src/Services/Sorcha.ApiGateway/
dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Web/

# Run EF migration
dotnet ef migrations add AddRegisterSubscriptions \
  --project src/Services/Sorcha.Tenant.Service/ \
  --context TenantDbContext

# Run tests
dotnet test tests/Sorcha.Tenant.Service.Tests/ --filter "FullyQualifiedName~Subscription"
dotnet test tests/Sorcha.Tenant.Service.Tests/ --filter "FullyQualifiedName~Bootstrap"

# Start all services
docker-compose up -d

# Bootstrap with org wallet
dotnet run --project src/Apps/Sorcha.Cli -- --profile docker bootstrap \
  -y -n "Test Org" -s "test-org" -e "admin@test.local" -a "Admin" -p "Test_2026!"
```

## Phase 1 — Verify

```bash
# Get auth token
TOKEN=$(curl -s http://localhost/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@test.local","password":"Test_2026!"}' | jq -r '.access_token')

# Check org has wallet (new field)
curl -s http://localhost/api/organizations \
  -H "Authorization: Bearer $TOKEN" | jq '.[0].walletAddress'

# List subscriptions (should include System Register as Owner)
curl -s "http://localhost/api/me/subscribed-registers" \
  -H "Authorization: Bearer $TOKEN" | jq .

# Subscribe to a public register
curl -s -X POST "http://localhost/api/organizations/{orgId}/register-subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"register_id":"<registerId>"}' | jq .

# Unsubscribe
curl -s -X DELETE "http://localhost/api/organizations/{orgId}/register-subscriptions/<registerId>" \
  -H "Authorization: Bearer $TOKEN"
```

## Phase 2 — Invitation Flow

```bash
# Create invitation (source org admin)
curl -s -X POST "http://localhost/api/organizations/{orgId}/register-invitations" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "register_id": "<privateRegisterId>",
    "target_org_did": "did:sorcha:org:<targetWalletAddress>",
    "expires_in_days": 7
  }' | jq .

# Accept invitation (target org admin, different token)
curl -s -X POST "http://localhost/api/organizations/{targetOrgId}/register-invitations/accept" \
  -H "Authorization: Bearer $TARGET_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"invitation_token": "<token_from_step_above>"}' | jq .
```

## Key Verification Points

1. **System Register name visible** in Peer Network admin → Register Subscriptions tab
2. **Bootstrap creates org wallet** — check `walletAddress` is not null on org response
3. **Registers page scoped** — only shows subscribed registers
4. **New Submission scoped** — register dropdown shows only subscribed registers
5. **Owner subscriptions** — cannot be unsubscribed (DELETE returns 400)
6. **Phase 2: Invitation round-trip** — create → share token → accept → subscription created
