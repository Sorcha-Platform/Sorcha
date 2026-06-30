# Phase 1 Data Model: AIAS Demo Publish-Governance

This feature is a provisioning-script fix; it introduces **no new persisted schema**. The "model" below captures the governance entities and the relationship that must hold for provisioning to succeed. These are existing platform concepts; the fix only changes *which identity* owns the register.

## Entities

### AIAS Register
The distributed register the AIAS authority publishes its blueprint and participant to.
- **Identity**: `RegisterId` (platform-assigned), `Name` (used for idempotent reuse).
- **owners[]**: roster of `{ userId, walletId }` entries. The roster is the governance authority the F142 PublishGate checks.
- **advertise / isPublic**: true — so the public org can subscribe and consumer/discovery flows can read it.
- **Validation rule**: the publishing wallet (verification-admin/issuer) MUST appear as an owner (`walletId`) on this roster **before** blueprint publish.

### Verification-Admin (Issuer) Wallet
The AIAS organisation's publishing identity.
- **Fields**: `Address` (on-ledger wallet address → register `owners[].walletId`), owning `UserId`.
- **Relationship**: linked to the verification-admin platform user; that user's session JWT must carry `wallet_address == this.Address` at publish time.

### Verification-Admin User / Session
The platform user that drives provisioning of the AIAS authority.
- **Fields**: `UserId`, `Headers` (auth session). A *fresh* session minted after the wallet link carries the `wallet_address` claim.
- **Relationship**: owns the Issuer Wallet; signs the register-ownership attestation; publishes the blueprint and participant.

### AIAS Blueprint
The workflow definition published to the AIAS register.
- **Gated by**: the register's publish-governance authority (the roster check above).

### Sorcha Public Organisation
The well-known public org subscribed to the AIAS register for public/consumer discovery.
- **Relationship**: subscribed (type `Public`) to the AIAS register once the register exists and is advertised.

### Agent Configuration
The output artefact written after a successful blueprint publish.
- **Precondition**: blueprint publish succeeded (authority-ready state reached).

## The Governing Relationship (the fix)

```
Verification-Admin User ──owns──▶ Issuer Wallet ──is owner on──▶ AIAS Register roster
        │                              ▲                                  │
        │ fresh session JWT            │ ownership attestation            │ PublishGate
        │ carries wallet_address ──────┘ signed by wallet owner           │ matches roster
        ▼                                                                 ▼
   Publish Blueprint / Participant ──────────────────────────────▶  ✅ no 403
```

**Before (broken)**: Register owner = sysadmin (docker) wallet; publisher = issuer wallet → roster mismatch → 403.

**After (fixed)**: Register owner = issuer wallet; publisher = issuer wallet (fresh JWT) → roster match → publish succeeds; participant seal and public-org subscription resolve as a side effect.

## State Transitions (provisioning happy path)

1. **Org + verification-admin user + issuer wallet created/linked.**
2. **Register created** with `owners = [{ userId: vAdmin.UserId, walletId: vWallet.Address }]`; ownership attestation signed by the issuer wallet owner. → register exists, issuer wallet on roster.
3. **Fresh verification-admin login** → JWT carries `wallet_address`.
4. **Blueprint published** by verification-admin (fresh JWT) → PublishGate passes (no 403).
5. **Participant published** onto the register → seals within the normal window (no ~90s timeout).
6. **Public org subscribed** to the register → succeeds (no 500).
7. **Agent config written** → authority-ready state reached.

**Idempotent re-run**: at step 2, if a register with `Name` already exists it is reused; its ownership MUST already be (or be made) the issuer wallet so steps 3–7 still pass.
