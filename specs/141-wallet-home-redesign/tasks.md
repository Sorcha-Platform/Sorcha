---
description: "Task list for Citizen Wallet Home Bolder reskin"
---

# Tasks: Citizen Wallet Home — "Bolder" Visual Reskin

**Input**: Design documents from `specs/141-wallet-home-redesign/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/component-contracts.md, quickstart.md

**Tests**: INCLUDED — the spec's verification section explicitly requires Playwright E2E (CitizenWallet) at phone/tablet plus a web-host render-sanity check; bUnit added for component logic.

**Organization**: By user story (US1 empty home = MVP). The feature centres on two host files (`Index.razor`, `MainLayout.razor`) and four new shared components, so genuine cross-story parallelism is limited to distinct component/CSS/test files — `[P]` is applied only where files truly don't collide.

## Path Conventions

- Shared components: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Wallet/`
- Theme: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Theme/SorchaMudTheme.cs`
- PWA host: `src/Apps/Sorcha.Wallet.Pwa/Pages/Index.razor`, `src/Apps/Sorcha.Wallet.Pwa/MainLayout.razor`, `src/Apps/Sorcha.Wallet.Pwa/wwwroot/css/app.css`
- Web host (verify only): `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/`
- E2E: `tests/Sorcha.UI.E2E.Tests/` · bUnit: `tests/Sorcha.UI.Core.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a known-good baseline before changing chrome.

- [ ] T001 Build the touched projects clean on this branch and bring the stack up for E2E (`dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Components.User`, `…/Sorcha.Wallet.Pwa`; `docker-compose up -d`); confirm the current wallet home renders at `http://localhost/wallet` as the pre-reskin baseline.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Theme tokens, CSS variables, and the E2E harness that every story's components draw on.

**⚠️ CRITICAL**: No story component can be styled until tokens/variables exist.

- [ ] T002 Extend `SorchaMudTheme.Default` `PaletteLight`/`PaletteDark` with the design tokens (light `Background #f4f5fb`, `Surface #ffffff`, `TextPrimary #0f1024`, `TextSecondary #5a607a`, `LinesDefault #e5e7ef`; dark `Background #0a0b14`, `Surface #181928`, `TextPrimary #f3f4fa`, `TextSecondary #9a9cb3`, `LinesDefault #252638`) in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Theme/SorchaMudTheme.cs`. Keep Primary/Secondary unchanged.
- [ ] T003 [P] Add shared CSS custom properties — `--sorcha-gradient`, `--sorcha-hero-gradient` (light + a dark-scope override ending `#1a0d2e`), `--sorcha-accent #48bb78`, `--sorcha-warn #d69e2e` — plus a global `@media (prefers-reduced-motion: reduce)` base hook, in `src/Apps/Sorcha.Wallet.Pwa/wwwroot/css/app.css`. Confirm the dark-mode selector hook (R-006 #1: MudBlazor 9.2 dark root class) used to switch `--sorcha-hero-gradient`.
- [ ] T004 [P] Establish the authenticated citizen-wallet E2E harness: create `tests/Sorcha.UI.E2E.Tests/PageObjects/CitizenWallet/WalletHomePage.cs` and, if absent, an `AuthenticatedCitizenWalletTestBase` (sign-in once, `/wallet` base URL, console-error/network capture) in `tests/Sorcha.UI.E2E.Tests/Infrastructure/`. Page object exposes selectors for hero, eyebrow/headline, header affordances, ghost stack + top card, Present/Verify buttons, and the floating tab bar (via `data-testid`).

**Checkpoint**: Tokens + variables + E2E harness ready — story work can begin.

---

## Phase 3: User Story 1 - First-run citizen sees the bold empty home (Priority: P1) 🎯 MVP

**Goal**: The empty wallet renders the gradient hero, the three-card ghost stack (Enrol tap-target), and the Present(disabled)/Verify(enabled) action pair.

**Independent Test**: Sign in as a citizen with zero credentials; confirm hero "WELCOME" + ghost stack + action pair; tap top ghost card → enrolment; tap Verify → verify flow.

### Tests for User Story 1 ⚠️ (write first, expect fail)

- [ ] T005 [P] [US1] E2E empty-home test (hero eyebrow "WELCOME" + "Your wallet is empty"; three ghost cards; Present disabled; Verify enabled; tap top ghost → `/wallet/enrol`; tap Verify → `/wallet/verify`; zero console errors) in `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletHomeRedesignTests.cs`.
- [ ] T006 [P] [US1] bUnit tests for `BigActionButton` (disabled suppresses `OnActivated`; pointerdown adds pressed class), `WalletCardStack` (renders 3 cards; top card invokes `OnAddCredential`; accessible name), `WalletHero` (Empty copy; renders `HeaderContent`) in `tests/Sorcha.UI.Core.Tests/Components/Wallet/`.

### Implementation for User Story 1

- [ ] T007 [P] [US1] Create `BigActionButton.razor` + `.razor.css` (Primary gradient / Ghost surface variants, 104px/radius 16, icon-chip + title/subtitle, pointer-event `scale(.97)` press, `Disabled` opacity .72 + no-op, accessible name, `data-testid`) in `…/Components/Wallet/`.
- [ ] T008 [P] [US1] Create `WalletCardStack.razor` + `.razor.css` (three ghost cards per handoff offsets/rotations/opacities; top card gradient + "SORCHA" eyebrow + "Add a credential" + plus-circle + subtitle; top card = button → `OnAddCredential`; `prefers-reduced-motion` → static positions; `data-testid`) in `…/Components/Wallet/`.
- [ ] T009 [P] [US1] Create `WalletHero.razor` + `.razor.css` (`WalletHeroMode` Empty/Active; absolute gradient layer using `--sorcha-hero-gradient` + clip-path `polygon(0 0,100% 0,100% 78%,0 100%)` + 8% inline-SVG grid; relative content layer; `HeaderContent` slot styled white-on-gradient; eyebrow/h1/subtitle from Mode + optional overrides; `data-testid`) in `…/Components/Wallet/`.
- [ ] T010 [US1] Wire `Index.razor` empty branch: render `WalletHero Mode="Empty"` with `HeaderContent` = existing `ContextChipSwitcher` + inbox bell + scan affordances; `WalletCardStack` `OnAddCredential` → `Nav.NavigateTo("enrol")`; `BigActionButton` Present (`Disabled=true`) + Verify → `Nav.NavigateTo("verify")`. Preserve the existing waiting-card branch (pending notice) and clock-skew alerts. (depends T007–T009)
- [ ] T011 [US1] Suppress the top `MudAppBar` on Home only (R-006 #2) so the hero owns the chrome, and relocate the org-switcher/bell/scan usage into the hero header slot; keep the top bar for non-home pages. Confirm `ContextChipSwitcher` reads correctly white-on-gradient (add a thin variant/override if needed, R-006 #3). (touches `Index.razor` + `MainLayout.razor`; depends T009, T010)

**Checkpoint**: Empty home is the new bold design and is independently demoable.

---

## Phase 4: User Story 2 - Citizen with credentials sees the populated home under the new chrome (Priority: P1)

**Goal**: Populated wallet shows the active hero + existing credential cards + enabled Present, with every F125 band and F124 overlay preserved.

**Independent Test**: Sign in with ≥1 credential (or "Load demo"); confirm hero "ACTIVE WALLET" + count, existing cards render, Present opens present flow, and all pre-existing bands/overlays still behave.

### Tests for User Story 2 ⚠️

- [ ] T012 [P] [US2] E2E populated-home test (after Load-demo: hero "ACTIVE WALLET" + "1 credential"; existing credential card present; Present enabled → `/wallet/present`; needs-attention/recent-activity/context-peek render when seeded; welcome overlay fires once) in `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletHomeRedesignTests.cs`.

### Implementation for User Story 2

- [ ] T013 [US2] Wire `Index.razor` populated branch: `WalletHero Mode="Active" CredentialCount="@_credentials.Count"`; `BigActionButton` Present enabled → existing `HandlePresentRequestedAsync`; render the EXISTING credential cards unchanged; keep `NeedsAttentionBand`/`RecentActivityFeed`/`ContextPeekFooter`/`WaitingCard`/`WelcomeTakeover` exactly as today, repositioned under the hero. (depends T010)
- [ ] T014 [US2] Regression-verify under the new chrome: context switch refreshes hero count + bands + dismisses pending welcome; push-then-render still triggers welcome eligibility; transient sync/notice failures never block render (manual + assert in T012). (depends T013)

**Checkpoint**: Both empty and populated homes work; no F125/F124 regression.

---

## Phase 5: User Story 3 - Floating tab bar navigation (Priority: P2)

**Goal**: A floating pill nav bar (Home/Devices/Activity/Settings) replaces the flush bottom rail across the wallet shell.

**Independent Test**: From any primary screen, the floating bar shows, the active tab is highlighted+labelled, each tab navigates, and content is never obscured.

### Tests for User Story 3 ⚠️

- [ ] T015 [P] [US3] E2E nav test (floating bar visible on Home/Devices/Activity/Settings; tap each → correct base-relative route; active tab highlighted+labelled, others icon-only; long content not obscured) in `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletNavigationRedesignTests.cs`.
- [ ] T016 [P] [US3] bUnit `FloatingTabBar` tests (active route → pill+label; inactive icon-only; `OnNavigate` emits base-relative route; each tab has accessible name) in `tests/Sorcha.UI.Core.Tests/Components/Wallet/`.

### Implementation for User Story 3

- [ ] T017 [P] [US3] Create `FloatingTabBar.razor` + `.razor.css` (`position:fixed; left:16; right:16; bottom:14; height:56; radius:18; backdrop-filter:blur(20px)`; light/dark surface + shadow; 4 fixed tabs; active = gradient pill + label, inactive icon-only; `ActiveRoute` param; `OnNavigate` EventCallback; accessible names; active detection per R-006 #4; `data-testid` per tab) in `…/Components/Wallet/`.
- [ ] T018 [US3] Replace the bottom `MudAppBar` rail in `MainLayout.razor` with `FloatingTabBar` (`OnNavigate` → base-relative `Nav.NavigateTo`, home `""`); keep `MudMainContent` bottom padding (≥84px) so content clears the bar. (depends T017)

**Checkpoint**: Shell navigation is the floating pill bar on every primary screen.

---

## Phase 6: User Story 4 - Dark mode follows theme preference (Priority: P2)

**Goal**: The wallet renders the dark palette + dark hero variant when the resolved theme is dark, light otherwise.

**Independent Test**: Set theme Dark → dark page/surfaces/text + dark hero gradient; Light → light palette; System → follows OS.

### Tests for User Story 4 ⚠️

- [ ] T019 [P] [US4] E2E theme test (preference Dark → home root carries the dark theme class + dark page bg; Light → light; assert hero gradient variant + legible text) in `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletHomeRedesignTests.cs`.

### Implementation for User Story 4

- [ ] T020 [US4] Bind dark mode in `MainLayout.razor`: `<MudThemeProvider Theme="SorchaMudTheme.Default" IsDarkMode="@_isDark" />`; inject `IThemeService`, call `InitializeAsync()` on init, set `_isDark = ThemeService.IsDarkMode`, subscribe `OnThemeChanged` → `StateHasChanged`, unsubscribe in dispose. (depends T002; verify R-001 binding API)
- [ ] T021 [US4] Add dark-variant rules to `WalletHero`/`BigActionButton`/`WalletCardStack`/`FloatingTabBar` `.razor.css` (dark shadows, ghost-card neutral `#22243a`, dark surfaces) keyed off the confirmed dark hook. (depends T007–T009, T017)

**Checkpoint**: Dark and light both render correctly from the user's preference.

---

## Phase 7: User Story 5 - Consistent, accessible rendering across sizes and surfaces (Priority: P3)

**Goal**: No overflow at phone/tablet, reduced-motion respected, accessible names present, shared components mount cleanly in the web host.

**Independent Test**: Phone + tablet widths no horizontal scroll; reduced-motion suppresses transforms; keyboard/AT reaches each control by name; `/app` builds+renders with the shared Wallet components compiled in.

### Tests for User Story 5 ⚠️

- [ ] T022 [P] [US5] E2E responsive+a11y test (phone ~375px and tablet ~768px viewports: no horizontal overflow, all regions visible; with reduced-motion emulated, no transform animation; assert accessible names on Present/Verify, ghost top card, each nav tab) in `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletHomeResponsiveTests.cs`.
- [ ] T023 [P] [US5] Web-host render-sanity (build `Sorcha.UI.Web.Client`; an E2E or smoke check that `/app` loads at phone/tablet with the shared `Components/Wallet/` compiled in and no console/runtime error) in `tests/Sorcha.UI.E2E.Tests/Docker/` (web category).

### Implementation for User Story 5

- [ ] T024 [US5] Ensure `@media (prefers-reduced-motion: reduce)` suppression is applied in every new component `.razor.css` (press scale, card-stack transforms → instant). (depends T007–T009, T017)
- [ ] T025 [US5] Verify/adjust phone + tablet layout in the component CSS + `Index.razor` so no region overflows horizontally at comfy density (hero clip, action grid, floating bar insets). (depends T010, T013, T018)
- [ ] T026 [US5] Add/confirm `aria-label`/role and accessible names on all interactive elements (action buttons, ghost top card as a button, nav tabs, header affordances). (depends T010, T018)

**Checkpoint**: Cross-size + a11y + web-host all green.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T027 Restyle the sync-warning as the amber pill (`--sorcha-warn`, icon tile, radius 14) shown only when sign-in is required / sync paused, and move its copy to a localisation resource (handoff note 7) — `Index.razor` + resource file.
- [ ] T028 [P] Run CI gates green: `pwsh scripts/check-no-snackbar.ps1` (no `ISnackbar` reintroduced) and `pwsh scripts/check-pwa-bundle.ps1` (no forbidden assemblies; `Sorcha.UI.Components.User` present).
- [ ] T029 [P] Doc propagation: note the new `Components/Wallet/` chrome components + dark-mode wiring in the `sorcha-ui` / `frontend-design` skills and the `Sorcha.UI.Components.User` README; correct the stale MudBlazor "8.15.0" in the `frontend-design` skill to 9.2.0.
- [ ] T030 Run `quickstart.md` end-to-end against the Docker stack and the full `Category=CitizenWallet` E2E suite; confirm zero console errors / failed network calls on the happy path (SC-006), then open the PR per repo policy.

---

## Dependencies & Execution Order

### Phase dependencies

- Setup (P1) → Foundational (P2) blocks all stories.
- US1 (P3 phase) is the MVP and creates all three home components; US2 wires the populated branch and **depends on US1** (same `Index.razor`).
- US3 (tab bar) is largely independent (new component + `MainLayout` bottom rail) — can run alongside US1/US2.
- US4 (dark mode) depends on T002 + the components existing (US1, US3) for their dark variants.
- US5 depends on the components + host wiring being in place.
- Polish (P8) after the desired stories.

### Within each story

- Tests written first (expect fail) → component(s) → host wiring.
- Models n/a (no new data). Components before host wiring; host wiring before regression checks.

### Parallel opportunities

- T003 ∥ T004 (foundational, different files).
- T005 ∥ T006 (US1 tests, different files); T007 ∥ T008 ∥ T009 (three independent component files).
- T015 ∥ T016 and T017 can proceed while US1/US2 Index work is in flight (different files: `MainLayout`/`FloatingTabBar` vs `Index.razor`), but T011/T018 both touch `MainLayout.razor` — sequence those.
- T022 ∥ T023; T028 ∥ T029.

### Sequencing cautions (same-file collisions)

- `Index.razor`: T010 → T013 → T014 → T025 → T027 (sequential).
- `MainLayout.razor`: T011 → T018 → T020 (sequential).

---

## Parallel Example: User Story 1

```text
# After Foundational, launch the three component builds together:
Task: T007 Create BigActionButton.razor + .razor.css
Task: T008 Create WalletCardStack.razor + .razor.css
Task: T009 Create WalletHero.razor + .razor.css
# and the two test files together:
Task: T005 E2E empty-home test
Task: T006 bUnit component tests
# Then T010 (Index wiring) — depends on T007–T009.
```

---

## Implementation Strategy

### MVP first (US1)

1. Phase 1 Setup → 2. Phase 2 Foundational (tokens/variables/E2E harness) → 3. Phase 3 US1 (empty bold home) → **STOP & VALIDATE** the empty home independently → demo.

### Incremental delivery

US1 (empty) → US2 (populated, no regression) → US3 (floating nav) → US4 (dark mode) → US5 (responsive/a11y/web-sanity) → Polish. Each story is demoable on its own; US2 builds on US1's components.

### Notes

- `[P]` = different files, no incomplete-task dependency.
- Keep all feedback inline (`IInlineFeedback`); never reintroduce `ISnackbar`.
- PWA nav stays base-relative (`NavigateTo("present")`, home `NavigateTo("")`).
- Commit after each task or logical group; verify tests fail before implementing.
