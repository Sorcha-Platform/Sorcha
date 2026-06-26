# Feature Specification: Fix PWA Consumer-Token Claims

**Feature Branch**: `165-fix-pwa-consumer-claims`

**Created**: 2026-06-26

**Status**: Draft

**Input**: User description: "Fix PWA consumer-token claims (backlog 3/4/5, relaunch): consumer-tier JWT lacks platform_user_id + wallet binding, so PWA security/devices/add-phone all fail to load. Mint per F136; verify on n1."

## Overview

A citizen who signs in to the Citizen Wallet PWA receives a **consumer-tier** access token. Three citizen-facing surfaces — **Security**, **Devices**, and **Add a phone** — fail to load because the token a real citizen presents does not reliably carry the stable identity the wallet backend needs to recognise the citizen and locate their wallet. The platform identity is the key that lets the backend resolve the citizen's wallet binding server-side; without it, every identity-scoped citizen surface returns "unauthorized" / empty and the pages render an error or blank state.

This feature makes the consumer-tier token **always** carry the citizen's stable platform identity, on **every** path that issues or renews a consumer token, and verifies the three PWA surfaces load end-to-end against the live `n1` network. The shape and tier boundary defined by Feature 136 (tiered JWT audiences) is the contract to mint against: a consumer token identifies the citizen and is inert against platform surfaces; wallet binding remains resolved by the backend from that identity, not embedded as a privilege marker in the token.

This is relaunch backlog items 3, 4, and 5.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Citizen views their Security settings on the PWA (Priority: P1)

A signed-in citizen opens the **Security** page in the Citizen Wallet PWA. The page recognises who they are and shows their account-security state instead of failing to load.

**Why this priority**: Security is the landing surface for account self-management; if the token cannot identify the citizen, this is the first place the failure is visible. It is the smallest end-to-end proof that consumer identity flows through correctly.

**Independent Test**: Sign in as a citizen on the PWA against `n1`, navigate to Security, and confirm the page loads the citizen's own security state (no unauthorized error, no blank/error fallback).

**Acceptance Scenarios**:

1. **Given** a citizen signed in to the PWA with a freshly issued consumer token, **When** they open the Security page, **Then** the page loads their security state successfully.
2. **Given** a citizen whose access token has been renewed during the session, **When** they open the Security page, **Then** it still loads successfully (the renewed token carries the same identity).

---

### User Story 2 - Citizen manages their devices on the PWA (Priority: P1)

A signed-in citizen opens the **Devices** page. The page lists the devices bound to their wallet and lets them label or revoke a device.

**Why this priority**: Device management is a core wallet-safety capability and depends on the backend resolving the citizen's wallet from their identity. It exercises the wallet-binding resolution path that the identity claim unblocks.

**Independent Test**: Sign in as a citizen on the PWA against `n1`, open Devices, and confirm the citizen's own device list loads (empty list is acceptable for a citizen with no extra devices — the success condition is a successful, citizen-scoped response, not an error).

**Acceptance Scenarios**:

1. **Given** a citizen signed in to the PWA, **When** they open the Devices page, **Then** the page returns their device list scoped to their own wallet without an unauthorized error.
2. **Given** a citizen with at least one registered device, **When** they revoke or relabel that device, **Then** the action succeeds and is reflected on reload.

---

### User Story 3 - Citizen adds a phone (pairs a new device) on the PWA (Priority: P2)

A signed-in citizen starts the **Add a phone** flow to pair an additional device to their wallet. The flow recognises the citizen and registers the new device against their wallet.

**Why this priority**: Add-a-phone is the highest-value but least-frequent of the three; it depends on the same identity and wallet resolution as Devices, so it is validated last once Stories 1 and 2 confirm the identity plumbing.

**Independent Test**: Sign in as a citizen on the PWA against `n1`, start Add a phone, and confirm the flow begins (issues a pairing artefact bound to the citizen) rather than failing to load.

**Acceptance Scenarios**:

1. **Given** a citizen signed in to the PWA, **When** they start the Add-a-phone flow, **Then** the flow loads and produces a pairing artefact bound to the citizen's wallet.
2. **Given** the citizen completes pairing on the second device, **When** they return to Devices, **Then** the newly added device appears in their device list.

---

### Edge Cases

- **Renewed tokens**: When a citizen's access token is renewed mid-session, the renewed consumer token MUST carry the same stable identity as the original — the three surfaces must not break after a renewal.
- **Alternate sign-in paths**: Citizens may arrive via different sign-in routes (password, social sign-in, post-2FA, organisation selection). Every route that issues a consumer token must carry the identity claim — a fix on one path that misses another leaves the surfaces intermittently broken.
- **Legacy / pre-fix tokens**: Tokens issued before this fix may lack the identity claim. The backend already has a fallback to resolve a citizen's wallet from an alternate stable identifier; the citizen surfaces must continue to work for such tokens (degrade, not error) until those tokens expire.
- **Citizen with no wallet yet**: A citizen who has not yet provisioned a wallet should see an appropriate empty/guidance state on Devices/Add-phone, not an unauthorized error.
- **Tier boundary preserved**: Adding the identity claim must not turn a consumer token into something accepted at platform/admin surfaces — the audience tier boundary defined by Feature 136 must remain intact, and the consumer token must continue to omit administrative role and platform-privilege markers.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every consumer-tier access token issued by the platform MUST carry the citizen's stable cross-organisation platform identity (`platform_user_id`), matching the consumer-token shape defined by Feature 136.
- **FR-002**: Every path that issues a consumer-tier token (initial sign-in, post-2FA, social sign-in callback, organisation selection, and any other interactive issuance) MUST include the identity claim — coverage MUST be path-complete, not limited to a single flow.
- **FR-003**: Token renewal MUST re-emit a consumer token carrying the same stable identity as the originally issued token, so identity-scoped surfaces keep working across renewals.
- **FR-004**: The Citizen Wallet PWA Security, Devices, and Add-a-phone surfaces MUST successfully load for a signed-in citizen, resolving the citizen and (where applicable) their wallet binding from the token's identity.
- **FR-005**: Wallet binding MUST be resolved by the backend from the citizen's identity per Feature 136 (wallet binding is a platform-privilege marker and is NOT embedded in a consumer token); this feature fixes the identity that enables that resolution rather than injecting wallet binding into the consumer token.
- **FR-006**: The fix MUST preserve the Feature 136 tier boundary — a consumer token MUST remain inert against platform/admin surfaces and MUST continue to omit administrative roles and platform-privilege markers.
- **FR-007**: Existing citizen tokens that predate the fix (lacking the identity claim) MUST continue to function via the backend's existing alternate-identifier fallback until they expire — the change MUST NOT regress already-issued sessions.
- **FR-008**: The corrected behaviour MUST be verified end-to-end on the live `n1` network: a real citizen sign-in followed by successful loads of all three PWA surfaces.

### Key Entities *(include if data involved)*

- **Consumer-tier access token**: The credential a citizen presents from the PWA. Identifies the citizen and is scoped so it cannot act on platform/admin surfaces. Must carry the stable platform identity; does not carry administrative roles or an embedded wallet-binding privilege marker.
- **Platform identity (`platform_user_id`)**: The citizen's stable, cross-organisation identifier. The key the wallet backend uses to recognise the citizen and locate their wallet.
- **Wallet binding**: The association between a citizen and their wallet (and that wallet's devices). Resolved by the backend from the citizen's platform identity, not from a claim embedded in the consumer token.
- **Citizen Wallet PWA surfaces**: Security, Devices, and Add-a-phone — the three citizen-facing pages whose failure to load is the observed symptom and whose successful load is the success condition.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A citizen signing in to the PWA on `n1` can open the Security page and see their security state on the first attempt, 100% of the time (no unauthorized/blank failures).
- **SC-002**: A citizen signing in to the PWA on `n1` can open the Devices page and receive their own device list (including a valid empty list) on the first attempt, 100% of the time.
- **SC-003**: A citizen can start the Add-a-phone flow on `n1` and obtain a pairing artefact bound to their wallet on the first attempt, 100% of the time.
- **SC-004**: All three surfaces continue to load successfully after an in-session token renewal — zero regressions attributable to renewed tokens.
- **SC-005**: A consumer token continues to be rejected at platform/admin surfaces (the tier boundary is unchanged) — verified for at least one representative platform-only operation.
- **SC-006**: The three relaunch backlog items (3, 4, 5) are closed, each evidenced by a successful citizen run on `n1`.

## Assumptions

- **Feature 136 is the contract to mint against.** "Mint per F136" is read as: produce the Feature 136 consumer-token shape — identity present, tier boundary enforced via audience, wallet binding resolved server-side and NOT embedded in the token. Embedding a wallet-binding privilege marker into a consumer token is explicitly out of scope because it would contradict the Feature 136 tier model.
- **The observed "wallet binding missing" symptom is downstream of the missing identity claim.** The wallet backend resolves a citizen's wallet from their identity; when the identity claim is absent the resolution cannot run, which presents to the citizen as the surfaces failing to load. Restoring the identity claim restores wallet resolution.
- **The root cause is path/coverage and/or deployment, not page logic.** The PWA pages and backend resolution logic exist; the gap is that one or more token-issuance paths (or the build deployed to `n1`) do not emit the identity claim. The fix targets issuance/renewal coverage plus an `n1` redeploy-and-verify, not a rewrite of the pages.
- **`n1` is the canonical verification network** for citizen flows in this relaunch; verification means a real interactive citizen sign-in, not a synthetic/minted-token test alone.
- **No new claim names or token formats are introduced** — the work conforms the issued tokens to the already-defined Feature 136 consumer shape rather than adding new fields.
- **The three surfaces share one root cause.** Security, Devices, and Add-a-phone all depend on citizen identity (and, for Devices/Add-phone, wallet resolution); fixing the identity claim is expected to resolve all three together.
