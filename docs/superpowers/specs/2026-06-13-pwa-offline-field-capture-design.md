# PWA Offline / Field Capture — Design (Sub-project C)

**Date:** 2026-06-13
**Status:** Design — awaiting user review before speckit specify
**Decision owner:** Stuart Fraser
**Parent programme:** PWA workflow participation (A/B/C/D). Depends on **A** (citizen workflow inbox).

---

## 1. Context

Sub-project A gave the Citizen Wallet PWA a "Things to do" inbox: a citizen can discover the
actions waiting on them, open one, fill it via the shared `SorchaFormRenderer`, and submit — **but
only online, in one sitting, and only the action they opened (its schema is fetched on open), with
no way to attach photos end-to-end.** C makes workflow participation **field-first**: a citizen (or
a user in the field) can open any of their pending actions **with no connectivity**, fill it,
**capture photos/media**, save an **encrypted local draft**, and have the PWA **submit it when back
online** — with safe conflict handling if the world moved on.

This matches the headline use case behind the whole programme: a field worker gathering data and
photos for a workflow on their phone, offline, syncing later.

### Decisions taken in brainstorming (2026-06-13)

1. **Unified C** — offline drafts **and** photo/media capture **and** the attachment-submission
   wiring ship together (not split).
2. **Conflict handling = detect, hold, and ask** — a deferred submit that is no longer valid is
   never silently dropped; the draft is kept, marked "needs attention", and the citizen is told
   what changed with discard vs. re-open-fresh choices.
3. **Offline boundary = pre-cache all my pending actions** — when online, the PWA proactively
   caches every inbox action's form context so any pending action can be opened offline.

### Grounding (verified, read-only)

- **Reusable:** XChaCha20-Poly1305 + IndexedDB device-bound encryption (`IndexedDbCredentialCache`
  + `indexeddb-bridge.js`/`xchacha-bridge.js`); capture controls `FileRenderer` (40 MB ceiling) and
  `PortraitCaptureControl` (camera → file-picker fallback today, into in-memory `FormContext`);
  `ISyncService` as a foreground drain trigger; server-side **idempotency/replay protection** keyed
  by `(instanceId, actionId, senderWallet, lastTransactionId)` → safe retry.
- **Attachment submission already exists** (not greenfield): the Blueprint Service consumes inline
  `Files` (base64) → `BuildFileTransactionsAsync` → stores file content → returns file-transaction
  hashes referenced from the action (`Program.cs:1560`), plus the consumer-tier `/api/file-chunks`
  staging pipeline (XChaCha20, 10 × 4 MB) for large files. **It is just not on the PWA's `/execute`
  path yet** — C's attachment work is *wiring an existing mechanism to the PWA submit path*.
- **Must build (PWA):** `IDraftStore` + `drafts` store; `ISubmitQueue` + `submitQueue` store (outbox);
  pre-cache of pending-action contexts; offline/connectivity status UI; conflict surface. The dev
  service worker is a no-op and there is **no** Background Sync API today — C drains on foreground
  signals (online event / app open / `ISyncService`), not closed-app push (that stays out of scope,
  companion-roadmap P2).

---

## 2. Scope

**In scope (consumer-tier, Citizen Wallet PWA):**
- Encrypted local **drafts** of an action's form data (autosave + resume).
- **Pre-caching** every pending action's form context so it can be opened offline.
- **Queued/deferred submit** (outbox) that flushes on reconnect, with per-item status.
- **Conflict handling** (detect/hold/ask) for stale deferred submits.
- **Photo/media capture** persisted in the encrypted draft, and **attachment submission** wired to
  the existing inline-`Files`/file-chunk mechanism on the PWA submit path.
- Connectivity + offline/queued/sync **status UI**.

**Out of scope:**
- Closed-app background push / Background Sync API (companion roadmap P2).
- Catalogue / start-new (sub-project B).
- Org-role / platform-tier work (sub-project D).
- Server-side draft storage (drafts are device-local only).

---

## 3. Architecture & components

All new PWA code unless noted. Reuses A's `IMyActionsClient`, `Actions.razor`,
`ApplicationInstance`, and the shared `SorchaFormRenderer`.

- **`IDraftStore`** (new) + `drafts` IndexedDB store — encrypted (XChaCha20-Poly1305, device key,
  reusing the credential-cache pattern). Key: `instanceId:actionId`. Holds serialized form data +
  captured media (as encrypted blobs) + metadata (saved-at, status). Mirrors `ICredentialCache`.
- **`IActionContextCache`** (new) + reuse/extend a store — caches each pending action's form
  context (blueprint action schema + layout + register/sender context) so the action renders
  offline. Refreshed from the inbox when online (hooks `ISyncService` / inbox load).
- **`ISubmitQueue`** (new) + `submitQueue` IndexedDB store (outbox) — enqueues a completed
  submission (payload + attachment refs); a drainer flushes on connectivity signals; per-item state
  `Queued → Submitting → Submitted | NeedsAttention`. Idempotency key reused so a retry can't
  double-submit.
- **Attachment submission** — on submit, captured media is sent via the existing inline-`Files`
  mechanism (small) or staged through `/api/file-chunks` (large), brought to the PWA `/execute`
  path. The exact wiring (extend the execute path to honor `Files` vs. submit through the
  Files-aware endpoint) is a plan/tasks decision; both reuse `BuildFileTransactionsAsync`.
- **Connectivity + status UI** — an `IConnectivity` signal (online/offline) and inbox/draft badges
  ("Saved offline", "Queued", "Needs attention"). `Actions.razor` (A) shows draft/queue state per
  row; `ApplicationInstance` (A) loads from / saves to the draft store.
- **Conflict handling** — the submit drainer inspects the server response; a stale/idempotent-reject
  marks the queue item + draft `NeedsAttention` and records *why* (already submitted / step moved on
  / instance closed). The UI offers **discard** or **re-open fresh** (re-fetch current action).

### Unit boundaries
- `IDraftStore` — persist/list/load/delete encrypted drafts. Testable via the IndexedDB JS-interop
  seam (mock the bridge).
- `ISubmitQueue` — enqueue/list/drain with injected clock + submit delegate. Testable with a stub
  submit function and an in-memory store.
- `IActionContextCache` — cache/get/refresh action contexts. Testable with a stub `IMyActionsClient`
  + action loader.
- Conflict classifier — pure function: server outcome → `{ Submitted | Stale(reason) | Retry }`.

---

## 4. User stories (priority order)

- **US1 (P1) — Resume & submit an offline draft.** Open a cached action offline, fill it, autosave
  an encrypted draft, resume it later, and submit when back online. *MVP of the offline loop.*
- **US2 (P1, foundational) — Pre-cache pending actions for offline open.** When online, cache every
  inbox action's form context so any pending action opens offline. (Foundational for US1's "open
  offline".)
- **US3 (P2) — Queued/deferred submit.** Submissions made offline are queued and auto-flushed on
  reconnect with visible per-item status.
- **US4 (P2) — Conflict handling.** A stale deferred submit is detected, held, and explained
  (discard / re-open-fresh) — no silent loss.
- **US5 (P3) — Photo/media capture + attachment submission.** Capture photos offline, persist them
  in the encrypted draft, and submit them via the existing Files/file-chunk mechanism on the PWA
  path. (The one backend-touching slice — isolated last.)

**Sequence:** US2 + US1 → US3 → US4 → US5.

---

## 5. Data & lifecycle

- **Draft**: `{ instanceId, actionId, formData, media[], savedAt, status }`, encrypted at rest.
  Status: `Editing → ReadyToSubmit → Queued → Submitted | NeedsAttention`.
- **Cached action context**: `{ instanceId, actionId, blueprintActionSchema, layout, registerId,
  senderWallet, cachedAt }`. Refreshed when online; staleness feeds conflict handling.
- **Queue item**: `{ id, instanceId, actionId, payload, attachmentRefs, idempotencyKey, state,
  attempts, lastError }`.
- **Device-bound encryption**: same key source as the credential cache. **Consequence:** drafts are
  lost if the device is lost/unpaired — acceptable and consistent with the credential cache (no
  server copy of in-progress drafts).

---

## 6. Error handling & edge cases

- **Offline open of an un-cached action** — if pre-cache hasn't run for an action, show a clear
  "available when you're back online" state rather than a broken form.
- **Draft store / encryption failure** — fail safe: never lose the in-memory form silently; surface
  a notice; don't crash the form.
- **Queue drain partial failure** — per-item; one item's failure doesn't block others; transient
  errors retry with backoff, stale → `NeedsAttention`.
- **Media too large** — respect the existing 40 MB ceiling / chunk limits; warn at capture, not at
  submit.
- **Duplicate submit** — server idempotency makes a double-flush safe (same key → same tx).

---

## 7. Testing strategy

- **`IDraftStore`** — save/load/round-trip (encrypted), list, delete; via the IndexedDB bridge seam.
- **`ISubmitQueue`** — enqueue, drain success, drain transient-retry, drain stale→NeedsAttention,
  ordering, idempotency-key reuse; stub submit delegate.
- **Conflict classifier** — table-driven: server outcomes → Submitted / Stale(reason) / Retry.
- **`IActionContextCache`** — cache + offline-get + refresh.
- **bUnit** — `ApplicationInstance` loads from / saves to draft; offline open of cached vs un-cached
  action; draft/queue/needs-attention badges on `Actions.razor`; connectivity-driven status.
- **US5** — capture → draft persistence → attachment-ref submission path; the backend wiring gets a
  Blueprint Service test if `/execute` is extended to honor `Files`.

---

## 8. Risks

- **Attachment wiring is the only backend-touching part** — isolate in US5 so US1-US4 stay
  pure-PWA. The mechanism exists (`BuildFileTransactionsAsync` / file-chunks); the work is routing
  the PWA submit through it.
- **Pre-cache freshness** — a cached schema can drift from the server; conflict handling (US4) is
  the backstop, and the cache refreshes whenever online.
- **No Background Sync** — drains are foreground-only; a citizen must open the app to flush the
  queue. Closed-app push is explicitly out of scope (P2).
- **Device-bound drafts** — lost on device loss; acceptable, matches the credential cache; messaged
  honestly.

---

## 9. Definition of done (C)

A citizen can, **with no connectivity**: open any of their pending actions, fill it, capture
photos, and save it; reopen and resume it; and when back online have it **submit automatically**
with visible status — and if it can no longer be applied, be **told why and offered discard or
re-open-fresh**, never losing their captured work silently. All consumer-tier; offline drafts +
queue + pre-cache are pure-PWA; only the attachment-submission slice (US5) touches the backend, by
reusing the existing Files/file-chunk mechanism.
