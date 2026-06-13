# PWA Citizen Workflow Inbox — Design (Sub-project A)

**Date:** 2026-06-13
**Status:** Design — awaiting user review before speckit specify
**Decision owner:** Stuart Fraser
**Parent programme:** PWA workflow participation (closing the "credentials only → perform Sorcha workflows" gap)

---

## 1. Context & the parent programme

Today the Citizen Wallet PWA (`Sorcha.Wallet.Pwa`) is, in practice, credential-first: it
holds, syncs, presents and verifies credentials, and it can submit **one** workflow action if
the citizen arrives at `Pages/ApplicationInstance.razor` with a known `instanceId` (Feature
125/137). The schema-driven form renderer (`SorchaFormRenderer` + the `Controls/` library +
`FormPayloadBuilder`) is already shared via `Sorcha.UI.Components.User` and works identically in
the web app and the PWA. What's missing is everything **around** that single form: a citizen has
no way to *discover* work waiting on them, and no surface for performing flows generally.

The goal is to let a citizen (and, later, a user in their organisational role) **perform Sorcha
workflows** on their phone — gathering and entering data, including photos — not just manage
credentials. This is larger than one feature, so it is decomposed into four independently
shippable sub-projects, each with its own spec → plan → tasks → implement cycle:

| # | Sub-project | Tier | Delivers | Depends on |
|---|-------------|------|----------|------------|
| **A** | **Citizen workflow inbox (online)** | consumer | "Actions waiting on me" list + open/fill/submit via the existing renderer; live count badge | — |
| **B** | Service catalogue | consumer | New catalogue API + browse-and-start-new (`Applications.razor`) | A |
| **C** | Offline / field capture | consumer | Encrypted local drafts (IndexedDB), camera/media capture, queued deferred submit, conflict handling | A |
| **D** | Dual-tier PWA + org-role work | + platform | PWA holds a platform session (sign-in by role); surfaces org-role actions; reopens the F136 boundary for mobile | A + C |

**Agreed sequence:** A → C → D, with B sloting in any time after A. This proves the
discovery→fill→submit loop on the lowest-risk tier (A), de-risks the offline/media plumbing while
still on consumer tier (C), and tackles the risky dual-tier auth change last (D), reusing a proven
UI and offline layer. The headline use case — an org field-worker capturing evidence offline — is
the **combination of C + D** on top of A.

This document specifies **sub-project A only**.

### Relationship to the companion-first decision

The companion-first roadmap (`docs/superpowers/specs/2026-06-06-citizen-wallet-companion-roadmap.md`)
scopes the PWA to sign-in / pairing / hold / sync / present / verify, with wallet creation, signup
and recovery explicitly web-only. A is a **deliberate, consistent extension** of "present": a
citizen acting as the **data subject of their own workflows** (apply for a thing, respond to an
action whose turn is theirs) is the natural companion to holding and presenting credentials. A
stays strictly **consumer-tier**. The platform-tier expansion (org-role work) is isolated in D,
where the F136 audience boundary is addressed on its own terms.

---

## 2. Scope of A

**In scope**
- A PWA **inbox page** ("Things to do") listing the citizen's pending actions — actions a live
  instance is currently waiting on **where the citizen is the designated actor** (it is their
  turn). Source: `GET /api/actions/pending`.
- **Lifecycle grouping**: *Needs you* (real list from `/api/actions/pending`) and *In review*
  (the existing single Feature-124 pending-application notice, rendered as a lightweight banner).
- Tapping a row routes into the **existing** `ApplicationInstance` form-fill/submit flow — no
  change to the renderer, payload builder, or `/execute` submit path.
- A nav entry with a **count badge** fed by `GET /api/actions/pending/count`, refreshed on the
  existing citizen SignalR signal.
- Graceful online degradation (last-known list retained on a refresh failure).

**Explicitly out of scope (deferred to the named sub-project)**
- Browse-and-start-new / service catalogue + its new API → **B**.
- Local draft save/resume, offline list/working, camera/media capture, queued deferred submit,
  conflict handling → **C**. *(There is no draft model anywhere today; A does not introduce one.)*
- A complete "all my instances and their state" tracker (including instances where it is someone
  else's turn beyond the single Feature-124 notice) → **B/C** (would need a new consumer-tier
  read endpoint; see §7).
- Org-role / platform-tier actions, dual-tier sign-in → **D**.
- Action rejection / dispute UX, async-encryption progress → later (parity items not required to
  prove the loop; revisit per sub-project).

**No backend changes** are required for A. The endpoints it depends on already serve consumer-tier
citizen tokens.

---

## 3. Backend surface A builds on (verified)

| Surface | Route | Auth | Consumer-ready? |
|---------|-------|------|-----------------|
| List pending actions | `GET /api/actions/pending?page&pageSize` | plain `RequireAuthorization()` | ✅ resolves wallet via `platform_user_id` |
| Pending count | `GET /api/actions/pending/count` | plain `RequireAuthorization()` | ✅ |
| Load instance + blueprint | `GET /api/instances/{id}`, `GET /api/blueprints/{id}` | existing | ✅ (used by `ApplicationInstance` today) |
| Submit action | `POST /api/instances/{id}/actions/{actionId}/execute` | `RequireAuthorization()` + wallet-ownership | ✅ |
| Pending-application notice (label) | `GET /api/v1/wallet/pending-applications` | `RequireConsumerAudience` | ✅ |

**"Pending" semantics (verified in code).** `GetPendingActionsByWalletAsync`
(`EfCoreInstanceStore.cs:265`) iterates `instance.CurrentActionIds` — the actions an *Active*
instance is currently waiting on — and filters them with `IsActionForWallet`
(`EfCoreInstanceStore.cs:658`), which keeps an action only when its blueprint **`Sender`** (the
participant *designated to perform that action*) binds to the citizen's wallet, or is still
open/unbound. It explicitly excludes actions whose sender is bound to a *different* wallet (the
F142 "citizen wrongly sees the analyst's action" fix). So `/api/actions/pending` means precisely
**"current actions whose turn is mine"** — not "actions I previously sent." This is the correct
inbox semantic and requires no change.

> **Do NOT add `RequireConsumerAudience` to `/api/actions/pending`.** The web `MyActions` page
> calls the same endpoint on the **platform** tier. It is a legitimately cross-tier "any-human"
> endpoint and, per F136, correctly stays plain `RequireAuthorization()`. Tightening it would break
> the web app.

Identity resolution prefers `platform_user_id` then falls back to `sub`/`wallet_address`
(`ActionEndpoints.cs:166`, aligned with Feature 136 / #878), so a consumer token with no wallet
binding still resolves the citizen's wallet(s) and their pending actions.

---

## 4. Architecture & components

A is intentionally small: **one typed client, one page, one nav badge** — the form machinery is
already shared and unchanged.

- **`IMyActionsClient`** (new, `Sorcha.Wallet.Pwa/Services/Actions/`)
  - `Task<PendingActionsPage> GetPendingAsync(int page = 1, int pageSize = 20, CancellationToken)`
  - `Task<PendingActionsCount> GetCountAsync(CancellationToken)`
  - Thin wrapper over `GET /api/actions/pending` and `/api/actions/pending/count`; deserialises to
    the existing `PendingActionSummary` shape (InstanceId, ActionId, title, Deadline, Urgency).
  - Consumer-tier bearer token attached by the PWA's existing auth message handler.
  - Registered in PWA DI alongside the other application clients.
- **`Actions.razor`** (new page, route `actions`)
  - The inbox. Built from shared `Sorcha.UI.Components.User` primitives (no MudBlazor `ISnackbar`;
    feedback via `IInlineFeedback` per Critical Pattern #12).
  - Renders the *Needs you* list (rows: title, deadline, urgency chip) and the *In review* banner
    (existing `IPendingApplicationClient`).
  - Decision pending in plan: repurpose the current empty-stub `Applications.razor` vs. add a new
    page and leave `Applications.razor` for B's catalogue. Naming/routing resolved in tasks.
- **Nav + badge**
  - A "Things to do" entry in PWA navigation with a count from `GetCountAsync`, refreshed on the
    citizen SignalR signal and on page focus.
- **Reuse, no fork**
  - `ApplicationInstance.razor` + `IApplicationActionClient` remain the single open-action surface.
    The inbox only *navigates* to `applications/{instanceId}` (base-relative — PWA path-prefix rule).

### Unit boundaries
- `IMyActionsClient` — *what:* fetch the citizen's pending actions + count. *Depends on:* the PWA
  `HttpClient` + auth handler. *Testable* with a stub `HttpMessageHandler`.
- `Actions.razor` — *what:* present the list + banner, route to the action. *Depends on:*
  `IMyActionsClient`, `IPendingApplicationClient`, `NavigationManager`. *Testable* with bUnit and a
  stubbed client.

---

## 5. Data flow

1. **Inbox load** — `Actions.razor` → `IMyActionsClient.GetPendingAsync(page)` →
   `GET /api/actions/pending` → render `PendingActionSummary` rows, sorted urgent → normal then by
   deadline ascending. Empty → friendly empty-state. Below the list, the Feature-124 notice renders
   as an "In review" banner via `IPendingApplicationClient`.
2. **Open** — tap row → `NavigateTo($"applications/{instanceId}")` → existing `ApplicationInstance`
   loads + renders the action with `SorchaFormRenderer`.
3. **Submit** — unchanged: `IApplicationActionClient.SubmitAsync` → `/execute`. On success it
   already posts the pending-application notice; A additionally returns the user to the inbox and
   refreshes list + count.
4. **Live refresh** — subscribe to the existing citizen hub signal (the same channel that drives
   silent credential sync); on signal, re-fetch count + list. A short poll runs **only while the
   page is open** as a fallback. (Background/push notification is out of scope — see C/P2 of the
   companion roadmap.)

---

## 6. Error handling & states

- **Refresh failure / transient network error on the list** — non-blocking inline message
  ("Couldn't refresh — showing last known"); retain the last-rendered list; never a blank error
  page. (True offline list caching is C; A degrades gracefully but does not persist.)
- **Empty** — "Nothing needs you right now" empty-state, with a catalogue CTA stub (becomes live
  in B).
- **Stale item** — if an action was already completed elsewhere, opening it lands on
  `ApplicationInstance`, which already handles "no current action"; surface that via
  `IInlineFeedback`, not a snackbar.
- **Auth/token expiry** — handled by the existing PWA sign-in/session refresh; no special handling
  in A.

---

## 7. Open questions / decisions deferred

- **Complete "my instances" tracker** — A's *In review* group is the single Feature-124 notice
  only. A full "all my instances + lifecycle state (Needs you / In review / Done)" tracker needs a
  new consumer-tier read endpoint (e.g. `GET /api/.../my-instances` listing instances where I'm a
  participant with their `InstanceState`). **Decision (2026-06-13): defer** to B/C; A ships with no
  new backend.
- **Page identity** — repurpose `Applications.razor` vs. new `Actions.razor`. Resolve in tasks;
  must not collide with B's catalogue plans for `Applications.razor`.
- **`urgentCount`** — `/api/actions/pending/count` returns `urgentCount` but it is always 0 today.
  A renders total count; urgent styling rides on each row's `Urgency`. Surfacing a real urgent
  count is a later backend refinement.

---

## 8. Testing strategy

- **PWA component tests** (bUnit, `JSRuntimeMode.Loose`, per the `sorcha-ui` / `playwright` skills):
  - Inbox renders rows from a stubbed `IMyActionsClient`.
  - Empty-state renders when no pending actions.
  - Urgent-before-normal ordering, then deadline ascending.
  - Nav badge shows the count from `GetCountAsync`.
  - **Row tap navigates to `applications/{instanceId}` (base-relative)** — guards the PWA
    path-prefix regression class (the 12 broken-nav buttons of PR #698).
- **Client test** — `IMyActionsClient` maps `/api/actions/pending` JSON to `PendingActionSummary`
  via a stub `HttpMessageHandler`; count mapping likewise.
- **No new backend tests** — A makes no backend change; existing `/api/actions/pending` coverage
  stands.
- **E2E (deferred, not blocking A)** — full inbox→open→submit against n1 once a seeded citizen with
  a pending action exists.

---

## 9. File references (read-only grounding)

- Pending actions endpoint + count — `src/Services/Sorcha.Blueprint.Service/Endpoints/ActionEndpoints.cs:16` (routes), `:166` (`ResolveUserWalletAddressesAsync`, prefers `platform_user_id`).
- "My turn" semantics — `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreInstanceStore.cs:265` (`GetPendingActionsByWalletAsync`), `:658` (`IsActionForWallet`).
- Pending-action model — `src/Services/Sorcha.Blueprint.Service/Models/PendingActionSummary.cs:13`.
- Instance lifecycle states — `src/Services/Sorcha.Blueprint.Service/Models/Instance.cs:150` (`InstanceState`).
- Pending-application notice (label) — `src/Services/Sorcha.Wallet.Service/Endpoints/PendingApplicationEndpoints.cs:19`; PWA client `src/Apps/Sorcha.Wallet.Pwa/Services/IPendingApplicationClient.cs`.
- Existing open-action surface (reused unchanged) — `src/Apps/Sorcha.Wallet.Pwa/Pages/ApplicationInstance.razor`; `src/Apps/Sorcha.Wallet.Pwa/Services/Applications/IApplicationActionClient.cs:83` (`LoadFormAsync`), `:137` (`SubmitAsync`).
- Shared form renderer (reused unchanged) — `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/SorchaFormRenderer.razor`; `.../Services/User/Forms/FormPayloadBuilder.cs`.
- Stub catalogue page (B) — `src/Apps/Sorcha.Wallet.Pwa/Pages/Applications.razor`.
- Companion-first decision — `docs/superpowers/specs/2026-06-06-citizen-wallet-companion-roadmap.md`.

---

## 10. Definition of done (A)

A citizen signed into the PWA on a consumer-tier session can:
1. Open a "Things to do" inbox and see a real list of the actions currently waiting on them
   (their turn), with title, deadline and urgency.
2. See a nav count badge that updates live when a new action arrives (SignalR) and after they act.
3. Tap an action, fill it via the existing renderer (including the field controls already shared),
   and submit it through the existing `/execute` path.
4. Return to the inbox and see it refresh; see an "In review" banner while an application awaits.
5. Experience graceful degradation on a transient refresh failure (no blank error, last-known list
   retained).

All with **no backend change**, strictly **consumer-tier**, reusing the shared renderer and the
existing `ApplicationInstance` submit flow.
