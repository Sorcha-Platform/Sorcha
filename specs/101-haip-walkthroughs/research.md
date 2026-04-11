# Phase 0 Research: HAIP Walkthroughs

**Feature**: 101-haip-walkthroughs
**Date**: 2026-04-11

## Research items

1. Extend Sorcha.Agent vs standalone tool for HAIP wallet commands?
2. P-256 key storage format for the holder key pair?
3. Credential storage strategy for received SD-JWT VCs?
4. PowerShell orchestration pattern for the walkthroughs?
5. Walkthrough chaining — how does HaipDrivingLicence depend on HaipIdentityAttestation?
6. JWT proof construction — which libraries and primitives to use?

---

## R1. Extend Sorcha.Agent vs standalone tool

### Current state

`Sorcha.Agent` is an existing CLI application (`src/Apps/Sorcha.Agent/`) built on `System.CommandLine` 2.0.2. It currently has two commands: `run` (long-running autonomous actor) and `validate` (actor definition validation). The project already has authentication infrastructure (`Auth/`), configuration management (`Configuration/`), and HTTP client setup for communicating with Sorcha services.

A standalone tool would require duplicating the auth pipeline, HTTP client configuration, and actor definition parsing. It would also require a separate build, separate Docker image, and separate documentation.

### Decision: extend Sorcha.Agent with `haip receive` and `haip present` commands

**Rationale.** The agent already has the auth and HTTP infrastructure. Adding two new commands follows the existing `System.CommandLine` pattern established by `RunCommand.cs` and `ValidateCommand.cs`. The HAIP wallet logic is self-contained in a `Haip/` subdirectory, keeping it cleanly separated from the existing actor-agent logic. The actor definition JSON format can be extended with a `haip` section for holder key configuration without breaking existing definitions.

**Consequence.** Two new command classes in `Commands/`, four new support classes in `Haip/`, one change to `Program.cs` to register the commands. The existing `run` and `validate` commands are unaffected.

**Alternative rejected.** Standalone `sorcha-haip-wallet` CLI tool. Rejected because it would duplicate auth infrastructure, require a separate Docker image, and fragment the tooling story. The agent is already the "external actor" tool — HAIP wallet behaviour is a natural extension of that role.

---

## R2. P-256 key storage — PEM file for private key, JWK JSON for public key

### Current state

The agent currently has no local key storage. All signing operations go through the Wallet Service. For the HAIP external wallet scenario, the agent acts as an independent holder with its own key pair, outside the Sorcha wallet infrastructure.

P-256 (secp256r1) is the HAIP 1.0 mandatory-to-implement algorithm for holder keys. .NET's `ECDsa` class natively supports P-256 key generation, PEM export/import (`ExportECPrivateKey`/`ImportECPrivateKey`), and signing.

### Decision: PEM file for private key, JWK JSON for public key

**Rationale.** PEM is the standard interchange format for EC private keys and .NET has first-class support via `ECDsa.ImportFromPem()` and `ECDsa.ExportECPrivateKeyPem()`. JWK is the format required by the `cnf` claim and the JWT proof `jwk` header — storing the public key as JWK avoids re-serialisation on every use. Both files are human-readable and inspectable, which aids walkthrough debugging.

**File layout:**
```
wallets/citizen/
├── holder_key.pem          # EC private key (PKCS#8 PEM)
├── holder_key.jwk.json     # EC public key (JWK format)
└── credentials/
    ├── VerifiedIdentityCredential.sdjwt
    └── DrivingLicenceCredential.sdjwt
```

**Consequence.** `HolderKeyManager` handles generation (if no PEM exists), loading (if PEM exists), and JWK serialisation. The JWK file is regenerated from the PEM on load to ensure consistency. Key pair generation is idempotent — existing keys are reused (FR-002).

**Alternative rejected.** PKCS#12 (.pfx) container for both keys. Rejected because it requires a password, is binary (not inspectable), and adds complexity for no security benefit in a walkthrough scenario. Also rejected: storing raw key bytes — not portable, not human-readable.

---

## R3. Credential storage — one SD-JWT file per credential type

### Current state

There is no existing credential storage in the agent. The Wallet Service stores credentials in PostgreSQL, but the HAIP external wallet operates independently.

### Decision: one SD-JWT file per credential type in the wallet directory

**Rationale.** The walkthrough scenario has a small number of credential types (2: VerifiedIdentityCredential, DrivingLicenceCredential). File-based storage is the simplest approach that supports the requirement to persist credentials between `haip receive` and `haip present` invocations. The raw SD-JWT compact serialisation is stored as-is — no wrapping, no metadata envelope. The credential type is extracted from the `vct` claim in the SD-JWT payload and used as the filename.

**Consequence.** `CredentialWallet` provides `SaveAsync(rawSdJwt)`, `LoadAsync(credentialType)`, `ListTypes()`, and `ExistsAsync(credentialType)`. The file name is `{credentialType}.sdjwt`. The wallet directory is configurable via the actor definition's `haip.walletDir` field or the `--wallet-dir` command option.

**Alternative rejected.** SQLite database for credential storage. Rejected as over-engineering for a walkthrough tool that manages 2-3 credentials. Also rejected: JSON envelope wrapping the SD-JWT with metadata — the SD-JWT itself contains all metadata (type, claims, issuer) and can be parsed to extract it.

---

## R4. PowerShell orchestration — follows existing walkthrough pattern

### Current state

All existing walkthroughs follow a consistent pattern:
- `setup.ps1` — provisions infrastructure (orgs, users, wallets, registers, blueprints) via the API Gateway
- `run.ps1` — executes the scenario (starts agent processes, waits for completion)
- `actors/*.json` — actor definitions for `sorcha-agent run`
- `state.json` — persisted state between setup and run (IDs, addresses, credentials)
- `SorchaWalkthrough` module (`walkthroughs/modules/`) — shared functions for auth, API calls, health checks, banner output

The module provides `Initialize-SorchaEnvironment`, `Get-SorchaSecrets`, `Write-WtBanner`, `Invoke-SorchaApi`, and profile-based URL resolution (gateway/direct/aspire/n1).

### Decision: follow the existing pattern exactly

**Rationale.** Consistency with ConstructionPermit, SelfBuildHouse, and other walkthroughs. The `setup.ps1` → `state.json` → `run.ps1` flow is well-established and understood by developers. The SorchaWalkthrough module provides all the HTTP plumbing needed for provisioning.

**Consequence.** Two new walkthrough directories following the exact same structure. The `haip receive` and `haip present` commands are invoked from `run.ps1` as `dotnet run --project src/Apps/Sorcha.Agent -- haip receive ...` (or via a built binary). State is passed via `state.json` and command-line arguments.

**Alternative rejected.** Docker Compose-based orchestration for walkthroughs. Rejected because existing walkthroughs run from the host against the Docker stack — changing the pattern would create inconsistency and require the agent to run inside Docker.

---

## R5. Walkthrough chaining — HaipDrivingLicence checks for state.json from HaipIdentityAttestation

### Current state

The existing walkthroughs are independent — each has its own setup.ps1 that provisions everything from scratch. There is no precedent for walkthrough chaining.

### Decision: HaipDrivingLicence setup.ps1 checks for the identity attestation state.json and runs the identity flow inline if missing

**Rationale.** The driving licence walkthrough requires a VerifiedIdentityCredential in the agent's wallet. Rather than duplicating the identity provisioning logic, the setup script checks:
1. Does `walkthroughs/HaipIdentityAttestation/state.json` exist?
2. Does the credential file referenced in that state exist in the wallet directory?

If both checks pass, the driving licence setup reuses the existing citizen identity and wallet. If either check fails, it invokes the HaipIdentityAttestation setup.ps1 and run.ps1 inline.

**Consequence.** The `state.json` format must include `credentialPaths` (a dictionary of credential type to file path) so the downstream walkthrough can verify the credential exists. The identity attestation walkthrough is fully self-contained and can run independently. The driving licence walkthrough has a soft dependency that is resolved automatically.

**Alternative rejected.** Making both walkthroughs completely independent by duplicating the identity issuance in the driving licence setup. Rejected because it would issue a second identity credential (wasting resources) and not exercise the chaining scenario that demonstrates real-world HAIP usage.

---

## R6. JWT proof construction — uses Sorcha.Cryptography primitives, no external JWT library

### Current state

The codebase does not use any external JWT library (e.g., `System.IdentityModel.Tokens.Jwt` or `jose-jwt`). JWT construction in existing code uses manual header + payload + signature assembly with `System.Text.Json` for payload serialisation and `Sorcha.Cryptography` for signing.

The SD-JWT library (`Sorcha.Cryptography.SdJwt`) already handles JWT parsing and base64url encoding/decoding. `ECDsa` in .NET provides `SignData` with `HashAlgorithmName.SHA256` for ES256 signatures.

### Decision: build JWT proofs manually using ECDsa + System.Text.Json, no external JWT library

**Rationale.** Two JWT structures need to be built:

1. **JWT proof of possession** (OID4VCI): header `{"typ":"openid4vci-proof+jwt", "alg":"ES256", "jwk":{...}}`, payload `{"iss":"...", "aud":"...", "iat":..., "nonce":"..."}`. Signed with the holder's P-256 private key.

2. **Key Binding JWT** (OID4VP/SD-JWT): header `{"typ":"kb+jwt", "alg":"ES256"}`, payload `{"aud":"...", "nonce":"...", "iat":..., "sd_hash":"..."}`. Signed with the same holder key.

Both are straightforward: serialise header and payload as JSON, base64url-encode each, concatenate with `.`, sign the result with `ECDsa.SignData()`, base64url-encode the signature, append after the second `.`.

**Consequence.** Two builder classes: `JwtProofBuilder` (for OID4VCI proofs) and `KbJwtBuilder` (for KB-JWTs). Each takes the relevant parameters and the `ECDsa` instance, returns the compact JWT string. The base64url encoding can use `Sorcha.Cryptography`'s existing utilities or `Microsoft.IdentityModel.Tokens.Base64UrlEncoder` if already referenced.

**Alternative rejected.** Adding `System.IdentityModel.Tokens.Jwt` as a dependency. Rejected because it pulls in a large dependency tree for constructing two simple JWTs. The manual approach is ~30 lines of code per builder and keeps the agent lightweight.

---

## Summary

All six research items resolved. No `NEEDS CLARIFICATION` markers remain. Key decisions:

1. **Extend Sorcha.Agent** with `haip receive` and `haip present` commands — reuses auth, CLI framework, and actor definitions.
2. **PEM + JWK files** for P-256 holder key storage — human-readable, portable, .NET-native.
3. **One SD-JWT file per credential type** — simple filesystem storage, credential type from `vct` claim used as filename.
4. **Follow existing walkthrough pattern** — setup.ps1 + run.ps1 + state.json + SorchaWalkthrough module.
5. **Walkthrough chaining via state.json** — HaipDrivingLicence checks for identity credential and runs identity flow inline if missing.
6. **Manual JWT construction** with ECDsa + System.Text.Json — no external JWT library, ~30 lines per builder.

Ready for Phase 1.
