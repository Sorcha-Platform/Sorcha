# Quickstart: Register Policy Model & System Register

**Feature**: 048-register-policy-model

## Prerequisites

- .NET 10 SDK
- Docker Desktop (for Redis, MongoDB, PostgreSQL)
- Sorcha services running (`docker-compose up -d` or `dotnet run --project src/Apps/Sorcha.AppHost`)

## 1. Create a Register with Custom Policy

Previously, registers were created with hardcoded defaults. Now you can specify operational policy at genesis.

### Initiate with Policy

```bash
curl -X POST http://localhost:80/api/registers/creation/initiate \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $JWT_TOKEN" \
  -d '{
    "name": "MyRegister",
    "tenantId": "tenant-001",
    "owners": [{ "userId": "user-001", "walletId": "wallet-001" }],
    "policy": {
      "version": 1,
      "governance": {
        "quorumFormula": "supermajority",
        "proposalTtlDays": 14,
        "ownerCanBypassQuorum": false,
        "blueprintVersion": "register-governance-v1"
      },
      "validators": {
        "registrationMode": "consent",
        "minValidators": 3,
        "maxValidators": 10,
        "requireStake": false,
        "operationalTtlSeconds": 120
      },
      "consensus": {
        "signatureThresholdMin": 3,
        "signatureThresholdMax": 7,
        "maxTransactionsPerDocket": 500,
        "docketBuildIntervalMs": 200,
        "docketTimeoutSeconds": 60
      },
      "leaderElection": {
        "mechanism": "rotating",
        "heartbeatIntervalMs": 2000,
        "leaderTimeoutMs": 10000,
        "termDurationSeconds": 120
      }
    }
  }'
```

If `policy` is omitted, default values are applied automatically (public validation, strict-majority quorum, rotating election).

### Finalize

Sign attestations and finalize as before — the policy is embedded in the genesis Control transaction.

## 2. Query Register Policy

```bash
# Get current policy
curl http://localhost:80/api/registers/{registerId}/policy \
  -H "Authorization: Bearer $JWT_TOKEN"

# Response includes isDefault flag
# { "registerId": "...", "policy": { ... }, "isDefault": false }
```

## 3. Update Policy via Governance

Policy changes require governance quorum (subject to the register's current quorum formula).

```bash
curl -X POST http://localhost:80/api/registers/{registerId}/policy/update \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $JWT_TOKEN" \
  -d '{
    "policy": {
      "version": 2,
      "governance": {
        "quorumFormula": "supermajority",
        "proposalTtlDays": 14,
        "ownerCanBypassQuorum": false,
        "blueprintVersion": "register-governance-v1"
      },
      "validators": {
        "registrationMode": "consent",
        "minValidators": 3,
        "maxValidators": 15,
        "requireStake": false,
        "operationalTtlSeconds": 120
      },
      "consensus": {
        "signatureThresholdMin": 3,
        "signatureThresholdMax": 10,
        "maxTransactionsPerDocket": 500,
        "docketBuildIntervalMs": 200,
        "docketTimeoutSeconds": 60
      },
      "leaderElection": {
        "mechanism": "rotating",
        "heartbeatIntervalMs": 2000,
        "leaderTimeoutMs": 10000,
        "termDurationSeconds": 120
      }
    },
    "updatedBy": "did:sorcha:w:wallet-001",
    "approvalSignatures": [
      {
        "approverDid": "did:sorcha:w:wallet-001",
        "signature": "base64-signature...",
        "isApproval": true,
        "votedAt": "2026-03-04T12:00:00Z"
      }
    ]
  }'
```

### Switching from Public to Consent Mode

When changing `registrationMode` from `public` to `consent`, include `transitionMode`:

```json
{
  "policy": { "validators": { "registrationMode": "consent", ... }, ... },
  "transitionMode": "grace-period",
  "updatedBy": "did:sorcha:w:wallet-001"
}
```

- `immediate` — unapproved validators are ejected at commit time
- `grace-period` (default) — unapproved validators continue for one TTL cycle

## 4. System Register

### Bootstrap (Development)

Set environment variable before starting:

```bash
SORCHA_SEED_SYSTEM_REGISTER=true docker-compose up -d
```

The System Register is created automatically on startup with:
- Deterministic ID (SHA-256 of "sorcha-system-register", truncated to 32 hex)
- System blueprints: `register-governance-v1`, `register-creation-v1`
- "system-setup" wallet as Owner

### Query System Register

```bash
# Get System Register info
curl http://localhost:80/api/system-register \
  -H "Authorization: Bearer $JWT_TOKEN"

# List system blueprints
curl http://localhost:80/api/system-register/blueprints \
  -H "Authorization: Bearer $JWT_TOKEN"

# Get specific blueprint
curl http://localhost:80/api/system-register/blueprints/register-governance-v1 \
  -H "Authorization: Bearer $JWT_TOKEN"
```

## 5. Approved Validators (Consent Mode)

```bash
# View on-chain approved list
curl http://localhost:80/api/registers/{registerId}/validators/approved \
  -H "Authorization: Bearer $JWT_TOKEN"

# View operational (online) validators
curl http://localhost:80/api/registers/{registerId}/validators/operational \
  -H "Authorization: Bearer $JWT_TOKEN"
```

## 6. Backward Compatibility

Existing registers (created before this feature) continue to work unchanged:

```bash
# Returns default policy with isDefault: true
curl http://localhost:80/api/registers/{legacyRegisterId}/policy
# { "isDefault": true, "policy": { "version": 1, "governance": { "quorumFormula": "strict-majority", ... } } }
```

To adopt the new policy model on an existing register, submit a `control.policy.update` transaction through the governance workflow.
