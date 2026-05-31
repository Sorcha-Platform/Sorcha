# Feature Specification: Assured Identity Demo Environment

**Feature Branch**: `144-assured-identity-demo`  
**Created**: 2026-05-31  
**Status**: Draft  
**Input**: User description: "Assured Identity Demo Environment — a standing, node-agnostic two-installation demo plus a self-service provisioning toolkit, built on the approved design at docs/superpowers/specs/2026-05-31-assured-identity-demo-environment-design.md"

## Overview

Turn the proven cross-installation Assured Identity loop into a **standing demo environment** anyone can stand up, operate, rebrand, and reset — where a tester goes, **unscripted through the real product**, from anonymous sign-up to a verified identity credential in their wallet. The decision-making components (topology, agent mode, tester journey, rebrand coherence, readiness) were settled in the approved design note; this spec defines the *what* and *why* for building the operability layer on top.

A guiding concept: **a demo is a mature walkthrough**. The scripted dev-facing walkthrough has proven the path; graduating it to a demo means it becomes coherent, node-agnostic, operable by parameter, and exercised by a human through real UIs — at which point the legacy scripts are retired.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Stand up a demo and complete the credential loop (Priority: P1)

An operator stands up a fresh issuing authority on an issuer installation and connects a public subscriber installation, then hands a tester a plain web address. The tester — with no script and no special tooling — signs up, is guided by the product to pair a wallet, applies for an assured identity, and shortly receives the credential in their wallet. The approval in between is performed automatically by the identity-validator agent.

**Why this priority**: This is the entire purpose of the feature — a repeatable, self-contained demonstration of the full decentralised identity-assurance loop across two installations. Everything else refines or extends it.

**Independent Test**: From a clean state, run the documented provision + connect sequence, then have a person who has never seen the system complete anonymous-sign-up → application → credential-in-wallet entirely through the real UIs. Success = the credential appears in the tester's wallet and the tester never encounters a transient "service unavailable" error.

**Acceptance Scenarios**:

1. **Given** two clean installations (one issuer, one public subscriber), **When** the operator runs the provisioning step on the issuer and the connect step on the subscriber, **Then** both report "ready" only after the agency's service is actually available to citizens on the subscriber.
2. **Given** a readied demo, **When** a first-time tester signs up on the subscriber, pairs a wallet, discovers the agency's service, and submits an application, **Then** the application is accepted and routed to the issuer for approval without the tester taking any out-of-band step.
3. **Given** a submitted application, **When** the identity-validator agent approves it, **Then** the resulting credential is delivered to the tester's wallet and is visible in their wallet app.
4. **Given** a tester who arrives immediately after the connect step reports "ready", **When** they start an application, **Then** they do not see a transient "service not yet available" error.

---

### User Story 2 - Choose how applications are approved (Priority: P2)

An operator decides, per demo run, whether applications are approved by a deterministic ruleset, by an AI persona that reads each application and decides, or by a human acting as the verification analyst.

**Why this priority**: The approval step is the demonstration's most flexible storytelling moment ("a rule decided", "an AI assessed you", "an officer reviewed you"). It must be a single, low-friction choice, but the loop is fully demonstrable with the default deterministic mode alone.

**Independent Test**: Run provisioning three times selecting each mode; in every case a submitted application results in an approved credential, and in human mode the operator is given clear approval instructions instead of an automatic decision.

**Acceptance Scenarios**:

1. **Given** the operator selects the deterministic mode (the default), **When** an application is submitted, **Then** it is approved automatically and quickly enough that a live demo does not stall.
2. **Given** the operator selects the AI mode, **When** an application is submitted, **Then** the AI persona produces a decision, and if it is slow or fails the tester is not left stranded (a safe fallback or clear status is surfaced).
3. **Given** the operator selects the human mode, **When** provisioning completes, **Then** no automatic approver runs and the operator receives instructions for approving as the analyst.

---

### User Story 3 - Rebrand and customise the issuing authority (Priority: P2)

An operator stands up an authority under a chosen agency name (e.g. a fictional council or registry), and the name appears consistently everywhere a tester sees it — including on the credential they receive. For deeper changes to the application itself, the operator uses the real product's design tools.

**Why this priority**: The vision explicitly includes testers standing up *their own* authority. Name coherence across the org, the register, and the credential is what makes a rebrand believable; getting it wrong undermines the demo.

**Independent Test**: Provision with a non-default agency name; complete the loop; confirm the credential's displayed issuer and every tester-visible label match the chosen name with no manual editing. Separately, amend the application's form through the product design tools and confirm the change appears without breaking the authority's identity.

**Acceptance Scenarios**:

1. **Given** the operator supplies an agency name, **When** the authority is provisioned, **Then** the org, the published service, and the credential's displayed issuer all reflect that single name.
2. **Given** a provisioned authority, **When** the operator re-runs provisioning with a different agency name, **Then** the authority is coherently rebranded without leaving stale references to the previous name on the tester-visible path.
3. **Given** a provisioned authority, **When** the operator amends the application's form via the product design tools and republishes, **Then** the workflow change takes effect while the authority's identity (org, wallet, register, credential issuer) remains intact.

---

### User Story 4 - Add more independent nodes to the demo (Priority: P3)

An operator points the demo at different installations, renames them, or adds further independent public installations that also subscribe to and host the agency's register, so testers can be served from more than one node.

**Why this priority**: Demonstrating that independent nodes can replicate and serve the same register is a core decentralisation message, but the headline demo works with a single subscriber, so this extends rather than gates the MVP.

**Independent Test**: With an authority already provisioned, connect a second, independent subscriber installation; confirm a tester on that second node can complete the full loop and receive the credential.

**Acceptance Scenarios**:

1. **Given** an installation inventory, **When** the operator selects different installations for issuer and subscriber roles, **Then** the demo provisions against those installations without editing the toolkit itself.
2. **Given** an authority advertised by the issuer, **When** the operator connects an additional independent subscriber installation, **Then** that node replicates the register and a tester on it can complete the loop.
3. **Given** multiple subscriber installations, **When** each connect step reports "ready", **Then** each was independently readiness-checked.

---

### User Story 5 - Reset and check demo health (Priority: P3)

An operator returns a node (or the whole demo) to a clean pre-provision state, and at any time can ask "is the demo ready for a tester right now?" and get a clear cross-node answer.

**Why this priority**: Operating a standing demo repeatedly requires a reliable reset and an at-a-glance readiness check; without them the demo rots between sessions. It supports the MVP rather than being part of the first proof.

**Independent Test**: After a completed run, reset the demo and confirm a subsequent fresh provision succeeds; query status before and after provisioning and confirm the reported readiness matches whether a tester can actually complete the loop.

**Acceptance Scenarios**:

1. **Given** a previously run demo, **When** the operator resets it, **Then** a subsequent provision behaves as if starting clean (no leftover state blocks it).
2. **Given** any point in the demo lifecycle, **When** the operator queries status, **Then** they receive a cross-node report of health, subscription state, service availability, and approver state that correctly predicts tester success.

---

### User Story 6 - Graduate the walkthrough to a demo (Priority: P3)

Once the demo runs green end-to-end, the maintainer retires the legacy scripted walkthrough and scratch scripts, and the project's guidance reflects that "the Assured Identity walkthrough" now means this demo.

**Why this priority**: Leaving two parallel artefacts (legacy walkthrough + new demo) causes drift and confusion. This consolidation is the explicit close-out of the graduation concept, but it must come *after* a proven green run, so it is sequenced last.

**Independent Test**: After a green demo run, confirm the legacy AssuredIdentity walkthrough and two-installation scratch scripts are removed, the demo lives in a first-class demo location, and the project guidance and memory describe the demo (not the retired scripts) as the canonical Assured Identity experience.

**Acceptance Scenarios**:

1. **Given** a green end-to-end demo run, **When** the maintainer performs graduation cleanup, **Then** no legacy AssuredIdentity walkthrough or two-installation scratch script remains, while shared reusable helpers are preserved.
2. **Given** the graduated demo, **When** a new contributor looks for "the Assured Identity walkthrough", **Then** project guidance and memory direct them to the demo and its location.

---

### Edge Cases

- **Re-provisioning an existing authority**: the provisioning step detects and reuses existing artefacts rather than duplicating, including reconciling the known footgun where a stale subscription record points at a register that no longer exists.
- **Tester arrives during the readiness window**: a tester who somehow starts before the service is recoverable is given a clear, non-alarming "not quite ready" state, not an opaque failure.
- **Approver unavailable or slow (AI mode)**: a slow or failing AI decision must not silently strand a tester mid-application.
- **Subscriber connected but issuer offline**: status reflects the broken link rather than reporting "ready".
- **Tester has no wallet device yet**: the product's own onboarding must carry them to a paired wallet before the credential can be delivered; the demo relies on this and adds no scaffolding around it.
- **Reset while a tester is mid-flow**: reset is an operator action assumed to occur between tester sessions; the spec does not guarantee graceful behaviour for an in-flight tester during a reset.
- **Multiple subscribers at different sync points**: one subscriber lagging in replication must not be reported as "ready".

## Requirements *(mandatory)*

### Functional Requirements

#### Provisioning & coherence

- **FR-001**: The system MUST provide a single operation that stands up a complete issuing authority on a chosen issuer installation — organisation, issuer identity, verification analyst, advertised register, and published application — usable with sensible defaults so a zero-configuration run produces a working authority.
- **FR-002**: The agency name MUST be a single supplied value that propagates consistently to the organisation, the published service, the analyst's published participant record, and the issuer name displayed on the credential the tester receives.
- **FR-003**: Re-running provisioning MUST be idempotent: existing authority artefacts are detected and reused rather than duplicated, and stale subscription-vs-missing-register state is reconciled rather than blindly reused.
- **FR-004**: The system MUST provide an operation that connects a chosen subscriber installation to an advertised register and MUST NOT report "ready" until the agency's service is actually available for a citizen to start an application on that node.
- **FR-005**: Re-running provisioning with a different agency name MUST produce a coherently rebranded authority with no stale references to the prior name on the tester-visible path.

#### Node-agnostic configuration & multi-node

- **FR-006**: Installation identity (address, installation name, role, reachability) MUST be configuration, not hard-coded, so installations can be swapped or renamed without changing the toolkit.
- **FR-007**: The operator MUST be able to select which installations act as issuer and subscriber by reference to the configured inventory.
- **FR-008**: The system MUST allow additional independent subscriber installations to connect to and replicate the same advertised register, each independently readiness-checked.
- **FR-009**: Each installation MUST retain its own trust material; a node joins by subscribing to the advertised register, never by sharing signing keys.

#### Approval agent

- **FR-010**: The operator MUST be able to choose, per provisioning run, how applications are approved: a deterministic ruleset (default), an AI persona, or a human.
- **FR-011**: In deterministic and AI modes the system MUST start the approving agent as part of provisioning; in human mode it MUST instead provide the operator with instructions to approve as the analyst.
- **FR-012**: In AI mode the system MUST ensure a slow or failed decision does not leave a tester stranded — via a bounded wait and a defined fallback or clearly surfaced status.
- **FR-013**: The default deterministic approval MUST complete quickly enough that a live demonstration does not visibly stall.

#### Tester journey (relies on existing product surfaces — no scaffolding)

- **FR-014**: A signed-in citizen MUST be able to discover the agency's service and start an application through the real product UI, without any demo-specific scaffolding or scripting.
- **FR-015**: After approval, the credential MUST be delivered to the tester's wallet and be visible in their wallet app.
- **FR-016**: The demo MUST NOT introduce new tester-facing UI; it relies on the product's existing onboarding, application, and wallet surfaces. Product surfaces known to be incomplete MUST be explicitly recorded as out of scope rather than worked around.

#### Operations

- **FR-017**: The system MUST provide an operation to reset a node, or the whole demo, to a clean pre-provision state such that a subsequent provision behaves as if starting fresh.
- **FR-018**: The system MUST provide an operation that reports, across all configured installations, whether the demo is ready for a tester — covering service health, subscription state, service availability, and approver state — such that the report reliably predicts tester success.

#### Documentation, graduation & alignment

- **FR-019**: The demo MUST live in a first-class demo location distinct from dev-facing scripted walkthroughs.
- **FR-020**: The demo MUST ship with an operator runbook (provision, connect, reset, status, agent modes, multi-node) and a tester runbook (the unscripted real-UI journey).
- **FR-021**: After a verified green end-to-end run, the legacy scripted Assured Identity walkthrough and two-installation scratch scripts MUST be retired, while shared reusable helpers are preserved. This cleanup MUST be sequenced as the final step, gated on the green run.
- **FR-022**: Project guidance and memory MUST be updated so that "the Assured Identity walkthrough" resolves to this demo, reflecting the concept that a demo is a mature walkthrough and recording the node-agnostic, multi-node nature and the demo's location.

### Key Entities *(include if feature involves data)*

- **Installation (inventory entry)**: a participating node — its reference id, network address, installation name, role (issuer or subscriber), and whether it can be reached for inbound rendezvous. The unit by which the operator selects topology.
- **Issuing authority**: the provisioned identity-assurance organisation — agency name, organisation, issuer identity, verification analyst, advertised register, and the published application. The thing the agency-name value makes coherent.
- **Approval agent configuration**: the chosen approval mode and the analyst identity it acts as; for non-human modes, the running approver.
- **Demo state record**: the provisioned artefact references that make provisioning idempotent and reset reliable.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a clean state, an operator can bring the demo to "ready" (issuer provisioned + one subscriber connected) by following the runbook in under 10 minutes.
- **SC-002**: A first-time tester, given only a web address and no script, completes anonymous sign-up → application → credential-in-wallet in under 5 minutes, and encounters no transient "service unavailable" error.
- **SC-003**: Provisioning is idempotent: running it twice in succession against the same installation produces no duplicate authority and leaves the demo working, verified across at least two consecutive runs.
- **SC-004**: Changing only the agency-name value yields a credential whose displayed issuer and all tester-visible labels match the chosen name, with zero manual edits.
- **SC-005**: An operator switches approval mode between runs with a single choice, and all three modes (deterministic, AI, human) result in an approved credential reaching the tester's wallet.
- **SC-006**: At least one additional independent subscriber installation can be connected, and a tester served from that node completes the full loop.
- **SC-007**: The status report's "ready / not ready" verdict matches actual tester success in 100% of checks during acceptance testing.
- **SC-008**: After graduation, no legacy Assured Identity walkthrough or two-installation scratch script remains, and a contributor who has never run the demo can complete it using only the demo runbook.

## Assumptions

- **Topology and transport are proven and in place**: the cross-installation reverse-stream reachability, asynchronous cross-node submission, and cross-node credential delivery this demo depends on are already delivered and live-verified; this feature builds the operability layer on top, not the transport.
- **Default node pair**: the default issuer is a NAT'd owner installation and the default subscriber is a public installation, matching the proven configuration; these are defaults in the inventory, not assumptions baked into the toolkit.
- **Default demo location**: the demo is assumed to live at a top-level demo directory for the Assured Identity demo (e.g. `demos/AssuredIdentity/`); the exact path is confirmable during planning without changing scope.
- **Tester journey uses existing product surfaces**: the citizen sign-up, wallet-device onboarding, service-discovery/start-application, and credential-delivery surfaces already exist in the product and are sufficient for the unscripted journey; the demo adds no UI.
- **Out-of-scope product surfaces**: the wallet app's in-app "applications" listing and the sample council portal pages are known to be incomplete and are explicitly not on the demo path.
- **AI-mode guardrail default**: when AI approval is selected, a bounded wait with a defined fallback (or clearly surfaced status) is acceptable for a demo; the precise bound and fallback are settled during planning.
- **Secrets handling**: per-installation trust material and signing keys are supplied to the toolkit through the existing secret store, not committed.
- **Single concurrent tester assumption for reset**: reset is an operator action performed between tester sessions; graceful mid-flow reset is not a requirement.

## Dependencies

- The proven cross-installation Assured Identity loop and its supporting fixes (reverse-stream rendezvous, async cross-node submit, replica reconcile, seal-persist) — must remain deployed on the participating installations.
- The autonomous approval agent capability with deterministic and AI decision modes.
- The product's existing citizen onboarding, application-start, and wallet surfaces.
- The product's design tools for deeper application customisation (US3 deep-customise path).

## Out of Scope

- Any new tester-facing UI or guided concierge overlay.
- Completing the wallet app's in-app application listing or the sample council portal pages.
- Hardening core product paths (e.g. making application-start wait for in-flight service recovery) — readiness gating in the toolkit is sufficient for the demo.
- Relayed multi-hop mesh routing between nodes; multi-node here means multiple independent subscribers of one advertised register.
- Production-grade operation (scaling, monitoring, SLAs) of the demo environment.
