# PWA Service Catalogue — Design (Sub-project B)

**Date:** 2026-06-14
**Status:** Design — ready to speckit specify/plan/tasks (implementation deferred to a focused session)
**Decision owner:** Stuart Fraser
**Parent programme:** PWA workflow participation (A/B/C/D). Depends on **A** (open/fill/submit + inbox).

---

## 1. Context & scope

Sub-project A gave the citizen an inbox of work *waiting on them* and the ability to open/fill/submit
an action they already have. B is the other half of discovery: **browse the services available and
start a new one** — e.g. "apply for a blue badge" — turning the empty-stub `Applications.razor` into
a real catalogue.

**Scope:** a citizen (consumer-tier) can browse the services they're entitled to start, tap one, and
begin a new application that drops them into the existing fill/submit flow. Out of scope: org-role
catalogues (D), offline catalogue browsing (could layer on C later), designer/admin surfaces.

### Grounding (verified read-only)

- **`Applications.razor` is an empty stub** (route `/applications`): its own comment says the
  *application catalogue API lands in a follow-up* and it currently shows an empty-state CTA pointing
  at the council web shell.
- **Starting an application already exists:** `POST /api/instances/` (`CreateInstance`,
  Blueprint Service `Program.cs:2153`, request `CreateInstanceRequest` `:4114`) creates a workflow
  instance from a published blueprint. After creation, the citizen's first action is reachable via
  the existing `ApplicationInstance` page (route `applications/{instanceId}`) that A already drives.
- **Published blueprints** live in `IPublishedBlueprintStore`; `/api/blueprints` exposes
  version/lookup endpoints, but **there is no citizen-facing "services I can start" catalogue
  endpoint** (filtered to what a consumer in their org context may initiate). This is B's one
  backend addition.

---

## 2. The one backend gap

B needs a **catalogue read endpoint** — "list the services a citizen may start" — consumer-tier,
scoped to the citizen's org/home context. Shape (to be finalised in speckit):

```
GET /api/catalogue            (or /api/services)   — Blueprint Service
  auth: consumer-tier capable (RequireAuthorization; resolves org from token)
  returns: [ { blueprintId, title, description, category?, startActionSummary? } ]
  filtered to blueprints flagged citizen-startable in the caller's context
```

Decisions for speckit: where "citizen-startable" is declared (a blueprint flag / a published-service
registry), how it's scoped to the org/home context, and whether the catalogue is curated vs. "all
published blueprints with an open first action". The simplest v1: list published blueprints whose
first action is citizen-initiable (open participant) in the caller's org context.

Everything else is front-end and reuses existing pieces.

---

## 3. Design

- **`ICatalogueClient`** (PWA): typed client over the new `GET /api/catalogue`, returning catalogue
  items. Bearer chain (consumer-tier token).
- **`Applications.razor`** (replace the stub): render the catalogue — searchable/grouped list of
  startable services with title + description; empty-state when none. Tapping an item starts it.
- **Start flow:** on tap → `POST /api/instances/` (`CreateInstance`) for the chosen blueprint →
  on success navigate to `applications/{instanceId}` (base-relative) → the **existing**
  `ApplicationInstance` (A) renders the first action to fill + submit. No new form/submit code.
- **Entry point:** A already left a catalogue CTA on the inbox empty-state; wire it to
  `/applications`. The `Cards.razor` "add" affordances also point at `/applications` — they light up
  once the catalogue is real.
- **Reuse:** `ApplicationInstance` (open/fill/submit), the shared `SorchaFormRenderer`, A's
  navigation conventions (base-relative). With C present, a started application's first action gets
  drafts/offline for free.

### Components
- New: `GET /api/catalogue` (Blueprint Service) + `ICatalogueClient` (PWA) + catalogue UI in
  `Applications.razor` + the start→CreateInstance→navigate flow.
- Reuse: `CreateInstance` endpoint, `ApplicationInstance`, form renderer.

---

## 4. Likely user stories (for speckit)

- **US1 (P1):** Browse the services I can start (catalogue list with title/description; empty-state).
- **US2 (P1):** Start a service → new instance created → land in the fill/submit flow. *(MVP loop.)*
- **US3 (P2):** Find a service (search/filter/group by category).
- **US4 (P3):** Sensible scoping/curation — only services actually startable in my context appear;
  honest messaging when the catalogue is empty.

---

## 5. Risks / decisions

- **Catalogue definition (main decision):** what makes a blueprint "citizen-startable" and how it's
  scoped to the caller's org/home context — needs a clear rule (blueprint flag vs. registry vs.
  "published + open first action"). Resolve in speckit research; keep v1 simple.
- **Backend touch:** unlike A/C, B adds one consumer-tier read endpoint. Keep it read-only and
  reuse the existing published-blueprint store; no change to instance creation.
- **Auth:** consumer-tier; do not expose blueprints the citizen can't start; resolve org/home from
  the token (no org parameter).
- **Live validation:** start a real service end-to-end (catalogue → CreateInstance → first action →
  submit) before trusting B.

---

## 6. Definition of done (B)

A citizen can open the wallet, browse the services available to them, pick one, and be taken
straight into filling and submitting its first action — turning `Applications.razor` from an
empty stub into a working "start something new" surface, on top of A's fill/submit flow and one new
consumer-tier catalogue endpoint.
