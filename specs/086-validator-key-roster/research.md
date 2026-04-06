# Research: Validator Key Roster

**Feature**: 086-validator-key-roster  
**Date**: 2026-04-06

## R1: How should the validator signing key be derived from the system wallet?

**Decision**: Use the existing `ISystemWalletSigningService.SignAsync()` with a dedicated derivation path `"sorcha:docket-signing"` (distinct from the existing `"sorcha:register-control"` used for genesis control transactions).

**Rationale**: The system wallet already supports derivation-path-based signing via `SignTransactionAsync(walletId, data, derivationPath)`. Using a distinct path for docket signing means the genesis control signing key and the docket signing key are separate purpose-derived children of the same system wallet. This follows the principle of key separation by purpose while keeping the derivation auditable.

**Alternatives considered**:
- Use the root system wallet key directly: Rejected — exposes the master key in every docket signature.
- Use the existing `"sorcha:register-control"` path: Rejected — conflates control transaction signing with docket signing. Separate purposes should have separate keys.
- Create a new wallet per register: Rejected — over-engineered. The system wallet already supports derivation.

## R2: Where does the validator roster live in the data model?

**Decision**: Add a `Validators` field (type: `ValidatorRoster`) to the existing `RegisterControlRecord` model. The `ValidatorRoster` contains a list of `ValidatorRosterEntry` objects plus threshold parameters.

**Rationale**: The control record is the existing trust anchor for register governance. It is carried in the genesis control transaction and updated via governance control transactions. Adding the validator roster here means it flows through the same governance pipeline (quorum approval, control transaction recording) and is available in the genesis for any peer that syncs the register.

**Alternatives considered**:
- Separate transaction type for validator roster: Rejected — adds complexity. Control transactions already carry governance state.
- Store in register metadata: Rejected — metadata is mutable without governance approval. Validator keys must be governance-controlled.

## R3: How does the DocketBuilder switch to using the purpose-derived key?

**Decision**: Change `DocketBuilder.BuildDocketAsync()` to call `SignTransactionAsync(systemWalletAddress, docketHash, derivationPath: "sorcha:docket-signing")` instead of the current `SignDataAsync(systemWalletAddress, docketHash)`. The returned `PublicKey` from the sign result will be the derived key (not the root key).

**Rationale**: `SignTransactionAsync` already supports the `derivationPath` parameter. The wallet service derives the child key internally and returns the derived public key. No new wallet service API is needed.

**Alternatives considered**:
- Add derivationPath to SignDataAsync: Rejected — SignTransactionAsync already has it.
- Pre-derive the key and store it: Rejected — the wallet service handles derivation; no need to externalize it.

## R4: How does ValidatorKeyCache change to support multiple keys?

**Decision**: Change from `ConcurrentDictionary<string, ValidatorKeyEntry>` (single key per register) to `ConcurrentDictionary<string, List<ValidatorKeyEntry>>` (list of authorized keys per register). Verification checks if the docket signer's public key is in the authorized set.

**Rationale**: The roster is a list from day one (FR-009). Even with one validator initially, the cache must support checking against a set. When governance transactions add validators, the cache is updated by replaying control transactions.

**Alternatives considered**:
- Keep single-key cache, reject if not match: Rejected — breaks immediately when a second validator is added.

## R5: How are pre-existing registers handled?

**Decision**: Clean break. Delete all existing registers on upgrade (FR-005). No backward compatibility needed (preproduction).

**Rationale**: The genesis format changes fundamentally. Retrofitting validator rosters into existing genesis records would require complex migration logic for zero production benefit. All current registers are test data.

## R6: How does the register creation flow accept external validators (FR-014)?

**Decision**: The `RegisterCreationOrchestrator.FinalizeAsync` accepts an optional `List<ValidatorRosterEntry>` parameter. When provided, these entries are used as the validator roster in the genesis control record. When null/empty, the orchestrator auto-populates with the local validator's derived key.

**Rationale**: This satisfies FR-014 (external roster support) needed for the future System Register (087) where the validator roster is curated externally. The default behavior (auto-populate local validator) preserves the current creation UX for standard registers.
