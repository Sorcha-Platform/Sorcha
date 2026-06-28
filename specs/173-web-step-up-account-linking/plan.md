# Implementation Plan: Web Step-Up Social Account Linking (B-UI)

**Branch**: `173-web-step-up-account-linking` | **Date**: 2026-06-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/173-web-step-up-account-linking/spec.md`

## Summary

Deliver the **web (`/app`) user-facing half** of the Feature 168 anonymous social account-linking
step-up flow. When a social sign-in whose verified email matches an existing account returns a
`LinkRequired` outcome, the web host lands at `/app/#outcome=LinkRequired&linkPendingToken=…` and
currently **dead-ends**. This feature detects that fragment, removes the token from the address bar,
and presents a **purpose-built anonymous step-up prompt** (`LinkExistingAccountPrompt`) that proves
ownership of the existing account using a **passkey** or an **authenticator (TOTP) code** (ReOAuth
deferred), then redeems the link-pending token to complete the link and establish a normal web
session.

**Technical approach**: A new anonymous-flow client service consumes the three Feature 168 endpoints
(challenge-initiate / challenge-verify / link-confirm) with the **link-pending token as principal**
(no bearer session). The prompt **reuses** the existing passkey ceremony (`PasskeyInteropService`),
the wire-compatible challenge enums/models (`ChallengeMethod`, `ChallengeVerifyError`), and the
inline-feedback surface (`IInlineFeedback`). The fragment-handoff JS is extended to stage the
`LinkRequired` outcome (it currently only stages a `token`). On confirm success the returned
access/refresh tokens are fed into the **existing** session-establishment path
(`ITokenCache` + `CustomAuthenticationStateProvider`), identical to a normal social sign-in. The
authenticated `AuthChallengeDialog` and all Feature 150 account-security components are **left
untouched**.

## Technical Context

**Language/Version**: C# 14 / .NET 10; Blazor WebAssembly (web client) + JS ES modules

**Primary Dependencies**: MudBlazor (UI), `Microsoft.JSInterop` (WebAuthn + fragment interop),
existing `PasskeyInteropService`, `IInlineFeedback`, `ITokenCache`,
`CustomAuthenticationStateProvider`, `HttpClient` (`AddCoreServices` base address). Server contract
provided by **Feature 168** (Tenant Service).

**Storage**: N/A on the client. Tokens transit via the existing volatile staging
(`window.__sorcha_fragment_token` + `localStorage['sorcha:fragment-pending']`) and `ITokenCache`.

**Testing**: xUnit + FluentAssertions + Moq for client-service/unit logic; Playwright (Docker test
infra, see `sorcha-ui` skill) for the end-to-end web prompt journeys (passkey, TOTP, fail-closed).

**Target Platform**: Browser (Blazor WASM) under the `/app` web host (`Sorcha.UI.Web` +
`Sorcha.UI.Web.Client`); shared machinery in `Sorcha.UI.Components.User`.

**Project Type**: Web application (Blazor WASM front-end consuming an existing REST contract).

**Performance Goals**: Happy path (continue → passkey → done) under 60s and ≤3 interactions
(SC-001). No additional network round-trips beyond initiate/verify/confirm.

**Constraints**: Fail-closed on every error path (SC-004); link-pending token never persists in
address bar/history after capture (SC-005, FR-002); no edits to Feature 150/116 security component
files (SC-007, FR-013); no `ISnackbar` — inline feedback only (FR-019, Critical Pattern #12);
anonymous surface strictly separate from `AuthChallengeDialog` (FR-012).

**Scale/Scope**: One new prompt component, one new anonymous client service + models, one JS
extension, one boot-time gate, DI registration, and tests. Web surface only — the `/wallet` PWA
prompt is tracked separately.

### Dependency status (prerequisite risk)

> **Feature 168 server endpoints are NOT present in this worktree.** A repo-wide search for
> `LinkRequired` / `linkPendingToken` / `SocialLinkStepUpEndpoints` returns nothing on this branch.
> The spec (Assumptions) treats F168 as an available backend dependency; this feature is purely the
> consumer. The contract this plan codes against is captured in [`contracts/`](./contracts/). **F168
> must be merged/available before the integration (Playwright) tests can pass.** Unit tests for the
> client service mock the HTTP boundary and do not require F168. This is a sequencing risk, not an
> ambiguity in this feature's scope.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment |
|-----------|------------|
| I. Microservices-First | PASS — UI-only consumer; no new service, no cross-service coupling added. Depends downward on the Tenant Service REST contract only. |
| II. Security First | PASS — fail-closed by design; token stripped from URL/history; all token-expiry, single-use, proof-tier, account-match, conflict, and rate-limit policy enforced server-side (F168); client surfaces non-leaky states only. No secrets stored. |
| III. API Documentation | N/A (no new server endpoints). XML docs required on all new public client types/members (Code Quality / build-warning policy). |
| IV. Testing | PASS — xUnit unit tests for the anonymous client service + state logic (>85% target); Playwright E2E for US1/US2/US3 journeys. |
| V. Code Quality | PASS — nullable enabled, async I/O, DI, no warnings; SPDX license headers on new files. |
| VI. Blueprint Standards | N/A — no blueprints. |
| VII. Domain-Driven Design | PASS — reuses ubiquitous terms; no new domain language introduced. |
| VIII. Observability | PASS (client scope) — failures surfaced via `IInlineFeedback`; server emits F168 metrics. No new server telemetry needed. |

**Cross-cutting pattern gates (CLAUDE.md):** #12 Notification Routing — inline feedback only, no
`ISnackbar` (FR-019). Reuse over duplication — consume existing passkey/TOTP machinery, do not fork
(FR-014). No re-consolidation of F150 components (FR-013).

**Result: PASS — no violations. Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/173-web-step-up-account-linking/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (UI-consumed F168 contract)
│   ├── social-link-stepup-endpoints.md
│   └── fragment-and-session.md
├── checklists/          # pre-existing
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.UI/
├── Sorcha.UI.Components.User/                 # shared user-facing machinery (reused + new, isolated)
│   ├── Components/
│   │   ├── Security/
│   │   │   └── AuthChallengeDialog.razor      # REUSE-AS-REFERENCE ONLY — DO NOT MODIFY (authenticated)
│   │   └── AccountLink/                        # NEW folder (anonymous surface, isolated from F150)
│   │       └── LinkExistingAccountPrompt.razor # NEW — the anonymous step-up prompt
│   ├── Services/
│   │   ├── User/
│   │   │   ├── PasskeyInteropService.cs        # REUSE (GetCredentialAsync)
│   │   │   └── AnonymousSocialLinkClientService.cs # NEW — calls the 3 F168 endpoints
│   │   └── Shared/
│   │       ├── Feedback/IInlineFeedback.cs     # REUSE
│   │       └── Authentication/CustomAuthenticationStateProvider.cs # REUSE (session establish)
│   └── Models/User/Authentication/
│       ├── AuthChallengeModels.cs              # REUSE (ChallengeMethod, ChallengeVerifyError)
│       └── AnonymousSocialLinkModels.cs        # NEW — link-flow request/response records + state
└── Sorcha.UI.Web.Client/
    ├── Routes.razor                            # EDIT — mount the LinkRequired boot gate
    ├── Components/
    │   ├── FragmentTokenHandler.razor          # REUSE-AS-REFERENCE (returnUrl handling)
    │   └── AccountLink/LinkRequiredGate.razor  # NEW — boot-time detection + prompt host
    └── wwwroot/js/webauthn.js                  # REUSE (getCredential)

src/Apps/Sorcha.UI/Sorcha.UI.Web/
└── wwwroot/app/js/fragment-handoff.js          # EDIT — stage + clear LinkRequired outcome

tests/
├── Sorcha.UI.Components.User.Tests/ (or nearest existing UI test project)
│   └── AccountLink/AnonymousSocialLinkClientServiceTests.cs   # NEW unit tests
└── <Playwright E2E project per sorcha-ui skill>
    └── LinkExistingAccountPromptTests.*         # NEW E2E (US1/US2/US3)
```

**Structure Decision**: Web application. The reusable prompt + anonymous client service live in
`Sorcha.UI.Components.User` (so they can consume `PasskeyInteropService`, the challenge models, and
`IInlineFeedback` already shipped there) in a **new `AccountLink` folder**, keeping them physically
separate from the Feature 150 `Security/` components and the authenticated `AuthChallengeDialog`. The
web-host-only wiring (fragment staging, boot-time detection gate, route mount) lives in
`Sorcha.UI.Web` / `Sorcha.UI.Web.Client`. The `/wallet` PWA could later reference the same prompt,
but PWA wiring is explicitly out of scope here.

## Complexity Tracking

> No constitution violations — section intentionally empty.
