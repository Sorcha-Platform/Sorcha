# Quickstart — Feature 116 Account Linking

End-to-end developer walk-through for verifying the feature locally.

## Prerequisites

- .NET 10 SDK installed.
- Docker Desktop running.
- Repo on branch `116-account-linking`.

## 1. Apply migration squash

```powershell
# Pre-release squash policy — modify InitialCreate, do not version forward.
git rm src/Services/Sorcha.Tenant.Service/Migrations/20260425152258_InitialCreate*

# After AuthChallengeToken DbSet + OnModelCreating land:
$env:ConnectionStrings__Sorcha__Postgres = "Host=localhost;Database=tenant;Username=stub;Password=stub"
dotnet ef migrations add InitialCreate `
  --project src/Services/Sorcha.Tenant.Service `
  --startup-project src/Services/Sorcha.Tenant.Service `
  --output-dir Migrations
```

Verify the regenerated migration file references `auth_challenge_tokens`.

## 2. Build & run

```bash
docker-compose down -v   # discard old tenant volume
docker-compose build tenant tenant-frontend
docker-compose up -d
```

Aspire dashboard: http://localhost:18888 — confirm Tenant Service health is green.

## 3. Smoke test the aggregate read

```bash
# Sign in to get a JWT (use existing seeded dev user or new sign-up)
TOKEN=$(curl -s -X POST http://localhost/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"alice@dev.sorcha.dev","password":"DevPassword123!"}' \
  | jq -r .accessToken)

# Aggregate read — should return password=isSet, no socials, no passkeys, all canRemove computed
curl -s http://localhost/api/me/auth-methods -H "Authorization: Bearer $TOKEN" | jq
```

Expected: `password.canRemove=false` (last method), empty `socials`, empty `passkeys`, accurate `email` and `emailVerified`.

## 4. End-to-end user-story walkthroughs

### US1 — Link Google, then unlink

1. Open http://localhost/app, sign in as Alice.
2. Settings → **Accounts** (first tab).
3. **Linked sign-in providers** → click **Google**. Complete OAuth in the popup.
4. Confirm Google appears in the list with email + linked-at + last-used.
5. Click **Unlink** on the Google row.
6. `AuthChallengeDialog` opens. With 2FA enabled → TOTP input. With password only → password input.
7. Submit proof → row disappears.

Verify rejection path: in a fresh browser profile, sign up as a *different* user with the same Google email. Then sign back in as Alice and try to link the same Google account → expect `409` and the user-facing collision message.

### US2 — Add, rename, remove a passkey

1. Settings → **Accounts** → **Passkeys** → **Add a passkey**.
2. Provide display name "Dev YubiKey". Complete WebAuthn ceremony.
3. Confirm row appears with `Last used: never`.
4. Click **Rename** → change to "Updated name" → save. Row updates inline; no dialog.
5. Click **Remove** → `AuthChallengeDialog` → submit proof → row disappears from list.
6. In Postgres: `SELECT status, disabled_reason, disabled_at FROM passkey_credentials WHERE id = 'X'` → confirm `status=Revoked`, `disabled_reason='user-removed'`, `disabled_at` populated.

### US3 — Set / change / remove password

For a passkey-only test user (or social-only):
1. Settings → **Accounts** → **Password** → **Set a password**.
2. `AuthChallengeDialog` → WebAuthn step-up → submit.
3. Provide new password → save → `204`.

For a password user:
1. Settings → **Security** → **Password** → **Change password**.
2. `AuthChallengeDialog` → enter current password → provide new → save.

For a multi-method user:
1. Settings → **Accounts** → **Password** → **Remove password**.
2. `AuthChallengeDialog` → submit → `204`. Confirm `PasswordHash IS NULL` in DB.

### US4 — Aggregate read transparency

1. Settings → **Accounts**. Confirm all four sections render with accurate `last-used` timestamps and the four social Add pills.
2. With Google + GitHub already linked, confirm the Google and GitHub pills appear struck-through and disabled while Microsoft and Apple are active.

## 5. Verify edge cases

- **Last-method floor**: with only one method, attempt Remove via the disabled UI button (should be unclickable). Then bypass the UI by hand-crafting a `DELETE` curl call with a valid challenge token → expect `409 LastSignInMethodProtected`.
- **Reused challenge token**: capture a token from `/challenge/verify`, present it twice → second attempt returns `401 ChallengeAlreadyConsumed`.
- **Cross-operation token**: issue a token for `ChangePassword`, present on `DELETE /api/auth/social/{id}` → `401 ChallengeOperationMismatch`.
- **Tampered OAuth state**: capture `state` from the initiate response, mutate one character, post to `/callback` → `400 InvalidOAuthState`. Logged at Warning with the offending IP.
- **2FA disable now gated**: with 2FA enrolled, attempt `POST /api/2fa/disable` without a challenge → `401`. With a fresh `Disable2Fa`-scoped token → success.

## 6. Run the test suites

```bash
# Unit + integration
dotnet test tests/Sorcha.Tenant.Service.Tests/

# E2E (Playwright in Docker)
dotnet test tests/Sorcha.UI.E2E.Tests/ --filter "FullyQualifiedName~AccountsTab"
```

Coverage target: >85% on new code (constitutional requirement IV).

## 7. Telemetry sanity-check

In the Aspire dashboard, filter metrics by `Sorcha.Tenant.Auth`. After the walkthrough above expect non-zero values on:

- `sorcha_auth_challenge_issued_total{method=Totp,scope=RemoveAuthMethod}`
- `sorcha_auth_challenge_consumed_total{outcome=success}`
- `sorcha_auth_method_added_total{kind=passkey}`
- `sorcha_auth_method_removed_total{kind=passkey}`
- `sorcha_auth_floor_blocked_total{kind=...}` (only after the manual 409-bypass test)
- `sorcha_auth_link_collision_total{provider=google}` (only after the cross-account collision test)

## 8. Rollback

Pre-release; no rollback procedure beyond `docker-compose down -v`. The migration squash means the table appears or disappears with the InitialCreate baseline.
