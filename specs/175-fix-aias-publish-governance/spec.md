# Feature Specification: Fix AIAS Demo Blueprint-Publish Governance Gap

**Feature Branch**: `175-fix-aias-publish-governance`

**Created**: 2026-06-30

**Status**: Draft

**Input**: User description: "Fix AIAS demo blueprint-publish 403 governance gap. In demos/AIAS/AiasDemo.psm1 the AIAS register is created owned by the sysadmin (docker) account, but the blueprint is then published by the AIAS verification-admin wallet, which has NO publish-governance role on that register, so POST /api/blueprints/{id}/publish returns 403 (Blueprint PublishGate: caller lacks a publish-governance role on register). The same governance gap also causes the 90s participant-publish seal timeout and the public-org auto-subscribe 500 seen during provisioning. FIX: make the AIAS register publishable by the AIAS org - either create the register OWNED BY the AIAS verification-admin/issuer wallet, OR grant that wallet a publish-governance role on the register before Publish-AiasBlueprint runs. Mirror how demos/AssuredIdentity establishes register ownership/governance for its publishing wallet. VERIFY: running demos/AIAS/run-demo.ps1 against a clean Docker stack reaches blueprint publish with NO 403 and writes the agent config. EDIT ONLY demos/AIAS/ (primarily AiasDemo.psm1, plus the shared walkthrough governance helper only if strictly required). Do NOT touch src/Apps/Sorcha.Agent, the verify path, or any service code - this is a provisioning-script governance fix."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - AIAS demo provisions through to blueprint publish (Priority: P1)

A developer or demo operator runs the AIAS demo provisioning script against a clean Docker stack. The script establishes the AIAS issuing authority — an organisation, a verification-admin (issuer) wallet, a register, and a published blueprint — and then writes the agent configuration so the AIAS agent can run. Today provisioning fails because the publishing wallet has no publish-governance authority on the register it must publish to.

**Why this priority**: This is the entire purpose of the fix. Without it, the AIAS demo cannot complete provisioning and the agent config is never written, so the demo is unusable end-to-end. Every other observed failure (participant-publish timeout, public-org subscribe error) is a downstream symptom of the same governance gap.

**Independent Test**: Run the AIAS demo entry script against a freshly started, clean Docker stack and confirm provisioning reaches the blueprint-publish step, completes it with no HTTP 403, and writes the agent configuration file.

**Acceptance Scenarios**:

1. **Given** a clean Docker stack and no prior AIAS state, **When** the AIAS demo provisioning runs, **Then** the AIAS register is created such that the verification-admin (issuer) wallet holds publish-governance authority over it.
2. **Given** the AIAS register has been created with the publishing wallet holding governance authority, **When** the AIAS blueprint is published by the verification-admin wallet, **Then** the publish request succeeds (no HTTP 403 "caller lacks a publish-governance role on register").
3. **Given** the blueprint publish has succeeded, **When** provisioning continues, **Then** the agent configuration is written and the demo reports a successful authority-ready state.

---

### User Story 2 - Participant publish and public-org subscription complete without governance-related failures (Priority: P2)

While provisioning the AIAS authority, the script publishes the verification participant onto the register and ensures the Sorcha public organisation can subscribe to it (so consumer/public-discovery flows can read the register). Today these steps fail or stall — a ~90 second participant-publish seal timeout and an HTTP 500 on public-org auto-subscribe — because the same wallet/register governance relationship is missing.

**Why this priority**: These are downstream symptoms of the same root cause. Once the publishing wallet owns/governs the register, the participant seal and public-org subscription are expected to resolve as a side effect. They are called out separately so that verification explicitly confirms the symptoms are gone, not just the primary 403.

**Independent Test**: During the same clean-stack provisioning run, observe that the participant-publish step completes within the normal readiness window (no ~90s seal timeout) and that the public-org subscription step does not return a server error.

**Acceptance Scenarios**:

1. **Given** the register is owned/governed by the publishing wallet, **When** the verification participant is published onto the register, **Then** the participant seals without hitting the ~90s timeout.
2. **Given** the register is advertised/public, **When** the Sorcha public organisation is subscribed to it, **Then** the subscription succeeds with no HTTP 500.

---

### User Story 3 - Re-running the demo remains safe and idempotent (Priority: P3)

A developer re-runs the AIAS demo after a previous (partial or complete) run, against the same or a clean stack. The governance change must not introduce duplicate registers, conflicting ownership, or a failure on re-run.

**Why this priority**: The shared walkthrough provisioning helpers already implement idempotent reuse of registers by name; the fix must preserve that behaviour rather than regress it. This protects the common developer loop of running the demo repeatedly during iteration.

**Independent Test**: Run the AIAS demo twice in succession and confirm the second run reuses the existing authority (or cleanly re-provisions) and still reaches a successful authority-ready state without governance errors.

**Acceptance Scenarios**:

1. **Given** a prior successful AIAS provisioning, **When** the demo is run again, **Then** it reuses the existing register/blueprint where appropriate and does not error on ownership or governance.

---

### Edge Cases

- **Publishing wallet differs from the org admin that creates the register.** The register-creation flow must sign the ownership attestation with the wallet that will own/govern the register (the verification-admin wallet), even when a different identity drives the create call. The shared helper already supports a distinct wallet-signer context for exactly this case; the fix must use it correctly.
- **The AIAS demo assets are not yet present in this working tree.** The provisioning script (`demos/AIAS/AiasDemo.psm1`) and entry script (`demos/AIAS/run-demo.ps1`) referenced by the request are the surface to be corrected; the fix is scoped to that demo. (See Assumptions.)
- **Register already exists by name on re-run.** Ownership/governance must remain correct for the reused register; the fix must not silently leave a reused register owned by the wrong identity such that publish fails again.
- **Two governance approaches are acceptable.** Either create the register owned by the verification-admin/issuer wallet, OR grant that wallet a publish-governance role on the register before publish runs. The mirror of AssuredIdentity (create-owned-by) is the preferred approach.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The AIAS demo provisioning MUST result in the verification-admin (issuer) wallet holding publish-governance authority over the AIAS register before the AIAS blueprint is published.
- **FR-002**: Provisioning MUST achieve FR-001 by one of two means, mirroring the AssuredIdentity demo: (a) creating the register owned by the verification-admin/issuer wallet, or (b) granting that wallet a publish-governance role on the register prior to the blueprint-publish step. Approach (a) is preferred.
- **FR-003**: The blueprint-publish step MUST complete without an HTTP 403 "caller lacks a publish-governance role on register" when run by the verification-admin wallet.
- **FR-004**: Provisioning MUST complete the participant-publish step within the normal readiness window, without the previously observed ~90 second seal timeout.
- **FR-005**: Provisioning MUST complete the Sorcha public-organisation subscription to the AIAS register without an HTTP 500.
- **FR-006**: Provisioning MUST write the AIAS agent configuration after a successful blueprint publish, so the demo reaches an authority-ready state.
- **FR-007**: The register-ownership attestation MUST be signed by the wallet that will own/govern the register (the verification-admin wallet), accommodating the case where the identity that drives register creation differs from the register owner.
- **FR-008**: The change MUST preserve idempotent re-run behaviour: re-running the demo MUST NOT create conflicting ownership or fail due to an already-existing register, and MUST still reach an authority-ready state.
- **FR-009**: The change MUST be confined to the AIAS demo directory (`demos/AIAS/`, primarily the provisioning module). The shared walkthrough governance helper MAY be modified only if strictly required to express owner-wallet governance, and any such change MUST preserve the behaviour of existing callers (e.g. the AssuredIdentity and Membership demos).
- **FR-010**: The change MUST NOT modify the AIAS agent application, the verification (verify) path, or any platform service code; this is a provisioning-script governance fix only.

### Key Entities *(include if feature involves data)*

- **AIAS register**: The distributed register the AIAS authority publishes its blueprint to. Has an owner identity and an associated publish-governance authority that gates who may publish blueprints to it.
- **Verification-admin (issuer) wallet**: The AIAS organisation's publishing identity. Must hold publish-governance authority over the AIAS register. This is the wallet that signs the register-ownership attestation and publishes the blueprint and participant.
- **AIAS blueprint**: The workflow definition published to the AIAS register; publishing it is gated by the register's publish-governance authority.
- **Sorcha public organisation**: The well-known public org subscribed to the AIAS register so public/consumer-discovery flows can read it.
- **Agent configuration**: The output artefact written once provisioning succeeds, enabling the AIAS agent to operate against the provisioned authority.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Running the AIAS demo entry script against a clean Docker stack reaches the blueprint-publish step and completes it with zero HTTP 403 governance failures.
- **SC-002**: A clean-stack run produces the AIAS agent configuration file (authority-ready state reached) with no manual intervention.
- **SC-003**: The participant-publish step completes within the normal readiness window, eliminating the previously observed ~90 second seal timeout (0 occurrences).
- **SC-004**: The public-organisation subscription step completes with zero HTTP 500 responses.
- **SC-005**: The AssuredIdentity and Membership demos continue to provision successfully (no regression introduced by any shared-helper change), confirmed by an unchanged-or-passing run of each.
- **SC-006**: All file changes are contained within `demos/AIAS/` (and, only if strictly required, the single shared walkthrough governance helper); no changes appear under `src/`.

## Assumptions

- The AIAS demo assets (`demos/AIAS/AiasDemo.psm1`, `demos/AIAS/run-demo.ps1`) are the intended target of this fix. They are not present in the current working tree alongside the existing `demos/AssuredIdentity` and `demos/Membership` demos; the fix is authored against the AIAS demo surface as described, treating AssuredIdentity as the established reference pattern.
- The AssuredIdentity demo is the canonical reference: it creates its register owned by the verification-admin wallet and signs the ownership attestation with that wallet, which is the pattern to mirror for AIAS.
- The shared walkthrough register-creation helper already supports specifying both an owner wallet and a distinct wallet-signer context, so the preferred approach (create register owned by the issuer wallet) can be expressed without changing service code, and likely without changing the shared helper at all.
- "Publish-governance role" is the register-side authority that the platform's blueprint publish gate checks; conferring register ownership on the publishing wallet satisfies this gate (as demonstrated by AssuredIdentity).
- A clean Docker stack with the standard sysadmin/docker bootstrap account is the verification environment, consistent with how the other demos are exercised.
- Verification is performed by running the demo end-to-end; no automated unit test is required for this provisioning-script fix beyond the successful run and the non-regression of sibling demos.
