# Research — Register-native credential delivery

**Feature**: 106-register-native-credentials
**Phase**: 0 (/speckit.plan)
**Date**: 2026-04-15
**Spec**: [spec.md](./spec.md)
**Plan**: [plan.md](./plan.md)
**Upstream design**: [docs/superpowers/specs/2026-04-14-register-native-credential-delivery-design.md](../../docs/superpowers/specs/2026-04-14-register-native-credential-delivery-design.md)

## Framing

Phase 0 of /speckit.plan normally resolves technical unknowns and documents "what we chose and why" for the upcoming implementation phase. Feature 106 entered speckit already equipped with a full brainstormed design document plus a live debug trace against n1.sorcha.dev that confirmed every existing primitive the feature intends to reuse. Consequently this research.md does not explore unknowns — it **consolidates decisions already made** so the record is internally discoverable without requiring readers to cross-reference the upstream design doc.

All five decisions below are cited back to the upstream design document's §13 primitives map, which in turn cites source files by `file:line` for each claim.

---

## Decision 1 — Encryption primitive: reuse `EncryptionPipelineService`

**Choice**: All credential-delivery encryption routes through the existing `Sorcha.TransactionHandler.Encryption.EncryptionPipelineService.EncryptDisclosedPayloadsAsync` method. No new wrapper, no new envelope format, no new wire shape at the encryption layer.

**Rationale**:

- Already the authoritative encryption layer for recipient-addressed disclosures on the Sorcha register. Used by Feature 085 (file chunking), Feature 079 (trust hardening), and wave 14b (disclosure encryption) — three production usages, all at scale-tested correctness.
- X25519 wrap + XChaCha20-Poly1305 AEAD is NIST-post-quantum-adjacent (XChaCha20 is part of the CNSA 2.0 allowlist per Sorcha's constitution) and has a 24-byte nonce that eliminates birthday-bound concerns across the credential population.
- Constitution principle II (Security First) and FR-004 both forbid introducing new cryptographic machinery.

**Alternatives considered**:

- **New envelope shape dedicated to credentials.** Would add a second encryption wire contract, double the audit surface, and require security review for a scheme the rest of the system doesn't use. Rejected on "no new crypto" grounds.
- **Wallet Service `/decrypt` endpoint repurposed.** That endpoint is session-scoped (it decrypts payloads on behalf of a logged-in user) and doesn't support the "recipient-addressed, asynchronously delivered" shape. Using it would require round-tripping to the Wallet Service HTTP surface for every inbound credential — violates the latency budget and forces a synchronous dependency between the notification path and the credential store write.
- **Transport-level TLS as the encryption layer.** Peer sync already runs over mTLS, but the register stores transactions at rest on every subscribing node; TLS protects the wire, not the at-rest copy. Credential payloads must be encrypted at the application layer so the issuer's node (and any intermediate nodes) cannot read them.

---

## Decision 2 — Inbound credential detection hook: `NotificationDeliveryService.DeliverAsync`

**Choice**: Detect inbound credential offers by extending `NotificationDeliveryService.DeliverAsync` with a new "Step 2b" between user resolution and preference check. The new `IInboundCredentialDetector` service fetches the sealed transaction, tries to extract and decrypt a recipient-addressed credential payload, and persists a `PendingAcceptance` credential row if one is found. No new background worker; no new Redis channel; no polling loop.

**Rationale**:

- The bloom-filter notification path already exists and fires for any sealed transaction where a local wallet is a potential recipient (`NotificationDeliveryService.cs:54`). That observation point is exactly what the feature needs — conceptually, "another thing we do when a wallet-relevant tx lands".
- Extending the existing hook inherits its metrics, telemetry, rate limiting, and false-positive handling machinery (`NotificationMetrics.RecordNoUserFound()` at line 47 of `NotificationMetrics.cs` is directly reusable for credential-detection false positives).
- The existing SignalR `InboundActionEvent` emission becomes the UI notification for new pending credentials — just needs a new `CredentialOfferId` field added to the event shape.
- The 30-second latency target (SC-002) is tight enough that any polling-based alternative would either miss the target or require so much polling frequency that it'd cost more than the push-based approach.

**Alternatives considered**:

- **New `InboundCredentialWorker` background service with its own register observation loop.** Duplicates work the Wallet Service already does for notifications. Would require its own bloom-filter registration path, its own metrics, its own failure handling. Rejected for complexity and duplication.
- **Timer-based polling of the register from the Wallet Service.** Latency floor too high (would need sub-10-second polling to meet SC-002, and that's a lot of redundant queries). Rejected.
- **Issuer-side push via direct HTTP to the holder's Wallet Service.** Breaks the federated constraint — the issuer wouldn't know which Wallet Service to call, and introducing a directory service for this would be a massive scope creep that violates FR-019 (no node-to-node RPC).

---

## Decision 3 — Instance mirror reconstruction in the Blueprint Service

**Choice**: Add a new background service `InstanceMirrorReconstructor` to the Blueprint Service that subscribes to the existing Redis `docket:confirmed` channel (already consumed by `TransactionLifecycleEventBridge`) and reconstructs read-only `Instance` rows for transactions whose `participantWallets` match locally-registered wallets. The reconstructed rows are flagged `IsReadOnlyMirror = true` so the normal execution pathway rejects writes.

**Rationale**:

- `/api/actions/pending` is a hot path — MyActions refreshes it on every page load and SignalR push. A query-time register lookup for each call would fan out across every instance on every subscribed register and scale poorly.
- Mirror reconstruction pays the cost once per confirmed transaction (write-time) and amortises it across all subsequent reads. Matches the shape of every other Sorcha projection (e.g. Wallet Service's transaction lifecycle rows).
- The `docket:confirmed` Redis channel already exists and is consumed by `TransactionLifecycleEventBridge` inside the Blueprint Service for Feature 104 lifecycle ticks — reusing it keeps the surface area small.
- Read-only mirrors prevent the holder's execution path from accidentally advancing instance state as if the holder were the issuer — only the reconstructor can write.

**Alternatives considered**:

- **Query-time register lookup.** Rejected for scale — each `/api/actions/pending` would spray across every register the user is subscribed to.
- **Cross-node instance gossip via peer network.** Violates "register is the only cross-node channel" (FR-019) and creates a second cross-node data channel the system would have to reason about.
- **Hook mirror reconstruction into `TransactionLifecycleEventBridge` directly instead of a new background service.** Would bloat an existing service with unrelated responsibilities. A separate service is cheap and keeps the mirror logic testable in isolation.
- **Make the mirror real-write-capable (not read-only).** Dangerous — the holder's execution path could try to advance Action 2 from the holder's node, which is an authorisation mismatch. The read-only flag protects the invariant.

---

## Decision 4 — Reuse the wave 14b `CredentialClaimCard` dialog

**Choice**: The holder's accept/decline UI dialog is the existing wave 14b `CredentialClaimCard` component, now driven by the MyCredentials PENDING tab as well as the MyActions entry point. No new card component.

**Rationale**:

- Verified end-to-end in the browser as part of PR #290's debug trace (`.planning/debug-trace/06-claim-card-rendered.png`). Renders correctly, handles accept and decline, integrates with the existing `CredentialOfferSchemaResolver` dispatch.
- The card takes offer data as a parameter — it doesn't care whether the offer came from a blueprint action's prepopulated payload or from a pending credential in the inbox. Both code paths produce the same `CredentialOfferInfo` shape.
- Both entry points (MyActions and MyCredentials) drive the same dialog instance, meeting FR-009 without UX duplication.

**Alternatives considered**:

- **New `InboxCredentialCard` component.** Would duplicate the existing card's look, feel, and behaviour for no user-visible benefit. Rejected on YAGNI grounds.
- **Redirect MyCredentials PENDING tab to MyActions.** Worse UX — the user explicitly wants to see pending credentials in the credentials surface, not have the click bounce them to actions.

---

## Decision 5 — `Declined` is a retained terminal state, not a hard-delete

**Choice**: When a holder declines a pending credential, the `CredentialEntity` row transitions to `Status = Declined` and is retained in the local wallet store indefinitely. The holder can explicitly delete it later via `DELETE /api/v1/wallets/{address}/credentials/{id}`. No automatic hard-delete.

**Rationale**:

- User gave this direction explicitly during the brainstorming step ("agree" on the "decline hard-delete vs status" design question).
- Retained decline history is auditable — the holder can see what they declined and when, and a third party examining the wallet store can verify the holder's declinations are internally consistent with the register's rejection transactions.
- Privacy-conscious holders retain the explicit delete path — no loss of user control.
- Matches the "DAD" framing of the Sorcha constitution: every action is recorded, including the act of declining.

**Alternatives considered**:

- **Hard-delete on decline.** Rejected by user direction. Would also lose the audit trail for future "why didn't I get credential X?" investigations.
- **Soft-delete with automatic TTL.** FR-024 explicitly rules out a workflow-level TTL. The credential's own embedded validity window is the only expiry signal.
- **Store declined credentials in a separate "archive" table.** Splits the credential entity across two tables for no gain. The status enum extension handles the lifecycle cleanly.

---

## Open questions (none)

The feature spec's quality checklist passes with zero `NEEDS CLARIFICATION` markers (see `checklists/requirements.md`). All five decisions above are recorded against explicit rationale. The implementation phase can proceed directly from here into Phase 1 data-model and contract generation without waiting for additional user input.

## References

- Upstream design document: `docs/superpowers/specs/2026-04-14-register-native-credential-delivery-design.md` §13 (existing primitives map) and §11 (implementation wave breakdown)
- Speckit specification: `./spec.md`
- Quality checklist: `./checklists/requirements.md`
- Debug trace evidence: `.planning/debug-trace/` (all 8 screenshots from the live chrome-devtools-mcp walkthrough)
- Sorcha constitution: `.specify/memory/constitution.md`
- Live runtime evidence:
  - `NotificationDeliveryService.cs:54` — existing notification delivery entry point
  - `EncryptionPipelineService.cs:66` — existing encryption primitive
  - `TransactionLifecycleEventBridge.cs:21` — existing `docket:confirmed` subscriber
  - `HaipCredentialMinter.MintCredentialAsync` — existing credential mint path
  - `RejectionConfig.IsTerminal` on blueprint action models — existing reject protocol
