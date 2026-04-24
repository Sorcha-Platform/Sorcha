# Implementation Plan: Transactional Email & Verification Sweep

**Branch**: `112-email-sweep` | **Date**: 2026-04-24 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/112-email-sweep/spec.md`
**Design doc**: [`docs/superpowers/specs/2026-04-24-email-sweep-design.md`](../../docs/superpowers/specs/2026-04-24-email-sweep-design.md)

## Summary

Unify the Tenant Service's four transactional email flows (verification, invitation, password reset, welcome) on a single template-backed dispatch path. Fix two latent plaintext-token bugs (verification and invitation currently send the raw token in a plaintext body with no link). Introduce a one-shot welcome email with two variants: a public variant that primes users for the recovery-phrase moment they will encounter on first wallet creation, and an invited variant that carries the inviting organisation's name and branding. Per-org branding (name + logo + primary colour) lifts straight from the existing `Organization.Branding` record — no new schema. Scriban 5.x renders twelve embedded templates (six HTML + six plaintext) against strongly-typed models.

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: Scriban 5.12 (new, single dependency), existing MailKit 4.x, existing Azure.Communication.Email, existing EF Core 10 with Npgsql
**Storage**: PostgreSQL via EF Core (Tenant Service DB). One new nullable column `WelcomeSentAt` on the `PlatformUsers` table, folded into the existing `20260408160910_InitialCreate` migration (pre-release convention).
**Testing**: xUnit + FluentAssertions + Moq (existing stack). Snapshot-style assertions against committed golden HTML/text fixtures for each template.
**Target Platform**: Linux containers (Docker Compose) and .NET Aspire dev. Service-internal functionality — no new HTTP surface.
**Project Type**: single microservice change (Sorcha.Tenant.Service + its test project)
**Performance Goals**: Template rendering <10ms per email (templates pre-compiled at startup). Welcome dispatch <1s per user at the trigger moment (bounded by SMTP/ACS send, which is unchanged).
**Constraints**: No disk I/O on the hot path — templates are embedded resources parsed once at startup. No secret material in email bodies. No log lines above DEBUG that could leak tokens. Multipart HTML+plaintext on every message. Must render correctly in Gmail web/mobile, Outlook web/desktop, and Apple Mail without external CSS or webfonts.
**Scale/Scope**: <10k transactional emails/day at steady state. 4 new service classes + 1 interface tightening + 5 modified callers + 12 template files + 1 DB column.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Notes |
|-----------|------------|-------|
| I. Microservices-First | PASS | All changes confined to Sorcha.Tenant.Service. No cross-service coupling added. No new upward dependencies. |
| II. Security First | PASS | FR-021 forbids secret material in logs above DEBUG. Existing token generation and validation unchanged. No new auth surfaces. No new data encrypted-at-rest requirements (tokens already short-lived, URL-safe). |
| III. API Documentation | N/A | Feature adds no new HTTP endpoints. Internal interfaces get XML documentation per Principle V. |
| IV. Testing Requirements | PASS | Four new unit-test suites (renderer, branding resolver, dispatcher, facade) plus template snapshot tests plus updated integration tests. Target >85% on new code. Existing `InvitationServiceTests`, `PasswordResetServiceTests` are updated in place. |
| V. Code Quality | PASS | C# 14 / .NET 10. Async/await preserved. DI for every new service. Nullable reference types on. Zero new compiler warnings expected. |
| VI. Blueprint Creation Standards | N/A | Not a blueprint feature. |
| VII. Domain-Driven Design | PASS | Uses "Platform User" not "user", "Organisation" not "tenant" throughout. No term drift. |
| VIII. Observability by Default | PASS | All send failures logged via existing Serilog structured logs. Optional (non-blocking) `email_send_failures_total{template}` counter for dashboarding. |

**Gate: PASS — no violations. No Complexity Tracking entries required.**

## Project Structure

### Documentation (this feature)

```text
specs/112-email-sweep/
├── plan.md              # This file (/speckit.plan output)
├── spec.md              # Feature spec (already written)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output — template model contracts + internal interface surfaces
│   ├── email-templates.md
│   └── internal-interfaces.md
├── checklists/
│   └── requirements.md  # Already written — quality checklist passed
└── tasks.md             # Phase 2 output (/speckit.tasks — not created by this command)
```

### Source Code (repository root)

```text
src/Services/Sorcha.Tenant.Service/
├── Emails/                                  # NEW — embedded-resource template root
│   └── Templates/
│       ├── base.html            base.txt
│       ├── verify.html          verify.txt
│       ├── invite.html          invite.txt
│       ├── reset.html           reset.txt
│       ├── welcome-public.html  welcome-public.txt
│       └── welcome-invited.html welcome-invited.txt
├── Services/
│   ├── IEmailSender.cs                      # MODIFIED — tightened to one SendAsync
│   ├── SmtpEmailSender.cs                   # MODIFIED — add multipart text
│   ├── AcsEmailSender.cs                    # MODIFIED — add multipart text
│   ├── IEmailTemplateRenderer.cs            # NEW
│   ├── ScribanEmailTemplateRenderer.cs      # NEW
│   ├── IEmailBrandingResolver.cs            # NEW
│   ├── EmailBrandingResolver.cs             # NEW
│   ├── ITransactionalEmailService.cs        # NEW — facade
│   ├── TransactionalEmailService.cs         # NEW
│   ├── WelcomeEmailDispatcher.cs            # NEW — one-shot idempotent
│   ├── EmailVerificationService.cs          # MODIFIED — use facade + trigger welcome
│   ├── InvitationService.cs                 # MODIFIED — use facade, load org branding
│   ├── PasswordResetService.cs              # MODIFIED — use facade, delete inline HTML
│   ├── LoginService.cs                      # MODIFIED — welcome trigger on first login
│   └── SocialLoginService.cs                # MODIFIED — welcome trigger on first login
├── Extensions/
│   └── ServiceCollectionExtensions.cs       # MODIFIED — DI for new services
├── Migrations/
│   ├── 20260408160910_InitialCreate.cs      # MODIFIED — add WelcomeSentAt column
│   └── 20260408160910_InitialCreate.Designer.cs  # MODIFIED — regenerated snapshot
└── Models/
    └── PlatformUser.cs                      # MODIFIED — WelcomeSentAt property

tests/Sorcha.Tenant.Service.Tests/
├── Services/
│   ├── ScribanEmailTemplateRendererTests.cs     # NEW
│   ├── EmailBrandingResolverTests.cs            # NEW
│   ├── TransactionalEmailServiceTests.cs        # NEW
│   ├── WelcomeEmailDispatcherTests.cs           # NEW
│   ├── EmailVerificationServiceTests.cs         # NEW or updated
│   ├── InvitationServiceTests.cs                # MODIFIED — assert link, not token
│   └── PasswordResetServiceTests.cs             # MODIFIED — assert shared layout
└── Fixtures/
    └── Emails/                                  # NEW — golden template fixtures
        ├── verify.html  verify.txt
        ├── invite.html  invite.txt
        ├── reset.html   reset.txt
        └── welcome-public.html  welcome-public.txt  welcome-invited.html  welcome-invited.txt

Directory.Packages.props                          # MODIFIED — Scriban 5.12.0
src/Services/Sorcha.Tenant.Service/Sorcha.Tenant.Service.csproj  # MODIFIED — reference Scriban
```

**Structure Decision**: Single-service change inside the existing `src/Services/Sorcha.Tenant.Service/` project tree. A new `Emails/Templates/` subfolder holds embedded-resource templates next to the service that sends them. No new projects, no new test projects. The existing `Sorcha.Tenant.Service.Tests` project gains four new test classes and updates three existing ones. Templates are shipped as embedded `.html` and `.txt` resources so they travel with the service image with zero deployment ceremony.

## Complexity Tracking

*No constitution violations — no entries required.*
