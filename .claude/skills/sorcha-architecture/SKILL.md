---
name: sorcha-architecture
description: |
  Sorcha feature-specific API references, domain models, and cross-cutting architectural patterns that don't belong in service-level skills.
  Use when: working on or extending any of the features documented below (Participant Identity, Register Invitations, Trust Hardening, Stored Data / file attachments, Validator Roster, Org Key Derivation, Platform Org Topology, Consumer Persona, System Register Genesis, Open Participants / late binding, x-review / credential id-cards, ownership-agnostic submission / derived relationship, Timebound Presentation Lifecycle, Transactional Email / welcome dispatcher, Storage Provider Audit / IStorageRegistrationLog, Atomic Distributed Cache / IAtomicDistributedCache, Validator Mempool Durability / IVerifiedTransactionQueue lease pattern). Also use when you need a concise catalogue of well-known IDs, DID shapes, or the endpoint surface for these features.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# Sorcha Architecture Skill

Feature-specific endpoint surfaces, models, and cross-cutting patterns referenced from the project root but kept out of the auto-loaded `CLAUDE.md`. Load this when touching any of the features listed in the description.

For implementation patterns scoped to a single technology (EF, Aspire, Blazor, etc.) use the matching technology skill. For blueprint-authoring concerns (schemas, routes, participants) use `blueprint-builder`.

Authoritative sources referenced throughout: feature specs under `specs/{id}-{slug}/`, service-level skills under `.claude/skills/`, and detailed endpoint docs in `docs/reference/API-DOCUMENTATION.md`.

---

## Participant Identity API

The Participant Identity Registry bridges Tenant Service users with Blueprint workflow participants and their Wallet signing keys.

### Endpoints (via API Gateway /api/*)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/organizations/{orgId}/participants` | Register participant (admin) |
| GET | `/organizations/{orgId}/participants` | List org participants |
| GET | `/organizations/{orgId}/participants/{id}` | Get participant details |
| PUT | `/organizations/{orgId}/participants/{id}` | Update participant |
| DELETE | `/organizations/{orgId}/participants/{id}` | Deactivate participant |
| POST | `/participants/search` | Search across accessible orgs |
| GET | `/participants/by-wallet/{address}` | Lookup by wallet address |
| POST | `/participants/{id}/wallet-links` | Initiate wallet link challenge |
| POST | `/participants/{id}/wallet-links/{challengeId}/verify` | Verify wallet signature |
| GET | `/participants/{id}/wallet-links` | List linked wallet addresses |
| DELETE | `/participants/{id}/wallet-links/{linkId}` | Revoke wallet link |
| POST | `/me/register-participant` | Self-register as participant |
| GET | `/me/participant-profiles` | Get all user's participant profiles |

### On-Register Participant Publishing (Tenant Service)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/organizations/{orgId}/participants/publish` | Publish participant record to register |
| PUT | `/organizations/{orgId}/participants/publish/{participantId}` | Update published participant record |
| DELETE | `/organizations/{orgId}/participants/publish/{participantId}` | Revoke published participant record |

### Published Participant Queries (Register Service)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/registers/{registerId}/participants` | List published participants (status filter, pagination) |
| GET | `/registers/{registerId}/participants/by-address/{walletAddress}` | Get participant by wallet address |
| GET | `/registers/{registerId}/participants/{participantId}` | Get participant by ID |
| GET | `/registers/{registerId}/participants/by-address/{walletAddress}/public-key` | Resolve public key for encryption |

### Key Models

- **ParticipantIdentity**: User + Organization + Status + DisplayName
- **LinkedWalletAddress**: WalletAddress + VerifiedAt + Status (max 10 per participant)
- **WalletLinkChallenge**: Nonce + Expiration (5 min) for signature verification
- **PublishedParticipantRecord**: On-register identity with addresses, version, status
- **PublicKeyResolution**: Resolved public key for field-level encryption (410 Gone if revoked)

### Service Client

```csharp
// Use IParticipantServiceClient from Sorcha.ServiceClients
var participant = await participantClient.GetByIdAsync(orgId, participantId);
var canSign = await participantClient.ValidateSigningCapabilityAsync(orgId, participantId);
```

---

## Register Invitation API

Private register invitation system using cryptographic envelopes (ED25519 sign + X25519 encrypt via Wallet Service). Register owners invite organizations by DID; target orgs accept by decrypting and verifying the token.

### Endpoints (via API Gateway /api/*)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/organizations/{orgId}/register-invitations` | Create signed+encrypted invitation (admin) |
| POST | `/organizations/{orgId}/register-invitations/accept` | Accept invitation token (admin) |
| GET | `/organizations/{orgId}/register-invitations` | List invitations (filter: sent/received/all) |
| DELETE | `/organizations/{orgId}/register-invitations/{invitationId}` | Revoke pending invitation (admin) |

### Key Models

- **RegisterInvitationRecord**: SourceOrgId + TargetOrgDid + RegisterId + Nonce + Status (Pending/Accepted/Revoked/Expired) + ExpiresAt
- **InvitationNonce**: Replay protection via unique DB index on consumed nonces
- **InvitationTokenEnvelope**: Version + ED25519 Signature + X25519 EncryptedPayload + SenderDID
- **InvitationPayload**: RegisterId + SourceOrgDid + TargetOrgDid + Nonce + ExpiresAt + Names
- **SorchaDidIdentifier.Organization**: `did:sorcha:org:{walletAddress}` — DID type for org identity

### Crypto Flow

1. **Create**: Serialize payload → encrypt to target wallet (X25519) → sign encrypted blob (ED25519) → base64 envelope token
2. **Accept**: Decode token → verify sender signature → decrypt with target wallet → validate nonce/expiry/target → create `SubscriptionType.Invited` subscription

---

## Trust Hardening API (Feature 079)

Transaction receipts, Merkle inclusion proofs, revocation transactions, and offline verification bundles. All operate on transaction envelopes (FLE-compatible).

### Transaction Receipts & Proofs (Register Service)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/registers/{registerId}/receipts/batch` | Store receipt batch (internal) |
| GET | `/registers/{registerId}/transactions/{txId}/receipt` | Get receipt by tx ID |
| GET | `/registers/{registerId}/dockets/{docketNumber}/receipts` | List docket receipts |
| POST | `/registers/{registerId}/receipts/verify` | Verify receipt (public) |
| GET | `/registers/{registerId}/transactions/{txId}/inclusion-proof` | Generate Merkle proof |
| POST | `/registers/{registerId}/inclusion-proofs/verify` | Verify proof (public) |

### Revocation & Status (Register Service)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/registers/{registerId}/transactions/revoke` | Submit revocation |
| GET | `/registers/{registerId}/transactions/{txId}/status` | Get lifecycle status (active/revoked/superseded) |

### Verification Bundles (Register Service)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/registers/{registerId}/transactions/{txId}/verification-bundle` | Export portable bundle |
| POST | `/registers/{registerId}/verification-bundles/verify` | Verify bundle (public) |

### Key Models

- **TransactionReceipt**: Signed attestation of finality with embedded Merkle inclusion proof and validator signature(s)
- **MerkleInclusionProof**: Compact proof (log2(n) steps) of transaction inclusion in a docket
- **RevocationPayload**: Revocation reason + target tx reference (Superseded/Erroneous/Compromised/Expired/Withdrawn/Regulatory)
- **VerificationBundle**: Portable package (VC + receipt + proof + revocation status) for offline verification
- **TransactionLifecycleStatus**: Active, Revoked, or Superseded

### Transaction Lifecycle Ticks (Wallet Service)

WhatsApp-style delivery indicators tracked per-wallet:
- Grey tick: Submitted (Pending)
- Blue tick: Sealed in docket (Confirmed)
- Double blue ticks: Receipt confirmed (Receipted)

`WalletTransaction` entity tracks both outbound (signed) and inbound (recipient) transactions.

---

## Stored Data Transactions API (Feature 085)

File attachments as first-class fields in blueprint action schemas. Files are transparently chunked (≤4MB), encrypted with HKDF-SHA256 derived per-chunk keys (XChaCha20-Poly1305), and submitted as staged transactions. The Wallet Service mediates file retrieval.

### File Chunk Submission (Blueprint Service)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/file-chunks` | Submit encrypted file chunk (staged, pre-action) |

### File Download (Wallet Service)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/v1/wallets/{address}/files/download` | Fetch, decrypt, reassemble, stream file |

Query params: `registerId`, `actionTxId`, `fieldName`, `fileIndex` (default 0)

### Blueprint Schema Extension

File fields use `format: "file-reference"` with `x-file` extension:
```json
{
  "sitePhoto": {
    "type": "string",
    "format": "file-reference",
    "x-file": { "accept": ["image/jpeg"], "maxSizePerFile": "16MB", "maxChunks": 10 }
  }
}
```

### Key Models

- **FileReference**: Runtime value in action payload (fileName, contentType, size, hash, salt, chunkTransactionIds, masterKeyId)
- **FileChunkMetadata**: Per-chunk transaction metadata (type="file-chunk", chunkIndex, totalChunks, fileHash)
- **FileSchemaExtension**: Blueprint schema x-file extension (accept, maxSizePerFile, maxChunks)
- **Limits**: 4MB chunks, 10 max per file, 40MB ceiling, 30-min orphan timeout

### Encryption Flow

1. Server generates random `MasterFileKey` + `salt` per file upload session
2. Each chunk encrypted with `HKDF-SHA256(MasterFileKey, salt, "sorcha-chunk-{n}")` → XChaCha20-Poly1305
3. `MasterFileKey` wrapped per recipient in action payload Challenges
4. Download: Wallet Service unwraps key, derives chunk keys, decrypts, reassembles, verifies SHA-256

---

## Validator Key Roster (Feature 086)

Register genesis control records include a `validators` field declaring authorized docket signing keys. Remote peers extract these keys to verify synced dockets.

### Key Design Points
- **Signing key**: Purpose-derived from system wallet using `"sorcha:docket-signing"` derivation context (distinct from `"sorcha:register-control"` used for genesis transactions)
- **DocketBuilder**: Signs with `SignTransactionAsync(walletAddress, hash, "sorcha:docket-signing", isPreHashed: true)` — NOT the root wallet key
- **ValidatorRoster**: List of `ValidatorRosterEntry` (1-10 entries) + `RequiredSignatures` (default 1) + `Version`
- **ValidatorKeyCache**: Multi-key roster per register; `IsAuthorizedSigner(registerId, publicKey)` checks Active + Rotated keys
- **Governance**: `AddValidator`, `RemoveValidator`, `RotateValidatorKey` operation types on the existing governance proposal endpoint
- **External roster (FR-014)**: Register creation accepts optional external validator list for future System Register (087)
- **Shared-wallet contract**: Register.Service (`SystemWalletSigning:ValidatorId`) and Validator.Service (`Validator:ValidatorId`) MUST be configured with the same identifier on a given node. Both call `IWalletServiceClient.CreateOrRetrieveSystemWalletAsync` with that string; wallets are keyed by it. Register.Service uses the resulting wallet to populate the `sorcha:docket-signing` pubkey on new registers' rosters; Validator.Service uses it to sign dockets. Divergent IDs → different derived keys → validator never matches its own roster entry → dockets never seal. In docker-compose this is `local-validator`.
- **#1397 — `validator:*`-owned system wallets may only be signed by the Validator service principal.** `WalletEndpoints.SignTransaction`'s service-token ownership bypass (legitimate for e.g. Blueprint Service signing an issuing org's wallet during credential issuance) is narrowed: when the target wallet's `Owner` starts with `validator:`, the caller's `client_id` claim must equal `validator-service` (the Validator principal's seeded `ClientId` in `Sorcha.Tenant.Service.Data.DatabaseInitializer` — NOT `service-validator`) or the request is refused with `403` + a `SEC-AUDIT` log. Every other service-token sign target is unaffected.

---

## Org Key Derivation API (Feature 083)

Organisation-level HD key derivation using Sorcha-specific BIP32 paths (`m/0x534F52'/org'/dept'/user'/usage/index`). Custodial mode with pluggable seed protection.

### Endpoints (Wallet Service)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/wallets/org/{orgId}/master-key` | Provision org master key (one-shot, returns mnemonic once) |
| POST | `/api/wallets/org/{orgId}/derive-key` | Derive user key (idempotent) |
| POST | `/api/wallets/org/{orgId}/keys/{derivedKeyId}/rotate` | Rotate key (new at next index, old decrypt-only) |
| DELETE | `/api/wallets/org/{orgId}/keys/{derivedKeyId}` | Revoke key (wallet locked, DID event for identity keys) |

### Key Models

- **OrgMasterKey**: Organisation root seed, encrypted at rest, one per org
- **DerivedKeyRecord**: User key derived from org master, tracks path/usage/index/status
- **KeyUsage**: Identity (0), VCIssuance (1), Governance (2), Communications (3), ServiceAuth (4)
- **CustodyMode**: Custodial (implemented), CoSigned (schema only), SelfCustody (schema only)

---

## Platform Organisation Topology API

Three-tier org topology: system admin org, public org (social login + email/password), and private orgs. `PlatformUser` is the cross-org identity anchor; `UserIdentity` handles per-org authorisation.

### Well-Known Organisation IDs

| ID | Purpose |
|----|---------|
| `00000000-0000-0000-0000-000000000001` | System Admin Org |
| `00000000-0000-0000-0000-000000000002` | Public Org |

### Platform Management Endpoints (SystemAdmin only)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/platform/organizations` | List all orgs (paginated, status filter) |
| PUT | `/platform/organizations/{orgId}/status` | Update org status (Active/Suspended) |
| GET | `/platform/organizations/{orgId}/users` | Audit org users (read-only) |
| POST | `/platform/organizations` | Create org with admin invite |
| GET | `/platform/settings` | Get platform settings |
| PUT | `/platform/settings/public-org` | Enable/disable public org |

### Authentication & Org Switching Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/auth/social/initiate` | Start social login OAuth flow |
| POST | `/auth/social/callback` | Complete social login callback |
| POST | `/auth/register` | Email/password signup (public org) |
| GET | `/auth/me/organizations` | List user's org memberships |
| POST | `/auth/switch-org` | Switch active org (re-issues JWT) |

### Platform Identity Models

- **PlatformUser**: Cross-org identity with email uniqueness, social logins, passkey credentials
- **PlatformSocialLogin**: OAuth provider links (Google, GitHub, Microsoft, Apple)
- **PlatformUserOrgMembership**: Maps platform users to org-scoped roles
- **PlatformSettings**: Platform governance (public org enable/disable, max orgs per user)

---

## Consumer Persona API (Feature 092)

Per-user identity persona stored as ciphertext in Tenant Service with the content key derived by Wallet Service under `sorcha:persona-vault`. Read side returns attributes wrapped in `PersonaAttribute<T>` carrying provenance. `SorchaFormRenderer` consumes the persona to autofill recognised form fields with a cream tint and a visible `self` provenance tick. Edit releases the claim. A global toggle switches silent apply to a one-click "Fill from profile" button.

### Endpoints (via API Gateway /api/*)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/me/persona` | Read the signed-in user's persona (returns empty for new users, never 404) |
| PUT | `/me/persona` | Replace the persona with a full `PersonaAttributesV1` payload |
| DELETE | `/me/persona` | Delete the persona row (idempotent) |

Internal (not routed through gateway):

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/v1/wallets/{address}/persona/encrypt` | Derive content key, encrypt payload |
| POST | `/api/v1/wallets/{address}/persona/decrypt` | Derive content key, decrypt ciphertext |

### Key Models

- **PersonaAttributesV1**: Plaintext write-side shape — givenName, familyName, fullName, dateOfBirth, emails, phones, addresses, nationalities. Each multi-value list capped at 5 with exactly one default.
- **PersonaReadModelV1**: Wire shape with `Default*` + `All*` pairs for multi-value attributes, each wrapped in `PersonaAttribute<T>`.
- **PersonaAttribute<T>**: `Value`, `Source` (`SelfAsserted`/`VerifiedCredential`), `VerifiedBy` (issuer DID, always null in v1), `LastUpdated`.
- **PlatformUserPersona**: EF entity — 1:1 with `PlatformUser`, hard-delete cascade, XChaCha20-Poly1305 ciphertext with 24-byte nonce and `wrappedKeyRef == walletAddress`.
- **PersonaFillResult**: Per-field autofill decision carried by the resolver (field path, attribute name, value, source, `PersonaMatchMode`).

### Schema Extension

Form authors can pin a field to a specific persona attribute via a JSON-Schema extension:

```json
{
  "applicantEmail": { "type": "string", "format": "email", "x-persona": "defaultEmail" },
  "nextOfKinEmail": { "type": "string", "format": "email", "x-persona": false }
}
```

Without an explicit tag, the conservative inference allowlist applies: `format: "email"` → default email, `format: "tel"` → default phone, field names `dateOfBirth`/`dob`/`birthDate` → date of birth, postal-address object shape → default address.

### Cryptography

- **Derivation purpose**: `sorcha:persona-vault` (BIP44-style index 104 under the `SorchaDerivationPaths` constants).
- **AEAD**: XChaCha20-Poly1305 via the existing `ISymmetricCrypto`, 24-byte nonce.
- **HKDF**: Per-file chunk keys derived with HKDF-SHA256 in `PersonaCryptoService`.
- **Ciphertext location**: Tenant DB only. Content key never leaves Wallet Service. Reading requires a service token carrying `RequirePersonaCrypto` policy.

### Client surface

```csharp
// Sorcha.UI.Core.Services.Persona.IPersonaService — session-cached client facade
var persona = await personaService.GetAsync();
await personaService.UpdateAsync(newAttributes);
await personaService.SetAutofillEnabledAsync(false);
```

`SorchaFormRenderer` resolves fills via `PersonaAutofillResolver` and renders a disclosure summary banner (`PersonaFillSummary`) above the form with Review and Clear all actions. When the global autofill toggle is off, the same banner renders a one-click "Fill from profile" button instead. See `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyProfile.razor` for the user-facing page.

---

## System Register Genesis Trust Anchor (Feature 099)

The system register is bootstrapped from a pre-signed genesis block produced by an offline ceremony. Instances never create the genesis at runtime — they consume it from a genesis file (config path or embedded resource).

### Genesis Ceremony CLI

```bash
# Create a new network genesis (offline — no services needed)
sorcha system-register create --network-id sorcha-dev

# Outputs:
#   system-register-genesis.json  → embed in source tree or deploy as config
#   temp/genesis-validator-key.json → import into first validator, then destroy
#                                     (the gitignored /temp — it is private key material)

# Verify a genesis file
sorcha system-register verify path/to/system-register-genesis.json

# Import validator key into running Wallet Service (first validator only)
sorcha system-register import-validator-key --key temp/genesis-validator-key.json
```

### Bootstrap Flow (Register Service)

1. **Check local** — system register exists? Proceed normally.
2. **Try peer sync** — sync from peers, verify genesis signature against trust anchor.
3. **Ingest genesis** — load pre-signed genesis file, submit to Validator Service.
4. **Stop** — if no genesis file and no peers, log actionable message and halt.

### Configuration

```json
{
  "SystemRegister": {
    "GenesisFile": "/etc/sorcha/system-register-genesis.json"
  }
}
```

When `GenesisFile` is null, the embedded resource in `Sorcha.Register.Models` is used.

### Key Files

| File | Purpose |
|------|---------|
| `src/Common/Sorcha.Register.Models/Genesis/` | Genesis file models and loader |
| `src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json` | Embedded dev genesis |
| `src/Services/Sorcha.Register.Service/Services/GenesisIngestionService.cs` | Load, verify, submit genesis |
| `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs` | 4-step bootstrap flow |
| `src/Services/Sorcha.Peer.Service/Replication/SystemRegisterSyncVerifier.cs` | Peer genesis trust check |
| `src/Apps/Sorcha.Cli/Commands/SystemRegisterCommands.cs` | CLI ceremony commands |

---

## Cross-Cutting Pattern: Open Participants & Late Binding (Feature 103)

Citizen-facing services accept a walk-in public user as the applicant without requiring a pre-existing participant record. The contract lives in three places and must be honoured end-to-end:

1. `Action.IsStartingAction = true` is the **open** flag. Any authenticated wallet may submit; the first qualifying submitter is late-bound to the action's `Sender` participant for the life of the instance. Re-binding is immutable — a second submission from a different wallet throws.
2. The participant referenced by `Action.Sender` on a starting action MUST have `Participant.WalletAddress = null` in the published blueprint. Pre-baking a wallet is the foot-gun the publish-time guardrail **`VAL_BP_010`** exists to catch.
3. Walkthrough authors MUST NOT include open participants in their `$walletMap`. The correct shape is to omit the citizen/applicant entry entirely and let the runtime late-bind.

```powershell
# CORRECT shape for citizen-facing walkthroughs:
$walletMap = @{
    "verification-analyst" = $analystWallet.Address
    # "citizen" is intentionally absent — late-bound at runtime
}
```

Credential-bootstrapped flows (e.g. Driving Licence requiring a Verified Citizen credential) layer `credentialRequirements` on the open starting action — the HAIP presentation gate fires *before* the late-bind block, so only credential holders become the bound applicant.

Runtime source (line numbers indicative, verified 2026-06 — grep the method bodies if a citation misses): `ValidationEngine.cs:~1352` (validator skips strict wallet check for starting actions), `ActionExecutionService.cs:~236-248` (strict check fires only when `WalletAddress` non-null), `ActionExecutionService.cs:~419-452` (late-bind block, persisted via `IInstanceStore.UpdateAsync`). Authoritative documentation: `.claude/skills/blueprint-builder/SKILL.md` → "Open Participants & Late Binding" section. Feature design: `specs/103-verified-citizen-v2/`.

---

## Cross-Cutting Pattern: Review Summary (`x-review`) — Feature 107

Mark a wizard page as a read-only summary of the form's prior pages. The renderer draws a stylised credential id-card previewing what the citizen will receive once issued. The same component renders the assessor's pending-review screen and the issued credential's wallet detail view — one component, three states, with the watermark (`Draft` / `Pending` / `Issued` / `None`) derived from the hosting action's runtime state.

```jsonc
{
  "title": "Review your details",
  "x-review": {
    "layout": "id-card",                  // v1 only; passport-page / tabular / receipt reserved
    "editable": true,                     // Generates Edit-X per section
    "header": {
      "issuerName": "Acme Verification Co.",
      "credentialName": "Assured Identity",
      "colourTheme": "identity-navy"     // v1: identity-navy | licence-pink
    }
  }
}
```

**Stacked-cards variant** fires automatically when the hosting action declares both `credentialRequirements` and `credentialIssuanceConfig` — the renderer draws two id-cards on the review page (presented identity above with a ✓ Verified chip, credential-to-be below with a Pending watermark).

**Portrait capture** rides the same renderer via two extensions on `x-file`: `capture: "user"` requests the front-facing camera on mobile; `embedAs: "image-token-jpeg-240x320"` triggers the client-side resizer, producing a base64 JPEG token at `{fieldPointer}/tokenImageBase64` alongside the chunked full-resolution original. Server-side gate in `ActionExecutionService.BuildClaimsFromMappings` enforces ≤27KB base64; oversize → claim omitted with `WARN_CRED_PORTRAIT_OVERSIZE_001`, credential still issues.

Runtime source: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/ReviewSummaryRenderer.razor`, `IdCardLayout.razor`, `SchemaLayoutParser.cs`. Authoritative documentation: `.claude/skills/blueprint-builder/SKILL.md` → `x-review` + `x-file.capture`/`embedAs` sections. Contract: `specs/107-assured-identity-v1/contracts/x-review-extension.md`.

---

## Cross-Cutting Pattern: Ownership-agnostic submission + derived relationship (Feature 108)

Register.Service is the authoritative source of per-register state on each installation. `RegisterLocalRelationship` is derived on demand from the latest control record (genesis + governance control transactions) plus the node's local wallet/validator key — not stored as a flag. `Register.SyncState` is a typed enum (`Indeterminate` / `Syncing` / `CaughtUp` / `Error`) derived from local docket height, peer-advert high-water-mark, and validator sealing progress.

**Submission rule (Blueprint.Service):** `ActionExecutionService` makes two concurrent calls on every submission — `IValidatorServiceClient.SubmitTransactionAsync` (local mempool) and `IPeerServiceClient.DistributeTransactionAsync` (peer fan-out to source peers via the `TransactionDistribution.SubmitTransaction` gRPC RPC). No ownership-aware branching — each downstream service uses its derived relationship to decide what to do. Validator seals iff on the roster; peer fan-out reaches the owner when local is a subscriber.

**Validator enrolment (Validator.Service):** `IRegisterMonitoringRegistry` is populated by `RegisterMonitoringBootstrap` at startup + on `register:relationship-changed` Redis events + every 5-minute safety poll. The previous side-effect enrolment from `/api/v1/transactions/validate` is removed — subscribers never attempt to seal, eliminating chain-fork risk.

**Observation intake (Register.Service):** Peer.Service pushes `PeerHeightObservation` on every advert ingest; Validator.Service pushes `ValidatorSealingObservation` on docket seal. Neither is persisted — they feed the in-memory `IObservationStore` that `RegisterSyncStateResolver` consumes.

Endpoints: `GET /api/registers/{id}/local-relationship`, `GET /api/registers/{id}/sync-state`, `GET /api/internal/my-validated-registers` (requires `X-Validator-Public-Key` header). Internal intake: `POST /api/internal/registers/{id}/peer-height-observation`, `POST /api/internal/registers/{id}/validator-observation`, `POST /api/internal/peer/distribute/{id}`.

Runtime source: `src/Core/Sorcha.Register.Core/LocalRelationship/`, `src/Core/Sorcha.Register.Core/SyncState/`, `src/Core/Sorcha.Register.Core/Observations/`, `src/Services/Sorcha.Validator.Service/Services/RegisterMonitoringBootstrap.cs`, `src/Services/Sorcha.Peer.Service/GrpcServices/TransactionDistributionGrpcService.cs` (SubmitTransaction RPC). Spec: `specs/108-register-local-relationship/`.


---

## Cross-Cutting Pattern: Timebound Presentation Lifecycle (Feature 111)

Three-event on-register lifecycle for timebound evidence presentations. HAIP external-wallet credential presentation is the first consumer, but the primitive is consumer-agnostic.

**Events written to the same register as the originating action:**
1. `PresentationInitiated` — submitted on every attempt, carries submitter wallet, action ref, `requirementsDigest` (SHA-256 of canonical credentialRequirements), presentationRequestId, consumerName. Never contains credential data.
2. `PresentationOutcome` — written when the verifier callback arrives. `kind=success` carries VerifiedClaims + submissionHash; `kind=decline` carries reason (from `PresentationDeclineReason` enum) + optional diagnostics (only when `outcomeDetailLevel=verbose`).
3. `PresentationAbandoned` — written when TTL expires with no callback, *only if* the blueprint has `presentationConfig.recordAbandonment=true`. Phase 6 (deferred).

**Submission flow:** `ActionExecutionService` step 4c routes HAIP-requirement actions through `IPresentationLifecycleService.InitiateAsync` — which rate-limits via `IPresentationRateLimiter` (per-wallet-per-register sliding window; 429 + Retry-After on reject), stores pending state in Redis (hash at `sorcha:presentation:pending:{id}` with TTL = validity window), builds + signs + submits the PresentationInitiated tx, and returns QR details. The `/execute` endpoint returns HTTP 202 Accepted with `AwaitingPresentation=true`; the action does NOT complete here.

**Callback flow:** Verifiers POST to `POST /api/presentations/callbacks/{consumerName}` (behind `AuthorizationPolicies.RequireService`). Blueprint Service dispatches to the registered `IPresentationConsumer` by name; the consumer returns a `PresentationOutcome` which the lifecycle service writes as a tx. Two-level idempotency guard: Redis sentinel via SET NX for first-writer-wins, plus a late-outcome path that bypasses NX when sentinel is `"abandoned"` (producing `"abandoned+outcome"`).

**Consumer contract** (`IPresentationConsumer` in `Sorcha.PresentationLifecycle.Abstractions`): exposes `ConsumerName` + `VerifyAsync(context, payload, ct)`. Consumers never write the register — they return outcomes. HAIP ships `HaipPresentationConsumer` in `Sorcha.Haip.Service` and the `PresentationCallbackRelay` that forwards VerificationResult from `HandleDirectPost` to Blueprint.

**Blueprint config** (`Blueprint.PresentationConfig`, optional): `recordAbandonment` (bool, default false), `outcomeDetailLevel` (Minimal|Verbose), `presentationValidityWindowSeconds` (override platform default 600s).

**Endpoints:** `GET /api/presentations/{id}/status` returns current state (awaiting-presentation / success / decline / abandoned / abandoned-with-late-outcome / expired). Register tx stream is the authoritative history — the status endpoint reads Redis + sentinel.

**Runtime source:** `src/Common/Sorcha.PresentationLifecycle.Abstractions/` (cross-consumer contract), `src/Services/Sorcha.Blueprint.Service/Services/Implementation/PresentationLifecycleService.cs`, `src/Services/Sorcha.Blueprint.Service/Storage/Presentations/` (pending store + rate limiter), `src/Services/Sorcha.Blueprint.Service/Endpoints/PresentationEndpoints.cs`, `src/Services/Sorcha.Haip.Service/Services/HaipPresentationConsumer.cs`, `src/Services/Sorcha.Haip.Service/Services/PresentationCallbackRelay.cs`. Spec: `specs/111-presentation-lifecycle/`.

### Three traps this surface has already sprung (found by first live execution, 2026-07-27)

All three were invisible to unit tests and to static review. Each sits at a seam where both sides are
individually correct.

**1. `/api/presentations/**` needs an API-Gateway route.** It did not have one until #1309. The whole
surface — request object, `direct_post` callback, status poll, F127 disclosed-claims — was
unreachable through the gateway; requests fell through to the `ui-static` catch-all. **The fastest
diagnosis is one curl**: `GET /api/presentations/{any-guid}/status` must return Blueprint's **JSON**
`{"error":"Presentation request not found or expired."}`. A **bodiless** 404 means the gateway is not
routing it. The route carries **no `AuthorizationPolicy`** deliberately — these endpoints are
mixed-auth (request-object GET and status are `.AllowAnonymous()`; the `sorcha-wallet` callback is
`RequireConsumerAudience`), so an edge policy stricter than the endpoints breaks the anonymous ones.

**2. Lifecycle transactions must be exempt from action-data schema validation** (#1312). A
`PresentationInitiated` carries lifecycle metadata, never the gated action's payload — so applying
that action's schema made **any `presentationSource: "SorchaWallet"` action whose schema declares a
`required` array permanently unsealable** (`VAL_SCHEMA_004`). Now skipped via
`TransactionTypeClassifier.IsLifecycleTransaction`. **Only** the action-data schema check is
exempted; chain integrity, signatures, sender authorisation and the `VAL_BP_003` route-reachability
check all still apply — and `PresentationInitiated` deliberately keeps its **full** reachability
check, because it genuinely advances action N-1 → N (that is why `IsIntraActionLifecycleTerminal`
excludes it; do not "tidy" the two predicates together).

**3. `PendingPresentation`'s fields must round-trip through the store.**
`RedisPendingPresentationStore` hand-maintains an explicit `HashEntry[]` write list and an explicit
read reconstruction. When #1195/T032 added `Nonce`, `VerifierClientId`, `CredentialType` and
`RequiredClaimNames`, neither hand-list was updated — the service wrote them, the consumer read them,
Redis never carried them, and **every** SorchaWallet presentation declined with *"the pending state
carries no nonce/credentialType (pre-T032 entry?)"*. The diagnostic's guess was wrong: no row had
ever carried them. The in-memory store holds the object directly and round-trips fine, which is why
tests stayed green. **Adding a field to `PendingPresentation` means editing both hand-lists and
adding a round-trip test over the Redis implementation.**

**Why none of this surfaced earlier:** the SorchaWallet consumer is the only path that rebuilds a
`VerifierSession`, and it had never been exercised end to end. The only other SorchaWallet-gated
blueprint (`aias-device-registration`) declares no `required` array, so trap 2 never fired for it.

### Seal-aware ordering (Feature 119)

Two pre-existing chain-integrity races in the lifecycle outcome path are closed by **Feature 119**:

- **Race 2 (VAL_CHAIN_001):** outcome submitted before initiated has sealed — its `previousTransactionId` points at a still-mempool tx and the validator chain check rejects it.
- **Race 1 (VAL_BP_003):** FR-015 advancement evaluated before outcome has sealed — `StateReconstructionService` reads sealed-only and picks the wrong predecessor for the next action.

**The rule:** a transaction whose `previousTransactionId` references a Sorcha-managed predecessor MUST NOT be submitted until that predecessor is observed sealed. State-transitions depending on a Sorcha-managed seal MUST NOT fire until that seal is observed.

**Mechanism — `IPresentationSealCoordinator`** (singleton, Redis-backed):

- Two Redis hashes keyed by predecessor txId — `sorcha:presentation:awaiting-seal:submit:{predecessorTxId}` (built+signed `TransactionSubmission` deferred for the outcome and abandonment sites) and `sorcha:presentation:awaiting-seal:advance:{outcomeTxId}` (queued `CompleteAfterPresentationAsync` invocation).
- `PresentationSealSubscriber : BackgroundService` subscribes to the existing `transaction:confirmed` Redis Streams channel via `IEventSubscriber` and calls `coordinator.DrainOnSealAsync(txId)` on each event. Periodic recovery sweep at `PresentationLifecycleOptions.SealRecoverySweepIntervalSeconds` (default 5s) covers missed events (poll register for entries >30 s old) and TTL-fails entries past the validity window with sentinel `failed-predecessor-not-sealed`.
- `HandleOutcomeAsync` and `HandleAbandonmentAsync` check predecessor seal via `IRegisterServiceClient.GetTransactionAsync` before submitting — sealed → submit inline (existing path, unchanged); pending → enqueue and return.
- The FR-015 advancement on outcome success is enqueued to the advance queue rather than fired via `Task.Run`. The coordinator's drain creates a fresh DI scope and calls `CompleteAfterPresentationAsync` with `CancellationToken.None` (mirrors PR #583 lifetime contract).

**Validator carve-out** (Feature 119, `Sorcha.Validator.Service/Services/ValidationEngine.cs`): `VAL_BP_003` route reachability check is **skipped** when the current transaction's metadata `Type` is `PresentationOutcome` or `PresentationAbandoned`. These are intra-action lifecycle terminals — the outcome and abandonment chain off the same action's `PresentationInitiated` (both with the same `MetaData.ActionId = N`), and reflexively checking "is action N reachable from action N via routes" would always fail. Chain integrity is still enforced by `VAL_CHAIN_001` and `VAL_CHAIN_FORK`; only the workflow-routing check is bypassed for these specific tx types. `PresentationInitiated` still gets the full route check (it does advance from action N-1 to action N). See `specs/119-presentation-seal-ordering/EXECUTION-DEVIATIONS.md` for the forensic trail of why a Blueprint-only fix was impossible.

**Sentinel state machine extension** (additive — see XML doc on `IPendingPresentationStore.GetOutcomeSentinelAsync`):

- `outcome-pending-seal` — writer claimed; outcome submission deferred until predecessor seals. Treated as an idempotent-replay state alongside `outcome-pending-write`.
- `failed-predecessor-not-sealed` — never-seals timeout fired by recovery sweep. Operator-visible failure.
- `failed-validator-reject` — should-not-happen path: queued tx rejected on drain (other than `VAL_CHAIN_FORK`, which dedupes silently).

**Observability** on `Sorcha.Blueprint.Service.Presentation` meter:

- `sorcha_presentation_seal_wait_seconds{site}` — histogram, enqueue→drain.
- `sorcha_presentation_seal_queue_depth{site}` — observable gauge.
- `sorcha_presentation_seal_timeout_total{site}` — counter, never-seals failures.
- `sorcha_presentation_seal_recovered_via_sweeper_total{site}` — counter, missed-event recoveries.

OTel span `presentation.seal-wait` parented to the existing `presentation.outcome` / `presentation.abandoned` span.

**Runtime source (Feature 119):** `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IPresentationSealCoordinator.cs`, `src/Services/Sorcha.Blueprint.Service/Services/Implementation/RedisPresentationSealCoordinator.cs`, `src/Services/Sorcha.Blueprint.Service/Services/Implementation/PresentationSealSubscriber.cs`. Spec: `specs/119-presentation-seal-ordering/`. Design: `docs/superpowers/specs/2026-05-08-feature-111-chain-races-design.md`.

---

## Transactional Email Architecture (Feature 112)

Every transactional email sent by the Tenant Service — verification, invitation, password reset, welcome — flows through a single templated pipeline. **Application code calls `ITransactionalEmailService`, never `IEmailSender` directly.**

### Components (`Sorcha.Tenant.Service/Services/`)

| Component | Kind | Responsibility |
|-----------|------|----------------|
| `ITransactionalEmailService` / `TransactionalEmailService` | Scoped facade | Single entry point. Four typed methods: `SendVerificationAsync`, `SendInvitationAsync`, `SendPasswordResetAsync`, `SendWelcomeAsync`. Builds the view model, resolves branding, renders, and delegates to the sender. Stateless. |
| `IEmailTemplateRenderer` / `ScribanEmailTemplateRenderer` | Singleton | Parses every embedded `.html`/`.txt` under `Emails/Templates/*` at startup (fail-fast on parse errors). Includes an in-memory `ITemplateLoader` so `{{ include 'base.html' }}` works without disk I/O. Snake_case member renaming (e.g. `display_name` → `.DisplayName`). |
| `IEmailBrandingResolver` / `EmailBrandingResolver` | Scoped | Returns `EmailBranding` (sender name, logo URL, primary colour, tagline, reply-to). Sorcha defaults from `EmailSettings`; per-org overrides via `Organization.Branding` with per-field fallback — org name always wins, other fields fall back per-field to Sorcha. |
| `IEmailSender` + `SmtpEmailSender` / `AcsEmailSender` | Singleton | Tightened to `SendAsync(to, subject, htmlBody, textBody, ct)`. MailKit (SMTP) or Azure Communication Services — auto-selected on `Email:AcsConnectionString`. Multipart HTML + plaintext required on every message. |
| `WelcomeEmailDispatcher` | Scoped | One-shot-per-user welcome. Idempotent via `PlatformUser.WelcomeSentAt`; non-throwing (a send failure is logged, never blocks the triggering authentication flow). |

### Templates (embedded resources)

```
src/Services/Sorcha.Tenant.Service/Emails/Templates/
  base.html  base.txt           — shared frame (header + body slot + footer)
  verify.html  verify.txt       — Sorcha-branded
  invite.html  invite.txt       — per-org branded (logo + colour)
  reset.html  reset.txt         — Sorcha-branded
  welcome-public.html  .txt     — Sorcha-branded, recovery-phrase advance-warning
  welcome-invited.html  .txt    — per-org branded, role-aware
```

Every `.html` template ends with `{{ capture content }}...{{ end }} {{ include 'base.html' }}`. Plaintext counterparts are hand-authored (not HTML-stripped).

### Welcome dispatch rules

- Trigger points: `EmailVerificationService.VerifyTokenAsync` (email+password path), `LoginService` success path (covers users who've already verified and are logging in for the first time), `SocialCallback` Razor PageModel (social/passkey — IdP pre-verifies).
- Pre-conditions: `EmailVerified == true` AND `WelcomeSentAt == null`.
- Variant: public-org-only membership → `welcome-public`; any standard-org membership → `welcome-invited` using the **earliest-joined** standard org for branding.
- No recovery-phrase content appears in any email body, ever — FR-016. The phrase is shown exactly once in `CreateWallet.razor` and is not stored anywhere we can retrieve.

### Snapshot fixtures

All six template pairs have committed golden fixtures at `tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/{verify,invite-branded,invite-default,reset,welcome-public,welcome-invited}.{html,txt}`. Regenerate on intentional copy changes:

```bash
UPDATE_EMAIL_FIXTURES=1 dotnet test \
  tests/Sorcha.Tenant.Service.Tests/Sorcha.Tenant.Service.Tests.csproj \
  --filter "FullyQualifiedName~EmailTemplateSnapshotTests"
```

### Call-site examples

```csharp
// Verification — EmailVerificationService
var verifyUrl = $"{_emailSettings.BaseUrl.TrimEnd('/')}/auth/verify-email?token={Uri.EscapeDataString(token)}";
await _transactional.SendVerificationAsync(
    new VerifyEmailDispatch(user.Email, user.DisplayName, verifyUrl, 24), ct);

// Invitation — InvitationService (org-branded)
var invitingOrg = await _organizationRepository.GetByIdAsync(organizationId, ct);
await _transactional.SendInvitationAsync(
    new InviteEmailDispatch(request.Email, inviterName, invitingOrg, role, acceptUrl, days), ct);

// Welcome — never called directly by application code. Always via the dispatcher:
await _welcomeDispatcher.SendIfPendingAsync(platformUser, ct);
```

### Runtime source

`src/Services/Sorcha.Tenant.Service/Services/*Email*.cs`, `src/Services/Sorcha.Tenant.Service/Services/*Welcome*.cs`, `src/Services/Sorcha.Tenant.Service/Emails/Templates/**`. DI wiring: `ServiceCollectionExtensions.AddTenantEmail`. Tests: `tests/Sorcha.Tenant.Service.Tests/Services/{EmailTemplateSnapshotTests,ScribanEmailTemplateRendererTests,EmailBrandingResolverTests,TransactionalEmailServiceTests,WelcomeEmailDispatcherTests,EmailVerificationServiceTests}.cs`. Spec: `specs/112-email-sweep/`. Design doc: `docs/superpowers/specs/2026-04-24-email-sweep-design.md`. Tenant Service README carries the user-facing architecture overview.

---

## Citizen Wallet PWA (Feature 114) — server + PWA + reference verifier surface

End-to-end working wallet ecosystem. Twelve PRs landed 2026-04-26 (#427-#438). Wallet PWA (`Sorcha.Wallet.Pwa`, Blazor WASM) and reference verifier (`Sorcha.Verifier`, Blazor Server) are real projects in `src/Apps/`. The flow: sign in via Settings → Enrol device → credentials sync → present with full holder→device chain + issuer-signature verification → wallet auto-renews delegation 30 days before expiry. Demo-mint bridge (`/verify/demo/mint`) generates per-mint issuer keys for the demo until US4 ships real credential issuance.

### Endpoints

#### Wallet Service — public (citizen JWT, consumer-tier audience `{installation}:consumer` — gated by `RequireConsumerAudience`, spec 136)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/v1/wallet/devices/enrol` | Enrol a device — derives holder key (slot 108), issues device-delegation SD-JWT VC, allocates status-list slot, registers device with Tenant Service. Strict rate limit. |
| POST | `/api/v1/wallet/devices/renew-delegation` | Idempotent re-issuance of holder→device delegation, signed by holder key. Wallets call when within 30 days of expiry. 404 on cross-user / unknown / revoked device. (PR #435) |
| GET | `/api/v1/wallet/credentials` | Full credential snapshot for fresh-wallet seeding. (PR #428) |
| GET | `/api/v1/wallet/sync?since={cursor}` | Incremental delta. Cursor older than 30 days → 410 Gone (wallet falls back to /credentials). (PR #428) |
| DELETE | `/api/v1/wallet/devices/{deviceId}` | Citizen-initiated revoke from the PWA. Looks up `(listId, idx)` on Tenant via `IPlatformUserDeviceClient.GetByIdAsync`, calls `IDeviceRevocationService.RevokeAsync` (status-list flip + SignalR `DeviceRevoked`), then `IPlatformUserDeviceClient.RevokeAsync` to flip the Tenant row. 404 indistinguishable from non-existence. |
| GET | `/api/v1/wallet/devices` | List the citizen's enrolled devices (active + revoked, ordered by enrolment desc). Proxied through Tenant via `IPlatformUserDeviceClient.ListAsync`. |
| PUT | `/api/v1/wallet/devices/{deviceId}/label` | Rename. Validates label length 1..120. 404 on cross-user mismatch. |
| POST | `/api/v1/wallet/presentations/log` | US5 PR2 — report a batch of locally-recorded presentations. Validates `PresentationLogReportRequest` (400 malformed), returns **202 Accepted**, dispatches `ICitizenPresentationLogReporter` off the request path via `IServiceScopeFactory`. Per-entry Redis SET-NX dedupe (`sorcha:wallet:presentation-log-dedupe:{logEntryId}`, 24h); new entries pass to the `IPresentationLogForwarder` seam. **US5 PR3** swapped the PR2 logging no-op for `CitizenPresentationStoreForwarder`, which writes to the durable `ICitizenPresentationStore` (below). |
| GET | `/api/v1/wallet/presentations` | US5 PR3 — list the citizen's cross-device presentation history newest-first as `PresentationHistoryResponse` (reuses the wire `PresentationLogEntry`; disclosed claim **names** only, `registerId`/`actionTxId` always null). Empty history → empty list, never 404. Strict rate limit. |
| DELETE | `/api/v1/wallet/presentations/{id}` | US5 PR3 — server-authoritative delete of one history entry, scoped to the caller. Always **204** (idempotent; cross-user / non-existent indistinguishable). Removes the entry on every device; does not touch any verifier record (there is none). |

#### Tenant Service — public (citizen JWT, recovery flows from main UI)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/v1/me/devices` | List the authenticated citizen's enrolled wallet devices (active + revoked, ordered by enrolment desc). Backs the additive MyDevices page in Sorcha.UI.Web. |
| DELETE | `/api/v1/me/devices/{deviceId}` | Revoke a device. Tenant flips `Status=Revoked` + records `RevokedAt`/`RevokedByPlatformUserId`. Wallet status-list bit propagation is the Wallet Service's `DELETE /wallet/devices/{id}` endpoint, dispatched via service-to-service in PR2. 404 indistinguishable from non-existence to avoid device probing. |

#### Wallet Service — public (anonymous, verifier-facing)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/v1/wallet/status/{orgId:guid}/citizen-devices/{listId:int}.statuslist+jwt` | IETF Token Status List 2024 JWT for the org/list. `Cache-Control: public, max-age=21600`. |

#### Tenant Service — internal (service principal, `RequireService` policy)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/internal/platform-user-devices` | Bridge endpoint called by Wallet Service after issuing a delegation credential. Idempotent on `(PlatformUserId, DevicePublicJwkThumbprint)`. |
| GET | `/api/internal/platform-user-devices/{id}?platformUserId={uid}` | Scoped device lookup for the renewal flow. Cross-user probes return 404 indistinguishably from non-existence. (PR #435) |
| GET | `/api/internal/platform-user-devices?platformUserId={uid}` | List a citizen's enrolled devices. Used by Wallet Service to back `GET /api/v1/wallet/devices`. |
| PUT | `/api/internal/platform-user-devices/{id}/label?platformUserId={uid}` | Rename (label 1..120). Used by Wallet Service to back `PUT /api/v1/wallet/devices/{id}/label`. |
| DELETE | `/api/internal/platform-user-devices/{id}?platformUserId={uid}` | Tenant-row revoke called by Wallet Service after a PWA-initiated revoke. Pure local flip — does NOT call back to Wallet (caller has already done the status-list flip + SignalR). |

#### Wallet Service — internal (service principal, `RequireService` policy)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/internal/citizen-status-list/revoke` | Status-list bit flip + SignalR `DeviceRevoked` broadcast called by Tenant Service after a web-UI-initiated revoke. Pure Wallet-side — does NOT call back to Tenant. Body: `{organizationId, listId, indexInList, deviceId, platformUserId}`. |

#### Reference verifier — public (anonymous)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/verify/demo/mint` | Demo-only — mints SD-JWT VC + holder→device delegation bound to a posted device JWK. Registers the freshly-generated issuer key in `JwkRegistryIssuerKeyResolver` so subsequent presentations pass full signature verification. |
| POST | `/verify/r/{sessionId}/response` | OID4VP `direct_post` ingest — wallet POSTs `{vpToken, delegation}` here. |
| GET | `/verify/r/{sessionId}/status` | Polled by the verifier UI for outcome. |

### Sign-in (social + passkey)

The PWA has a dedicated `/wallet/signin` screen. `WalletAuthenticationStateProvider` provides the Blazor auth state; the router uses `AuthorizeRouteView` so all routes require authentication by default. Public routes (sign-in, enrol, cancelled-enrolment, and the social OAuth fragment-return landing) are exempted via `[AllowAnonymous]`. `Components/RedirectToSignIn.razor` handles the redirect from any protected page to `/wallet/signin`.

All three sign-in methods mint a **Consumer-tier** token (`{installation}:consumer` audience, spec 136).

#### Methods

| Method | PWA entry point | Server endpoints | Notes |
|--------|-----------------|------------------|-------|
| **Passkey** | `IPasskeyInterop` / `wwwroot/js/webauthn.js` | `POST /api/auth/passkey/assertion/options`, `POST /api/auth/passkey/assertion/verify` | Verify request carries `"tier":"consumer"`. |
| **Social** | `ISocialProvidersClient` drives provider buttons; `IAuthService.BeginSocialSignInAsync` starts the flow | `GET /api/auth/social/providers` (anonymous; returns `{"providers":[...]}`) → `POST /api/auth/social/initiate` (body: `{"provider":"…","surface":"wallet"}`) → `GET /auth/social/callback` Razor page → redirect to `/wallet/#token=…&refresh=…&expires_in=…` | `auth-fragment.js` IIFE captures the fragment before Blazor boots; `IAuthService.TryConsumeSocialReturnAsync` persists the tokens. |
| **Password + 2FA** | `IAuthService` | `POST /api/auth/login` (body: `{"tier":"consumer",…}`) + `POST /api/auth/verify-2fa` (body: `{"tier":"consumer",…}`) | `tier` field on both requests forces Consumer-tier regardless of `returnTo`. |

#### Login-only enforcement

Unknown social identity → `SocialCallbackModel` calls `ResolveOrCreateSocialUserAsync(…, allowCreate: false)` → `SocialLoginRefusal.NoExistingAccount` → redirect to `/wallet/signin?authError=no_account`. No `PlatformUser` is created. Other refusal reasons (unverified provider, unverified existing account) also redirect to `/wallet/signin?authError=refused`.

#### Silent refresh

`BearerTokenHandler` (delegating handler on the PWA's typed HTTP clients) silently re-mints the session via `POST /api/auth/token/refresh` when the access token is near expiry. A failed refresh clears the token store and redirects to `/wallet/signin`.

#### Key files — PWA sign-in surface

| File | Purpose |
|------|---------|
| `src/Apps/Sorcha.Wallet.Pwa/Pages/SignIn.razor` | Sign-in screen — password/2FA form + passkey button + social provider buttons |
| `src/Apps/Sorcha.Wallet.Pwa/Services/WalletAuthenticationStateProvider.cs` | Blazor auth state, token storage, Consumer-tier gate |
| `src/Apps/Sorcha.Wallet.Pwa/Services/IAuthService.cs` | Login, 2FA verify, social initiate/consume, signout |
| `src/Apps/Sorcha.Wallet.Pwa/Services/IPasskeyInterop.cs` | JS interop seam for `webauthn.js` |
| `src/Apps/Sorcha.Wallet.Pwa/Services/ISocialProvidersClient.cs` | Typed HTTP client for `GET /api/auth/social/providers` |
| `src/Apps/Sorcha.Wallet.Pwa/Services/BearerTokenHandler.cs` | Delegating handler; silent refresh via `/api/auth/token/refresh` |
| `src/Apps/Sorcha.Wallet.Pwa/Components/RedirectToSignIn.razor` | Redirect component for protected routes |
| `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/webauthn.js` | Passkey assertion (client-side FIDO2) |
| `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/auth-fragment.js` | IIFE: captures `#token=…&refresh=…&expires_in=…` fragment before Blazor boots |
| `src/Services/Sorcha.Tenant.Service/Endpoints/SocialLoginEndpoints.cs` | `GET /api/auth/social/providers` + `POST /api/auth/social/initiate` (surface field) |
| `src/Services/Sorcha.Tenant.Service/Pages/Auth/SocialCallback.cshtml.cs` | Wallet branch: Consumer-tier mint + `/wallet/#…` redirect + login-only refusal |
| `src/Services/Sorcha.Tenant.Service/Endpoints/PublicPasskeyEndpoints.cs` | `POST /api/auth/passkey/assertion/verify` (tier hint) |
| `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs` | `POST /api/auth/verify-2fa` (tier hint) |

### Key Models

- **`PlatformUserDevice`** (Tenant Service) — `Active|Revoked` status, RFC 7638 thumbprint (43 chars), citizen-editable `Label`, `DelegationExpiresAt` + `DelegationCredentialJti` rotated on renewal, `(StatusListId, StatusListIndex)` pair allocated from the org's pool (lists roll over at 32 768 bits — both fields needed to disambiguate after rollover). Cascade delete from `PlatformUser`.
- **`CitizenDeviceStatusList`** (Wallet Service) — packed bitstring (default 32 768 bits / 4 KB), `RevokedCount`, `LastAllocatedIndex` watermark, `SignedJwt` cached column. One row per `(OrganizationId, ListId)`; lists roll over at capacity.
- **`CitizenWalletSyncCursor`** (Wallet Service) — per-`(PlatformUserId, PlatformUserDeviceId)` `LastEventSeq` for the not-yet-implemented `/sync` endpoint.
- **`DeviceDelegationCredential`** (`Sorcha.CitizenWallet.Abstractions`) — typed wrapper around the SD-JWT VC payload. `iss=did:sorcha:holder:{thumbprint}`, `sub=did:sorcha:device:{thumbprint}`, `vct=https://sorcha.dev/vc/citizen-device-delegation/v1`, `cnf.jwk` = device's EC P-256 public JWK, `status.status_list = { uri, idx }`. Lifetime 12 months.

### Key Services

- **`IHolderKeyService`** / `HolderKeyService` (Wallet Service) — derives the citizen's per-PlatformUser holder key under `sorcha:citizen-holder` (slot 108, BIP44 path `m/44'/0'/0'/0/108`). Public JWK + RFC 7638 thumbprint cached in Redis (24 h TTL). PQC wallets derive a classical co-key (ES256/Ed25519). Signing always re-derives + zeroises after use.
- **`ICitizenStatusListPublisher`** / `CitizenStatusListPublisher` (Wallet Service) — owns the bitstring + signed-JWT lifecycle. `AllocateIndexAsync(orgId, signingWallet)` → monotonic `(listId, idx)`. `FlipAsync` is idempotent. Token Status List 2024 wire format. Default 32 768 capacity, 24 h list lifetime, signed by slot-109 key.
- **`CitizenStatusListPublisherService`** (Wallet Service `BackgroundService`) — hourly tick, scans for lists within 1 h of `exp` and re-signs each via `RegenerateAsync`. Singleton with scoped deps via `IServiceScopeFactory`. Internal `RunOnceAsync` test seam. Closes the v1 freshness gap when no revocations occur.
- **`IDeviceDelegationIssuer`** / `DeviceDelegationIssuer` (Wallet Service) — pure composition over `IHolderKeyService` + `ICitizenStatusListPublisher`. Issues the SD-JWT VC payload signed with the holder key.
- **`IOrgStatusSigningWalletResolver`** / `OrgStatusSigningWalletResolver` (Wallet Service) — lazily provisions a per-org ED25519 system wallet (owner=`system:citizen-status:{orgId}`, tenant=`system`) on first call. Every list signed by this wallet's slot-109 key — verifiers pin one kid per org rather than per citizen.
- **`IPlatformUserDeviceService`** / `PlatformUserDeviceService` (Tenant Service) — `RegisterAsync` (idempotent on `(PlatformUserId, DevicePublicJwkThumbprint)` — refreshes delegation fields on retry, preserves `Id` + `EnrolledAt`); `GetByIdAsync(deviceId, platformUserId)` scoped lookup for renewal (PR #435); `ListAsync` returns active+revoked ordered by enrolment desc; `RevokeAsync` flips `Status=Revoked`, records `RevokedAt`/`RevokedByPlatformUserId`, idempotent on already-revoked (US3 PR1).
- **`IPlatformUserDeviceClient`** (Sorcha.ServiceClients.Http, namespace `Sorcha.ServiceClients.PlatformUserDevice`) — Wallet→Tenant service-to-service HTTP client. `RegisterAsync` (carries both `statusListId` and `statusListIndex` so revoke-by-deviceId can reach `FlipAsync(orgId, listId, idx)` after lists roll over) + `GetByIdAsync` (404→null). Uses `ServiceAuthClient` token.
- **`ICitizenWalletClient`** (Sorcha.ServiceClients.Http, namespace `Sorcha.ServiceClients.CitizenWallet`) — forward HTTP client for the PWA (and tests / reference verifier setup) to call Wallet Service. Methods: `EnrolDeviceAsync`, `SyncAsync` (410→null), `ListCredentialsAsync`, `RenewDelegationAsync` (404→null). Caller-supplied JWT; no service-principal injection.
- **`ICitizenSyncService`** / `CitizenSyncService` (Wallet Service) — composes credential deltas + full snapshots; mints/validates the opaque sync cursor as an HMAC-SHA256 JWT carrying `{sub: holderKeyId, seq, iat}` per research §R-006. 30-day cursor lifetime → 410 → wallet falls back to /credentials (PR #428).
- **`ICitizenCredentialEventStream`** / `EfCoreCitizenCredentialEventStream` (Wallet Service) — reads `CitizenCredentialEventLog` joined to `CredentialEntity` for `/sync` payload composition. Status mapping: `Active`/`PendingAcceptance` → `Added`, `Revoked`/`Declined` → `Revoked`, replacement events → `Replaced`. Registered as scoped (US4 PR3 flipped `CitizenSyncService` from singleton → scoped to consume it). The previous `EmptyCitizenCredentialEventStream` stub was retired in PR #576.
- **`IHolderAddressLookup`** / `EfCoreHolderAddressLookup` (Wallet Service, US4) — single method `ResolvePlatformUserIdAsync(walletAddress, ct)`. Reads `CitizenHolderIndex` with a 24-hour Redis cache (key `sorcha:citizen:holder-index:{addr}`). Returns null on miss — that's how the projector distinguishes citizen credentials from org credentials. The index is written from `CitizenWalletEndpoints.EnrolDevice` at the one moment the citizen JWT carries both the wallet address and the platform user id.
- **`ICitizenInboxProjector`** / `CitizenInboxProjector` (Wallet Service, US4) — single composition point for citizen credential push. Methods `OnCredentialAddedAsync(CredentialEntity, ct)` and `OnCredentialStatusChangedAsync(CredentialEntity, oldStatus, ct)`. Resolves recipient address via `IHolderAddressLookup`; on hit, allocates next `Seq` (MAX(Seq)+1 with unique-index-violation retry), inserts a `CitizenCredentialEventLog` row, then emits `WalletHub.CredentialAvailable(credentialId)` to `WalletHub.GroupNameFor(platformUserId)`. Hub emit is try/log/swallow — the pull-on-open `/sync` path remains authoritative; push is the latency optimisation. Org credentials hit no-op.
- **`IDelegationRenewalService`** / `DelegationRenewalService` (Wallet Service) — composes `IPlatformUserDeviceClient.GetByIdAsync` → `IDeviceDelegationIssuer.IssueAsync` → `IPlatformUserDeviceClient.RegisterAsync` (refresh in place, idempotent on thumbprint). Always re-issues; status-list slot stays the same. Rejects renewals for revoked devices (PR #435).
- **`IIssuerKeyResolver`** (verifier): production is a `CompositeIssuerKeyResolver` = `DidResolverBackedIssuerKeyResolver` (F120 — resolves `did:sorcha:org:...` to published verification methods) → `JwkRegistryIssuerKeyResolver` (in-memory, tests + demo-mint). `OptOutIssuerKeyResolver` remains the no-op fallback for tests that don't register keys. `VerifiablePresentationValidator` step 4b verifies credential JWS when a key resolves; `RequireIssuerSignature` defaults `true` (fail-closed, F120 FR-019). Wired in both `Sorcha.Verifier` and Blueprint Service (PR #795).

### Derivation slots (`SorchaDerivationPaths`)

| Slot | Context | Path | Purpose |
|------|---------|------|---------|
| 108 | `sorcha:citizen-holder` | `m/44'/0'/0'/0/108` | Per-citizen holder identity. Issuers bind credentials to this key via `cnf`; signs device delegation credentials. |
| 109 | `sorcha:citizen-status-signing` | `m/44'/0'/0'/0/109` | Per-org status-list signing key. One pinnable kid per org. |

Canonical home: `Sorcha.Wallet.Contracts.Constants.SorchaDerivationPaths`
(`src/Common/Sorcha.Wallet.Contracts/Constants/SorchaDerivationPaths.cs`) — a zero-dependency leaf
so services, CLI, Blazor UI and the WASM PWA can all reference it. **Never hard-code a `"sorcha:*"`
literal**; a typo derives a different valid key and dockets silently stop sealing. Enforced by
`scripts/check-derivation-contexts.ps1` (CI: `derivation-contexts-gate`), allowlist empty. The
former `Sorcha.CitizenWallet.Abstractions.Constants.DerivationContexts` mirror is deleted.

### `WalletHub` (SignalR)

Hub URL `/hubs/wallet`. `[Authorize(AuthenticationSchemes = "Bearer")]`. On connect, the bearer JWT's `platform_user_id` claim places the connection in group `wallet:platform-user:{guid:N}` so server-side broadcasters target a single citizen across all their enrolled devices. Public helpers `WalletHub.PlatformUserIdClaim` and `WalletHub.GroupNameFor(Guid)`. Server-to-client events: `DeviceRevoked(Guid)` (US3) and `CredentialAvailable(string)` (US4 — emitted from `CitizenInboxProjector`).

### Key Files

| File | Purpose |
|------|---------|
| `src/Common/Sorcha.CitizenWallet.Abstractions/` | DTOs, derivation context constants, VCT URIs, validators, embedded JSON schema |
| `src/Core/Sorcha.Wallet.Portable/Domain/Entities/CitizenDeviceStatusList.cs` + `CitizenWalletSyncCursor.cs` + `CitizenHolderIndex.cs` + `CitizenCredentialEventLog.cs` | Wallet Service entities (last two added in US4 PR #575) |
| `src/Services/Sorcha.Tenant.Service/Models/PlatformUserDevice.cs` | Tenant Service entity |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/HolderKeyService.cs` | Slot-108 holder key derivation + JWK + thumbprint + sign |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenStatusListPublisher.cs` | Token Status List 2024 publisher |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenStatusListPublisherService.cs` | Hourly freshness BackgroundService |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/DeviceDelegationIssuer.cs` | SD-JWT VC composition |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/OrgStatusSigningWalletResolver.cs` | Lazy per-org system-wallet provisioner |
| `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` | `/api/v1/wallet/devices/{enrol,renew-delegation}`, `/credentials`, `/sync` |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenSyncService.cs` | Sync delta composer + JWT cursor mint/validate (Scoped since US4) |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/EfCoreCitizenCredentialEventStream.cs` | US4 — reads `CitizenCredentialEventLog` joined to `CredentialEntity` for `/sync` |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/EfCoreHolderAddressLookup.cs` + `Interfaces/IHolderAddressLookup.cs` | US4 — citizen wallet address → PlatformUserId resolver, Redis-cached |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenInboxProjector.cs` + `Interfaces/ICitizenInboxProjector.cs` | US4 — composition point for credential push (event log + hub emit) |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/InboundCredentialDetector.cs` + `Credentials/CredentialStore.cs` | US4 hooks: `TryExtractAsync` (post-`AddAsync`), `PatchStatusAsync`, `UpdateStatusAsync` invoke the projector |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/DelegationRenewalService.cs` | Renewal orchestrator (lookup → re-issue → tenant refresh) |
| `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenStatusListEndpoints.cs` | Public status-list JWT endpoint |
| `src/Services/Sorcha.Wallet.Service/Hubs/WalletHub.cs` | SignalR hub at `/hubs/wallet` |
| `src/Services/Sorcha.Tenant.Service/Services/PlatformUserDeviceService.cs` | Tenant device registry |
| `src/Services/Sorcha.Tenant.Service/Endpoints/InternalEndpoints.cs` | `/api/internal/platform-user-devices` bridge |
| `src/Common/Sorcha.ServiceClients.Http/PlatformUserDevice/` | Wallet → Tenant s2s client (`RegisterAsync`, `GetByIdAsync`) |
| `src/Common/Sorcha.ServiceClients.Http/CitizenWallet/` | PWA → Wallet client (enrol, sync, list-credentials, renew-delegation) |
| `src/Apps/Sorcha.Wallet.Pwa/` | Blazor WASM PWA (mounted at `/wallet/` via gateway `PathRemovePrefix`). Pages: Index/Enrol/Present/CredentialDetail/Settings/Devices/Activity. Components: ConsentSheet/CredentialPickerDialog/NoMatchingCredentialDialog. wwwroot/js: webcrypto-bridge.js, indexeddb-bridge.js. |
| `src/Apps/Sorcha.Wallet.Pwa/Services/` | All PWA services: WebCryptoDeviceKeyService, IndexedDb{Credential,Delegation,StatusList,DeviceMeta,SyncCursor,AccessToken}Store, AuthService, EnrolmentService, SyncService, DelegationRenewalClient, BearerTokenHandler, ServerClockHandler, ServerClockObserver, **CitizenWalletHubConnection** (US4 — singleton SignalR client at `/hubs/wallet`, subscribes to `CredentialAvailable` + `DeviceRevoked`) |
| `src/Apps/Sorcha.Wallet.Pwa/Pages/Index.razor` | Home page; subscribes to `CitizenWalletHubConnection` events in `OnInitializedAsync`, fires `SyncNowAsync` / `RenewIfDueAsync`, `IAsyncDisposable` detach |
| `src/Apps/Sorcha.Wallet.Pwa/wwwroot/service-worker.published.js` | US4 — handles Background Sync `sync` events tagged `citizen-credential-sync` to replay `/sync` while document is hidden |
| `src/Apps/Sorcha.Verifier/` | Blazor Server reference verifier (mounted at `/verify/`). Pages: Index/VerifierSession/Outcome. Endpoints: PresentationResponseEndpoints, DemoMintEndpoint. |
| `src/Apps/Sorcha.Verifier/Services/IIssuerKeyResolver.cs` | Issuer-signature verification seam — OptOut + JwkRegistry impls |
| `specs/114-citizen-wallet-pwa/` | Spec, plan, contracts, data model, tasks |
| `docs/superpowers/specs/2026-04-26-citizen-wallet-pwa-design.md` | Brainstorm design doc |

### Test patterns specific to Feature 114

- **`TestCitizenWalletDbContext`** (`tests/Sorcha.Wallet.Service.Tests/Services/`) — inherits `WalletDbContext` and `modelBuilder.Ignore<T>()`s every base entity (jsonb columns are Npgsql-only). Reuse this pattern for any future Wallet Service citizen-only tests using the InMemory provider.
- **`HolderKeyService` mock gotcha** — when mocking `IKeyManagementService.DeriveKeyAtPathAsync`, return a fresh `byte[].Clone()` each call. The publisher zeroises the private key after signing; a shared array reference is wiped between invocations.
- **Endpoint handler tests** — follow the established reflection-based static-handler invocation pattern (`PresentationEndpointTests`, `CitizenWalletEnrolEndpointTests`). No `WebApplicationFactory` needed for handler-level coverage.
- **PWA service tests** — extract IJSRuntime-touching state behind small interfaces (e.g. `ISyncCursorStore`, `IDeviceMetaStore`, `IAccessTokenStore`) so unit tests use the in-memory variant and never mock IJSRuntime directly. Each interface ships with both impls in the same file. Mocking IJSRuntime generic InvokeAsync<T> is brittle — avoid.

### Sync + renewal flow (cross-cutting)

The PWA's two background loops on every Home load:

1. **Sync** — `SyncService.SyncAsync` reads cursor from `ISyncCursorStore`, calls `/sync?since=`, applies adds/revokes/replacements to `ICredentialCache`, persists new cursor, updates `IDelegationStore` if server piggybacked a renewal. On 410 → `ListCredentialsAsync` snapshot fallback then fresh `/sync` to bootstrap a usable cursor. After the core sync completes it drains the presentation log (US5, below).
2. **Delegation renewal** — `DelegationRenewalClient.RenewIfDueAsync` reads `IDeviceMetaStore`, checks `DelegationExpiresAt - now > 30 days`. If due, calls `/devices/renew-delegation`, persists fresh JWT + refreshed expiry. Five outcomes: `NotEnrolled`, `NotDue`, `Renewed`, `DeviceNotFound` (server 404 → device revoked elsewhere), `Failed`.

**Presentation-log drain (US5 PR2)** — at the tail of every successful `SyncService.SyncAsync`, `DrainPresentationLogAsync` lists `IPresentationLog` entries where `!SyncedToServer && CredentialId != Guid.Empty`, maps the PWA-local `PresentationLogEntry` to the wire `PresentationLogReportRequest` (outcome `Sent → Acknowledged`, `Rejected → VerifierRejected`; `RegisterId`/`ActionTxId`/`VerifierDid` null on the offline path), POSTs `/presentations/log`, and on 202 re-`AppendAsync`es each entry with `SyncedToServer = true` (IndexedDB `put` upsert by id). Best-effort — a drain failure is logged and retried next sync, never failing the sync. The PWA-local entry carries a `Guid CredentialId` (the cache id, set in `Present.razor`) purely so the drain can populate the wire correlation field; pre-PR2 entries (empty id) are skipped.

### Cross-device presentation history (US5 PR3)

The reconciled replacement for the stale "offline `IPresentationConsumer` writes the register" model (which was structurally impossible against F111/F127 — consumers can't write the register and a free-standing offline presentation has no originating register). **No Blueprint Service change, no register/ledger write** (FR-010 / SC-004). Design: `docs/superpowers/specs/2026-05-20-f114-us5-offline-presentation-reconciliation-design.md`.

- **`CitizenPresentationRecord`** (Wallet Service entity, `WalletDbContext`, migration `AddCitizenPresentationRecord`) — durable per-citizen row. Composite PK `(PlatformUserId, EntryId)`; `DisclosedClaims` jsonb (claim **names** only — never values); `(PlatformUserId, PresentedAt desc)` index. No register correlation.
- **`ICitizenPresentationStore`** / `EfCoreCitizenPresentationStore` (Postgres, scoped) + `InMemoryCitizenPresentationStore` (singleton fallback/test). `UpsertAsync` idempotent on the composite PK preserving `ReportedAt` — the **authoritative** dedupe; `ListAsync` newest-first; `DeleteAsync` citizen-scoped. Registered via `IStorageRegistrationLog` (`RegisterPersistent`/`RegisterInMemory`) but **NOT** on the F113 fail-fast audited list (convenience data — warns, doesn't gate startup). OTel counter `sorcha_citizen_presentation_store_total{op=upsert|list|delete}` on the `Sorcha.Wallet.Service` meter.
- **`CitizenPresentationStoreForwarder : IPresentationLogForwarder`** replaces PR2's `LoggingPresentationLogForwarder` (DI flipped Singleton→Scoped — it now consumes the scoped store/`WalletDbContext`). `ForwardAsync` calls `store.UpsertAsync`. PR2's reporter + Redis SET-NX dedupe are unchanged (hot-retry short-circuit); the store upsert is the durable idempotency. Delivery is convenience-grade (at-most-once-ish): PR2 marks the local entry synced on the 202 before the off-request-path forward runs, so a store-write failure is logged and not retried; the upsert idempotency heals a re-report while the 24h SET-NX claim is live. Outbox-grade delivery is deferred.
- **PWA Activity merge rule (design §5)** — `PresentationActivityMerge.Build`: `display = (server history) ∪ {local entries where !SyncedToServer}`. A just-made presentation shows immediately from the local log; once synced, it is represented by the server list and the synced local copy is **display-suppressed** (so a server-authoritative delete removes it from every device and a lingering synced local copy never resurrects it). Per-row delete (`PresentationActivityActions.DeleteEverywhereAsync`) is server-authoritative: `ICitizenWalletClient.DeletePresentationAsync` then `IPresentationLog.DeleteAsync`. Reframed FR-009 copy: "removed from your history on all your devices; does not affect the verifier's own records."

Sync cursor is an HMAC-SHA256 JWT carrying `{sub: holderKeyId, seq, iat}` per research §R-006. 30-day cursor lifetime. `EfCoreCitizenCredentialEventStream` (US4 PR #576) reads from `CitizenCredentialEventLog`; the previous `EmptyCitizenCredentialEventStream` stub is retired. Real credential events flow whenever `InboundCredentialDetector.TryExtractAsync` decrypts a `targetAudience: "SorchaLocalWallet"` credential bound to a citizen's holder wallet — see "Citizen credential push (US4)" below.

### Citizen credential push (US4)

The end-to-end chain when an issuer publishes a credential whose recipient is a citizen-PWA holder:

1. **Block sealed** — Validator seals a docket containing the issuer's credential-issuance transaction (`targetAudience: "SorchaLocalWallet"`).
2. **Inbound detect** — `InboundCredentialDetector.TryExtractAsync` (Wallet Service) decrypts the AEAD envelope using the recipient wallet's X25519 key, builds a `CredentialEntity`, calls `CredentialStore.AddAsync(...)`.
3. **Project** — Immediately after `AddAsync`, `ICitizenInboxProjector.OnCredentialAddedAsync` runs:
   - `IHolderAddressLookup.ResolvePlatformUserIdAsync(recipientWalletAddress)` — null = org credential, no-op; non-null = citizen.
   - Insert `CitizenCredentialEventLog` row with `Seq = MAX(Seq)+1` for that PlatformUserId (unique-index-violation retry handles concurrent issues).
   - `IHubContext<WalletHub>.Clients.Group(WalletHub.GroupNameFor(pid)).CredentialAvailable(credentialId)` — try/log/swallow.
4. **Status mutations** — `CredentialStore.PatchStatusAsync` and `UpdateStatusAsync` call `ICitizenInboxProjector.OnCredentialStatusChangedAsync` after the mutation succeeds. Active→Revoked/Declined writes a `Revoked` log entry; replacement transitions write a `Replaced` entry.
5. **PWA receives** — `CitizenWalletHubConnection` (Singleton SignalR client at `/hubs/wallet`) is subscribed to `CredentialAvailable` + `DeviceRevoked` only. `Pages/Index.razor` subscribes in `OnInitializedAsync`, fires `SyncService.SyncNowAsync()` on `CredentialAvailable` (and `DelegationRenewalClient.RenewIfDueAsync` on `DeviceRevoked`), detaches via `IAsyncDisposable`. Reconnect-with-jitter (0/2/5/10/30 s); connection failures swallow silently.
6. **Background sync fallback** — `service-worker.published.js` handles `sync` events tagged `citizen-credential-sync` (Chromium Background Sync API) by replaying `/sync` when the document is hidden.

The hub emit is an optimisation. The pull-on-open `/sync` path is authoritative — closing the PWA before issuance and reopening after still surfaces the credential because the projector wrote the event log row regardless of hub-emit success.

### Citizen-PWA worked-example blueprint (`SorchaLocalWallet`)

A council issues an Assured Identity credential to a citizen-PWA holder. Composes with Open Participants & Late Binding — the citizen applicant has `walletAddress: null` and is late-bound on first submission.

```jsonc
{
  "title": "Assured Identity (PWA delivery)",
  "participants": [
    { "id": "applicant", "walletAddress": null },
    { "id": "verifier",  "walletAddress": "ws1qta..." }
  ],
  "actions": [
    { "id": 1, "isStartingAction": true, "sender": "applicant",
      "schemaRef": "AssuredIdentityApplication/v1" },
    { "id": 2, "sender": "verifier", "schemaRef": "VerifierDecision/v1" },
    { "id": 3, "sender": "verifier",
      "credentialIssuanceConfig": {
        "credentialType": "AssuredIdentityCredential/v1",
        "targetAudience": "SorchaLocalWallet",
        "recipientParticipantId": "applicant",
        "claimMappings": [
          { "claimName": "givenName",   "sourceField": "/1/payload/givenName" },
          { "claimName": "familyName",  "sourceField": "/1/payload/familyName" },
          { "claimName": "dateOfBirth", "sourceField": "/1/payload/dateOfBirth" }
        ],
        "disclosable": ["givenName", "familyName", "dateOfBirth"],
        "expiryDuration": "P5Y"
      } }
  ]
}
```

Detailed authoring guidance lives in `.claude/skills/blueprint-builder/SKILL.md` → "SorchaLocalWallet citizen-PWA worked example".

### Pre-release contract correction (US4)

`CachedCredentialPayload.Id`, `RevokedCredentialEntry.Id`, and `ReplacedCredentialEntry.OldId`/`NewId` (in `Sorcha.CitizenWallet.Abstractions`) changed from `Guid` to `string` to carry credential identifiers as the projector emits them. The PWA's local `CachedCredential.Id` stays `Guid` for IndexedDB indexing; `ToCachedCredential` maps `string → Guid` deterministically via SHA-256 first-16-bytes.

### Issuer-signature trust (verifier)

`VerifiablePresentationValidator` step 4b verifies the credential JWT against the issuer key returned by `IIssuerKeyResolver.ResolveAsync(iss)`. Behaviour matrix:

| Resolver returns | `RequireIssuerSignature` | Outcome |
|---|---|---|
| Public JWK | (any) | Verify signature; reject if mismatch. |
| `null` | `false` (PWA offline doorstep) | Accept on holder→device chain alone, **and mark the outcome `IssuerSignature = NotVerified`** (review H3 — no longer a silent accept). |
| `null` | `true` (production hardening) | Reject with "RequireIssuerSignature is enabled". |

**Issuer-signature status on the outcome (review H3).** `VerificationOutcome.IssuerSignature` (`Verified` / `NotVerified`) records whether the issuer JWS was actually checked. The desk verifier + Blueprint Service run `requireIssuerSignature:true`, so an accepted outcome is always `Verified`. The PWA citizen verifier runs `requireIssuerSignature:false` with `OptOutIssuerKeyResolver` (a citizen device has no service principal to resolve `did:sorcha:org:*` and is often offline), so it can land `Accepted + NotVerified`; `RealVerifierEngine` maps that to **`VerifyOutcome.Warn`** (reduced assurance, "issuer not verified") — never a plain `Pass`. This offline reduced-assurance path is a deliberate, documented scoped exception (see `RealVerifierEngine` remarks); online issuer verification on the device (via a consumer/anonymous DID path) is a backlog enhancement.

The seam shipped in PR #434; the DID-resolver-backed production impl shipped in **Feature 120** (`DidResolverBackedIssuerKeyResolver`, resolving `did:sorcha:org:...` via the F120 DID resolver registry → published verification methods). Both the reference verifier (`Sorcha.Verifier`) and Blueprint Service (PR #795, for `SorchaWalletPresentationConsumer`) now register a `CompositeIssuerKeyResolver` that tries the DID-backed resolver first and falls back to `JwkRegistryIssuerKeyResolver`. `RequireIssuerSignature` defaults to `true` (F120 FR-019). The demo flow still uses the JWK registry — `DemoMintEndpoint` registers each freshly-generated issuer JWK on every mint so demo presentations pass full signature verification without a published DID document.

### Sign-out data purge (PWA)

`IAuthService.SignOutAsync` clears the access token **and** wipes every per-device IndexedDB store via `ILocalDataPurge` → `SorchaIndexedDb.wipe()` (a single transaction that enumerates `db.objectStoreNames` and clears each — future-proof against new stores). Sign-out previously cleared only the access token, leaving the next citizen on a shared device with the prior user's `credentials`, `personas`, `delegation`, `verifications`, `presentationLog`, `context`, and the `flags` record (`WelcomedAt`/`TourDismissedAt`) — a cross-user leak that also suppressed the welcome takeover + guided tour for the new user. `Pages/Settings.razor` sign-out then `forceLoad`-navigates home (`NavigateTo("", forceLoad: true)`) so singleton in-memory state (hub connection, `IHasPairedDeviceProbe` cache, `ManagedUserContext`) re-initialises clean. **Do NOT add a new per-device store and wire its own per-store clear into sign-out** — `wipe()` already covers it; the per-store approach is what caused the original bug. Regression guard: `CitizenWalletSignOutWipeTests` (E2E) + `AuthAndBearerTests.AuthService_SignOut_PurgesAllLocalData` (unit).

### Credential cache cipher resilience (PWA)

The PWA's IndexedDB credential cache (`indexeddb-bridge.js`) is a mirror of server-authoritative state, so it must tolerate rows it can't decrypt rather than crash. PR #797 switched the at-rest cipher from AES-GCM-256 (12-byte nonce, WebCrypto) to XChaCha20-Poly1305 (24-byte nonce, noble) with no migration — a credential cached under the old scheme made `listCredentials()` throw `Uint8Array expected of length 24, got length=12`, aborting the entire sync (surfaced as "Sync error: …") for any device carrying a legacy row. `getCredential`/`listCredentials` now **evict** undecryptable rows and continue; the server re-seeds them under the current scheme via `/credentials` or `/sync`. **Any future change to the cache cipher MUST keep this evict-and-continue behaviour** — never let one bad row abort the listing. Regression guard: `CitizenWalletCredentialCacheMigrationTests` (E2E; run locally via `dotnet vstest`, INFRA-skipped in CI).

---

## AssuredIdentity on the PWA (Feature 124) — pending-application notice + first-credential takeover

Spec 1 of the Strathcarron citizen arc. The PWA's first user-visible UX beat on top of Feature 114: a designed waiting state while an application is in review and a single-fire welcome takeover when the first credential lands.

### Endpoints

#### Wallet Service — citizen JWT, `/api/v1/wallet/pending-applications`

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/v1/wallet/pending-applications` | Read the active notice or null. Wallet calls this on every Home render. |
| PUT | `/api/v1/wallet/pending-applications` | Set or replace the notice (label, 1..80 chars). Idempotent; TTL resets. 24-hour TTL. |
| DELETE | `/api/v1/wallet/pending-applications` | Clear. Idempotent (204 whether or not present). |

All three scoped to the calling `PlatformUserId` via JWT; rate-limited by `RateLimitPolicies.Strict`. Contract: `specs/124-assured-identity-pwa/contracts/pending-application-notice.openapi.yaml`.

### Server-side store

- **`PendingApplicationNotice`** record (`Sorcha.Wallet.Service.Models`): `Label` + `SetAt`. Compile-time enforces "no credential content" by accepting only `string Label`.
- **`IPendingApplicationStore`** / **`RedisPendingApplicationStore`** (Wallet Service) — `IDistributedCache`-backed (Redis in prod, in-memory in tests). Key `sorcha:wallet:pending-app:{platformUserId:N}`, 24-hour absolute TTL. No EF migration; stays off the storage-audit-gated path.
- **OpenTelemetry counter**: `sorcha_pending_application_notice_total{op=set|clear|read}` on the existing `Sorcha.Wallet.Service` meter.

### PWA — per-device flags + waiting state + welcome takeover

- **`IWalletFlagsStore`** / `IndexedDbWalletFlagsStore` / `InMemoryWalletFlagsStore` (`Sorcha.Wallet.Pwa.Services`) — per-device flags persisted in IndexedDB store `device` at key `flags`. Co-tenants the existing `DeviceMetaRecord` (key `enrolment`). Single record `WalletFlagsRecord(DateTimeOffset? WelcomedAt)`. One-way transition: null → UTC timestamp on dismissal, no un-welcome.
- **`IPendingApplicationClient`** / `HttpPendingApplicationClient` — thin HTTP client over the three endpoints, uses the wallet's `BearerTokenHandler` + `ServerClockHandler` chain.
- **`Components/WaitingCard.razor`** — pulsing skeleton card, plain HTML + CSS, `aria-live="polite"`. Rendered on Home empty-credentials branch when `IPendingApplicationClient.GetAsync()` returns a non-null notice.
- **`Components/WelcomeTakeover.razor`** — full-screen overlay reusing the cross-cutting `IdCardLayout` (umbrella invariant FR-015 — *one* visual component across form preview / reviewer pending / wallet detail) with `Watermark = Issued`, `ColourTheme = IdentityNavy`. Constructs the `IdCardLayoutConfig` from `CachedCredential` (header only; body sections empty until Spec 2's wallet UX foundations land). Pure CSS keyframes (200ms fade-in), `role="dialog" aria-modal="true"`.
- **`wwwroot/css/welcome-takeover.css`** — keyframes (`sorcha-skeleton-pulse`, `sorcha-takeover-fade-in`) + the overlay/card-frame classes.
- **`Pages/Index.razor`** orchestration — injects `IWalletFlagsStore`, loads the flags record in `OnInitializedAsync` *before* eligibility evaluation, runs `EvaluateTakeoverEligibility` at three sites per R-011 belt-and-braces ordering: init (cold-open / US4), every `SyncNowAsync` completion (foreground / US3), `OnHubCredentialAvailable` (push-then-sync). Idempotent once `WelcomedAt` is non-null (US5 / FR-006).

### Walkthrough integration

- `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1` exports `Set-SorchaCitizenPendingApplication` and `Clear-SorchaCitizenPendingApplication`. Phase 1 brackets the verification analyst's approval with these calls.
- `walkthroughs/AssuredIdentity/blueprints/assured-identity.json` — action 2's `credentialIssuanceConfig.targetAudience` is `"SorchaLocalWallet"` (the load-bearing flip from `HaipExternalWallet`). The credential lands directly in the citizen's PWA via register-native delivery; no out-of-band claim step needed.
- Phase 2 (Driving Licence) is currently a stub — the HAIP presentation flow it depended on can't run scripted against the PWA. Deferred to Spec 4 of the citizen arc.

### Runtime source

| File | Purpose |
|------|---------|
| `src/Services/Sorcha.Wallet.Service/Endpoints/PendingApplicationEndpoints.cs` | GET/PUT/DELETE handlers |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/RedisPendingApplicationStore.cs` | Distributed cache store |
| `src/Services/Sorcha.Wallet.Service/Models/PendingApplicationContracts.cs` | DTOs (`PendingApplicationNotice`, `PendingApplicationEnvelope`, `SetPendingApplicationRequest`) |
| `src/Services/Sorcha.Wallet.Service/Validators/SetPendingApplicationRequestValidator.cs` | FluentValidation rules |
| `src/Apps/Sorcha.Wallet.Pwa/Services/IWalletFlagsStore.cs` | Per-device flags interface + InMemory + IndexedDB impls + `WalletFlagsRecord` |
| `src/Apps/Sorcha.Wallet.Pwa/Services/IPendingApplicationClient.cs` | PWA HTTP client + `PendingApplicationView` |
| `src/Apps/Sorcha.Wallet.Pwa/Components/WaitingCard.razor` | Pulsing skeleton card |
| `src/Apps/Sorcha.Wallet.Pwa/Components/WelcomeTakeover.razor` | Full-screen welcome overlay |
| `src/Apps/Sorcha.Wallet.Pwa/wwwroot/css/welcome-takeover.css` | Animations + overlay styling |
| `src/Apps/Sorcha.Wallet.Pwa/Pages/Index.razor` | Eligibility + dismissal orchestration |
| `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1` | `Set-`/`Clear-SorchaCitizenPendingApplication` helpers |

Spec: `specs/124-assured-identity-pwa/`. Umbrella: `docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md`. Detailed design: `docs/superpowers/specs/2026-05-13-spec-1-assured-identity-on-pwa-design.md`.

---

## Storage Provider Audit (Feature 113)

Every audited storage interface registration goes through `IStorageRegistrationLog` from `Sorcha.ServiceDefaults.Storage`. Production and Staging refuse to start when an audited interface lands on an in-memory implementation. Operators see `[STORAGE-FALLBACK]` warnings at boot, the `storage-providers` health check reports `Degraded`, and the `Sorcha.Storage` OpenTelemetry meter exposes `sorcha_storage_provider_info` and `sorcha_storage_fallback_active` for dashboards.

### Audited interfaces (fail-fast in Production)

| Interface (FQN) | Service |
|-----------------|---------|
| `Sorcha.Wallet.Core.Repositories.Interfaces.IWalletRepository` | Wallet |
| `Sorcha.Register.Core.Storage.IRegisterRepository` | Register |
| `Sorcha.Blueprint.Service.Storage.IInstanceStore` | Blueprint |
| `Sorcha.Blueprint.Service.Storage.IActionStore` | Blueprint |
| `Sorcha.Validator.Service.Services.Interfaces.IVerifiedTransactionQueue` | Validator |
| `Sorcha.AtomicCache.IAtomicDistributedCache` | HAIP + future consumers |
| `Sorcha.Tenant.Service.Storage.IInboxStore` | Tenant (Feature 118 / T065) |

Cache-style stores (`IBlueprintStore`, `IPublishedBlueprintStore`, `BlueprintCache`, `ValidatorRegistry`, in-process routing tables) emit the warning but are intentionally not audited — they reload from the persistent transaction log on cold start.

### Adoption pattern

```csharp
var storageLog = services.GetStorageRegistrationLog();
var interfaceName = typeof(IFooRepository).FullName!;

if (hasResolverConfig)
{
    services.AddScoped<IFooRepository, EfCoreFooRepository>();
    storageLog.RegisterPersistent(interfaceName, typeof(EfCoreFooRepository).FullName!, "postgres");
}
else
{
    services.AddSingleton<IFooRepository, InMemoryFooRepository>();
    storageLog.RegisterInMemory(
        interfaceName,
        typeof(InMemoryFooRepository).FullName!,
        "no Postgres connection string in ConnectionStrings:Service:Postgres or ConnectionStrings:Sorcha:Postgres");
}
```

**Always use `typeof().FullName!`** — magic strings silently bypass the audit when the namespace doesn't match. Two FQN bugs (`IWalletRepository`, `IRegisterRepository`) were caught this way during the original feature 113 rollout.

### Bypass

`Storage:AllowInMemoryInProduction=true` skips fail-fast and emits `LogCritical`. Per-service config; intended only for CI smoke tests against ephemeral environments.

### Runtime source

`src/Common/Sorcha.ServiceDefaults/Storage/` — `IStorageRegistrationLog`, `AuditedStorageInterfaces`, `StorageRegistrationLog`, `StorageProvidersHealthCheck`, `StorageRegistrationEnforcement`, `StorageEnforcementHostedService`, `StorageRegistrationMetrics`. CLAUDE.md pattern #10 carries the operator-facing summary. Spec: `specs/113-storage-durability-audit/`. Design doc: `docs/superpowers/specs/2026-04-25-storage-clients-audit-design.md`.

---

## Atomic Distributed Cache (Feature 113)

`Sorcha.AtomicCache` is a separate common project providing `IAtomicDistributedCache` — the GETDEL + Lua-backed CAS primitive that closes the GET+DEL TOCTOU window in HAIP replay-protection state. Two consumers today: `NonceStore.ConsumeAsync` and `PreAuthCodeStore.RedeemAsync`. `PresentationRequestStore.MarkCompletedAsync` carries a `TODO(113-followup)` for the read-many+CAS migration.

### Operations

| Method | Redis primitive | InMemory primitive |
|--------|-----------------|--------------------|
| `GetAsync` | `GET` | `ConcurrentDictionary.TryGet` |
| `SetAsync(ttl)` | `SET key value EX ttl` | `ConcurrentDictionary[key]=` + expiry tracking |
| `RemoveAsync` | `DEL` | `TryRemove` |
| `GetAndRemoveAsync` | `GETDEL` (`StringGetDeleteAsync`) — single round-trip atomic | `TryRemove(key, out value)` — atomic at dictionary level |
| `TryUpdateIfMatchAsync` | Lua: `GET → if equals → SET PX ttl → return 1 else 0` | `lock` over read+write |

### Wiring

```csharp
services.AddAtomicDistributedCache(builder.Configuration, "Haip");
```

Idempotent — multiple consumers in one service each call this safely. Resolves Redis via SorchaConnections cascade (`ConnectionStrings:Haip:Redis` → `ConnectionStrings:Sorcha:Redis`). Records the choice in the storage registration log; on the audited list, so Production/Staging fail-fast applies.

### OpenTelemetry

The `Sorcha.Haip.Nonces` meter exposes `sorcha_haip_nonce_consume_total` (counter, tags: `store ∈ {nonce,preauth,presentation}`, `outcome ∈ {success,miss}`).

### Runtime source

`src/Common/Sorcha.AtomicCache/` — `IAtomicDistributedCache`, `RedisAtomicDistributedCache` (`StringGetDeleteAsync` + Lua CAS in milliseconds), `InMemoryAtomicDistributedCache`, `Extensions/AtomicCacheServiceExtensions.cs`. HAIP consumers: `src/Services/Sorcha.Haip.Service/Services/{Nonce,PreAuthCode}Store.cs`.

---

## Validator Mempool Durability (Feature 113)

`IVerifiedTransactionQueue` uses a Claim/Confirm/Release lease pattern that lets HA-replica validator deployments share one mempool without double-sealing. Two implementations:

- **`InMemoryVerifiedTransactionQueue`** — per-process `ConcurrentDictionary<string, RegisterQueue>`. Dev/test fallback. On the audited list — Production/Staging fail-fast.
- **`RedisVerifiedTransactionQueue`** — Redis sorted sets per register, single Lua claim+auto-release script. Survives validator process restart; multiple replicas with the same identity coordinate via shared Redis state.

### Lease lifecycle

```csharp
var leases = await queue.ClaimAsync(registerId, maxBatchSize, leaseDuration, ct);
try
{
    var docket = await BuildAndSealAsync(leases, ct);
    await queue.ConfirmAsync(registerId, leases.Select(l => l.TransactionId), ct);
}
catch
{
    await queue.ReleaseAsync(registerId, leases.Select(l => l.TransactionId), ct);
    throw;
}
```

If the validator dies between Claim and Confirm, the lease auto-releases on the next ClaimAsync (default 60s, `ValidatorMempool:LeaseDurationSeconds`). The Redis Lua script handles auto-release atomically as the first step of any claim. Confirm/Release are idempotent.

### Redis key layout

```
sorcha:vtq:{registerId}:available    ZSET  score=ComputeScore(priority, enqueueTime)
sorcha:vtq:{registerId}:claimed      ZSET  score=lease expiry unix-ms
sorcha:vtq:{registerId}:payload      HASH  txId → JSON(VerifiedTransaction)
sorcha:vtq:{registerId}:scores       HASH  txId → numeric score (priority restoration)
```

`{registerId}` cluster-slot braces keep multi-key Lua / batch operations slot-local. Score = `-priority * 1e13 + enqueuedAtUnixMs` so `ZRANGE 0..N-1` returns highest-priority first with FIFO within priority class.

### OpenTelemetry

The `Sorcha.Validator.Mempool` meter exposes `sorcha_validator_mempool_lease_expired_total` (counter, per-register). Per-register size gauge tracked as follow-up — Redis impl can't compute cross-register totals cheaply.

### Selection

`VerifiedQueueExtensions.AddVerifiedTransactionQueue` branches on the SorchaConnections Redis cascade. `n1` deployments get the Redis-backed implementation automatically once `ConnectionStrings:Sorcha:Redis` is set; fallback paths are flagged at boot via the storage registration log.

### Runtime source

`src/Services/Sorcha.Validator.Service/Services/{In,}VerifiedTransactionQueue.cs` (in-memory + Redis impls embed Lua scripts as constants), `src/Services/Sorcha.Validator.Service/Services/ValidatorMempoolMetrics.cs`, `src/Services/Sorcha.Validator.Service/Extensions/VerifiedQueueExtensions.cs`. Single caller: `src/Services/Sorcha.Validator.Service/Services/DocketBuilder.cs` (claim → build → confirm; release on failure; **genesis path also confirms** — caught by claude-review on PR #416 as a real lease-leak bug).

---

## AI Discoverability Surface (Feature 117)

The AI-agent-facing surface every external consumer reads. Every artefact below is gated by the `ai-discoverability-check` workflow on every PR to `master`. Touch any of these and the workflow re-runs the structural checks in `scripts/check-discoverability.sh`.

### Well-known endpoints (served by the gateway)

| Endpoint | Purpose | Source |
|---|---|---|
| `GET /.well-known/openapi.json` | Aggregated OpenAPI 3.1 with `info.x-mcp-server`, `info.x-standards`, version from assembly | `src/Services/Sorcha.ApiGateway/Discoverability/WellKnownOpenApiEndpoints.cs` |
| `GET /.well-known/openapi.yaml` | YAML form of the same document | (same handler) |
| `GET /.well-known/mcp.json` | MCP server manifest — transports, authentication, tool catalogue | `src/Services/Sorcha.ApiGateway/Discoverability/McpManifestEndpoint.cs` |
| `GET /api/mcp/tools` | Full MCP tool catalogue (36 tools across admin/designer/participant slices) | `src/Services/Sorcha.ApiGateway/Discoverability/McpToolCatalogueEndpoint.cs` |

### Repo-root files

| File | Purpose | Notes |
|---|---|---|
| `llms.txt` | One-screen factual summary, llmstxt.org-conforming, ≤ 8192 bytes | Structural check enforces single H1 / single blockquote / `## Capabilities` / `## Standards` / `## Links` |
| `STANDARDS.md` | Single source of truth for every implemented standard | Cross-referenced by `llms.txt`, `docs/llms-full.txt`, and every published doc's `standards[]` frontmatter |

### Published documents under `docs/`

| File | Purpose |
|---|---|
| `docs/architecture.md` | Architectural overview — services, evidence flow, discovery surface |
| `docs/openid4vc-haip-integration.md` | Wallet ecosystem boundary (OpenID4VCI / OpenID4VP / HAIP 1.0) |
| `docs/applicability.md` | Regulatory-pull domains (DPP, trade finance, IPC-1782, municipal) |
| `docs/security-model.md` | Selective disclosure, post-quantum posture, honest gaps |
| `docs/quickstart.md` | Agent-runnable setup against a clean Docker host |
| `docs/mcp-server.md` | Connecting an AI agent via MCP (transports, auth, role slices, worked example) |
| `docs/llms-full.txt` | Long-form machine-readable narrative, ≤ 32 KB |

Each published doc carries YAML frontmatter (`title`, `description`, `standards[]`, `last_updated` ISO date). The `standards[]` entries must match a `full|partial` row in `STANDARDS.md` verbatim — abbreviations like "W3C VC Data Model 2.0" will fail the check; use the full row name "W3C Verifiable Credentials Data Model 2.0".

### Tooling

| Path | Purpose |
|---|---|
| `scripts/check-discoverability.sh` | Local + CI orchestrator, runs every structural sub-check |
| `.spectral.yaml` | OpenAPI lint rules including the marketing-adjective deny-list |
| `.github/workflows/ai-discoverability-check.yml` | CI workflow — runs the orchestrator on every PR |
| `.github/pull_request_template.md` § *Standards & discoverability* | Author-facing reminder to update `STANDARDS.md` / bump `last_updated` / review `llms.txt` when standards change |

### Tone source for any new content

`docs/strategic-context.md` — canonical voice and framing for every machine-readable artefact. Read before writing or revising `info.description`, `llms.txt`, MCP tool descriptions, or any of the published docs. Marketing adjectives (revolutionary, best-in-class, industry-leading, cutting-edge, world-class, seamless, game-changing, next-generation, state-of-the-art) are deny-listed and CI-enforced.

## Council application enrolment gate (Feature 126)

Spec 3 of the Strathcarron citizen arc. The cold-start onboarding gate that turns a council-page visitor into a Sorcha-account-holding, wallet-enrolled citizen as a side-effect of the application form they came for. **Drop-in library component** consumed by any council page.

### Three citizen tiers (derived, never persisted)

| `/whoami` | `/me/devices.Count` | Tier | Surface |
|---|---|---|---|
| 401 | — | 3 (ColdStart) | `PreflightSignupSurface` — explainer + signup CTA. No QR. |
| 200 | 0 | 2 (MiniGate) | `WalletPairingSurface` with `TierMode.MiniGate` copy. |
| 200 | ≥1 | 1 (FastPath) | `ChildContent` — the application form. |

Tier is recomputed on every visit. Transitions are one-way (ColdStart → MiniGate → FastPath).

### Server surface (Sorcha.Tenant.Service)

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/auth/enrol-session` | Mint a one-time enrolment session JWT for the signed-in caller. Returns `{ sessionToken, qrUrl, expiresAt }`. Scope `"enrol"`, 10-min TTL, signed with the existing auth signing key. |
| `POST` | `/api/auth/enrol-session/redeem` | Anonymous. Validates signature + scope + expiry, atomically consumes the JTI via `IAtomicDistributedCache.GetAndRemoveAsync`, returns `{ accessToken, expiresIn, displayName, email }`. 400 / 409 / 410 mapped to `RedeemEnrolSessionErrorCode`. |

Single-use enforcement: `EnrolSessionService.MintAsync` writes a sentinel value at `sorcha:enrol-session:{jti}`; `RedeemAsync` consumes it atomically. First redeem wins; absent-at-redeem with a still-valid JWT means "already used". The displayName + email come fresh from `PlatformUser` at redeem time so the PWA dialog reflects the user's current profile.

### TenantHub.DeviceEnrolled event

`Task DeviceEnrolled(Guid platformUserId, Guid deviceId)` on `ITenantHubClient`. Published from `PlatformUserDeviceService.RegisterAsync` (including idempotent re-register) to the per-user group via `TenantHubGroups.User`. Try/log/swallow on publish failure — never fails the device write. Thin-signal payload — opaque IDs only, council page fetches details via the existing `GET /api/v1/me/devices`.

### `?returnTo=` allowlist

`ReturnToAllowlistOptions` from `Auth:ReturnToAllowlist:Hosts` — HTTPS-only-except-`http://localhost`, exact-host or `*.host` suffix. Open redirects fail closed. Wired into `LoginModel.IsValidReturnUrl` and `SignupModel.IsValidReturnUrl` as an overload that accepts the allowlist alongside the existing relative-only path.

### Library component

`EnrolGateComponent` in `Sorcha.UI.Components.User` (under `Sorcha.UI.Core.Components.EnrolGate` namespace). Consumer-side API:

```razor
<EnrolGateComponent CouncilName="Strathcarron Council"
                    ServiceLabel="driving licence application"
                    OnReady="@HandleCitizenReady">
    <!-- the form goes here; renders only after the gate clears -->
    <DrivingLicenceForm />
</EnrolGateComponent>
```

`OnReady` fires with the resolved platformUserId once the citizen reaches FastPath. Sub-components: `PreflightSignupSurface`, `WalletPairingSurface` (with `TierMode.MiniGate` / `TierMode.PostSignup` copy), `HybridQrAffordance` (with `HybridQrLayout.Auto` / `QrFirst` / `LinkFirst` for FR-008 same-device mobile prominence).

A complete worked example of a consumer page composing `EnrolGateComponent` lives in `samples/strathcarron-portal/Pages/DrivingLicence.razor` — the Strathcarron Council sample portal that ships alongside the platform per the platform-vs-consumer boundary contract (`docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md`). F127 PR-A relocated this page out of `Sorcha.UI.Web.Client` into `samples/` as the structural prerequisite for the F127 credential-gating work.

### Cross-device pairing signal

`IEnrolPairingSignal` composes `TenantHubConnection.OnDeviceEnrolled` (SignalR, primary) with a 3-second `/api/v1/me/devices` poll (fallback after 2 s of no hub connection). Manual-recovery affordance fires after 60 s of no signal (FR-016 / SC-005). On signal, the surface flips to "Phone ready ✓" and `OnReady` cascades up to the consuming council page.

### Observability

`Sorcha.Enrolment` OTel meter — three instruments per research §R-010:
- `sorcha_enrol_session_minted_total` counter, tag `purpose ∈ {tier3_first_qr, regenerate}`
- `sorcha_enrol_session_redeemed_total` counter, tag `outcome ∈ {success, expired, replay, scope_mismatch, signature_fail, malformed}`
- `sorcha_enrol_pairing_signal_latency_seconds` histogram, tag `path ∈ {signalr, polling}`

### Non-obvious patterns worth keeping

- **Custom `LifetimeValidator` returning `false` raises `SecurityTokenInvalidLifetimeException`, NOT `SecurityTokenExpiredException`.** For deterministic Expired detection with an injected `TimeProvider`: set `ValidateLifetime=false` and check the `exp` claim manually after `ValidateToken`.
- **`Sorcha.AtomicCache` ProjectReference is NOT transitive** through `Sorcha.ServiceDefaults`. Services calling `AddAtomicDistributedCache` need explicit ProjectReference; test projects too if they mock `InMemoryAtomicDistributedCache`.
- **`Sorcha.UI.Components.User` RootNamespace is `Sorcha.UI.Core`** — files live under `Components.User/...` folders but namespaces are rooted at `Sorcha.UI.Core`, so consumers `using Sorcha.UI.Core.Components.EnrolGate`.
- **Single-use enforcement on `IAtomicDistributedCache`** uses SetAsync-at-create + GetAndRemoveAsync-at-consume (the established `Sorcha.Haip.Service.NonceStore` pattern). No native `SET NX` on the interface — this pattern is the convention.
- **Idempotent re-register publishes `DeviceEnrolled` too.** A council page that missed the original signal (refresh, hub disconnect during first enrolment) still advances. Subscribers tolerate the repeat.

## Credential gates (Feature 127)

Spec 4 of the Strathcarron citizen arc. Adds **`SorchaWalletPresentationConsumer`** to F111's Timebound Presentation Lifecycle — the first non-HAIP `IPresentationConsumer`. Council pages gate a starting action on the citizen's existing Sorcha-Wallet-held credential; the disclosed claims pre-populate the form on the next action; the citizen fills the gap fields and submits. Full design: `docs/superpowers/specs/2026-05-15-spec-4-credential-gated-second-service-design.md` (§14 carries the F111 reconciliation that landed during PR-B; the pre-amendment shape of §3/§4/§8 is superseded).

### Three-action chain (the F111 idiom for "verify then fill")

```
verify-identity         citizen, starting action
                        credentialRequirements[0].presentationSource = "SorchaWallet"
                        no form schema — exists solely to gate
       │
       ▼ predecessor
submit-blue-badge       citizen
                        form schema with only the gap fields
                        x-persona.presentation = "verify-identity"  (autofill)
       │
       ▼ predecessor
issue-blue-badge        licensing-officer
                        credentialIssuance to SorchaLocalWallet
```

Worked example: `walkthroughs/Strathcarron/blueprints/strathcarron-blue-badge.json`.

### The callback verifies ONLY against the server-side pending row (G2 fix, 2026-07-29)

`SorchaWalletVerificationPayload` (the wire shape `POST /callbacks/sorcha-wallet/{requestId}` binds) carries `VpToken` and `DelegationCredential` — **no `VerifierSession`**. It used to carry an optional `session` field that, when present, was used verbatim instead of the server-rebuilt one. Since the callback only requires consumer-tier authentication (any signed-in citizen — `RequireConsumerAudience`, CLAUDE.md §13), an attacker could POST their own session object with an attacker-chosen `RequiredVct`, an emptied `RequiredClaims` gate, and an attacker nonce/clientId — satisfying any credential gate with any held credential, no forgery required. `SorchaWalletPresentationConsumer.VerifyAsync` now **always** rebuilds the `VerifierSession` from the pending-presentation `PresentationInitiationContext` (`Nonce`, `CredentialType`, `RequiredClaimNames`, resolved `ClientId`); nothing under the wire payload's shape can influence which session the validator checks against. System.Text.Json silently drops an unknown `session` property a stale or malicious caller still sends.

### Server surface (extends F111's existing surface)

| Method | Path | Owner | Status |
|---|---|---|---|
| `POST` | (existing action-submission endpoint) | F111 | unchanged — submitting the `verify-identity` action fires F111's `InitiateAsync`, which dispatches to `SorchaWalletPresentationConsumer.BuildInitiationAsync` and mints a single-use `ClaimsFetchToken`. |
| `GET` | `/api/presentations/{requestId}/status` | F111 | unchanged — used as the polling fallback. |
| `POST` | `/api/presentations/callbacks/sorcha-wallet/{requestId}` | F111 | unchanged endpoint shape — wallet posts signed VP; F111 dispatches to `SorchaWalletPresentationConsumer.VerifyAsync`. |
| `GET` | `/api/presentations/{requestId}/disclosed-claims?token={ClaimsFetchToken}` | **NEW (F127)** | Returns disclosed claims in plaintext to the council page for autofill. Token-authed (single-use), bound to a specific `presentationRequestId`. Statuses: 200 `success` / 200 `pending` / 401 `token-*` / 410 `outcome-decline` / 410 `outcome-abandoned` / 410 `claims-expired`. |

### `IPresentationConsumer` extension (the F111 deferred contract)

F127 lands F111's "non-HAIP initiation contract extension" via a default-throws default-interface-method:

```csharp
public interface IPresentationConsumer
{
    string ConsumerName { get; }
    Task<PresentationOutcome> VerifyAsync(...);
    Task<ConsumerInitiationDescriptor> BuildInitiationAsync(...)
        => throw new NotSupportedException(...);  // HAIP keeps its hardcoded path
}
```

HAIP impls remain unchanged. `SorchaWalletPresentationConsumer` overrides `BuildInitiationAsync` to return an OID4VP `openid4vp://` request URI carrying the council DID, presentation definition derived from `credentialRequirement`, fresh nonce, and `response_uri` resolving to the F111 callback endpoint.

### Hub event

`Task PresentationOutcomeReady(string presentationRequestId)` on `IBlueprintHubClient`. Published from `PresentationLifecycleService.HandleOutcomeAsync` to `BlueprintHubGroups.PresentationNonce(presentationRequestId)` on every terminal outcome write (success or decline). Thin-signal contract — opaque ID only; council page fetches lifecycle state via F111's status endpoint, and on success fetches plaintext claims via the new disclosed-claims endpoint.

### Library component — one card, two transports

`PresentationRequestCard` in `Sorcha.UI.Components.User` (namespace `Sorcha.UI.Core.Components.Presentation`) is the **single** credential-gate surface. It replaced both `CredentialGateComponent` (council pages) and the web app's HAIP-only `PresentationRequestQrCard`, which are deleted.

```razor
<EnrolGateComponent CouncilName="..." OnReady="@HandleCitizenReady">
    <PresentationRequestCard RequestId="@_init.PresentationRequestId"
                             PresentationRequestUri="@_init.AuthorizationRequestUri"
                             ClaimsFetchToken="@_init.ClaimsFetchToken"
                             Source="PresentationSource.SorchaWallet"
                             LinkBackUrl="/services/driving-licence"
                             NameOfMissingCredentialType="an Assured Identity"
                             OnClaims="@HandleClaimsAsync">
        <BlueBadgeForm Disclosed="@_disclosed" OnSubmit="HandleFormSubmit" />
    </PresentationRequestCard>
</EnrolGateComponent>
```

The page owns the action-submission HTTP call (its own auth, retry policy, error UX); the card owns QR + waiting + claims-fetch + autofill handoff. `ChildContent` renders only after `GateOutcome.Success`. `OnClaims` yields `IReadOnlyDictionary<string, object?>?` — the same shape as `DisclosedClaimsResponse.Claims`.

**`Source` is load-bearing.** The card resolves an `IPresentationGateTransport` from it:

| Source | Transport | Waits on | Claims |
|---|---|---|---|
| `SorchaWallet` | `SorchaWalletGateTransport` | `IPresentationSignal` (hub + `/status` poll) | `/disclosed-claims?token=` |
| `HaipExternalWallet` | `HaipGateTransport` | `IHaipOfferService` result poll | inline with the outcome |

Each transport maps its own vocabulary onto `GateOutcome` (`Pending`/`Submitted`/`Success`/`Declined`/`Expired`/`Abandoned`/`Unreachable`); F111's `abandoned-with-late-outcome` maps to `Success`. **`Unreachable` is distinct from `Expired` on purpose** — the lifecycle holds no such request, which is our fault, not the citizen's. `AddSorchaPresentationGate(baseAddress)` registers the SorchaWallet transport; the HAIP transport is registered beside `IHaipOfferService` in `AddCoreServices`, because a council page has no HAIP service and the card injects `IEnumerable<IPresentationGateTransport>`.

#1330 adds a same-device route: when the citizen's own server-custody wallet holds a match, `PresentationRequestCard` probes via `ISorchaWalletLocalPresenter` (Sorcha.UI.Components.User) and renders a "Use this device" consent panel (`UseThisDevicePanel`) with the QR collapsed beneath; hosts without the presenter registered (council portal) stay QR-only.

> **Why this exists.** `ActionExecutionService` dropped both the `PresentationSource` and the `ClaimsFetchToken` when mapping `PresentationInitiationResult` onto the submission response, so the web app routed **every** gate to HAIP. A SorchaWallet gate therefore polled a verifier that had never heard of it, 404'd for five minutes, and reported "Expired". Both fields now ride on `PresentationRequestResponse`/`PresentationRequestInfo`, guarded by `Sorcha.UI.ContractTests`.

### Cross-device coordination

`IPresentationSignal` composes `PresentationHubConnection.OnPresentationOutcomeReady` (SignalR primary) with a 3-second F111 `/status` poll (fallback after 2 s of no hub connection). Manual-recovery affordance fires after 60 s of no signal. Mirror of F126's `IEnrolPairingSignal` cadence.

`OnRequestUnreachable` fires after **three consecutive 404s** from `/status`. A 404 is permanent — the lifecycle holds no such request — while a 500 may succeed next tick; collapsing the two into one null is what let a doomed gate run out its whole window and then read as an expiry. Three, not one, because a just-created request can 404 briefly before its lifecycle row is visible.

> The `/status` response field is **`state`**, not `status` (`PresentationSignal.StatusProbeShape`). Reading the wrong one deserialises to null on every poll and hangs the gate silently.

> Named `PresentationHubConnection` rather than `BlueprintHubConnection` because `Sorcha.UI.Core.Services.BlueprintHubConnection` already exists for admin / workflow notifications and the two would collide.

### Storage

Two new short-TTL Redis stores in `Sorcha.Blueprint.Service.Storage.Presentations/`:

- **`IClaimsFetchTokenStore`** — token → presentationRequestId binding. Single-use via Lua GETDEL.
- **`IDisclosedClaimsStore`** — plaintext claims keyed by presentationRequestId, TTL = remaining validity window (floored at 10 s). Written by `HandleOutcomeAsync` immediately BEFORE the hub publish — guarantees race-safe-readable claims the moment the council page receives the signal.

Both back the disclosed-claims endpoint; the register transaction remains the legal record, the Redis stash is the operational signal.

### Non-obvious patterns worth keeping

- **`PresentationSource.SorchaWallet`** enum value on `CredentialRequirement` is the blueprint-author surface. JSON-serialised as `"SorchaWallet"` (PascalCase). Maps to consumer-name `"sorcha-wallet"` in `PresentationLifecycleService.InitiateAsync`'s dispatch switch.
- **`ClaimsFetchToken` is opt-in**: `InitiateAsync` mints + returns one ONLY for consumers that produce council-page-readable claims (currently `"sorcha-wallet"`). HAIP gets `null`.
- **The disclosed-claims endpoint consumes the token on pending-state too** — the council page must reuse the same token on retry. Because the page subscribes to the hub signal and fetches only on outcome ready, pending-state fetches should be rare; the token semantics keep the surface simple.
- **F119 deferred outcomes don't publish `PresentationOutcomeReady` yet.** The typical inline path covers the common case; the deferred-write path (seal-aware ordering) needs its own publish from `PresentationSealSubscriber`. Inline TODO flagged in `PresentationLifecycleService.HandleOutcomeAsync`.
- **Verifier-DID resolution (Spec 5, shipped) populates the OID4VP `client_id`** with the council org's canonical DID. `PresentationLifecycleService.InitiateAsync` resolves `blueprint.OrganizationId` → `did:sorcha:org:{walletAddress}` via `IOrgDidDocumentClient.ResolveCanonicalDidAsync` (GET `/orgs/{orgId}/did.json`, read the document `id`) and passes it as `PresentationInitiationContext.VerifierClientId`. `SorchaWalletPresentationConsumer.BuildInitiationAsync` emits `context.VerifierClientId ?? "did:sorcha:org:UNKNOWN"` — the placeholder is now the graceful-degradation fallback (org with no published DID document), not a TODO. The request is still **unsigned**, so `client_id` is a display identity; signed request objects (mutual auth) are the deferred Scope B. The production issuer-side `IIssuerKeyResolver` is wired in Blueprint Service (`DidResolverBackedIssuerKeyResolver` via F120), so the same `client_id` resolves to the org's published signing keys when Scope B lands. Design: `docs/superpowers/specs/2026-05-20-spec-5-verifier-did-resolution-design.md`.


## Cold-start onboarding (Feature 128)

Four citizen routes outside the F126 council-page gate, all sharing the existing `enrol-session` primitive extended with a `mode` discriminator.

### Primitive extension

`POST /api/auth/enrol-session` accepts an optional `mode: gated | standalone` body field. Default `gated` — preserves F126 callers verbatim. The discriminator is persisted as a signed JWT claim (`pair_mode`) on the session token and echoed on `POST /api/auth/enrol-session/redeem`. Telemetry counters `sorcha_enrol_session_minted_total` and `sorcha_enrol_session_redeemed_total` gain a `mode` dimension.

### New endpoints

| Path | Auth | Purpose |
|---|---|---|
| `POST /api/auth/enrol-session/short-code` | bearer | Mint a 6-digit numeric short code wrapping a standalone enrol-session token. 5-min TTL, single-use, 5-attempts-per-code rate-limit. Used by the takeover sub-affordance + mobile-web install fallback. |
| `POST /api/auth/enrol-session/redeem-short-code` | anonymous | Redeem a 6-digit code → underlying enrol-session redeem result. |
| `GET /api/v1/me/devices/has-any` | bearer | Aggregate read returning `{ hasAnyDevice, latestEnrolledAt }`. Drives the takeover trigger + nag-banner trigger. |
| `GET /api/v1/wallet/exists` | consumer-tier | **(F149)** Aggregate read returning `{ hasWallet }`. ALWAYS 200 for an authenticated consumer (no 401/404 ambiguity). Drives the wallet-aware branch of `PairingTakeover`. |
| `POST /api/auth/pairing-resumption-email` | bearer, rate-limited | Dispatches the "Email me a link" magic-link to the caller's account email. |
| `GET /api/auth/pairing-resumption/redeem?token={id}` | anonymous | Redeems the magic-link → 302 to `/auth/login?returnUrl=/setup/add-device&email=...&reason=pairing-resumption`. Single-use. |

### Hub event extension

`IWalletHubClient.DeviceEnrolled(Guid deviceId)` — broadcast on `WalletHubGroups.CitizenWallet(platformUserId)` from `CitizenWalletEndpoints.EnrolDevice` after registration success. Mirrors the existing `DeviceRevoked` event. Drives F128 takeover auto-dismissal on remote pair-success.

### Shared client services (Sorcha.UI.Components.User)

- **`IHasPairedDeviceProbe`** (`Services/User/Devices/`) — typed HttpClient calling `GET /api/v1/me/devices/has-any`. Per-session cache, `Changed` event, optimistic `RaiseLocalPairCompleted()` flip. Registered in both `Sorcha.Wallet.Pwa` (drives `PairingTakeover`) and `Sorcha.UI.Web.Client` (drives `PairingNagBanner`).
- **`IPwaInstallabilityProbe`** (`Services/User/Pairing/`) — JS-interop probe via `wwwroot/js/pwa-install-probe.js`. Three-verdict (CannotInstall / CanInstallProgrammatically / CanInstallManually). Determines whether `PairingHandoffSurface` renders the QR variant or the install variant.

### Components (Sorcha.UI.Components.User)

- **`PairingTakeover`** (`Sorcha.Wallet.Pwa/Components/PairingTakeover.razor` — **PWA-local, NOT in Components.User**) — full-page overlay mounted in `Sorcha.Wallet.Pwa/MainLayout.razor` outside `MudLayout`. Renders when `HasAnyDevice == false`. **(F149) Wallet-aware:** when there is no device here it runs a one-shot `IHasWalletProbe.HasWalletAsync`; a walletless citizen sees a "Create your wallet first" body that force-loads the web `/app/wallets/create` handoff (the web Blazor client is under the /app base path — NOT origin-root) (companion-first), while a wallet owner gets the pair body below. The overlay stays hidden until both the device check and the wallet check resolve (no flash). Pair-body primary action invokes `IEnrolmentService.EnrolAsync` against the existing PWA session; secondary disclosure accepts a 6-digit code for cross-device-to-this-device pairing. Auto-dismisses on probe-change OR `CitizenWalletHubConnection.OnDeviceEnrolled`.
- **`PairingHandoffSurface`** (`Components/Pairing/`) — hosted at `/setup/add-device`. Switches on `IPwaInstallabilityProbe` verdict between the QR variant (desktop) and the install variant (mobile, with always-visible short code). Common Skip + "Email me a link" affordances.
- **`PairingNagBanner`** (`Components/Pairing/`) — persistent dismissable banner mounted in `Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`. Renders when `HasAnyDevice == false`. CTA → `/setup/add-device`.

### Post-login routing gate

`Sorcha.Tenant.Service/Pages/Auth/Login.cshtml.cs`'s `RedirectToApp` extends to query `IPlatformUserDeviceService.HasAnyAsync` (parsing `platform_user_id` from the freshly-issued access token). When zero paired devices AND no explicit `ReturnUrl` override, the returnUrl defaults to `/setup/add-device` so the WASM client lands the citizen on `PairingHandoffSurface`. FR-020 + FR-026.

### Spec / contracts refinements vs the original brainstorm

1. **Discriminator is a JWT claim, not a server cache record.** Simpler, inherits single-use enforcement for free.
2. **`returnTo` is NOT on the mint API.** Stays a URL-query concern on the redeem side (matching F126). Mode/returnTo coherence enforcement is a redeem-page concern, not a mint-API concern.
3. **Pairing-resumption redeem is conservative.** 302s to `/auth/login` with email pre-filled instead of auto-signing-in. The auto-sign-in variant (mint fresh access+refresh tokens, 302 to `/app/`) is captured as a polish-phase TODO.
4. **Seamless `start_url`-baked token (FR-031) deferred.** Short-code fallback covers Story 3 end-to-end without the iOS-quirk-dependent seamless path. SC-006 measurement post-launch drives whether to add the seamless path.

### Files / file paths

- Server: `Sorcha.Tenant.Service/Services/{EnrolSessionService,PairingShortCodeService,PairingResumptionTokenService}.cs`; `Endpoints/{EnrolSessionEndpoints,PairingShortCodeEndpoints,PairingResumptionEndpoints,PlatformUserDeviceEndpoints}.cs`; `Emails/Templates/pairing-resumption.{html,txt}`
- Wallet Service: `Endpoints/CitizenWalletEndpoints.cs` (DeviceEnrolled broadcast); `Hubs/IWalletHubClient.cs` (DeviceEnrolled signature)
- Wallet PWA: `Components/PairingTakeover.razor`; `Services/CitizenWalletHubConnection.cs` (OnDeviceEnrolled); `Services/Enrolment/{IPairingShortCodeRedeemer,PairingShortCodeRedeemer}.cs`
- Sorcha.UI.Components.User: `Components/Pairing/{PairingHandoffSurface,PairingNagBanner}.razor`; `Services/User/Devices/{IHasPairedDeviceProbe,HasPairedDeviceProbe}.cs`; `Services/User/Pairing/{IPwaInstallabilityProbe,PwaInstallabilityProbe}.cs` (`PairingTakeover.razor` is PWA-local — listed under Wallet PWA above)
- Sorcha.UI.Web.Client: `Pages/Setup/AddDevice.razor`; `Pages/Get.razor`; `wwwroot/js/pwa-install-probe.js`

### Feature 149 — wallet-aware PairingTakeover (companion-first P0)

Closes the cold-start dead-end where a signed-in but walletless citizen tapped "Set up this
device" → enrol 404. Companion-first: the web app owns wallet creation, so the PWA routes the
citizen there instead of building in-PWA wallet creation.

- **Endpoint:** `GET /api/v1/wallet/exists` → `200 { hasWallet }` (consumer-tier, always 200) in
  `Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` (`WalletExists` handler; projects
  `ResolveCitizenContextAsync` → `walletAddress is not null`). DTO `WalletExistsResponse` in
  `Sorcha.CitizenWallet.Abstractions/Models/`.
- **Why not reuse holder-keys/enrol 404:** `holder-keys` 401s on no-wallet and 404s on
  *wallet-but-no-key* (the opposite signal); the no-wallet status is inconsistent across the
  citizen surface. A dedicated boolean endpoint is unambiguous.
- **Client:** `IHasWalletProbe` + `HasWalletProbe` (`Sorcha.Wallet.Pwa/Services/Wallet/`,
  PWA-local). **One-shot** (`Task<bool> HasWalletAsync` only — no `Changed`/`Refresh`): walletless
  is a terminal cold-start state (false→true once, never back). **Fail-safe `true`** on any
  transient failure (network / non-2xx / empty / malformed body) so a real wallet owner is never
  routed to create a second wallet. Registered with the Bearer + ServerClock handler chain.
- **State machine** (`PairingTakeover`): hidden while `HasAnyDevice == null` OR `_hasWallet ==
  null`; `HasAnyDevice == true` → hidden; `false`+`_hasWallet == false` → create-wallet body;
  `false`+`_hasWallet == true` → existing pair body (unchanged). The wallet check runs once, only
  when there is no device here.
- **Tests:** `CitizenWalletExistsEndpointTests` (Wallet Service, reflection handler invocation);
  `HasWalletProbeTests` + `PairingTakeoverTests` (PWA, bUnit — incl. a nav-target assertion for
  `/app/wallets/create`).
- **Web-base-path gotcha (fix PR after #978):** the create-wallet CTA must target
  `{origin}/app/wallets/create`, not origin-root `/wallets/create` (404 on n1) — the web Blazor
  client is mounted under `/app`. This differs from `SignIn.razor` `GoToWebSignup`, whose
  `/auth/signup` IS a root-level tenant-service Razor page. Don't assume web routes are at origin
  root just because a sibling handoff is.

---

## EUDI credential format & unified trust (Feature 135)

Two coupled capabilities, shipped across three merged PRs (US1 #806, US2 #807, US3 #809):

1. **One trust decision for every verification path.** Both the internal Blueprint engine `CredentialVerifier` and the HAIP OpenID4VP `HaipPresentationVerifier` route the trust decision through a single `ITrustEvaluator` (in `Sorcha.Blueprint.Engine.Credentials`, WASM-friendly). The historical engine defect where `SignatureValid` was hard-coded `false` ("defer to the service layer") is gone — signatures are verified for real and trust **fails closed** by default.
2. **A credential-format seam** adding ISO `mso_mdoc` (CBOR/COSE) beside SD-JWT VC, online/OpenID4VP only (proximity deferred), with a selectable issuer trust anchor.

### The trust model

- **`CredentialRequirement`** dropped the flat `AcceptedIssuers` list → gained `Format` (default `SdJwtVc`) + `TrustPolicy?`. **`CredentialIssuanceConfig`** gained `Format` + `TrustAnchor` (`register` | `x509-tenant` | `x509-lotl`, default `register`).
- **`TrustPolicy`** = `Sources` (`TrustSourceRef[]`) + `Combinator` (`AnyOf`/`AllOf`) + `MinAssuranceLevel` (`Low`<`Substantial`<`High`). Null policy → default register@Low (`TrustPolicyExtensions.FromLegacyIssuers`). `TrustPolicyExtensions` lives in namespace `Sorcha.Blueprint.Models.Credentials` — call it fully-qualified from files that only `using`-alias the model.
- **`ITrustSourceResolver`** (one per `TrustSourceKind`): `register` (DID/assertionMethod via `IIssuerDirectory`), `x509-tenant` (X.509 chain to a tenant root via `ITenantTrustAnchorProvider`), `did-allowlist` (direct + alsoKnownAs), `trustlist` (operator snapshot — `TrustListSourceResolver` subclasses the x509 source, requesting the anchor set by `TrustSourceRef.TrustListId`). Resolvers are engine-local; network sources inject behind seams with service-layer adapters (mirrors the `IRevocationChecker` WASM pattern). The engine ships `ITrustResolverRegistry`; `TrustEvaluator` does signature-precondition → per-source vouch + combinator → assurance (source-tier + upward-only claim override, honoured only for ≥Substantial sources) → status via `IStatusListChecker` → `TrustEvidence` + SHA-256 policy digest.
- **`IStatusListChecker`** unifies revocation: `BitstringStatusListChecker` (W3C) and `IetfTokenStatusListChecker` (IETF, explicit interface impl — the two have distinct `StatusListBit` enums) both implement it.
- **`TrustEvidence`** (vouching source, register height / CRL version / trust-list id+freshness, assurance, policy digest) is carried on `VerificationResult` for spec-079 receipts — re-checkable offline.
- **`TrustMetrics`** (`Sorcha.Trust` meter, registered in ServiceDefaults): `sorcha_trust_decision_total{outcome,source,format,assurance,reason}` — no subject data (FR-024). Recorded from both verification paths.

### Service-layer adapters (engine stays dependency-free)

`IIssuerDirectory` + `IIssuerKeyResolver` + `ITenantTrustAnchorProvider` are engine-local seams. Each consuming service owns thin adapters: Blueprint.Service has `DidIssuerDirectory` + `DidX5cIssuerKeyResolver` (the x5c→DID→jwk port); HAIP has its own `DidIssuerDirectory` + `ConfiguredTenantTrustAnchorProvider` (roots from `Haip:TrustedRootCertificates`). `HaipPresentationVerifier` went Singleton→Scoped (it consumes the scoped `IDidResolverRegistry`).

### mdoc (mso_mdoc) — ISO 18013-5 on the BCL

`Sorcha.Mdoc` (extracted from `Sorcha.Cryptography` in F185 so the WASM wallet can use it; BCL + BouncyCastle — `System.Formats.Cbor` + `System.Security.Cryptography.Cose`, pinned 10.0.8):
- `MdocCbor` — tag-24 (`#6.24(bstr .cbor X)`) wrap/unwrap **verbatim** (digests/signatures are over the tagged outer bytes; capture via `CborReader.ReadEncodedValue()`, splice via `CborWriter.WriteEncodedValue`). `CoseX5Chain` — x5chain on COSE label 33 (RFC 9360). Models: `IssuerSigned(Item(Bytes))`, `MobileSecurityObject`(+`MsoStatus`/`ValidityInfo`), `DeviceResponse`/`Document`/`DeviceSigned`/`DeviceAuth`. `MdocCodec` — encode/decode + the OpenID4VP 1.x hash-based `SessionTranscript` + `DeviceAuthentication` builders.
- `MdocService.Verify` — issuer COSE_Sign1 over the MSO (key from x5chain leaf), value-digest integrity (fixed-time), holder binding over the reconstructed `DeviceAuthentication`, validity window.
- `MdocIssuer.IssueIssuerSigned` — builds + signs an mdoc credential (ES256/P-256 only; throws otherwise).
- ~~**MAC-based device auth (`deviceMac`) is NOT verified** in v1 (BCL has no `COSE_Mac0`; OpenID4VP uses `deviceSignature`).~~ **Superseded by Feature 185**, which added `CoseMac0` (the BCL still has no such type — we hand-rolled it) and now verifies `deviceMac`. Online OpenID4VP still uses `deviceSignature`; a `deviceMac` presented with no `EMacKey` is **rejected**, not waved through.

### Format handlers + verifier wiring

- `ICredentialFormatHandler.VerifyAsync` per format. `SdJwtVcFormatHandler` (engine) resolves the issuer key via `IIssuerKeyResolver`, verifies via `ISdJwtService` (issuer-only or KB overload), populates `IssuerContext`, calls the evaluator. `MdocFormatHandler` (engine) runs `MdocService` then routes trust through the evaluator over the issuer x5chain + MSO status; fails closed on bad signature/digests/binding before trust. `MdocFormatHandler.IssueAsync` validates the format/anchor combo (mdoc requires X.509 anchor + chain; register/no-chain fail closed) and wraps `MdocIssuer`.
- The engine `CredentialVerifier` now dispatches by `CredentialRequirement.Format` to the format handler and only orchestrates type-match + required-claim constraints.
- `MdocPresentationVerifier` (HAIP) maps a base64url `vp_token` through `MdocFormatHandler` onto the shared `VerificationResult`.

### Endpoints

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/credential` (OpenID4VP `direct_post`, existing) | Accepts SD-JWT VC **or** `mso_mdoc`. mdoc `vp_token` is a JSON object `{ "<queryId>": ["<base64url(DeviceResponse)>"] }` (vs SD-JWT's compact `~`-string) — `VerifierEndpoints.HandleDirectPost` dispatches by shape to `MdocPresentationVerifier` under an x509-tenant policy. |
| `POST` | `/credential` (OpenID4VCI issuance, existing) | Issues SD-JWT VC or `mso_mdoc` per the offer's `Format`/`TrustAnchor`. X.509 anchors attach the org cert chain (fail closed 422 if unresolved); mdoc binds the holder proof JWK → MSO device key (EC P-256) via `CredentialEndpoints.BuildEc2CoseKeyFromJwk` and uses the **local** issuer key. |
| `PUT`/`GET` | `/api/v1/trust/trustlists/{id}`, `GET /api/v1/trust/trustlists` | Tenant Service operator trust-list snapshot admin (admin-scoped, `RateLimitPolicies.Strict`) writing to the singleton `OperatorSnapshotTrustListProvider`. Live LOTL deferred. |

### Two trust rails: register/DID-native vs X.509/EUDI-external

Credential trust runs on **two distinct rails**, and which one applies depends on **who the verifier is** — conflating them is the cause of "reach for the X.509 CA expecting external interop" mistakes.

- **Rail 1 — register/DID-native (intra-ecosystem).** Verifiers *inside* Sorcha (the engine `CredentialVerifier`, HAIP verifier, another Sorcha node) anchor trust on the **register** (wallet signatures + validator roster) and on `did:sorcha:org:` resolution. **No X.509, no external CA.** This is the DAD model — the distributed register is the shared trust root. Federated Sorcha nodes are *separate installations* (F143): the trust boundary is the register, not shared JWT/PKI. **This is the correct rail for org→org credential checks (e.g. an insurer that is itself a Sorcha participant).** Issuer key resolution + the three-address DID-anchoring caveats live in the `verifiable-credentials` skill → "Org VC-Issuer Signing & DID Anchoring".

- **Rail 2 — X.509/x5c (EUDI/external bridge).** Verifiers *outside* Sorcha that only speak PKI (EUDI wallets, third parties) need a cert chain terminating at a root **they already trust**. F135's `CredentialIssuanceConfig.TrustAnchor` models this: `x509-tenant` (chain to the per-tenant **self-signed** root from `InternalCaTrustProvider`) vs `x509-lotl` (chain to an external trusted-list anchor).

**Current-state gap (not a plan — just what's true today):** the X.509 rail is **intra-ecosystem only**. `InternalCaTrustProvider` mints a *self-signed* root per tenant, which no external party trusts unless Sorcha's root is planted in their store. Real external interop requires `x509-lotl` (chain to a CA on a recognised List of Trusted Lists) — and **LOTL consumption is deferred** (`x509-tenant` is the only working anchor; the trustlist admin GET returns metadata, not distributable roots). Two further blockers stack on the external bridge: (a) `X509CertificateBuilder` is **P-256-only** (Ed25519 org keys can't be wrapped — the `ASN1 corrupted data` enrol 500), and (b) the org-cert enrol (`POST /api/v1/trust/tenants/{tenantId}/orgs/{wallet}/enrol` → `IssueOrgCertAsync`) is an explicit trust-admin API call invoked **only by HAIP walkthrough setup** — nothing auto-enrols on org creation and there is no admin UI for it. So "the org has an externally-usable X.509 identity" is not a state any normal org reaches today.

### Key files

`src/Common/Sorcha.Mdoc/**` (codec, models, MdocService, MdocIssuer — moved out of Sorcha.Cryptography by F185), `src/Core/Sorcha.Blueprint.Engine/Credentials/**` (TrustEvaluator, resolvers, format handlers, seams, TrustMetrics), `src/Common/Sorcha.ServiceClients.Http/Trust/TrustListProvider.cs`, `src/Services/Sorcha.Haip.Service/Services/{HaipPresentationVerifier,MdocPresentationVerifier,IetfTokenStatusListChecker,HaipTrustAdapters}.cs`, `src/Services/Sorcha.Blueprint.Service/Credentials/{DidIssuerDirectory,DidX5cIssuerKeyResolver}.cs`, `src/Services/Sorcha.Tenant.Service/Endpoints/TrustEndpoints.cs`. Clean-break gate: `scripts/check-trust-clean-break.ps1`. Spec: `specs/135-eudi-credential-format-trust/`.

### Clean-break notes (no shims)

- `CredentialRequirement.AcceptedIssuers` and `HaipPresentationVerifier._trustedRoots`/`AddTrustedRoot` are **removed** (gate-enforced). Seven unrelated presentation-request/verifier DTOs keep their own `AcceptedIssuers` — left untouched.
- mdoc is **ES256/P-256-only at the format layer** and additive — it does not touch Sorcha-native signing or the PQC `Multicodec` fallback (SC-009). Register-anchored mdoc is rejected at issuance (mdoc's issuer key is x5chain-resolved; no DID path in `MdocService`).
- **Deferred follow-ups**: HAIP trustlist-source *consumption* (verifier root distribution — the admin GET returns metadata not roots; x509-tenant is the working mdoc anchor). ~~a real external EUDI PID known-answer vector (vectors are generated end-to-end in tests), and MAC-based device auth~~ — **both closed by Feature 185**: `deviceMac`/`COSE_Mac0` is implemented, and real ISO 18013-5 Annex D reference data now pins the codec (see the Feature 185 section).

---

## Tiered-audience JWT identity model (Feature 136)

Platform-security rework of how Sorcha issues + validates JWT access tokens. **Full reference is the `jwt` skill** ("Tiered audiences + issuer hardening"); this is the catalogue pointer. Spec/plan/tasks: `specs/136-jwt-audience-tiers/`; design: `docs/superpowers/specs/2026-05-21-tiered-audience-identity-model-design.md`.

- **Four installation-namespaced tier audiences** — `{installation}:consumer | platform | service | enrol-session`, derived from the single source of truth `SorchaAudiences` (`Sorcha.ServiceDefaults.Auth`). `InstallationName` (default `sorcha`) drives both the audience namespace and the issuer. **Supersedes the old per-feature audience strings** — notably the F114 citizen JWT `sorcha:citizen-wallet` is now `{installation}:consumer` (a citizen access token is consumer-tier).
- **Validation = authenticate-broad / authorize-narrow**: bearer accepts `SorchaAudiences.All`; tier enforced per-endpoint by `RequireConsumerAudience` / `RequirePlatformAudience` / `RequireService`. The real user↔service boundary was already `token_type`; this promotes it (and the consumer↔platform split) into `aud`.
- **Issuer hardening**: no shared default; `SorchaIssuer.Resolve` → explicit, else `urn:sorcha:{installation}`, else `urn:sorcha:dev-local` (non-prod), else fail-closed at startup (Production/Staging). `SorchaIssuer.AllowsDevLocalFallback(env)` gates the dev-local fallback. Mint (`TokenService`/`EnrolSessionService` via Tenant `JwtConfiguration`) and validate (`AddJwtAuthentication`) resolve through the **same** helpers or tokens self-reject.
- **Per-tier claim sets**: consumer omits `org_id`/roles (inert on platform surfaces); platform = full user shape; service = `client_id`/`service_name`/`scope[]`. Refresh carries `tier`.
- **Tier follows the person, not the UI host**: a citizen is `:consumer` on both `/app` (web) and `/wallet` (PWA); an admin is `:platform` in org context. Login derives the tier from `returnTo` (`/wallet`⇒consumer, `/app`⇒platform) as a *preference* that **downgrades to entitlement** (citizen on `/app`→consumer); an explicit `tier=platform` over-request by a non-entitled user is **refused (403, FR-008)**. `switch-org` re-mints at the new context's tier (FR-016). Endpoint classification: consumer surfaces (`/api/v1/wallet/*`)→`RequireConsumerAudience`; admin/org→`RequirePlatformAudience` composed on the role policy; `/api/internal/*`→extended `RequireService`; genuinely cross-tier `/me/*` stay plain `.RequireAuthorization()`.
- **Status: ALL FIVE user stories DONE** (branch `136-jwt-audience-tiers`). US1 classification, US2 service-isolation (`RequireService` asserts `:service`), US3 issuer hardening + per-deploy `InstallationName`, US4 consumer-tier login, US5 tier-follows-person + over-request gate, `IdentityMetrics` wired. McpServer validation realigned to the installation issuer. Remaining: docs/regression polish + an edge case (2FA-on-`/wallet` tier carry).
- **No migration** (pre-release): coordinated config rollout; existing tokens expire. Dependency contract for downstream Spec B (PWA auth/signup parity).

---

## Cross-node submission round-trip (Feature 137 — Stage 5)

> **F145 supersedes the instance mechanics here.** C5 "mirror submission" and the owner-node mirror advance are retired — instance state is now a single ledger projection (`InstanceProjector`, see the F145 section). The credential-delivery parts of this section (`cnf` binding, holder-key fields, recipient-key precedence) are still live; only the mirror/instance-advance wording is historical.

Closes the citizen→credential loop across a federated node split: a citizen on a local SyncOnly replica submits an application against a register owned by another node (n1); the owner validates/seals it, an analyst approves, and the resulting credential is **bound to the citizen's holder key and encrypted to the citizen's wallet**, then delivered back to the citizen's local wallet. Four of five components (C5 mirror submission, C1 published-store-aware instance creation, C4 fan-out config, C2 event-driven recovery) landed earlier; **C3 (credential delivery) is this surface.**

### `cnf` binding + recipient-key precedence (server)

- **`cnf` binding is no longer skipped.** `IssueCredentialRequest.HolderJwk` (`Wallet.Service/Endpoints/CredentialEndpoints.cs`) flows into `SdJwtService.CreateTokenAsync(holderJwk:)` so the SD-JWT carries `cnf`. Threaded through `IWalletServiceClient.IssueCredentialAsync(holderJwk:)`. Absent → unbound credential (pre-137 behaviour).
- **`CredentialIssuanceConfig.HolderKeySourceField`** (JSON Pointer, default `/holderKeys/holderJwk`) opts a blueprint into bound cross-node delivery. Null → pre-137 behaviour (no `cnf`, register/derivation-only recipient key).
- **FR-012 precedence in `ActionExecutionService` (step 8c, before minting → SC-004 fail-closed):** (1) published participant record (`IRegisterServiceClient.ResolvePublicKeyAsync`) wins; (2) carried `encryptionPublicKey` (from the submitted `holderKeys` field, resolved by `ResolveCarriedHolderKeys`) injected into a local `effectiveExternalKeys` passed to `ResolveRecipientKeysAsync` at step 9d **only when the register lookup misses**; (3) neither → throw `[VAL_RUNTIME_CRED_004]` (no credential issued). A configured `HolderKeySourceField` with no resolvable holder JWK → `[VAL_RUNTIME_CRED_005]` (FR-014). The carried `encryptionPublicKey` is the citizen wallet's **primary public key** (ED25519/NISTP256); the AEAD pipeline derives X25519 from it, so it is byte-identical to the register-resolved recipient key.

### `GET /api/v1/wallet/holder-keys` (Wallet Service, consumer-tier)

`RequireConsumerAudience`. Returns the citizen's public delivery keys for the `sorcha-holder-key` form field: `{ holderJwk (slot 108), encryptionPublicKey (base64 wallet public key), algorithm (ED25519|NISTP256), walletAddress }`. Backed by `IHolderKeyService.GetDeliveryKeysAsync` (combines slot-108 JWK + `Wallet.PublicKey`). 404 when no wallet resolves (indistinguishable). Contract: `specs/137-cross-node-submission/contracts/holder-keys-endpoint.openapi.yaml`. Public material only — never a private key.

### `sorcha-holder-key` form field (client)

`ControlTypes.HolderKey` (`Sorcha.Blueprint.Models/Control.cs`) — dispatched by `FormSchemaService.InferControlFromSchema` when an object field carries `format: "sorcha-holder-key"` (checked before the object-recursion branch). Rendered read-only by `HolderKeyRenderer.razor` (`Sorcha.UI.Components.User`), which calls `IHolderKeyClient.GetHolderKeysAsync` and writes `{Scope}/holderJwk`, `{Scope}/encryptionPublicKey`, `{Scope}/algorithm` via `FormContext.SetValue` (sibling fan-out, like `PostcodeLookupRenderer`). `IHolderKeyClient` is registered auth-wrapped in both the web SPA (`Sorcha.UI.Core` `ServiceCollectionExtensions`) and the PWA (`Sorcha.Wallet.Pwa` DI). Contract: `specs/137-cross-node-submission/contracts/sorcha-holder-key-field.md`.

### Validation note (`x-*` strip on both paths)

`SchemaValidator` (`Sorcha.Blueprint.Engine`) now strips `x-*` extension keywords before evaluation (mirroring the Validator Service's `ValidationEngine.StripCustomExtensionKeywords`), so `x-holder-key` (and any other `x-*` UI hint) is tolerated on the blueprint-service action-data path as well as the validator path. Unknown `format` values (`sorcha-holder-key`) validate as pass.

### PWA submission

`Sorcha.Wallet.Pwa/Pages/ApplicationInstance.razor` renders `SorchaFormRenderer` for the instance's current action (loaded via `IApplicationActionClient.LoadFormAsync` → `GET /api/instances/{id}` + blueprint) and submits via `FormPayloadBuilder.BuildNested` → `POST /api/instances/{id}/actions/{actionId}/execute` (server-signed via the bearer token; the citizen wallet is server-custodied). The F125 `StubApplicationSubmissionService` / `IUserSigner` seam is retained for its tests.

### Worked example

`walkthroughs/AssuredIdentity/blueprints/assured-identity.json` — action 1 carries a `holderKeys` (`format: sorcha-holder-key`) field; action 2's `credentialIssuanceConfig.holderKeySourceField` points at `/holderKeys/holderJwk`. `run-phase1-identity.ps1` fetches `/api/v1/wallet/holder-keys` and supplies `holderKeys` in the action-1 payload. The live n1↔local cross-node run is **Tier-2** (the genesis-key machine); the C3 server is unit + single-node-integration covered (SC-005 Tier-1). Spec: `specs/137-cross-node-submission/`. Design: `docs/superpowers/specs/2026-05-23-cross-node-submission-design.md`.

## F142 — Blueprint Design Lifecycle Overhaul

The designer workspace is rail-driven: **Describe -> Understand -> Rehearse -> Go live**, with the Go-live stage gated by a server-side `RehearsalPass` keyed by the **executable-definition hash**. The UI lock mirrors the server state (`LifecycleState`) — never the other way round.

### Executable-definition hash (`ExecutableDefinitionHasher` + `FormKeywordClassifier`)

`Sorcha.Blueprint.Engine.Implementation.ExecutableDefinitionHasher` canonicalises a blueprint into the bytes that actually drive execution and SHA-256s them. `FormKeywordClassifier` partitions JSON-Schema keywords into **presentational** (`title`, `description`, layout `x-*`, ordering hints) versus **behavioural** (`type`, `enum`, `required`, `pattern`, validation `x-*`, anything that changes which payloads validate). Only behavioural keywords contribute to the hash, so chrome edits (relabelling a field) don't re-lock the lifecycle, but a `required` toggle does. Unknown `x-*` extensions are treated as **behavioural** (fail-safe). The hash is the join key for `RehearsalPass`.

### Two server tables (both via F113 `IStorageRegistrationLog`)

- **`RehearsalPass`** — `{ Id, BlueprintId, ExecDefHash, RehearsedAt, RehearsedByPlatformUserId, SandboxRegisterId }`. Recorded exactly once per terminal-success rehearsal (in `RehearsalOrchestrationService.SubmitStepAsync` when `response.IsComplete`). One row per `(BlueprintId, ExecDefHash)` clears the soft gate.
- **`PublishOverride`** — `{ Id, BlueprintId, Version, RegisterId, ExecDefHash, OverriddenByPlatformUserId, OverriddenAt, Reason? }`. Append-only audit. Written by the publish endpoint AFTER a successful publish on the override path.

Both stores have a Postgres-backed `EfCore*Store` and an in-memory fallback registered via `RegisterPersistent` / `RegisterInMemory`. They are NOT audited (rehearsal/audit data, not ledger state), so the in-memory fallback is acceptable in non-production.

### Publish gate (`IPublishGate` / `PublishGate`)

The gate runs BEFORE any publish — `EvaluateAsync` returns one of `Forbidden` / `RehearsalRequired` / `Proceed` / `ProceedWithOverride`. The endpoint (`POST /api/blueprints/{id}/publish`) acts on the outcome and writes the override audit only AFTER `IPublishService.PublishAsync` returns success.

- **Governance HARD gate (FR-027 / D5).** `IRegisterServiceClient.GetGovernanceRosterAsync(registerId)` -> look for a member holding Owner / Admin / Designer whose subject matches the caller. The roster subject is a wallet-DID (`did:sorcha:w:{walletId}`), so the **wallet address is the primary match key** (substring within the DID), with `org_id` as the fallback (covers callers without a wallet claim — some service-principal contexts). No roster, no match, no wallet+no org -> **403 Forbidden, no record written**.
- **Rehearsal SOFT gate (FR-032 / D4).** Look up `IRehearsalPassStore.GetLatestAsync(blueprintId, execDefHash)`. Hit -> `Proceed`. Miss + no override -> **409 `REHEARSAL_REQUIRED`** with the computed `execDefHash` in the response (the UI shows the rehearsal CTA). Miss + `override.confirm=true` -> `ProceedWithOverride`. The caller has already passed the HARD governance check, so the same Owner/Admin/Designer authority that lets them publish lets them override.

### Sandbox register (`SandboxRegisterProvider`)

One per-org devMode register, lazily provisioned via the **two-phase initiate/finalize ceremony** (NOT a bare `POST /api/registers` — there is no such endpoint). The provider:

1. Mints (and caches) the org's `sandbox-owner-{orgId}` wallet.
2. `POST /api/registers/initiate` with `DevMode=true`, `Advertise=false`, `Metadata["sandbox"]="true"`.
3. Signs each attestation hash pre-hashed against the owner wallet (mirrors the CLI's `RegisterCreateCommand`).
4. `POST /api/registers/finalize` to seal the genesis transaction.

Singleton (per-org cache is process-wide transient state) — scoped clients are resolved per-operation through `IServiceScopeFactory`. The `Metadata["sandbox"]="true"` marker drives the computed `Register.Sandbox` flag (T009) which the Go-live picker excludes.

### Ephemeral per-role wallets + sandbox blueprint clone (VAL_BP_002 workaround)

`RehearsalOrchestrationService.StartFullAsync` mints one ephemeral ED25519 wallet per participant role on the blueprint. **Critical**: the validator resolves sender authorisation from the **published blueprint on the register**, not from BP-service instance state. So a non-starting participant (e.g. a reviewer) with no resolvable wallet on the published blueprint trips VAL_BP_002 even if the BP-service has pre-bound a wallet on the instance.

The orchestration publishes a sandbox-specific **clone** of the blueprint (`BuildSandboxBlueprint`) with each NON-starting participant's ephemeral wallet baked into its `WalletAddress`. Starting-action senders are left null so open-participant late-binding still fires (VAL_BP_010 forbids a baked wallet there). The `RehearsalPass` keeps the **original** exec-def hash — the sandbox clone's wallet differences never reach the Go-live gate.

### Amend loop (`POST /api/blueprints/from-published` -> `LifecycleState.AmendContext`)

`BlueprintFromPublishedEndpoint` clones a published version back to a fresh draft and stamps three lineage keys onto the cloned `Blueprint.Metadata`:

- `x-source-register` — the register the source was published to.
- `x-source-blueprint-id` — the source blueprint id.
- `x-source-version` — the source version number.

`DesignerBlueprint.razor.cs` reads those keys on load and rehydrates `LifecycleState.AmendContext` — the rail surfaces "Amending vN" plus an "Amend" entry on the services list (T057).

### Chat tools (3 new) + directed-build starter

The chat orchestration registers three layout/UX tools on top of the existing schema tools:

- `set_form_layout` — set/replace a layout JSON on an action's form (`x-layout` block).
- `set_field_autofill` — set an autofill hint on a single field.
- `set_review_page` — set the workflow's review-page (terminal summary) config.

A directed-build conversation starts when the AI emits a sentinel of the shape `__directed-start:<id>` where `<id>` is one of the three starter ids: **`grant`**, **`permit`**, **`certify-then-apply`**. The orchestration also recognises plain-language openers ("I want to issue a grant"; "I want a permit application"; "Citizens certify, then apply") — `DirectedBuildStarter` matches the phrase, replays as if the sentinel was emitted, and the rail's Describe stage drives the build.

### UI surface

- `LifecycleRail.razor` (Core/Components/Designer) — the four-stage progress + lock + "amending vN" tag.
- `JourneyView.razor` — the journey of actions/participants the rehearsal walks (Describe + Understand surfaces).
- `RehearsalStepper` — the Rehearse stage runner; on terminal success calls `RecordRehearsalPassed` which flips the rail's Go-live lock.
- `GoLivePanel` — the Go-live picker (sandbox-excluded), system-info card, review, permanence notice, and publish action. Publish outcomes: 200 `overridden:true/false`, 403 with reason, 409 `REHEARSAL_REQUIRED` (returns to Rehearse) or override confirmation.

Designer-only components live in **`Sorcha.UI.Core`** (PWA-forbidden — Core never reaches the wallet PWA bundle). Form authoring stays in **`Sorcha.UI.Components.User`** alongside `SorchaFormRenderer`. The canonical designer route is `/designer/blueprint?stage={describe|understand|rehearse|golive}`.

### Observability

`BlueprintDesignerMetrics` (`Sorcha.Blueprint.Designer` meter, already on the ServiceDefaults export allowlist) records:

- `rehearsal_run_total{mode,outcome}` — counter, mode in {`dryRun`, `full`}, outcome in {`InProgress`, `Passed`, `Abandoned`, `Failed`}.
- `rehearsal_duration_seconds{outcome}` — histogram (s), start to terminal write.
- `publish_override_total{register_id,reason_provided}` — counter, audit-row writes.
- `sandbox_provision_total{outcome,org_id}` — counter, outcome in {`Created`, `Reused`, `Failed`}.

Plus `LogInformation` audit lines on publish overrides, sandbox provisioning, and sandbox-rehearsal discard.

---

## Peer NAT Traversal — Reverse-Stream Rendezvous (Feature 143)

Makes a register **owner** node behind NAT reachable by public subscribers, by folding a reverse-stream rendezvous capability into the **peer service** (the standalone `Sorcha.PeerRouter` is **retired**). Verified live across a real NAT boundary (tiny↔n1 over Caddy:50051).

### The model

- **Rendezvous is a capability, not a node.** A peer with a reachable address (`NetworkAddress.ExternalAddress` set, or explicit `PeerService:RelayRendezvousEnabled=true` → `PeerServiceConfiguration.IsRendezvousCapable`) accepts inbound reverse streams.
- **The NAT'd node always dials out** and holds a persistent bidirectional `PeerCommunication.Stream` to each anchor; the rendezvous reuses that stream to broker requests back. NAT only blocks the *initiating* direction.
- **Connection-direction invariant:** the subscriber initiates every cross-node connection (submit fan-out, docket pull, live subscribe). So the **owner must be inbound-reachable** — unless it dials out and is reached over its reverse stream (this feature).

### Server side (rendezvous) — `PeerCommunicationServiceImpl.Stream`

Accepts a reverse stream, registers it in `ReverseStreamManager` (keyed by the NAT'd peer id) on the first message (`InvalidArgument` if no `sender_peer_id`), pumps inbound messages through the existing `RelayMessageHandler`, tears down on disconnect. Gated on `IsRendezvousCapable`. `ReverseStreamManager.DispatchAsync(peerId, msg)` pushes a brokered request to a held stream (fail-fast `Unavailable` if none).

### Brokered flows (reuse `RelayMessageHandler` correlation)

| Flow | Message types | Owner-side handler |
|---|---|---|
| Sync (pull) | `REGISTER_SYNC_REQUEST/RESPONSE`, `TRANSACTION_DATA_REQUEST/RESPONSE` | reads cache / Register Service |
| Notify | `TRANSACTION_NOTIFICATION` | triggers a sync |
| **Submit-for-sealing** | `SUBMIT_TRANSACTION_REQUEST/RESPONSE` (12/13, **F143**) | `RelayMessageHandler.HandleSubmitTransactionRequestAsync` → `IValidatorServiceClient.SubmitTransactionAsync` → ack over reverse stream |

`TransactionDistributionService.ForwardSubmissionAsync`: when a register's owner has no direct channel but its reverse stream is held, brokers the submission via `RelayCommunicationService.SendAndWaitAsync<SubmitTransactionRelayResponse>` instead of returning `LocallyOwned:true`.

### Spoke side (multi-anchor) — `RelayCommunicationService`

`EstablishReverseStreamAsync` maintains one reverse stream **per configured seed** concurrently (`Anchor` per seed; independent reconnect/backoff/keepalive; per-anchor write lock — gRPC streams aren't concurrent-write-safe). `SendViaRelayAsync` order: (1) rendezvous self-anchor (`ReverseStreamManager`), (2) outbound anchors via `OrderAnchorsForSend` — target-match → lowest `AverageLatencyMs` → recency, with failover, (3) unary fallback.

### Observability (`Sorcha.Peer.Service` meter)

`peer.reverse_streams.active` (gauge), `peer.relay.forward.duration{flow=submit|sync}`, `peer.path.selection{path=self|remote}`, `peer.anchor.failover`, `peer.anchor.reconnect`.

### Scope / deferred

v1 covers a NAT'd **owner** reached via its anchors (the demo: tiny=NAT'd owner dials n1=public subscriber; advert flows the proven `tiny→n1` heartbeat direction). **Deferred:** anchor-set *gossip* + multi-hop mesh routing (a subscriber relaying through a third-party anchor it doesn't hold — needs hub→hub forwarding); relay-payload re-encryption; rendezvous authz/quotas. **Trust boundary is the register** (wallet signatures + roster), not JWT — federated nodes are **separate installations** and MUST NOT share JWT signing keys.

Spec: `specs/143-peer-nat-traversal/`. Design: `docs/superpowers/specs/2026-05-30-peer-nat-traversal-design.md`.

---

## Ledger-Derived Workflow Instances (Feature 145) — US1 CUTOVER DONE (pending cross-node live validation)

Makes a workflow instance a **deterministic projection of the sealed register** — one shared state machine on every node, no origin/mirror duplication, one async submission path, routing decisions carried on the transaction (in the clear) and validated at seal under a pluggable attestation. Replaces the imperative per-node instance mutation + the reconstructed cross-node mirror + the dual sync/async submission split. **Status: US1 cutover landed — the `InstanceProjector` is the single instance writer on every node; the inline + encrypted submit paths return 202 and no longer advance instance state; the mirror (`InstanceMirrorReconstructor` / `IsReadOnlyMirror` / `Create|UpdateMirrorAsync`) is removed; the 3 mirror clean-break patterns are CI-enforced. **Live-validated 2026-06-01: local single-node AssuredIdentity PASS, AND cross-node tiny+n1 PASS — SC-001 (owner + subscriber both materialize the SAME instance from the sealed/replicated ledger via the projector, no mirror) and SC-002 (cross-node credential delivery).** PR #891 (merged, `0fb1c0e5`). **US3 (routing-decision validation + governance) DONE** — `VAL_ROUTING_001/002` + `routingAttestation` register policy + typed-decision seal carry (the singular `NextActionId` seal-write is gone; producer string + projector fallback linger until US5). US2 (ReactionDispatcher), US4 (rebuild parity), and US5 (legacy `NextActionId` removed end-to-end; `RoutingDecision` is the sole routing carrier) are also DONE + merged. **T017 (roster-based sealer selection) DONE** — see subsection below. Remaining tail: the residual US5 topology-heuristic/dual-path *removal* sweep (T034), US6 (presentation onto the projection), and the T040 clean-break enforcement flip.**

> **SUPERSEDED MODEL NOTE (read when touching F106 / F137 below):** the "owner vs read-only mirror" instance model (F106 `InstanceMirrorReconstructor` + `IsReadOnlyMirror` + the F137 C5 owner-node "mirror submission/advance") is **retired by F145**. There is no mirror row and no dual submission path: the instance is a single ledger projection written only by the `InstanceProjector` on every node. F106/F137 prose below is kept for credential-delivery history (the `cnf`-binding / holder-key parts are still live); treat any "mirror" / `IsReadOnlyMirror` / "owner advances the mirror" wording there as historical.

### RoutingDecision — carried, attested routing fact (DONE)

`RoutingDecision` rides on the action transaction's **clear** metadata (`TransactionMetaData.RoutingDecision`, `src/Common/Sorcha.Register.Models/Transactions/`), replacing the singular `TransactionMetaData.NextActionId` hint (now marked legacy, removed in T024). It carries the **full** next-action set (`ActionRef[]` — preserves parallel branches) plus a pluggable `Attestation`:

```
RoutingDecision { completedActionId:int, nextActions:ActionRef[], attestation:Attestation }
ActionRef       { actionId:int, branchKey?:string }
Attestation     { kind:AttestationKind, signature?:string }   // v1 = SenderSigned
AttestationKind = SenderSigned | ValidatorReEvaluated | Proof  // v2/v3 reserved
```

- Serialized canonically via `RegisterSerializationOptions.Canonical` (camelCase, the #881 relay-stability lesson). `RoutingDecision.ComputeSignableBytes()` returns the attestation-free canonical bytes the sender signs (the signature can't sign over itself).
- **Producer** (`ActionExecutionService.cs` step 10d): builds the decision from the routing result, signs `ComputeSignableBytes()` with the sender wallet via `IWalletServiceClient.SignTransactionAsync`, base64s into `Attestation.Signature`, writes canonical JSON to `transaction.Metadata["routingDecision"]`.
- **Validator (US3, DONE)** — `ValidationEngine.ValidateRoutingDecisionAsync` (step 4b-iii, behind `EnableRoutingValidation`) enforces, when a decision is carried on a forward-routing action tx: **`VAL_ROUTING_001`** — every `nextActions[i].actionId` is a structural successor of `completedActionId` in the published route graph (`Action.Routes.NextActionIds` ∪ `RejectionConfig.TargetActionId`; terminal `[]` valid; `completedActionId` must equal the tx's action). **`VAL_ROUTING_002`** — governance strength gate (refuses if the register demands a stronger strength than v1 supports), attestation-kind gate (only `SenderSigned`), and signature verify over `SHA256(ComputeSignableBytes())` against the tx signer via `ICryptoModule.VerifyAsync` (mirrors `VerifySignaturesAsync`). Genesis/control/participant/rejection/intra-action-lifecycle txs and txs carrying no decision are skipped.
- **Seal carry (US3 T024, DONE)** — `DocketBuildTriggerService.ResolveRoutingDecision` projects the validated decision onto the typed sealed `TransactionMetaData.RoutingDecision`; the singular `NextActionId` seal-write is removed. `InboundTransactionRouter`'s wallet-notification hint now derives from `RoutingDecision.NextActions[0]`. **US5 is DONE** — the producer's `nextActionId` string hint and the projector's legacy `NextActionId` fallback are both removed; `TransactionMetaData` carries only the typed `RoutingDecision`, and `(MetaData|Metadata|metadata).NextActionId` is CI-enforced absent by the clean-break gate (verified: zero matches in `Sorcha.Register.Models/Transactions`).
- The projection consumes `nextActions` regardless of attestation kind; only validation branches on kind. v2/v3 are refused ("unsupported attestation strength") until implemented; required strength is the register-governance field `RegisterControlRecord.RoutingAttestation` (typed `AttestationKind?`, sibling of `CryptoPolicy`, default `sender-signed`), read by the validator via `IGovernanceRosterService.GetCurrentRosterAsync().ControlRecord.RoutingAttestation`.

### Deterministic instance identity (DONE)

`InstanceIdentity.Derive(registerId, blueprintId, startingActionTxHash)` (`src/Services/Sorcha.Blueprint.Service/Services/Implementation/InstanceIdentity.cs`) = lowercase-hex SHA-256 over the three UTF-8 fields separated by `0x1F` unit-separator bytes (anti-collision on field boundaries). Node-independent: every node derives the same id for the same workflow. "Start application" becomes a local draft (no ledger write); the instance is born when its starting action seals. (Wiring `POST /instances` to a draft + returning the derived id on submit is US1 T018.)

### The projection fold (DONE)

`InstanceProjection` (`src/Services/Sorcha.Blueprint.Service/Services/Implementation/InstanceProjection.cs`) is the pure, deterministic heart:
- `ProjectedTransaction` record = the facts a sealed action contributes (`TxId`, `PreviousTransactionId`, `CompletedActionId`, `NextActionIds`, `ParticipantBindings`, `IsRejection`).
- `Project(...)` — batch rebuild from a transaction set, folded in **chain order** (predecessor links), **order-independent**, dedups by tx-id. This is what US4's `InstanceRebuildService.RebuildAsync` calls (FR-003 parity).
- `Apply(instance, tx)` — online incremental fold, **idempotent** on the `Instance.LastAppliedTxId` watermark (re-observing a folded tx is a no-op, FR-004).
- Advances `CurrentActionIds` from the full `nextActions` set (parallel branches preserved, sorted for canonical cross-node equality); merges participant-id-keyed bindings; derives terminal `Completed` (no current actions) / `Rejected` state. Needs only the validated decision — never decrypts payload (FR-010).

### The projector — single instance writer on every node (DONE)

`InstanceProjector : BackgroundService` (`Services/Implementation/InstanceProjector.cs`, registered in `Program.cs`) wraps `Apply`, subscribes `docket:confirmed` (`RegisterEventChannels.DocketConfirmed`) on **every** node (it generalizes and replaces the deleted owner-only `InstanceMirrorReconstructor`), resolves each `ProjectedTransaction` from the register transaction + carried `RoutingDecision` (typed field, else the `routingDecision` tracking-metadata JSON, else the legacy `NextActionId` fallback), and folds it. Idempotent via `LastAppliedTxId`. **The projector is pure (state only)** — post-fold it delegates side effects to the `ReactionDispatcher`.

### Reactions — exactly-once, role-gated side effects (US2, DONE — notifications/inbox only)

`ReactionDispatcher` (+ `IReactionDispatcher`, `Services/Implementation/ReactionDispatcher.cs`) owns the `action-available` / `workflow-completed` notification + durable-inbox writes (via the existing `INotificationService` → SignalR + `BlueprintInboxWriter`). Each reaction is **entitlement-gated** — `IWalletServiceClient.GetWalletAsync(wallet)` returns null when the wallet isn't hosted on this node, so only the hosting node fires (cross-node dedup) — and **idempotent** via `IAtomicDistributedCache.TrySetIfAbsentAsync` (new SET-NX primitive on the audited cache) on `react:{sealedTxId}:{kind}:{wallet}` (replay/restart/rebuild dedup). Metrics `reaction_dispatched_total` / `reaction_idempotent_skip_total` / `reaction_entitlement_skip_total` (tagged `kind`) on the `Sorcha.Blueprint.Reactions` meter. **Invoked in-process by the projector post-fold** (not a separate `BackgroundService`) — race-free (gets the folded instance), a deliberate simplification of the spec's BackgroundService shape. This removed the projector's old per-node notification firing (which duplicated cross-node and wasn't idempotent).

> **Credential mint is NOT a reaction — it stays INLINE by design** (Stuart, 2026-06-01). Minted during submit + sealed into the recipient-addressed encrypted disclosure group inside the action tx, so it already lives on the immutable, disclosure-controlled, replicated ledger (DAD) and is already exactly-once. Spec task T028 (move mint to a `CredentialMint` reaction) is **dropped**; `ActionExecutionService` issuance is untouched, and credential *delivery* rides the existing on-ledger disclosure-group replication, not a reaction.

### Rebuild + parity (US4, DONE)

The materialized instance row is a cache; `InstanceRebuildService` (+ `IInstanceRebuildService`) reconstructs it purely from the register's sealed txs (`GetTransactionsByInstanceIdAsync` → `InstanceProjection.Project`). `RebuildAsync` is bit-for-bit identical to the materialized view because both the online projector and the rebuild share the **single `InstanceProjectionResolver`** (the projector's private tx→`ProjectedTransaction` resolution — routing-decision read, participant-binding resolution, tenant id — was extracted into it). `CheckParityAsync` reports field-level divergence (state / currentActionIds / bindings); `RebuildAndPersistAsync` is operator repair. Internal endpoints (`RequireService`): `GET /api/internal/instances/{registerId}/{instanceId}/parity` + `POST .../rebuild`. The T031 parity test is the CI self-check.

**The single async submit (T016):** `ActionExecutionService.ExecuteAsync` always returns **202** (`IsAsync`, empty `NextActions`, `IsComplete=false`) — no owner/subscriber response branch, no imperative advance. A bounded seal-wait is kept on the locally-owned path purely for chain ordering (the next submit's `StateReconstructionService` reads the prior tx from the sealed ledger as `previousTransactionId`; DevMode reads plaintext, encrypted decrypts). `instanceReference` is generated once **pre-submit** (the only point with the plaintext for both DevMode and encrypted registers, written before the tx can seal so it never races the projector). `EncryptionBackgroundService` likewise submits but no longer advances or notifies. Presentation-completion advancement is being moved onto the projection too (US6 — see the subsection below); the imperative `CompleteAfterPresentationAsync` is retired in favour of a `RoutingDecision` carried on the successful outcome tx.

### Roster-based sealer selection (T017, DONE)

The Peer service forwards a fully-signed submission to the node(s) that can seal it. **Who seals is decided by the register's control-record roster (Feature 108), not by a seeds/topology heuristic.** Because the Peer service is a **separate process that does not own the ledger** (no `IReadOnlyRegisterRepository`/`ILocalIdentityProvider` in its DI), it cannot host `IRegisterLocalRelationshipService` directly — it consults the co-located Register service via the existing `IRegisterServiceClient.GetLocalRelationshipAsync` (`GET /api/registers/{id}/local-relationship`, server-side roster-cached, recomputed on control-tx seal).

- `TransactionDistributionService.ForwardSubmissionAsync` (`src/Services/Sorcha.Peer.Service/Distribution/`) resolves the relationship first: if this node `IsOwner`/`IsValidator` for the register, its co-located validator seals locally → returns `LocallyOwned: true`, **no fan-out**. A subscriber/unknown relationship (or a failed lookup) falls through to the existing transport — direct channels → reverse-stream relay to a NAT'd owner (F143) → configured seed nodes (F137) — so behaviour is never worse than the prior heuristic.
- The scoped `IRegisterServiceClient` is resolved from the singleton service via an optional `IServiceScopeFactory` (the F143 optional-ctor-param pattern; production DI injects it, unit tests pass null ⇒ heuristic fallback path).
- **F108 follow-up #1 also closed**: `TransactionDistributionGrpcService.SubmitTransaction` now sets the `receiver_is_validator` response flag honestly from the same relationship lookup (previously hard-coded `false`) — the submitting peer can distinguish "forwarded to a node whose validator will seal" from "forwarded to a relay/subscriber". Best-effort: an absent/failed lookup leaves it `false` (conservative).
- The roster *decision* is unit-tested (`TransactionDistributionServiceTests`); P2P fan-out itself is not unit-provable and is validated via the cross-node walkthrough. **T034 is DONE** — `TransactionDistributionService.ForwardSubmissionAsync` has no `LocallyOwned` branch and no seed/topology-heuristic fallback (verified: pure carrier-aware fan-out — direct channel → reverse-stream relay to a NAT'd owner → no-op when no carrier is known). The only residual clean-break flip is **T040** (`ApplyInstanceStateChanges` enforcement), gated behind the US6 imperative-advance deletion (below).

### Presentation lifecycle on the projection (US6 — built, LIVE-GATED)

Moves presentation-driven advancement off the imperative path onto the projector. **Correcting a stale assumption:** presentation-lifecycle txs (`PresentationInitiated/Outcome/Abandoned`) DO get a numeric sealed `TransactionMetaData.ActionId` (via `DocketBuildTriggerService`), so the projector folds them — and with no `RoutingDecision` they folded as an empty-terminal, retiring the still-current gated action (a latent bug for *non-terminal* presentation actions, masked because tested flows have the presentation terminal).

- **Increment 1 (merged):** `InstanceProjectionResolver` skips a presentation-lifecycle tx (`TransactionType.IsPresentationLifecycle()`) that carries no `RoutingDecision` — the gated action stays current (arrived via the previous action's routing fold) until a successful outcome routes it onward.
- **Increments 2-3 (built, live-gated):** a SUCCESSFUL `PresentationOutcome` carries a sender-signed `RoutingDecision`. `IPresentationRoutingDecisionBuilder` (implemented by `ActionExecutionService`) computes+signs it from the real blueprint/instance/draft payload (mirrors the old `CompleteAfterPresentationAsync` routing + step-10d sign; null on missing instance / non-current action). `PresentationLifecycleService.HandleOutcomeAsync` attaches it to `built.Metadata["routingDecision"]` before `ToTransactionSubmission` (inline + F119-deferred paths). The projector folds the sealed outcome and advances; `ReactionDispatcher` notifies post-fold. The imperative advance (the two `EnqueueAdvancementAsync` calls + the legacy `Task.Run → CompleteAfterPresentationAsync`) is no longer invoked. **F119 outcome-tx SUBMISSION deferral (`EnqueueSubmissionAsync`) + the `VAL_BP_003` carve-out + idempotency sentinels are retained.** The now-dead imperative methods + the F119 **advancement**-queue API (`EnqueueAdvancementAsync`/`TryDrainAdvancementAsync` — currently **zero callers**, hence the whole `CompleteAfterPresentationAsync`→`ApplyInstanceStateChanges` chain is unreachable) are KEPT until live validation confirms the projector advance (removal-follows-proven-replacement; T040 enforces `ApplyInstanceStateChanges` then). Note this is distinct from the F119 **submission**-queue API (`EnqueueSubmissionAsync`/`TryDrainSubmissionAsync`), which is **live and load-bearing** for predecessor-seal ordering — do not delete that one.
- **Live gate (must pass before merge):** re-run the F111/F127 presentation walkthroughs; confirm presentation success advances on every node and the sealed success outcome shows `RoutingDecision=PRESENT` in Mongo (the dormant-routing trap).

Contract: `specs/145-ledger-derived-instances/contracts/submission-response.md`. Spec: `specs/145-ledger-derived-instances/` (+ `US6-IMPLEMENTATION-PLAN.md`). Design: `docs/superpowers/specs/2026-05-31-ledger-derived-instances-design.md`. CI clean-break gate (3 mirror patterns enforced): `scripts/check-ledger-derived-clean-break.ps1` + `.github/workflows/ledger-derived-clean-break-gate.yml`.

---

## Feature 150 — Unified Account Security Surface

The successor to Feature 116. Consolidates account-security management into one discoverable **Security** home, adds an **assurance-aware floor rule**, finishes the stubbed step-up proofs, and (in follow-up phases) adds Email/SMS OTP second factors with full web ⇄ PWA parity. Design: `docs/superpowers/specs/2026-06-10-unified-account-security-design.md`; spec/contracts: `specs/150-account-security/`.

### Assurance model (server-authoritative, computed never stored)

`AssurancePolicy` (`Sorcha.Tenant.Service/Services/AssurancePolicy.cs`) is the single source of truth:
- `AuthAssuranceTier` (`Basic=1 < Strong=2 < Strongest=3`) — ordinal so the floor compares with `>=`.
- `TierOfMethod(AuthMethodKind)` — Passkey=Strongest, Password=Strong, Social=Basic (badge).
- `TierOfProof(ChallengeMethod)` — Passkey=Strongest, Totp/ReOAuth=Strong, **Password=Basic** (T061 resolved 2026-06-11 — a password is a phishable knowledge factor). Its own ops (`ChangePassword`/`RemovePassword`) are Basic-gated by the floor's own "required = tier-being-weakened" rule (no dead-end for password-only users); the load-bearing guarantee holds — a Basic proof can never disable TOTP (Strong) or remove a passkey (Strongest).
- `RequiredProofTier(ScopedOperation, AuthMethodKind? target)` — the floor. `RemoveAuthMethod` is ambiguous (passkey-revoke vs social-unlink) so the **target is required**; a **null target fails safe to Strongest**.

### Shared wire contracts (DRIFT-004)

`AuthAssuranceTier`, `ChallengeMethod`, `ScopedOperation`, `AuthMethodKind`, the four `AuthMethods*` records, the four `Challenge*Request`/`Response` records, and the three `Totp*` records live **once**, in the zero-dependency leaf **`Sorcha.Tenant.Models.Auth`** — referenced by the Tenant Service, the web UI and the PWA alike. They were previously hand-mirrored UI↔Tenant and had already drifted: the UI's `ChallengeMethod` omitted `EmailOtp`/`SmsOtp` (so the ladder selecting either would have thrown on an unknown enum name and killed the step-up dialog outright) and its `ScopedOperation` omitted `LinkSocial`. **Do not re-declare any of these client-side** — guarded by `AuthMethodsWireContractTests` in `tests/Sorcha.UI.ContractTests`.

Two deliberate carve-outs:
- **`AuthMethodsPasskey.Status` is a `string`**, not `CredentialStatus` — that enum is EF-mapped with ~40 service-internal usages, so hoisting it into the leaf would drag persistence in. `AuthMethodService.PasskeyStatusWireValue` kebab-cases it so the wire value is byte-identical to before; the test pins the member names.
- **`ChallengeVerifyResult`/`ChallengeVerifyError` stay UI-local** — they fold the response body together with the HTTP status into one value the dialog switches on, so they mirror no server type.

**Converter precedence (counter-intuitive, load-bearing):** System.Text.Json ranks the `options.Converters` collection **above** a type-level `[JsonConverter]` attribute. These enums carry `[JsonConverter(typeof(JsonStringEnumConverter))]` (default PascalCase names) but `SorchaJson` registers a kebab-case `JsonStringEnumConverter` in `Converters` — so **kebab wins**: `AuthAssuranceTier.Strong` goes on the wire as `"strong"`, `ScopedOperation.RemoveAuthMethod` as `"remove-auth-method"`. Reading is case-insensitive, so PascalCase input still binds. Pinned by `ClientAndServer_RoundTripTheAggregateThroughTheServersOwnOptions`.

### The ladder-floor rule (the security spine)

> A step-up proof authorises a destructive/downgrade op on a method **iff `proofTier >= RequiredProofTier(op, target)`** AND the last-sign-in-method floor (`Total >= 1`) holds. Strict — no lower-tier fallback. A Basic (email/SMS) proof can therefore **never** strip a passkey, disable TOTP, or change the password.

Enforced in `AuthChallengeService`: **initiate** offers only floor-eligible proof methods (filter-then-ladder — TOTP is still preferred for ChangePassword, but only a Passkey can authorise a passkey removal); **verify** re-checks and returns `403 proof_tier_insufficient` (`ChallengeVerificationOutcome.ProofTierInsufficient`). The `TargetMethodKind` is threaded through `ChallengeInitiate/VerifyRequest` → service → endpoints, and the UI client (`AuthMethodsClientService`) sends it from the dialog (`AuthChallengeDialog` `Target` param: PasskeysSection→Passkey, SocialLinksSection→Social). The aggregate read `GET /api/me/auth-methods` surfaces per-row `AssuranceTier` + `RequiredProofTier` + `CanRemove` (UI reflects, never decides).

### Always-notify (paired defense, FR-009/FR-011)

`ISecurityChangeNotifier` (`SecurityChangeNotifier.cs`) fires on **every** security mutation — F118 inbox entry (`ITenantSecurityInboxWriter.WriteSecurityChangeAsync`, DetailHref `/security`) **and** a Sorcha-branded email (`ITransactionalEmailService.SendSecurityChangeAsync` + `security-change` template). Both legs best-effort (try/log/swallow) — a notify failure never rolls back the operation. Wired into PasswordManagementService, SocialLinkService, PasskeyService (add/revoke/rename), and TotpService (enable/disable; maps the org-scoped UserIdentity id → account-wide PlatformUser id the notifier needs).

### UI surface (shared, built once)

`Sorcha.UI.Components.User/Components/Security/` (RootNamespace `Sorcha.UI.Core`): `SecurityHome` (three job-based groups — *How you sign in* / *Two-factor authentication* / *Recovery*), `AssuranceBadge`, `TwoFactorSection` (TOTP; US2 adds Email, US3 SMS), and the relocated `PasswordSection`/`SocialLinksSection`/`PasskeysSection`/`AuthChallengeDialog` + `PasskeyInteropService`. Mounted by the web host at `Pages/Security.razor` (`/app/security`); the avatar menu (`UserProfileMenu.razor`) has a **Security** item (`data-testid=user-menu-security`) between *My Profile* and *My Devices*. The Settings *Accounts* + *Security* tabs are retired; `/settings?tab=accounts|security` redirect to `/security`.

### Status / phasing (independently shippable)

- **US1 (shipped):** consolidation + floor rule + finished Passkey/Re-OAuth proofs + always-notify. Re-OAuth proof rung's in-browser redirect-return UX is deferred (the API path + Passkey/TOTP/Password rungs cover the security-critical removals).
- **US2 (Email OTP), US3 (SMS OTP, config-gated via `ISmsSender`), US4 (PWA parity):** follow-up phases. US2 owns the pre-release **schema squash** (`PlatformUser.PhoneNumber`/`PhoneVerifiedAt` + `PlatformUserTwoFactor` folded into the Tenant initial migration); US3 rides the same migration. Both reuse a Redis-backed `ServerSentOtpService` + `VerificationChannelRegistry` (SMS channel registered only when `ISmsSender` is configured; aggregate `SmsAvailable` gates the UI).

---

## Open Verifier PWA — present-then-cross-check the register anchor (Feature 155)

Evolves the `Sorcha.Verifier` reference app (Blazor **Server**) into an installable PWA that does **present-then-cross-check** verification, rendered as a verdict with a four-layer, progressively-disclosed validation trail. **Open** = no pre-shared issuer allowlist; resolve-and-verify everything reachable from the credential, surface the issuer identity, leave the trust judgement to the operator. Design: `docs/superpowers/specs/2026-06-17-open-verifier-pwa-design.md`; spec/plan/tasks: `specs/155-open-verifier-pwa/`.

### The four validation layers (engine + app)

`VerificationOutcome` (`Sorcha.Verifier.Engine/Models/VerifierSession.cs`) gained `IReadOnlyList<ValidationLayerResult> Layers` (+ enums `ValidationLayer { LivePresentation, IssuerSignature, Revocation, RegisterAnchor }`, `LayerStatus { Pass, Fail, Unverified }`). `VerifiablePresentationValidator` populates the first three on **every** return path (the Revocation layer is **omitted**, not faked Pass, when the credential carries no status reference); the verifier app appends **RegisterAnchor** after the anchor read (the engine stays HttpClient-free). `LayerStatus.Unverified` (could-not-determine) is deliberately distinct from `Fail` — **Unverified never vetoes** an otherwise-passing verdict; a `Fail` does (`VerdictViewModel.OverallPass = Accepted && no Fail layer`). The validator surfaces the credential `jti` on the IssuerSignature layer `Detail` for the anchor lookup.

### Layer 4 — the open register-anchor cross-check

A credential **cannot embed its own issuance txId** (the SD-JWT is built before the issuance tx seals), so "self-anchoring" carries the **registerId** (a disclosable `registerAnchor` claim) + uses the credential's own **jti** as the lookup key. New **public/anonymous** Register Service endpoint `GET /api/registers/{registerId}/credentials/{credentialId}/anchor` (`VerificationEndpoints.cs`) finds the credential-issuance tx via `IReadOnlyRegisterRepository.GetCredentialIssuanceTransactionAsync` (matches `MetaData.TrackingData["type"]=="credential-issuance"` + `["credentialId"]`) and returns `{ txId, docketNumber, sealedAt, status, inclusionProof }`. The verifier's `IRegisterAnchorClient` calls it (base from `RegisterService:PublicBaseUrl`) then re-verifies the Merkle proof against the existing anonymous `POST /inclusion-proofs/verify`. F079's GET inclusion-proof/bundle stay auth-gated; this new read is the open path.

### UI + PWA (path A)

Three screens: **Ask** (`Index.razor` — `QuestionPresets`: "Age over 18?" requests only `age_over_18`+`portrait`, "Confirm identity", "Custom"), **QR session** (unchanged OID4VP `direct_post` transport), **Verdict** (`Outcome.razor` — `IdCardLayout`-style header + the four-layer trail via `MudExpansionPanels`, label-left/status-right, disclosed-vs-withheld block proving minimal disclosure, register-anchor as a "tap to verify inclusion proof" beat). PWA shell: `wwwroot/manifest.webmanifest` (scope `/verify/`), `service-worker.js` (shell + `offline.html`; circuit not cached), `js/pwa-install.js` (`beforeinstallprompt`), wired in `App.razor` + an install button in `MainLayout.razor`. Trust runs `requireIssuerSignature:true` with the composite DID-backed resolver — the issuing org **must have an org master key** or its `iss` is the unresolvable bare-wallet form (the [[org-vc-issuer-did-anchoring]] split-brain).

**Out of scope (roadmap):** WASM/offline verifier (path B), hard issuer allowlist, ZK age predicates, the external X.509/EUDI rail + Ed25519 certs, mdoc presentation.

### Verdict screen wired into both hosts (Feature 174)

The rich `VerdictTrailPanel` / `VerdictViewModel` (F163) — orphaned since F164 rewired the hosts to the shared question/QR flow but never mounted the verdict — is now the single verdict surface on **both** the web desk verifier (`Sorcha.Verifier/Components/Pages/Index.razor`, Blazor Server) and the PWA doorstep verifier (`Sorcha.Wallet.Pwa/Pages/Verify.razor`, WASM). It is **preset-adaptive**: the "Confirm identity" preset leads with a large portrait + name; "Age over 18?" leads with an "18+ / Over 18 — confirmed" hero + a minimal-disclosure statement and hides the name; both share the collapsed four-layer trust trail, issuer line, and disclosed/withheld split. A hard **fail** (`!OverallPass`) suppresses the disclosed-identity block (portrait/name/"Shared with you"/issuer) so nothing is presented as trusted; **warn** (`Accepted` + `IssuerSignature == NotVerified`) renders a distinct amber banner, never a plain pass. Isolated CSS (`VerdictTrailPanel.razor.css`) carries the mockup palette with a `prefers-color-scheme: dark` override.

The verdict is **not** re-validated client-side — HAIP verifies online (with the real nonce + issuer-key resolution) and its `/result` endpoint returns the authoritative `VerificationResult` (`isValid`, `verifiedClaims`, `errors`, `holderKeyVerified`). `HaipOutcomeMapper` (`Sorcha.UI.Components.User/Services/User/Verification/`) maps that into a `VerificationOutcome` — synthesising the LivePresentation/IssuerSignature/Revocation layers from HAIP's real result (an accepted online verdict ⇒ all Pass + `IssuerSignature = Verified`), parsing `iss`/`jti` from the vp_token's issuer JWT for the issuer line + on-demand register-anchor lookup. A HAIP `"Denied"` state is surfaced as a **Fail** verdict (not a bare transport error), so declined credentials render a red banner + reason (SC-4). The outcome flows to the hosts through `IVerificationTransport` → `VerificationSessionPoll.Outcome` → `VerificationSessionQr.OnOutcome`. The offline `RealVerifierEngine` path (paste/proximity) still produces its own `VerificationOutcome` via `IVerifiablePresentationValidator` and can land `Accepted + NotVerified` → the warn treatment.

**AIAS issues `age_over_18`** (derived from `dateOfBirth` at issue time via `AgeClaimDeriver` in `ActionExecutionService.IssueCredentialFromActionAsync`, driven by an explicit `age_over_18` claim mapping + `disclosable` entry in `demos/AIAS/blueprints/aias-assured-identity.template.json`) so the "Age over 18?" preset has a claim to match — fail-closed (claim omitted when DOB is missing/unparseable), extensible to `age_over_NN` via the claim-name pattern. Design: `docs/superpowers/specs/2026-07-18-verifier-verdict-screen-design.md`.

---

## Agent-disclosed prior-action data (Feature 176)

Closes a fail-open hole found live on n1 (2026-07-07): the autonomous `Sorcha.Agent` decided on an **empty**
payload — it mapped `PendingAction.PreviousPayload` from the `/api/actions/pending` summary's
`prepopulatedPayload` (a Feature-104 form-prefill seed, empty for the AIAS verify action), which does **not**
carry the disclosed prior-action application data. Every external check defaulted (missing fields resolve to
false/null), so a fake postcode "ZZ99 9ZZ" was **approved** and a credential issued. The blueprint grants the
agent's `verification-analyst` participant `/*` disclosure, but the agent never fetched the disclosed view.

**The endpoint (read-side of the DAD model).** `GET /api/workflows/{instanceId}/actions/{actionId}/disclosures`
(+ instance-wide `GET /api/workflows/{instanceId}/disclosures`) in `WorkflowDisclosureEndpoints.cs`, filling a
route the client (`IBlueprintServiceClient.GetDisclosedDataAsync`) and MCP `DisclosedDataTool` already targeted
but no server implemented. `.RequireAuthorization()`; resolves the **caller's** wallet(s) via the same
Wallet-Service fallback `ParticipantWalletResolver.ResolveUserWalletAddressesAsync` uses (consumer/service
tokens omit `wallet_address` under F136 — resolved by `platform_user_id`→owner, #912; extracted from
`ActionEndpoints` into `Sorcha.Blueprint.Service.Services.Infrastructure` during the P0 review fix on
`fix/pwa-p0-claim-and-camera` once `InstanceActionEndpoints` became a fourth caller). Returns `DisclosedActionData`
(`Models/DisclosedActionData.cs`): `recipientResolved` + merged `disclosedFields` (agent) + a per-prior-action
`disclosures[]` list `{actionId, actionTitle, disclosedAt, data}` (MCP-compatible wire shape). Non-recipient →
`200` with `recipientResolved:false` + empty view (distinguishes "no disclosure" from auth failure). No new JWT
claim.

**The shared resolver (`IActionDisclosureResolver`).** The disclosure logic was extracted from the previously
private `ActionExecutionService.ApplyDisclosuresAsync` into `ActionDisclosureResolver` so the execution
(submit) path and the query (read) path share **one** authority (`ActionExecutionService` now delegates to it —
regression-guarded by the existing DevMode disclosure test). Two methods:
- **Submit-side** `ApplyDisclosuresAsync(action, data, blueprint, participantWallets, registerId)` — engine
  `ApplyDisclosures` (per-action JSON-Pointer rules) + participant→wallet resolution → `{wallet → fields}`.
- **Read-side** `ResolveDisclosedDataAsync(instanceId, actionId, callerWallets, delegationToken)` —
  **reconstruct-then-clamp**: `IStateReconstructionService.ReconstructAsync` scoped to the caller's wallets
  yields each required prior action's caller-decryptable view (encrypted **and** dev-mode paths are normalised
  by StateReconstruction), then the submit-side primitive **clamps** each action's data to the caller
  participant's entitlement — a belt-and-braces guarantee that the dev-mode merge-everything fallback can never
  widen disclosure to a non-recipient. `registerId` is derived from the instance (not a param). Fails closed to
  an empty view on any reconstruction fault.

**Agent consumption (`Sorcha.Agent`).** `IDecisionEngine.RequiresDisclosedPayload` gates the fetch:
`RulesDecisionEngine` returns `_rulesRequireChecks` (rules referencing `checks.*`), `AiDecisionEngine` returns
`false` — so only check-dependent agents fetch, and existing simple/persona agents are unaffected (no
hold-forever regression). Per pending action `RunCommand` calls `DisclosedPayloadEnricher` (over
`HttpDisclosedDataClient`, a raw GET with the agent's **user** bearer + `X-Delegation-Token` — NOT
`BlueprintServiceClient`, which mints a service token and would resolve the wrong identity); on a fetch
failure / non-recipient it **holds** (no submission, retries next poll), else sets `PreviousPayload =
disclosedFields`. `PollingInboxListener` no longer sources `PreviousPayload` from the summary (kept only the
`schema→dataSchema` correction). Defense-in-depth: `RulesDecisionEngine` also holds when `_rulesRequireChecks`
and the payload is empty (mirrors the #1077 hold). US3 explainability: the structured "External checks
evaluated … (from payload fields: […])" log identifies the evaluated facts + source fields for any decision.

**Credential issuance gated on the decision (FR-004 / SC-003).** The n1 E2E exposed a second, pre-existing
gap the now-working reject path reached for the first time: the `SorchaLocalWallet`/HAIP credential mint in
`ActionExecutionService` fired whenever the action had a `credentialIssuanceConfig`, with no gate on the
decision — so a `decision:"rejected"` submission still minted + delivered a credential. Fix:
`CredentialIssuanceConfig.IssuanceCondition` (optional JSON-Logic over the submitted action data). When set
and falsy, the mint is skipped (both delivery paths share one `credentialIssuanceAllowed` flag evaluated via
`IJsonLogicEvaluator`); null preserves always-issue; unevaluable fails closed. The AIAS + AssuredIdentity
action-2 configs carry `"issuanceCondition": {"==":[{"var":"decision"},"approved"]}`. Separately, the agent
calls the endpoint **through the API gateway**, which needed a `/api/workflows/{**catch-all}` route to
blueprint-cluster (the MCP tool uses the direct service address, so unit tests didn't catch that gap).

**Witness:** `demos/AIAS/rehearse.ps1` — valid → approved (credential delivered), invalid "ZZ99 9ZZ" → rejected
(no credential). Spec: `specs/176-agent-disclosed-payload/`.

---

## AIAS decision integrity & visibility (Feature 183)

Two coupled fixes to the AIAS web path (M1). Spec: `specs/183-aias-decision-visibility/`. Design:
`docs/superpowers/specs/2026-07-12-aias-emailverified-claim-source-design.md`.

- **`x-claim-source` (US1) — the emailVerified gate is now genuine.** Every real web submission was
  auto-rejected because the read-only `emailVerified` field (on no form page) never reached the
  wallet-signed payload → agent read absent → false → reject. Fix: a **headless** JSON-Schema property
  extension `x-claim-source: "<claim>"` + `ClaimSourceSeeder` (`Sorcha.UI.Components.User/Services/User/Forms/`,
  `IClaimSourceSeeder`) that at form init stamps the field from the authenticated principal's named JWT
  claim (`email_verified`, already minted by F157 `TokenService`, already on the `ClaimsPrincipal` via
  `CustomAuthenticationStateProvider`) into `FormContext.FormData` — so it rides the signature.
  `SorchaFormRenderer.SeedClaimSourcesAsync` runs fire-and-forget on `actionChanged` (mirrors persona
  autofill; graceful-skip via `IServiceProvider.GetService`, never clobbers user input). **Boolean fails
  closed** (absent/unparseable → false). Registered in `AddSorchaUserComponents` (web + PWA). Top-level
  properties only (nested = documented YAGNI).
- **`x-decision-notice` (US2) — reject visibility.** The agent's terminal reject fired only an ephemeral
  `WorkflowCompleted` SignalR signal — no durable record, no reason. Fix: a **route** annotation
  `x-decision-notice` (`Sorcha.Blueprint.Models.DecisionNotice` on `Route.DecisionNotice`) +
  `BlueprintInboxWriter.WriteDecisionAsync` (`Category="Workflow"`, `IconKey="workflow.rejected"`,
  `Summary`=reason; kind-discriminated deterministic `SourceEventId` `("decision-notice", …)`). F118 bell
  drawer renders it with **no client change**. **Reject-only**: approval is already surfaced (claim
  action-available + credential-received). **The delivery mechanism was reworked by Feature 184 — see
  below; the original inline `ActionExecutionService` hook and `DecisionNoticeDispatcher` are gone.**
  Follow-up issue #1163: citizen "My Applications" history page + email-on-decision.

---

## Decentralised decision notice + reason codification (Feature 184)

The F183 notice never reached a citizen, for two coupled reasons — both fixed here. Spec:
`specs/184-decision-notice-decentralised/`. Design:
`docs/superpowers/specs/2026-07-13-aias-decision-notice-decentralised-design.md`.

**1. It fired on the wrong node.** The inline `ActionExecutionService` hook runs only on the node that
processed the **decider's** submission (the agent / register-owner node). A citizen's account, wallet and
inbox live on **their** node — the default assumption in a federated DAD deployment. So the notice now
fires from the **fold**: `InstanceProjector` → `ReactionDispatcher.DispatchDecisionNoticeAsync`, which runs
on every node holding the register and is **entitlement-gated** (`IWalletServiceClient.GetWalletAsync` is
local-only ⇒ only the node hosting the recipient's wallet acts; the decider's node folds the same tx and
skips) and **idempotent** (`react:{sealedTxId}:decision-notice:{wallet}` SET-NX). `IReactionDispatcher.DispatchAsync`
now takes the sealed `TransactionModel` (the projector already holds it) rather than a bare txId, plus a new
`IActionResolverService` dep. It runs **before** the terminal/active branching, so a notice on a
non-terminal route (e.g. "returned for more info") fires too.

**2. The reason could not travel.** A background fold on the citizen's node has **no delegation token** and
cannot decrypt a disclosure group; `IStateReconstructionService` / `IActionDisclosureResolver` are
prior-action-scoped and don't return the completed action's own payload anyway. And copying the free-text
`verificationNotes` into clear metadata would leak analyst prose to every node. Fix — **reason
codification**:

- **Carrier = the sender-signed `RoutingDecision`.** It gains `routeId` + `reasonCode` (`Sorcha.Register.Models`).
  Both are **copied into `ComputeSignableBytes()`'s field-by-field rebuild** — a field omitted there would ride
  the wire unauthenticated while appearing signed — so `VAL_ROUTING_002` verifies them with **zero new
  validator code**, and they reach every node with zero new plumbing (already projected onto the sealed tx by
  `DocketBuildTriggerService`, already read by `InstanceProjectionResolver.ResolveRoutingDecision`). A raw
  `TrackingData` key would NOT do: the tx signature covers only `{TxId}:{PayloadHash}`.
- **`routeId`, not next-action-set matching.** Two routes can share a next-action set (and every terminal route
  shares the empty one), differing only by condition — which the citizen's node cannot re-evaluate. The producer
  knows the route it took, so it says so. The engine's `RoutingResult` gained a top-level `MatchedRouteId`
  because `BuildRoutingResult` returns `RoutingResult.Complete()` for a terminal route, discarding `route.Id` —
  and a reject route **is** terminal.
- **Catalogue in the blueprint.** `DecisionNotice` drops `reasonField` (clean break) and gains `reasonCodeField`
  (pointer to the code in the payload), `reasons` (code → citizen-facing message) and `fallbackMessage`.
  `DecisionNotice.ResolveMessage(code)` = `reasons[code] ?? fallbackMessage ?? ""`. The citizen-facing copy
  lives in the replicated blueprint; the agent's prose stays on the ledger as the audit record. Agent rules
  emit the code as an ordinary payload field — **no agent code change**.
- **The payload is read exactly once, by the decider** (`ActionExecutionService.ResolveDecisionReasonCode`,
  step 10d) — the node submitting it, which can plainly read it. Nothing downstream touches payload.

**Recipient resolution** (already on the branch, `dedb339c`): `BlueprintInboxWriter.ResolveRecipientPlatformUserIdAsync`
tries the participant registry first, then falls back to the **sending wallet's `Owner`** — a **consumer
wallet's `Owner` IS the PlatformUserId** — which is what makes a **late-bound open-participant citizen**
(no participant record ⇒ 404) resolvable at all.

**Removed (clean break, CI-greppable):** `DecisionNoticeDispatcher` (+ tests), the `ActionExecutionService`
9-notice hook, `SafeEvaluateCondition`, `DecisionNotice.ReasonField`.

---

## Citizen "My Applications" — durable outcome + reason (Feature 186)

The citizen web surface for *"what did I submit, and what happened?"*. Extends F145's fold and reuses
F184's reason plumbing. **Web only** (`Sorcha.UI.Web.Client`); the PWA keeps `/applications` (F154
catalogue) and `/applications/{guid}` (`ApplicationInstance.razor`) and is untouched.

| Method | Endpoint | Notes |
|--------|----------|-------|
| GET | `/api/me/applications` | Paged; caller's own applications, terminal ones included |
| GET | `/api/me/applications/{instanceId}` | Adds a step timeline; `404` for "not yours" **and** "no such thing" |

Blueprint Service's first `/api/me/*` group. Authorization is **plain** — not `RequireConsumerAudience`
— because a citizen is consumer-tier on `/wallet` and platform-tier on `/app` and must see the same
applications from either (pattern #13: no "any-human" tier, so cross-tier endpoints stay unclassified).

**A sibling of `/api/instances`, never a reshaping of it.** The PWA binds `GET /api/instances/{id}`,
so that group keeps its raw-model shape and this one carries the citizen projection.

### The load-bearing insight: a refusal is a route, not a state

Under F184 a refusal is expressed as **taking a route that declares `x-decision-notice`**. When such a
route ends the branch, `InstanceProjection.ApplyInPlace` sees an empty next-action set and assigns
`InstanceState.Completed` — so a refused application and an approved one are **indistinguishable by
state**. Report `state` and you tell a refused citizen their application "completed".

Hence `MyApplicationSummary.Outcome`, derived from the taken route's notice severity
(`Warning`/`Error` ⇒ `NotApproved`), separate from `State`.

- `Instance.DecisionRouteId` / `DecisionReasonCode` are folded from `RoutingDecision` on the signed
  clear metadata — inside `ComputeSignableBytes`, so determinism and rebuild parity hold. Assigned
  **unconditionally**, so a fold with no decision *clears* them.
- The **wording is resolved on read**, via the same `DecisionNotice.ResolveMessage` the
  `ReactionDispatcher` uses. Deliberately not folded: the blueprint is node-local, and folding it
  would break F145's "identical on every node". The reason **code** never reaches a citizen.
- `ResolveMessage` returns `FallbackMessage ?? ""` — treat empty as *no reason* and omit the field.

### Traps

- **`ProjectedTransaction.IsRejection` is dead.** Nothing in `src/` sets it true; only a unit test's
  own helper does, so `InstanceState.Rejected` is unreachable through the fold. Do not "fix" it here —
  `BuildRejectionTransactionAsync` writes metadata with no blueprint/instance/action id, so the
  transaction is not instance-scoped and cannot reach the fold at all. It belongs to F145's retirement
  of the imperative advance.
- **`InstanceState` serialises as an integer.** Blueprint Service configures no
  `JsonStringEnumConverter`. The DTO sends the **name**.
- **`Metadata["BlueprintTitle"]` is absent on projector-created instances** — only the imperative
  creation path stamps it. The blueprint lookup fallback is the normal case, not defensive coding.
- **`EfCoreInstanceStore.UpdateAsync` copies model→entity by hand.** A field missing from that list is
  written in memory, reported saved, and lost. `EfCoreInstanceStoreUpdateRoundTripTests` is the guard.
- **`needsYou` must fail closed** — terminal, unresolvable blueprint, or absent binding all ⇒ false.
  That is what makes #1268 (a stale live "Take Action" button) unable to recur.

`WebInboxDetailRouter` maps inbox `/api/instances/{id}` hrefs to `my-applications/{id}`; the web host
previously registered nothing and fell through to the refusing default, so decision notices rendered
as dead rows. Base-relative, like the PWA's router.

---

## AIAS Cyber Level (M2)

A second AIAS conference-demo workflow (`demos/AIAS/blueprints/aias-cyber-level.template.json`), independent
of the M1 Assured Identity application. The citizen presents their Assured Identity credential to prove
entitlement, answers an eight-question cyber-hygiene questionnaire (six graded `Selection` questions +
two 0-3 sliders, 24 points total), and the autonomous **Cyber agent** (`Start-AiasAgent -Mode cyber`) scores
the answers into a Bronze/Silver/Gold/Platinum band and issues a `CyberLevelCredential` carrying the level
plus the portrait mapped forward from the presentation — or hard-rejects before scoring when the presented
credential carries no portrait.

- **The `scored-questionnaire` check** (`ScoredQuestionnaireCheck`, `src/Apps/Sorcha.Agent/Decision/Checks/`)
  sums a questionnaire into a single numeric fact, two modes per question shape: `answers` maps an exact
  submitted string to points (graded multiple choice), `ranges` maps a submitted number into a band
  (slider), evaluated top-down with each range's `max` an INCLUSIVE upper bound and the entry with no `max`
  the catch-all. **There is deliberately no "could not score" outcome** — every question is schema-`required`
  so the validator guarantees presence, and an unrecognised or missing answer simply scores 0 (a `ranges`
  field that is absent or non-numeric always scores literal 0, never falling through to the catch-all band —
  the catch-all is for a genuine numeric answer outside every declared range, not a stand-in for "no
  answer"). A faulting scorer is contained by `ExternalCheckRunner` into boolean `false` (JSON-Logic coerces
  to 0), so a broken scorer lands in the lowest band and issues nothing rather than throwing.
- **The numeric-fact contract on `ExternalCheckResult`.** `ExternalCheckResult(Name, Value, Detail, Numeric)` —
  `Numeric` is an optional `double?`, null for ordinary boolean checks (`portraitPresent`), populated for
  `scored-questionnaire` (`cyberScore`). `ExternalCheckRunner.RunAsync` merges **either** the numeric **or**
  the boolean into the `checks.*` fact dictionary per check name — never both — so `{"var":
  "checks.cyberScore"}` resolves to the band-comparable number in rules, and `checks.portraitPresent` stays a
  plain boolean. A check needing both a meaningful bool and a number must expose two distinct fact keys.
- **Answer strings ARE the scoring keys, matched ordinally against `agent/cyber.checks.json`'s `answers`
  table.** The `Selection` control has no separate display-label/value split, so a mistyped or drifted answer
  string scores 0 **silently** — no error, no log line beyond the routine `score N (...)` breakdown. Any
  change to an enum value in the blueprint's `dataSchemas` MUST be mirrored verbatim in `cyber.checks.json`.
- **Two-register topology.** The cyber questionnaire runs on its **own** register (`New-AiasOrg` step 6a
  creates it; step 6c publishes the same Assure-ID agent wallet as a participant on it too — one agent
  *wallet*, two register *participations*, not two identities), separate from the Identity register the
  Assured Identity credential is issued on. This is the one new cross-register assumption M2 introduces:
  a credential minted on one register gates a workflow submitted on another, exercised via the action's
  `credentialRequirements` (`presentationSource: SorchaWallet`) and a directly-submitted
  `credentialPresentations` array (bypasses the async F111 HAIP/SorchaWallet presentation lifecycle in
  favour of the synchronous internal-verifier branch in `ActionExecutionService`). `Publish-AiasBlueprint`
  publishes per-spec `RegisterId`s (not one shared register), and `Get-AiasDemoStatus` / `Test-AiasAgentAlive`
  probe the cyber register and cyber agent process independently so either going missing reports a named
  reason rather than a false-positive Ready.
- **Rehearsal**: `demos/AIAS/rehearse.ps1 -Scenario cyber` (default scenario is still `identity`, M1's
  unchanged three paths). Four paths: perfect card (24/24) → Platinum; perfect card minus two deliberate
  "trap" answers (calendar password rotation, inspecting a sender address instead of verifying out-of-band —
  2 points each) → Silver (20/24) — **the path that proves the spread is real and both traps bite**, not
  just that scoring runs; a dishonest-but-consistent low score (0/24, below the 12-point Bronze floor) → no
  credential, inbox decision notice matches the `cyber-fail` catalogue entry; a perfect card presented with a
  portrait-less Assured Identity → hard reject before scoring, no credential, inbox notice matches
  `no-portrait`. Spec: `specs/174-aias-assured-identity/`.

---

## EUDI conformance — DCQL dialect, trust rail, verifier auth (Feature 181, US1–US6)

Protocol-alignment feature moving every Sorcha presentation surface onto the OpenID4VP 1.0 **final**
dialect and adding multi-credential / alternative asks. **All six user stories are shipped.** US1
(dialect cutover, #1147) + US2 (multi-credential, #1149) are the presentation track; US3 (trusted-list
rail, #1150), US4 (external issuance identity), US5 (cert lifecycle), US6 (verifier authentication) are
the trust track. Spec: `specs/181-eudi-conformance/`.

### Shared DCQL dialect (US1)

The single owner of the `dcql_query` wire shape is `Sorcha.Verifier.Engine/Dcql/` — `DcqlModels`
(records with exact spec property names + `Validate()`), `DcqlRequestBuilder` (asks + alternative
groups → `DcqlQuery`, owns the required/optional → `claims`+`claim_sets` mapping), `DcqlRequestParser`
(inverse; rejects PE shapes with `LEGACY_DIALECT`), and `DcqlVpToken` (the object-keyed response
envelope `{ "<queryId>": ["<presentation>"] }`). Presentation Exchange (`presentation_definition` /
`input_descriptors` / `presentation_submission`) is retired and CI-gated
(`scripts/check-presentation-dialect.ps1` + `.presentation-dialect-allowlist`, ratcheted empty). SD-JWT
`typ` is now `dc+sd-jwt` (verify still dual-accepts stored `vc+sd-jwt`). Every producer converges on the
served `request_uri` form; the inline-`presentation_definition` deep link is refused.

### Multi-credential & alternative asks (US2)

- **`CredentialRequirement.AnyOfGroup`** (`Sorcha.Blueprint.Models/Credentials/`) — the blueprint-author
  surface for alternatives. Requirements on one action sharing a non-null tag are alternatives (present
  any one); ungrouped requirements are each AND-required.
- **`RequirementDcqlMapper`** (`Sorcha.Blueprint.Service`) — THE requirement-set → `DcqlQuery` map
  (contract §4): one credential query per requirement (id = slugified type), anyOf groups →
  `credential_sets` options, and — once any group exists — an explicit required singleton set per
  ungrouped ask so AND-requiredness survives the presence of `credential_sets`.
- **Plumbing** — `PresentationLifecycleService.InitiateAsync` builds the query from the action's full
  same-source requirement set and carries it to the two DCQL producer sites: the SorchaWallet consumer
  via `PresentationInitiationContext.DeclaredDcqlQueryJson` (served verbatim; single-ask fallback), and
  the HAIP verifier via `IHaipServiceClient.CreatePresentationRequestAsync(declaredQueryJson)` →
  `CreatePresentationRequestBody.DeclaredQuery` → `PresentationRequestStore.CreateAsync(declaredQuery)`
  → `GetRequestObject` serves it. `DeclaredDcqlQueryJson` is JSON (not a typed field) to keep the
  `Sorcha.PresentationLifecycle.Abstractions` assembly free of a DCQL-model dependency.
- **Wallet side** — `PresentationEngine.MatchQuery` returns a `DcqlMatchResult` (per-query candidates,
  `credential_sets` solving, unsatisfied-required detection); `BuildVpTokenEnvelopeAsync` builds one
  SD-JWT presentation per consented query into the object-keyed envelope (shared single-presentation
  core with `BuildVpTokenAsync`). `Present.razor` gains a multi-credential path (gated on >1 credential
  query or a multi-option set): alternative-option pick → per-query candidate pick → filtered
  multi-query consent → envelope build. The proven single-ask flow is preserved verbatim.
- **Consent UI** (`Sorcha.UI.Components.User/Components/Presentation/`) — `ConsentSheet` renders one
  section per query (`DcqlMatchResult` + per-query `Selections` + `QueryOptionalToggles`); required
  claims locked, optional toggleable, unsatisfiable required asks flagged, confirm disabled when any
  required query/set is unmet (no partial submit). `CredentialPickerDialog` gains an alternative mode
  (`SetChoice` → offer each satisfiable option, no auto-pick).
- **Verifier side** — HAIP `HandleDirectPost` runs a per-query verification loop; the overall verdict
  is `VerifierEndpoints.AllRequiredSatisfied` (with `credential_sets`: every required set has one
  fully-verified option; without: every declared query verifies). Unknown envelope key →
  `DCQL_UNKNOWN_QUERY_ID`. `VerificationResult.PerQuery` carries per-query outcomes.
- **Known gap** — the F127 `SorchaWalletPresentationConsumer.VerifyAsync` is still single-credential, so
  full multi-credential PWA→SorchaWallet *verification* is a follow-up; the HAIP verifier is the
  multi-query-capable path (covered by `MultiCredentialPresentationTests`).

### US3 — Trusted-list snapshot rail (ETSI TS 119 612)

Adds the external-EUDI trust rail: operators import signed trusted lists; verifying services (Blueprint
+ HAIP) resolve CA anchors from the imported snapshot for the `x509-lotl` / `trustlist` trust source.

- **Model + store** — `TrustedListSnapshot` + `TrustedListAnchor` (Tenant EF, `public` schema, cascade
  delete; folded into the InitialCreate migration). `ITrustedListSnapshotStore` (EF + in-memory): import
  supersedes the prior Active version, newest-per-`trustListId` is authoritative, delete removes all
  versions. Warn-tier storage-log registration (not audited). A `TenantDbContextDesignTimeFactory` lets
  `dotnet ef` build the model.
- **Import** — `TrustedListImportService` (Tenant): enveloped XMLDSig **core** verify (`SignedXml`,
  `verifySignatureOnly` — tamper protection per R5/D3; XAdES + LOTL pivot-chain deferred), TS 119 612
  parse (sequence/dates/territory/operator/signer identity), granted **CA/QC** anchor extraction +
  extracted-vs-skipped summary, sequence-monotonicity. Typed failures `TRUSTLIST_MALFORMED` /
  `_SIGNATURE_INVALID` / `_SEQUENCE_REGRESSION`. Registers an ECDSA XMLDSig `SignatureDescription` so
  ECDSA-signed lists verify (EU LOTL is RSA).
- **Admin surface** (`TrustEndpoints`, replaces the F135 placeholder PUT — clean break): `POST
  /api/v1/trust/trustlists/import` (multipart | HTTPS fetch-once), `GET /trustlists`, `GET
  /trustlists/{id}` (detail + anchors + summary), `DELETE /trustlists/{id}`, service-tier `GET
  /trustlists/{id}/anchors` (DER roots + freshness, 404 `TRUSTLIST_UNAVAILABLE`). Admin UI:
  `Sorcha.UI.Web.Client/Pages/Admin/TrustedLists.razor` + `ITrustedListAdminService`.
- **Verifying-services read path** — `HttpTrustListProvider` (`Sorcha.ServiceClients.Http`, singleton):
  service-tier read of the anchors endpoint with a 15-min in-process cache, fail-closed null on 404.
  Service-layer adapter `TrustListAnchorProvider` (one per service — Blueprint + HAIP) maps the snapshot
  to a `TrustAnchorSet` with **`AnchorSetId = {trustListId}#{sequenceNumber}`** so the snapshot identity
  flows into `TrustEvidence.TrustListId` (FR-015). Both services register the trustlist
  `ITrustSourceResolver` + a named Tenant client.
- **Freshness** (`TrustListFreshness.Compute`, boundary-deterministic via `TimeProvider`): Fresh strictly
  before the effective next-update (list `NextUpdate`, else `ListIssue + 90d`). The adapter gates on it —
  **warn mode** (default) vouches with a stale-flagged evidence trail + metric + log; **strict mode**
  (`Trust:TrustListStrictFreshness`) fails closed (`TRUSTLIST_STALE`). Multibase base64url (`u`-prefix)
  Bitstring Status List `encodedList` now decodes beside plain base64 (R7).
- **Metrics** (`Sorcha.Trust` meter): `sorcha_trustlist_stale_evaluation_total{trust_list_id,sequence,mode}`
  + `sorcha_trustlist_snapshot_info` gauge (one series per Active snapshot). Structured import/delete audit
  logs.
- **SC-004 proof** — `TrustListVerificationTests`: import fixture list → verify a credential issued under
  the fixture CA (vouched, evidence `eu-lotl#3`) → delete → fail closed.

### US4 — Externally-verifiable issuance identity (external X.509 rail)

An org generates a CSR bound to its P-256 issuing key, imports an externally-issued cert+chain, and
issues credentials that chain to the **external** root — failing closed when the cert is absent, expired,
or key-mismatched. This is the missing outbound half of the `x509-lotl` rail (US3 was the inbound
verify half).

- **Org P-256 key resolution** — the org's issuing key is its **primary** key when that is ES256, else a
  derived HAIP co-key under `sorcha:haip-issuer-signing` (`boundKeySource` ∈ `Primary` | `HaipCoKey`).
  Wallet Service exposes the internal seam `IOrgIssuerCertKeyService` — resolve the org P-256 SPKI +
  pre-hashed ES256 sign — over `GET/POST /api/internal/wallets/{address}/issuer-cert-key[/sign]`; client
  methods `IWalletServiceClient.ResolveIssuerCertKeyAsync` / `SignIssuerCertPreHashedAsync`. Private key
  never leaves custody (CSR/cert signing is remote pre-hashed signing).
- **Tenant org-cert endpoints** (`TrustEndpoints`, under `/api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}`,
  `RequireAdministrator` + `RequirePlatformAudience` except the public chain reader):

  | Method | Route | Purpose |
  |---|---|---|
  | `GET` | `certificates` | List internal + imported certs with status/validity/chain summary + `eligibility` (`eligible`, `reason` = `CERT_KEY_NOT_ELIGIBLE`, `boundKeySource`) |
  | `POST` | `csr` | Generate a CSR bound to the org's P-256 key (server-resolved; optional `subjectDn`) → `csrPem` + `boundKeySource` + `boundPublicKeyThumbprint` |
  | `POST` | `certificates/import` | Import leaf `certificatePem` + `chainPem[]` (with/without root); supersedes prior Active imported cert |
  | `DELETE` | `certificates/{certificateId}` | Retire an imported cert (Status→Superseded); idempotent. Internal certs use the `revoke`/CRL path |
  | `GET` | `imported-cert-chain` | **Public** — imported chain for x5c resolution |

  Typed failure codes (`CertErrorCodes`, `422` problem+json): `CERT_KEY_NOT_ELIGIBLE`, `CERT_KEY_MISMATCH`,
  `CERT_CHAIN_INVALID`, `CERT_EXPIRED`, `CERT_UNSUITABLE`, `CERT_EXTERNAL_ANCHOR_UNAVAILABLE`.
- **Persistence** (Tenant Postgres, folded into InitialCreate) — `TenantRootCaRecord` (CA key AES-256-GCM
  encrypted), `OrgCertificateRecord`, `CsrRecord`; `InternalCaTrustProvider` is now a write-through cache
  over `ICertificateStore`.
- **Chain-attach** — `CredentialIssuanceConfig.TrustAnchor` (`register` | `x509-tenant` | `x509-lotl`)
  drives the x5c the issuer attaches. `x509-lotl` resolves the imported external chain and **fails closed
  `CERT_EXTERNAL_ANCHOR_UNAVAILABLE`** if absent; `x509-tenant` (per-tenant self-signed root) and
  `register` (DID-native, no x5c) are unchanged. Metric
  `sorcha_org_cert_issuance_total{provenance,outcome,reason}`.

### US5 — Certificate lifecycle

- **Typed eligibility guard** — `X509CertificateBuilder.BuildOrgCert` throws
  `CertKeyNotEligibleException` for a non-P-256 key (kills the prior ASN.1 500), so enrol returns
  `422 CERT_KEY_NOT_ELIGIBLE` for an Ed25519-primary org.
- **Enrol** (`POST .../orgs/{wallet}/enrol`, existing route, changed semantics) — body no longer carries
  a caller-supplied key; the server resolves the org P-256 key itself and **re-issues the internal
  tenant-root cert with auditable history** (`ITrustProvider.ReissueInternalCertAsync` supersede). Doubles
  as the **backfill** action for pre-existing orgs.
- **Auto-enrol** — best-effort server-side hook riding on `POST /api/organizations/{id}/wallet`, the
  moment an org first has a wallet (#1525), **not an API**. Failure never fails the link. It used to
  ride on server-side provisioning at org creation and on the `OrgWalletReconciliationService` sweep;
  both are gone, because they minted org wallets with nobody present to receive the recovery phrase.
- **Admin UI** — certificates panel in `OrgSettings.razor` backed by `IOrgCertificateAdminService`.
  `Organization.WalletAddress` is now exposed on `OrganizationResponse`.

### US6 — Verifier authentication

The HAIP verifier signs its OpenID4VP request object; the wallet cryptographically authenticates the
verifier before showing consent.

- **Verifier side** (HAIP) — `RequestObjectSigner` signs the request object (ES256) with an X.509
  **verifier certificate** (SAN dNSName = `Haip:PublicHost`), embeds the **`x5c`** chain, and uses a
  prefixed **`x509_san_dns:{host}`** `client_id`. Config: `Haip:VerifierCertificate` (PFX path or base64)
  + `Haip:VerifierCertificatePassword?` + `Haip:PublicHost`. Dev fallback = self-signed cert;
  **prod/staging fail-fast** when unconfigured. (`VerifierCertificate.cs`.)
- **`sorcha-agent` side (#1538, 2026-08-21)** — the agent uses the SAME `RequestObjectValidator`
  rather than its own check, so agent and wallet cannot drift on what counts as an authentic verifier.
  It previously verified only against a JOSE-header embedded `jwk`; US6 emits `x5c` and no `jwk`, so
  the check could only ever refuse and the agent's whole OID4VP present leg was unusable. Because an
  agent has no human to render consent to, it applies a policy over the three-state verdict: hard
  refusal always throws; `Unverifiable` throws unless `--allow-unverified-verifier`;
  `AuthenticUntrusted` proceeds with a warning (FR-027 — and it is the ordinary local path, since a
  dev verifier self-signs) unless `--require-trusted-verifier`. `--verifier-client-id` pins the
  expected identity; unpinned, the client_id is read from the request object itself, which proves
  internal consistency but **not** identity.
- **Wallet side** — `RequestObjectValidator` (`Sorcha.Verifier.Engine`, pure BouncyCastle / WASM-safe):
  ES256 JWS verify over the x5c leaf → leaf SAN == `client_id` host → chain-walk to a trusted anchor,
  yielding a three-state `VerifierAuthState`: `TrustedListVerified` / `AuthenticUntrusted` /
  `Unverifiable`. Tampered signature / SAN mismatch = **hard refusal** (`REQUEST_OBJECT_INVALID` /
  `REQUEST_HOST_MISMATCH`); **absent anchors never block** (FR-027). `ConsentSheet` renders the three
  states. KB-JWT `aud` = the full prefixed `client_id` on both sides. Metric
  `sorcha_request_auth_total{state}` on `Sorcha.Trust`.
- **Scope notes** — the anchor-fetch → `TrustedListVerified` path needs a **public** anchors read
  endpoint (US3's is service-tier); a documented follow-up, so v1 renders valid signed requests as
  `AuthenticUntrusted`. The F127 `SorchaWallet` consumer keeps its **DID** `client_id` (register-native
  rail) — it renders `Unverifiable` under the x509 validator and is never blocked.

---

## Proximity credential sharing — ISO 18013-5 over BLE (Feature 185)

In-person, **offline** credential presentation: the citizen's phone and a verifier's device exchange a
presentation directly over Bluetooth, with **no network and no server**. This is F135's explicit
*"proximity deferred"* coming due. Spec: `specs/185-mobile-proximity-sharing/`. Design:
`docs/superpowers/specs/2026-07-13-mobile-proximity-sharing-design.md`.

**Status: US1 shipped (the protocol). The native transport and both UIs are the follow-on PRs.**

### The architecture in one line

**Thin native transport, one shared C# protocol.** The (not-yet-built) Capacitor plugin does *only* BLE —
opaque bytes — while every byte of ISO protocol lives in C#, written once, shared by holder and reader. The
alternative (implementing 18013-5 in Swift *and* Kotlin) was rejected: two implementations of the tag-24 rules
is two chances to get them wrong, in languages the test suite cannot reach.

### `Sorcha.Mdoc` — extracted, pure-managed (the enabling move)

`Sorcha.Cryptography` P/Invokes libsodium + MCL and **cannot load in Blazor WASM**, so the wallet could never
reference the mdoc codec. The `Mdoc/` tree is now its own pure-managed project (the
`Sorcha.Cryptography.Secp256k1` precedent). `Blueprint.Engine` and `Haip.Service` re-point at it; clean break,
no shim.

New in it: `CoseKey`, **`CoseSign1Builder`** (COSE_Sign1 from a *raw* signature — the device key is a
non-extractable WebCrypto key, so `CoseSign1Message.SignDetached` **cannot** be used), **`CoseMac0`** (the BCL
has no such type — the documented reason F135 refused `deviceMac`), `MdocSessionCrypto`,
`ProximitySessionTranscript`, `DeviceEngagement`, `SessionEstablishment`/`SessionData`, `MdocDeviceRequest`,
and **`MdocDeviceResponseBuilder`** — the holder side, which did not previously exist anywhere (`MdocCodec`'s
write path is private; `MdocIssuer` is issuer-side, `MdocService` verifier-side).

`MdocService` gained `Verify(response, byte[] sessionTranscript, byte[]? eMacKey)`; the OpenID4VP overload
delegates to it, so the online and proximity paths cannot drift. **`deviceMac` is now verified** — a `deviceMac`
with no `EMacKey` is **rejected**, not waved through.

### THE crypto finding: `deviceMac` needs a SECOND device key

ISO derives `EMacKey` by ECDH between the mdoc's **static** device key (published in the MSO) and the reader's
**ephemeral** key — so the MSO device key must be **ECDH-capable**. In WebCrypto a key's usages are fixed at
generation and **a key cannot be both ECDSA and ECDH**. The wallet's existing device key is ECDSA-only
(`WebCryptoDeviceKeyService`), so it **structurally cannot** produce a `deviceMac`.

⇒ The mdoc rail gets a **second**, non-extractable **ECDH** P-256 device key; the SD-JWT/KB-JWT rail keeps the
existing ECDSA one. Sorcha-issued proximity-capable mdocs bind `MSO.DeviceKey` to the ECDH key.

### The two transcript forms (the classic week-loser)

The standard uses **both**, and they are not interchangeable:

| Form | Used for |
|---|---|
| `SessionTranscript` — the bare 3-element array | Spliced into `DeviceAuthentication` |
| `SessionTranscriptBytes` — `#6.24(bstr .cbor …)` | **Hashed (SHA-256) for the HKDF salt** |

Swap them and you derive plausible keys that decrypt nothing, **silently**. `ProximitySessionTranscript` hands
out both, named, so no caller has to choose.

### Evidence: ISO Annex D, not self-consistency

`IsoAnnexDVectorTests` reproduces the **standard's own ciphertexts** — decrypts its `SessionEstablishment` to its
`DeviceRequest`, its `SessionData` to its `DeviceResponse`, re-encrypts byte-for-byte, and **verifies its own
`deviceMac`**. A wrong salt, info string, ECDH pairing or nonce cannot reproduce someone else's ciphertext.

This settled empirically what prose disagrees about: **`DeviceAuthenticationBytes` occupies the `payload` slot
of `MAC_structure`** (RFC 9052 detached handling), *not* `external_aad`.

⚠ **Vector provenance:** ISO 18013-5:2021 is paywalled; the vectors come from OpenWallet Foundation `multipaz`
(Apache-2.0), whose constants are named `ISO_18013_5_ANNEX_D_*`. Documented in full on `IsoAnnexDVectors`.
⚠ **The DIS draft is a trap** — freely downloadable, with *different* crypto (empty HKDF info, `0x00`/`0x01`
salts, 2-element transcript). Validating against it yields a confidently wrong implementation.

### `Sorcha.Proximity.Abstractions` — the seam, and why it carries opaque bytes

`IProximityTransport` (`ProbeAsync` / `StartPeripheralAsync` / `ConnectCentralAsync` / `SendAsync` / `Received`
/ `Disconnected` / `StopAsync`) knows **nothing** about CBOR, mdoc, credentials or sessions — and that ignorance
is load-bearing. It is what lets **`LoopbackProximityTransport`** stand in for the radio so the **entire
exchange runs in CI with no Bluetooth and no phones**. Protocol knowledge in the transport would destroy that.
`ProximityHolderSession` / `ProximityReaderSession` are the state machines.

**The disclosure invariant is structural:** `ApproveAsync` is the *only* method that can encode credential data,
and it is reachable only from `AwaitingConsent`. Declining, abandoning, or a transport failure cannot disclose
anything — FR-010 is a property of the type, not a promise in a comment. It also refuses to disclose an element
the reader never asked for, so a bug in a consent UI cannot widen disclosure.

**A response disclosing NOTHING is not an acceptance.** The digest check iterates the elements *present*, so an
empty disclosure passes vacuously — sound crypto, but it would tell an operator their check succeeded when they
learned nothing. `ProximityReaderSession` fails that closed.

### Known gaps (honest)

- **The evidence bar is our own two devices** — self-consistency, **not** certified-reader interop. The Annex D
  vectors are the compensating control.
- **`MdocIssuer` uses a flat namespace equal to the docType.** A real mDL separates them
  (`org.iso.18013.5.1` vs `org.iso.18013.5.1.mDL`). An interop blocker for *issuance*; harmless to the
  proximity protocol, which is namespace-agnostic.
- **`readerAuth` parsed, not verified** — honestly skipped rather than stubbed.
- **No published vector for QR engagement** (Annex D's worked example is NFC handover), so the QR transcript is
  validated structurally.
- **CLOSED (was: "`MdocService.Verify` uses BCL `X509Certificate`/`ECDsa`, WASM unproven").** The verify path is
  now pure-managed: `X509Leaf` (BouncyCastle `X509CertificateParser`) resolves the issuer key, and
  `CoseSign1Builder.VerifyEmbedded`/`VerifyDetached` verify with BouncyCastle. `MdocSessionCrypto`'s AES-GCM
  moved to BouncyCastle `GcmBlockCipher` too — the Annex D ciphertext vectors still reproduce byte-for-byte, so
  the swap is pinned, not taken on faith. `MdocIssuer` keeps BCL `ECDsa` for **signing** — server-side only,
  and a browser never holds an issuer private key. Enforced by `scripts/check-wasm-safe.ps1` +
  `wasm-safe-gate.yml`.
- **Correction worth knowing: BCL `ECDsa` verification DOES work under browser-wasm.** An earlier note here
  implied otherwise. `Sorcha.Verifier.Engine` uses `ECDsa.Create` + `VerifyData` for ES256, it ships inside
  `Sorcha.Wallet.Pwa`, and that is the live holder→device delegation check the wallet performs in the browser
  on every presentation. The genuinely unreliable APIs are `X509Certificate2`/`X509CertificateLoader`,
  `ECDiffieHellman` and `AesGcm` — those are what the gate bans. New `Sorcha.Mdoc` code prefers BouncyCastle
  throughout anyway (one provider is easier to reason about than two), but that is a preference, not a
  portability requirement, and the gate does not pretend otherwise.

---

## Provenance — trust-anchor and proof lineage (Feature 188, Phase 1)

Two **read-only** admin surfaces that answer "who signed off on what, and can you prove it?" from a
fact back to the trust anchor. Phase 1 = the verification engine + register lineage. Spec:
`specs/188-provenance-lineage/`.

**Named provenance, NOT audit, and the split is load-bearing.** `IAuditService`
(`Sorcha.UI.Core/Services/Admin/`) *writes* a log of administrative actions. Provenance *reads*
evidence and reports what can be proven about it. One word covering both is the collision class
Feature 187 spent its length untangling (`Docket` ×2, `ValidatorSignature` ×2, `VoteDecision` ×2 with
*incompatible* values).

### The tri-state — `Unverified` is a first-class result, never a failure

`VerificationStatus` (`Verified` / `Failed` / `Unverified`) lives in the zero-dependency leaf
**`Sorcha.Verification.Abstractions`**, hoisted there from `Sorcha.Verifier.Engine.Models.LayerStatus`
(whose members were `Pass`/`Fail`/`Unverified`) so both engines share one declaration. **Member order
is load-bearing** — `VerificationOutcome` round-trips through `System.Text.Json` with no string-enum
converter, so these are integers on the wire.

- **`Unverified` = the check could not run.** It never vetoes an otherwise-passing trail.
- **A check that did not run MUST NOT report `Verified`.** A surface that manufactures confidence is
  worse than no surface.
- **Absence of evidence is never evidence of tampering.** A partial replica, a single-validator
  deployment, and a record predating the evidence a check needs are all ordinary healthy states.

### API surface (Register Service — it owns the evidence)

| Method | Route | Verifies? |
|---|---|---|
| GET | `/api/provenance/registers/{registerId}` | **No** — paged docket spine |
| GET | `/api/provenance/registers/{registerId}/dockets/{docketNumber}` | Yes — one docket's trail |
| GET | `/api/provenance/instances/{instanceId}` | Phase 2 — reserved, returns 501 |

`RequireAdministrator` **composed with** `RequirePlatformAudience` (pattern #13 — the tier gate sits
*on* the role gate; an Administrator role on a consumer-tier token is refused). Missing evidence
returns **200 with `unverified` rows carrying reasons, never a 5xx** — an auditor needs to know
*which* link failed.

**Two endpoints, deliberately.** Verification is O(n) hashing per docket, so a spine that verified
eagerly would be O(n·m) on a list view. `DocketSpineEntry` has **no status field**, and a test
enforces its absence.

⚠ **`/api/provenance/**` needs its own API-Gateway route** (`provenance-api` → `register-cluster`).
Without one it falls through to the `ui-static` `/{**catch-all}` and the whole surface is silently
unreachable — a **bodiless 404, no content-type**. Identical to F111/#1309.

### The five checks (`Sorcha.Provenance.Engine`, dependency-free)

`ProvenanceLayer` = `Anchor` | `Chain` | `Seal` | `Signers` | `Proposer`, emitted broadest-first. Every
check carries a **required `CheckedAgainst`** stating its basis, and a `Reason` when unverified.

The engine takes assembled evidence and returns a verdict trail. **It may never reference
`Sorcha.Cryptography` (libsodium P/Invoke, not WASM-loadable) or `Sorcha.ServiceClients.*`** — that
forecloses the Phase-3 portable export it exists for. Guarded at runtime by `EngineIsPortableTests`
(transitive closure) and statically by `scripts/check-wasm-safe.ps1`.

### THE FIVE TRAPS — every one produced a FALSE FAILURE on healthy live data

Four were found only by running against real n1 dockets, after a 2,500-test green suite.

1. **Roster-as-of, not roster-now.** A signature valid when made must stay valid. Evidence assembly
   resolves the roster version applying **at that docket**; the engine is never handed the current
   roster (there is no parameter for it). The boundary is **exclusive**: a change sealed in docket 12
   does not govern docket 12 — that docket was signed under the set in force at the time. Inclusive
   would accuse the sealing validators of *every governance docket*.
2. **A docket does not commit to its transaction ids.** `DocketBuilder.cs:159` builds the Merkle tree
   from `DocketHasher.ComputeTransactionHash(TxId, PayloadHash, Timestamp)` **leaves**. Recomputing
   over raw `TransactionIds` mismatches **100% of the time**. Leaves must follow `TransactionIds`
   *in order*, or tamper-detection becomes vacuous. ⚠ `Register.Service/Program.cs:3000`
   (`POST /proofs/inclusion`) still recomputes over raw ids and disagrees with
   `VerificationEndpoints.cs:82`, which does it correctly — see #1372.
3. **Identifier spaces differ.** `DocketHeader.ProposerValidatorId` is the validator's *configured*
   id (`local-validator`); `ValidatorRosterEntry.ValidatorId` is a *wallet address* (`ws11q…`).
   **Signers matches on PUBLIC KEY** (recorded identically on both sides, and the stronger claim).
   **Proposer cannot** — a docket header carries no proposer key — so a non-match is `Unverified`.
4. **A mismatched trust anchor is `Unverified`, not `Failed`.** From one node, "my anchor is not this
   network's" and "this register is forged" are indistinguishable; cross-node reconciliation is out of
   scope. Consequence: **Anchor has no `Failed` path on a single node in Phase 1.** Issue #1374.
5. **Empty votes is the common case.** Single-validator nodes record none. `signers` ⇒ `Unverified`.
   **A green tick there is the most serious defect this feature can have.**

### Runtime source

`src/Common/Sorcha.Verification.Abstractions/`, `src/Common/Sorcha.Provenance.Engine/`
(`DocketProvenanceVerifier`, `Evidence/`, `Seams/IMerkleRootCalculator`),
`src/Services/Sorcha.Register.Service/Provenance/` (`RosterAsOfResolver`, `DocketEvidenceAssembler`,
`MerkleRootCalculator` — a **delegation** to the platform's one `MerkleTree`, never a reimplementation
— `NodeTrustAnchor`, `ProvenanceMetrics`), `Endpoints/ProvenanceEndpoints.cs`,
`src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Provenance/` (admin-facing — **not**
`Components.User`, which reaches the wallet PWA).

Metrics: `sorcha_provenance_check_total{layer,status}` and
`sorcha_provenance_trail_duration_seconds{surface}` on the `Sorcha.Provenance` meter. No subject data.
`failed` and `unverified` must stay distinguishable — the first is an integrity signal worth alerting
on, the second usually means missing evidence.


## Blueprint version pinning — an instance runs the definition it started on (Feature 194)

Republishing a blueprint to a register it is already on used to be accepted, increment a version, and
**silently replace the executable definition for every instance of that id, including ones in
flight**. Confirmed live on n1: three versions on one register, all accepted, no error anywhere. An
instance mid-flow was then validated against actions and schemas it never saw.

### The pin is a sealed ledger fact, not a lookup

Under F145 an instance is a deterministic projection of the sealed ledger, so which definition it
runs **cannot** be resolved per-node at fold time: a value two nodes cannot both derive from sealed
transactions is a value they can diverge on. The pin therefore rides `RoutingDecision`
(`Sorcha.Register.Models`), which already travels on every forward-routing action transaction, is
sender-signed, and is verified by `VAL_ROUTING_002`.

```
RoutingDecision { completedActionId, nextActions[], routeId?, reasonCode?,
                  blueprintExecDefHash?,   // Feature 194 — the pin
                  attestation }
```

⚠ **`ComputeSignableBytes()` rebuilds the decision FIELD BY FIELD.** A property added to the record
and not to that rebuild **rides the wire unauthenticated while appearing signed** — the transaction
signature covers only `{TxId}:{PayloadHash}`, so it does not cover this. F189 lost `ValidatorEntry`
to exactly this shape. The guard is `RoutingDecisionSigningCoverageTests`, which is
**reflection-driven and fails on a property type it cannot mutate rather than skipping it**. Per-field
tests exist too and are fine, but they are a hand-written list: every one of them stays green when a
new uncovered property is added, which is the only case that matters.

### Which value is the pin, and why not the ordinal

The **executable-definition hash** (`ExecutableDefinitionHasher`, F142), never the ordinal `Version`.
The ordinal is assigned from in-memory insert order (`versions.Count + 1`) and re-derived on
recovery, so it does not reliably denote the same definition twice. F194 additionally **removed the
ordinal from the hashed projection** — it was inside the content address, which is a contradiction
and a latent way for an author to strand every in-flight instance by renumbering a draft.

Because the hasher already ignores presentational keywords (F142 `FormKeywordClassifier`), a
relabelling republish yields the **same** pin: no new definition, no instance moved, rehearsal pass
still valid.

### The lifecycle

| Stage | Behaviour |
|---|---|
| **Publish** | `PublishService.PublishAsync` takes a **deep copy** after `$ref` flattening, hashes *that*, and stores it as `PublishedBlueprint.ExecDefHash`. The copy matters: the store used to hold the live draft reference, so "immutable snapshot" was false and hashed content could change under its own hash. |
| **Instance creation** | Pins to the **latest PUBLISHED** definition — never the draft. A draft pin would name a definition no validator can resolve. |
| **Every action** | `ActionExecutionService` (and `EncryptionBackgroundService`, the encrypted-register producer) stamp the **instance's** pin, never a re-derived "latest". |
| **Fold** | `InstanceProjectionResolver` reads it off the sealed decision; `InstanceProjection.Apply` returns `FoldOutcome.RefusedForeignDefinition` for a transaction claiming a different pin. Null is accepted as the pre-feature case, counted, never treated as a mismatch. |
| **Validator** | `ResolveBlueprintAsync(blueprintId, execDefHash, ct)` at all **three** call sites. An unresolvable pin is `VAL_BP_VERSION_001` — **never a fallback to latest**, which would reintroduce the defect silently. |
| **Cache** | Two key shapes, deliberately: `…:{id}` for system/unpinned resolution (system blueprints have no instance and no pin) and `…:{id}:{hash}` for a pinned definition. Format lives once, in `Sorcha.Blueprint.Models.BlueprintCacheKey`, used by the validator's cache AND the Blueprint Service's publish path. |
| **Recovery** | `BlueprintRecoveryService` restores **every** published definition, oldest-first, deduped by content hash. It used to keep newest-per-id — under pinning that strands an instance permanently at the first restart. |

### Surfaces

- `GET /api/blueprints/{id}/definitions/{execDefHash}` — the pinned definition (404 = refusal).
- `GET /api/instances/{instanceId}/definition` — `pinState` ∈ `pinned` | `unresolvable` | `unpinned`,
  plus the hash, the version **derived from the pin**, and `isPinnedToLatest`. Nothing is guessed:
  an unresolvable pin returns null for version and `isPinnedToLatest`, because that state IS the
  diagnosis and a plausible substitute would read as healthy.
- `GET /api/blueprints/{id}/versions` entries carry `execDefHash`.
- F186 `/api/me/applications` resolves decision-notice wording from the **pinned** definition.

Metrics on `Sorcha.Blueprint.Instances`: `…instance_projection.pin_fallback{path}` and
`…instance_projection.pin_mismatch`.

### Two things that will bite

- **Deploy `validator-service` BEFORE `blueprint-service`.** New validator + old producer is safe (a
  null pin is omitted from the wire, so canonical bytes are unchanged); the reverse **refuses every
  submission**, because the old rebuild computes different signable bytes.
- **Every failure mode of this feature degrades to the OLD BEHAVIOUR, not to an error.** A cache
  re-keyed on one side only, a producer that stops stamping, a pin dropped from a copy list — all of
  them silently resolve "latest" again, with a green suite. The acceptance check is therefore the
  positive one: `pin_fallback` reading **zero** on a register created after the deploy.

Spec / plan / research: `specs/194-blueprint-version-pinning/`. Design:
`docs/superpowers/specs/2026-08-23-blueprint-version-pinning-design.md`. Issue #1559.

---

## Register governance: what signs what (Feature 189)

Getting the signing key wrong here fails **silently at the wrong layer** — the transaction is
accepted, stored, and then refused by the Validator with `submitter not found in roster`, on any
register whose genesis has sealed. Before genesis seals, `roster == null` and everything is admitted,
so the same code "works" in a fresh environment and fails in a real one. That race produced a false
PASS during this feature's own verification.

### The three keys, and which is correct where

| Slot | Context | Whose key | Use for |
|---|---|---|---|
| 100 | `sorcha:register-attestation` | the **organisation** | genesis attestations, governance control txs, approvals |
| 101 | `sorcha:register-control` | the **node** | node-internal control operations only |
| 102 | `sorcha:docket-signing` | the validator | sealing dockets |

**A register's governance roster is built from its genesis attestations, which record the
ORGANISATION's slot-100 key.** So anything the Validator authorises against the roster must be signed
at slot 100 by an organisation on it — never by the node. Route it through
`IGovernanceSigningService` (which resolves the roster subject), not `ISystemWalletSigningService`.

Both `/governance/crypto-policy` and `/governance/propose` were on the node key at different times.
The second survived a live verification of the first because that verification only exercised
crypto-policy. **When you fix a signing path, check its siblings** — the fix is per-endpoint and the
defect is per-endpoint.

### Verifying a governance change actually happened

`200` and `submitted: true` mean **accepted**, not **enacted**. A control transaction only takes
effect when it seals into a docket. Check the docket, not the response:

```bash
docker exec sorcha-mongodb mongosh -u sorcha -p sorcha_dev_password --authenticationDatabase admin   --quiet --eval 'db.getSiblingDB("sorcha_register_<id>").dockets.find({},{TransactionIds:1,_id:0}).forEach(printjson)'
# and the verdict:
docker logs --since 3m sorcha-validator-service | grep -E "validated|rejected|not found in roster"
```

**Confirm the genesis is in docket 0 BEFORE testing** any governance operation. Otherwise a pass
proves only that enforcement had not started.

### The SSR cannot stand in for an ordinary register

The system register is unique by design — its genesis is pre-signed offline and it is deliberately
outside the governance path until F189 US4. Testing governance against it can neither confirm nor
refute general behaviour. Create an ordinary register: `POST /api/registers/initiate` → sign each
attestation with `derivationPath: "sorcha:register-attestation"` → `POST /api/registers/finalize`
echoing the **whole `attestationData` object** back (the flattened shape fails with an empty name).
The window is 5 minutes, so script it.

### An approval's signature is not the transaction's signature

A governance **approval** reaches the ledger as an action submission of `register-governance-v1`
(action 2, "Collect Quorum") built by `GovernanceApprovalActionSubmitter` and put through
`IValidatorServiceClient` — never written straight to storage, which was the original US1 defect.

Two signatures are in play and they sign different things:

| | Signs | Lives in | Produced by |
|---|---|---|---|
| **Authority** | `GovernanceApprovalStatement` — register, whole operation, approver, approve/reject | the **payload** (`GovernanceApprovalActionPayload`) | the approver, outside the platform |
| **Carry** | `SHA-256("{txId}:{payloadHash}")` | `TransactionSubmission.Signatures` | this node, as a roster org (`Metadata["carriedBy"]`) |

The validator verifies every entry in `Signatures` against the transaction digest, so filing the
detached approval signature there fails `VAL_SIG_002` **every time** — and the message blames the
approver rather than the code that misfiled it. The approver signs before any transaction exists, so
they cannot sign an envelope; that is the whole reason the two are separate.

The carry deliberately does **not** sign as the approver even when the node holds their key: that
would dress a carry up as an approval — the server-side signing R-014 withdrew — and only sometimes,
which is worse than always. A node with no governance key for the register cannot carry an approval.

The payload's shape is the blueprint's action-2 `dataSchemas`, held in lockstep by
`GovernanceApprovalPayloadContractTests` (bidirectional, derived from serialisation). Before T075 the
two disagreed on nine fields, including two the schema declared on the delegation that actually live
on the authorisation — so **no conforming payload could ever have been produced**. Add a field to
either side without the other and that test fails; a hand-written list would not have caught it.

### Signatures must bind the whole operation

`GovernanceApprovalStatement` v2 binds the operation's canonical serialisation, not a field list. A
list silently stops covering a property added later: v1 left `ValidatorEntry` unbound, so an
`AddValidator` approval bound "add a validator" and **not which one** — its public key and endpoint
sat outside the digest. Harmless while the server both built and signed; a substitution vector the
moment signing moves outside it. Guard binding with a **reflection-driven** test over the type's
properties, never a hand-written list.

### Accountability is verified per node, by one implementation (T079)

Every approval carries an `authorisation` naming the individual behind it — directly, or through a
delegation they signed. **Both signatures are checked before a vote counts**: the organisation's
carries *authority*, the authorisation carries *responsibility*, and an approval that resolves to
nobody is one the register can never attribute to a decision (FR-029). An authorisation that fails
refuses the approval outright rather than counting it with the accountability discarded (FR-032) — a
record that looks complete and is not is worse than a refusal.

**The verifier lives in `Sorcha.Validator.Core`, not in either service.** Two sides need the identical
answer: the Register Service admitting a submission over HTTP, and `RightsEnforcementService`
recounting the approvals sealed against a proposal on **every** node — including nodes that never saw
the submission. Until T079 only the first ran the check, so an approval whose authorisation was absent
or forged counted everywhere except its point of entry. Two implementations of one rule is how the two
would come to disagree about whether a governance change is authorised.

**The authorisation is attestation metadata, never a roster claim.** It is deliberately not matched
against the roster — the individual behind an organisation's approval is not a member of the
register's governance. Matching it would either reject every valid approval as "not on roster" or,
worse, let an individual's signature stand in for their organisation's.

**Uncheckable fails closed.** A validator with no `IDetachedApprovalVerifier` counts no approval
carrying an authorisation, matching how the same class treats an unreadable proposal. Every refusal is
logged with a reason: an approval that stops counting silently looks to an operator exactly like one
that was never submitted — the shape that let a swallowed deserialisation count zero approvals for a
whole live run.

**One code path, no conversion.** `GovernanceAuthorisationValidator.Validate` and
`IDetachedApprovalVerifier` each take `(approverDid, isApproval, authorisation)` as well as a whole
submission, so a `GovernanceApprovalSubmission` and a sealed `GovernanceApprovalActionPayload` are
validated by the same code with **no** mapping between them. A hand-maintained mapping is exactly what
dropped `ValidatorEntry` on `/propose` and made `AddValidator` unusable without anything failing.

### A proposal's status is derived, never stored (T043/T046)

`GET /api/registers/{id}/governance/proposals?status=Open|Enacted|Invalidated|Expired|All` and
`GET .../proposals/{proposalId}` are the audit surface. Every field on them comes from sealed content:
status from `GovernanceProposalStatus.Derive` (a pure function in `Sorcha.Register.Models`, so the
Validator, the CLI and a console cannot each derive a different answer), counting from
`GovernanceApprovalTally`, arithmetic from `ValidateQuorumAsync`.

**Precedence is load-bearing and mutation-verified.** `Enacted` outranks everything: an enactment *is*
a roster change, so ordering invalidation first reports every enacted proposal as `Invalidated`, and
ordering expiry first makes one silently re-read as `Expired` once its window passes. Both orderings
look correct for exactly as long as anyone tests inside the window. `Invalidated` is reported ahead of
`Expired` because it refuses an approval even inside the window. An unset `ExpiresAt` must be guarded
with `!= default` — treating it as a date at the epoch reports *every* proposal as expired.

**Two drafted reasons do not exist, deliberately.** `withdrawn` has no producer anywhere — no
endpoint, no transaction type — so a status for it would name a state no ledger can reach.
`refused-not-on-roster` is why an individual *approval* did not count, not what happened to the
proposal; it is reported per-approval in `excludedApprovals[]`.

**An enactment is the OUTCOME of a proposal, not a proposal.** It carries the same
`transactionType=GovernanceOperation` tracking value as the proposal it settles, so without an
explicit exclusion it is listed as a proposal in its own right — the same governance change appearing
twice, the second row showing no approvals because approvals chain off the proposal, not the
enactment. **The discriminator is naming ANOTHER proposal (`EnactsProposalId`), not carrying a
roster**: an Owner-override propose-and-enact is one transaction that is both and carries a roster
too, so excluding everything with a roster would drop single-owner governance from the audit surface
entirely. Found live on n1; no test saw it.

**The endpoint this replaced read `MetaData.TrackingData`.** That sits outside the signature, outside
the payload hash and outside the docket's merkle leaf, so anyone able to submit can rewrite it with
nothing detecting the change. An audit surface sourced from forgeable fields is worse than none,
because it looks authoritative. Read the signed payload.

**The Register Service configures no JSON options**, so its minimal APIs use the web defaults — under
which an enum goes on the wire as a **number**. `"status": 1` is unreadable by a typed client (it
throws) and matches no filter written against `Enacted`. Pin enums with `[JsonConverter]` on the type
rather than relying on host registration, and write wire-contract tests against
`JsonSerializerOptions.Web`, not `SorchaJson.Options` — asserting against options the service does not
use is a test that passes while the surface is broken.

### The roster diff an approver reads is the one the enactment writes (T084)

`GET .../proposals/{id}` carries `rosterDiff` — the roster member by member as it would be, each row
marked `Unchanged` / `Added` / `Removed` / `RoleChanged`. A departing member stays in the list marked
`Removed` rather than vanishing, because a row that disappears is a change the reader has to notice by
its absence.

It is computed by **`GovernanceEnactmentService.ProjectRoster`, the same call that builds the
enactment payload**. A console deriving its own preview would eventually show an approver an
accurate-looking change that differs from the one that happens — FR-027 defeated more quietly than by
showing a JSON blob. That is also why the diff is computed server-side at all: `ApplyOperation` lives
in `Sorcha.Register.Core`, which a Blazor client cannot reference, and re-implementing it client-side
is exactly the drift to avoid.

`rosterDiff` is **null** in three cases, each deliberate: an operation that changes no membership (an
all-unchanged list says something untrue about the change); an **Invalidated** proposal (projecting it
onto the current roster describes a change that can never happen); and an **Enacted** one (the sealed
enactment is the record, and re-projecting would apply an applied change twice). The status carries
the explanation instead.

### `authMethod` is recorded, never enforced — and it means key custody

`ApprovalAuthMethod` (`software` / `hardware-backed` / `service` / `unknown`) records **how the
approving key was held**, so a register *can* later require a minimum standard per operation.
Enforcing one before organisations have hardware-backed governance keys provisioned would lock them
out of their own registers (R-016).

The fact is already sealed, on each approval transaction's payload. It is **not** added to the
enactment: `ControlTransactionPayload` carries only `version` / `roster` / `operation` /
`enactsProposalId`, and duplicating evidence already on the ledger buys nothing. What T081 added is
carrying it onto the counted vote — `ApprovalTallyCheck` → `ApprovalSignature.AuthMethod` — where a
future policy gate would sit. `ApprovalTallyCheck` carries the `Authorisation` too, so the caller
never has to keep a second list aligned with it by approver.

**`ApprovalSignature.AuthMethod` used to mean something else.** It carried `passkey` / `totp` /
`password` / `re-oauth` — how a *person authenticated* — written by the in-platform approval path that
R-014 replaced with external signing. That path had no callers left, and `GovernanceApprovalService`
is now deleted rather than left registered: one field carrying two vocabularies is a fact no consumer
can interpret. `ToVotes` derives the token from the payload converter's own naming policy, so the vote
and the transaction it came from cannot drift to two spellings of one fact.

### The Control discriminator buys six exemptions, and two of them hold quorum up (T054)

`Metadata["Type"] = "Control"` is not one flag. It is read in six places, and reaching for it —
or removing it — moves all six at once:

| What it waives | Where |
|---|---|
| Per-sender sequence replay (`VAL_REPLAY_001`) | `ValidationEngine` via `IsGenesisOrControlTransaction` |
| Action-schema validation | same |
| Blueprint conformance in full (`VAL_BP_001/002/003`) | same |
| Routing-decision attestation (`VAL_ROUTING_001/002`) | same |
| Crypto policy (`VAL_POLICY_*`) | **a separate inline comparison** at `ValidationEngine.cs:831` |
| Fork detection | indirectly — `DocketRegisterProjection.ResolveTransactionType` maps it onto the persisted `TransactionType`, and the fork bypass keys on the *predecessor's* |

The crypto-policy arm does **not** route through `TransactionTypeClassifier`. A change made only in
the classifier leaves it behind, silently and in the permissive direction.

**Two of the six are load-bearing for quorum, so "make governance ordinary actions" is not
available.** Every approval sets `PreviousTransactionId` to the proposal, so N approvals are N
children of one parent — a star, which only the fork bypass permits. And every approval is action 2
sent by participant `voter`, which `VAL_BP_002` Tier 3 (`ResolveChainBoundWalletAsync`) binds
immutably to the *earliest* in-instance transaction for that role. Withdraw either and the **second**
organisation's approval is refused, by two independent routes, and quorum can never be reached.
These are deliberate invariants of the workflow model — one successor per transaction, one wallet per
role per instance. Quorum is many signers on one step; an action chain is a line.

**The roster check never rode on this flag.** `RightsEnforcementService.IsGovernanceTransaction`
tries `BlueprintId == GovernanceBlueprint.BlueprintId` *first*, and all four producers set it from
the same shared constant. The `Control` arm is a redundant second path left from R-004, when a
proposal still carried an empty `BlueprintId`.

**⚠ Withdrawing the schema exemption needed a resolver, and the blocker was not the one you would guess.**
T054 attempted exactly that — a predicate naming actions 1/2/4, and `ValidateSchemaAsync` running
`IsGenesisOrControl && !IsGovernanceAction`. It was **reverted after failing the live gate on n1**
(2026-08-09): every governance proposal returned 202 and then never sealed, with
`VAL_SCHEMA_001: Blueprint 'register-governance-v1' not found`.

**`ResolveBlueprintAsync` is global by id — cache, then Blueprint Service — but the governance
blueprint is in neither.** It is seeded onto the **system register** by `SystemRegisterBootstrapper`
and exists only as a publish transaction there; there is no `blueprint."Blueprints"` row for it, and
`BlueprintRecoveryService` deliberately **rejects** these system blueprints with `no_provenance`. So
"the resolver is global, therefore the SSR copy resolves" is an inference that does not hold — the
mechanism is global, the blueprint is absent. Enforcing the contract fails **every governance
transaction on every register**.

**Fixed:** `ResolveBlueprintAsync` gained a last-resort arm reading the **system register's ledger**
via `IRegisterServiceClient.GetSystemRegisterBlueprintJsonAsync`. It works on any node holding an SSR
replica, is tried last, and returns null (never throws) so an unreachable Register Service fails
closed. Enforcement is live-proven on n1. (`ControlBlueprintVersionResolver` is **not** the right
tool — control *config* blueprints, returns `ResolvedControlBlueprintVersion`, not a `BlueprintModel`.)

⚠ **A node's SSR is as old as its genesis.** n1's copy predated T053 and declared **no `dataSchemas`**,
and FR-006 skips validation when an action declares none — so the live gate would have gone green
while checking nothing. Publish the version under test to the SSR first
(`POST /api/system-register/publish`; the newest by timestamp wins) and verify it landed.

Note for when it is built: a predicate used to **withdraw** an exemption may safely key on unsigned
fields (`BlueprintId`/`ActionId`/metadata) — forging it true buys more validation, forging it false
leaves the forger facing the roster check as before. That reasoning **inverts** the moment it grants
anything, so move the discriminator into the signed payload first, as C-VAL did for the lifecycle
predicates.

### The action-1 contract described a payload nothing emits

Action 1's `dataSchemas` declared a bare `GovernanceOperation` — `operationType`, `proposerDid`,
`proposedAt`, `rosterSnapshotId`, `quorumFormulaAtRaise` at the top level. Every producer emits a
`ControlTransactionPayload` envelope (`version` / `roster` / `operation` / `enactsProposalId`) with
those fields nested under `operation`. **No conforming payload could ever have existed**, and nothing
noticed because the contract went unenforced behind the Control exemption. It also declared
`requiresAcceptance`, which no model can produce, and omitted `approvalSignatures` and `status`,
which the model emits.

The envelope is not negotiable — the roster travels on it, `GovernanceProposalStatus.Derive` reads
`enactsProposalId` from it, and `RightsEnforcementService` rebuilds approval statements from the
operation stored inside it — so the schema is what was wrong.

`GovernanceControlPayloadContractTests` guards it, mirroring the action-2 test. Three things worth
copying:

- **Derived from serialisation, bidirectional.** A hand-written field list rots in the same
  direction as the bug.
- **Structural agreement is not evaluation.** The test also runs the payload through JsonSchema.Net
  with the Validator's own `x-`-keyword strip and `RequireFormatValidation: true`. Without the strip
  the shipped schema does not parse at all — *"Unknown keywords (x-enumNames) are disallowed for this
  dialect"* — so a test that skipped it would fail for a reason production never hits.
- **The enum wire value depends on where the converter is declared.** Most of these enums carry
  `JsonStringEnumConverter` on the **property**, so serialising a bare value under the payload's own
  options yields the *integer*. Compare against `Enum.GetNames` and separately assert what the
  producer actually emits.

Action 4 (enactment) declares no `dataSchemas` and is skipped by FR-006. Giving it one means
restating `RegisterControlRecord`, which has its own contract.

**Open: the governance blueprint's routes are inert.** Action 1's conditions read `ownerOverride`,
`requiresAcceptance` and `quorumMet`; action 2's read `quorumMet`; action 3's read `accepted`. No
producer emits any of them, and governance transactions are exempt from `VAL_ROUTING_*`. The routes
are a design sketch, not a definition anything executes — which is what T057's "diff the sequence
against the published blueprint" has to confront.

### Governance is a star; the instance model is a line (T055)

**Do not give a governance transaction an `instanceId` to make it fold.** It is the obvious
one-line change, it looks right, and it is wrong silently — `GovernanceIsNotInstanceScopedTests`
executes the reason rather than describing it.

Governance transactions are exempt from `VAL_ROUTING_*` and therefore carry **no
`RoutingDecision`**, and `InstanceProjectionResolver` treats a transaction without one as
contributing a **terminal (empty) next-action set**. So a proposal given an instance id folds to
`InstanceState.Completed` **the moment the change is raised** — before any approval, before the
enactment that changes the roster. Nothing errors. Same shape as the latent defect F145 US6 found
for presentation lifecycle.

And the projection cannot represent the shape anyway: `InstanceProjection.OrderByChain` builds a
`Dictionary` keyed by predecessor id — **one successor per predecessor** — so sibling approvals
overwrite one another and the losers fall out to the straggler path. Folding the same set in two
orders yields different watermarks, which is exactly the determinism guarantee F145 makes.

That is the **third** independent place the platform's instance model is linear by construction:

1. Fork detection — one successor per transaction (bypassed only for Control predecessors).
2. `VAL_BP_002` Tier 3 — one wallet per participant role per instance, bound by the earliest tx.
3. `InstanceProjection.OrderByChain` — one successor per predecessor.

Quorum is many signers on one step. Nothing short of changing all three makes it an instance, and
all three exist to protect every other workflow.

**R-009 is met by the tally being pure, not by folding.** `GovernanceApprovalTally.Prepare` takes
only the register id, the operation stored on the proposal, the roster it was raised against, and
the sealed approvals — pinned by reflection, so a well-meant extra parameter (a clock, node
identity, config) fails the build. Order-independence is pinned over **all six** orderings of three
approvals; the one deliberate order-dependence, first-vote-wins on a duplicate approver, is pinned
separately so it cannot drift to last-wins. Seal order is the same on every node because the docket
fixes it.

⚠ **`RegisterRole.Auditor` is non-voting.** A tally fixture built on the shared roster silently
counts two approvals while appearing to count three. Assert the expected count before comparing, or
the test passes vacuously.

### A roster member added by governance has NO KEY, and promoting one bricks the register

`Add` puts a subject on the roster with `PublicKey = string.Empty` — both writers do
(`GovernanceEnactmentService.ProjectRoster` and the Owner-override path in `Program.cs`). The
comment says *"recorded when they first attest"*. **Nothing ever attests.** The only writers of a
real attestation key in `src/` are the CLI genesis ceremony, `RegisterCreationOrchestrator` (genesis,
from signed attestations), and `ApplyOperation`'s Transfer arm copying an existing one;
`GovernanceProposalRequest` carries no target key.

Roster authority is matched **by public key** (`RightsEnforcementService` →
`GovernanceKeyMatcher.Matches`, and `TryDecode("")` is false), so an added member can neither sign a
governance transaction nor cast a counting approval — its votes are excluded as
`KeyNotTheRosterKey`. And `ApplyOperation`'s Transfer arm promotes the target **carrying its empty
key**, so transferring ownership to an added member leaves a register whose Owner nobody can act as.
`SelectSigner` prefers the Owner and `/propose` passes `preferredSubject: null`, so there is no route
around it — the register is permanently ungovernable.

**The failure is silent at the HTTP layer.** Live on n1 (2026-08-17): the promoted Owner's next
proposal returned **200** and never sealed — `VAL_PERM_002: none of 1 signature(s) match a roster
member`. Only the docket and the validator log show it. Issue **#1464**; it is why F189 US4's
acceptance test cannot be performed on the system register (**#1400**).

### The system register is proposable but NOT approvable

Its roster has exactly one member — the ceremony Owner, `did:sorcha:genesis:…`, whose key is the
node's `validator:*` system wallet. US4 (#1396) made that subject able to **sign a control
transaction** server-side. It cannot **approve** one: `Transfer` is excluded from the Owner override
(FR-010) so it needs quorum — which on a one-member roster is that member's own approval — but
approvals are detached and produced **outside** the platform by design (R-014), and
`/api/v1/wallets/{address}/sign` correctly refuses `validator:*` wallets to everything except the
`validator-service` / `register-service` principals (#1397/#1424). Issue **#1465**.

⚠ **Do not "unblock" this by minting a service token to sign the system wallet from a harness.** That
is the #1397 oracle wearing a different hat, and it proves nothing about the governance surface.

### A governance change must not rewrite the rule it is judged by

`ApplyOperation` rebuilt `RegisterControlRecord` with an object initializer naming **six of its ten**
properties, silently dropping `CryptoPolicy`, `RegisterPolicy`, `RoutingAttestation` and
`Validators` on every enacted Add/Remove/Transfer — and `GetCurrentRosterAsync` takes the newest
roster-bearing payload **wholesale**, so they did not come back.

Nothing failed, because both readers of the validator roster (`RegisterLocalRelationshipService` and
the peer `ValidatorKeyCache`) resolve it from the **genesis** docket. What moved was governance
itself: `ValidateQuorumAsync` reads the quorum formula from `RegisterPolicy.Governance`, so **a
register configured for `Unanimous` reverted to the `StrictMajority` default on its first governance
change** — three-of-three quietly becoming two-of-three. It also discarded its own caller's work,
since `ProjectRoster` applies a validator-roster change and then hands the record straight to it.

Fixed in PR #1463 via `RegisterControlRecord.ShallowCopy()` — **clone, never re-list**, so the next
property added to the type is carried forward on the day it is added. Guarded by
`ApplyOperationPreservesRegisterConfigurationTests`, which asserts **by reflection**. Registers that
already enacted a change keep the truncated roster; the ledger is immutable.

### A node's seeded system blueprints are never updated

`SeedBlueprintsIfMissingAsync` skips a blueprint that already **exists**, so redeploying does not
refresh it: the image's catalogue at `/app/blueprints/templates/{id}.json` and the SSR's published
copy drift apart with nothing checking. n1 served the pre-T054 `register-governance-v1` from its
2026-08-07 genesis until 2026-08-17, and once SSR-ledger blueprint resolution went live that refused
**every governance proposal on every register** with `VAL_SCHEMA_004` — behind a `202 Accepted`.

**Republishing alone is not enough** — `BlueprintCache` is Redis-backed with an in-process L1. The
remedy is three steps: `POST /api/system-register/publish` with the image's own catalogue body
(extract via `docker cp`; the containers are chiseled and have no shell), then
`redis-cli DEL sorcha:validator:blueprint:{id}`, then recreate the validator. Issue **#1466**.

### ⚠ R-006 — an approval proves custody, not organisational intent. NOT SOLVED.

**Do not describe register governance as proving that an organisation approved something.** An
organisation's governance key is its slot-100 key, derived from a wallet in the platform's own
custody, so any principal able to call the Wallet Service's signing endpoint for that wallet can
produce that organisation's approval. A sealed approval proves *the node was asked to use the key*.
Worst under `Unanimous`, where one privileged principal satisfies a whole consortium.

**The live gates are themselves the demonstration.** T048 and T049 produced all three organisations'
approvals — and their accountability blocks — from a single `admin@sorcha.local` token, because that
is all the platform requires. The governance mechanics under test are sound; what the signatures
attest is narrower than it reads.

F189 narrowed the surface without closing it: signing left the server's automatic path (R-014), every
counted approval carries an individual accountability block (FR-029), and both signatures are
re-verified on every node (FR-032). But the individual's key is custodied identically, so the block
records *who was named*, not who consented. Closing it needs the organisation's governance key held
outside the platform — the open key-custody question behind T083. Issue **#1380**; stated in
`docs/security-model.md` → Honest Gaps.

### Delegated approvals are refused — the grant path does not exist (T095)

`AuthorisationKind.Delegated` fails closed with `AuthorisationRefusalReason.DelegationNotAvailable`
(**501**, not 422 — the submission may be perfectly well-formed). Nothing on the platform can
**grant** a delegation: `GovernanceDelegation` is carried by a submission and verified by the
validator, and issued by nothing.

**"No granting path" did not mean unreachable, and that gap was the hole.** A hand-crafted submission
needs no UI. The delegated path's cryptography is sound — the grant must be signed by a key genuinely
belonging to the individual named as granting it, it empowers exactly one approver key, and scope,
expiry and revocation are enforced. What nothing checks is whether that individual had any
**authority** to grant: no roster check, no Owner check, not even organisational affiliation. It does
not escalate authority (the org signature is still required, so it is bounded by R-006 above) but it
degrades the accountability record to a self-assertion, which is what FR-029 exists to prevent.

**The verification code is kept, not deleted** — it is correct, covered, and a real granting path
needs exactly it. The gate is a defaulted `allowDelegated` parameter on the shared
`GovernanceAuthorisationValidator.Validate` rather than a constant, so the delegation tests keep
running and opt in explicitly. Production reaches the validator through **one** call site
(`DetachedApprovalVerifier`) which never passes it. **Lift the refusal in the same change that adds
granting — flip the default, don't spread the argument through callers.**

⚠ **T096 (interactive signing windows) is closed as not applicable — its premise is wrong.** Nothing
expires mid-review. The 5 minutes is `RegisterCreationOrchestrator._pendingExpirationTime`, which
bounds register *creation*; a signing request's `ExpiresAt` is the **proposal's** expiry, 7 days by
default. There is no window to extend, and it never blocked T083.

### Where each quorum rule is actually enforced (T033-T038)

Three of the six US2 properties are enforced somewhere other than the obvious place, and a test
written against the obvious place passes while proving nothing.

**`QuorumFormulaAtRaise` is recorded, never read.** `ValidateQuorumAsync` takes the formula from
`controlRecord.RegisterPolicy?.Governance?.QuorumFormula` on the **current** roster — setting
`operation.QuorumFormulaAtRaise = Unanimous` in a fixture is decorative and changes no arithmetic.
That is not the drift it looks like: a policy change is itself a control transaction, so it moves the
roster head and FR-011b invalidates every open proposal underneath it. The formula therefore cannot
change beneath a live proposal, which is why reading it from the current roster is safe.

**Expiry dies at the Validator, not at the enactment gate.** `GovernanceEnactmentService.TryEnactAsync`
checks the roster snapshot but deliberately checks **no window**; the refusal comes from
`GovernanceRosterService.ValidateProposal` → `VAL_PERM_004` inside `RightsEnforcementService`. The
approve endpoint's `409 expired` only stops a late approval being *raised on that node*, and a
transaction can reach a validator without passing through any particular node's endpoint. Test the
Validator, and route `ValidateProposal` through the **real** rule — a mock stubbed to
`Failure("Proposal has expired")` asserts only that the test can return its own string, and survives
the rule being deleted.

**The tally is handed the CURRENT roster, and that is correct.**
`GovernanceEnactmentService.CollectStructurallyEligibleVotesAsync` passes
`GetCurrentRosterAsync(...).ControlRecord` into `GovernanceApprovalTally.Prepare`, whose parameter is
documented as "the roster the proposal was raised against". Not a bug: the FR-011b comparison earlier
in `TryEnactAsync` returns `NotEnactable` unless snapshot == head, so by the time the tally runs the
two are provably the same object-shape. Do not "fix" this by threading a separate snapshot through —
the guard is what makes them identical, and a second roster parameter would create a way for them to
differ.

⚠ **SC-010's test was green and proved half of what it claimed.**
`RemovingTheLastOutstandingApprover_InvalidatesRatherThanEnacts` asserted the guard fired
(`VAL_PERM_009`) and that `ValidateQuorumAsync` was never reached — but stated the *attack* only in a
comment. Nothing executed the counterfactual, so the test would have passed identically had the attack
never been available: a green tick asserting a known attack is defended, when nothing checked it was
ever possible. The fix is to run the arithmetic the guard prevented —
`before.GetQuorumThreshold(formula: Unanimous)` is 2 against 1 collected approval, `after` falls to
`<= collected` — so removal genuinely converts a blocked change into an approved one and the guard is
demonstrably load-bearing. **Any "the check prevents X" test needs X executed, not described.**

Mutation-test these specifically: flooring `Unanimous` at 2 must red **only** the counterfactual
assertion, and removing the `Transfer` carve-out must red the sole-owner Transfer test while its
`Add` sibling stays green. A `Transfer` test built on a two-member roster cannot separate "the
override was withheld" from "there were not enough votes" — the sole-owner roster is the only shape
where the override is the sole thing between zero approvals and a register changing hands.
