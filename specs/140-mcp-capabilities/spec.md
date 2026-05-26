# Feature Specification: MCP Server Capability Gap Closure

**Feature Branch**: `140-mcp-capabilities`
**Created**: 2026-05-26
**Status**: Draft
**Input**: Feature 2 of the MCP remediation milestone (Waves 1–4). Builds on Feature 1 (`139-mcp-foundation`). Full design and rationale: `docs/superpowers/specs/2026-05-26-mcp-server-remediation-design.md`.

## Overview

Once the MCP Server Foundation (Feature 139) makes the existing 36-tool surface work — tiered by token, privileges enforced by the platform, reachable over both transports — the server still cannot do many things the platform supports. An AI agent cannot manage register federation (operators have had to drive register sync by hand), issue or revoke credentials, run a presentation flow, manage a citizen's own wallet/devices/persona, or perform deeper platform administration.

This feature closes those gaps by adding new tools across four independently-shippable waves. Every new tool inherits the Foundation's contract: it executes with the caller's credentials, is tier-scoped, routes through the typed service clients (no hand-rolled endpoints), is covered by the live integration safety net, and is reflected in the advertised catalogue.

**This feature depends on Feature 139 being complete.** It introduces no new auth, transport, or enforcement mechanisms — only new tools on the established foundation.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register control & federation (Wave 1) (Priority: P1)

An operator (platform-admin) uses an AI agent to manage register membership and federation: subscribe a node to a register, check a register's sync state and this node's relationship to it, submit and verify transactions (receipt, inclusion proof, verification bundle), and revoke a transaction — without dropping to manual service calls.

**Why this priority**: This is the gap that triggered the whole review. The federation work (cross-node genesis sync) forced operators to drive register sync through direct service calls because no MCP tools existed. It is the highest-value, most-requested capability.

**Independent Test**: With a platform-admin token against a running platform, subscribe to a register, query its sync state and local relationship, submit a transaction and retrieve its inclusion proof and verification bundle, then revoke it — each via a tool, each returning a real result.

**Acceptance Scenarios**:

1. **Given** a platform-admin token, **When** the agent subscribes the node to a register, **Then** the subscription is created and confirmed.
2. **Given** a register the node participates in, **When** the agent queries sync state and local relationship, **Then** it receives the current typed state and the node's derived role.
3. **Given** a sealed transaction, **When** the agent requests its inclusion proof and verification bundle, **Then** both are returned and independently verifiable.
4. **Given** a transaction, **When** the agent submits a revocation with a reason, **Then** the revocation is recorded and the transaction's lifecycle status reflects it.
5. **Given** a consumer token, **When** it attempts any register-control tool, **Then** the platform refuses it.

---

### User Story 2 - Credential & presentation lifecycle (Wave 2) (Priority: P2)

An issuer or verifier operator uses an AI agent to run credential flows: create a credential offer, create a presentation request, poll a presentation's lifecycle to its outcome, and manage an issued credential's lifecycle (revoke, suspend, reinstate, refresh).

**Why this priority**: Credentials and presentations are a core platform pillar entirely absent from the tool surface. High value, but the federation gap (Wave 1) is the more acute operational pain.

**Independent Test**: With an appropriately-scoped platform token, create a credential offer and confirm its lifecycle status; create a presentation request and poll it to a terminal outcome; revoke then reinstate an issued credential and confirm each status transition.

**Acceptance Scenarios**:

1. **Given** an issuer-scoped token, **When** the agent creates a credential offer, **Then** an offer is returned and its status is queryable.
2. **Given** a verifier-scoped token, **When** the agent creates a presentation request and polls it, **Then** it observes the lifecycle progress to a terminal outcome (success / decline / abandoned).
3. **Given** an issued credential, **When** the agent revokes, suspends, reinstates, or refreshes it, **Then** the credential's status reflects each transition.

---

### User Story 3 - Citizen self-service (Wave 3) (Priority: P2)

A citizen (consumer-tier token), via their AI assistant, manages their own Sorcha presence: list and inspect their credentials, manage enrolled devices, view and update their persona, see pending applications, manage invitations, and present a credential — all scoped to themselves.

**Why this priority**: This is the consumer-facing half of "both audiences, tiered by token." It builds directly on the Foundation's new consumer slice. Equal value to Wave 2; sequenced after it only because issuance (Wave 2) is what populates a citizen's wallet.

**Independent Test**: With a consumer token, list the citizen's credentials and devices, read and update the persona, list pending applications, and confirm every operation is scoped to the calling citizen and cannot reach another citizen's data.

**Acceptance Scenarios**:

1. **Given** a consumer token, **When** the citizen lists credentials and devices, **Then** they see only their own.
2. **Given** a consumer token, **When** the citizen reads and updates their persona, **Then** the change persists and is reflected on next read.
3. **Given** a consumer token, **When** the citizen views pending applications, **Then** they see the notices addressed to them.
4. **Given** a consumer token, **When** any tool is invoked, **Then** it operates only on the calling citizen's data (no cross-citizen access).

---

### User Story 4 - Platform-administration depth (Wave 4) (Priority: P3)

A system administrator uses an AI agent for deeper platform operations: suspend or reactivate an organisation, read and change platform settings (e.g. the public-org toggle), audit an organisation's users, control validators (start / stop / restart), and provision or reset a platform user.

**Why this priority**: Rounds out operator capability. Valuable for automation but lower urgency than the federation, credential, and citizen gaps; several of these are infrequent, high-trust operations.

**Independent Test**: With a system-admin token, suspend and reactivate a test organisation, toggle a platform setting and read it back, audit an org's users, and start/stop a validator — each via a tool, each enforced by the platform.

**Acceptance Scenarios**:

1. **Given** a system-admin token, **When** the agent suspends and then reactivates an organisation, **Then** the organisation's status reflects each change.
2. **Given** a system-admin token, **When** the agent changes and reads a platform setting, **Then** the new value is returned.
3. **Given** a system-admin token, **When** the agent audits an organisation's users, **Then** it receives the read-only user list.
4. **Given** a system-admin token, **When** the agent starts, stops, or restarts a validator, **Then** the validator's state reflects the command.
5. **Given** a non-admin platform token, **When** it attempts any of these tools, **Then** the platform refuses it.

---

### Edge Cases

- **Tool invoked by the wrong tier/role**: hidden from the caller's tool list and refused by the platform if invoked directly (inherited from Foundation).
- **High-blast-radius operation** (e.g. validator stop, org suspend): the platform's existing authorization and confirmation rules apply; the tool surfaces the platform's response, including refusals, verbatim.
- **Citizen tool reaching for another citizen's data**: must be impossible — the platform scopes by the caller's identity; a not-found is indistinguishable from a non-existent resource.
- **A wave's underlying platform operation is itself service-to-service only**: the tool is admin-tier-gated at the surface and the cross-service call is handled by the established forwarding mechanism, never by widening a backend endpoint.
- **Presentation that never reaches an outcome**: the poll tool reports the awaiting/abandoned/expired state rather than hanging.

## Requirements *(mandatory)*

### Functional Requirements

**Cross-cutting (apply to every new tool in this feature):**

- **FR-001**: Every new tool MUST execute with the caller's credentials and be authorized by the platform (inheriting the Foundation's pass-through and advisory-narrowing contract); no new tool introduces its own auth path.
- **FR-002**: Every new tool MUST be tier-scoped per the Foundation's mapping: register-control and platform-admin tools require platform-admin; credential/presentation tools require the appropriate platform role; citizen self-service tools require consumer tier; cross-tier tools are tagged explicitly.
- **FR-003**: Every new tool MUST route through a typed service client method (adding the method to the client when absent); no tool may hand-roll a URL.
- **FR-004**: Every new tool MUST be covered by the live integration safety net across the tiers that may and may not invoke it, asserting real success or expected refusal.
- **FR-005**: Every new tool MUST be reflected in the advertised catalogue and manifest (subject to the Foundation's integrity gate) and carry a description meeting the established quality bar.

**Wave 1 — Register control & federation:**

- **FR-006**: Provide tools to subscribe and unsubscribe a node to/from a register.
- **FR-007**: Provide tools to read a register's sync state and the node's local relationship to it.
- **FR-008**: Provide tools to submit a transaction and to retrieve its receipt, inclusion proof, and verification bundle.
- **FR-009**: Provide a tool to revoke a transaction with a reason and reflect its lifecycle status.

**Wave 2 — Credential & presentation lifecycle:**

- **FR-010**: Provide a tool to create a credential offer and query its status.
- **FR-011**: Provide a tool to create a presentation request and poll its lifecycle to a terminal outcome.
- **FR-012**: Provide tools to revoke, suspend, reinstate, and refresh an issued credential.

**Wave 3 — Citizen self-service:**

- **FR-013**: Provide consumer-tier tools for a citizen to list and inspect their own credentials.
- **FR-014**: Provide consumer-tier tools to list, rename, and revoke the citizen's own enrolled devices.
- **FR-015**: Provide consumer-tier tools to read and update the citizen's own persona.
- **FR-016**: Provide consumer-tier tools to view pending applications and to manage the citizen's invitations.
- **FR-017**: Provide a consumer-tier tool to present a credential.
- **FR-018**: All citizen self-service tools MUST be scoped to the calling citizen; cross-citizen access MUST be impossible.

**Wave 4 — Platform-administration depth:**

- **FR-019**: Provide system-admin tools to suspend and reactivate an organisation.
- **FR-020**: Provide system-admin tools to read and update platform settings.
- **FR-021**: Provide a system-admin tool to audit an organisation's users (read-only).
- **FR-022**: Provide system-admin tools to start, stop, and restart a validator.
- **FR-023**: Provide system-admin tools to provision a platform user and reset a user's password.

**Scope guards:**

- **FR-024**: This feature MUST NOT introduce a raw-signing tool (deferred to its own security-reviewed effort) and MUST NOT introduce node-lifecycle tools (bootstrap / validator-key import / reset — operator-only, out of MCP).
- **FR-025**: Each wave MUST be independently shippable on top of the Foundation, in priority order, without requiring later waves.

### Key Entities *(include if feature involves data)*

- **Tool wave**: a cohesive, independently-shippable group of new tools (register-control, credential/presentation, citizen self-service, platform-admin).
- **Register-control operation**: subscription, sync-state/relationship query, transaction submit/verify, revocation.
- **Credential lifecycle operation**: offer, presentation request/poll, revoke/suspend/reinstate/refresh.
- **Citizen self-service operation**: credentials, devices, persona, pending applications, invitations, presentation — always scoped to the caller.
- **Platform-admin operation**: org status, platform settings, org user audit, validator control, user provisioning.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can complete a full register-federation cycle (subscribe → check sync state → submit → verify → revoke) entirely through MCP tools, with no manual service calls — the workflow that previously required hand-driving.
- **SC-002**: An operator can run a credential lifecycle (offer → present/poll → revoke → reinstate) end-to-end through MCP tools.
- **SC-003**: A citizen, using only a consumer-tier token, can list their credentials and devices, update their persona, and view pending applications — and cannot reach any other citizen's data.
- **SC-004**: A system administrator can suspend/reactivate an organisation, change and read a platform setting, and control a validator through MCP tools.
- **SC-005**: 100% of the new tools resolve to a live platform operation and are covered by the integration safety net across permitted and refused tiers.
- **SC-006**: Each wave is delivered and demonstrable independently, in priority order, on top of the Foundation.
- **SC-007**: The advertised catalogue and manifest reflect every new tool, and the integrity gate stays green.

## Assumptions

- Feature 139 (MCP Server Foundation) is complete and merged; this feature builds entirely on its auth, transport, tier-mapping, service-client, and safety-net mechanisms.
- The platform already exposes the underlying operations (the gap is MCP coverage, not platform capability) — confirmed by the platform API surface review in the milestone design doc.
- The exact tool list per wave is the planned set above; the final per-tool breakdown is confirmed at plan time for each wave, but the wave boundaries and priorities are fixed.
- High-trust operations (validator control, org suspension, user provisioning) rely on the platform's existing authorization rules; the MCP tools surface those rules' decisions, they do not relax them.
- Raw signing, full delegated-authorization (OAuth), and node-lifecycle tooling remain out of scope and tracked separately.
