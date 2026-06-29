# Feature Specification: Fix "Verification Not Configured" False Error

**Feature Branch**: `174-fix-haip-verify-auth`

**Created**: 2026-06-29

**Status**: Draft

**Input**: User description: "Fix verify not-configured error: the HAIP verify transport calls /api/v1/verifier/requests (RequireAuthorization) unauthenticated so it 401s and is shown as Verification is not yet configured here. Wire each host auth handler into the IHaipVerifierClient typed HttpClient registered in AddSorchaUserComponents (web AuthenticatedHttpMessageHandler, PWA BearerTokenHandler+ServerClockHandler, Verifier service token), override the NotConfigured stub transport with HaipVerificationTransport in Sorcha.UI.Web.Client Program.cs, and make HaipVerificationTransport surface transport errors via the components error/retry path instead of returning an empty session that renders as not-configured"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Verification works on a configured host (Priority: P1)

A person opening a verification surface in a host where verification **is** wired up (the web client and the wallet PWA) is able to start a verification request and see the live presentation/QR flow, instead of being told "Verification is not yet configured here."

**Why this priority**: This is the core defect. Today every configured host falsely reports verification as unavailable because the underlying request to the verifier is rejected for lack of credentials and the rejection is misread as "not configured." Until this is fixed, the verification feature is unusable everywhere, regardless of correct deployment.

**Independent Test**: Open the verification surface on the web client (and separately on the PWA) against a running verifier backend, trigger a verification request, and confirm the presentation/QR session renders rather than the "not configured" message.

**Acceptance Scenarios**:

1. **Given** a host with verification wired up and a reachable verifier backend, **When** a user opens the verification surface and starts a request, **Then** the request is accepted by the backend and the live verification session (presentation/QR) is displayed.
2. **Given** the same configured host, **When** the verification surface loads, **Then** the "Verification is not yet configured here" message is NOT shown.
3. **Given** a host where verification is genuinely not wired up, **When** a user opens the verification surface, **Then** the "not configured" message is still shown (the legitimate not-configured state is preserved).

---

### User Story 2 - Transport failures are visible and recoverable (Priority: P2)

When a verification request cannot reach or is rejected by the verifier backend (network failure, authentication failure, server error), the user sees a clear error state with a retry option — not a silent "empty" session that masquerades as "not configured."

**Why this priority**: The "not configured" message is currently a catch-all that hides real, transient failures. Distinguishing "this host has no verification" from "verification is configured but the last attempt failed" lets users retry and lets operators diagnose problems, but it only matters once the P1 happy path works.

**Independent Test**: Force the verifier backend to fail (stop it, or make it return an error/401) for a configured host, open the verification surface, and confirm an error state with a retry control appears; restore the backend, retry, and confirm the session then loads.

**Acceptance Scenarios**:

1. **Given** a configured host whose verifier backend returns an authentication or server error, **When** a user starts a verification request, **Then** an error state is shown with a retry affordance, distinct from the "not configured" message.
2. **Given** the error state is displayed, **When** the user selects retry and the backend is now reachable, **Then** the verification session loads successfully.
3. **Given** a transport failure, **When** the failure is surfaced, **Then** the user is not shown an empty/blank session that implies the feature is absent.

---

### User Story 3 - Each host authenticates with its own credentials (Priority: P3)

The verification request carries the credentials appropriate to the host it runs in — the signed-in user's session on the web client, the wallet holder's bearer token (with server-clock handling) on the PWA, and a service identity for backend-to-backend calls — so the verifier backend accepts the request in every host.

**Why this priority**: This is the mechanism that makes Story 1 true across all three host types. It is called out separately because each host has a distinct credential model and all three must be satisfied; missing any one re-introduces the false "not configured" error for that host.

**Independent Test**: For each host (web client, PWA, service-to-service path), confirm the outbound verification request includes the host's expected authentication and is accepted by the backend.

**Acceptance Scenarios**:

1. **Given** an authenticated user on the web client, **When** a verification request is sent, **Then** it carries the user's session credentials and is accepted.
2. **Given** an authenticated wallet holder in the PWA, **When** a verification request is sent, **Then** it carries the holder's bearer token and correct time/clock handling and is accepted.
3. **Given** a backend service initiating verification, **When** the request is sent, **Then** it carries a service identity accepted by the verifier endpoint.

---

### Edge Cases

- **Genuinely unconfigured host**: A host that does not wire up verification must continue to show the legitimate "not configured" state — the fix must not make every host claim to be configured.
- **Expired or refreshable token (PWA)**: When the holder's token is expired but refreshable, the request should refresh and retry rather than surface a hard error.
- **Clock skew (PWA)**: Server-clock handling must remain in effect so time-sensitive verification is not rejected for skew.
- **Backend reachable but rejects credentials (401/403)**: Treated as a transport/auth error (Story 2), not as "not configured."
- **Backend unreachable (network/DNS)**: Treated as a retryable transport error, not as "not configured."
- **Retry storms**: Repeated retries after persistent failure should not spam the backend or lock the UI.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A verification request originating from a host where verification is wired up MUST be sent with credentials that the verifier backend accepts, so the request succeeds instead of being rejected for lack of authentication.
- **FR-002**: The web client host MUST attach the signed-in user's authentication to outbound verification requests.
- **FR-003**: The wallet PWA host MUST attach the holder's bearer token and apply server-clock handling to outbound verification requests, including refresh-and-retry when the token is expired but refreshable.
- **FR-004**: Backend-to-backend verification requests MUST carry a service identity accepted by the verifier endpoint.
- **FR-005**: A host that has verification wired up MUST use the real verification transport rather than the placeholder/stub that always reports "not configured."
- **FR-006**: When a verification request fails to reach or is rejected by the backend, the system MUST present an error state with a retry option, distinct from the "not configured" state.
- **FR-007**: The system MUST NOT render a transport failure as an empty verification session that appears identical to the "not configured" state.
- **FR-008**: A host that genuinely does not wire up verification MUST continue to display the "not configured" state.
- **FR-009**: Retrying after a failure MUST re-attempt the verification request and, on success, proceed to the live verification session.
- **FR-010**: The credential-attachment and transport-selection behavior MUST be applied consistently wherever the user-facing verification components are registered, so no host is left on the unauthenticated/stub path.

### Key Entities *(include if feature involves data)*

- **Verification Request**: A user- or service-initiated request to begin a credential presentation/verification. Carries the originating host's credentials; its acceptance or rejection determines whether a live session or an error is shown.
- **Verification Session**: The live state of an in-progress verification (e.g., pending presentation, submitted, verified/denied/expired). A populated session drives the presentation/QR UI; an empty session must NOT be conflated with "not configured."
- **Verification Transport**: The component that issues verification requests on behalf of the UI. Has a real implementation (issues authenticated requests, surfaces errors) and a stub implementation (always reports "not configured"); the correct one must be selected per host.
- **Host Credential Context**: The per-host authentication mechanism — web user session, PWA holder bearer token with clock handling, or backend service identity — that must be applied to verification requests.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a correctly deployed/configured host with a reachable verifier backend, 100% of verification-surface loads proceed to a live verification session and 0% show the "not configured" message.
- **SC-002**: Verification works on all three host paths (web client, wallet PWA, and service-to-service) without any path falling back to the false "not configured" error.
- **SC-003**: When the verifier backend is failing, users see an error-with-retry state in 100% of attempts (0% blank/empty sessions and 0% false "not configured" messages).
- **SC-004**: After a transient backend failure is resolved, a user-initiated retry succeeds and reaches the live verification session without reloading the host.
- **SC-005**: A host that does not wire up verification still shows the "not configured" state in 100% of loads (no regression of the legitimate case).

## Assumptions

- The "Verification is not yet configured here" message is presently produced because the verification transport calls the verifier requests endpoint without credentials, the endpoint rejects the call (401), and the empty result is rendered as "not configured."
- The verifier requests endpoint requires authentication; the fix is to authenticate the caller, not to relax the endpoint's authorization.
- The three host credential mechanisms already exist and are used by other outbound calls in their respective hosts; this feature reuses them for the verification transport rather than inventing new authentication.
- The user-facing verification components already expose an error/retry presentation path; the fix routes transport failures into that existing path instead of into the empty-session/"not configured" path.
- A stub transport that reports "not configured" exists as the default and remains the correct behavior for hosts that have not wired up verification; only hosts that wire it up override it with the real transport.
- Scope is limited to fixing authentication and error-surfacing for the verification transport across the web client, PWA, and service-to-service paths; it does not change the verifier backend's behavior, the verification protocol, or the underlying credential models.
