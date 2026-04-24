---
name: sorcha-architecture
description: |
  Sorcha feature-specific API references, domain models, and cross-cutting architectural patterns that don't belong in service-level skills.
  Use when: working on or extending any of the features documented below (Participant Identity, Register Invitations, Trust Hardening, Stored Data / file attachments, Validator Roster, Org Key Derivation, Platform Org Topology, Consumer Persona, System Register Genesis, Open Participants / late binding, x-review / credential id-cards, ownership-agnostic submission / derived relationship). Also use when you need a concise catalogue of well-known IDs, DID shapes, or the endpoint surface for these features.
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
