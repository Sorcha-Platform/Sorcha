# Spec 4 — Credential-gated second service (Blue Badge)

**Date:** 2026-05-15
**Status:** Design locked. Brainstorm complete — six decisions captured in §10.
**Umbrella:** [`2026-05-13-strathcarron-citizen-arc.md`](2026-05-13-strathcarron-citizen-arc.md)
**Spec 1 implementation tag:** `spec-124-complete`
**Spec 2 implementation tag:** `spec-125-complete`
**Spec 3 implementation tag:** `spec-126-complete`

## Purpose

The architecture pays for itself. Sarah returns to Strathcarron a few weeks after her cold-start onboarding to apply for a Blue Badge. The council form's first action is **gated on her existing `AssuredIdentityCredential`** — the credential she received as a side-effect of her driving-licence application in Spec 3. She presents it from her wallet, the form already knows who she is, she fills only the new fields, submits, and the `BlueBadgeCredential` lands in the same wallet that delivered her `AssuredIdentityCredential` last time.

This is the spec where the existing wallet stops being "the thing that received a credential" and starts being **the thing that proves something**.

## What's locked by the umbrella

| # | Decision | Source |
|---|----------|--------|
| 1 | **Subject is Blue Badge.** Generic enough to be plausible council content; specific enough to be visually distinct from a driving licence. | Umbrella §"Spec 4" |
| 2 | **One issuer per credential, one credential per service.** Strathcarron Council issues `BlueBadgeCredential` exactly as it issued `AssuredIdentityCredential` in Spec 3 — same target audience (`SorchaLocalWallet`), same delivery pattern, different blueprint. | Umbrella invariant #2 |
| 3 | **Generic claim names.** `dateOfBirth`, `givenName` — not `strathcarronDob`. Discipline that makes Spec 5's third-party-verifier story honest. | Umbrella invariant #3 |
| 4 | **Wallet reuses `x-review` id-card renderer** with `Watermark=Issued` for the new credential. One visual component, same rendering as `AssuredIdentityCredential`. | Umbrella invariant #4 |
| 5 | **Hybrid universal QR is the only invocation mechanism.** Same artifact as Spec 3, same three resolutions (scan, tap, paste). The QR pattern from Spec 3 carries through. | Umbrella invariant #5 |
| 6 | **Email/password is the account anchor.** Sarah's account already exists; this spec adds no new account model. | Umbrella invariant #6 |
| 7 | **PWA and Sorcha.UI.Web are co-equal.** Citizen can drive the application from either device. | Umbrella invariant #7 |

## §1 — What ships

The visible deliverables:

1. **`BlueBadgeCredential` blueprint** (issuer: Strathcarron Council, target audience: `SorchaLocalWallet`, same shape as `AssuredIdentityCredential`).
2. **Credential-gated starting action** — the Blue Badge application's first action requires the citizen to present an `AssuredIdentityCredential` from their wallet. Open-participant late binding is preserved (Feature 103 / `x-review` machinery).
3. **PWA picker + ConsentSheet** — when the council page asks the wallet to present a credential satisfying the gate, the PWA renders the existing F125 picker (designed in Spec 2) and a ConsentSheet showing the claims being disclosed.
4. **Autofill on the council form** — the disclosed claims from the presented credential pre-populate fields on the Blue Badge form (per the existing `PersonaAutofillResolver` + `x-persona` extension contract from Feature 092, with the **credential as the source** instead of the persona).
5. **Issuance of `BlueBadgeCredential`** after the citizen submits — lands in the same PWA wallet via the existing F124 register-native delivery path.

## §2 — The returning-citizen journey, step by step

Sarah comes back to Strathcarron's council site three weeks after onboarding. She browses to `/strathcarron/services/blue-badge`.

### Step 0 — `EnrolGateComponent` returns FastPath

The Feature 126 gate probes `/whoami` + `/me/devices`. Sarah is signed in (account from Spec 3), has at least one paired wallet device (also from Spec 3). Tier 1. Form renders.

### Step 1 — Council page renders the application surface

Standard form with a prominent **"Prove you're you"** section at the top. Plain-English explainer:

> To apply for a Blue Badge we need to confirm your identity. Tap the button below — your wallet will ask you which credential to use.

Below it: a **Present from wallet** button.

### Step 2 — Citizen taps the button

Council page calls a new server endpoint that builds an OID4VP presentation request asking for **any credential of type `AssuredIdentityCredential`** issued by Strathcarron Council. Endpoint returns a presentation-request URL + nonce; council page renders the hybrid QR (using `HybridQrAffordance` from Spec 3, `Layout=Auto`).

Same-device path: tap-link opens the PWA at `/wallet/present?request=<encoded-request-uri>`.
Cross-device path: QR scanned by the phone wallet.

### Step 3 — PWA picker + ConsentSheet

PWA loads the presentation request. The wallet's existing F125 picker surface (`/wallet/present`) walks the citizen through:

1. **Picker**: shows credentials in the wallet that satisfy the request. Sarah has one (`AssuredIdentityCredential`); it's selected by default. (See open question 10.2 on multi-credential selection UX.)
2. **ConsentSheet**: lists the claims being disclosed. (See open question 10.1 on per-claim toggles vs all-or-nothing.)
3. **Confirm**: signs the VP, posts it to the council's presentation-response endpoint.

### Step 4 — Council page picks up the presentation

The council page is subscribed to a server-side completion event (server-set cookie or SignalR — see open question 10.3). On receipt, the page rerenders with:

- The "Prove you're you" section collapsed to "Verified ✓ — Sarah Example (sarah@example.test)".
- The remaining form fields pre-populated with the disclosed claims (`givenName`, `familyName`, `dateOfBirth`, `homeAddress`).
- The Blue Badge-specific fields (`mobilityCondition`, `previousBadgeNumber`) empty for Sarah to fill.

### Step 5 — Sarah submits the form

Standard submit. Late-bind sender = Sarah's wallet address (same pattern as Spec 3). The blueprint's second action (`issue-blue-badge`) mints a `BlueBadgeCredential` and delivers it into Sarah's wallet via `SorchaLocalWallet`.

### Step 6 — Watch your wallet

Success copy mirrors Spec 3: "Your application is in. Watch your wallet — your Blue Badge will arrive within a few seconds." Wallet's F124 first-credential takeover does **not** fire (Sarah already has a credential); the new credential just appears in the home-row stack.

## §3 — Credential-gated blueprint shape

A new pattern on the starting action: `prerequisites.presentationRequests`.

```json
{
  "id": "submit-blue-badge-application",
  "isStartingAction": true,
  "actor": "citizen",
  "prerequisites": {
    "presentationRequests": [
      {
        "id": "assured-identity-check",
        "credentialType": "AssuredIdentityCredential",
        "issuerAllowlist": ["did:sorcha:org:strathcarron-council"],
        "requiredClaims": ["givenName", "familyName", "dateOfBirth", "homeAddress"]
      }
    ]
  },
  "schema": {
    "type": "object",
    "required": ["mobilityCondition"],
    "properties": {
      "mobilityCondition": { "type": "string", "title": "Reason for the Blue Badge" },
      "previousBadgeNumber": { "type": "string", "title": "Previous Blue Badge number (if any)" }
    }
  },
  "x-persona": {
    "presentation": "assured-identity-check"
  }
}
```

The blueprint runtime resolves `prerequisites.presentationRequests` into the OID4VP presentation requests the council page advertises. The `x-persona.presentation` extension reuses Feature 092's autofill machinery — the renderer fills disclosed fields automatically and surfaces the `PersonaFillSummary` banner with `Source=PresentedCredential` (a new provenance value alongside `self` and `verifier`).

## §4 — Server surface (new + extended)

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/blueprint/presentation-requests` | Council page mints a presentation request from a blueprint's starting-action prerequisites. Returns `{ requestUri, nonce, qrUrl, expiresAt }`. |
| `POST` | `/api/blueprint/presentation-responses` | PWA posts a signed verifiable presentation. Server validates against the original request, stashes the disclosed claims keyed by `nonce`, fires a SignalR `PresentationReceived(nonce)` event. |
| `GET` | `/api/blueprint/presentation-responses/{nonce}` | Council page fetches the stashed claims after the SignalR event fires (or via polling fallback, same pattern as Feature 126's `IEnrolPairingSignal`). |

`Sorcha.Verifier.Engine` (the validator extracted in F125) runs the verification on the server side. This is the **first non-PWA consumer** of that engine — closing the loop the F125 design promised.

## §5 — Library component growth

A new component, `CredentialGateComponent`, sits **above** `EnrolGateComponent` (or alongside it) in the council page composition. Composition pattern:

```razor
<EnrolGateComponent CouncilName="Strathcarron Council" OnReady="@HandleReady">
    <CredentialGateComponent BlueprintId="@_blueprintId"
                             StartingActionId="submit-blue-badge-application"
                             OnPresented="@HandlePresentedAsync">
        <!-- the application form goes here -->
        <BlueBadgeForm Disclosed="@_disclosed" />
    </CredentialGateComponent>
</EnrolGateComponent>
```

`CredentialGateComponent` owns: minting the presentation request, rendering the hybrid QR/link/paste affordance, subscribing to the `PresentationReceived` SignalR event, fetching the disclosed claims, and firing `OnPresented` with the claim dictionary.

If no `prerequisites.presentationRequests` is defined on the starting action, `CredentialGateComponent` renders `ChildContent` directly — drop-in for any blueprint, gated or not.

## §6 — Reusing Spec 1's `AssuredIdentityCredential`

Sarah's `AssuredIdentityCredential` is **the same credential** issued during the driving-licence application in Spec 3. No new issuer setup, no new keys, no new register. The Strathcarron Council credentials register from F126 carries both `AssuredIdentityCredential` and `BlueBadgeCredential` revocation status (Feature 079 trust hardening).

For the demo to land, Spec 3's seed walkthrough needs to be reachable from Spec 4's setup: a "rerun the Tier 1 fast-path" walk that ends with the citizen holding `AssuredIdentityCredential` BEFORE the Blue Badge journey starts.

## §7 — Failure paths

| What goes wrong | Citizen sees |
|---|---|
| Citizen has no credential matching the gate | Council page renders an error state: "We need a `AssuredIdentityCredential` from Strathcarron Council to continue. If you don't have one yet, apply for a driving licence first." Link back to the driving licence form. |
| Citizen has multiple matching credentials | Picker surfaces all of them, sorted by issuance date desc. Selection UX — see open question 10.2. |
| Citizen taps the link on the wrong device | Same as Spec 3's friend-scans-by-mistake: PWA-side `EnrolmentRedeemConfirmDialog`-style confirmation (showing the credential the wallet is about to present + the council it's being presented TO) before signing. (See open question 10.4.) |
| Presentation request expired | Council page renders "QR expired — let's get you a new one" with a regenerate button (same shape as Spec 3 §7). |
| Presentation signature fails verification on the server | Council page surfaces "Couldn't verify that credential — try again." Wallet logs the failure to the F125 verification history surface. |
| Credential revoked at the moment of presentation | Server-side `Sorcha.Verifier.Engine` returns a status-list-hit. Council page surfaces "This credential has been revoked. Please contact Strathcarron Council." |

## §8 — New server surface; reuses everything we have

What's new in F127:
- The two presentation-request / response endpoints (§4).
- `prerequisites.presentationRequests` blueprint syntax + runtime resolver.
- `CredentialGateComponent` library component.
- Blue Badge blueprint + register-side issuance wiring.

What's already in place:
- `Sorcha.Verifier.Engine` — F125's extracted validator does the heavy lifting server-side.
- `HybridQrAffordance` — drop-in from F126.
- SignalR + polling fallback pattern — same `IEnrolPairingSignal` shape applies (`IPresentationSignal`).
- F124 register-native credential delivery — `BlueBadgeCredential` lands in the wallet exactly as `AssuredIdentityCredential` did.
- F092 `PersonaAutofillResolver` + `x-persona` extension — extended with `presentation` as a source.

## §9 — Testing strategy

| Layer | Coverage |
|---|---|
| Blueprint Service unit | `BlueprintRuntime.ResolvePrerequisitesAsync` — surfaces the OID4VP presentation requests; rejects malformed gates. |
| Blueprint Service integration | `POST /api/blueprint/presentation-requests` returns a request URL + nonce; `POST .../presentation-responses` validates and stashes claims. |
| `Sorcha.Verifier.Engine` integration | Server-side path: feed a presentation produced by a real F125 PWA flow, assert the validator returns the expected disclosed claims + a clean trust status. |
| Component | `CredentialGateComponent` — renders the hybrid affordance when prerequisites exist, falls through to ChildContent when absent, fires OnPresented on the SignalR event. |
| PWA flow | Picker + ConsentSheet — the wallet selects the right credential, signs the VP with `IUserSigner`, posts the response. |
| E2E (Playwright) | `[Demo("blue-badge-second-service")]` — full Tier 1 returning-citizen walk: arrive → present → autofill → submit → credential lands. |

## §10 — Decisions captured (brainstorm 2026-05-15)

The six load-bearing questions the umbrella deferred to this spec. Each entry below records the question, the option chosen, and why.

| # | Decision | Rationale |
|---|----------|-----------|
| Q1 | **ConsentSheet is all-or-nothing in v1.** Single Confirm / Decline over the full disclosed claim list. | Keeps the citizen on familiar consent-prompt ground. Per-claim toggles introduce a "your request can't be granted" failure surface that's not justified by Spec 4's use case. Per-claim disclosure lands as future hardening when a real use case (e.g. third-party verifier asking for more than the minimum) demands it. |
| Q2 | **Picker hides itself when exactly one credential matches; force-selects when ≥2.** ConsentSheet always renders. | Strathcarron's Spec 4 demo has one credential per citizen — the picker would be intrusive on the common path. The picker is honest about the multi-match case but doesn't add friction when there's nothing to choose. |
| Q3 | **SignalR with polling fallback** (`IPresentationSignal` — a one-line variant of F126's `IEnrolPairingSignal`). | Spec 3's hybrid pattern works; same 2 s hub-connect window, 3 s polling cadence, 60 s manual-recovery ceiling. Reuses `Sorcha.Verifier.Engine` for verification and the existing hub topology for transport. |
| Q4 | **PWA-side confirmation dialog before signing** the VP — shows the credential type being presented + the verifier asking for it. | Mirrors Spec 3's `EnrolmentRedeemConfirmDialog` trust model. Server-set cookie binding is deferred to Spec 5 (the cross-org leg) where the verifier-is-not-the-issuer story is the load-bearing concern. |
| Q5 | **Walkthrough setup script depends on Spec 3's `state.json`** with chained Spec 3 setup as fallback. | Existing AssuredIdentity phase-1 → phase-2 convention. Operator runs Spec 3's setup first, then Spec 4's setup picks up the citizen's existing `AssuredIdentityCredential` from the F126 walkthrough state. |
| Q6 | **`EnrolGateComponent` wraps `CredentialGateComponent` wraps the form.** Linear composition. | First sign in / pair a device, then present a credential. Returning Tier 1 citizens with a paired device and matching credential glide through both gates with two transient screens at most. Cold-start citizens hit Spec 3's preflight first; if they need a credential they don't have, the no-credential error state points them back at the driving-licence flow. |

### Q1 — ConsentSheet disclosure surface: per-claim toggles vs all-or-nothing

The presentation request lists `requiredClaims`. When the PWA renders the ConsentSheet, does it:

**A. All-or-nothing** — "This council wants: givenName, familyName, dateOfBirth, homeAddress. [Confirm] [Decline]". Simple, fast, no decisions for the citizen.

**B. Per-claim toggles** — every claim has a toggle. The citizen can decline individual claims; the wallet decides whether the resulting subset still satisfies `requiredClaims`. Honest about what's being shared, but introduces friction and a "your request can't be granted" failure path.

**Recommendation:** A for v1 — keeps the cold-start citizen on familiar ground (consent dialogs they recognise from existing apps). Per-claim toggles land in a follow-up when there's a use case (e.g. a third-party verifier asking for more than the strict minimum).

### Q2 — Multi-credential selection UX

If Sarah holds two `AssuredIdentityCredential`s (maybe she went through the cold-start journey twice during testing), the picker needs to choose between them.

**A. Auto-select most-recent** — render the picker preselected; citizen confirms.

**B. Force selection** — render the picker with no default; citizen must tap.

**C. Hide the picker entirely when exactly one credential matches** — render only the ConsentSheet. Picker fires only when ≥2 match.

**Recommendation:** C, falling back to A when there's a tie. Strathcarron's Spec 4 demo has one credential per citizen; the picker is honest about the rare multi-match case but doesn't intrude in the common path.

### Q3 — Cross-device coordination after presentation

After the PWA posts the VP, how does the council page learn?

**A. SignalR with polling fallback** — same shape as Spec 3's `IEnrolPairingSignal`. New `IPresentationSignal`.

**B. Server-set cookie + redirect** — PWA posts a redirect URL back to the council page; browser-mediated handoff.

**C. Plain polling** — council page polls `GET /api/blueprint/presentation-responses/{nonce}` every 2 s.

**Recommendation:** A. Spec 3's hybrid pattern works; the verifier-side `IPresentationSignal` is a one-line variant of `IEnrolPairingSignal`.

### Q4 — Friend-scans-by-mistake mitigation

Spec 3 uses an `EnrolmentRedeemConfirmDialog` that shows the bound user's email + display name before any device-pairing happens. Spec 4 has a parallel risk: someone else scans Sarah's presentation QR, and their wallet starts presenting their own `AssuredIdentityCredential` to Strathcarron's Blue Badge form.

**A. Confirmation dialog on the PWA side** showing the council + the credential type being presented before signing — same pattern as Spec 3.

**B. Bind the presentation request to the citizen via a server-set cookie** (deferred future hardening per umbrella).

**Recommendation:** A. v1 mitigation. The wallet shows "You're about to present `AssuredIdentityCredential` to Strathcarron Council. If that's not what you wanted, cancel." Same trust model as Spec 3.

### Q5 — Walkthrough seeding

Spec 4's demo requires Sarah to already hold an `AssuredIdentityCredential` before the Blue Badge journey starts.

**A. Spec 4 setup script chains Spec 3's setup as a prerequisite step.** Operator runs one command.

**B. Spec 4 setup script duplicates the credential-issuance step.** Cleaner isolation but copy-pasted blueprint provisioning.

**C. Spec 4 setup script assumes Spec 3's state.json exists** and reuses its citizen accounts.

**Recommendation:** C with A as fallback. The walkthroughs/ folder convention has setup scripts that depend on prior state.json files (`AssuredIdentity`'s phase-2 setup depends on phase-1 state). Spec 4 fits that mould.

### Q6 — Where the gate lives

Architecturally, `CredentialGateComponent` is a peer of `EnrolGateComponent`. But composition order matters:

**A. Enrol gate wraps credential gate** — first sign in / pair a device, then present a credential. Linear.

**B. Credential gate wraps enrol gate** — first prove who you are, then ensure your wallet is paired. Inverted (and weird — you can't present a credential from an unpaired wallet).

**C. Sibling pattern** — both gates render and the council page composes the result.

**Recommendation:** A. EnrolGateComponent (FastPath) → CredentialGateComponent (presents) → form. Returning citizens with a paired device and a matching credential glide through both gates with two transient screens at most (sign-in + present). Cold-start citizens hit Spec 3's preflight first.

> **Decision (2026-05-15 brainstorm):** Q1=A, Q2=C, Q3=A, Q4=A, Q5=C, Q6=A. All recommendations adopted. See §10's summary table.

## §11 — Out of scope / deferred

- **Fully external cross-org presentation** — citizen presents Strathcarron credential to a non-council verifier. Architecturally supported via `Sorcha.Verifier.Engine`; not in the demo. Lands in Spec 5.
- **Per-claim disclosure toggles** — see Q1. Future hardening.
- **Server-set cookie binding for the presentation request** — see Q4. Future hardening.
- **Multi-issuer credential matching** — a presentation request that accepts any of several issuers. Out of scope; one issuer per credential per umbrella invariant #2.
- **`IIssuerKeyResolver` production path** — Spec 5 owns this. Spec 4 uses the existing demo resolver.

## §12 — Success criteria

| ID | Criterion | Verify |
|---|---|---|
| **SC-4-001** | A returning citizen (Tier 1) holding `AssuredIdentityCredential` completes Blue Badge from "click Apply" to "form ready to fill" in ≤ 45 s in 95% of attempts. | Stopwatch walkthrough × 10 |
| **SC-4-002** | The council form's required fields disclosed from the credential are pre-populated with no manual entry. | Component test + E2E |
| **SC-4-003** | A citizen WITHOUT `AssuredIdentityCredential` arriving at the Blue Badge form sees a clear error state pointing back at the driving-licence flow — no dead-end. | E2E |
| **SC-4-004** | The presentation-completion signal reaches the council page within 2 s of PWA signing in 95% of attempts. | OTel histogram on the new `IPresentationSignal` |
| **SC-4-005** | A revoked `AssuredIdentityCredential` presented against the Blue Badge gate is rejected on the server with an actionable message. | Integration test |
| **SC-4-006** | Existing F124 + F125 + F126 test suites stay green. | Standard CI |

## §13 — Open items for plan-phase

Locked once Q1-Q6 are brainstormed:

1. `CredentialGateComponent` lives in `Sorcha.UI.Components.User` (library), alongside `EnrolGateComponent`. Drop-in for any council page.
2. `Sorcha.Blueprint.Service` owns the presentation request / response endpoints.
3. `Sorcha.Verifier.Engine` is the validation layer — first non-PWA consumer.

## References

- Umbrella: `docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md`
- Spec 1 (F124) implementation: `specs/124-assured-identity-pwa/`
- Spec 2 (F125) implementation: `specs/125-sorcha-wallet-user-agent/`
- Spec 3 (F126) implementation: `specs/126-enrol-inside-wizard/`, tag `spec-126-complete`
- `Sorcha.Verifier.Engine` extraction (PR #711)
- Feature 092 (Consumer persona + `x-persona` autofill resolver)
- Feature 079 (Trust hardening — credential revocation + status lists)
- `sorcha-architecture` skill: § "Citizen Wallet PWA (Feature 114)", § "Council application enrolment gate (Feature 126)"
