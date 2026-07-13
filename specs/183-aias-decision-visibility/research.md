# Research: AIAS decision integrity & visibility

Most decisions were settled during brainstorming (`docs/superpowers/specs/2026-07-12-aias-emailverified-claim-source-design.md`). This file records the load-bearing choices and the alternatives rejected, so no NEEDS CLARIFICATION remains.

## D1 — How to carry the applicant's real email-verified status onto the submission

- **Decision**: A headless, schema-declared claim binding (`x-claim-source: "<claim>"`) seeded at form init from the authenticated `ClaimsPrincipal`, written into `FormContext.FormData` so it rides the wallet-signed payload.
- **Rationale**: The `email_verified` claim already exists on every user token (`TokenService` mints it from `platformUser.EmailVerified`, re-emitted on refresh — F157) and is already on the client principal (`CustomAuthenticationStateProvider` reads all claims raw, `MapInboundClaims = false`). Writing into `FormData` is the proven `HolderKeyRenderer` / persona-autofill mechanism, so the value is covered by the wallet signature (FR-002). Correct: the gate becomes real (verified→approved, unverified→rejected).
- **Alternatives rejected**:
  - *Carry the schema `default` (always true)* — makes the gate cosmetic; an unverified web user (login does not block unverified) would be wrongly approved.
  - *Agent queries account status* — cross-org lookup (applicant in public org, agent in AIAS org), extra auth surface, and gameable (type anyone's verified email).
  - *Server-side stamp in `ActionExecutionService`* — the payload is wallet-signed; a server-derived field is not covered by the signature.

## D2 — Headless binding vs a visible format-dispatched control

- **Decision**: A **headless** `x-claim-source` extension parsed at form init, independent of page placement — NOT a `format`-dispatched control.
- **Rationale**: The renderer only dispatches controls for fields placed on an `x-page`/`x-section`; `emailVerified` is agent-facing metadata, not user input, and is on no page. A claim binding that works only when the field is visible is a weak, non-reusable primitive. A form-init seed pass (mirroring the F152 `InitialFormData` seed + the persona-autofill fire-and-forget) covers any field regardless of placement.
- **Alternatives rejected**: `format: "sorcha-email-verified"` control (the initial preview) — would force the field onto a visible page and is single-purpose.

## D3 — Fail-closed coercion

- **Decision**: For a `type: boolean` property, coerce the claim string case-insensitively (`"true"`→true, everything else including absent/unparseable→false). Non-boolean types seed the raw string only when the claim is present.
- **Rationale**: An expired/absent session must never be treated as verified (FR-003, security posture). Matches the agent's own `EmailVerifiedCheck` tolerance (bool or "true"/"false").

## D4 — Where the reject reason is available (notification hook point)

- **Decision**: Hook in `ActionExecutionService` after route resolution, when the selected route carries `x-decision-notice`. The just-merged action payload holds `verificationNotes`; the recipient is resolved from the instance participant bindings.
- **Rationale**: The AIAS reject is a **terminal-route** decision (agent submits action 2 `decision: rejected` → route `nextActionIds: []`). Today `ReactionDispatcher` fires only an ephemeral `WorkflowCompleted` SignalR signal — no durable entry, no reason. The reason is in hand only at route-selection time. `BlueprintInboxWriter` already resolves wallet→participant→PlatformUserId→inbox and has deterministic idempotency, so `WriteDecisionAsync` reuses all of it.
- **Alternatives rejected**:
  - *Extend `ReactionDispatcher`'s workflow-completed path* — generic, has no reason and can't distinguish reject from claim-complete.
  - *A new My Applications page / email* — larger; deferred to a follow-up (spec Out of Scope).

## D5 — Reject-only (approval already notified)

- **Decision**: The new writer fires on **reject only**.
- **Rationale**: Approval already produces durable notifications — the claim action becoming available fires `BlueprintInboxWriter.WriteActionAvailableAsync`, and credential delivery fires `WalletInboxWriter.WriteCredentialReceivedAsync`. A new approval entry would double-notify. FR-013 makes "no duplicate approval notice" explicit.

## D6 — Recipient = the starting participant, declared explicitly

- **Decision**: `x-decision-notice.recipientParticipantId` names the participant (for AIAS: `"citizen"`, the `isStartingAction: true` sender). Resolve it to a wallet via the instance participant bindings — the same resolution `credentialIssuanceConfig.recipientParticipantId` already uses for delivery.
- **Rationale**: Explicit and reusable; avoids inferring recipients from disclosure rules (a rabbit hole). The credential delivery path proves this resolution works for `citizen`.

## D7 — Idempotency & fail-safety

- **Decision**: Deterministic `SourceEventId` from `(recipientWallet, instanceId, actionId, "decision-notice")` (mirrors the existing `WriteActionAvailableAsync` helper); the whole write is `try` / `LogWarning` / swallow.
- **Rationale**: FR-010 (never block/reverse the decision) and FR-011 (no duplicates on retry/replay). Matches every existing inbox writer's contract.

## D8 — Testing strategy

- **Decision**: Pure `ClaimSourceSeeder.Resolve(schema, principal)` unit tests (no bUnit) as the tight guard on the client bug; `BlueprintInboxWriter` decision-write + `ActionExecutionService` routing tests for the server; de-hardcoded `rehearse.ps1` with an added unverified→reject case as the end-to-end regression; Chrome DevTools for live n1 confirmation.
- **Rationale**: The client bug is "value absent from the wire though the field is on no page" — a pure resolver test proves the value lands. bUnit wiring is covered by the renderer edit but the resolver test is the essential regression.
