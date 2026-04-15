# Feature Specification: Register-native credential delivery

**Feature Branch**: `106-register-native-credentials`
**Created**: 2026-04-15
**Status**: Draft
**Input**: User description: Replace the Feature 104 wave 14b OpenID4VCI pre-authorized-code claim-card pattern with register-native credential delivery that works across a federated multi-peer deployment. The full design rationale lives in `docs/superpowers/specs/2026-04-14-register-native-credential-delivery-design.md`; this spec formalises the contract the implementation plan binds against.

## User Scenarios & Testing *(mandatory)*

The core insight framing these scenarios: today Sorcha ships blueprints that issue verifiable credentials to citizens on the platform, but the claim flow requires the citizen and the issuer to be on the same node — breaking the federated-by-default promise of the platform. This feature reshapes credential delivery so that the register itself (already peer-replicated) is the delivery channel, and the holder's acceptance is an explicit, auditable step that works regardless of which node the holder is connected to.

### User Story 1 - Holder receives a credential on their own node in a federated network (Priority: P1)

A citizen signs up for Sorcha on node B. They submit a Verified Citizen application to a register that a government assessor on node A is subscribed to. The assessor reviews and approves the application. Within seconds, the citizen — still on node B, never touching node A — sees a new pending credential in their wallet. They click to claim it; the credential lands in their active credentials list and the assessor's instance closes as completed on node A. At no point does node B call node A directly; everything flows through the register's peer sync.

**Why this priority**: This is the entire point of the feature. Federated credential issuance is the thing Sorcha's register architecture was built to support, and the current pattern silently assumes single-node. Without this story working, Sorcha's cross-node story is fiction for anything involving credentials. Every other user story depends on the same primitives working.

**Independent Test**: Deploy two Sorcha nodes subscribed to the same register. On node A, execute a blueprint that issues a Verified Citizen credential to a holder whose wallet was created on node B. Verify the credential appears in the holder's pending list on node B without the holder's browser ever contacting node A. Verify the holder can accept it on node B and the instance closes on node A.

**Acceptance Scenarios**:

1. **Given** node A hosts the assessor's blueprint service and node B hosts the holder's wallet and blueprint services, both subscribed to register R, **When** the assessor executes the issuing action on node A, **Then** the sealed transaction reaches node B via register peer sync, node B's wallet service detects and decrypts the recipient-addressed credential payload, and the holder sees the pending credential in their wallet view within 30 seconds.
2. **Given** the holder has a pending credential on node B, **When** they click to accept it, **Then** the credential transitions to the active state locally, an acceptance transaction is sealed to the register, and node A's blueprint service observes the acceptance and transitions the issuing instance to completed without either node calling the other directly.
3. **Given** the holder has a pending credential on node B, **When** they click to decline it, **Then** the credential is retained locally with a declined status for audit purposes, a rejection transaction is sealed to the register, and node A's blueprint service transitions the issuing instance to rejected.
4. **Given** the holder on node B has not yet claimed a pending credential, **When** they navigate between their pending actions view and their pending credentials view, **Then** the same pending credential is surfaced in both places and either entry point drives the same accept or decline outcome.

---

### User Story 2 - Single-node demo still works identically (Priority: P1)

A developer running the standard single-node `docker-compose up` deployment must see the same end-to-end credential flow as the federated case. Signup, submit, approve, accept — all of it works the same way and the credential lands in the same place. The demo path is not a different code path from the federated path.

**Why this priority**: Regression on the demo path would block every walkthrough, every onboarding session, every exploratory test, and the daily development loop. The federated model must degrade gracefully to the single-node case. Tied with Story 1 because both are P1 — neither alone is shippable.

**Independent Test**: On a fresh `docker-compose up` deployment, run the Verified Citizen walkthrough end-to-end through the browser. A new public user must be able to sign up, create a wallet, submit the form, have the approval executed, and accept the resulting credential — all without special configuration or code branches that only fire on single-node.

**Acceptance Scenarios**:

1. **Given** a single-node `docker-compose up` deployment with a registered issuer and a fresh public user, **When** the user completes the full submit → approve → accept flow, **Then** the resulting credential appears in the user's active credential list and the issuing instance is completed, with no configuration difference from the federated case.
2. **Given** the same deployment, **When** the user views their pending credentials inbox, **Then** they see newly arrived credentials listed there even when the issuer and holder share a physical host.

---

### User Story 3 - External mobile wallet path is preserved (Priority: P1)

A blueprint author specifies that a credential should be issued to an external wallet (e.g. the holder will scan a QR code with their phone-based wallet app). The existing HAIP OpenID4VCI pre-authorized-code flow continues to work exactly as it does today — no behavioural change, no migration required, no deprecation.

**Why this priority**: The feature explicitly adds a second delivery mode rather than replacing the first. The external-wallet path remains load-bearing for mobile-first consumer scenarios and for federation with non-Sorcha wallets. A regression here breaks the wave 14b ecosystem.

**Independent Test**: Run the existing HaipDrivingLicence walkthrough (which uses external-wallet delivery) on a fresh deployment and verify it still passes end-to-end. No blueprint or code changes to the external-wallet path.

**Acceptance Scenarios**:

1. **Given** a blueprint whose issuing action is configured for the external-wallet delivery mode, **When** the issuing action executes, **Then** the system produces an OpenID4VCI credential offer URI that an external wallet can redeem exactly as it does today.
2. **Given** a blueprint switching from external-wallet to register-native delivery, **When** the blueprint is republished, **Then** the engine routes its issuance through the new register-native path while existing instances of the old version complete through their original path.

---

### User Story 4 - Holder refuses or misses a credential (Priority: P2)

A holder either explicitly declines a credential offered to them, or simply never opens their wallet to act on it. The issuer's workflow state is not left dangling in either case; the register carries the outcome back to the issuer, and the holder retains an auditable record of their own action (or inaction) through the credential's embedded validity period.

**Why this priority**: Without explicit decline handling the issuer's instance state leaks — an assessor's pending-review list grows unbounded with ghost instances. Without the holder's audit trail, the feature doesn't meet the "DAD" alteration pillar (every action is recorded, not just the happy-path ones). P2 because the happy path (Story 1) is more critical for first-ship, but this has to land in the same release.

**Independent Test**: Issue a credential, then have the holder click decline. Verify the holder's wallet keeps a row marked as declined, the issuer's instance transitions to rejected via a sealed rejection transaction, and nothing is left in a "waiting" state. Separately, issue a credential with a short embedded validity and let it expire; verify both sides reach a clean terminal state without bespoke TTL logic.

**Acceptance Scenarios**:

1. **Given** a holder has a pending credential, **When** they decline it, **Then** the local record transitions to a declined state but is not deleted, a rejection transaction is sealed to the register, and the issuer sees the instance close as rejected.
2. **Given** a holder has a pending credential whose embedded validity window has expired, **When** they next view their wallet, **Then** the credential is shown as expired and cannot be accepted, and the user can still explicitly delete it from the audit trail when they wish.

---

### User Story 5 - Blueprint authors adopt register-native delivery for new workflows (Priority: P2)

A blueprint author building a new on-platform credential-issuing workflow opts in to register-native delivery through a single configuration value on the issuing action. They do not need to add middleware, operate an external token endpoint, provision trust chains, or design a claim-code redemption UI. The default example they see when they open the blueprint builder skill documentation uses the new delivery mode.

**Why this priority**: The feature is only useful if authors reach for it by default. P2 because it depends on Story 1 working; once it does, this is about ergonomics and adoption. It's a documentation and migration task more than a code task.

**Independent Test**: A blueprint author unfamiliar with the feature reads the blueprint-builder skill documentation, writes a new credential-issuing blueprint, and the credential delivery works end-to-end on first deploy without them knowing about bloom filters, mirror reconstruction, or SD-JWT internals.

**Acceptance Scenarios**:

1. **Given** the blueprint-builder skill documentation, **When** an author follows the default example for credential issuance, **Then** their resulting blueprint uses register-native delivery and the author has not needed to learn OpenID4VCI terminology to ship the feature.
2. **Given** the Verified Citizen walkthrough template as shipped, **When** a fresh deployment runs the walkthrough, **Then** the walkthrough uses register-native delivery as its default and the holder sees the credential land in the pending inbox rather than via a pre-auth redemption flow.

---

### Edge Cases

- **Bloom filter false positives.** The wallet notification path uses bloom filters for wallet relevance. A false positive will cause the inbound detection step to run against a transaction that isn't actually addressed to any local wallet. The system must treat this as a no-op, not a failure.
- **Non-credential transactions on the same disclosure path.** Not every recipient-addressed disclosure is a credential offer. The inbound detection must only persist a pending credential when the decrypted content actually matches the credential-offer shape; anything else passes through untouched.
- **Holder has multiple wallets and the register is subscribed under more than one.** The holder receives the credential once per matching wallet. Duplicate detection by credential id prevents persisting the same credential more than once in the holder's wallet store.
- **Blueprint not yet synced on holder node.** The holder's mirror reconstructor needs the blueprint definition to surface the action context in the pending-actions view. If the blueprint hasn't reached the holder's node yet, the pending credential must still appear in the credentials inbox (which doesn't depend on the blueprint), and the pending action in the actions view appears once the blueprint catches up.
- **Holder accepts after the credential's embedded validity window has passed.** Acceptance must fail cleanly with a user-facing "credential has expired" message; the issuer's instance should still close cleanly via an expiry path rather than stay in a pending-forever state.
- **Holder declines but the rejection transaction fails to seal on the register.** Local state transitions to declined, but the register-side closure is still pending. The system must retry the rejection transaction and surface the "closure in progress" state honestly rather than claiming the decline is complete.
- **Holder node subscribes to the register AFTER the transaction was sealed.** Late subscribers should be able to replay historical transactions and recover any pending credentials addressed to their wallets that were sealed before they joined.
- **Wallet creation happens after the token was issued.** Related to the Fix A work that shipped just before this feature: the pending-actions and inbound-credentials paths must resolve the holder's wallets at request time (not from a stale token claim), so a holder who creates a wallet in the same session immediately sees credentials addressed to that wallet without a re-login.

## Requirements *(mandatory)*

### Functional Requirements

**Delivery mode on the issuing action**

- **FR-001**: The system MUST allow a blueprint author to specify that an action issues credentials via "register-native delivery to a Sorcha wallet holder on the platform" as an alternative to the existing external-wallet delivery mode.
- **FR-002**: The system MUST continue to support the existing external-wallet delivery mode without behavioural change; the two modes MUST coexist and a blueprint MUST be free to choose either.
- **FR-003**: When register-native delivery is selected, the system MUST mint the credential with a cryptographic binding to the holder wallet's public key (as it does today) and seal the credential payload into the issuing action's transaction in a form that only the holder's wallet can decrypt.
- **FR-004**: The register-native delivery mode MUST reuse the existing recipient-addressed disclosure encryption primitive without introducing new cryptographic machinery.

**Holder-side inbound detection**

- **FR-005**: When a peer-replicated transaction carrying a register-native credential offer reaches a node where the intended holder's wallet lives, the system MUST decrypt the credential with the wallet's private key and persist it in the holder's local wallet store as pending acceptance, within a target of 30 seconds of the transaction being sealed to the register on the issuing node.
- **FR-006**: The holder's local wallet store MUST distinguish between credentials in pending acceptance, active, declined, expired, and revoked states.
- **FR-007**: The system MUST emit a real-time notification on the holder's existing wallet event channel when a new pending credential is persisted, so the holder's UI can surface the new item without polling.
- **FR-008**: The inbound detection path MUST tolerate bloom-filter false positives and non-credential transactions without failing; anything that is not a decodable credential offer addressed to a local wallet MUST be ignored silently.

**Holder-side visibility**

- **FR-009**: The holder MUST see pending credentials in both their "pending actions" view and their "credentials inbox" view; either entry point MUST drive the same accept or decline outcome.
- **FR-010**: The system MUST reconstruct enough of the issuing blueprint instance's state on the holder's node (from peer-replicated register transactions) for the pending-actions view to surface the holder's next action without requiring any direct communication with the issuing node.
- **FR-011**: The reconstructed instance state on the holder's node MUST be treated as read-only by the normal blueprint execution path; only the reconstructor pathway is permitted to write to it.
- **FR-012**: The system MUST verify the signature and validator consensus on any transaction it uses to reconstruct instance state — arbitrary peer gossip MUST NOT be trusted.

**Acceptance and rejection**

- **FR-013**: The holder MUST be able to explicitly accept a pending credential; doing so MUST transition the credential to the active state locally and MUST also seal an acceptance transaction to the register that the issuer's node can observe and act on.
- **FR-014**: The holder MUST be able to explicitly decline a pending credential; doing so MUST transition the credential to the declined state locally (retained, not deleted) and MUST also seal a rejection transaction to the register via the blueprint engine's existing rejection protocol.
- **FR-015**: A declined credential MUST remain in the holder's local wallet store for audit visibility until the holder explicitly deletes it; the holder MUST always retain the ability to explicitly delete it.
- **FR-016**: On the issuer's node, the blueprint instance MUST transition to completed when an acceptance transaction is observed on the register, and to rejected when a rejection transaction is observed — with no direct communication between the issuer and holder nodes.
- **FR-017**: The acceptance and rejection transactions MUST be signed by the holder's wallet so the issuer can verify, from the transaction itself, that the holder (and only the holder) chose the outcome.

**Cross-node correctness**

- **FR-018**: The feature MUST work end-to-end when the issuer and holder are on different Sorcha nodes that share a peer-replicated register. No single part of the feature's design may require the two to share a blueprint service instance.
- **FR-019**: The feature MUST NOT introduce any node-to-node remote procedure call. The only durable cross-node channel is the register itself.

**Backwards compatibility and migration**

- **FR-020**: Existing blueprint instances that were created before this feature ships MUST continue through their original delivery mode and MUST NOT be migrated or altered in flight.
- **FR-021**: The shipped Verified Citizen walkthrough MUST be updated to use register-native delivery by default as part of this feature; an example blueprint demonstrating the external-wallet path MUST still exist in the shipped examples so the alternative remains documented.
- **FR-022**: Blueprint-builder skill documentation MUST be updated so that a new blueprint author reading the default credential-issuance example uses register-native delivery without needing any explicit opt-in.

**Auditability and terminal states**

- **FR-023**: Both the acceptance and rejection transactions MUST be sealed to the register so they are replayable and verifiable by any party that observes the register later.
- **FR-024**: The feature MUST NOT introduce a workflow-level "holder response TTL" that auto-closes pending issuances; the credential's own embedded validity window is the only expiry signal.
- **FR-025**: Every pending credential in the holder's wallet store MUST reference the issuing transaction and blueprint so that the audit trail from holder back to issuer is complete.

### Key Entities

- **Credential record in the holder's wallet store**: A locally persisted representation of a credential the holder has received. Holds one of the lifecycle states (pending acceptance, active, declined, expired, revoked), references the issuing transaction and blueprint, and carries the serialised credential token for presentation.
- **Issuing blueprint action**: An action in a blueprint that mints and delivers a credential. Carries configuration that selects between register-native delivery and external-wallet delivery, and identifies the recipient participant.
- **Sealed issuance transaction**: A transaction on the register that carries the minted credential encrypted to a specific holder wallet, produced when the issuing action executes. The delivery channel across nodes.
- **Holder acceptance transaction**: A transaction on the register, signed by the holder's wallet, that records the holder's explicit acceptance of a received credential. The signal the issuer's node observes to close the instance as completed.
- **Holder rejection transaction**: A transaction on the register, signed by the holder's wallet, that records the holder's explicit decline of a received credential. The signal the issuer's node observes to close the instance as rejected.
- **Read-only instance mirror**: A reconstruction on the holder's node of an issuing blueprint instance, built from observing peer-replicated transactions. Surfaces pending actions to the holder's UI but is never written to by the normal execution path — only the reconstructor pathway can modify it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

**Primary cross-node outcome**

- **SC-001**: On a federation of two Sorcha nodes sharing a register, a holder on node B receives and accepts a credential issued by an assessor on node A, end-to-end, without node B making any direct call to node A.
- **SC-002**: From the moment the issuing transaction is sealed to the register on the issuing node, the holder sees the pending credential in their wallet view on their own node within 30 seconds in 95% of runs on a healthy peer network.
- **SC-003**: The issuer's blueprint instance transitions to completed within 30 seconds of the holder clicking accept in 95% of runs, without any direct communication between nodes.

**Holder experience**

- **SC-004**: A holder completes the full flow from "notification of new pending credential" to "credential active in wallet" in under 15 seconds of active interaction time.
- **SC-005**: 100% of pending credentials surfaced to the holder are reachable from both the pending-actions view and the credentials-inbox view; neither surface can hide an item the other shows.
- **SC-006**: A holder who declines a credential still has access to their declined-credentials audit trail for as long as they choose not to delete it, measured by "declined credentials remain visible until explicit delete".

**Regression guards**

- **SC-007**: The existing external-wallet HAIP walkthrough continues to pass end-to-end without modification on every release; no blueprint using the external-wallet delivery mode regresses.
- **SC-008**: The single-node demo path (the `docker-compose up` developer experience) produces identical user-visible behaviour to the federated case, measured by "the same walkthrough script passes in both configurations".

**Authoring and documentation**

- **SC-009**: A blueprint author reading the blueprint-builder skill documentation for the first time produces a working credential-issuing blueprint on their first attempt without ever encountering OpenID4VCI terminology.
- **SC-010**: The shipped Verified Citizen walkthrough's default delivery mode is register-native at the moment this feature ships; the external-wallet path is demonstrated by a separate, clearly labelled example.

**Auditability**

- **SC-011**: For every holder action (accept or decline), a sealed transaction exists on the register that a third-party auditor can independently verify without accessing private credential content.
- **SC-012**: The feature adds zero dangling workflow states: on any terminal outcome (accept, decline, credential expiry), both the issuer's instance and the holder's local record reach a consistent terminal state without external intervention.

## Assumptions

- The Sorcha peer sync layer is assumed reliable enough that a transaction sealed on one node reaches other subscribed nodes within the 30-second target used in SC-002 and SC-003. If peer sync latency degrades, the target degrades with it — the feature does not add its own retry or acceleration layer.
- The holder's wallet bloom filter registration is assumed to be kept in sync with their actual wallet set; Fix A (PR #288) established that pending-actions resolution is self-healing when the bloom filter is stale, but the inbound credential detection path assumes the address is at least eventually present in the filter.
- Holders operate with a single primary wallet per credential in the typical case. Multi-wallet users receive one pending credential per matching wallet, deduplicated by credential id.
- The holder has already created a wallet before any credential arrives. First-time wallet creation is out of scope for this feature and is handled by the consumer-onboarding work (Feature 105) that runs in parallel.
- The issuer blueprint action specifies a recipient participant that has been late-bound to the holder's wallet via the open-participant mechanism earlier in the instance's lifetime. Blueprint authors are responsible for using the existing late-binding pattern correctly; the feature does not change late-binding semantics.
- External wallets (mobile phones, non-Sorcha holders) are out of scope — they continue to use the existing external-wallet delivery mode. Feature 106 is strictly for on-platform wallets.
- The holder is expected to be online to receive notifications. Offline-first holders (wallets that sync intermittently) are supported insofar as pending credentials wait in the local store until the holder's UI next opens, but real-time notification latency targets don't apply while offline.

## Dependencies

- The full Feature 103/104 fix chain (PRs #285, #286, #287, #288, #290) must be deployed; the feature's single-node demo path builds on the browser flow those PRs restored.
- The existing recipient-addressed disclosure encryption primitive must remain the authoritative encryption layer; the feature does not introduce an alternative.
- The existing SD-JWT verifiable credential format and its holder-key-binding claim continue to be the credential shape; the feature only changes how the credential is delivered.
- Feature 105 (consumer onboarding / persona capture) informs the user journey context but is not a hard dependency. This feature ships whether or not Feature 105 has landed; it does not require persona data to function.

## Out of Scope

- Reworking presentation protocols (OpenID4VP, KB-JWT) — presentation flows are unchanged.
- New cryptographic primitives — reuses the existing encryption layer end-to-end.
- New credential formats — still SD-JWT VC.
- Removing or deprecating the external-wallet delivery mode — it remains load-bearing for mobile and cross-wallet federation scenarios.
- Workflow-level "holder response TTL" or auto-expiry of unaccepted credentials — the credential's embedded validity window is the single expiry signal.
- Migrating existing in-flight blueprint instances from the external-wallet path to register-native delivery — in-flight instances continue through their original path unchanged.
- Holder-side batch operations on the pending credentials inbox (bulk accept, bulk decline, filter-by-issuer, etc.) — future UX work after this feature ships.
- Decline-reason collection and surfacing to the issuer — future enhancement tracked separately.
