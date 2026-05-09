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
#   genesis-validator-key.json    → import into first validator, then destroy

# Verify a genesis file
sorcha system-register verify path/to/system-register-genesis.json

# Import validator key into running Wallet Service (first validator only)
sorcha system-register import-validator-key --key genesis-validator-key.json
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

Runtime source: `ValidationEngine.cs:1027` (validator skips strict wallet check for starting actions), `ActionExecutionService.cs:196-216` (strict check fires only when `WalletAddress` non-null), `ActionExecutionService.cs:309-332` (late-bind block, persisted via `IInstanceStore.UpdateAsync`). Authoritative documentation: `.claude/skills/blueprint-builder/SKILL.md` → "Open Participants & Late Binding" section. Feature design: `specs/103-verified-citizen-v2/`.

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

End-to-end working wallet ecosystem. Twelve PRs landed 2026-04-26 (#427-#438). Wallet PWA (`Sorcha.Citizen.Wallet`, Blazor WASM) and reference verifier (`Sorcha.Citizen.Verifier`, Blazor Server) are real projects in `src/Apps/`. The flow: sign in via Settings → Enrol device → credentials sync → present with full holder→device chain + issuer-signature verification → wallet auto-renews delegation 30 days before expiry. Demo-mint bridge (`/verify/demo/mint`) generates per-mint issuer keys for the demo until US4 ships real credential issuance.

### Endpoints

#### Wallet Service — public (citizen JWT, audience `sorcha:citizen-wallet`)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/v1/wallet/devices/enrol` | Enrol a device — derives holder key (slot 108), issues device-delegation SD-JWT VC, allocates status-list slot, registers device with Tenant Service. Strict rate limit. |
| POST | `/api/v1/wallet/devices/renew-delegation` | Idempotent re-issuance of holder→device delegation, signed by holder key. Wallets call when within 30 days of expiry. 404 on cross-user / unknown / revoked device. (PR #435) |
| GET | `/api/v1/wallet/credentials` | Full credential snapshot for fresh-wallet seeding. (PR #428) |
| GET | `/api/v1/wallet/sync?since={cursor}` | Incremental delta. Cursor older than 30 days → 410 Gone (wallet falls back to /credentials). (PR #428) |
| DELETE | `/api/v1/wallet/devices/{deviceId}` | Citizen-initiated revoke from the PWA. Looks up `(listId, idx)` on Tenant via `IPlatformUserDeviceClient.GetByIdAsync`, calls `IDeviceRevocationService.RevokeAsync` (status-list flip + SignalR `DeviceRevoked`), then `IPlatformUserDeviceClient.RevokeAsync` to flip the Tenant row. 404 indistinguishable from non-existence. |
| GET | `/api/v1/wallet/devices` | List the citizen's enrolled devices (active + revoked, ordered by enrolment desc). Proxied through Tenant via `IPlatformUserDeviceClient.ListAsync`. |
| PUT | `/api/v1/wallet/devices/{deviceId}/label` | Rename. Validates label length 1..120. 404 on cross-user mismatch. |

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
- **`IIssuerKeyResolver`** (verifier) + `OptOutIssuerKeyResolver` (default — preserves v1 contract) + `JwkRegistryIssuerKeyResolver` (in-memory, used by tests + demo-mint to register per-mint issuer keys). `VerifiablePresentationValidator` step 4b verifies credential JWS when key resolved; `RequireIssuerSignature: true` flips to fail-closed when no resolver hit (PR #434). Production hardening swaps in a DID-resolver-backed impl that reads tenant register verification methods.

### Derivation slots (`SorchaDerivationPaths`)

| Slot | Context | Path | Purpose |
|------|---------|------|---------|
| 108 | `sorcha:citizen-holder` | `m/44'/0'/0'/0/108` | Per-citizen holder identity. Issuers bind credentials to this key via `cnf`; signs device delegation credentials. |
| 109 | `sorcha:citizen-status-signing` | `m/44'/0'/0'/0/109` | Per-org status-list signing key. One pinnable kid per org. |

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
| `src/Apps/Sorcha.Citizen.Wallet/` | Blazor WASM PWA (mounted at `/wallet/` via gateway `PathRemovePrefix`). Pages: Index/Enrol/Present/CredentialDetail/Settings/Devices/Activity. Components: ConsentSheet/CredentialPickerDialog/NoMatchingCredentialDialog. wwwroot/js: webcrypto-bridge.js, indexeddb-bridge.js. |
| `src/Apps/Sorcha.Citizen.Wallet/Services/` | All PWA services: WebCryptoDeviceKeyService, IndexedDb{Credential,Delegation,StatusList,DeviceMeta,SyncCursor,AccessToken}Store, AuthService, EnrolmentService, SyncService, DelegationRenewalClient, BearerTokenHandler, ServerClockHandler, ServerClockObserver, **CitizenWalletHubConnection** (US4 — singleton SignalR client at `/hubs/wallet`, subscribes to `CredentialAvailable` + `DeviceRevoked`) |
| `src/Apps/Sorcha.Citizen.Wallet/Pages/Index.razor` | Home page; subscribes to `CitizenWalletHubConnection` events in `OnInitializedAsync`, fires `SyncNowAsync` / `RenewIfDueAsync`, `IAsyncDisposable` detach |
| `src/Apps/Sorcha.Citizen.Wallet/wwwroot/service-worker.published.js` | US4 — handles Background Sync `sync` events tagged `citizen-credential-sync` to replay `/sync` while document is hidden |
| `src/Apps/Sorcha.Citizen.Verifier/` | Blazor Server reference verifier (mounted at `/verify/`). Pages: Index/VerifierSession/Outcome. Endpoints: PresentationResponseEndpoints, DemoMintEndpoint. |
| `src/Apps/Sorcha.Citizen.Verifier/Services/IIssuerKeyResolver.cs` | Issuer-signature verification seam — OptOut + JwkRegistry impls |
| `specs/114-citizen-wallet-pwa/` | Spec, plan, contracts, data model, tasks |
| `docs/superpowers/specs/2026-04-26-citizen-wallet-pwa-design.md` | Brainstorm design doc |

### Test patterns specific to Feature 114

- **`TestCitizenWalletDbContext`** (`tests/Sorcha.Wallet.Service.Tests/Services/`) — inherits `WalletDbContext` and `modelBuilder.Ignore<T>()`s every base entity (jsonb columns are Npgsql-only). Reuse this pattern for any future Wallet Service citizen-only tests using the InMemory provider.
- **`HolderKeyService` mock gotcha** — when mocking `IKeyManagementService.DeriveKeyAtPathAsync`, return a fresh `byte[].Clone()` each call. The publisher zeroises the private key after signing; a shared array reference is wiped between invocations.
- **Endpoint handler tests** — follow the established reflection-based static-handler invocation pattern (`PresentationEndpointTests`, `CitizenWalletEnrolEndpointTests`). No `WebApplicationFactory` needed for handler-level coverage.
- **PWA service tests** — extract IJSRuntime-touching state behind small interfaces (e.g. `ISyncCursorStore`, `IDeviceMetaStore`, `IAccessTokenStore`) so unit tests use the in-memory variant and never mock IJSRuntime directly. Each interface ships with both impls in the same file. Mocking IJSRuntime generic InvokeAsync<T> is brittle — avoid.

### Sync + renewal flow (cross-cutting)

The PWA's two background loops on every Home load:

1. **Sync** — `SyncService.SyncAsync` reads cursor from `ISyncCursorStore`, calls `/sync?since=`, applies adds/revokes/replacements to `ICredentialCache`, persists new cursor, updates `IDelegationStore` if server piggybacked a renewal. On 410 → `ListCredentialsAsync` snapshot fallback then fresh `/sync` to bootstrap a usable cursor.
2. **Delegation renewal** — `DelegationRenewalClient.RenewIfDueAsync` reads `IDeviceMetaStore`, checks `DelegationExpiresAt - now > 30 days`. If due, calls `/devices/renew-delegation`, persists fresh JWT + refreshed expiry. Five outcomes: `NotEnrolled`, `NotDue`, `Renewed`, `DeviceNotFound` (server 404 → device revoked elsewhere), `Failed`.

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
| `null` | `false` (v1 default) | Log warning; accept on holder→device chain alone. |
| `null` | `true` (production hardening) | Reject with "RequireIssuerSignature is enabled". |

The seam is shipped (PR #434); production hardening swaps `OptOutIssuerKeyResolver` for a DID-based resolver that reads tenant register verification methods and resolves `did:sorcha:org:...` to the issuer's signing key. The demo flow uses `JwkRegistryIssuerKeyResolver` — `DemoMintEndpoint` registers each freshly-generated issuer JWK on every mint so subsequent presentations of that credential pass full signature verification.

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
