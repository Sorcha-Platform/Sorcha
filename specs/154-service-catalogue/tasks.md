# Tasks: PWA Service Catalogue

**Input**: design docs in `/specs/154-service-catalogue/`. **Tests**: INCLUDED (TDD). **Depends on A.**

## Conventions
- One consumer-tier read endpoint; start reuses CreateInstance + ApplicationInstance (no change).
- List only citizen-startable services; base-relative nav; no `ISnackbar`.

## Phase 1: Backend — catalogue endpoint
- [ ] T001 (TDD) Failing tests `tests/Sorcha.Blueprint.Service.Tests/Catalogue/CatalogueStartableTests.cs`: the startable predicate — open first-action sender ⇒ startable; hard-coded sender ⇒ not; no actions / no sender / no register ⇒ excluded.
- [ ] T002 Add `Endpoints/CatalogueEndpoints.cs`: `GET /api/catalogue` (RequireAuthorization), reads `IPublishedBlueprintStore`, maps startable published blueprints → `CatalogueItem{blueprintId,title,description,registerId}`; `IsCitizenStartable` helper; `.WithSummary/.WithDescription`. Map via `app.MapCatalogueEndpoints()` in Program.cs. Make T001 pass.
- [ ] T003 (TDD) Endpoint test: returns only startable+with-register items mapped correctly (in-memory published store).

## Phase 2: PWA — catalogue client + page (US1)
- [ ] T004 (TDD) `tests/Sorcha.Wallet.Pwa.Tests/Catalogue/CatalogueClientTests.cs`: `ICatalogueClient` maps `/api/catalogue` JSON → `CatalogueItem[]`; transient failure → empty (page shows notice).
- [ ] T005 Add `Services/Catalogue/ICatalogueClient.cs` (+ `HttpCatalogueClient`, `CatalogueItem`); a `StartAsync(item)` → `POST /api/instances/` returning the new instance id. DI register (bearer chain). Make T004 pass.
- [ ] T006 (TDD) bUnit `tests/Sorcha.Wallet.Pwa.Tests/Pages/ApplicationsCatalogueTests.cs`: lists services; empty state; tap → StartAsync called → navigates base-relative to `applications/{id}`; load-failure notice.
- [ ] T007 Replace `Pages/Applications.razor` stub with the catalogue: load via `ICatalogueClient`, render list (name+description), empty/loading/error states, tap → start → navigate. Make T006 pass.

## Phase 3: US2 search
- [ ] T008 [US2] (TDD) bUnit: typing a query narrows the list; no-match state.
- [ ] T009 [US2] Add client-side search/filter to `Applications.razor`. Make T008 pass.

## Phase 4: Polish + PR
- [ ] T010 [P] snackbar gate clean; T011 [P] coverage ≥85% new; T012 [P] docs note; T013 clean build (Blueprint Service + PWA) + suites green.
- [ ] T014 quickstart manual verification (publish a startable blueprint → browse → start → submit). Flag as pre-merge.
- [ ] T015 PR + merge-on-green.

## Order
Backend endpoint → PWA client+page (US1 MVP) → search (US2) → polish → PR.
