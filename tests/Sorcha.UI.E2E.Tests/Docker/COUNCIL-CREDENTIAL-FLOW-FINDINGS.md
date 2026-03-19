# Council Digital Credential E2E Flow — Test Findings

**Test:** `CouncilCredentialFlowTests.cs` | **Date:** 2026-03-19 | **Iterations:** 18

## Summary

Long-running E2E integration test exercising a civic scenario: citizen obtains a council digital ID credential, then uses it to request a council service. Run against Docker stack with fresh volumes.

**Setup phase:** Fully functional after fixes (org creation, user provisioning, wallets, participants, blueprints)
**Test 1:** PASSED — citizen registration, wallet, participant
**Tests 2-9:** Blocked at instance creation (register not fully provisioned in Docker; register creation needs attestation signing which requires the full walkthrough bootstrap flow)

---

## Platform Bugs Found & Fixed

### FIX-1: OrgProvisioningService NpgsqlRetryingExecutionStrategy Crash
- **File:** `src/Services/Sorcha.Tenant.Service/Services/OrgProvisioningService.cs`
- **Bug:** `BeginTransactionAsync()` inside `AdminProvisionAsync` is incompatible with `NpgsqlRetryingExecutionStrategy`
- **Fix:** Wrapped transaction in `CreateExecutionStrategy().ExecuteAsync()`
- **Impact:** All org creation via platform admin API was broken (500)

### FIX-2: AuditLogEntry JSONB Serialization Crash
- **File:** `src/Services/Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs`
- **Bug:** `Dictionary<string, object>` → JSONB column fails without `EnableDynamicJson()` (Npgsql 8+ requirement)
- **Fix:** Built `NpgsqlDataSourceBuilder` with `EnableDynamicJson()` before passing to EF Core
- **Impact:** Any operation writing audit logs crashed (500)

### FIX-3: Configurable Lockout Thresholds
- **File:** `src/Services/Sorcha.Tenant.Service/Services/PlatformUserService.cs`
- **Bug:** Progressive lockout thresholds were hardcoded (5/10/15/20/25 attempts). During development and testing, automated API calls quickly trigger lockout with no recovery path short of DB manipulation + service restart.
- **Fix:** New `LockoutConfig` class reads from `Security:Lockout` config section. Production defaults unchanged. Development/Docker config uses relaxed thresholds (50/100/200/500/1000).
- **Config files:** `appsettings.Development.json`, `docker-compose.yml`

---

## Platform Issues Catalogued (Not Yet Fixed)

### ISSUE-5: Invitation Email Crash (No SMTP in Docker)
- **Component:** `OrgProvisioningService` → `InvitationService` → `SmtpEmailSender`
- **Severity:** Major
- **Problem:** When creating an org with an admin email that doesn't match an existing PlatformUser, the provisioner creates an invitation and tries to send an email. In Docker (no SMTP server), this throws `SocketException: Connection refused` and the entire org creation fails with 500.
- **Workaround (test):** Pre-register the admin user before org creation so the provisioner finds them and skips the invitation path.
- **Recommended fix:** `SmtpEmailSender.SendAsync` should catch connection failures gracefully — log the error, mark the invitation as "email pending", and return success. The invitation record should still be created so it can be resent or accepted via a direct link. A crash here is disproportionate to the failure.

### ISSUE-6: Self-Registration Requires Email Verification (No SMTP)
- **Component:** `RegistrationService` + `LoginService`
- **Severity:** Major
- **Problem:** Self-registered users have `EmailVerified = false`. The login service rejects unverified accounts. In Docker there's no SMTP to deliver verification emails, so self-registered users can never log in.
- **Workaround (test):** Auto-verify via direct PostgreSQL UPDATE after registration.
- **Recommended fix (short-term):** Add a Development-only config flag `Seed:AutoVerifyEmails = true` that skips email verification for new registrations. Or add an admin endpoint `POST /api/organizations/{orgId}/users/{userId}/verify-email` for manual approval.
- **Recommended fix (long-term):** Administrative approval flow — when a user self-registers, an org admin can approve them (which also verifies the email), rather than requiring email round-trip. This is a better UX for council scenarios where the admin knows who should have access.

### ISSUE-7: Signup Page UI — "Registration failed"
- **Component:** Signup Razor Page (`/auth/signup`)
- **Severity:** Major
- **Problem:** Email tab signup submits but returns "Registration failed." No detailed error. Likely caused by password policy HIBP check failing (no outbound internet in Docker) or email sending failure during registration.
- **Details:** The Passkey tab is shown by default. Email tab correctly activates via `[data-tab='email']` click. Form fills correctly. POST returns the generic error.
- **Recommended fix:** Return specific error messages from the registration endpoint. If HIBP check fails due to network, fall back gracefully (log warning, skip check in Development).

### ISSUE-8: Citizen UI Login — Stuck on Login Page
- **Component:** Login Razor Page (`/auth/login`)
- **Severity:** Major
- **Problem:** After API registration + email verification, the citizen can obtain a JWT via service-auth API, but the Razor login page redirects back to itself. The citizen is registered in the public org; the login page may be showing an org selection flow that the test doesn't handle.
- **URL pattern:** `http://localhost/auth/login?returnUrl=http%3A%2F%2Flocalhost%2Fapp%2F`
- **Recommended fix:** Investigate whether single-org users should skip org selection. The `LoginAsUserAsync` in the test's `MultiUserTestBase` needs to handle the org selection step if required.

### ISSUE-9: Cross-Org Participant Registration
- **Component:** Participant Identity API
- **Severity:** Major
- **Problem:** The citizen registers in the public org but needs to participate in a council org's blueprint. The self-registration endpoint (`/me/organizations/{orgId}/self-register`) validates that the user belongs to the target org. Cross-org participation isn't supported via self-registration.
- **Recommended fix:** Either (a) participants register in their own org and blueprints resolve them cross-org by wallet address, or (b) add a cross-org participant invitation flow.

---

### ISSUE-10: Register Creation Needs Full Attestation Flow
- **Component:** Register Service
- **Severity:** Major
- **Problem:** The simplified register creation (initiate + finalize) in the E2E setup fails or produces an unusable register. The full register creation requires a 3-phase flow with attestation signing (as implemented in the walkthrough's `New-SorchaRegister`). Without a valid register, `CreateInstanceRequest` fails because the blueprint publish transaction doesn't exist on the register.
- **Recommended fix:** Either (a) port the walkthrough's full `New-SorchaRegister` flow into the E2E test setup, or (b) add a simplified "create test register" admin endpoint that skips attestation for Development environments.

### ISSUE-11: Blueprint Instance Creation Requires Published Register
- **Component:** Blueprint Service
- **Severity:** Informational
- **Problem:** `POST /api/instances` with `CreateInstanceRequest { blueprintId, registerId }` expects the blueprint to have a publish transaction on the register. This is created during `POST /api/blueprints/{id}/publish`, which writes to the register. If the register isn't properly initialized, publishing succeeds (in-memory) but the register write fails silently.
- **Impact:** Instance creation returns "Blueprint not found" or fails to chain from the publish transaction.
- **Recommended fix:** Blueprint publish should fail explicitly if the register write fails, rather than succeeding with a partial state.

---

## Test Infrastructure Notes

### What Works
- `MultiUserTestBase` — per-user browser contexts, API token management, state persistence across ordered tests
- `SignupPage` — email tab selection, form fill (selectors fixed for `#tab-email` scoping)
- `CredentialsPage`, `ActionSubmissionPage` — created, not yet exercised
- Blueprint template loading from repo root via `RepoPath()` helper
- Auto email verification via Docker psql
- Wallet creation via `/api/v1/wallets` (nested response parsing)
- Participant self-registration with JWT org_id extraction
- Blueprint creation with server-generated ID extraction
- Staff user login via UI — all 3 council staff successfully authenticated

### What Needs Work
- `LoginAsUserAsync` — needs org selection handling for multi-org users (citizen login blocked)
- Register creation — needs full attestation signing flow (3-phase) from walkthrough
- Action execution — not yet reached (blocked by register)
- UI verification of workflows/credentials — not yet reached

---

## Files Changed

### Service Fixes (3 bug fixes)
| File | Change |
|------|--------|
| `src/Services/Sorcha.Tenant.Service/Services/PlatformUserService.cs` | Configurable lockout via `LockoutConfig` + `IConfiguration` |
| `src/Services/Sorcha.Tenant.Service/Services/OrgProvisioningService.cs` | Wrapped transaction in execution strategy |
| `src/Services/Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs` | `EnableDynamicJson()` on NpgsqlDataSourceBuilder |
| `src/Services/Sorcha.Tenant.Service/appsettings.Development.json` | Relaxed lockout config for development |
| `docker-compose.yml` | `Security__Lockout__*` env vars for development |

### Test Infrastructure (new)
| File | Change |
|------|--------|
| `blueprints/templates/council-id-application-template.json` | Flow 1 blueprint |
| `blueprints/templates/council-service-request-template.json` | Flow 2 blueprint |
| `tests/.../Infrastructure/MultiUserTestBase.cs` | Multi-user auth switching base class |
| `tests/.../Infrastructure/TestConstants.cs` | Council test constants + timeouts |
| `tests/.../PageObjects/SignupPage.cs` | Signup page object |
| `tests/.../PageObjects/WorkflowPages/ActionSubmissionPage.cs` | Action form page object |
| `tests/.../PageObjects/CredentialsPage.cs` | Credentials page object |
| `tests/.../Docker/CouncilCredentialFlowTests.cs` | Main 9-test flow class |
