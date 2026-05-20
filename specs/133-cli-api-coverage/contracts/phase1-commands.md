# Phase 1 Command Contracts

Each row is the contract for one new/changed CLI command: the command surface (what the operator types), the backing endpoint, the client method, and the success/error behaviour. "Client" = the Refit interface method to add, or ♻️ a reused shared-library method.

Global options (`--profile`, `--output table|json|csv|yaml`, `--quiet`, `--verbose`, `--machine-readable`) apply to every command (FR-024). Standard exit codes apply (FR-025): 0 success · 2 auth · 3 authorisation · 4 not-found · 5 validation · 1 general.

## US1 — `sorcha transaction …` (trust-hardening)

| Command | Args/Options | Method + Endpoint | Output | Errors |
|---------|--------------|-------------------|--------|--------|
| `transaction proof <txId>` | `--register <id>` (req), `--out <file>` (opt) | `GetInclusionProofAsync` → `GET /api/registers/{registerId}/transactions/{txId}/inclusion-proof` | `MerkleInclusionProof`; if `--out`, write JSON to file | 404 tx/register not found |
| `transaction verify-proof` | `--register <id>` (req), `--file <proof.json>` (req) | `VerifyInclusionProofAsync` → `POST /api/registers/{registerId}/inclusion-proofs/verify` | `{ isValid, computedRoot }`; non-zero exit if invalid | 5 if file unreadable/malformed |
| `transaction revoke <txId>` | `--register <id>` (req), `--reason <enum>` (req), `--superseded-by <txId>` (opt), `--signer <addr>` (opt) | `RevokeTransactionAsync` → `POST /api/registers/{registerId}/transactions/revoke` (202) | `{ revocationTxId, originalTxId, status }` | 3 if not authorised to revoke; 5 invalid reason |
| `transaction status <txId>` **(FIX)** | `--register <id>` (req) | `GetTransactionStatusAsync` → `GET …/transactions/{txId}/status` — **re-type response to `TransactionStatusResponse`** | `status: Active/Revoked/Superseded` + revocation detail | 404 not found |

**Reason enum** (`--reason`): `Superseded` (requires `--superseded-by`), `Erroneous`, `Compromised`, `Expired`, `Withdrawn`, `Regulatory`.

## US2 — `sorcha register …` (sync diagnostics)

| Command | Args | Method + Endpoint | Output |
|---------|------|-------------------|--------|
| `register relationship <registerId>` | — | `GetLocalRelationshipAsync` → `GET /api/registers/{registerId}/local-relationship` | derived role set (owner/validator/subscriber) |
| `register sync-state <registerId>` | — | `GetSyncStateAsync` → `GET /api/registers/{registerId}/sync-state` | `Indeterminate/Syncing/CaughtUp/Error` + heights |
| `register sync-health` | — | `GetSyncHealthAsync` → `GET /health/sync` | `{ status, registers[], checkedAt }` (table: one row per register) |

All read-only; 404 on unknown register; sync-health needs no register arg.

## US3 — `sorcha validator …` (roster governance, extends existing approve/reject)

| Command | Args/Options | Method + Endpoint | Output |
|---------|--------------|-------------------|--------|
| `validator register` | `--register <id>`, `--validator-id <id>`, `--public-key <pk>`, `--grpc-endpoint <url>` (all req) | `RegisterValidatorAsync` → `POST /api/validators/register` (201) | `{ validatorId, status: active|pending, orderIndex, transactionId }` |
| `validator count <registerId>` | — | `GetValidatorCountAsync` → `GET /api/validators/{registerId}/count` | `{ activeCount, min, max, hasQuorum }` |
| `validator audit <registerId>` | `--validator-id <id>` (opt), `--limit`, `--offset` | `GetValidatorAuditAsync` → `GET /api/validators/{registerId}/audit` | entries table |
| `validator suspend <registerId> <validatorId>` | `--reason <text>` (req), `--by <id>` (opt, default current) | `SuspendValidatorAsync` → `POST …/{validatorId}/suspend` | `{ status: suspended }` |
| `validator reactivate <registerId> <validatorId>` | `--notes <text>` (opt), `--by <id>` (opt) | `ReactivateValidatorAsync` → `POST …/{validatorId}/reactivate` | `{ status: active }` |
| `validator revoke <registerId> <validatorId>` | `--reason <text>` (req), `--by <id>` (opt) | `RevokeValidatorAsync` → `POST …/{validatorId}/revoke` | `{ status: revoked }` |
| `validator sequence <registerId> <walletAddress>` | — | `GetValidatorSequenceAsync` → `GET …/sequence/{walletAddress}` | `{ last, next }` |

`suspend`/`revoke` are destructive → require explicit `<validatorId>` (no wildcard). 3 on authorisation failure.

## US4 — `sorcha wallet org-key …` (REUSE shared client)

Injects `Sorcha.ServiceClients.Http` `IWalletServiceClient`; **no CLI Refit method or DTO added**.

| Command | Args/Options | Reused method | Output |
|---------|--------------|---------------|--------|
| `wallet org-key provision <orgId>` | `--algorithm <ED25519\|...>` (opt, default ED25519) | `ProvisionOrgMasterKeyAsync` | `OrgMasterKeyProvisionResponse` — **mnemonic shown ONCE**, with a warning it is not stored |
| `wallet org-key derive <orgId>` | `--user-id <id>` (req), `--department <uint>` (opt, default 0), `--usage <KeyUsage>` (req) | `DeriveOrgKeyAsync` | `DerivedKeyResponse` |
| `wallet org-key rotate <orgId> <derivedKeyId>` | — | `RotateOrgKeyAsync` | `DerivedKeyResponse` (new index, old decrypt-only) |
| `wallet org-key revoke <orgId> <derivedKeyId>` | — | `RevokeOrgKeyAsync` | `RevokeKeyResponse` |

`KeyUsage`: Identity, VCIssuance, Governance, Communications, ServiceAuth. `provision`/`revoke` are sensitive — mnemonic never echoed to logs, never written to the token cache.
