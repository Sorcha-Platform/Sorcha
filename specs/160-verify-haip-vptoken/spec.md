# Feature Specification: HAIP returns raw vp_token on verifier result poll (PR B1)

**Feature Branch**: `160-verify-haip-vptoken`
**Created**: 2026-06-25
**Status**: Ready for planning
**Parent design**: `docs/superpowers/specs/2026-06-25-verify-unification-design.md` (stage B1)

## Summary
The Verify-unification design computes the rich verdict **client-side** on both verifier hosts. For
that, a verifier polling HAIP for a presentation result needs the **raw submitted presentation** back,
not just HAIP's pass/fail verdict. Today HAIP discards the raw `vp_token` after validating it. This
feature makes HAIP **persist and return the raw `vp_token` (and `presentation_submission`)** alongside
its existing `VerificationResult`, so a client can re-validate locally and build the 4-layer trail.

This wave is **purely additive** to the verifier result response. No auth changes, no UI.

## User Scenarios & Testing

### Primary (verifier client re-validates locally)
1. A verifier creates a presentation request (existing `POST /api/v1/verifier/requests`).
2. A holder wallet submits its `vp_token` via `direct_post` (existing).
3. The verifier polls the result endpoint and receives — in addition to today's fields — the **raw
   `vp_token`** and **`presentation_submission`** that the holder submitted.
4. The verifier can now feed that raw token to a local validator to produce its own rich verdict.

### Acceptance scenarios
- **Given** a request that no holder has answered yet, **when** the result is polled, **then** the
  response contains a null `vpToken` (nothing submitted) and the existing `state`.
- **Given** a holder has submitted a `vp_token` via `direct_post`, **when** the result is polled,
  **then** the response includes the exact raw `vp_token` string and the `presentation_submission`
  the holder posted, alongside the existing `result`.
- **Given** a request whose TTL has expired, **when** the result is polled, **then** behaviour is
  unchanged from today (the stored request is gone, so no raw token leaks beyond TTL).

## Functional Requirements
- **FR-001**: The stored presentation request MUST retain the raw `vp_token` submitted via
  `direct_post`, scoped to the request's existing TTL (no new retention window).
- **FR-002**: The stored presentation request MUST retain the `presentation_submission` submitted
  alongside the `vp_token`, when present.
- **FR-003**: The verifier result endpoint response MUST include the raw `vp_token` and
  `presentation_submission` once a holder has submitted; both MUST be null/absent before submission.
- **FR-004**: Existing response fields (`requestId`, `state`, `result`) MUST be unchanged
  (additive only — no breaking change to the Blueprint Service consumer).
- **FR-005**: Behaviour for not-found and expired requests MUST be unchanged.

## Key Entities
- **PresentationRequest** (stored): gains `SubmittedVpToken` and `PresentationSubmission` fields,
  populated on `direct_post`, returned on the result poll.

## Success Criteria
- **SC-001**: A poll after submission returns the byte-identical `vp_token` the holder posted.
- **SC-002**: The Blueprint Service result consumer continues to work unchanged (no field removed
  or renamed).
- **SC-003**: The raw token is never returned before submission or after TTL expiry.

## Assumptions
- HAIP's `direct_post` is `vp_token`-only; holder→device binding is carried inside the token's
  KB-JWT. There is no separate "delegation credential" form field to capture, so this wave does not
  add one. (Client-side validation of any device-delegated model is a PR B2 concern.)
- The verifier-tier authorization allowance on create/poll (today `RequireService`) is **out of
  scope here** and deferred to PR B3, where the hosts actually call HAIP and the tier can be
  verified live.

## Out of scope
- Any UI change. Any change to HAIP's own server-side validation. Auth-tier changes (→ B3).
- Separate delegation-credential capture (→ B2 if needed).
