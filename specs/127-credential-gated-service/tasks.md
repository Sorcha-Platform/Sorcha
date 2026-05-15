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
- [x] T002 [P] Reserve port 5400 for `strathcarron-portal` in `docs/getting-started/PORT-CONFIGURATION.md` — add the new entry alongside the existing service ports.
- [x] T003 [P] Reserve env-var key `STRATHCARRON_PORTAL_URL` in the operator-facing docs so PowerShell walkthroughs can override the default `http://localhost:5400/` if needed.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Top-level `samples/` directory + the CI grep gate that enforces the boundary rule. These MUST land before any user-story tasks because all sample work depends on them.

**⚠️ CRITICAL**: No user story work can begin until Phase 2 is complete.

- [x] T004 Create top-level `samples/` directory and write `samples/README.md` explaining the boundary contract: "Code in `samples/` is application-specific; no `ProjectReference` into `src/Apps/Sorcha.UI/` other than `Sorcha.UI.Components.User`. CI grep gate enforces. See `docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md`."
- [x] T005 Create `scripts/check-samples-references.ps1` — PowerShell script that globs `samples/**/*.csproj`, parses each for `<ProjectReference>` entries, fails with a clear message if any reference points into `src/Apps/Sorcha.UI/` and is not `Sorcha.UI.Components.User`. Exit code 1 on violation, 0 on clean.
- [x] T006 Wire `scripts/check-samples-references.ps1` into the existing GitHub Actions CI workflow (`.github/workflows/ci.yml` or whichever file currently runs `dotnet test`) — runs alongside the build step; build fails if the gate fails.
- [ ] T007 Verify `Sorcha.AtomicCache` `ProjectReference` is present in `src/Services/Sorcha.Blueprint.Service/Sorcha.Blueprint.Service.csproj` (F126 lesson — `Sorcha.AtomicCache` is NOT transitive through `Sorcha.ServiceDefaults`). Add if missing.

**Checkpoint**: `samples/` exists; CI fails on a forbidden reference; the Blueprint Service can resolve `IAtomicDistributedCache`. Story implementation can begin.

---

## Phase 3: User Story 2 — Consumer council page in `samples/` (Priority: P1 — architectural sequencing first) 🎯 MVP-PR-A

**Goal**: The Strathcarron council site is a separate deployable. PR-A creates `samples/strathcarron-portal/`, moves the F126 driving-licence page out of `Sorcha.UI.Web.Client`, and proves the F126 cold-start walkthrough still works end-to-end against the new sample.

**Independent Test**: After PR-A, the F126 walkthrough (cold-start journey) runs end-to-end against `http://localhost:5400/services/driving-licence`. The old route in `Sorcha.UI.Web.Client` no longer responds. CI grep gate fails the build when a deliberate forbidden reference is added.

### Sample project skeleton

- [x] T008 [P] [US2] Create `samples/strathcarron-portal/Sorcha.Sample.StrathcarronPortal.csproj` — Blazor WASM, `net10.0`, nullable enabled, no warnings as errors. `ProjectReference` entries: `Sorcha.UI.Components.User`, `Sorcha.ServiceClients`, `Sorcha.CitizenWallet.Abstractions` (for shared models). NO other references into `src/Apps/Sorcha.UI/`.
- [x] T009 [P] [US2] Create `samples/strathcarron-portal/Program.cs` — Blazor WASM host setup: `WebAssemblyHostBuilder.CreateDefault`, `AddSorchaServiceClients(builder.Configuration)`, `AddHttpClient<ITierProbeService, HttpTierProbeService>(…)`, `AddHttpClient<IEnrolPairingSignal, EnrolPairingSignal>(…)`, base address pointed at `builder.HostEnvironment.BaseAddress`. License header per CLAUDE.md §7.
- [x] T010 [P] [US2] Create `samples/strathcarron-portal/App.razor` — standard Blazor `<Router>` with `RouteView` using `CouncilLayout`.
- [x] T011 [P] [US2] Create `samples/strathcarron-portal/wwwroot/index.html` — standard Blazor WASM template, page title "Strathcarron Council", link to `css/council.css`, viewport meta for mobile.
- [x] T012 [P] [US2] Create `samples/strathcarron-portal/wwwroot/css/council.css` — distinct council styling: serif header font, navy primary colour (`#1F3A5F` — not MudBlazor's purple), pale background, accessible link colour, simple grid for service cards.
- [x] T013 [P] [US2] Create `samples/strathcarron-portal/wwwroot/images/strathcarron-logotype.svg` — placeholder council mark (simple shield with "SC" monogram + "STRATHCARRON COUNCIL" wordmark).

### Council chrome (PR-A baseline IA)

- [x] T014 [P] [US2] Create `samples/strathcarron-portal/Layout/CouncilLayout.razor` — `LayoutComponentBase`, renders `<CouncilHeader /> <main>@Body</main> <CouncilFooter />`.
- [x] T015 [P] [US2] Create `samples/strathcarron-portal/Layout/CouncilHeader.razor` — logotype on the left, wordmark, primary nav: Services / About / Contact us / My account. Plain `<nav>` markup, no MudBlazor.
- [x] T016 [P] [US2] Create `samples/strathcarron-portal/Layout/CouncilFooter.razor` — postal address ("Strathcarron Council, Main Street, Strathcarron"), accessibility statement link, privacy notice link, copyright line.

### Services landing page

- [x] T017 [P] [US2] Create `samples/strathcarron-portal/Pages/Index.razor` — `@page "/"`, "Strathcarron Council — services" page heading, two service cards in a CSS grid: **Driving Licence** (live link to `/services/driving-licence`) and **Blue Badge** (rendered as a "coming soon" placeholder card with disabled affordance; PR-C activates it).

### Move the F126 driving-licence page

- [x] T018 [US2] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/CouncilApplicationDrivingLicence.razor samples/strathcarron-portal/Pages/DrivingLicence.razor`. Update `@page` to `"/services/driving-licence"`. Update `@layout` to `CouncilLayout`. Remove the `using Sorcha.UI.Web.Client.Components.Layout` line. Update the header to remove the F126 "stand-in" doc-comment (the new home is no longer a stand-in).
- [x] T019 [US2] Extract the form body of `samples/strathcarron-portal/Pages/DrivingLicence.razor` into `samples/strathcarron-portal/Components/DrivingLicenceForm.razor` so PR-C's `BlueBadge.razor` can mirror the shape. Preserve every `data-testid` attribute exactly (F126 Playwright tests depend on `driving-licence-submit`).
- [x] T020 [P] [US2] Create `samples/strathcarron-portal/Components/DrivingLicenceForm.razor` (target of T019's extract) — `@bind-Value`-driven form with full-name, date-of-birth, address fields and the submit button.

### Container + compose

- [x] T021 [P] [US2] Create `samples/strathcarron-portal/Dockerfile` — multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` build stage runs `dotnet publish -c Release -o /app/publish`; `nginx:alpine` final stage copies `/app/publish/wwwroot/` to `/usr/share/nginx/html/` and uses default nginx config (port 80).
- [x] T022 [US2] Add `strathcarron-portal` service to `docker-compose.yml`: `build.context: ./samples/strathcarron-portal/`, `ports: ["5400:80"]`, `depends_on: [gateway]`, `networks: [sorcha]`. Verify no port clash via `docs/getting-started/PORT-CONFIGURATION.md`.

### Update F126 references after the move

- [x] T023 [US2] Update `walkthroughs/Strathcarron/setup-cold-start-demo.ps1` — `state.json.councilPage` now points at `http://localhost:5400/services/driving-licence`. Update the operator-facing `Write-WtInfo` messages to match.
- [x] T024 [US2] Update F126 Playwright nav-coverage suite (search `tests/` for tests that reference `/strathcarron/services/driving-licence` or `/app/strathcarron/services/driving-licence`) — switch to the new URL. Verify `data-testid="driving-licence-submit"` still resolves.
- [x] T025 [US2] Update doc propagation for the move: `.claude/skills/sorcha-architecture/SKILL.md` "Council application enrolment gate (Feature 126)" section, `.specify/MASTER-TASKS.md` if it references the old URL, `MEMORY.md` "n1 SPA path quirk" bullet (note that the quirk now applies only to surfaces inside Sorcha.UI.Web.Client; council pages no longer live there).

### PR-A validation

- [ ] T026 [US2] Bring up the local Docker stack and walk the F126 cold-start journey end-to-end against `http://localhost:5400/services/driving-licence`. Confirm: preflight signup → wallet-pairing QR → PWA redeem + confirm dialog → device pairing → council page advances → form → "watch your wallet". This is the SC-006 gate.
- [x] T027 [P] [US2] Verify `scripts/check-samples-references.ps1` rejects a deliberate violation: temporarily add `<ProjectReference Include="..\..\src\Apps\Sorcha.UI\Sorcha.UI.Core\Sorcha.UI.Core.csproj" />` to the sample csproj, run the gate, assert exit 1 with a clear message naming the forbidden reference, revert.

**Checkpoint**: PR-A is shippable. The F126 cold-start walkthrough works against the new sample. The CI grep gate is live. No new functionality yet — this is the structural prerequisite.

---

## Phase 4: User Story 1 — Returning Tier 1 happy path (Priority: P1) 🎯 MVP-PR-B-and-C

**Goal**: Sarah returns to Strathcarron, taps "Prove you're you" on the Blue Badge page, her wallet presents her `AssuredIdentityCredential` (verified server-side via `Sorcha.Verifier.Engine` inside the new `SorchaWalletPresentationConsumer`), the council page autofills the form from the disclosed claims, she fills the Blue Badge-specific fields, submits, and the `BlueBadgeCredential` lands in her wallet.

**Independent Test**: After PR-B + PR-C land, run `walkthroughs/Strathcarron/setup-blue-badge-demo.ps1`, sign in as the returning citizen at `http://localhost:5400/services/blue-badge`, walk the journey. End-to-end in under 45 seconds, new credential in the wallet within ~5 s of submit.

**F111 reconciliation**: This phase was substantially restructured on 2026-05-15 after discovering Feature 111 already ships the presentation-lifecycle substrate. F127 now extends F111 rather than building parallel infrastructure. See `docs/superpowers/specs/2026-05-15-f127-f111-reconciliation.md` and the design doc's §14. The task count shrank from 31 to 27 net; many of the old "create new endpoint / new model / new service" tasks are gone.

### Platform-side: Sorcha-wallet consumer + F111 extensions (PR-B)

- [x] T028 [P] [US1] Add new optional method `Task<ConsumerInitiationDescriptor> BuildInitiationAsync(PresentationInitiationContext, CancellationToken)` to `src/Common/Sorcha.PresentationLifecycle.Abstractions/IPresentationConsumer.cs` as a default-throws default interface method per `contracts/presentation-consumer-buildinitiation.md`. HAIP's existing `IPresentationConsumer` impls continue to compile unchanged.
- [x] T029 [P] [US1] Add `ConsumerInitiationDescriptor` record `(string AuthorizationRequestUri, string? RequestUri, string? Nonce)` to `src/Common/Sorcha.PresentationLifecycle.Abstractions/` next to `IPresentationConsumer.cs`.
- [x] T030 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service/Services/Implementation/SorchaWalletPresentationConsumer.cs` — implements `IPresentationConsumer` with `ConsumerName = "sorcha-wallet"`. `VerifyAsync` deserialises the wallet's signed VP from the F111 callback payload, invokes `Sorcha.Verifier.Engine`, returns `PresentationOutcome.Success` with verified claims filtered to `context.RequiredClaims`, or `PresentationOutcome.Decline` with reason code (`expired-credential` / `revoked` / `wrong-issuer` / `signature-invalid` / `claims-missing`). `BuildInitiationAsync` returns an OID4VP `openid4vp://` URI carrying the council DID, presentation_definition derived from `context.CredentialRequirement`, nonce, and `response_uri` resolving to F111's existing `/api/presentations/callbacks/sorcha-wallet/{requestId}` endpoint. **PR-B partial**: VerifyAsync fully implemented; BuildInitiationAsync returns a stub URI marked TODO(T032) — finalised when the lifecycle service's per-consumer dispatch lands.
- [x] T031 [US1] Create `src/Services/Sorcha.Blueprint.Service/Storage/Presentations/IClaimsFetchTokenStore.cs` + `RedisClaimsFetchTokenStore.cs`. `StoreAsync(token, presentationRequestId, ttl)` writes via `SET NX`; `GetAndRemoveAsync(token)` returns the bound `presentationRequestId` and atomically deletes (NonceStore pattern; first-fetch wins). Implementation uses `IConnectionMultiplexer` directly + Lua `GETDEL` script (matching the F111 pattern); `IAtomicDistributedCache` wrapper migration deferred — direct StackExchange.Redis is the established Blueprint Service convention.
- [x] T032 [US1] Extend `src/Services/Sorcha.Blueprint.Service/Services/Implementation/PresentationLifecycleService.cs.InitiateAsync` — branches on `credentialRequirement.PresentationSource` at the top. HAIP path unchanged (legacy `_haipClient.CreatePresentationRequestAsync`); SorchaWallet path generates a fresh `Guid` requestId, constructs a `PresentationInitiationContext`, dispatches to the registered `"sorcha-wallet"` consumer's `BuildInitiationAsync`. Consumer-name flows through the rest of the body (replaces the previously hardcoded `"haip"` in `pending.ConsumerName` and `BuildPresentationInitiatedAsync(consumerName)`). On SorchaWallet, mints a 192-bit URL-safe `ClaimsFetchToken` via `IClaimsFetchTokenStore.StoreAsync` (TTL = validity window) and returns it on `PresentationInitiationResult`.
- [x] T033 [US1] Extend `PresentationInitiationResult` (in `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IPresentationLifecycleService.cs`) with an optional `string? ClaimsFetchToken` field. Existing HAIP callers receive `null`; the new Sorcha-wallet path returns a fresh single-use value per F127's claims-fetch contract.
- [ ] T034 [US1] Extend `src/Services/Sorcha.Blueprint.Service/Endpoints/PresentationEndpoints.cs` with `GET /api/presentations/{presentationRequestId:guid}/disclosed-claims` per `contracts/disclosed-claims-endpoint.md`. Implements: validate `?token=` via `IClaimsFetchTokenStore.GetAndRemoveAsync`; assert bound `presentationRequestId` matches the path param; read the `presentation-outcome` from the register (decrypting per disclosure rules); return `DisclosedClaimsResponse` (subset filtered to `requiredClaims`). `AllowAnonymous`, rate-limited under `RateLimitPolicies.Api`, OpenAPI `.WithSummary()` + `.WithDescription()`.
- [x] T035 [P] [US1] Extend `src/Services/Sorcha.Blueprint.Service/Hubs/BlueprintHubGroups.cs` — add `public static string PresentationNonce(Guid presentationRequestId) => $"presentation:{presentationRequestId:N}";`. CI grep gate (Feature 118 `check-no-inline-group-strings.ps1`) covers this since it enforces builder usage globally.
- [x] T036 [P] [US1] Extend `src/Services/Sorcha.Blueprint.Service/Hubs/IBlueprintHubClient.cs` — add `Task PresentationOutcomeReady(string presentationRequestId)` with XML doc per Feature 118 thin-signal convention. The `<see cref="…"/>` points at the disclosed-claims endpoint as the council page's next call.
- [x] T037 [US1] Wire `PresentationLifecycleService.HandleOutcomeAsync` to publish `IBlueprintHubClient.PresentationOutcomeReady(requestId)` to `BlueprintHubGroups.PresentationNonce(requestId)` immediately after the `presentation-outcome` tx is written (inline path). Try/log/swallow — publishing never fails the outcome write. **TODO(seal-drain)**: mirror the publish from `PresentationSealSubscriber` when an F119-deferred outcome eventually writes; tracked inline in the lifecycle service as a code comment.
- [x] T038 [US1] DI wiring in `src/Services/Sorcha.Blueprint.Service/Program.cs`: `SorchaWalletPresentationConsumer` registered as `IPresentationConsumer` alongside `HaipPresentationConsumer`; `IClaimsFetchTokenStore` → `RedisClaimsFetchTokenStore`. `Sorcha.AtomicCache` not required (the store uses `IConnectionMultiplexer` directly via the F111 pattern). Storage-registration-log entry deferred — these are short-TTL Redis caches, not audited persistent stores per Feature 113's audit list.

### Platform-side tests (PR-B)

- [ ] T039 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service.Tests/Services/SorchaWalletPresentationConsumerTests.cs` — `VerifyAsync` returns Success on a well-formed VP; returns Decline with the appropriate reason on `signature-invalid` / `claims-missing` / `credential-revoked` / `issuer-not-trusted`; `VerifiedClaims` filtered to required claims (no leak of extra VP claims). `BuildInitiationAsync` produces a well-formed `openid4vp://` URI with all required parameters; nonce is fresh per call; `response_uri` resolves to the F111 callback endpoint for the `sorcha-wallet` consumer name.
- [ ] T040 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service.Tests/Storage/Presentations/RedisClaimsFetchTokenStoreTests.cs` — `StoreAsync` then `GetAndRemoveAsync` returns the bound requestId; second `GetAndRemoveAsync` returns null (single-use); TTL respected.
- [ ] T041 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service.Tests/Endpoints/DisclosedClaimsEndpointTests.cs` (WebApplicationFactory) — success path returns plaintext claims; invalid token returns 401; token bound to a different requestId returns 401; pending-outcome returns 200 + `status=pending` without consuming the token; decline / abandonment returns 410; expired returns 404. Rate-limit policy enforced.
- [ ] T042 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service.Tests/Hubs/PresentationOutcomeReadyPublishTests.cs` — integration test asserting `PresentationOutcomeReady(requestId)` reaches a test client subscribed to `BlueprintHubGroups.PresentationNonce(requestId)` after a successful `HandleOutcomeAsync`, and is NOT delivered to clients on other groups.
- [ ] T043 [P] [US1] Create `src/Services/Sorcha.Blueprint.Service.Tests/Services/PresentationLifecycleServiceSorchaWalletTests.cs` — integration test: `InitiateAsync` against the Sorcha-wallet consumer dispatches to `BuildInitiationAsync` AND mints a `ClaimsFetchToken`; against the existing HAIP consumer falls back to legacy path AND does NOT mint a token. Same `PresentationInitiationResult` shape for both, with `ClaimsFetchToken` populated only on the Sorcha-wallet path.

### Library-side: `IPresentationSignal` + `BlueprintHubConnection` extension + `CredentialGateComponent` (PR-B)

- [ ] T044 [P] [US1] Extend `BlueprintHubConnection` (in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/Hubs/`) — add `event Func<string, Task>? OnPresentationOutcomeReady;` and wire to the hub's `PresentationOutcomeReady` typed-client method (mirror of how F126's `TenantHubConnection.OnDeviceEnrolled` is wired).
- [ ] T045 [P] [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/IPresentationSignal.cs` — interface with `Task<PresentationSignalOutcome> WaitForAsync(Guid presentationRequestId, CancellationToken ct)` and `event Func<Task>? OnManualRecoveryAvailable` (60 s ceiling per FR-023). `PresentationSignalOutcome` carries the `OutcomeKind` (Success / Decline / Abandoned) so the council page can branch on the kind even before fetching claims.
- [ ] T046 [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/PresentationSignal.cs` — composes `BlueprintHubConnection.OnPresentationOutcomeReady` (primary) with a 3 s polling fallback against F111's existing `GET /api/presentations/{requestId}/status` endpoint. Uses `TimeProvider.CreateTimer` for testability. Surfaces `OnManualRecoveryAvailable` after 60 s of no signal. Returns `PresentationSignalOutcome` when the lifecycle state transitions to terminal.
- [ ] T047 [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/CredentialGate/CredentialGateComponent.razor` — parameters: `Guid BlueprintId`, `string StartingActionId` (e.g. `"verify-identity"`), `EventCallback<DisclosedClaimsResponse> OnPresented`, `string? LinkBackUrl` (US3), `string? NameOfMissingCredentialType` (US3), `RenderFragment? ChildContent`. On init: submits the named starting action via `Sorcha.ServiceClients.Http.Blueprint` (which fires F111's `InitiateAsync`); receives `(presentationRequestId, authorizationRequestUri, claimsFetchToken)`; renders `HybridQrAffordance` (Layout=Auto) with `authorizationRequestUri`; subscribes to `IPresentationSignal.WaitForAsync(presentationRequestId)`. On `Success` signal: fetches disclosed claims via `GET /api/presentations/{id}/disclosed-claims?token=…` and fires `OnPresented(DisclosedClaimsResponse)`. On `Decline` / `Abandoned`: renders the appropriate error UX (US3 handles the no-credential variant). If the action has no `credentialRequirement` (resolved via the action metadata), renders `ChildContent` directly.
- [ ] T048 [US1] DI registration in `samples/strathcarron-portal/Program.cs` — add `IPresentationSignal` / `PresentationSignal` via `AddHttpClient<IPresentationSignal, PresentationSignal>(…)` (mirrors F126's `EnrolPairingSignal` registration); register `BlueprintHubConnection` if not already (it provides `OnPresentationOutcomeReady`).
- [ ] T049 [P] [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User.Tests/Components/CredentialGate/CredentialGateComponentTests.cs` (bunit) — renders QR on init when the action has a credentialRequirement; fires `OnPresented` with the disclosed claims on Success signal; renders `ChildContent` directly when no credentialRequirement; expires-and-regenerate path; manual-recovery path; decline/abandoned error UX.
- [ ] T050 [P] [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User.Tests/Services/User/Presentation/PresentationSignalTests.cs` — `FakeTimeProvider`-driven: hub-fast-path (signal arrives within 2 s), polling-path (hub fails to connect, F111 status-poll resolves at 3 s tick), manual-recovery-path (no signal after 60 s fires `OnManualRecoveryAvailable`).

### Blue Badge content (PR-C)

- [ ] T051 [P] [US1] Create `walkthroughs/Strathcarron/blueprints/strathcarron-blue-badge.json` — **three-action chain** per design §14: `verify-identity` (starting; citizen; `credentialRequirement.presentationSource: "sorcha-wallet"`, `credentialType: "AssuredIdentityCredential"`, `issuerAllowlist: ["did:sorcha:org:strathcarron-council"]`, `requiredClaims: ["givenName", "familyName", "dateOfBirth", "homeAddress"]`, no form schema), `submit-blue-badge-application` (predecessor `verify-identity`; citizen; schema with `mobilityCondition` required + `previousBadgeNumber` optional; `x-persona.presentation: "verify-identity"`), `issue-blue-badge` (predecessor `submit-blue-badge-application`; licensing-officer; issuance of `BlueBadgeCredential` to `SorchaLocalWallet`). `instanceReference` configured per CLAUDE.md §6.
- [ ] T052 [P] [US1] Create `samples/strathcarron-portal/Components/BlueBadgeForm.razor` — parameters: `DisclosedClaimsResponse Disclosed`. Renders the four identity fields as read-only chips at the top ("Verified ✓ — {SubjectDisplayName}"), the two Blue Badge-specific fields (`mobilityCondition` required text, `previousBadgeNumber` optional text), submit button bound to a callback. Reading-age compliant copy; data-testids `blue-badge-mobility`, `blue-badge-prev`, `blue-badge-submit`.
- [ ] T053 [US1] Create `samples/strathcarron-portal/Pages/BlueBadge.razor` — `@page "/services/blue-badge"`, `@layout CouncilLayout`. Composition: `EnrolGateComponent` → `CredentialGateComponent` (BlueprintId from config, StartingActionId="verify-identity", LinkBackUrl="/services/driving-licence", NameOfMissingCredentialType="Assured Identity") → `BlueBadgeForm`. On submit, calls the second action `submit-blue-badge-application` via `Sorcha.ServiceClients.Http.Blueprint` with the Blue-Badge-specific fields (autofilled disclosed claims are joined server-side by the x-persona.presentation autofill resolver). Renders "Your application is in. Watch your wallet…" success copy.
- [ ] T054 [US1] Update `samples/strathcarron-portal/Pages/Index.razor` — Blue Badge card from "coming soon" placeholder to live link at `/services/blue-badge`.

### Walkthrough seed (PR-C)

- [ ] T055 [US1] Create `walkthroughs/Strathcarron/setup-blue-badge-demo.ps1` — reads `state.json` from Spec 3, fails fast if absent. Publishes `strathcarron-blue-badge.json` against the existing register from Spec 3. Confirms the returning-Tier-1 citizen (`returning-*@example.test`) holds an `AssuredIdentityCredential` (queries the register); fails fast if not. Writes `blueBadgeBlueprintId` back into `state.json`. Prints walk URLs.
- [ ] T056 [US1] Update `walkthroughs/Strathcarron/setup-cold-start-demo.ps1` — automate the returning-Tier-1 device pairing AND `AssuredIdentityCredential` issuance step (currently operator-driven per F126 deferral note). Spec 4 needs the credential to actually exist for the demo to land. Document the new flow in the script header.

### PR-C validation

- [ ] T057 [US1] Walk the returning-Tier-1 journey end-to-end (Walk 1 from quickstart.md). Stopwatch ≤ 45 s in 95% of attempts (FR-012). Verify autofill (SC-002), 2 s signal latency (SC-004), no first-credential takeover, new credential lands in wallet within ~5 s.
- [ ] T058 [US1] Add an HAIP smoke test as part of the PR-B regression — confirms the existing HAIP presentation path still works after the `IPresentationConsumer.BuildInitiationAsync` extension lands and after the lifecycle service's dispatch path changes. Likely covered by existing F111 test suite; verify and call out if any gaps surface.

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
