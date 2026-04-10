# System Register Genesis Trust Anchor

**Date:** 2026-04-10
**Status:** Draft
**Feature:** System Register Genesis Ceremony & Trust Verification

---

## Problem

When a Sorcha instance starts, `SystemRegisterBootstrapper` checks for the system register locally and creates one from scratch if missing. Each instance generates its own wallet with a random mnemonic, signs the genesis with its own keys, and populates the validator roster with its own public key.

On a multi-instance network, this produces N incompatible system registers — same deterministic ID, same structure, but different signatures and validator rosters. Instances can't sync from each other because their validator keys don't match. There is no shared trust anchor.

The system register is the root of trust for the entire platform (blueprints, governance, organisations are seeded into it), so this breaks the foundation of multi-instance deployments.

## Requirements

1. A scripted, repeatable **genesis ceremony** that produces a pre-signed genesis block offline
2. Instances consume the genesis — they never create one at runtime
3. The genesis private key never touches runtime services
4. Peer sync of the system register verifies the genesis signature against a distributed trust anchor
5. Rogue instances cannot forge or substitute a fake system register genesis
6. Different environments (dev, staging, prod) each run their own ceremony
7. The genesis file auto-embeds into the Docker image via the build pipeline
8. Future commissioning model: "which Sorcha network do I join?" is answered by which genesis file you deploy with

## Design

### 1. Genesis Ceremony CLI

New `system-register` command group in `Sorcha.Cli`:

**`sorcha system-register create`**

- Generates a fresh ED25519 keypair (the "genesis key") using `Sorcha.Cryptography` directly — no Wallet Service dependency
- Builds the `RegisterControlRecord` with deterministic `SystemRegisterId` (`aebf26362e079087571ac0932d4db973`)
- Populates the validator roster with the genesis keypair's derived docket-signing public key
- Signs the control record at derivation context `sorcha:register-control`
- Signs the attestations at `sorcha:register-attestation`
- Outputs two files:
  - `src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json` (default `--output` path) — the complete signed genesis transaction
  - `genesis-validator-key.json` (current working directory) — the private key material for the first validator to import
- Prints the genesis public key fingerprint (SHA-256 of public key, truncated) for human verification
- Prints a warning: "Store genesis-validator-key.json securely or destroy it after importing into the first validator. It is not needed for normal operation."

Options:
- `--network-id <name>` — human-readable network label (e.g., `sorcha-prod`, `sorcha-dev`). Embedded in the genesis file. Default: `sorcha-local`.
- `--output <path>` — override output path for the genesis file. Default: `src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json`.
- `--algorithm <algo>` — signing algorithm. Default: `ED25519`.

**`sorcha system-register verify <genesis-file>`**

- Loads a genesis file, verifies all signatures, prints the validator roster and public key fingerprint
- Exit code 0 on success, 1 on failure
- For operators to confirm a genesis file is legitimate before deploying

**`sorcha system-register import-validator-key --key <path>`**

- Imports the genesis validator private key into the local Wallet Service
- The imported key enables the local validator to sign dockets as the rostered genesis validator
- One-time operation for the first validator on a new network
- Requires the Wallet Service to be running — run this after services start but before the bootstrapper needs to seal the genesis docket. The bootstrapper waits for the validator key to be available before attempting to seal.

### 2. Genesis File Format

```json
{
  "version": 1,
  "networkId": "sorcha-prod",
  "genesisTransaction": {
    "txId": "genesis-aebf26362e079087571ac0932d4db973",
    "payload": "<base64url-encoded RegisterControlRecord>",
    "signature": {
      "publicKey": "<base64>",
      "signatureValue": "<base64>",
      "algorithm": "ED25519",
      "signedAt": "2026-04-10T..."
    }
  },
  "validatorRoster": {
    "validators": [
      {
        "validatorId": "<wallet-address>",
        "publicKey": "<base64>",
        "algorithm": "ED25519",
        "derivationContext": "sorcha:docket-signing",
        "status": "Active"
      }
    ],
    "requiredSignatures": 1,
    "version": 1
  },
  "genesisPublicKeyFingerprint": "<sha256-truncated>"
}
```

The `networkId` is logged on startup so operators can confirm which network they're joining.

### 3. Trust Anchor Configuration

**Resolution order:**

1. **Config file path** — `SystemRegister:GenesisFile` in `appsettings.json` or environment variable. Checked first.
2. **Embedded default** — `system-register-genesis.json` embedded as a resource in `Sorcha.Register.Models`. Ships with the build.
3. **Missing** — instance refuses to start with a clear error message.

```json
{
  "SystemRegister": {
    "GenesisFile": "/etc/sorcha/system-register-genesis.json"
  }
}
```

**Build pipeline integration:**

The genesis file lives at `src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json`. The `.csproj` includes it as an embedded resource:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources/system-register-genesis.json" />
</ItemGroup>
```

Running the ceremony with the default `--output` writes directly to this location. Next `dotnet build` embeds it in the assembly. Docker image picks it up with no extra steps.

The `genesis-validator-key.json` (private key) is written to the current working directory — never into the source tree — so it cannot accidentally end up in git or a Docker image.

### 4. Modified Bootstrap Behaviour

`SystemRegisterBootstrapper` changes from "create if missing" to a four-step flow:

**Step 1: Check local state**
Query `RegisterManager.GetRegisterAsync(SystemRegisterId)`. If it exists and the genesis signature matches the trust anchor, proceed normally (seed blueprints, etc.).

**Step 2: Try peer sync**
Ask peers for the system register via the existing replication protocol (`FullReplica` mode). Before accepting the genesis from a peer, verify the genesis transaction's signature against the trusted public key from the genesis file/embedded resource. If signature matches, accept and continue normal sync (chain integrity, docket signatures, etc.). If mismatch, reject and log: "Peer has system register signed by unknown key. Expected fingerprint: X, got: Y."

**Step 3: Load and ingest pre-signed genesis**
Load genesis file (config path → embedded resource), verify signature. Ingest the genesis transaction into the local store. Check if the local validator's docket-signing key is in the validator roster. If yes, submit to local Validator Service for docket sealing and proceed.

**Step 4: Stop**
If the local validator cannot seal the docket (not in roster), or no genesis file was found, or no peers have it: log a clear message and stop the service.

Log messages:
- "System register not available. No peers found and no genesis file configured. Run `sorcha system-register create` to initialize a new network."
- "System register genesis loaded but local validator is not in the validator roster. Import the genesis validator key with `sorcha system-register import-validator-key` or wait for a rostered validator to come online."
- "Peer system register rejected: genesis signed by unknown key. Expected fingerprint: {expected}, got: {actual}."

**No degraded state.** No silent retry loops. The operator must act.

### 5. Peer Sync Verification

Normal register sync via `DocketFinalizationService` extracts the validator roster from the genesis control record and verifies docket signatures against it. This is self-referential — it trusts whatever genesis it receives.

For the system register only, an additional check is applied: the genesis transaction's signature is verified against the trusted public key from the genesis file/embedded resource before the register is accepted.

A new `SystemRegisterSyncVerifier` wraps this check. It only applies to `SystemRegisterConstants.SystemRegisterId` — all other registers sync exactly as before.

```
Peer offers system register
  → Fetch genesis docket (docket 0)
  → Extract genesis transaction
  → Verify signature against trusted genesis public key
  → Match: accept, continue normal sync
  → Mismatch: reject, log fingerprint mismatch
```

### 6. Genesis Validator Key Lifecycle

1. **Ceremony** → produces genesis keypair
2. **First validator** imports `genesis-validator-key.json` → can seal genesis docket
3. **Network bootstraps** → system register operational
4. **Governance proposals** → add real validator keys (`AddValidator`), network grows
5. **Genesis key rotated out** via `RotateValidatorKey` governance proposal → no longer authoritative
6. **Key material destroyed** → ceremony private key is no longer needed

### 7. Future Commissioning Model

The genesis file becomes the network identity token. Joining a Sorcha network is a deliberate choice: "which genesis file do I deploy with?" Different networks (dev, staging, prod, third-party) each have their own genesis ceremony and their own trust anchor. An instance cannot accidentally join the wrong network because the genesis signatures won't match.

## Impact on Existing Code

### Changes

| Component | Change |
|-----------|--------|
| `SystemRegisterBootstrapper` | Rewrite — no longer creates genesis, follows 4-step flow |
| `Sorcha.Cli` | New `system-register` command group (`create`, `verify`, `import-validator-key`) |
| `SystemRegisterConstants` | Add `GenesisPublicKeyFingerprint` for embedded default |
| `Sorcha.Register.Models` | New `SystemRegisterGenesis` model; new `Resources/` embedded resource; `.csproj` update |
| `ServiceDefaults` | New `SystemRegisterOptions` config section |
| `Peer.Service` | New `SystemRegisterSyncVerifier` for genesis signature check |

### No Changes

| Component | Reason |
|-----------|--------|
| `RegisterCreationOrchestrator` | User-created registers still work the same way |
| `DocketBuilder` | Signs dockets the same way |
| `ValidatorKeyCache` | Extracts keys from genesis the same way |
| `GenesisManager` | Creates genesis dockets the same way |
| Governance proposals | Adding/rotating validators unchanged |
| All other register sync | Unaffected |

## Testing

### Unit Tests
- Ceremony: key generation, genesis signing, file output, deterministic register ID
- Genesis file loading: config path, embedded resource fallback, missing file
- Signature verification: valid genesis accepted, tampered genesis rejected, wrong network rejected
- Bootstrapper: all 4 flow paths (local exists, peer sync, genesis ingest + seal, stop)
- `SystemRegisterSyncVerifier`: matching fingerprint accepted, mismatched rejected

### Integration Tests
- Full lifecycle: ceremony → first instance boots → imports validator key → seals genesis docket → second instance syncs from peer → verifies genesis signature → operational
- Negative: second instance with different genesis file rejects peer's system register

## Security Considerations

- **Genesis private key**: never touches runtime config, written to CWD only during ceremony, operator's responsibility to secure/destroy
- **Embedded resource**: the genesis file contains only public data (signed payload, public keys, fingerprints) — safe to commit to git and embed in Docker images
- **Rogue peer protection**: system register genesis is verified against a trust anchor before acceptance — a peer with a different genesis cannot inject a fake system register
- **No fallback to self-creation**: if the trust anchor is missing or invalid, the instance stops rather than creating an unverified genesis
