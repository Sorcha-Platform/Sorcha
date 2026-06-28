# Quickstart & Validation: Step-Up-Gated Social Account Linking

Runnable validation that the B-backend feature works end-to-end. Maps each scenario to the user
stories (US1–US5) and the success criteria (SC-001…SC-006). Implementation bodies live in `tasks.md`
/ the source; this is the run/verify guide.

## Prerequisites
- .NET 10 SDK; repo restored and built: `dotnet restore && dotnet build`
- Tests target `tests/Sorcha.Tenant.Service.Tests`.
- `JwtSettings:SigningKey` configured (drives the link-pending HMAC key); fail-closed otherwise.

## Build & test
```bash
# All Tenant Service tests
dotnet test tests/Sorcha.Tenant.Service.Tests

# Just this feature's suites
dotnet test tests/Sorcha.Tenant.Service.Tests \
  --filter "FullyQualifiedName~LinkPendingToken|FullyQualifiedName~SocialLinkStepUp|FullyQualifiedName~SocialLinkConfirm|FullyQualifiedName~SocialCallbackLinkRequired"
```

## Scenario 1 — Unconnected social matching existing account → LinkRequired, no session (US1, SC-001)
1. Seed an existing `PlatformUser` with a **verified** email and **no** `(provider, subject)` link.
2. Drive the social callback with that provider, verified email == the account's email.
3. **Expect**: a **LinkRequired** outcome carrying a link-pending token; **no** JWT/session issued;
   no `PlatformSocialLogin` row created.
- Reference: [contracts/link-pending-token.md](./contracts/link-pending-token.md), data-model state diagram.

## Scenario 2 — Complete the link with a valid proof (US1 scenarios 2–3, SC-003)
1. From Scenario 1's link-pending token, `POST /api/auth/social/link/challenge/initiate`, satisfy the
   offered method, `POST .../verify` → obtain a challenge token.
2. `POST /api/auth/social/link/confirm` with the link-pending token + `X-Auth-Challenge`.
3. **Expect**: 200 with the same session shape as a normal social sign-in; `PlatformSocialLogin` now
   linked. Sign in again with the same provider → direct sign-in, **no** further step-up.
- Reference: [contracts/social-link-stepup.md](./contracts/social-link-stepup.md), [contracts/link-confirm.md](./contracts/link-confirm.md).

## Scenario 3 — Reject matrix (US2, SC-002)
Call link-confirm and assert each is rejected with **no link**:
| Case | Expect |
|------|--------|
| (a) No `X-Auth-Challenge` proof | 401 |
| (b) Expired link-pending token | 401 |
| (c) Proof scoped to a different operation | 401/403 |
| (d) Proof belonging to a **different account** than the token targets | 403 |
| (e) Tampered link-pending token signature | 401 |
- Reference: [contracts/link-confirm.md](./contracts/link-confirm.md) behaviour steps 1–4.

## Scenario 4 — FR-010 proof policy across five configs (US3, SC-005)
For each target-account config, initiate `LinkSocial` step-up and assert the accepted/offered method:
| Config | Expected |
|--------|----------|
| Passkey | passkey proof accepted |
| Linked social (another provider) | re-auth with that social accepted |
| Password only, no 2FA | password alone accepted |
| Password + 2FA | password alone **insufficient**; password ∧ 2FA required (403 `proof_tier_insufficient` on bare password) |
| Password + 2FA + passkey | strongest (passkey) accepted; bare password insufficient |
- Reference: research Decision 5; assert against the single ladder code path.

## Scenario 5 — Cancel / abandon leaves everything untouched (US4)
1. Obtain a link-pending token (Scenario 1). Never call link-confirm; let it expire (or call after
   expiry).
2. **Expect**: no `PlatformSocialLogin` row, no session ever issued, target account unchanged; a
   post-expiry confirm attempt → 401, no state change.

## Scenario 6 — Unchanged paths (US5, SC-004)
| Path | Expect (identical to today) |
|------|------------------------------|
| Social email matches **no** account, account-creation surface | new account created + session issued |
| Social email matches no account, login-only surface (citizen wallet) | refused, no account created |
| `(provider, subject)` **already linked** | direct sign-in, no link prompt |

## Scenario 7 — Collisions at confirm time (SC-006)
Race/late-binding: by confirm time the `(provider, subject)` is already linked elsewhere, or the
social email now belongs to another account → **409 Conflict**, never a duplicate/overwrite.

## Telemetry check (FR-017)
After running Scenarios 1–3, confirm the `sorcha_social_login_*` counter shows `link_required` and the
link-confirm `success` / `conflict` / `rejected` tags (no PII).
