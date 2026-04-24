# Quickstart — Feature 112: Transactional Email & Verification Sweep

**Audience**: a Sorcha engineer picking up this feature (implementer, reviewer, or someone revisiting it six months later).

This quickstart explains how to run the end-to-end verification/welcome/invitation/reset flows locally, inspect the emails that get sent, and run the test suite. It does **not** explain the template authoring contract — see `contracts/email-templates.md` for that.

---

## Prerequisites

- .NET 10 SDK installed
- Docker Desktop running (for Postgres, Redis, and — when configured — a local SMTP catcher)
- Ability to run PowerShell or Bash
- A freshly-cloned working tree on branch `112-email-sweep`

---

## Running the Tenant Service locally

The fastest end-to-end loop uses `docker-compose up` plus the Tenant Service test project. For interactive debugging with breakpoints, use Aspire.

### Option A — Full stack via docker-compose

```bash
docker-compose up -d
```

The Tenant Service ships with SMTP settings that default to `localhost:587` when `Email:AcsConnectionString` is unset. Without a catcher configured, email sends will fail at the SMTP connect step. Two practical paths:

**Path A1 — point at a local Mailpit** *(recommended during development)*:

Add to `docker-compose.override.yml`:
```yaml
services:
  mailpit:
    image: axllent/mailpit:latest
    container_name: sorcha-mailpit
    ports:
      - "1025:1025"    # SMTP
      - "8025:8025"    # Web UI
```

Then in `tenant-service` service environment:
```yaml
Email__SmtpHost: mailpit
Email__SmtpPort: "1025"
Email__UseSsl: "false"
```

All emails land in the Mailpit web UI at http://localhost:8025. *(Note: Mailpit integration is listed as a recommended follow-up in the design doc; ship this spec without it if time is short.)*

**Path A2 — rely on logs only**:

Leave SMTP configured to `localhost:587`. Sends will throw; the log entry records the recipient, subject, and that the send failed. Not great for UI validation but fine for flow verification.

### Option B — Aspire with breakpoints

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

Tenant Service is available on its Aspire-assigned HTTPS port. Same email backend caveats as Option A.

---

## Triggering each email flow

### 1. Verification email (User Story 1)

```bash
# From a terminal:
curl -X POST http://localhost:80/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"stuart+verify@sorcha.io","password":"Correct-horse-battery-staple-9","displayName":"Stuart Fraser"}'
```

Expected: one email arrives for `stuart+verify@sorcha.io` titled `Confirm your email`, with a `Confirm my email` button linking to `/auth/verify-email?token=…`. The plaintext body contains the same link.

Clicking the link or posting to `/api/auth/verify-email?token=…` should mark the user verified AND fire the welcome email in under a minute.

### 2. Welcome email, public variant (User Story 3)

Fires automatically after step 1 completes (user verified in the public org). Expected: one email titled `Welcome to Sorcha, Stuart 👋` with the recovery-phrase advance-warning section. Inspect the content against `contracts/email-templates.md § 5`.

### 3. Welcome email, invited variant (User Story 3, alternative path)

Requires a branded test organisation. Seed one via the admin UI or directly:

```bash
# Assuming a SystemAdmin bearer token in $TOKEN:
curl -X POST http://localhost:80/api/platform/organizations \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"Acme Verification Co.","subdomain":"acme","branding":{"logoUrl":"https://…/acme-logo.png","primaryColor":"#FF5722","companyTagline":"Verify with confidence"}}'
```

Then invite a fresh email:
```bash
curl -X POST http://localhost:80/api/organizations/{orgId}/invitations \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"email":"stuart+invited@sorcha.io","role":"Consumer","expiryDays":7}'
```

Expected: invitation email branded with Acme logo + orange primary colour. Accept the invitation, then sign in once. The welcome email that arrives after first sign-in is the **invited** variant and carries Acme branding, not Sorcha's, and does NOT include the recovery-phrase section.

### 4. Invitation email (User Story 2)

Covered by the step above — the invitation email is sent by the `POST /api/organizations/{orgId}/invitations` call.

### 5. Password reset email (User Story 4)

```bash
curl -X POST http://localhost:80/api/auth/password-reset \
  -H "Content-Type: application/json" \
  -d '{"email":"stuart+verify@sorcha.io"}'
```

Expected: email titled `Reset your password` with a `Reset password` button. Visual treatment identical to the verification email.

---

## Running the test suite

### All Tenant Service tests

```bash
dotnet test tests/Sorcha.Tenant.Service.Tests/Sorcha.Tenant.Service.Tests.csproj
```

### Just the new email surfaces

```bash
dotnet test tests/Sorcha.Tenant.Service.Tests/Sorcha.Tenant.Service.Tests.csproj \
  --filter "FullyQualifiedName~EmailTemplate|FullyQualifiedName~EmailBranding|FullyQualifiedName~TransactionalEmail|FullyQualifiedName~WelcomeEmailDispatcher"
```

### Template snapshot regeneration

When a template copy change is intentional and the snapshot test fails:

1. Inspect the diff to confirm the change matches what you meant.
2. Set the test environment variable `UPDATE_EMAIL_FIXTURES=1` and re-run:
   ```bash
   UPDATE_EMAIL_FIXTURES=1 dotnet test tests/Sorcha.Tenant.Service.Tests/Sorcha.Tenant.Service.Tests.csproj \
     --filter "FullyQualifiedName~ScribanEmailTemplateRendererTests"
   ```
3. Fixtures under `tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/` are overwritten.
4. Commit the fixture changes in the same commit as the template change so reviewers see both.

---

## Where to look when something's wrong

| Symptom | First place to check |
|---------|----------------------|
| Template doesn't parse at startup — service throws | `Emails/Templates/<name>.html` — Scriban parse error details in the startup log |
| Email has raw token in body | The caller is still using `IEmailSender.SendAsync` directly instead of `ITransactionalEmailService` |
| Welcome email arrives twice | `WelcomeEmailDispatcher.SendIfPendingAsync` was not awaited, or `WelcomeSentAt` was never persisted — check the integration test that uses the real DB context |
| Invitation email shows Sorcha branding instead of org branding | `Organization.Branding` is null on the inviting org, OR `InviteEmailDispatch.InvitingOrganization` was not populated by the caller |
| Plaintext body is empty | The caller hit `IEmailSender.SendAsync` with an empty string — the renderer always returns a non-empty plaintext pair, so the bug is upstream |
| Email deliverability issues on a real domain | Unrelated to this feature — check SPF / DKIM / DMARC on the sending domain |

---

## Acceptance-scenario smoke walk (~10 minutes)

Maps to the spec's user stories for a full-feature smoke after any meaningful change to this code path:

1. Sign up a new email+password user → verification email arrives with branded Confirm button → click link → verification succeeds AND welcome (public) arrives within 60 seconds with recovery-phrase section.
2. Create a branded org, invite a fresh email → invitation email arrives with org logo + colour + org name + role → accept → sign in once → welcome (invited) arrives within 60 seconds, no recovery-phrase section.
3. Request a password reset for an existing user → reset email arrives with the same visual frame as the verification email.
4. Sign in again for any already-welcomed user → no duplicate welcome email.

If all four pass, the feature is behaviourally sound. The snapshot tests protect against accidental template drift.
