# Feature 106 — Register-native credential delivery

**Date:** 2026-04-14
**Status:** Draft, brainstorm complete, ready for implementation plan
**Owner:** Sorcha Contributors
**Related:** Feature 103 (Verified Citizen v2), Feature 104 (wave 14b credential claim card), Feature 085 (stored data transactions), Feature 079 (trust hardening / disclosures)

## 1. Summary

Replace the Feature 104 wave 14b three-action HAIP OpenID4VCI pre-auth claim pattern with a **register-native** credential delivery model. Credentials issued to on-platform wallets are **sealed into the issuing action's transaction** as recipient-encrypted disclosures, peer-replicated via the register, picked up by the holder's Wallet Service through the existing bloom-filter notification path, and surfaced to the holder as **pending credentials awaiting explicit acceptance or rejection**.

The three-action blueprint shape (submit → review → claim) stays, but the claim action becomes a **symbolic terminal action** that exists only to anchor the engine's rejection protocol and close the instance state machine. The OpenID4VCI pre-auth code dance is retained for the **external wallet** case (`HaipExternalWallet`) but is no longer the on-platform default.

## 2. Goals

1. **Cross-node correctness by default.** Every layer of the design must work when issuer and holder live on different Blueprint Service instances sharing a peer-replicated register. Single-node is only a demo shape.
2. **Holder control.** Credentials never land silently in the active credential store. Every inbound credential passes through a `PendingAcceptance` state the holder must explicitly accept or reject.
3. **Auditable closure.** Both accept and reject are signed transactions sealed to the register so the issuer's blueprint instance closes cleanly with the correct terminal state.
4. **Reuse existing primitives.** No new cryptographic machinery, no new notification channel, no new protocol. The design stands on `EncryptionPipelineService` + `TransactionLifecycleEventBridge` + `NotificationDeliveryService` + `CredentialEntity` + the blueprint engine's `RejectionConfig`.

## 3. Non-goals

- **External HAIP wallets.** The `HaipExternalWallet` target audience on `credentialIssuanceConfig` stays exactly as it is today — external wallets scanning a QR + OpenID4VCI pre-auth flow continue to work for mobile phones and federated wallet apps. Feature 106 adds a *second* target audience for on-platform holders, it does not replace the first.
- **Reworking presentations.** OpenID4VP verification and SD-JWT KB-JWT presentation are out of scope. This feature covers issuance delivery only.
- **New credential formats.** The credential itself is still an SD-JWT VC minted by `HaipCredentialMinter` (or its replacement). Only the *delivery envelope* changes.
- **Auto-expiry of unaccepted credentials at the workflow level.** If the issued credential has a `notValidAfter` embedded, it expires as credentials always do. No new blueprint-level TTL for "holder hasn't responded yet".

## 4. Problem statement

Wave 14b shipped a three-action pattern:

```
Action 1: Citizen submits identity details (open, late-bound to citizen wallet)
Action 2: Assessor reviews + mints HAIP credential offer (OpenID4VCI pre-auth)
          → Route.OutputMapping carries /haip/* fields into Action 3 pending payload
Action 3: Citizen claims credential via CredentialClaimCard in MyActions
          → HaipLocalReceiveService redeems the pre-auth code against HAIP service
          → Credential lands in local wallet store
```

This pattern breaks in two distinct ways:

### 4.1 Cross-node correctness

The claim card lives in the **issuer's** Blueprint Service local instance state. `GetPendingActionsByWalletAsync` queries the local `Instances` table, which is **not** replicated across nodes — only the register is. On a federated deployment where the citizen's Wallet Service runs on node B and the issuer's Blueprint Service runs on node A, the citizen's browser connected to node B sees:

- `MyActions` empty (their local Blueprint Service has no instance for them)
- `MyCredentials` empty (nothing to claim)
- No route to recover the credential at all, because the pre-auth bearer code lives in the issuer's HAIP service memory

The only way to make wave 14b work cross-node is to give the holder an RPC channel into the issuer's node, or to peer-replicate instance state, or to push the pre-auth code out-of-band to the holder's device. All three break the "register is the only cross-node channel" invariant.

### 4.2 OpenID4VCI is the wrong tool for on-platform flows

OpenID4VCI's pre-auth code flow is designed for the case where:
- The holder is on an unknown external device at mint time
- The issuer doesn't have the holder's wallet pubkey ahead of time
- The delivery channel is a short-lived bearer token scanned via QR

None of these apply when the holder and issuer are both on the Sorcha platform:
- The holder's wallet is bound to the instance via late-binding before Action 2 runs
- The engine *already knows* the holder's wallet pubkey from the open-participant binding
- The register *is* the delivery channel — secure, signed, replicated, auditable

Using OpenID4VCI for this case is using an RPC call where a sealed transaction would do.

### 4.3 Empirical evidence (2026-04-14 debug trace)

Live walkthrough on `n1.sorcha.dev` via chrome-devtools-mcp revealed the pattern collapses at five distinct layers on a single-node deployment before we even get to the cross-node case. Each fix peeled back the next layer, now merged as PRs #285, #286, #287, #288, #290 (see `.planning/debug-trace/` for the trace). Even with all five patches deployed the single-node path works end-to-end but remains fundamentally single-node.

## 5. Design overview

### 5.1 Core shape

```
Action 1: Citizen submits data            (open, late-bound — unchanged)
Action 2: Assessor reviews + issues       credentialIssuanceConfig.targetAudience = "SorchaLocalWallet"
          credential                      Engine mints credential, encrypts to citizen wallet pubkey
                                          via EncryptionPipelineService (X25519 wrap + XChaCha20-Poly1305),
                                          seals into Action 2's Disclosures as a recipient-encrypted
                                          payload group. Routes to Action 3 on approval.
Action 3: Holder accept / reject          Sender = same open participant as Action 1 (same late-bound wallet).
                                          Terminal on execute (accept) AND on reject
                                          via RejectionConfig.IsTerminal = true.
                                          Empty dataSchema or nonce-only — no form to fill.
                                          Exists purely as the engine's anchor for accept/reject state.
```

### 5.2 The two holder-side surfaces

Per the product requirement that holders see pending credentials in both activity streams:

| Surface | Backing data | Trigger |
|---|---|---|
| **MyActions** — pending action list | Blueprint Service's local instance mirror (reconstructed from register sync) | Standard pending-actions query. After Fix A (#288), resolves via holder's wallet even with a stale JWT claim. |
| **MyCredentials → PENDING tab** | Wallet Service's `CredentialEntity` table filtered by `Status = PendingAcceptance` | New `InboundCredentialHook` fires on `NotificationDeliveryService.DeliverAsync` when a credential-offer transaction is detected. |

Both surfaces are holder-side local state. Neither requires RPC to the issuer's node. Accept and Reject from either entry point run the same flow and produce the same register transaction.

### 5.3 Notification path (already exists)

The Wallet Service already runs the full peer-replicated transaction observation path:

1. **Register Service** maintains a bloom filter of "wallet addresses this node cares about" (`AddressRegistrationService`).
2. When a sealed transaction lands on any subscribed register, the Register Service checks the transaction's recipient wallets against the bloom filter.
3. Matches are published on the `wallet:notifications` Redis channel.
4. `NotificationDeliveryService.DeliverAsync` (Wallet Service) consumes the notification, resolves the wallet → user → preferences, builds an `InboundActionEvent`, and publishes it to SignalR's `events:wallet` hub. UI subscribes via `EventsHub`.

Feature 106 hooks into step 4. When delivering a notification for a transaction, `DeliverAsync` inspects the transaction's disclosures for a recipient-encrypted payload matching the local wallet's pubkey, decrypts it, and if the decrypted content is a credential, creates the `CredentialEntity` row with `Status = PendingAcceptance` **before** firing the SignalR event. The SignalR event then carries enough metadata for the UI to know whether this is a plain action notification or a new credential offer.

No new Redis channel. No new background worker. Just an additional step inside the existing delivery pipeline.

## 6. Detailed design

### 6.1 `CredentialIssuanceConfig.targetAudience = SorchaLocalWallet`

The enum already exists (`Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfig.cs:90-92`) with values `SorchaInternal` (default) and `HaipExternalWallet`. Feature 106 introduces the new value `SorchaLocalWallet` and the engine branch that consumes it.

**Engine behaviour (new branch in `ActionExecutionService.ExecuteAsync`):**

1. Resolve the recipient participant's wallet via `instance.ParticipantWallets[recipientParticipantId]`.
2. Fetch the recipient wallet's pubkey via `IWalletServiceClient.GetWalletAsync(address)`.
3. Mint the credential via the existing `HaipCredentialMinter` with `holderJwk = { kty=OKP, crv=Ed25519, x=<wallet pubkey> }`. The credential is a standard SD-JWT VC with `cnf` binding to the holder wallet, identical in format to the HAIP path — only the delivery changes.
4. Build a disclosure group targeted at the recipient wallet: `DisclosureGroup { Recipient = walletPubkey, Payload = { credential: serialisedSdJwt, credentialType, issuerDid, offerId } }`.
5. Encrypt via `IEncryptionPipelineService.EncryptDisclosedPayloadsAsync` — same primitive Feature 085 uses for file chunks and Feature 079 uses for other disclosures.
6. Seal the encrypted payload into Action 2's transaction under a `/credential` disclosure pointer.
7. Route to Action 3 as usual.

No changes to validator, docket builder, or peer sync — the transaction is a normal sealed tx from their perspective.

### 6.2 Inbound credential detection (`Wallet.Service`)

**Touch point:** `NotificationDeliveryService.DeliverAsync` (`src/Services/Sorcha.Wallet.Service/Services/Implementation/NotificationDeliveryService.cs:54`).

Add a new step between "Step 1: Resolve address → wallet → user" and "Step 3: Route based on preference":

```csharp
// Step 2b: Inbound credential detection (Feature 106)
// Inspect the sealed transaction for a recipient-encrypted credential payload
// addressed to this wallet. If present, decrypt, persist as PendingAcceptance,
// and enrich the notification event so the UI can distinguish credential offers
// from plain action notifications.
var credentialOffer = await _inboundCredentialDetector.TryExtractAsync(
    recipientAddress, transactionId, registerId, cancellationToken);

if (credentialOffer is not null)
{
    var entity = new CredentialEntity
    {
        Id = credentialOffer.CredentialId,
        WalletAddress = recipientAddress,
        Type = credentialOffer.CredentialType,
        IssuerDid = credentialOffer.IssuerDid,
        SubjectDid = $"did:sorcha:wallet:{recipientAddress}",
        RawToken = credentialOffer.SerialisedSdJwt,
        ClaimsJson = credentialOffer.ClaimsJson,
        IssuedAt = DateTimeOffset.UtcNow,
        ExpiresAt = credentialOffer.ExpiresAt,
        Status = CredentialStatus.PendingAcceptance,  // ← new status
        IssuanceTxId = transactionId,
        IssuanceBlueprintId = blueprintId,
    };
    await _credentialRepository.AddAsync(entity, cancellationToken);
    actionEvent.CredentialOfferId = credentialOffer.CredentialId;  // Enrich the SignalR event
}
```

**`IInboundCredentialDetector` (new):**

```csharp
public interface IInboundCredentialDetector
{
    /// Fetches the sealed transaction from the Register Service, inspects its
    /// disclosures for a recipient-encrypted payload targeted at `walletAddress`,
    /// decrypts it with the wallet's private key via IWalletManager, and
    /// returns the extracted credential if present. Returns null when the
    /// transaction is not a credential offer or the local wallet is not a
    /// recipient.
    Task<InboundCredentialExtract?> TryExtractAsync(
        string walletAddress,
        string transactionId,
        string registerId,
        CancellationToken cancellationToken);
}
```

Detection uses **blueprint action metadata** as the primary signal (does this transaction's blueprint action have `credentialIssuanceConfig.targetAudience = SorchaLocalWallet`?) with a **disclosure payload marker** as the fallback (a `Type: "credential-offer-v1"` field on the decrypted payload) so that non-blueprint credential emission still works if we ever need it.

### 6.3 Status enum extension

```csharp
public enum CredentialStatus
{
    Active,             // existing
    Expired,            // existing
    Revoked,            // existing
    PendingAcceptance,  // NEW — decrypted and held in wallet but not yet accepted
    Declined,           // NEW — holder declined; kept for audit; not usable
}
```

Stored as-is on `CredentialEntity.Status`. The existing `GET /api/v1/wallets/{address}/credentials` endpoint gains a `?status=PendingAcceptance` filter (already supported by the repository; just needs endpoint parameter wiring).

**Declined: kept, not deleted.** On decline, the row stays with `Status = Declined` for audit visibility. A privacy-conscious holder can explicitly `DELETE /api/v1/wallets/{address}/credentials/{id}` later. Rationale: a user who is surprised to find a declined credential in their history is protected by the explicit delete; a user who wants a record of what they've declined gets one.

### 6.4 Blueprint Service instance mirror reconstruction

**Problem:** On a holder node, the Blueprint Service has no record of the instance created by the issuer's node. `GetPendingActionsByWalletAsync` returns empty — the holder sees nothing in MyActions.

**Fix:** A **read-only instance mirror** reconstructed from the holder's own register sync. Mechanism:

1. Blueprint Service subscribes to the `docket:confirmed` event bridge (it already does — `TransactionLifecycleEventBridge` exists in the Blueprint Service for Feature 104 lifecycle ticks).
2. On each confirmed transaction, check if any `participantWallets` on the transaction's instance match a locally-registered wallet via `IWalletServiceClient.GetWalletsByOwnerAsync` — the same lookup Fix A uses for the pending-actions query (`ActionEndpoints.cs:ResolveUserWalletAddressesAsync`). Results should be cached per request to avoid repeated service calls during a batch of transactions.
3. If yes and no local `Instance` row exists: create a new one with `IsReadOnlyMirror = true` and hydrate from the transaction content.
4. If yes and a local row exists: apply the same state transitions the issuer would apply (advance `CurrentActionIds`, append to `PendingActionPayloads`, etc.).
5. `IInstanceStore.UpdateAsync` rejects writes to read-only mirrors from the normal execution path — only the reconstructor can write.

After reconstruction, `GetPendingActionsByWalletAsync` (now benefitting from Fix A's wallet resolution fallback from #288) surfaces Action 3 in the holder's MyActions pending list. The existing `PendingActionSummary.DataSchema` population from #290 ensures the claim-card dispatch works.

**Important:** the mirror reconstructor must verify transaction signatures and validator consensus before trusting content. It does not trust arbitrary peer-replicated transactions — it trusts transactions that are sealed in a docket the validator has confirmed. This is the same trust model the wallet service already applies for inbound notifications.

### 6.5 Accept flow

**User click path:** MyActions → TAKE ACTION → dialog → CLAIM CREDENTIAL, **or** MyCredentials → PENDING tab → credential card → CLAIM CREDENTIAL.

Both paths drive the same handler:

1. **Local state update** — `PATCH /api/v1/wallets/{address}/credentials/{id}` → `Status = Active`. Immediate, local-only, fast feedback.
2. **Register transaction** — `POST /api/instances/{instanceId}/actions/3/execute` with an empty payload signed by the holder's wallet. The action has no form fields; replay protection comes from the engine's transaction uniqueness guarantees (same as every other action execution). This seals an "accept" transaction.
3. **Issuer closure** — the issuer's Blueprint Service observes the accept transaction on its own register sync and transitions the instance to `Completed`.

Steps 1 and 2 run in parallel from the UI. Step 3 is asynchronous and out-of-session.

### 6.6 Reject flow

**User click path:** Same two surfaces, DECLINE button.

1. **Local state update** — `PATCH /api/v1/wallets/{address}/credentials/{id}` → `Status = Declined`. Row preserved for audit.
2. **Register transaction** — blueprint engine's existing rejection mechanism. Action 3 has `RejectionConfig.IsTerminal = true`, so `POST /api/instances/{instanceId}/actions/3/reject` with an optional reason seals a rejection transaction.
3. **Issuer closure** — the issuer's Blueprint Service observes the reject transaction and transitions the instance to `Rejected`.

Same parallel execution as accept. No new rejection machinery — reuses the engine's existing `RejectionConfig` pathway.

### 6.7 Blueprint template shape

No new `credentialIssuanceConfig` fields. The existing `targetAudience` enum gains a new value. The wave 14b `outputMapping` from Action 2 → Action 3 can either stay (for parity) or be dropped (since the credential is now in Action 2's disclosures, not threaded through routing). Recommendation: **drop the outputMapping on the approval route** and let Action 3 exist purely as the symbolic terminal action.

Example Action 2 for Feature 106:

```jsonc
{
  "id": 2,
  "title": "Review and approve",
  "sender": "government-assessor",
  "dataSchemas": [ /* verificationDecision, reviewerNotes */ ],
  "credentialIssuanceConfig": {
    "credentialType": "VerifiedCitizenCredential",
    "recipientParticipantId": "citizen",
    "targetAudience": "SorchaLocalWallet",
    "claimMappings": [ /* claim → data pointer mappings */ ]
  },
  "disclosures": [
    { "participantAddress": "government-assessor", "dataPointers": ["/*"] },
    { "participantAddress": "citizen", "dataPointers": ["/credential"] }
  ],
  "routes": [
    { "id": "approved", "nextActionIds": [3],
      "condition": { "==": [{ "var": "verificationDecision" }, "approved"] } },
    { "id": "rejected", "nextActionIds": [], "isDefault": true }
  ]
}
```

Example Action 3 (the symbolic terminal):

```jsonc
{
  "id": 3,
  "title": "Accept your Verified Citizen credential",
  "sender": "citizen",
  "requiredPriorActions": [2],
  "dataSchemas": [ { "type": "object", "properties": {} } ],
  "rejectionConfig": { "isTerminal": true, "requireReason": false },
  "routes": [
    { "id": "accepted-terminal", "nextActionIds": [], "isDefault": true }
  ]
}
```

## 7. Data flow — happy path

```
    Citizen node                         Issuer node
    ─────────────                        ──────────────
    [browser] submits Action 1
         │
         ▼
    Blueprint Service → Validator → Register
                                        │
                              [register sync]
                                        │
                                        ▼
                             Issuer Blueprint Service
                             mirrors instance
                             sees Action 2 pending
                                        │
                                        ▼
                             [browser] assessor approves
                             → Action 2 execute
                             → engine mints credential
                             → encrypts to citizen wallet pubkey
                             → seals into Action 2 disclosures
                             → Validator → Register
                                        │
                              [register sync]
                                        │
    Citizen node ◄──────────────────────┘
    ├─ Register Service: bloom match → wallet:notifications
    │         │
    │         ▼
    ├─ Wallet Service NotificationDeliveryService
    │     ├─ resolves wallet → user → prefs
    │     ├─ InboundCredentialDetector extracts encrypted payload
    │     ├─ decrypts with citizen wallet private key
    │     ├─ creates CredentialEntity with Status = PendingAcceptance
    │     └─ fires SignalR event enriched with CredentialOfferId
    │
    ├─ Blueprint Service mirror reconstructor
    │     ├─ sees Action 2 tx confirmed
    │     ├─ advances local Instance.CurrentActionIds to [3]
    │     └─ GetPendingActionsByWalletAsync now returns Action 3
    │
    └─ Browser
          ├─ MyActions: Action 3 "Claim your Verified Citizen credential"
          ├─ MyCredentials → PENDING: new credential card
          └─ User clicks CLAIM CREDENTIAL
                ├─ PATCH credential Status → Active (local)
                └─ POST Action 3 execute → Validator → Register
                                                │
                                     [register sync]
                                                │
                                                ▼
                                     Issuer Blueprint Service
                                     sees Action 3 execute tx
                                     → transitions instance to Completed
```

## 8. Migration and backward compatibility

### 8.1 Existing blueprints

`HaipExternalWallet` target audience keeps working exactly as it does today. Wave 14b blueprints stay valid. Existing wave 14b instances in flight continue through the existing claim-card flow.

### 8.2 Deprecation path

- **Feature 106 ships**: blueprint authors SHOULD prefer `SorchaLocalWallet` for on-platform flows. Documentation updated. `blueprint-builder` skill updated with the new pattern as the default example.
- **Feature 107 (future)**: `HaipExternalWallet` moves to a "legacy" status on documentation only. Still fully supported.
- **Never**: removing `HaipExternalWallet` — it's load-bearing for the external-wallet scan-QR-with-phone use case.

### 8.3 Status enum migration

Adding new enum values to `CredentialStatus` is backward-compatible at the serialisation layer (System.Text.Json handles unknown enum values with `JsonStringEnumConverter` by default on older clients). No database migration needed beyond the new allowed values.

## 9. Security considerations

1. **Recipient-only encryption.** The credential payload is wrapped with the recipient wallet's X25519 pubkey via `EncryptionPipelineService`. Only the holder's wallet private key can decrypt. A node without the holder's wallet sees only the sealed ciphertext in the disclosure.
2. **Signature verification on mirror.** The instance mirror reconstructor only trusts transactions that are in a docket the local validator has confirmed. Arbitrary peer gossip is not trusted.
3. **Accept/reject authorisation.** Action 3's sender is the same open participant as Action 1, which late-bound to the holder's wallet. Only a transaction signed by that wallet is accepted. The reconstructor reads the binding from Action 1's sender field on the sealed transaction, so it works cross-node.
4. **Bloom filter false positives.** The existing `NotificationDeliveryService.DeliverAsync` handles false positives with `NoUserFound` at line 72-79. A false positive on the credential detection path is handled the same way — `TryExtractAsync` returns null if the payload cannot be decrypted or isn't a credential.
5. **Replay.** The credential's `IssuanceTxId` is unique on the register. Attempting to replay an Accept/Reject transaction against an already-resolved instance is rejected by the engine's existing state machine.
6. **Privacy.** Decline kept with `Status = Declined` is holder-local — it never leaves the holder's Wallet Service. The reject transaction *is* visible on the register, so a verifier can tell that this instance was rejected, but not the content or the holder's reason (unless `requireReason = true` was set and the reason is disclosed).

## 10. Open questions / future work

- **TTL on accept/reject.** The spec currently says "if the credential expires, it's expired" — relies on the credential's own `notValidAfter` being honoured at presentation time. A future iteration could add a shorter "holder must respond within N days" TTL at the blueprint level, auto-declining after expiry. Not in scope for Feature 106.
- **Multi-credential issuance in one action.** Currently `credentialIssuanceConfig` issues one credential per action. Batching (e.g. a package of verified identity + address proof + age gate) is a future extension. The recipient-encrypted disclosure shape can carry a list, so this is a forward-compatible path.
- **Holder node doesn't have the issuer's blueprint published.** The reconstructor needs the blueprint definition to resolve action titles, schemas, etc. If the holder's node hasn't synced the blueprint yet, it may need to pull it from a peer. Tracked as a reconstructor implementation detail.
- **Wave 14b instance migration.** Existing wave 14b instances in flight when Feature 106 ships continue through their existing claim-card path. No migration needed — the blueprint engine routes each instance by its published blueprint version.

## 11. Implementation wave breakdown

| Wave | Scope | Verification gate |
|---|---|---|
| **1 — Engine** | Add `SorchaLocalWallet` branch in `ActionExecutionService.ExecuteAsync`. Mint credential via `HaipCredentialMinter` with holder wallet's pubkey as `cnf`. Encrypt via `EncryptionPipelineService`. Seal into Action 2 disclosure under `/credential` pointer. Unit tests against a mock validator. | Test: Action 2 with `targetAudience = SorchaLocalWallet` produces a sealed transaction with a recipient-encrypted `/credential` disclosure. Validator accepts. |
| **2 — Wallet Service inbound** | `CredentialStatus.PendingAcceptance` + `Declined` enum values. Repository filter wiring. New `IInboundCredentialDetector` + default implementation that reads the sealed tx, finds the recipient-encrypted disclosure, decrypts with the local wallet's private key, and extracts the credential. Hook into `NotificationDeliveryService.DeliverAsync` Step 2b. Unit tests for the detector. | Test: fake sealed tx with a recipient-encrypted credential → detector returns the decoded credential → repository stores it as `PendingAcceptance`. |
| **3 — Blueprint Service mirror** | New `InstanceMirrorReconstructor` background service consuming the `docket:confirmed` event. For confirmed transactions involving local wallets, creates or advances a read-only `Instance` row. `IInstanceStore` rejects writes to read-only mirrors from execution paths. | Integration test with two Blueprint Service containers sharing a Register: issuer executes Action 2, holder's Blueprint Service reconstructs the instance, holder's MyActions pending query returns Action 3. |
| **4 — Client UI** | Wire `MyCredentials` PENDING tab to `Status = PendingAcceptance` (filter already supported, just needs UI wiring). CredentialClaimCard already exists; hook its Accept / Decline buttons to:<br>• `PATCH /api/v1/wallets/.../credentials/{id}` for local status update<br>• `POST /api/instances/.../actions/3/execute` or `.../reject` for the register transaction.<br>Wire the SignalR notification enrichment (`CredentialOfferId` on `InboundActionEvent`) to refresh both MyActions and MyCredentials views. | Playwright test: full submit → approve → notification → Accept → credential in Active tab → instance Completed on issuer's node. |
| **5 — Verified Citizen v2 migration** | Update the `HaipVerifiedCitizen` blueprint template to use `targetAudience = SorchaLocalWallet`. Update the walkthrough setup + run scripts. Update the blueprint-builder skill documentation to show the new pattern as the default for on-platform flows. Keep one example blueprint using `HaipExternalWallet` so the external-wallet path is still demonstrated. | n1 end-to-end verification: fresh public user signup → submit → approve (via any path) → credential lands in PENDING tab → user Accepts → credential active. |
| **6 — Cross-node verification** | Deploy a second Sorcha node (locally via docker compose, or a second VM) subscribed to the same register as n1. Issue a credential on node A, accept on node B. Verify the full cross-node flow. | Passing cross-node walkthrough run. This is the primary win Feature 106 buys us — it doesn't matter until it works cross-node. |

Rough estimate: 6-8 working sessions end to end, each wave committed and reviewable independently.

## 12. Non-code deliverables

- Update `.claude/skills/blueprint-builder/SKILL.md` with the register-native pattern as the default credential-issuance example.
- Update `.claude/skills/walkthrough-builder/SKILL.md` with the new `MyCredentials → PENDING` inbox as the expected holder landing page.
- Update CLAUDE.md's "HAIP pipeline" section to distinguish on-platform vs external-wallet delivery paths.
- Add a short README under `walkthroughs/HaipVerifiedCitizen/` explaining which `targetAudience` the walkthrough uses and why.

## 13. Appendix — existing primitives map

Cited in Section 5.3 and Section 6:

| Primitive | Location | Used for |
|---|---|---|
| `EncryptionPipelineService` | `src/Common/Sorcha.TransactionHandler/Encryption/EncryptionPipelineService.cs:66` | X25519 wrap + XChaCha20-Poly1305 AEAD for disclosure encryption |
| `EncryptedPayloadGroup` + `WrappedKey` | `src/Common/Sorcha.TransactionHandler/Encryption/Models/EncryptionModels.cs:12` | Recipient-addressed encrypted payload envelope |
| `NotificationDeliveryService.DeliverAsync` | `src/Services/Sorcha.Wallet.Service/Services/Implementation/NotificationDeliveryService.cs:54` | Existing bloom-filter notification hook point |
| `AddressRegistrationService` | `src/Services/Sorcha.Wallet.Service/Services/Implementation/AddressRegistrationService.cs` | Wallet bloom filter registration |
| `TransactionLifecycleEventBridge` | `src/Services/Sorcha.Wallet.Service/Services/Implementation/TransactionLifecycleEventBridge.cs:21` | Existing `docket:confirmed` / `receipt:generated` subscriber |
| `HaipCredentialMinter.MintCredentialAsync` | `src/Services/Sorcha.Haip.Service/Services/HaipCredentialMinter.cs:46` | SD-JWT VC mint — reused as the credential generator |
| `CredentialEntity` | `src/Core/Sorcha.Wallet.Portable/Domain/Entities/CredentialEntity.cs` | Local credential store — gains new status enum values |
| `ActionExecutionService.ExecuteAsync` | `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs:196` | Engine entry point — gains `SorchaLocalWallet` branch |
| `RejectionConfig` | `src/Common/Sorcha.Blueprint.Models/Action.cs` (RejectionConfig field) | Existing rejection machinery reused for Action 3 decline |
