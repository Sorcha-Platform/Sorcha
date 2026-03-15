# Research: System Register as Real Ledger

**Date**: 2026-03-15
**Feature**: 057-system-register-ledger

## R1: Server-Side Register Creation (Bootstrapping Without UI)

**Decision**: The `SystemRegisterBootstrapper` will call the `RegisterCreationOrchestrator` directly (both live in the Register Service process), using the existing two-phase flow: `InitiateAsync` → sign attestation with system wallet → `FinalizeAsync`.

**Rationale**: The orchestrator already handles genesis transaction construction, system wallet signing, validator submission, and register persistence. Reusing it avoids duplicating ~200 lines of transaction creation logic and ensures the system register is created identically to user registers.

**Alternatives considered**:
- **Direct validator submission (bypass orchestrator)**: Rejected — would duplicate canonical JSON serialization, hash computation, and transaction model construction.
- **New dedicated bootstrap method on orchestrator**: Rejected — unnecessary when InitiateAsync/FinalizeAsync already support server-side callers (system wallet can sign attestations programmatically).

**Implementation notes**:
- `InitiateAsync` returns `AttestationsToSign` with hash data to sign
- Bootstrapper signs each attestation hash using `ISystemWalletSigningService` (derivation path: `sorcha:register-control`)
- `FinalizeAsync` verifies signatures, builds genesis transaction, submits to validator
- The deterministic register ID is passed via the `InitiateRegisterCreationRequest.RegisterId` field (if supported) or the bootstrapper sets it before calling initiate

## R2: Blueprint Publication as Transactions

**Decision**: Blueprint publication uses `IValidatorServiceClient.SubmitTransactionAsync` with the blueprint JSON as the transaction payload. Transaction metadata includes `TransactionType = Action` and `BlueprintId = "system-blueprint-publish"` to distinguish blueprint transactions from the genesis control record.

**Rationale**: Using the standard transaction submission flow ensures blueprints get the same cryptographic guarantees (validation, docket sealing, replication) as all other register data.

**Alternatives considered**:
- **New transaction type enum value**: Rejected — adding `Blueprint = 4` to `TransactionType` is a larger change. Using `Action` type with metadata markers is sufficient and non-breaking.
- **Direct MongoDB write followed by validator submission**: Rejected — contradicts the goal of using the real ledger.

**Implementation notes**:
- Blueprint JSON payload is serialized to canonical JSON, hashed, and included in `TransactionSubmission.Payload`
- `TransactionSubmission.Metadata["Type"] = "BlueprintPublish"` identifies blueprint transactions
- `TransactionSubmission.Metadata["BlueprintId"]` contains the blueprint's logical ID (e.g., "register-creation-v1")
- For version chains: `TransactionSubmission.PreviousTransactionId` references the previous blueprint version's transaction ID
- Transaction ID is computed as SHA-256 of `"blueprint-{blueprintId}-{timestamp}"` for uniqueness

## R3: Blueprint Query Strategy

**Decision**: `SystemRegisterService.GetAllBlueprintsAsync` queries the register's transactions, filtering by metadata type `"BlueprintPublish"`. The convenience API endpoints continue to return the same response shape.

**Rationale**: The register already stores transactions with payloads and metadata. Querying by metadata type is a natural filter that works with existing transaction query infrastructure.

**Alternatives considered**:
- **Maintain a local cache/index of blueprints**: Rejected for MVP — adds complexity. Can be added later if performance requires it.
- **Use OData queries on the register**: Possible but the convenience endpoints provide a simpler, purpose-built API.

**Implementation notes**:
- `IRegisterServiceClient` or direct MongoDB query against the system register's transaction collection
- Filter: `MetaData.TransactionType == Action` AND `Metadata["Type"] == "BlueprintPublish"`
- Blueprint payload extracted from `Payloads[0].Data` (base64url decode → JSON)
- Blueprint version derived from transaction chain (count predecessors or use metadata)

## R4: Idempotent Bootstrap

**Decision**: On startup, the bootstrapper checks if the system register exists in the register registry. If it exists, bootstrap is skipped. If seed blueprints are missing (register exists but no blueprint transactions), only the blueprint submission is retried.

**Rationale**: Idempotency is critical for production reliability — restarts must not create duplicate registers or transactions.

**Implementation notes**:
- Check: `IRegisterManager.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId)` — if non-null, register exists
- Check: Query transactions on system register for blueprint metadata — if seed blueprints found, skip
- If register exists but seed blueprints missing: Submit blueprint transactions only
- If register doesn't exist: Full bootstrap (create register + submit blueprints)

## R5: Waiting for Docket Sealing Before Blueprint Submission

**Decision**: After genesis submission, the bootstrapper polls for the genesis docket to be sealed before submitting blueprint transactions. This ensures the register is fully operational (has height > 0) before accepting blueprint transactions.

**Rationale**: The validator builds dockets asynchronously. Submitting blueprint transactions before the genesis docket is sealed could fail if the validator hasn't processed the genesis yet.

**Alternatives considered**:
- **Submit blueprints immediately (fire-and-forget)**: Rejected — transactions submitted before genesis is processed may be rejected by the validator.
- **Use SignalR notification for docket sealed**: Over-engineered for a startup task.

**Implementation notes**:
- Poll `IRegisterServiceClient.GetRegisterHeightAsync(registerId)` until height > 0
- Polling interval: 1 second, timeout: 30 seconds
- If timeout: Log warning and retry blueprint submission on next startup

## R6: Derivation Path for Blueprint Signing

**Decision**: Add `"sorcha:blueprint-publish"` to the system wallet signing whitelist for blueprint transactions. This gives blueprint signing a distinct derivation path from register control operations.

**Rationale**: Separation of derivation paths follows the HD wallet principle of purpose-specific keys. Register control and blueprint publishing are distinct operations.

**Alternatives considered**:
- **Reuse `sorcha:register-control`**: Simpler but conflates two different signing purposes.

**Implementation notes**:
- Add to `AllowedDerivationPaths` in `SystemWalletSigningOptions`
- Bootstrapper uses `"sorcha:blueprint-publish"` when signing blueprint transactions
