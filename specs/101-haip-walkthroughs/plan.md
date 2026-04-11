# Implementation Plan: HAIP Walkthroughs

**Branch**: `101-haip-walkthroughs` | **Date**: 2026-04-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/101-haip-walkthroughs/spec.md`

## Summary

Extend the existing `Sorcha.Agent` CLI with two new commands (`haip receive` and `haip present`) that act as a minimal HAIP external wallet, then create two end-to-end walkthroughs proving the HAIP issuance and verification pipelines work against the Docker stack.

- **`haip receive`** completes the OID4VCI pre-authorized code flow: fetches issuer metadata, exchanges a pre-auth code for an access token + c_nonce, constructs a JWT proof of possession binding a locally-generated P-256 holder key, submits it to the credential endpoint, and stores the returned SD-JWT VC to disk.
- **`haip present`** completes the OID4VP direct_post flow: fetches a signed request object, selects disclosures from a stored credential, builds a Key Binding JWT (aud, nonce, iat, sd_hash), and submits the VP token via direct_post.
- **HaipIdentityAttestation** walkthrough provisions a Government Identity Authority org that issues a VerifiedIdentityCredential to a citizen via HAIP.
- **HaipDrivingLicence** walkthrough chains off the identity attestation, requiring the citizen to present their identity credential to a Council licensing authority before receiving a DrivingLicenceCredential — exercising both HAIP verification and issuance in a single Blueprint workflow.

## Technical Context

**Language/Version**: C# 13, .NET 10
**Primary Dependencies**: `System.CommandLine` 2.0.2 (existing Sorcha.Agent CLI framework), `Sorcha.Cryptography.SdJwt` (SD-JWT parsing, disclosure selection, KB-JWT hash computation), `System.Security.Cryptography.ECDsa` (P-256 key generation and signing), `System.Text.Json` (JWK/JWT payload construction), `System.Net.Http` (OID4VCI/OID4VP HTTP flows).
**Storage**: Local filesystem only. PEM file for holder private key, JWK JSON for public key, raw SD-JWT files for credentials. No database. State persisted via `state.json` in walkthrough directories.
**Testing**: xUnit, FluentAssertions, Moq. Unit tests for `HolderKeyManager`, `CredentialWallet`, `JwtProofBuilder`, `KbJwtBuilder`. No integration tests required — the walkthroughs themselves serve as E2E validation.
**Target Platform**: net10.0, Docker (walkthroughs run against Docker stack via `http://localhost`).
**Project Type**: Existing multi-service monorepo. Extends `Sorcha.Agent` (existing CLI app). Two new walkthrough directories with PowerShell scripts.
**Performance Goals**: Agent commands complete in < 5 seconds against a healthy Docker stack. Walkthroughs complete in < 2 minutes each.
**Constraints**: Must follow existing walkthrough patterns (setup.ps1 + run.ps1 + state.json + SorchaWalkthrough module). Must reuse existing `System.CommandLine` patterns from `RunCommand.cs` and `ValidateCommand.cs`. No external JWT library — uses `Sorcha.Cryptography` primitives and `ECDsa` directly.
**Scale/Scope**: ~6 new C# files in Sorcha.Agent, ~2 new walkthrough directories with ~3 scripts each, ~4 unit test files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Microservices-First Architecture** | PASS. No new services. Extends an existing CLI application (`Sorcha.Agent`) with two new commands. Walkthrough scripts orchestrate existing services via HTTP. |
| **II. Security First** | PASS. Holder private keys are stored as PEM files in a local wallet directory with no network exposure. The agent never sends the private key over the wire — only the public key (as JWK in proofs) and signatures. |
| **III. API Documentation** | PASS. New commands are self-documenting via `System.CommandLine` descriptions. Walkthrough READMEs document setup and usage. XML comments on all public classes. |
| **IV. Testing Requirements** | PASS. Unit tests for all new HAIP wallet logic (key management, JWT construction, credential storage). The walkthroughs themselves are the integration/E2E test layer. |
| **V. Code Quality** | PASS. Standard C# conventions, nullable enabled, license headers. Follows existing `Sorcha.Agent` patterns. |
| **VI. Blueprint Creation Standards** | PASS. The driving licence Blueprint is created as a JSON file (`driving-licence.json`), following the primary creation policy. |
| **VII. Domain-Driven Design** | PASS. Clean separation: `HolderKeyManager` (key lifecycle), `CredentialWallet` (storage), `JwtProofBuilder` (OID4VCI proofs), `KbJwtBuilder` (OID4VP KB-JWTs). |
| **VIII. Observability by Default** | PASS. Commands use `ILogger` via the existing Sorcha.Agent logging pipeline. Structured log events for key generation, token exchange, credential receipt, and presentation submission. |

**Constitution gate: PASS.** No violations. `Complexity Tracking` section empty.

## Project Structure

### Documentation (this feature)

```text
specs/101-haip-walkthroughs/
├── spec.md              # Feature specification (complete)
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # /speckit.tasks output
```

### Source Code (repository root)

Existing Sorcha multi-service monorepo. Paths touched by this spec:

```text
src/
└── Apps/
    └── Sorcha.Agent/
        ├── Commands/
        │   ├── HaipReceiveCommand.cs          # NEW — `haip receive` command (OID4VCI pre-auth flow)
        │   └── HaipPresentCommand.cs          # NEW — `haip present` command (OID4VP direct_post flow)
        ├── Haip/
        │   ├── HolderKeyManager.cs            # NEW — P-256 key pair generation, PEM/JWK persistence, loading
        │   ├── CredentialWallet.cs             # NEW — file-based SD-JWT VC storage (save, load, list by type)
        │   ├── JwtProofBuilder.cs             # NEW — OID4VCI JWT proof of possession (header + payload + ES256 sign)
        │   └── KbJwtBuilder.cs                # NEW — OID4VP Key Binding JWT (aud, nonce, iat, sd_hash + ES256 sign)
        └── Program.cs                         # CHANGE — register haip receive and haip present commands

walkthroughs/
├── HaipIdentityAttestation/
│   ├── setup.ps1                              # NEW — provision Government org, citizen user, persona, credential offer
│   ├── run.ps1                                # NEW — invoke `sorcha-agent haip receive`, verify credential
│   └── actors/
│       └── citizen.json                       # NEW — actor definition with haip wallet config
├── HaipDrivingLicence/
│   ├── setup.ps1                              # NEW — check identity credential, provision Council org, publish blueprint
│   ├── run.ps1                                # NEW — present identity, receive licence credential
│   ├── actors/
│   │   └── citizen.json                       # NEW — actor definition referencing identity + licence credentials
│   └── blueprints/
│       └── driving-licence.json               # NEW — Blueprint with presentation requirement + credential issuance

tests/
└── Sorcha.Agent.Tests/
    └── Haip/
        ├── HolderKeyManagerTests.cs           # NEW — key generation, PEM round-trip, JWK serialisation
        ├── CredentialWalletTests.cs            # NEW — save, load, list, missing-file handling
        ├── JwtProofBuilderTests.cs            # NEW — proof structure, c_nonce binding, ES256 signature verification
        └── KbJwtBuilderTests.cs               # NEW — KB-JWT structure, sd_hash computation, signature verification
```

**Structure Decision**: Existing monorepo. New code is concentrated in a `Haip/` subdirectory under `Sorcha.Agent` for the wallet logic, with two new command files in the existing `Commands/` directory. Walkthrough scripts follow the established pattern (setup.ps1 + run.ps1 + actors/ + state.json). Unit tests mirror the source structure under the existing `Sorcha.Agent.Tests` project. No new projects, no new service boundaries.

## Complexity Tracking

*No Constitution violations — this section intentionally empty.*
