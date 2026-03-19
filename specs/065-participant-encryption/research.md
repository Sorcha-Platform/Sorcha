# Research: 065 Participant Resolution, Starting Action Binding & Field-Level Encryption

**Date**: 2026-03-19

## Key Findings

### 1. VAL_BP_002 Already Handles Missing Wallet Addresses Gracefully

**Decision**: Extend existing graceful handling — don't fail, resolve dynamically.
**Rationale**: The validator (ValidationEngine.cs line 919-921) already logs debug and skips validation when a participant has no wallet address. The fix is to add two resolution paths: (a) check instance `ParticipantWallets` for bound participants, (b) query register participant index for organisational participants.
**Alternatives considered**: Separate validation rule (VAL_BP_005) — rejected because extending the existing check is simpler and maintains one validation point.

### 2. Instance.ParticipantWallets Already Exists

**Decision**: Use existing `Dictionary<string, string>` field on Instance for starting action binding.
**Rationale**: The Instance model (Instance.cs line 48) already has `ParticipantWallets` which maps participant ID → wallet address. Currently populated during blueprint publish. For starting actions, populate it at execution time when the first action is submitted.
**Alternatives considered**: New `ParticipantBindings` entity — rejected because `ParticipantWallets` serves the same purpose and is already used by disclosure evaluation.

### 3. Register Model Needs DevMode Flag

**Decision**: Add `DevMode` boolean to `Register` model (defaults to false).
**Rationale**: CryptoPolicy's `EnforcementMode` (Permissive/Strict) controls algorithm governance, not whether encryption happens. DevMode is a separate concern — should encryption run at all. Adding to Register rather than CryptoPolicy keeps the concepts clean.
**Alternatives considered**: (a) Add to CryptoPolicy as a third mode — rejected because DevMode is about development convenience, not cryptographic governance. (b) Per-blueprint setting — rejected because encryption is a register-level concern (data at rest).

### 4. ActionExecutionService Already Has Encrypted vs Plaintext Paths

**Decision**: DevMode uses the existing plaintext path (lines 413-418); encrypted path (lines 403-408) used when DevMode is off.
**Rationale**: The code already branches between `BuildEncryptedActionTransactionAsync` and `BuildActionTransactionAsync`. Currently the branch decision is based on whether encryption succeeded. For DevMode, simply skip the encryption pipeline and use the plaintext path.
**Alternatives considered**: Always encrypt then strip — rejected because it defeats the purpose of DevMode (no crypto overhead).

### 5. Participant Resolution Has Two Stages

**Decision**: Two resolution mechanisms, checked in order:
1. **Instance bindings** (`ParticipantWallets`) — for dynamically bound participants (citizen from starting action)
2. **Register participant index** (`ParticipantIndexService`) — for organisational participants with published records

**Rationale**: Instance bindings are ephemeral (per-workflow), register records are persistent (per-organisation). Both already exist. The validator's read-only register access includes the participant index.
**Alternatives considered**: Single resolution — rejected because starting action participants have no register record; they're bound at runtime.

### 6. EncryptionPipelineService Is Ready

**Decision**: No changes needed to EncryptionPipelineService itself. Wire it into ActionExecutionService conditional on DevMode.
**Rationale**: The service already handles disclosure grouping, symmetric encryption (XChaCha20-Poly1305), per-recipient asymmetric key wrapping, size estimation, and atomic failure. It's fully implemented and tested.
**Alternatives considered**: N/A — the service is feature-complete for this use case.

### 7. Disclosure Evaluation Already Maps Participants to Wallets

**Decision**: Extend `ApplyDisclosures` (ActionExecutionService lines 787-817) to resolve wallets via the two-stage mechanism above.
**Rationale**: Currently looks up `instance.ParticipantWallets[participantId]` and warns if not found. After this feature: (a) check instance bindings, (b) if not found, query register participant index, (c) if still not found, warn.
**Alternatives considered**: Separate resolution service — rejected because the disclosure evaluation already does the mapping and adding another layer adds complexity.

### 8. Batch Public Key Resolution Exists

**Decision**: Use existing `IRegisterServiceClient.ResolvePublicKeysBatchAsync()` for encryption recipient key resolution.
**Rationale**: Already supports 1-200 addresses per batch, returns resolved keys + not-found + revoked lists. Hard-fails on revoked participants. Used by `ResolveRecipientKeysAsync` in ActionExecutionService (line 1298-1353).
**Alternatives considered**: N/A — existing implementation is sufficient.
