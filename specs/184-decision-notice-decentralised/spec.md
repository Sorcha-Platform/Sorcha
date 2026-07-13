# Feature Specification: Decentralised decision notice + reason codification

**Feature Branch**: `fix/183-decision-notice-citizen-recipient` (continuation branch — no new branch per user instruction)

**Created**: 2026-07-13

**Status**: Draft

**Input**: Follow-up to Feature 183 US2. Relocate the `x-decision-notice` reject notice from the inline `ActionExecutionService` hook to the entitlement-gated `ReactionDispatcher`, so it fires on the **citizen's own node** as that node folds the inbound sealed transaction. Carry a non-sensitive reason **code** plus the taken route id on the sender-signed `RoutingDecision`, and resolve the code to citizen-facing text from the replicated blueprint. Clean break: remove the free-text `reasonField` path and `DecisionNoticeDispatcher`.

**Approved design**: `docs/superpowers/specs/2026-07-13-aias-decision-notice-decentralised-design.md` — authoritative for all implementation decisions.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A rejected applicant sees why, on their own node (Priority: P1)

A citizen applies for an assured-identity credential through a council/agency web page. An autonomous
agent — running on the **issuing agency's** node, not the citizen's — evaluates the application and
rejects it. The citizen's account, wallet and notification inbox live on their **own** node. When the
rejecting decision replicates to that node and is folded into the shared ledger-derived instance, the
citizen receives a durable notification carrying the reason, visible in their bell drawer, surviving
reload, re-login and device change.

**Why this priority**: This is the whole feature. Today the notice fires only on the agency's node,
where the citizen has no account, so a rejected applicant receives nothing at all — the application
silently disappears. Everything else here exists to make this work.

**Independent Test**: Reject an application and confirm a durable inbox entry appears for the applicant,
carrying the decision reason. On a single-node deployment the citizen's node *is* the folding node, so
the path is exercised end-to-end; on a federated split, only the citizen's node produces the entry.

**Acceptance Scenarios**:

1. **Given** a citizen whose wallet is hosted on node C and an agent whose submission is processed on
   node A, **When** the reject transaction seals and replicates, **Then** node C writes exactly one
   durable decision notice for the citizen and node A writes none.
2. **Given** a rejected application, **When** the citizen opens their notification drawer, **Then** they
   see the decision title and the reason for the rejection.
3. **Given** a rejected application whose notice has already been delivered, **When** the same sealed
   transaction is re-observed (replay, restart, or instance rebuild), **Then** no duplicate notice is
   created.
4. **Given** an *approved* application, **When** it completes, **Then** no decision notice is written
   (approval is already surfaced by the credential arriving).

---

### User Story 2 - The reason survives an encrypted register (Priority: P1)

The reason shown to the citizen is carried as a short, non-sensitive **code** on the transaction's public
metadata, signed by the deciding participant. The citizen-facing wording lives in the blueprint — the
shared, replicated contract every node holds. The citizen's node therefore renders the reason without
decrypting anything and without holding any delegation on the applicant's behalf.

**Why this priority**: Same priority as US1 because US1 cannot be delivered without it. A background
process on the citizen's node has no means to read encrypted application payload, and copying free-text
analyst prose into public metadata would leak it to every node holding the register.

**Independent Test**: Confirm the sealed transaction's public metadata carries a reason **code** and no
free text, and that the delivered notice text matches the blueprint's wording for that code.

**Acceptance Scenarios**:

1. **Given** an agent that rejects for a specific reason, **When** the transaction seals, **Then** its
   public metadata carries the corresponding reason code and the taken route's identity, and carries no
   free-text reason.
2. **Given** a reason code carried on a decision, **When** the citizen's node delivers the notice,
   **Then** the notice text is the blueprint's wording for that code.
3. **Given** a decision carrying a code the blueprint does not define (or no code at all), **When** the
   notice is delivered, **Then** the citizen sees the declared fallback wording rather than nothing.
4. **Given** the reason code and route identity on a sealed decision, **When** a node other than the
   deciding one reads them, **Then** any alteration in transit is detectable (they are covered by the
   deciding participant's signature).

---

### Edge Cases

- **Citizen's wallet is not hosted on the folding node** — the node skips quietly; delivery is the
  responsibility of the node that hosts the wallet. No misfire, no error.
- **The applicant is a late-bound open participant** with no participant-registry record — the recipient
  is resolved from the sending wallet's owner instead. (Already implemented on this branch.)
- **The recipient participant is not bound to a wallet on the instance** — no notice; logged and skipped.
- **The blueprint no longer contains the route the decision names** (version drift) — no notice; logged
  and skipped.
- **The route carries no decision notice** — the ordinary case for the vast majority of routes; nothing
  happens.
- **The notice's inbox write fails** — logged and swallowed; the workflow's sealed state and the folded
  instance are unaffected.
- **The decision is a non-terminal route** (e.g. "returned for more information") — a notice still fires;
  delivery is not conditional on the workflow ending.
- **Transactions sealed before this feature** carry no route identity — they simply produce no notice.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST deliver a decision notice from the node that hosts the recipient's wallet,
  triggered by that node folding the inbound sealed decision transaction — not from the node that
  processed the deciding participant's submission.
- **FR-002**: Exactly one notice MUST be delivered per (sealed decision, recipient), regardless of how
  many nodes fold the transaction or how many times a node re-observes it.
- **FR-003**: A node that does not host the recipient's wallet MUST NOT deliver the notice, and MUST NOT
  error.
- **FR-004**: A decision MUST carry, on the transaction's public metadata, the identity of the route the
  deciding participant took and (where the route declares a decision notice) a non-sensitive reason code.
- **FR-005**: Both values MUST be covered by the deciding participant's existing routing attestation
  signature, so that alteration by a relaying node is detectable.
- **FR-006**: The citizen-facing wording for each reason code MUST be declared in the blueprint's route
  annotation and resolved from the replicated blueprint on the recipient's node.
- **FR-007**: The recipient's node MUST resolve the notice without decrypting application payload and
  without holding any delegated authority on the recipient's behalf.
- **FR-008**: An absent or unrecognised reason code MUST resolve to the route's declared fallback wording.
- **FR-009**: No free-text reason may be carried on the transaction's public metadata. The free-text
  reason field on the notice annotation is removed.
- **FR-010**: A notice failure at any step MUST NOT affect sealing, routing, or the folded instance state.
- **FR-011**: Notices MUST fire for terminal and non-terminal routes alike.
- **FR-012**: The audit record of the decision (the deciding participant's own notes, as disclosed on the
  ledger) MUST be preserved unchanged; it is simply no longer the delivery mechanism for the notice.
- **FR-013**: Delivery MUST reuse the existing recipient resolution (participant registry first, falling
  back to the sending wallet's owner) so late-bound citizens resolve correctly.

### Key Entities

- **Routing decision**: The deciding participant's signed, publicly-readable statement of what a
  submission routed to. Gains the taken route's identity and an optional reason code.
- **Decision notice annotation**: A blueprint route annotation declaring who is notified, which payload
  field carries the reason code, the notice title and severity, the code → citizen-facing message
  catalogue, and the fallback message.
- **Reaction**: An entitlement-gated, idempotent side effect fired by a node after it folds a sealed
  transaction into the instance. A decision notice becomes one of these.
- **Inbox entry**: The durable, cross-session notification record the citizen sees.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A rejected applicant sees the reason for the decision in their notification drawer, and it
  is still there after a page reload and after signing out and back in.
- **SC-002**: In a deployment where the applicant and the deciding agent are on different nodes, exactly
  one notice reaches the applicant, produced by the applicant's node.
- **SC-003**: The reason the applicant sees matches the wording the blueprint declares for the coded
  reason the decision carried.
- **SC-004**: No free-text reason appears anywhere in a sealed transaction's public metadata.
- **SC-005**: Re-observing a sealed decision (restart, replay, or a full instance rebuild) produces no
  duplicate notice.
- **SC-006**: An approved application produces no decision notice.

## Assumptions

- The citizen is **not** co-located with the deciding agent by default; single-node deployments are the
  degenerate case where the citizen's node and the agent's node are the same, and must behave correctly
  under the same code path.
- The blueprint is replicated to every node holding the register (it is published on the register), so the
  code → message catalogue is available wherever the notice is delivered.
- The deciding participant (the agent) is the authority on its own decision, so no independent validator
  rule is needed for the route identity or reason code beyond the existing signature check.
- The autonomous agent emits the reason code through its existing rules-file payload mechanism; no agent
  code change is required.
- Approval visibility is already covered (the credential arrives in the wallet), so notices are used for
  rejections and other non-approval decisions.
- Email-on-decision and an applicant-facing "My Applications" history page remain out of scope (tracked
  on issue #1163).
