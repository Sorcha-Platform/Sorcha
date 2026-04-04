# Quickstart: Wallet Key Derivation & UI Transaction Lifecycle

**Feature**: 083-wallet-key-derivation

## Prerequisites

- .NET 10 SDK
- Docker Desktop (for PostgreSQL, Redis)
- `docker-compose up -d` running

## Key Concepts

### Org Key Derivation

Organisations provision a master seed that derives deterministic HD wallets for their members. The derivation path follows:

```
m / 0x534F52' / org_id' / dept_id' / user_id' / key_usage / index
     Sorcha     org hash   dept(0)   user hash   purpose    rotation
```

**Key usage types**: Identity (0), VC Issuance (1), Governance (2), Communications (3), Service Auth (4).

**Lifecycle**: Provision master key → auto-derive identity wallets → derive purpose-specific keys → rotate/revoke as needed.

### Transaction Ticks

WhatsApp-style delivery indicators for transactions:

| Icon | State | Meaning |
|------|-------|---------|
| Grey ✓ | Pending | Submitted, not yet in a docket |
| Blue ✓ | Sealed | Sealed in a docket by the validator |
| Blue ✓✓ | Receipted | Cryptographic receipt confirmed |

## API Usage

### 1. Provision Org Master Key

```bash
# Admin provisions master key (returns mnemonic ONCE)
curl -X POST http://localhost/api/wallets/org/{orgId}/master-key \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json"

# Response includes mnemonic (one-time display)
# {
#   "organizationId": "...",
#   "masterPublicKey": "xpub...",
#   "mnemonic": "abandon ability able ... zoo",
#   "algorithm": "ED25519"
# }
```

### 2. Derive User Key

```bash
# Derive a VC issuance key for a user
curl -X POST http://localhost/api/wallets/org/{orgId}/derive-key \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "user-guid-here",
    "keyUsage": "VCIssuance",
    "departmentId": 0
  }'

# Response: wallet address, derivation path, key usage
```

### 3. Rotate Key

```bash
curl -X POST http://localhost/api/wallets/org/{orgId}/keys/{derivedKeyId}/rotate \
  -H "Authorization: Bearer $ADMIN_TOKEN"

# Old key → Rotated (decrypt only), new key → Active (sign + decrypt)
```

### 4. Revoke Key

```bash
curl -X DELETE http://localhost/api/wallets/org/{orgId}/keys/{derivedKeyId} \
  -H "Authorization: Bearer $ADMIN_TOKEN"

# Key revoked, wallet locked, DID event published (if identity key)
```

## UI Features

### Transaction List

The Transactions tab in Wallet Detail now shows a Status column with tick indicators. Click any row to open the detail panel.

### Transaction Detail Panel

Slide-out panel showing:
1. **Timeline**: Submitted → Sealed → Receipted with timestamps
2. **Details**: Register, direction, counterparty, sequence, docket
3. **Receipt Proof**: Receipt ID, Merkle root, validator, signature + verify/download actions

### Real-Time Updates

Transaction ticks update live via SignalR — no page refresh needed.

## Testing

```bash
# Run org key derivation tests
dotnet test --filter "OrgKeyDerivation"

# Run transaction tick E2E tests
dotnet test --filter "TransactionTick"

# Run all feature tests
dotnet test --filter "FullyQualifiedName~083"
```

## Related Documentation

- **Design doc**: `docs/superpowers/specs/2026-04-04-wallet-key-derivation-ui-design.md`
- **Research**: `specs/083-wallet-key-derivation/research.md`
- **Data model**: `specs/083-wallet-key-derivation/data-model.md`
- **API contract**: `specs/083-wallet-key-derivation/contracts/wallet-org-keys.yaml`
