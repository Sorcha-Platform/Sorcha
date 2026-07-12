# AIAS `emailVerified` gate — claim-source binding fix

**Date:** 2026-07-12
**Feature:** 174 (AIAS Assured Identity) / M1
**Author:** Stuart + Claude
**Status:** Design — awaiting spec-review approval

## Problem

Every citizen application submitted through the **web UI** to the AIAS Assured Identity
demo is rejected by the autonomous Assure-ID agent with *"AIAS needs a verified email
before it can assure you."* — so no real user can ever receive a credential.

### Root cause (verified)

1. The blueprint (`demos/AIAS/blueprints/aias-assured-identity.template.json`, action 1)
   declares `emailVerified: { type: boolean, default: true, readOnly: true }`. The field
   is **not placed on any `x-page` / `x-section`**.
2. The web form renderer only creates controls for fields referenced by a page/section,
   so no control renders for `emailVerified`, nothing writes it into `FormContext.FormData`,
   and `FormPayloadBuilder.BuildNested` (which serialises *only* what is in `FormData`)
   omits it. **Real submissions carry `[name, dob, email, address, holderKeys, portrait]`
   with no `emailVerified` key** (confirmed by decoding a DevMode action-1 transaction).
3. `EmailVerifiedCheck.cs` resolves `/emailVerified` from the payload; absent → `false`.
4. `assure-id.rules.json` rule 3: `checks.emailVerified == false` → **reject**.

Admin-verifying the account does nothing — the check reads the *payload*, never the account.
The `rehearse.ps1` harness **hardcodes `emailVerified = $true`** in its hand-built payload
(line 101), which masked the bug: "all paths pass" never exercised the real web-form shape.

### Why the obvious alternatives were rejected

- **Carry the schema `default` (always `true`)** — fastest, but the email-verified gate
  becomes cosmetic for the web path. Login does **not** block unverified users
  (`LoginService` mints a token for them carrying `email_verified=false`), so an unverified
  web user would be **wrongly approved**. Real correctness hole.
- **Agent queries account status** — the applicant lives in the *public* org while the agent
  authenticates in the *AIAS* org (cross-org lookup + auth surface), and it is gameable
  (type anyone's already-verified email). Dominated.
- **Server-side stamp in `ActionExecutionService`** — the action payload is **wallet-signed**;
  a server-derived field is not covered by the signature. Ruled out.

## Chosen approach

**Stamp `/emailVerified` from the authenticated user's real `email_verified` claim, client
side, before signing** — via a small, reusable schema-driven *claim-source binding*.

This is correct (the value reflects real account state and is covered by the wallet
signature, so the gate genuinely fires: verified → approved, unverified → rejected), it
matches the field's own documented intent ("carried automatically from the platform's
email-verification state"), and the infrastructure already exists:

- **The JWT already carries `email_verified`** (`"true"`/`"false"`). `TokenService` mints it
  at login from `platformUser.EmailVerified` and re-emits it on refresh (Feature 157).
- **`CustomAuthenticationStateProvider`** reads every JWT claim raw (`MapInboundClaims =
  false`), so the client already holds `email_verified` on the `ClaimsPrincipal`.
- **`HolderKeyRenderer` / persona autofill** are proven precedents for writing values into
  `FormContext.FormData` so they ride the payload and the wallet signature.

### Refinement vs. the option preview

The decision preview floated `format: "sorcha-email-verified"`. Format-based dispatch in this
codebase is *control-based* and requires the field to be **placed on a visible page**.
`emailVerified` is agent-facing metadata, not user input — forcing every claim-backed field
onto a page is a poor general primitive. So we implement the binding as a **headless
schema extension `x-claim-source`**, seeded at form-init independent of page placement.
This is the genuinely reusable shape the "reusable claim-source binding" choice asked for.

## Components

### 1. Schema extension: `x-claim-source`

A string property extension naming a JWT claim to seed the field from. Reusable by any
future blueprint field. Follows the existing `x-*` convention (`x-rule`, `x-address-lookup`,
`x-file`, `x-holder-key`).

Blueprint change (`aias-assured-identity.template.json`, action 1, `emailVerified` property):

```jsonc
"emailVerified": {
  "type": "boolean",
  "title": "Email verified",
  "description": "… Carried automatically from the platform's email-verification state …",
  "default": true,
  "readOnly": true,
  "x-claim-source": "email_verified"   // NEW
}
```

### 2. `ClaimSourceSeeder` (new — pure, unit-testable)

`src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Forms/ClaimSourceSeeder.cs`
(namespace `Sorcha.UI.Core.Services.Forms`).

```csharp
public interface IClaimSourceSeeder
{
    // Walks top-level schema properties carrying `x-claim-source`, reads the named claim
    // from `user`, coerces to the property's declared `type`, and returns pointer→value
    // ("/emailVerified" → true). Boolean claims fail closed: absent/unparseable → false.
    IReadOnlyDictionary<string, object?> Resolve(JsonDocument? mergedSchema, ClaimsPrincipal? user);
}
```

Coercion rules:
- property `type: boolean` → `bool` (`"true"`/`"false"` case-insensitive; anything else → `false`).
- other types → the raw claim `string` (only when the claim is present).
- property has no `x-claim-source`, or the claim is absent for a non-boolean → not seeded.

Boolean fail-closed is the correct security posture: an expired/absent session must not be
treated as verified. Top-level scan only (matches the demo; nested is a documented YAGNI
extension point).

### 3. Renderer wiring

`SorchaFormRenderer` resolves `AuthenticationStateProvider` + `IClaimSourceSeeder` via
`IServiceProvider.GetService` (same graceful-skip pattern persona autofill uses, so bUnit
contexts without them registered are unaffected). On `actionChanged`, a fire-and-forget
`SeedClaimSourcesAsync` — mirroring `LoadPersonaAndApplyAsync` — reads the auth state,
calls `Resolve`, and writes each result into `_formContext.FormData` **only if the user has
not already set that pointer**, then `StateHasChanged`. The read is a local cached-token
parse (milliseconds); the multi-page wizard guarantees it completes well before submit.

Data flow to the wire is unchanged and identical to holder-keys: `FormData` →
`FormSubmission.Data` → host page → `FormPayloadBuilder.BuildNested` → `PayloadData` →
wallet-signed → register. The agent's `EmailVerifiedCheck` now reads a real boolean.

### 4. Process fix (regression guards)

- **`rehearse.ps1`** — stop hardcoding. Tie the approve path's `emailVerified` to the
  applicant's real verified state (the harness already admin-confirms the email, so
  `true` is correct and now *mirrors* the client stamp, with a comment saying so). **Add a
  third case**: an *unverified* applicant (skip `Confirm-SorchaUserEmail`) submitting with
  **no `emailVerified` key** → assert **reject** with the email reason and **no credential**.
  This exercises the real gate in both directions via the API and can never silently pass
  again on an absent field.
- **`ClaimSourceSeederTests`** (`tests/Sorcha.UI.Core.Tests/Components/Forms/`) — the tight
  guard on the client bug, no bUnit needed:
  - schema with `emailVerified` boolean + `x-claim-source`, principal `email_verified=true`
    → `/emailVerified: true`;
  - principal `email_verified=false` → `/emailVerified: false`;
  - claim absent → `/emailVerified: false` (fail-closed);
  - property without `x-claim-source` → not seeded.

  Proves the value lands even though the field is on no page — the exact regression.

## Decision notification — making the reject route visible

A genuine reject route the applicant cannot see is a black hole. Today, when the AIAS agent
rejects (submits action 2 `decision: rejected` → **terminal** route `nextActionIds: []`), the
`ReactionDispatcher` fires `NotifyWorkflowCompletedAsync`, which sends **only an ephemeral
SignalR `WorkflowCompleted` signal — no durable inbox entry and no reason**. The on-brand
`verificationNotes` is disclosed to the citizen in the ledger but no surface reads it, and
`/my-workflows` is a legacy redirect stub — so a rejected applicant sees nothing (exactly the
reported experience). Approval, by contrast, is *already* durably notified: the claim action
becoming available fires `BlueprintInboxWriter.WriteActionAvailableAsync`, and delivery fires
`WalletInboxWriter.WriteCredentialReceivedAsync`.

**Scope (confirmed): reject notification in this PR; a "My Applications" history page + email
are a follow-up issue.** Approval needs no new writer — it is already surfaced; adding one
would only double-notify.

### Design — a blueprint-declared terminal-decision notice

Reuse the F118 durable-inbox pattern. The reason is in hand only at route-selection time, so
the hook is `ActionExecutionService`, guarded and fail-safe (an inbox-write failure must never
affect sealing/routing — matches every existing inbox writer).

1. **Route annotation `x-decision-notice`** (reusable, blueprint-declared — mirrors the
   `credentialIssuanceConfig` shape already on this action):

   ```jsonc
   // aias-assured-identity.template.json, action 2, "rejected-terminal" route:
   "x-decision-notice": {
     "recipientParticipantId": "citizen",
     "reasonField": "/verificationNotes",
     "title": "AIAS could not assure your identity",
     "severity": "Warning"
   }
   ```

2. **`BlueprintInboxWriter.WriteDecisionAsync(...)`** (new method on the existing writer) —
   reuses the same wallet → participant (`IParticipantServiceClient`) → `PlatformUserId`
   (`IPlatformInboxClient`) resolution and deterministic-idempotency helper. Writes an inbox
   entry: `Category: "Workflow"`, `Severity` from the annotation, `Title` from the annotation,
   **`Summary` = the resolved reason string** (the on-brand `verificationNotes`),
   `DetailHref: /api/instances/{instanceId}`, idempotency `SourceEventId` derived from
   `(recipientWallet, instanceId, actionId, "decision-notice")`.

3. **Hook** — in `ActionExecutionService`, after routes are resolved for a submitted action:
   for any selected route carrying `x-decision-notice`, resolve the recipient participant's
   wallet from the instance participants (the same participant→wallet resolution the credential
   delivery already uses for `recipientParticipantId`), resolve the reason from the just-merged
   payload at `reasonField`, and call `WriteDecisionAsync`. Wrapped in `try` / `LogError` /
   swallow.

The F118 bell drawer renders inbox entries generically, so **no client change is needed** for
the reject entry (and its reason) to appear — durable, cross-session, cross-device. The
existing ephemeral `WorkflowCompleted` signal is unchanged; this adds the durable record.

### Notification tests

- **`BlueprintInboxWriter` decision-write test** — resolves recipient, carries the reason as
  the summary, and is idempotent on retry; short-circuits on unresolved wallet/user.
- **`ActionExecutionService` routing test** — a terminal route carrying `x-decision-notice`
  triggers exactly one decision write with the resolved reason; a route without the annotation
  writes nothing; an inbox-write throw does not fail the submission.

## Deployment (n1)

All artifacts must ship together for the live gate + reject visibility to work:

1. **Web client image** — the `ClaimSourceSeeder` + renderer wiring build into `sorcha-ui-web`.
2. **Blueprint Service image** — the `x-decision-notice` hook in `ActionExecutionService` +
   `BlueprintInboxWriter.WriteDecisionAsync` build into the blueprint service image.
3. **Live blueprint** — the seeder only fires if the *provisioned* AIAS blueprint schema
   carries `x-claim-source`, and the reject notice only fires if the reject route carries
   `x-decision-notice`. The current live blueprint (`aias-assured-identity-20260712152806`)
   predates both, so the AIAS demo blueprint must be **re-provisioned** from the updated
   template (new blueprint id → update `state.json` + `assure-id.config.json` + restart the
   local agent). No `down -v` / no re-genesis.

Code-only deploy per the n1-deploy skill: build the changed images → push/pull `:latest`
(Docker Publish) or `docker save`/`scp`/`load` the two changed services → `up -d
--force-recreate --no-deps <svc>`. Standing `up` must keep `-f docker-compose.smtp.yml`.

## Verification bar (SC)

Drive the real web app on `https://n1.sorcha.dev/app` with Chrome DevTools.

1. **Happy path** — sign up a fresh citizen, verify email (ACS or admin-confirm), submit the
   AIAS application with a real UK postcode (e.g. `EH9 1JA`) + a photo. Confirm via the captured
   action-1 network request that **`emailVerified: true` is now on the wire**, the agent
   **approves**, and the `AssuredIdentityCredential` is delivered into the wallet.
2. **Reject visibility** — drive (or, via the API, submit) an application that the gate rejects,
   and confirm a **durable bell/inbox entry carrying the on-brand reason** appears for the
   applicant (survives reload / re-login) — no longer a silent black hole.

## Out of scope (follow-up issue)

- A citizen-facing **"My Applications" status/history page** (list of submitted applications with
  status + reason). Tracked as a follow-up.
- **Transactional email** on decision (F112).
- Nested (non-top-level) `x-claim-source` pointers; generalising `x-decision-notice` recipient
  resolution beyond an explicit `recipientParticipantId`.
- The two already-working reject routes (postcode, profanity) and the agent/rules.
- PWA parity (AIAS is a web-`/app` demo; the shared component picks up the fix regardless).
