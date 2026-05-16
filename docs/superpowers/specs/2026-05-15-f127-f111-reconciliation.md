# F127 ↔ F111 Reconciliation

**Date:** 2026-05-15
**Status:** Research output. Awaiting operator pick on the four options in §4.
**Spec affected:** F127 / `127-credential-gated-service`
**Pre-existing feature involved:** F111 / `111-presentation-lifecycle` (shipped)

## §1 — Why this doc exists

I started F127 PR-B and discovered Feature 111 (Timebound Presentation Lifecycle) had already shipped a presentation-lifecycle subsystem that overlaps heavily with what F127's locked design treated as greenfield. F127's design assumed:

- A new `/api/blueprint/presentation-requests` endpoint to mint requests
- A new `/api/blueprint/presentation-responses` endpoint for the wallet to post signed VPs
- A new `IPresentationRequestService` + `PresentationRequest` model + `IAtomicDistributedCache` stash
- A new `prerequisites.presentationRequests` blueprint syntax

F111 already ships:

- `IPresentationLifecycleService.InitiateAsync` — mint + write `presentation-initiated` to the register
- `IPresentationLifecycleService.HandleOutcomeAsync` — accept verifier callback + write `presentation-outcome`
- `IPresentationLifecycleService.HandleAbandonmentAsync` — sweeper writes `presentation-abandoned`
- `IPresentationConsumer` — pluggable verifier (HAIP today; consumer-name dispatch already generic)
- `IPendingPresentationStore` — Redis-backed pending state with TTL + outcome sentinel for idempotency
- `IPresentationRateLimiter` — per-wallet-per-register attempt throttling
- `GET /api/presentations/{requestId}/status` — wallet-polled lifecycle state
- `POST /api/presentations/callbacks/{consumerName}/{requestId}` — verifier callback
- `CredentialRequirement` declared on actions (with `PresentationSource` naming the consumer)
- Three register transaction types: `presentation-initiated`, `presentation-outcome`, `presentation-abandoned`

The two designs are doing the same job: take a citizen's credential, verify it, gate a workflow on it. F111 ships, with rate-limiting, idempotency, abandonment sweeping, and legally-weighted register evidence. F127's locked design didn't reference F111 because the brainstorm didn't surface it.

## §2 — What F127 actually needs

Stripped of any implementation prejudice:

1. The citizen arrives at the Blue Badge council page on the Strathcarron sample portal.
2. They tap "Prove you're you."
3. Their Sorcha wallet presents an `AssuredIdentityCredential` server-side over the Sorcha API (not HAIP — the wallet holds the key locally and posts directly).
4. The disclosed claims pre-populate the council form's identity fields.
5. The citizen fills the Blue-Badge-specific fields.
6. They submit the form.
7. The blueprint runtime issues `BlueBadgeCredential` into the same wallet.

What F127 doesn't need from scratch:

- A second presentation-lifecycle implementation.
- A second "pending presentation in Redis" store.
- A second register-transaction model for presentation events.
- A second rate-limit policy.

What F127 doesn't have today and would need either way:

- A `Sorcha.Verifier.Engine` integration on the server side (verifier today is the F125 PWA wallet's reference desk only).
- A way for the council page to learn the citizen's wallet has presented (signal + claim fetch).
- A way for the citizen to fill non-credential form fields AFTER the presentation (not at submit-time, as F111 expects).
- A blueprint pattern that says "this starting action requires a wallet-presented credential."

## §3 — The structural tension

F111's flow is:

```
citizen submits action with full draftPayload
    → InitiateAsync writes presentation-initiated to register
    → wallet scans QR
    → external verifier (HAIP) verifies + posts callback
    → HandleOutcomeAsync writes presentation-outcome (with disclosed claims, encrypted)
    → action completes
```

F127's locked design wants:

```
citizen taps "Prove you're you"
    → presentation request minted
    → wallet presents (Sorcha-Verifier.Engine validates server-side)
    → disclosed claims returned to council page
    → council page prefills form
    → citizen fills remaining fields + submits
    → action runs with prefilled + filled fields
    → BlueBadgeCredential issued
```

Three structural differences:

1. **Where the verifier lives.** F111: external `IPresentationConsumer` (HAIP). F127: in-process `Sorcha.Verifier.Engine`.
2. **When the action submits.** F111: at presentation request time, with full draftPayload. F127: AFTER presentation succeeds, with autofilled-by-disclosed-claims + citizen-entered fields.
3. **Whether the gate is legally-weighted evidence.** F111: every initiate writes to the register. F127's design implied this is a UX preview that might not need a register write.

## §4 — Four reconciliations

| # | Shape | Pros | Cons |
|---|---|---|---|
| **A** | **F127 builds parallel infrastructure.** Keep the locked design as-is — new endpoints, new stash, new blueprint syntax. F111 stays for HAIP/timebound flows; F127 is its own thing. | Locked design ships unchanged. Fast to PR-B. | Two presentation-lifecycle implementations in one service — exactly the duplication the GSD framework was built to prevent. Eventual reconciliation debt. Inconsistent with the "F111 is the primitive, others reuse it" principle in F111's spec §SC-007 (verbatim: "A second, non-HAIP consumer of the lifecycle primitive can be added in a future feature without modifying the primitive itself"). |
| **B** | **F127 adopts F111 wholesale + adds a Sorcha-wallet consumer.** Drop F127's new endpoints / new syntax. New `"sorcha-wallet"` `IPresentationConsumer` in Blueprint Service that calls `Sorcha.Verifier.Engine`. F111's existing endpoints serve as the wire surface. Blueprint authors declare `credentialRequirement.presentationSource: "sorcha-wallet"` on the gated action — the existing F111 mechanism. | Honours F111's "second consumer" extension contract (SC-007). One presentation-lifecycle implementation. F111's rate-limiting + abandonment sweeper + register-transaction trail come for free. | F111's flow puts the citizen's form submission at the START of presentation, not the END. F127's "fill the form after autofill" UX needs a workflow shape change — either two chained actions (`verify-identity` → `submit-blue-badge`) or a small F111 extension that supports "initiate-with-empty-payload, then complete-with-filled-payload-on-outcome". |
| **C** | **Two-action workflow split (subset of B).** F127's blueprint becomes two chained actions: action 1 = `verify-identity` (`credentialRequirement.presentationSource: "sorcha-wallet"`, no form schema), action 2 = `submit-blue-badge-application` (form schema, predecessor = `verify-identity`). Citizen taps "Prove you're you" → action 1 fires F111 → outcome → disclosed claims on register → council page reads claims, prefills → citizen submits → action 2 runs. | Idiomatic F111. No F111 extension needed. The "verify then fill" UX maps cleanly to a workflow shape. Two register transactions per citizen journey is honest record-keeping — there really are two distinct citizen events. | Blueprint authors write two actions instead of one. Slightly more cognitive overhead in the design example. **Disclosed claims on `presentation-outcome` are encrypted per register disclosure rules** — F127 needs a way to fetch them in plaintext for the council page's autofill. That's a new authenticated endpoint (claims-fetch) or a way for the council page to subscribe to the second action's pending state. |
| **D** | **Hybrid: F127 reuses F111's plumbing internally but exposes its own gate-shaped endpoints.** F127's new endpoints delegate to `IPresentationLifecycleService.InitiateAsync` and `HandleOutcomeAsync` under the hood; the council page sees F127's gate API; F111's lifecycle is the implementation detail. Same register tx trail as B/C; same UX as A. | UX shape preserved; F127 still uses F111's plumbing. | Adds an indirection layer that's hard to justify ("why does Blueprint Service have two presentation APIs?"). When the next non-HAIP consumer appears (F127 wasn't supposed to be unique), they have to choose between F127's gate API and F111's lifecycle API — confusing precedent. |

## §5 — Recommendation: **C, with a small claims-fetch endpoint**

C is B's idiomatic subset. It honours F111's "second consumer plugs in cleanly" promise, doesn't ask F111 to bend (no extension to InitiateAsync), and the two-action workflow shape is a natural map for "citizen proves themselves, then fills out the rest of the form."

The only piece F111 doesn't ship that F127 needs is: **the council page reading the disclosed claims from `presentation-outcome` so it can autofill the second action's form.** F111 stores outcomes on the register with encryption-per-disclosure-rule; the council page needs them in plaintext (or a controlled view of them) for autofill.

This is a small addition to F111's surface: a new endpoint `GET /api/presentations/{requestId}/disclosed-claims` (or similar — bikeshed on the path) that requires a session-bound proof the caller is the council page that initiated the request. Returns the disclosed claims in plaintext. The endpoint is the only NEW work on F111's side; everything else is consumer registration + a new blueprint shape.

### Concrete delta for F127

| Locked design said | Reconciled design says |
|---|---|
| New `POST /api/blueprint/presentation-requests` | **DROP.** Use F111's existing `InitiateAsync` via the action-submission path. Council page submits action 1 (`verify-identity`) which fires F111. |
| New `POST /api/blueprint/presentation-responses` | **DROP.** Use F111's existing `POST /api/presentations/callbacks/sorcha-wallet/{requestId}` — the wallet posts the signed VP there; F111's lifecycle service dispatches to the new `SorchaWalletPresentationConsumer`. |
| New `GET /api/blueprint/presentation-responses/{nonce}` | **REPLACE** with `GET /api/presentations/{requestId}/disclosed-claims` (or whatever path F111 prefers) — a small addition on F111's existing surface. Auth-gated. |
| New `prerequisites.presentationRequests` blueprint syntax | **DROP.** Use F111's existing `credentialRequirement` field on action 1. New `PresentationSource` value: `"sorcha-wallet"`. |
| `CredentialGateComponent` in `Sorcha.UI.Components.User` | **KEEP, REFRAME.** The component still wraps the council-page UX (mint via action 1 submission, poll/SignalR for outcome, autofill from claims-fetch, then surface action 2's form). Composition shape unchanged; implementation now wraps F111's surface, not F127's discarded endpoints. |
| `IPresentationSignal` (3 s polling + SignalR + 60 s manual recovery) | **KEEP, REUSE.** F111 ships polling on `GET /api/presentations/{requestId}/status`. SignalR addition is genuinely new (F111 didn't ship a hub event). 60 s manual recovery still applies. |
| String `nonce` in IAtomicDistributedCache, 5-min TTL | **DROP.** F111's `presentationRequestId` (Guid) + F111's pending store + F111's per-blueprint validity window (default 10 min). |
| New `BlueBadgeCredential` blueprint with `prerequisites.presentationRequests` | **REWRITE** as a two-action chain: `verify-identity` (F111 credential requirement) → `submit-blue-badge-application` (form fields). |

### What stays unchanged from F127's locked design

- The Strathcarron sample portal as the consumer-side host. PR-A already shipped this.
- The `CredentialGateComponent` consumer composition (`EnrolGateComponent` → `CredentialGateComponent` → form). The component's internal wiring rewires; its API doesn't.
- The umbrella invariants (one issuer per credential, generic claim names, hybrid universal QR, email/password as anchor, PWA ↔ Web co-equal Sorcha-branded surfaces, single protagonist Sarah).
- The boundary contract — samples/ separation, CI grep gate, Spec 4 PR-A's structural extract.
- The brainstorm Q1–Q6 decisions (all-or-nothing consent, picker-suppress-when-1, SignalR + polling, PWA-side confirm dialog, walkthrough chains off Spec 3, EnrolGate wraps CredentialGate wraps form).
- The boundary doc and platform-vs-consumer rule.

### What gets new design work

- The two-action workflow shape: how does the council page UI transition from action 1's "prove you're you" state to action 2's form? Same page? Re-render? This is a minor UX question.
- The auth model for the claims-fetch endpoint: the council page is unauthenticated (per Q-T4 in F127's research). It can't carry a bearer token. The natural fit is a one-time fetch token returned from `InitiateAsync` (alongside `presentationRequestId`) that the council page presents on claims-fetch — single-use, short-TTL.
- A new `SorchaWalletPresentationConsumer` in Blueprint Service: takes a signed VP `JsonElement`, calls `Sorcha.Verifier.Engine`, returns `PresentationOutcome` with verified claims. Mirrors `HaipPresentationConsumer` shape exactly.
- A new `IPresentationLifecycleService` initiation contract for non-HAIP consumers (F111's docstring flagged this as deferred: "Non-HAIP consumers (e.g. file-upload-deadline) will land in a future phase by extending `IPresentationConsumer` with an initiation contract."). F127 is that future phase. Minor extension; F111 already designed for it.

### What shrinks substantially

- Plan.md: most of the platform-side "new endpoints / new service / new model" tasks (T028–T039) collapse into: (a) `SorchaWalletPresentationConsumer`, (b) the claims-fetch endpoint + its auth shape, (c) the `IPresentationLifecycleService` non-HAIP initiation extension, (d) the SignalR hub event, (e) the blueprint shape rewrite. Roughly 15 tasks become roughly 8.
- Tasks.md PR-B count shrinks from ~16 to ~8. PR-C content (Blue Badge blueprint, council page) unchanged in shape but the blueprint JSON's structure changes.
- Contracts/ — three F127 contract files (`presentation-requests-endpoint.md`, `presentation-responses-endpoint.md`, `prerequisites-presentation-requests.schema.json`) get replaced with: a pointer to F111's existing endpoints, a new contract for the claims-fetch endpoint, and a pointer to F111's `CredentialRequirement` schema.

## §6 — Decision request

Pick one:

- **C (recommended)** — adopt F111, add `SorchaWalletPresentationConsumer`, add claims-fetch endpoint, two-action workflow. Update F127's design / spec / plan / tasks to match.
- **B** — same as C but with an F111 extension so a single action can have "initiate + complete in two stages." Asks F111 to bend; saves blueprint authors one action.
- **A** — keep F127's locked design unchanged, build parallel infrastructure. Fastest to PR-B; accumulates reconciliation debt.
- **D** — F127 keeps its endpoint shape, delegates internally to F111. Hybrid.

If C is picked, the next steps are:

1. Update F127's spec / plan / tasks / contracts to reflect the reconciled design.
2. Open a tiny F111 supplement design note for the claims-fetch endpoint (it's a small addition to a shipped feature; needs F111's own design honesty).
3. Re-derive PR-B's task list under the reconciled shape.
4. Then start implementation.

## §7 — Files referenced

- `src/Services/Sorcha.Blueprint.Service/Endpoints/PresentationEndpoints.cs` (F111)
- `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IPresentationLifecycleService.cs` (F111)
- `src/Services/Sorcha.Blueprint.Service/Services/Implementation/PresentationLifecycleService.cs` (F111)
- `src/Services/Sorcha.Blueprint.Service/Services/Implementation/HaipPresentationConsumer.cs` (F111 precedent)
- `src/Services/Sorcha.Haip.Service/Services/HaipPresentationConsumer.cs` (F111 sibling precedent)
- `src/Common/Sorcha.PresentationLifecycle.Abstractions/IPresentationConsumer.cs` (F111)
- `src/Services/Sorcha.Blueprint.Service/Storage/Presentations/IPendingPresentationStore.cs` (F111)
- `specs/111-presentation-lifecycle/spec.md` (F111)
- `docs/superpowers/specs/2026-05-15-spec-4-credential-gated-second-service-design.md` (F127)
- `specs/127-credential-gated-service/{spec,plan,tasks,research,data-model}.md` (F127)
