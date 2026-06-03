# Phase 1 Data Model

No new tables, columns, or migrations. This feature changes *which identifier* is used and *where the verifier reads*, not the schema. Entities/state touched:

## Organization (Tenant, existing)

- `WalletAddress: string?` — the canonical operational wallet (**A**). **Read** at issuance time (via the new internal endpoint) to anchor the issuer DID. Nullable: a null value means the org is not yet provisioned → issuance **fails closed** (does not fall back).

## OrgDidDocument (Tenant, existing)

- `OrganizationId: Guid`, `PrimaryDid: string` (= `did:sorcha:org:{walletAddress}`), document payload.
- `PrimaryDid` has a non-unique index (`TenantDbContext.cs:205`) — reused for the new **by-DID lookup** (`GetByPrimaryDidAsync`). After re-anchor, `PrimaryDid` becomes `did:sorcha:org:{A}` (the snapshot now carries A).
- Each verification method: `id = did:sorcha:org:{A}#vc-issuance-{n}`, `publicKeyJwk` = derived child **C's** key, referenced from `assertionMethod`. No structural change — `OrgDidDocumentService` already emits this shape; only the address it is fed changes.

## IssuanceKeyState (Wallet, existing)

- `OrganizationId`, `RotationIndex (n)`, `Status (Active|Rotated|Revoked)`, `PublicKey` (C's key), `Algorithm`.
- Unchanged. `RotationIndex` already drives the `#vc-issuance-{n}` suffix; after re-anchor the suffix sits under `did:sorcha:org:{A}`. All currently-Active rows are published (supports US3 rotation).

## Credential (SD-JWT VC, wire)

- JWS protected header: `alg`, `typ` (unchanged this PR), `kid = did:sorcha:org:{A}#vc-issuance-{n}`.
- Payload `iss = did:sorcha:org:{A}` (was `did:sorcha:org:{C}`); `cnf` holder binding unchanged.

## State / invariants

- **Issuance invariant:** a minted native credential's `iss` and `kid` are anchored on A, signed by C's key, and C's key is published under `did:sorcha:org:{A}` in `assertionMethod`. If A is unresolvable or no Active issuance key exists → no credential is minted (fail closed).
- **Verification invariant:** the issuer key is resolved from the **published** Tenant `did.json` for `did:sorcha:org:{A}`; if unreachable/absent → resolution returns null → fail closed.
- **Rotation:** `Rotated` keys are dropped from `assertionMethod` (existing behaviour); the published doc lists all `Active` keys so any in-window `#vc-issuance-{n}` resolves.
