# Research: System Register Genesis Trust Anchor

**Date**: 2026-04-10 | **Branch**: `099-genesis-trust-anchor`

## Research Decisions

### R-001: Standalone Crypto for Genesis Ceremony

**Decision**: Use `Sorcha.Cryptography.CryptoModule` directly for ED25519 key generation and signing. No Wallet Service dependency.

**Rationale**: `CryptoModule` supports standalone ED25519 operations:
- `GenerateED25519KeySetAsync(seed?)` — random or seeded key generation (lines 438-465)
- `SignED25519Async(hash, privateKey)` — detached signing (lines 467-479)
- `VerifyED25519Async(signature, hash, publicKey)` — verification (lines 481-496)

The ceremony is an offline operation. Requiring a running Wallet Service would defeat the purpose of offline key ceremony.

**Alternatives Considered**:
- NBitcoin BIP32 derivation: Adds unnecessary complexity for a one-shot keypair. The genesis key doesn't need HD derivation — it's a standalone signing key.
- Wallet Service CLI client: Would require services running, contradicts offline ceremony requirement.

### R-002: CLI Command Structure

**Decision**: Add `SystemRegisterCommand` group to existing `Sorcha.Cli` with three subcommands: `create`, `verify`, `import-validator-key`.

**Rationale**: The CLI uses System.CommandLine with hierarchical command groups. Existing patterns:
- `RegisterCommand` → `RegisterListCommand`, `RegisterCreateCommand`, etc.
- `RegisterSystemCommand` → `RegisterSystemStatusCommand`, `RegisterSystemBlueprintsCommand`
- Commands inherit from `Command`, use `SetAction(async (ParseResult, CancellationToken) => {...})`
- Output via `OutputHelper.GetFormatter()` for table/json/csv/yaml

New commands follow the same pattern. `create` and `verify` are standalone (no service dependency). `import-validator-key` requires Wallet Service (uses existing `HttpClientFactory.CreateWalletServiceClientAsync()`).

**Key Files**:
- `src/Apps/Sorcha.Cli/Program.cs` (lines 114-233): Root command setup, global options
- `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs` (lines 1219-1400): Existing system-register commands
- `src/Apps/Sorcha.Cli/Services/HttpClientFactory.cs`: Service client creation
- `src/Apps/Sorcha.Cli/ExitCodes.cs`: Exit code constants

### R-003: Genesis File Embedded Resource Pattern

**Decision**: Place genesis file at `src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json` as an `EmbeddedResource`.

**Rationale**: Existing pattern in `SystemSchemaLoader.cs` (Sorcha.Blueprint.Schemas):
```csharp
var resourceName = $"Sorcha.Blueprint.Schemas.SystemSchemas.{schemaName}.schema.json";
using var stream = assembly.GetManifestResourceStream(resourceName);
```
Resource name follows `{AssemblyNamespace}.{FolderPath}.{FileName}` convention. The `.csproj` needs `<EmbeddedResource Include="Resources/system-register-genesis.json" />`.

**Alternatives Considered**:
- ServiceDefaults project: Would couple all services to genesis. Register.Models is the right home since it owns `SystemRegisterConstants` and `RegisterControlRecord`.
- Content file (CopyToOutput): Would require file system access at runtime. Embedded resource is self-contained in the assembly.

### R-004: Bootstrap Flow Modification

**Decision**: Rewrite `SystemRegisterBootstrapper` to a 4-step flow: check local → peer sync → ingest genesis → stop.

**Rationale**: Current flow in `SystemRegisterBootstrapper.cs`:
- `BootstrapWithRetryAsync()` (lines 80-142): Checks local, then calls `CreateSystemRegisterAsync()`
- `CreateSystemRegisterAsync()` (lines 147-229): Two-phase creation via `RegisterCreationOrchestrator`
- `WaitForGenesisDocketAsync()` (lines 234-264): Polls until Height > 0 (30s timeout)
- `SeedBlueprintsIfMissingAsync()` (lines 269-326): Seeds default blueprints

New flow replaces `CreateSystemRegisterAsync()` with `IngestPreSignedGenesisAsync()` which:
1. Loads genesis file (config path → embedded resource)
2. Verifies genesis signature using `CryptoModule.VerifyAsync()`
3. Builds `TransactionSubmission` from pre-signed data
4. Submits to Validator Service via `IValidatorServiceClient.SubmitTransactionAsync()`
5. Waits for docket confirmation (reuses `WaitForGenesisDocketAsync()`)

**Key Dependencies**:
- `IValidatorServiceClient.SubmitTransactionAsync()`: POST `/api/v1/transactions/validate`
- `TransactionSubmission` record: TxId, RegisterId, Payload (JsonElement), PayloadHash, Signatures, CreatedAt
- `SignatureInfo` record: PublicKey, SignatureValue, Algorithm
- Signed data format: `SHA256(UTF8("{TxId}:{PayloadHash}"))`

### R-005: Peer Sync Verification Hook

**Decision**: Inject `ISystemRegisterSyncVerifier` into `DocketFinalizationService` to add genesis signature verification for system register only.

**Rationale**: The 5-step finalization pipeline in `DocketFinalizationService.FinalizeAsync()` (lines 105-173):
1. Ensure validator key cached
2. Verify chain integrity (PreviousHash)
3. Verify docket hash (recompute)
4. Verify proposer signature (cryptographic)
5. Persist to Register Service

The system register check hooks in **before step 1** for genesis dockets (Version 0). It verifies the genesis transaction's control record signature against the trusted public key from the genesis file.

**Key observation**: Genesis dockets (Version 0) already have a special exemption at line 109 — they bypass the validator key requirement. The new verifier adds a **positive check** (must match trust anchor) rather than just allowing the exemption.

**Integration point**: `DocketFinalizationService` constructor already takes `ValidatorKeyCache`, `ICryptoModule`, etc. Adding `ISystemRegisterSyncVerifier` follows the same DI pattern. Register in `Program.cs` (lines 155-187).

### R-006: Validator Key Import Mechanism

**Decision**: Add a new Wallet Service endpoint for raw key import, exposed via CLI `import-validator-key` command.

**Rationale**: Current Wallet Service supports wallet creation (random mnemonic) and recovery (from mnemonic), but no raw private key import. The genesis ceremony produces a standalone ED25519 keypair, not a BIP39 mnemonic. The import endpoint accepts the raw key material and creates a wallet entry the validator can use for docket signing.

**Alternatives Considered**:
- Generate a mnemonic during ceremony and use existing recovery: Adds unnecessary complexity. The genesis key is a one-shot signing key, not an HD wallet.
- File-based key loading at Validator startup: Would bypass the Wallet Service abstraction. All key material should flow through Wallet Service.
