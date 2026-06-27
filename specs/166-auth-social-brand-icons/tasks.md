---
description: "Task list for Feature 166 — Social Provider Brand Icons on Login & Signup"
---

# Tasks: Social Provider Brand Icons on Login & Signup

**Input**: Design documents from `/specs/166-auth-social-brand-icons/`

**Feature**: Add recognisable Google, Microsoft, GitHub, and Apple brand icons to social sign-in
buttons on three auth surfaces — web login, web signup (Razor Pages / Tenant Service), and the
citizen wallet PWA sign-in screen (Blazor WASM + MudBlazor). Visual-only; no auth-flow changes.

**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | contracts/icon-resolution.md ✅ | quickstart.md ✅

**Tests**: Unit tests for the web resolver are included (explicitly required in spec / research.md §R7).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to
- Exact file paths are included in every task description

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm no new packages/projects are required and the build baseline is clean.

- [X] T001 Confirm `MudBlazor 9.5.0` entry in `Directory.Packages.props` and verify `Icons.Custom.Brands.Google/Microsoft/GitHub/Apple` compile in `src/Apps/Sorcha.Wallet.Pwa` — no package change expected per plan.md

**Checkpoint**: Package baseline confirmed — foundational work can proceed.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Create the web icon resolver and the shared SVG sizing CSS that both Login and Signup depend on before their individual view patches are applied.

**⚠️ CRITICAL**: US1 and US2 both depend on T002 and T003 completing first.

- [X] T002 Create `src/Services/Sorcha.Tenant.Service/Services/SocialProviderBrandIcon.cs` — public static class with `public static HtmlString For(string? providerKey)` returning inline `<svg aria-hidden="true" …>` for `"google"` (4-colour G, fixed brand colours), `"microsoft"` (4-square, fixed brand colours), `"github"` (Octocat mark, `fill="currentColor"`), `"apple"` (apple mark, `fill="currentColor"`), and a neutral globe SVG (`fill="currentColor"`) for any other / null / empty input; match case-insensitively via `.ToLowerInvariant()`; pure, no-throw, no caller-supplied data interpolated into SVG; add `/// <summary>` XML doc on the class and method per project convention
- [X] T003 [P] Create `tests/Sorcha.Tenant.Service.Tests/Services/SocialProviderBrandIconTests.cs` — xUnit + FluentAssertions unit tests: each of `"google"`, `"microsoft"`, `"github"`, `"apple"` in lowercase, uppercase, and mixed casing returns a non-empty string starting with `<svg` and containing `aria-hidden="true"`; unknown key returns the neutral fallback (also starts with `<svg`, has `aria-hidden`); `null` and empty string return the neutral fallback and do not throw; depends on T002
- [X] T004 [P] Add `.social-btn svg` rule to `src/Services/Sorcha.Tenant.Service/wwwroot/css/auth.css` — set `width: 1.25rem; height: 1.25rem; flex-shrink: 0;` so the leading SVG scales correctly within the existing flex layout; no change to existing `.social-btn` rule (gap/align-items already set)

**Checkpoint**: Resolver unit tests pass (`dotnet test --filter "~SocialProviderBrandIcon"`); CSS diff is isolated to the new `.social-btn svg` rule; US1 and US2 view patches can now proceed in parallel.

---

## Phase 3: User Story 1 — Recognise my provider at a glance on the web login (Priority: P1) 🎯 MVP

**Goal**: Each social sign-in button on the web login page shows the provider's brand icon to the left of its label.

**Independent Test**: Start the stack, open `http://localhost/<tenant-auth>/auth/login` with Google + GitHub configured; confirm each social button displays a leading brand icon, the icon is legible in light and dark presentation, clicking still routes to the existing OAuth flow, and the SVG is `aria-hidden`.

- [X] T005 [US1] In `src/Services/Sorcha.Tenant.Service/Pages/Auth/Login.cshtml`, inside the `@foreach (var provider in Model.AvailableProviders)` social-btn loop (~L98-108), prepend `@Html.Raw(SocialProviderBrandIcon.For(provider))` as the first child of each `<button class="social-btn">` element; add the required `using`/`@inject` directive at the top of the page to make the static class available in Razor without model changes

**Checkpoint**: Web login page shows leading brand icons for every configured social provider; no button is text-only; unknown-provider fallback shows neutral globe; clicking any social button still starts the existing sign-in redirect (no flow change).

---

## Phase 4: User Story 2 — Recognise my provider at a glance on signup (Priority: P2)

**Goal**: Each provider choice on the web signup social tab shows the provider's brand icon, visually consistent with the login surface.

**Independent Test**: Open the web signup social option with providers configured; confirm each provider shows the correct brand icon and that the icon and label are visually identical to their appearance on the login page (US2 scenario 2).

- [X] T006 [US2] In `src/Services/Sorcha.Tenant.Service/Pages/Auth/Signup.cshtml`, inside the social provider loop (~L67-84), prepend `@Html.Raw(SocialProviderBrandIcon.For(provider))` as the first child of each social provider button element; mirror the identical change made in T005 to guarantee login/signup consistency (FR-002, SC-001)

**Checkpoint**: Web signup social tab shows the same brand icons at the same size and position as the login page; both surfaces confirmed consistent (compare side-by-side per US2 scenario 2).

---

## Phase 5: User Story 3 — Recognise my provider in the citizen wallet PWA (Priority: P2)

**Goal**: Each social provider button on the PWA sign-in screen shows the provider's brand icon as a leading icon, consistent with the passkey button's leading `Fingerprint` icon treatment.

**Independent Test**: Open the PWA sign-in screen (`/signin`) with providers configured on mobile viewport; confirm each social `MudButton` shows a leading brand icon that matches the size and alignment of the passkey button's `Fingerprint` icon, and that clicking a social button starts the existing PWA social flow unchanged.

- [X] T007 [US3] In `src/Apps/Sorcha.Wallet.Pwa/Pages/SignIn.razor`, add a `private static string ProviderIcon(string providerKey)` method — switch on `providerKey.ToLowerInvariant()` returning `Icons.Custom.Brands.Google`, `Icons.Custom.Brands.Microsoft`, `Icons.Custom.Brands.GitHub`, `Icons.Custom.Brands.Apple` for the four known keys and `Icons.Material.Filled.Public` for the default arm; mirrors the established pattern from `Sorcha.UI.Components.User/Components/Security/SocialLinksSection.razor` (~L187-194)
- [X] T008 [US3] In `src/Apps/Sorcha.Wallet.Pwa/Pages/SignIn.razor`, update the `MudButton` social-provider loop (~L44-51) to add `StartIcon="@ProviderIcon(provider)"` on each button; no other attribute changes — size, alignment, and spacing are inherited from `MudButton`'s `StartIcon` rendering, which already matches the passkey button at L36-41; depends on T007

**Checkpoint**: PWA sign-in screen shows a leading brand icon on every social `MudButton`; icon size/alignment visually matches the passkey `Fingerprint` icon; selecting any social button runs the existing PWA sign-in flow unchanged (US3 scenario 3).

---

## Final Phase: Polish & Cross-Cutting Concerns

**Purpose**: Verify end-to-end correctness across all three surfaces and ensure project conventions are met.

- [X] T009 [P] Run `dotnet build src/Services/Sorcha.Tenant.Service` and `dotnet build src/Apps/Sorcha.Wallet.Pwa` — confirm zero new warnings; in particular no missing `/// <summary>` warnings on `SocialProviderBrandIcon` and no nullable warnings introduced by the resolver or view changes
- [X] T010 [P] Run resolver unit tests and confirm all green: `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~SocialProviderBrandIcon"`
- [X] T011 Run the full Tenant Service test suite to confirm no regression: `dotnet test tests/Sorcha.Tenant.Service.Tests`
- [X] T012 Visual smoke test per quickstart.md §2-3: load web login + signup in browser, toggle light/dark — confirm Google/Microsoft show brand colours; GitHub/Apple marks are legible in both modes (track `currentColor`); then load PWA sign-in and confirm leading icons match passkey button treatment and narrow-mobile viewport shows no overflow (quickstart.md edge case)

**Done when**:
- [X] All resolver unit tests pass (T010)
- [X] Web login shows correct, legible, fallback-safe brand icons in light & dark (T005, T012)
- [X] Web signup shows identical icons to login for the same provider (T006, T012)
- [X] PWA sign-in shows leading brand icons matching the passkey button treatment (T007, T008, T012)
- [X] Zero new build warnings introduced (T009)
- [X] Full Tenant Service test suite green (T011)

---

## Dependency Graph

```
T001 (package check)
  └─→ T002 (resolver) ──→ T003 (unit tests)
         └─→ T004 (CSS) ──→ T005 [US1] Login.cshtml
                              └─→ T006 [US2] Signup.cshtml

T007 [US3] ProviderIcon() switch
  └─→ T008 [US3] SignIn.razor StartIcon wiring

T009, T010, T011, T012 (polish — after all above)
```

**US1 and US2 can only begin after T002 + T004 complete.**
**US3 (T007, T008) is fully independent of the web surface work and can proceed in parallel with Phase 3/4.**
**T003 (unit tests) can be written in parallel with T004 (CSS) once T002 (resolver) is done.**

---

## Parallel Execution Examples

**After T002 completes, run in parallel**:
```
T003 (unit tests)      T004 (CSS)       T007+T008 (PWA — independent)
```

**After T004 completes, run in parallel**:
```
T005 [US1] Login.cshtml    T006 [US2] Signup.cshtml
```

**Final parallel sweep**:
```
T009 (build check)    T010 (unit tests)    T011 (full suite)    T012 (visual smoke)
```

---

## Implementation Strategy

**MVP = Phase 3 (User Story 1)**: The web login resolver + CSS + Login.cshtml update delivers the
highest-traffic surface (P1) and the unit-tested resolver that proves the entire web approach correct.
US2 (signup) is a one-liner once the resolver exists; US3 (PWA) is independent and can be batched.

**Incremental delivery**:
1. Create resolver + tests (T002, T003) — independently verifiable unit value.
2. Add CSS (T004) — layout-safe, isolated change.
3. Patch Login.cshtml (T005) — US1 done, MVP complete.
4. Patch Signup.cshtml (T006) — US2 done, web surfaces complete.
5. Add PWA ProviderIcon + StartIcon (T007, T008) — US3 done, all three surfaces complete.
6. Polish pass (T009-T012) — confirm no regressions before PR.
