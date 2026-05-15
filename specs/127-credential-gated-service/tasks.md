---

description: "Task list for F127 — credential-gated second council service (Blue Badge)"
---

# Tasks: Credential-gated second council service (Blue Badge)

**Input**: Design documents from `/specs/127-credential-gated-service/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are included throughout. The design (§9) and the constitution (§IV — ≥85% coverage on new code) make tests load-bearing; plan.md names xUnit, bunit, and Playwright as the canonical stacks.

**Organization**: Tasks are grouped by user story. The spec's two P1 stories (US1 — returning Tier 1 citizen, US2 — consumer council page in `samples/`) are co-equal in priority, but architectural sequencing demands US2 lands first (the structural `samples/` extract is the prerequisite for everything else). This aligns with the plan's sub-PR sequence: **PR-A → PR-B → PR-C → PR-D**.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps task to spec.md user story (US1, US2, US3, US4)

## Path Conventions

This is a web/multi-service project. Repo root contains `src/`, `samples/`, `tests/`, `walkthroughs/`, `scripts/`, `docs/`, `.specify/`, `.claude/`, `specs/`. Paths below are absolute from repo root.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Capture baseline before any change; reserve infrastructure slots.

- [ ] T001 Capture baseline test counts for the five projects Spec 4 touches and record in the PR-A draft body: `dotnet test src/Services/Sorcha.Blueprint.Service.Tests`, `dotnet test src/Apps/Sorcha.UI/Sorcha.UI.Components.User.Tests`, `dotnet test src/Apps/Sorcha.UI/Sorcha.Wallet.Pwa.Tests`, `dotnet test src/Services/Sorcha.Verifier.Tests`, `dotnet test src/Services/Sorcha.Tenant.Service.Tests`. SC-006 verification will compare against these.
- [ ] T002 [P] Reserve port 5300 for `strathcarron-portal` in `docs/getting-started/PORT-CONFIGURATION.md` — add the new entry alongside the existing service ports.
- [ ] T003 [P] Reserve env-var key `STRATHCARRON_PORTAL_URL` in the operator-facing docs so PowerShell walkthroughs can override the default `http://localhost:5300/` if needed.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Top-level `samples/` directory + the CI grep gate that enforces the boundary rule. These MUST land before any user-story tasks because all sample work depends on them.

**⚠️ CRITICAL**: No user story work can begin until Phase 2 is complete.

- [ ] T004 Create top-level `samples/` directory and write `samples/README.md` explaining the boundary contract: "Code in `samples/` is application-specific; no `ProjectReference` into `src/Apps/Sorcha.UI/` other than `Sorcha.UI.Components.User`. CI grep gate enforces. See `docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md`."
- [ ] T005 Create `scripts/check-samples-references.ps1` — PowerShell script that globs `samples/**/*.csproj`, parses each for `<ProjectReference>` entries, fails with a clear message if any reference points into `src/Apps/Sorcha.UI/` and is not `Sorcha.UI.Components.User`. Exit code 1 on violation, 0 on clean.
- [ ] T006 Wire `scripts/check-samples-references.ps1` into the existing GitHub Actions CI workflow (`.github/workflows/ci.yml` or whichever file currently runs `dotnet test`) — runs alongside the build step; build fails if the gate fails.
- [ ] T007 Verify `Sorcha.AtomicCache` `ProjectReference` is present in `src/Services/Sorcha.Blueprint.Service/Sorcha.Blueprint.Service.csproj` (F126 lesson — `Sorcha.AtomicCache` is NOT transitive through `Sorcha.ServiceDefaults`). Add if missing.

**Checkpoint**: `samples/` exists; CI fails on a forbidden reference; the Blueprint Service can resolve `IAtomicDistributedCache`. Story implementation can begin.

---

## Phase 3: User Story 2 — Consumer council page in `samples/` (Priority: P1 — architectural sequencing first) 🎯 MVP-PR-A

**Goal**: The Strathcarron council site is a separate deployable. PR-A creates `samples/strathcarron-portal/`, moves the F126 driving-licence page out of `Sorcha.UI.Web.Client`, and proves the F126 cold-start walkthrough still works end-to-end against the new sample.

**Independent Test**: After PR-A, the F126 walkthrough (cold-start journey) runs end-to-end against `http://localhost:5300/services/driving-licence`. The old route in `Sorcha.UI.Web.Client` no longer responds. CI grep gate fails the build when a deliberate forbidden reference is added.

### Sample project skeleton

- [ ] T008 [P] [US2] Create `samples/strathcarron-portal/Sorcha.Sample.StrathcarronPortal.csproj` — Blazor WASM, `net10.0`, nullable enabled, no warnings as errors. `ProjectReference` entries: `Sorcha.UI.Components.User`, `Sorcha.ServiceClients`, `Sorcha.CitizenWallet.Abstractions` (for shared models). NO other references into `src/Apps/Sorcha.UI/`.
- [ ] T009 [P] [US2] Create `samples/strathcarron-portal/Program.cs` — Blazor WASM host setup: `WebAssemblyHostBuilder.CreateDefault`, `AddSorchaServiceClients(builder.Configuration)`, `AddHttpClient<ITierProbeService, HttpTierProbeService>(…)`, `AddHttpClient<IEnrolPairingSignal, EnrolPairingSignal>(…)`, base address pointed at `builder.HostEnvironment.BaseAddress`. License header per CLAUDE.md §7.
- [ ] T010 [P] [US2] Create `samples/strathcarron-portal/App.razor` — standard Blazor `<Router>` with `RouteView` using `CouncilLayout`.
- [ ] T011 [P] [US2] Create `samples/strathcarron-portal/wwwroot/index.html` — standard Blazor WASM template, page title "Strathcarron Council", link to `css/council.css`, viewport meta for mobile.
- [ ] T012 [P] [US2] Create `samples/strathcarron-portal/wwwroot/css/council.css` — distinct council styling: serif header font, navy primary colour (`#1F3A5F` — not MudBlazor's purple), pale background, accessible link colour, simple grid for service cards.
- [ ] T013 [P] [US2] Create `samples/strathcarron-portal/wwwroot/images/strathcarron-logotype.svg` — placeholder council mark (simple shield with "SC" monogram + "STRATHCARRON COUNCIL" wordmark).

### Council chrome (PR-A baseline IA)

- [ ] T014 [P] [US2] Create `samples/strathcarron-portal/Layout/CouncilLayout.razor` — `LayoutComponentBase`, renders `<CouncilHeader /> <main>@Body</main> <CouncilFooter />`.
- [ ] T015 [P] [US2] Create `samples/strathcarron-portal/Layout/CouncilHeader.razor` — logotype on the left, wordmark, primary nav: Services / About / Contact us / My account. Plain `<nav>` markup, no MudBlazor.
- [ ] T016 [P] [US2] Create `samples/strathcarron-portal/Layout/CouncilFooter.razor` — postal address ("Strathcarron Council, Main Street, Strathcarron"), accessibility statement link, privacy notice link, copyright line.

### Services landing page

- [ ] T017 [P] [US2] Create `samples/strathcarron-portal/Pages/Index.razor` — `@page "/"`, "Strathcarron Council — services" page heading, two service cards in a CSS grid: **Driving Licence** (live link to `/services/driving-licence`) and **Blue Badge** (rendered as a "coming soon" placeholder card with disabled affordance; PR-C activates it).

### Move the F126 driving-licence page

- [ ] T018 [US2] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/CouncilApplicationDrivingLicence.razor samples/strathcarron-portal/Pages/DrivingLicence.razor`. Update `@page` to `"/services/driving-licence"`. Update `@layout` to `CouncilLayout`. Remove the `using Sorcha.UI.Web.Client.Components.Layout` line. Update the header to remove the F126 "stand-in" doc-comment (the new home is no longer a stand-in).
- [ ] T019 [US2] Extract the form body of `samples/strathcarron-portal/Pages/DrivingLicence.razor` into `samples/strathcarron-portal/Components/DrivingLicenceForm.razor` so PR-C's `BlueBadge.razor` can mirror the shape. Preserve every `data-testid` attribute exactly (F126 Playwright tests depend on `driving-licence-submit`).
- [ ] T020 [P] [US2] Create `samples/strathcarron-portal/Components/DrivingLicenceForm.razor` (target of T019's extract) — `@bind-Value`-driven form with full-name, date-of-birth, address fields and the submit button.

### Container + compose

- [ ] T021 [P] [US2] Create `samples/strathcarron-portal/Dockerfile` — multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` build stage runs `dotnet publish -c Release -o /app/publish`; `nginx:alpine` final stage copies `/app/publish/wwwroot/` to `/usr/share/nginx/html/` and uses default nginx config (port 80).
- [ ] T022 [US2] Add `strathcarron-portal` service to `docker-compose.yml`: `build.context: ./samples/strathcarron-portal/`, `ports: ["5300:80"]`, `depends_on: [gateway]`, `networks: [sorcha]`. Verify no port clash via `docs/getting-started/PORT-CONFIGURATION.md`.

### Update F126 references after the move

- [ ] T023 [US2] Update `walkthroughs/Strathcarron/setup-cold-start-demo.ps1` — `state.json.councilPage` now points at `http://localhost:5300/services/driving-licence`. Update the operator-facing `Write-WtInfo` messages to match.
- [ ] T024 [US2] Update F126 Playwright nav-coverage suite (search `tests/` for tests that reference `/strathcarron/services/driving-licence` or `/app/strathcarron/services/driving-licence`) — switch to the new URL. Verify `data-testid="driving-licence-submit"` still resolves.
- [ ] T025 [US2] Update doc propagation for the move: `.claude/skills/sorcha-architecture/SKILL.md` "Council application enrolment gate (Feature 126)" section, `.specify/MASTER-TASKS.md` if it references the old URL, `MEMORY.md` "n1 SPA path quirk" bullet (note that the quirk now applies only to surfaces inside Sorcha.UI.Web.Client; council pages no longer live there).

### PR-A validation

- [ ] T026 [US2] Bring up the local Docker stack and walk the F126 cold-start journey end-to-end against `http://localhost:5300/services/driving-licence`. Confirm: preflight signup → wallet-pairing QR → PWA redeem + confirm dialog → device pairing → council page advances → form → "watch your wallet". This is the SC-006 gate.
- [ ] T027 [P] [US2] Verify `scripts/check-samples-references.ps1` rejects a deliberate violation: temporarily add `<ProjectReference Include="..\..\src\Apps\Sorcha.UI\Sorcha.UI.Core\Sorcha.UI.Core.csproj" />` to the sample csproj, run the gate, assert exit 1 with a clear message naming the forbidden reference, revert.

**Checkpoint**: PR-A is shippable. The F126 cold-start walkthrough works against the new sample. The CI grep gate is live. No new functionality yet — this is the structural prerequisite.

---

## Phase 4: User Story 1 — Returning Tier 1 happy path (Priority: P1) 🎯 MVP-PR-B-and-C

**Goal**: Sarah returns to Strathcarron, presents her `AssuredIdentityCredential` against the Blue Badge gate, the form is autofilled, she submits, the `BlueBadgeCredential` lands in her wallet.

**Independent Test**: After PR-B + PR-C land, run `walkthroughs/Strathcarron/setup-blue-badge-demo.ps1`, sign in as the returning citizen at `http://localhost:5300/services/blue-badge`, walk the journey. End-to-end in under 45 seconds, new credential in the wallet within ~5 s of submit.

### Platform-side data models (PR-B)

- [ ] T028 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service/Models/PrerequisitePresentationRequest.cs` — record `(string Id, string CredentialType, IReadOnlyList<string> IssuerAllowlist, IReadOnlyList<string> RequiredClaims)` matching the JSON schema in `contracts/prerequisites-presentation-requests.schema.json`.
- [ ] T029 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service/Models/PresentationRequest.cs` — record `(string Nonce, string RequestUri, string QrUrl, string TapUrl, string GateId, Guid BlueprintId, DateTimeOffset ExpiresAt)`.
- [ ] T030 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service/Models/PresentationResponse.cs` — request record `(string Nonce, string SignedVp)`.
- [ ] T031 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service/Models/DisclosedClaims.cs` — record `(IReadOnlyDictionary<string, JsonElement> Claims, string SubjectDisplayName, string HolderDid, PresentationTrustStatus TrustStatus)` with `PresentationTrustStatus` enum `{ Valid, Revoked, IssuerNotTrusted, SignatureInvalid }`.

### Blueprint schema validation

- [ ] T032 [US1] Wire `contracts/prerequisites-presentation-requests.schema.json` into the FluentValidation pipeline at blueprint publish — extend the existing schema-chain in `Sorcha.Blueprint.Service` so a blueprint declaring a malformed `prerequisites.presentationRequests` is rejected at publish-time with a structured error.

### Blueprint runtime + service

- [ ] T033 [US1] Extend `src/Services/Sorcha.Blueprint.Service/BlueprintRuntime/PrerequisitesResolver.cs` (create if absent) — `ResolvePrerequisitesAsync(Action action) → IReadOnlyList<PrerequisitePresentationRequest>`. Returns empty when no `prerequisites.presentationRequests` block exists. Throws structured validation error on malformed declaration.
- [ ] T034 [US1] Create `src/Services/Sorcha.Blueprint.Service/Services/IPresentationRequestService.cs` + `PresentationRequestService.cs` implementing: `MintAsync(blueprintId, startingActionId, gateId) → PresentationRequest`, `ValidateAndStashResponseAsync(nonce, signedVp) → DisclosedClaims`, `TryGetResponseAsync(nonce) → DisclosedClaims?`. Mint stashes the request in `IAtomicDistributedCache` with TTL 5 min; Validate calls `Sorcha.Verifier.Engine`, removes the original request entry on success (single-use via NonceStore pattern), stashes claims keyed by nonce with TTL 10 min.

### SignalR hub event

- [ ] T035 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service/Hubs/BlueprintHubGroups.cs` if absent (or extend) — add `public static string PresentationNonce(string nonce) => $"presentation:{nonce}";`.
- [ ] T036 [US1] Extend `src/Services/Sorcha.Blueprint.Service/Hubs/IBlueprintHubClient.cs` (or wherever the typed-client interface lives) — add `Task PresentationReceived(string nonce);` with XML doc `<see cref="PresentationEndpoints.GetPresentationResponseAsync"/>` per Feature 118 thin-signal convention.
- [ ] T037 [US1] Wire `PresentationRequestService.ValidateAndStashResponseAsync` to publish `PresentationReceived(nonce)` to `BlueprintHubGroups.PresentationNonce(nonce)` on validation success.

### Endpoints

- [ ] T038 [US1] Create `src/Services/Sorcha.Blueprint.Service/Endpoints/PresentationEndpoints.cs` exposing three routes per `contracts/presentation-requests-endpoint.md` + `contracts/presentation-responses-endpoint.md`: `POST /api/blueprint/presentation-requests`, `POST /api/blueprint/presentation-responses`, `GET /api/blueprint/presentation-responses/{nonce}`. Each route: `.WithName(...)`, `.WithSummary(...)`, `.WithDescription(...)`, `.RequireRateLimiting(RateLimitPolicies.Api)`, structured Problem+JSON error responses. POST `/presentation-responses` requires user-token authentication; other two are public.
- [ ] T039 [US1] Wire `PresentationEndpoints` into `Sorcha.Blueprint.Service/Program.cs` — DI for `IPresentationRequestService` + `PresentationRequestService`; `app.MapPresentationEndpoints()`; `IStorageRegistrationLog.RegisterInMemory` or `RegisterPersistent` for `IPresentationRequestService` per Feature 113 convention (it's a Redis-backed cache; mirror how F126's `IEnrolSessionService` registered).

### Platform-side tests

- [ ] T040 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service.Tests/Services/PresentationRequestServiceTests.cs` — Mint creates+stashes; Validate succeeds on a well-formed VP; Validate rejects `signature-invalid` / `claims-missing` / `credential-revoked` / `issuer-not-trusted`; single-use enforcement (second Validate against the same nonce returns `nonce-not-found`).
- [ ] T041 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service.Tests/BlueprintRuntime/PrerequisitesResolverTests.cs` — well-formed surfaces; malformed (missing required field, bad DID, empty arrays) throws structured error.
- [ ] T042 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service.Tests/Endpoints/PresentationEndpointsTests.cs` (WebApplicationFactory) — happy path on all three endpoints; error shapes; rate-limit enforced; auth required on POST `/presentation-responses`.
- [ ] T043 [P] [US1] Create hub publish integration test — assert `PresentationReceived(nonce)` reaches a test client subscribed to `BlueprintHubGroups.PresentationNonce(nonce)` after a successful Validate, and is NOT delivered to clients on other groups.

### Library-side: `IPresentationSignal` + `BlueprintHubConnection` extension

- [ ] T044 [P] [US1] Extend `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/Hubs/BlueprintHubConnection.cs` (or wherever the hub connection is owned) — add `event Func<string, Task>? OnPresentationReceived;` and wire it to the hub's `PresentationReceived` typed-client method.
- [ ] T045 [P] [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/IPresentationSignal.cs` — interface with `Task WaitForAsync(string nonce, CancellationToken ct)` and `event Func<Task>? OnManualRecoveryAvailable;` (60 s ceiling per FR-021).
- [ ] T046 [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/PresentationSignal.cs` — composes `BlueprintHubConnection.OnPresentationReceived` (primary) with a 3 s polling fallback against `GET /api/blueprint/presentation-responses/{nonce}`. Uses `TimeProvider.CreateTimer` for testability (not `Task.Delay`). Surfaces `OnManualRecoveryAvailable` after 60 s of no signal.
- [ ] T047 [US1] Register `IPresentationSignal` in `samples/strathcarron-portal/Program.cs` (via the standard `AddHttpClient<IPresentationSignal, PresentationSignal>(…)` pattern that mirrors F126's `EnrolPairingSignal` registration).

### `CredentialGateComponent`

- [ ] T048 [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/CredentialGate/CredentialGateComponent.razor` — parameters: `string BlueprintId`, `string StartingActionId`, `string GateId`, `EventCallback<DisclosedClaims> OnPresented`, `string? LinkBackUrl` (for US3), `string? NameOfMissingCredentialType` (for US3), `RenderFragment? ChildContent`. On init: mints a presentation request via `Sorcha.ServiceClients.Blueprint`, renders `HybridQrAffordance` (Layout=Auto), subscribes to `IPresentationSignal.WaitForAsync`. On signal: fetches claims via the service client, fires `OnPresented`, then renders `ChildContent`. If no `prerequisites.presentationRequests` declared on the action (resolver returns empty), renders `ChildContent` directly without minting.
- [ ] T049 [US1] Register `CredentialGateComponent`-required services in `samples/strathcarron-portal/Program.cs` (whatever's not already added via `IPresentationSignal` registration in T047).

### Library-side tests

- [ ] T050 [P] [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User.Tests/Components/CredentialGate/CredentialGateComponentTests.cs` (bunit) — renders QR on init when gate present; fires `OnPresented` with the disclosed claims on signal; renders `ChildContent` directly when no gate; expires-and-regenerate path; manual-recovery path.
- [ ] T051 [P] [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User.Tests/Services/User/Presentation/PresentationSignalTests.cs` — `FakeTimeProvider`-driven: hub-fast-path (signal arrives within 2 s), polling-path (hub fails to connect, polling resolves at 3 s tick), manual-recovery-path (no signal after 60 s fires `OnManualRecoveryAvailable`).

### Blue Badge content (PR-C)

- [ ] T052 [P] [US1] Create `walkthroughs/Strathcarron/blueprints/strathcarron-blue-badge.json` — two actions: `submit-blue-badge-application` (starting action; `prerequisites.presentationRequests` declares the `assured-identity-check` gate against `AssuredIdentityCredential` issued by `did:sorcha:org:strathcarron-council` requiring `givenName`/`familyName`/`dateOfBirth`/`homeAddress`; schema requires `mobilityCondition`, optional `previousBadgeNumber`; `x-persona.presentation: "assured-identity-check"`) and `issue-blue-badge` (issues `BlueBadgeCredential` to the citizen's wallet via `SorchaLocalWallet`). `instanceReference` configured per CLAUDE.md §6.
- [ ] T053 [P] [US1] Create `samples/strathcarron-portal/Components/BlueBadgeForm.razor` — parameters: `Dictionary<string, JsonElement> Disclosed`. Renders the four identity fields as read-only chips at the top ("Verified ✓ — {SubjectDisplayName}"), the two Blue Badge-specific fields (`mobilityCondition` required text, `previousBadgeNumber` optional text), submit button bound to a callback. Reading-age compliant copy; data-testids `blue-badge-mobility`, `blue-badge-prev`, `blue-badge-submit`.
- [ ] T054 [US1] Create `samples/strathcarron-portal/Pages/BlueBadge.razor` — `@page "/services/blue-badge"`, `@layout CouncilLayout`. Composition: `EnrolGateComponent` (CouncilName="Strathcarron Council", ServiceLabel="Blue Badge application") → `CredentialGateComponent` (BlueprintId from config, StartingActionId="submit-blue-badge-application", GateId="assured-identity-check", LinkBackUrl="/services/driving-licence", NameOfMissingCredentialType="Assured Identity") → `BlueBadgeForm`. On submit, calls `Sorcha.ServiceClients.Blueprint.StartInstanceAsync` with the disclosed claims + the Blue-Badge-specific fields. Renders "Your application is in. Watch your wallet…" success copy on success.
- [ ] T055 [US1] Update `samples/strathcarron-portal/Pages/Index.razor` — Blue Badge card switches from "coming soon" placeholder to live link at `/services/blue-badge`.

### Walkthrough seed

- [ ] T056 [US1] Create `walkthroughs/Strathcarron/setup-blue-badge-demo.ps1` — reads `state.json` from Spec 3, fails fast if absent. Publishes `strathcarron-blue-badge.json` against the existing register from Spec 3. Confirms the returning-Tier-1 citizen (`returning-*@example.test`) holds an `AssuredIdentityCredential` (queries the register); fails fast if not. Writes `blueBadgeBlueprintId` back into `state.json`. Prints walk URLs.
- [ ] T057 [US1] Update `walkthroughs/Strathcarron/setup-cold-start-demo.ps1` — automate the returning-Tier-1 device pairing AND `AssuredIdentityCredential` issuance step (currently operator-driven per F126 deferral note). Spec 4 needs the credential to actually exist for the demo to land. Document the new flow in the script header.

### PR-C validation

- [ ] T058 [US1] Walk the returning-Tier-1 journey end-to-end (Walk 1 from quickstart.md). Stopwatch ≤ 45 s in 95% of attempts (SC-001 / FR-010). Verify autofill (SC-002), 2 s signal latency (SC-004), no first-credential takeover, new credential lands in wallet within ~5 s.

**Checkpoint**: PR-B + PR-C shippable. US1 is independently demoable end-to-end.

---

## Phase 5: User Story 3 — No-credential cold-start citizen routed back (Priority: P2)

**Goal**: A citizen lacking the gating credential sees a clear, dead-end-free error state pointing at the driving-licence flow.

**Independent Test**: Provision a Tier 2 mini-gate citizen (no `AssuredIdentityCredential`), pair a device, browse to `/services/blue-badge`, confirm error state + link back to `/services/driving-licence`.

- [ ] T059 [P] [US3] Extend `CredentialGateComponent` — add empty-picker handling. When the wallet's picker returns 0 matching credentials (signalled via a new no-credential SignalR event OR a polling fallback that the wallet posts an empty-presentation acknowledgement to a new endpoint), render the error state: `<h3>We need a {NameOfMissingCredentialType} credential from Strathcarron Council to continue.</h3>` + "If you don't have one yet, apply for a driving licence first" + `<a href="@LinkBackUrl">…</a>`. (Tactical decision deferred to T060: how does the council page learn the picker came back empty? Options: PWA posts an "empty" presentation-response variant; PWA posts to a new no-credential endpoint; council page times out and switches to the error state proactively. Pick at task execution time; document the choice in `data-model.md`.)
- [ ] T060 [US3] Pick the no-credential signalling mechanism (see T059 note) and implement on the PWA side — `Sorcha.Wallet.Pwa/Pages/Present.razor` (or equivalent) detects empty picker, surfaces an in-wallet "no matching credential" screen, AND signals the council page via the chosen mechanism so the council page's error state can fire without a 60-s wait.
- [ ] T061 [P] [US3] bunit test — `CredentialGateComponent` renders the error state with the named credential type and the link when the empty-picker signal fires.
- [ ] T062 [P] [US3] Wallet bunit test — empty-picker UX renders correctly and emits the council-page signal.
- [ ] T063 [US3] Update `setup-blue-badge-demo.ps1` — ensure the Tier 2 mini-gate citizen (`mini-gate-*@example.test`) is paired with a device but has NO `AssuredIdentityCredential` (state preserved from Spec 3).
- [ ] T064 [US3] Walk the no-credential journey end-to-end (Walk 2 from quickstart.md). Verify SC-003 — clear error state, link back to driving-licence flow, no dead end.

**Checkpoint**: US3 independently testable.

---

## Phase 6: User Story 4 — Multi-credential picker (Priority: P3)

**Goal**: When the wallet holds more than one matching credential, the picker surfaces all of them, sorted newest-first. When it holds exactly one, the picker is suppressed and only the consent sheet renders.

**Independent Test**: Provision a citizen with two `AssuredIdentityCredential`s; walk the journey; confirm the picker renders both with a forced selection. Repeat with a one-credential citizen; confirm the picker is hidden.

- [ ] T065 [US4] Audit F125's wallet picker (`Sorcha.Wallet.Pwa/Pages/Present.razor` or equivalent) — confirm the existing behaviour at credential counts 0 / 1 / ≥2. Adjust to match design Q2 decision: hide entirely when count == 1, force-select (no default) when count ≥ 2.
- [ ] T066 [P] [US4] bunit test — picker hidden, consent sheet rendered directly when matching count == 1.
- [ ] T067 [P] [US4] bunit test — picker rendered with newest-first ordering, no default selection, when matching count ≥ 2.
- [ ] T068 [US4] Provision a multi-credential test citizen — extend `setup-blue-badge-demo.ps1` with an optional `-IssueDuplicate` switch that issues a second `AssuredIdentityCredential` to the returning citizen.
- [ ] T069 [US4] Walk the multi-credential journey end-to-end. Verify SC-001 still holds (the picker adds one tap to the path; total still under 45 s).

**Checkpoint**: All four user stories independently testable.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T070 [P] Add OTel histogram `blueprint.presentation_signal.latency_ms` to the publish path of `PresentationRequestService.ValidateAndStashResponseAsync` — measures wall-clock from POST receipt to `PresentationReceived` hub dispatch (primary SC-004 verification).
- [ ] T071 [P] Structured-logging audit on `PresentationRequestService` + `PresentationEndpoints` — every log call uses scope properties (`{Nonce}`, `{BlueprintId}`, `{GateId}`, `{TrustStatus}`); no `string.Format` or interpolation in log messages (constitution §VIII).
- [ ] T072 [P] Create `tests/Sorcha.E2E/Demo/BlueBadgeSecondServiceDemo.cs` — Playwright test, marked `[Demo("blue-badge-second-service")]`. Runs Walk 1 happy path end-to-end against the local docker-compose stack. Assertions: presence of "Verified ✓" pre-population, "Watch your wallet" success copy, BlueBadgeCredential appears in PWA's home-row stack within 10 s of submit.
- [ ] T073 [P] Doc propagation — `.claude/skills/sorcha-architecture/SKILL.md`: new "Credential Gates (Feature 127)" section with endpoint table + composition example + `prerequisites.presentationRequests` schema reference.
- [ ] T074 [P] Doc propagation — `docs/reference/API-DOCUMENTATION.md`: three new endpoints documented with examples.
- [ ] T075 [P] Doc propagation — `docs/reference/development-status.md`: F127 row added; status flips to "shipped" at PR-D merge.
- [ ] T076 [P] Doc propagation — `CLAUDE.md` "Feature API References" section: F127 appended to the list, pointing at the sorcha-architecture skill.
- [ ] T077 [P] Doc propagation — `.specify/MASTER-TASKS.md`: F127 task tracking row.
- [ ] T078 [P] Mark `specs/127-credential-gated-service/checklists/requirements.md` validation iteration complete with final pass timestamp.
- [ ] T079 Final regression — run `dotnet test` across all five projects from T001 (Blueprint.Service, UI.Components.User, Wallet.Pwa, Verifier, Tenant.Service). Compare counts to baseline. Add the delta (e.g., "Blueprint.Service 142/142, +X new") to the PR-D body. SC-006 gate.
- [ ] T080 Run all four quickstart walks against the deployed local stack as a final operator sign-off. Record any deviations; if SC-001 / SC-004 stopwatch targets fail, defer the failing case to a follow-up and document in the PR body.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies. T001–T003 can run in parallel.
- **Foundational (Phase 2)**: Depends on Phase 1. T004 must complete before T005; T005 must complete before T006; T007 is independent of T004–T006. **BLOCKS all user stories.**
- **US2 (Phase 3 — PR-A)**: Depends on Phase 2. **Must complete before US1**: PR-B/C tasks (US1) put new files into `samples/strathcarron-portal/` which doesn't exist until PR-A lands.
- **US1 (Phase 4 — PR-B + PR-C)**: Depends on Phase 3 (sample exists) and Phase 2 (CI gate live). Platform-side (T028–T043) and library-side (T044–T051) can proceed in parallel. Content (T052–T058) depends on both.
- **US3 (Phase 5)**: Depends on US1 (T048 — `CredentialGateComponent` exists; T054 — `BlueBadge.razor` consumes it).
- **US4 (Phase 6)**: Depends on US1 (the picker is exercised by US1's journey).
- **Polish (Phase 7)**: Depends on all desired user stories being complete. T079/T080 run last.

### Why US2 ships before US1 (despite both being P1)

The boundary doc mandates the `samples/` extract land first. PR-A is the structural prerequisite for the Blue Badge page. If PR-A doesn't land first, PR-B/C's new files go into the wrong place and have to be moved later — at which point the boundary gate fails.

### Within Each User Story

- All `[P]`-tagged tasks within a story can run in parallel (different files).
- Models before services before endpoints before tests-of-endpoints.
- bunit / integration tests can be written in parallel with the implementation they cover, but should land in the same PR so coverage doesn't drift.

### Parallel Opportunities

- All Phase 1 setup tasks in parallel.
- Phase 2: T004 → T005 → T006 (sequential); T007 in parallel with T004–T006.
- Phase 3 (US2): T008–T017 mostly parallel (different files in a brand-new project); T018–T020 sequential (extract chain); T021/T022 sequential; T023–T025 parallel; T026 final; T027 parallel.
- Phase 4 (US1):
  - **Wave A (parallel)**: T028, T029, T030, T031 (data models); T035, T044, T045 (library setup); T052 (blueprint JSON).
  - **Wave B (after A)**: T032, T033, T034, T036, T037 (runtime wiring); T046, T047 (signal); T053 (form component).
  - **Wave C (after B)**: T038, T039 (endpoints); T048, T049 (gate component); T054, T055 (page).
  - **Tests (parallel with Wave B/C)**: T040, T041, T042, T043, T050, T051.
  - **Final (after all)**: T056, T057, T058.
- Phase 5 (US3): T059/T060 sequential (signalling mechanism choice); T061/T062 parallel after T059/T060; T063/T064 sequential.
- Phase 6 (US4): T065 first; T066/T067 parallel; T068/T069 sequential.
- Phase 7: T070–T078 all parallel; T079/T080 sequential at the end.

---

## Parallel Example: User Story 1 Wave A (Platform-side data models)

```pwsh
# After T032 unblocks (schema validation pipeline ready), launch in parallel:
Task: "T028 Create src/Services/Sorcha.Blueprint.Service/Models/PrerequisitePresentationRequest.cs"
Task: "T029 Create src/Services/Sorcha.Blueprint.Service/Models/PresentationRequest.cs"
Task: "T030 Create src/Services/Sorcha.Blueprint.Service/Models/PresentationResponse.cs"
Task: "T031 Create src/Services/Sorcha.Blueprint.Service/Models/DisclosedClaims.cs"
Task: "T035 Create BlueprintHubGroups.PresentationNonce builder"
Task: "T044 Extend BlueprintHubConnection with OnPresentationReceived event hook"
Task: "T052 Create walkthroughs/Strathcarron/blueprints/strathcarron-blue-badge.json"
```

---

## Implementation Strategy

### MVP increment 1 — PR-A (structural extract)

Phase 1 + Phase 2 + Phase 3 (US2). Ships the sample, moves the F126 page, CI grep gate active. F126 cold-start walkthrough still works end-to-end. **No new feature visible to citizens yet — this is the boundary investment.**

### MVP increment 2 — PR-B (platform contract + library)

Phase 4 partial: platform-side (T028–T043) + library-side (T044–T051). Ships the `prerequisites.presentationRequests` blueprint contract, the three endpoints, the `CredentialGateComponent`. **The library is consumable; no concrete demo yet.**

### MVP increment 3 — PR-C (Blue Badge content)

Phase 4 final: content (T052–T058). Ships the Blue Badge blueprint, the council page, the seed script. **US1 is end-to-end demoable.** This is the spec's load-bearing demo beat.

### MVP increment 4 — PR-D (failure paths + multi-credential + polish)

Phase 5 (US3) + Phase 6 (US4) + Phase 7. Adds the no-credential error state, multi-credential picker behaviour, Playwright E2E, doc propagation, structured logging audit, regression sign-off. **Feature complete.**

Standing instruction per CLAUDE.md: each PR waits for `claude-review` before merging unless the operator explicitly says "merge and continue" (as on the boundary brainstorm PR #718).

---

## Notes

- `[P]` tasks = different files, no dependencies on incomplete tasks in the same wave.
- `[Story]` label maps tasks to spec user stories US1–US4.
- Each story is independently demoable after its phase completes.
- `data-testid` attributes preserved across the move (T019) so F126 Playwright suites keep passing.
- No EF migrations expected in Spec 4 — `BlueBadgeCredential` lives in the existing F126 register; presentation request/response are Redis-stashed.
- Avoid: introducing the third-party-integrator auth path (deferred to Spec 5 per Q-T4 in research.md); per-claim disclosure toggles (deferred per design Q1); server-set cookie binding (deferred per design Q4).
