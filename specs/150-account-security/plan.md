# Implementation Plan: Unified Account Security Surface

**Branch**: `150-account-security` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/150-account-security/spec.md`

**Authoritative design**: `docs/superpowers/specs/2026-06-10-unified-account-security-design.md` (approved; source of truth for architecture, the assurance/floor policy, the channel abstraction, data model, endpoint surface, and the 4-phase delivery plan).

## Summary

Consolidate the fragmented Feature-116 auth-method management into a single, discoverable **Security** home — surfaced in the user profile menu between *My Profile* and *My Devices*, built once as a shared `Sorcha.UI.Components.User` component, and rendered verbatim on the web app (`/app`) and the citizen wallet PWA (`/wallet`). Add **Email OTP** and **SMS OTP** as honestly-labelled **Basic** second factors alongside TOTP (Strong) and passkeys (Strongest), kept safe by a server-authoritative **assurance-aware floor rule** (a lower-tier proof can never authorise a destructive/downgrade change to a higher-tier method) plus **always-notify** on every change. SMS is **config-gated** via an `ISmsSender` seam (dormant until a provider is configured); Email OTP routes through the existing F112 transactional-email pipeline. Finish the stubbed Passkey + Re-OAuth step-up proofs. Delivery is four independently-shippable increments mapped 1:1 to the four user stories, **Phase 1 (US1) being the standalone MVP**.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs (Tenant Service), Blazor WebAssembly (`Sorcha.UI.Web.Client` web SPA + `Sorcha.Wallet.Pwa` PWA), MudBlazor, EF Core + Npgsql (Tenant Postgres), StackExchange.Redis (server-sent OTP state), Scriban (F112 templates), MailKit / Azure Communication Services email (existing `IEmailSender`), **new `ISmsSender`** (ACS SMS, config-gated), Fido2 (passkeys), SignalR (F118 notifications). `Sorcha.Cryptography` for any code hashing.

**Storage**: PostgreSQL (Tenant DB — `PlatformUser` phone columns + new `PlatformUserTwoFactor` flags; **squashed into the existing initial migration** — pre-release policy, NO incremental migrations; regenerate `InitialCreate` so Designer + ModelSnapshot stay in lockstep). Redis (server-sent OTP challenge state — single-use via GETDEL, TTL'd; **no migration at all**, cache-style store registered through F113 `IStorageRegistrationLog` but **not** on the fail-fast audited list).

**Testing**: xUnit + FluentAssertions + Moq (unit/integration, Tenant Service — SQLite in-memory where `ExecuteUpdateAsync` is needed, per the F116 pattern), bUnit (shared Security components), Playwright/NUnit (`Sorcha.UI.E2E.Tests`, per the `sorcha-ui` skill, web + PWA), F112 snapshot fixtures (`UPDATE_EMAIL_FIXTURES`) for new email templates.

**Target Platform**: Linux containers (Tenant Service behind the YARP gateway); Blazor WASM in browsers + installed PWA.

**Project Type**: Web — multi-service .NET solution; backend changes localised to the Tenant Service, frontend to the shared UI component library + two host apps.

**Performance Goals**: Security-home aggregate read renders without perceptible delay; OTP verify is a single Redis round-trip; this is a low-throughput, correctness-critical surface (not a hot path). Code-send latency bounded by the email/SMS provider.

**Constraints**: All authorization, floor-rule, and last-method decisions **server-authoritative** (FR-008/FR-010). New REST endpoints follow the `/me/*` cross-tier `.RequireAuthorization()` convention (F136) so one surface serves both consumer and platform tokens. In-app navigation **base-relative** on each host (`/app/security`, `/wallet/security`) — never origin-absolute. No string-interpolated email HTML (F112). No `ISnackbar` (CLAUDE.md #12). No hard-coded `<Version>` (unified versioning).

**Scale/Scope**: Every platform user and citizen; security operations are infrequent per user. Scope is bounded to the 4 user stories; out-of-scope items are recorded in the spec.

## Constitution Check

*GATE: must pass before Phase 0. Re-checked after Phase 1 design (below).*

| Principle | Assessment | Verdict |
|-----------|------------|---------|
| Microservices boundaries | All backend changes localised to the **Tenant Service** (it already owns identity, auth, TOTP, email). No new service; no new cross-service coupling. F118 notification + F112 email are existing in-process Tenant capabilities. | ✅ Pass |
| Service communication (gRPC internal / REST external) | No new internal service-to-service calls. UI → Tenant is external client-facing REST via the gateway (permitted). | ✅ Pass |
| Zero-trust / server-side enforcement | Floor rule, last-method floor, and OTP verification all enforced server-side; client only reflects server-issued `CanRemove` / `RequiredProofTier`. | ✅ Pass |
| Cryptographic standards / data at rest | OTP codes stored **hashed** (never plaintext) with short TTL. Phone number is PII stored E.164 (consistent with email storage); flagged in research for optional column encryption follow-up. No mnemonics. | ✅ Pass |
| Identity & access (JWT, multi-tenant) | Reuses JWT bearer; new endpoints are cross-tier `/me/*` (F136). Tier safety preserved by the identical floor rule across hosts. | ✅ Pass |
| Test coverage (≥80% core libs; xUnit; integration; E2E) | Floor-rule policy gets exhaustive matrix unit tests; OTP service unit-tested; bUnit for components; Playwright E2E web + PWA. | ✅ Pass |
| API documentation (Scalar, not Swagger; OpenAPI; XML) | New Minimal-API endpoints get `.WithSummary()` / `.WithDescription()` + XML docs; contract published in `contracts/`. | ✅ Pass |
| Notification routing (no `ISnackbar`; `IInlineFeedback` + inbox) | UI uses `IInlineFeedback` for own-action feedback; always-notify uses the F118 inbox writer; dialog errors use inline `MudAlert`. | ✅ Pass |
| Unified versioning (no hard-coded `<Version>`) | No project version edits; new files inherit root `Directory.Build.props`. | ✅ Pass |
| License headers (SPDX MIT) | All new source files carry the SPDX/Copyright header. | ✅ Pass |
| AI-code documentation policy | README + `sorcha-architecture` skill + `docs/` updated as part of completion (tracked as a task per phase). | ✅ Pass |

**Result: PASS — no violations, Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/150-account-security/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions + rationale
├── data-model.md        # Phase 1 — entities, assurance map, floor policy
├── quickstart.md        # Phase 1 — per-phase manual validation
├── contracts/
│   ├── account-security.openapi.yaml   # new/extended REST endpoints
│   └── floor-rule-policy.md            # assurance × operation authorization table
├── checklists/
│   └── requirements.md  # spec quality gate (from /speckit.specify)
└── tasks.md             # Phase 2 — /speckit.tasks output (NOT created here)
```

### Source Code (repository root — real directories touched)

```text
src/Services/Sorcha.Tenant.Service/
├── Endpoints/
│   ├── AuthMethodsEndpoints.cs          # extend aggregate: RequiredProofTier per row
│   ├── AuthChallengeEndpoints.cs        # extend: EmailOtp/SmsOtp rungs; finish Passkey/ReOAuth
│   └── TwoFactorChannelEndpoints.cs     # NEW: /api/me/2fa/{email,sms}/* enable/verify/disable
├── Services/
│   ├── Auth/
│   │   ├── IAuthMethodService.cs        # extend: assurance-aware CanRemove + RequiredProofTier
│   │   ├── AssurancePolicy.cs           # NEW: static tier map + floor-rule policy (single source)
│   │   ├── IVerificationChannel.cs      # NEW: channel abstraction (Kind, Tier, Initiate, Verify)
│   │   ├── VerificationChannelRegistry.cs   # NEW: registry; SMS registered only if configured
│   │   ├── ServerSentOtpService.cs      # NEW: generate/hash/store/send/verify (email+sms)
│   │   └── SecurityChangeNotifier.cs    # NEW: always-notify (F118 inbox + F112 email), try/log/swallow
│   ├── Sms/
│   │   ├── ISmsSender.cs                # NEW: config-gated seam (mirror IEmailSender selection)
│   │   └── AcsSmsSender.cs              # NEW: ACS SMS impl (active only when configured)
│   └── *Email* / *Welcome* (F112)       # extend: TwoFactorCodeDispatch + security-change dispatch
├── Emails/Templates/
│   ├── twofactor-code.{html,txt}        # NEW Scriban template (Sorcha-branded)
│   └── security-change.{html,txt}       # NEW Scriban template (Sorcha-branded)
├── Models/
│   ├── PlatformUser.cs                  # + PhoneNumber (E.164), PhoneVerifiedAt
│   └── PlatformUserTwoFactor.cs         # NEW 1:1: TotpEnabled (move/keep), EmailOtpEnabled, SmsOtpEnabled
└── Migrations/                          # SQUASH into existing initial migration (pre-release) — phone columns + two-factor table

src/Apps/Sorcha.UI/Sorcha.UI.Components.User/
├── Components/Security/
│   ├── SecurityHome.razor               # NEW job-based shell (mounted by both hosts)
│   ├── SignInMethodsSection.razor       # RELOCATED from Web.Client/.../AuthMethods/
│   ├── TwoFactorSection.razor           # NEW: TOTP + Email OTP + SMS OTP
│   ├── RecoverySection.razor            # NEW: backup codes
│   ├── AssuranceBadge.razor             # NEW: Strongest/Strong/Basic chip
│   └── AuthChallengeDialog.razor        # RELOCATED; finish Passkey + ReOAuth proofs
└── Services/Shared/
    └── AuthMethodsClientService.cs      # extend: 2FA channel enable/verify/disable methods

src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Shared/
└── UserProfileMenu.razor                # + "Security" item between My Profile and My Devices

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/
├── Pages/Security.razor                 # NEW @page "/security" host → <SecurityHome/>
└── Components/Settings/...               # RETIRE Accounts + Security tabs; redirect deep-links

src/Apps/Sorcha.Wallet.Pwa/
├── Pages/Security.razor                 # NEW @page "/security" host → <SecurityHome/>
└── (nav entry)                          # add Security entry to PWA nav

tests/
├── Sorcha.Tenant.Service.Tests/         # AssurancePolicy matrix, OTP service, channel registry, endpoints, email snapshots
├── Sorcha.UI.Core.Tests/                # bUnit: SecurityHome groups/badges/CanRemove gating, dialog rungs
└── Sorcha.UI.E2E.Tests/                 # Playwright: web /app/security + PWA /wallet/security
```

**Structure Decision**: No new project. Backend extension is confined to `Sorcha.Tenant.Service`; the UI is a shared component in `Sorcha.UI.Components.User` consumed by both hosts (F122), with the menu entry in `Sorcha.UI.Core`. This honours the microservices-boundary and shared-component conventions without introducing new services or libraries.

## Phase notes (delivery = the 4 user stories)

- **Phase 1 / US1 (P1) — MVP**: relocate the three sign-in sections + dialog into the shared library; build `SecurityHome` (job-based) + `AssuranceBadge`; add the menu entry + `/security` host pages on **both** hosts; retire the Settings tabs (redirect deep-links); implement `AssurancePolicy` (tier map + floor rule) and widen `IAuthMethodService` to emit `RequiredProofTier`; finish Passkey + Re-OAuth step-up proofs; add `SecurityChangeNotifier` always-notify. **No new channels.** Ships a coherent, discoverable, safe surface on web + PWA.
- **Phase 2 / US2 (P2) — Email OTP**: `IVerificationChannel` + registry + `ServerSentOtpService`; email channel via F112 `TwoFactorCodeDispatch` + `twofactor-code` template + snapshot fixtures; `/api/me/2fa/email/*`; login-2FA + step-up integration as Basic; rate-limits.
- **Phase 3 / US3 (P3) — SMS OTP (config-gated)**: `ISmsSender` + `AcsSmsSender`; phone capture + verify on `PlatformUser`; `/api/me/2fa/sms/*`; registry hides SMS when unconfigured; per-number send/cost guard.
- **Phase 4 / US4 (P4) — PWA parity**: mount `<SecurityHome/>` in the PWA host + nav; validate social-link OAuth round-trip inside the PWA; keep Passkeys distinct from My Devices; web + PWA E2E.

> Phase 1 establishes the shared component, so Phases 2–4 extend it in place. US4's PWA host is thin because the component is already shared — most of its work is the social-link round-trip validation and the My-Devices distinction.

## Complexity Tracking

*No constitution violations — section intentionally empty.*
