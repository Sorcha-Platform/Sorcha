# Feature Specification: MCP Server Foundation

**Feature Branch**: `139-mcp-foundation`
**Created**: 2026-05-26
**Status**: Draft
**Input**: Phase 0 of the MCP remediation milestone. Full design and rationale: `docs/superpowers/specs/2026-05-26-mcp-server-remediation-design.md`.

## Overview

The Sorcha MCP server exposes 36 tools to AI agents, but a review found it does not work against a secured backend: tool calls reach backend services **without the caller's credentials** (so they are anonymous and rejected), several tools target **endpoints that no longer exist**, and **consumer-tier (citizen) tokens receive no tools at all**. It also runs only as a local subprocess, so remote agents cannot connect.

This feature makes the **existing** tool surface genuinely usable: every tool executes with the caller's real privileges (enforced by the platform, tiered by the caller's token), every advertised tool reaches a working operation, citizens get a usable surface, and both local operators and remote agents can connect. No new capability tools are added here — that is the next feature.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Tools execute with the caller's real privileges (Priority: P1)

An AI agent or operator presents their Sorcha access token and invokes a tool. The tool performs the operation **as that identity**, and the platform — not the client — decides whether it is allowed. An admin token can run an admin operation; a citizen token attempting an admin operation is refused by the platform.

**Why this priority**: This is the core defect. Until the caller's identity reaches the backend, every protected tool fails (or, worse, the local-only check creates a false impression of enforcement). Nothing else in the feature has value without this.

**Independent Test**: Mint a platform-admin token, invoke an admin tool against a running backend, observe a real result. Mint a consumer token, invoke the same admin tool, observe a refusal that originates from the platform (not the local gate).

**Acceptance Scenarios**:

1. **Given** a valid platform-admin token, **When** the caller invokes an admin tool, **Then** the operation is performed with that identity and returns a real backend result.
2. **Given** a valid consumer token, **When** the caller invokes an admin tool, **Then** the call is refused with a clear "forbidden" result and no backend mutation occurs.
3. **Given** an expired token, **When** the caller invokes any tool, **Then** the result is a clear "unauthorized" status rather than a crash or a misleading success.

---

### User Story 2 - Citizens get a usable, correctly-scoped tool surface (Priority: P1)

A citizen (consumer-tier token) connects and sees the tools appropriate to them — their own wallet/credential self-service plus the workflow-participation tools needed to submit and track applications — and can use them. They do not see operator or designer tools.

**Why this priority**: Consumer tokens currently receive zero tools, which silently excludes the entire citizen audience the platform is built around. Fixing the privilege path (US1) is meaningless for citizens unless the tier mapping also admits them.

**Independent Test**: With a consumer token, list available tools and confirm the citizen and participation slices appear and the admin/designer slices do not; invoke a citizen self-service tool and a participation tool and confirm both succeed.

**Acceptance Scenarios**:

1. **Given** a consumer-tier token, **When** the caller lists tools, **Then** they see citizen self-service and workflow-participation tools and do **not** see operator or designer tools.
2. **Given** a platform-tier token with the designer role, **When** the caller lists tools, **Then** they see designer and participation tools but not operator tools.
3. **Given** a platform-tier token with neither admin nor designer role, **When** the caller lists tools, **Then** they see participation tools only.
4. **Given** a service-tier token, **When** the caller connects, **Then** the connection is refused with a clear message that service tokens are not valid MCP callers.

---

### User Story 3 - Remote agents and local operators can both connect (Priority: P2)

A remote AI agent connects to the MCP server over the network, authenticating with its own Sorcha token per request. A local operator continues to run the server as a subprocess with a token supplied at startup. Both reach the same tool surface, scoped to their token.

**Why this priority**: The "external agent" audience requires a network transport; the existing operator workflow must keep working. This is gated behind US1/US2 because a transport with no working tools has no value.

**Independent Test**: Start the server in network mode; send an authenticated request and confirm a tool dispatches; send an unauthenticated request and confirm it is rejected before any tool runs. Separately, start the server in local mode with a startup token and confirm a tool dispatches.

**Acceptance Scenarios**:

1. **Given** the server running in network mode, **When** a request arrives with a valid token, **Then** the tool dispatches scoped to that token's tier.
2. **Given** the server running in network mode, **When** a request arrives with no token or an invalid token, **Then** it is rejected before any tool executes.
3. **Given** the server running in local mode with a startup token, **When** a tool is invoked, **Then** it behaves identically to the network path for the same identity.

---

### User Story 4 - Every advertised tool reaches a working operation (Priority: P2)

A caller can trust that any tool the server advertises actually performs its stated operation against the current platform — no tool silently fails because it targets a route that no longer exists.

**Why this priority**: Drifted tools erode agent trust and waste calls. This is verified by the safety net (US5) but is a user-facing guarantee in its own right.

**Independent Test**: For each advertised tool, invoke it with an appropriately-scoped token against a running backend and confirm it returns either a real result or an expected, meaningful error — never a "route not found" caused by drift.

**Acceptance Scenarios**:

1. **Given** any advertised tool, **When** invoked with a correctly-scoped token, **Then** it resolves to a live platform operation (no drift-induced not-found).
2. **Given** the previously-broken submit-action tool, **When** a participant submits action data, **Then** the workflow advances and a transaction identifier is returned.

---

### User Story 5 - Breakage is visible, and the advertised catalogue stays honest (Priority: P3)

Operators can see which tools were invoked, by which tier, and with what outcome; and the publicly-advertised tool catalogue cannot drift away from what the server actually offers.

**Why this priority**: Observability and catalogue integrity prevent silent regression — the exact failure mode that produced this whole remediation. Valuable, but only after the surface works.

**Independent Test**: Invoke a mix of tools across tiers; confirm each invocation is recorded with caller tier, tool, and outcome, and counters reflect it. Separately, change the tool set without updating the advertised catalogue and confirm the integrity check fails.

**Acceptance Scenarios**:

1. **Given** a series of tool invocations, **When** an operator inspects telemetry, **Then** each invocation is attributed by tool, caller tier, and outcome.
2. **Given** a change to the set of tools, **When** the advertised catalogue is not updated to match, **Then** an automated integrity check fails and blocks release.

---

### Edge Cases

- **Expired token mid-session** (local mode): tools return a clear "unauthorized" status; the session does not crash.
- **Malformed / wrong-installation / no-tier-audience token**: rejected at the entry point with a clear message; no tool runs.
- **Service-tier token**: rejected as a non-caller (service tokens are for the internal mesh).
- **Backend rate-limit (429)**: surfaced to the caller as a clear "rate limited" status, not a raw error.
- **Backend unavailable**: surfaced as "unavailable" with guidance to retry; not a crash.
- **Local advisory gate disagrees with backend**: the advisory gate may only ever *narrow* visibility — it can never permit a call the backend would refuse. The backend is authoritative.
- **Tool the caller is not entitled to**: hidden from the caller's tool list and refused if invoked directly.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every tool invocation MUST be performed against backend services carrying the **caller's own credentials**, so that the platform authorizes the operation as the calling identity.
- **FR-002**: The platform (backend) MUST be the authoritative authorization decision for every tool; any check the MCP server performs locally is advisory and MUST only ever narrow access, never grant it.
- **FR-003**: The set of tools a caller can see and invoke MUST be derived from their token's **tier** (and, for platform-tier callers, their role): consumer-tier callers receive citizen self-service and workflow-participation tools; platform-tier admins receive operator/admin tools; platform-tier designers receive designer tools; workflow-participation tools are available to both consumer and platform tiers.
- **FR-004**: Consumer-tier tokens MUST receive a non-empty, usable tool surface (the prior behaviour of receiving zero tools is a defect to be removed).
- **FR-005**: Service-tier tokens MUST be rejected as MCP callers with a clear, actionable message.
- **FR-006**: Every advertised tool MUST resolve to a current, working platform operation; no advertised tool may target a non-existent route.
- **FR-007**: The submit-action tool MUST advance a workflow via the platform's current action-execution operation and return the resulting transaction identifier.
- **FR-008**: The server MUST support two connection modes — a local subprocess mode with a token supplied at startup, and a network mode where each request authenticates with its own token.
- **FR-009**: In network mode, requests without a valid token MUST be rejected before any tool executes.
- **FR-010**: Tool failures originating from the backend MUST be mapped to clear, distinct caller-facing statuses (at minimum: unauthorized, forbidden, not-found, rate-limited, unavailable, error).
- **FR-011**: Each tool invocation MUST be recorded with the caller's tier, the tool name, and the outcome, and MUST be reflected in telemetry counters suitable for alerting on broken or unauthorized patterns.
- **FR-012**: The publicly-advertised tool catalogue MUST match the set of tools the server actually offers, enforced by an automated check that fails the build on mismatch.
- **FR-013**: The raw-signing tool MUST be removed from the advertised surface for this feature (it is deferred to a dedicated, security-reviewed effort); the read-only wallet-info tool remains.
- **FR-014**: No new capability tools are introduced in this feature; the work is limited to making the existing surface correct, tiered, and reachable over both transports.
- **FR-015**: The design MUST leave clean seams for a future full delegated-authorization (OAuth) model without requiring rework of the tool layer.
- **FR-016**: Tokens MUST be validated against the installation's own issuer and tier audiences; tokens from another installation MUST be rejected.
- **FR-017**: Credentials MUST never be written to logs or telemetry.

### Key Entities *(include if feature involves data)*

- **Caller context**: the identity behind the current invocation — tier (consumer / platform / service), roles (platform only), home organisation, and the raw token used for forwarding. Resolved per-process in local mode and per-request in network mode.
- **Tier**: the trust boundary carried by the token's audience — consumer (citizen/wallet holder), platform (admin / operator / designer), service (internal mesh, not a valid caller).
- **Tool entitlement**: the mapping from a tool to the tier(s) and, where applicable, the platform role that may invoke it.
- **Tool result status**: the caller-facing outcome of an invocation (success, unauthorized, forbidden, not-found, rate-limited, unavailable, error).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of advertised tools resolve to a live platform operation when invoked with an appropriately-scoped token (zero drift-induced not-found results) — verified across all tiers by the integration safety net.
- **SC-002**: Authorization is demonstrably enforced by the platform: a consumer token is refused at least one admin operation **by the backend**, and an admin token succeeds at the same operation, in an automated test.
- **SC-003**: A consumer-tier token can invoke at least one citizen self-service tool and at least one workflow-participation tool — up from zero today.
- **SC-004**: The server is reachable and functional over both transports: an authenticated tool invocation succeeds and an unauthenticated request is rejected, in each of local and network modes.
- **SC-005**: An automated integrity check fails the build whenever the advertised catalogue diverges from the actual tool set.
- **SC-006**: Every tool invocation in the safety-net run is attributed in telemetry by tool, caller tier, and outcome.
- **SC-007**: The integration safety net exercises every advertised tool across the consumer, platform-admin, and platform-designer tiers and passes against a running platform.

## Assumptions

- Sorcha already issues tier-scoped, installation-namespaced access tokens (F136); this feature consumes them and does not change token issuance.
- A running platform (the standard local Docker stack) is available for the integration safety net, consistent with how end-to-end verification is already done in this repo.
- The full delegated-authorization (OAuth) model and the raw-signing tool are explicitly **out of scope** and tracked as separate efforts; this feature only ensures it can adopt them later without rework.
- "Tiered by token" treats the token as the source of truth for authorization; the MCP server never elevates a caller beyond what their token permits.
