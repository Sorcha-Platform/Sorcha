# Quickstart — Feature 115 Social Signup

**Audience**: Developer running the implementation; operator deploying
to n1.
**Phase**: 1 — Design

This is the joined-up "make it work" walkthrough. Step 1-3 are local
dev; Step 4-7 are n1 deploy.

---

## 1. Local dev — code-level smoke

After implementing the feature on branch `115-social-signup`:

```bash
# From repo root
dotnet restore
dotnet build src/Services/Sorcha.Tenant.Service/Sorcha.Tenant.Service.csproj --force
dotnet test tests/Sorcha.Tenant.Service.Tests/ \
  --filter "FullyQualifiedName~SocialLogin|FullyQualifiedName~Signup|FullyQualifiedName~DatabaseInitializer"
```

Expected: all new and extended tests pass. The pre-existing 81-test
constructor failure in `Blueprint.Service.Tests` is unrelated and is
filtered out by the path-scoped `dotnet test` invocation.

---

## 2. Local dev — UI smoke without OAuth round-trip

Sign-up page should render without OAuth credentials configured. With
`SocialProviders` empty in `appsettings.Development.json`:

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

Open `https://localhost:7110/auth/signup`. The "Social" tab should
exist but show **zero provider buttons** (FR-002). No JS errors in the
browser console.

---

## 3. Local dev — OAuth round-trip with Google (optional)

Follow [`docs/guides/SOCIAL-LOGIN-SETUP.md`](../../docs/guides/SOCIAL-LOGIN-SETUP.md)
to register a Google OAuth app for local dev. Add to
`appsettings.Development.json` (gitignored):

```json
{
  "SocialProviders": [
    {
      "Name": "Google",
      "ClientId": "<dev-client-id>",
      "ClientSecret": "<dev-client-secret>"
    }
  ]
}
```

Restart. Visit signup page → Google button appears → click → Google
consent screen → land back signed in at the app home.

---

## 4. n1 deploy — register OAuth applications

Operator runs steps in
[`docs/guides/SOCIAL-LOGIN-SETUP.md`](../../docs/guides/SOCIAL-LOGIN-SETUP.md):

- Google: Cloud Console → OAuth client → Web app → redirect URI
  `https://n1.sorcha.dev/auth/social/callback`. Capture client ID +
  secret.
- GitHub: Settings → Developers → New OAuth App → callback URL
  `https://n1.sorcha.dev/auth/social/callback`. Capture client ID +
  secret.

This is collaborative — Stuart drives the provider consoles, Claude
narrates next steps.

---

## 5. n1 deploy — seed the `.env` file

Once the four OAuth values are in hand:

```bash
ssh sorcha@51.105.7.135
cd /opt/sorcha
nano .env
# Paste:
#   GOOGLE_OAUTH_CLIENT_ID=...
#   GOOGLE_OAUTH_CLIENT_SECRET=...
#   GITHUB_OAUTH_CLIENT_ID=...
#   GITHUB_OAUTH_CLIENT_SECRET=...
chmod 600 .env
```

`/opt/sorcha/.env` is host-local; it persists across `docker compose
down` / restarts but is wiped if the VM is re-imaged.

---

## 6. n1 deploy — full reset to pick up new images and seeds

After branch `115-social-signup` is merged and the Docker Publish CI
workflow has built the new images:

```powershell
# From repo root on the developer's machine
.\scripts\n1-reset.ps1 -ResourceGroup sorcha-n1-uk -UpdateCompose -Yes
```

This:

- Stops + removes containers
- Wipes volumes (Postgres + Mongo + Redis state)
- Pulls latest `sorchadev/*:latest` images (Tenant + new Citizen Wallet
  + Citizen Verifier from F114, plus everything else)
- Re-runs bootstrap CLI to create the admin org + seed PlatformSettings
  with `PublicOrgEnabled=true` (per FR-019)

---

## 7. n1 deploy — smoke test the seven scenarios

Walk through these in a browser at `https://n1.sorcha.dev/auth/signup`:

1. **Demo banner** is visible at the top of the page (FR-020).
2. **Two buttons** — Google and GitHub — appear in the Social tab.
   Microsoft and Apple buttons are absent (FR-001).
3. **Google new-user signup**: click → consent → land logged in →
   welcome email arrives in mailbox.
4. **Google second login** (in a private window or after sign-out):
   click → consent (or skipped if remembered) → signed in → no second
   welcome email → display name updated if changed in Google profile.
5. **GitHub new-user signup**: same as 3 with GitHub.
6. **Email-collision refusal**: register `test+collision@gmail.com`
   via password; do NOT verify; sign-out; click "Continue with Google"
   using the same Gmail. Expected: refusal page with
   "an account exists for this email but isn't verified" message.
7. **Email-collision happy path**: verify the password account from
   step 6 by clicking the verification link. Sign-out. Click
   "Continue with Google" again. Expected: linked, signed in.

DB checks (run via Azure CLI run-command or SSH):

```sql
-- Tenant DB
\c sorcha_tenant
select email, "EmailVerified", "DisplayName" from tenant.platform_users;
-- Expect: each social-signup user has EmailVerified=true,
-- DisplayName = whatever the provider returned

select provider, subject, "LinkedAt", "LastUsedAt"
from public.platform_social_logins;
-- Expect: one row per (user, provider) pair, LastUsedAt advancing
-- on subsequent sign-ins
```

Telemetry check (Aspire dashboard at `:18888` or Grafana when wired):

- `sorcha_social_login_refusal_total{provider, reason}` — should show
  the step-6 refusal under `reason=existing_unverified`.

---

## 8. Rollback procedure

If the deploy regresses:

```bash
# Revert to the previous master tag
ssh sorcha@51.105.7.135
cd /opt/sorcha
docker compose -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml \
  pull tenant-service:<previous-tag>
docker compose -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml \
  up -d --force-recreate tenant-service
```

The `.env` file remains valid for any future redeploy. The DB-stored
`PlatformSettings.PublicOrgEnabled` survives the revert.

If the issue is purely the `SocialProviders` configuration (e.g. typo
in client secret), edit `/opt/sorcha/.env` and `docker compose
restart tenant-service` — no need for a full reset.

---

## Open follow-ups

- **Switch n1 to `Staging`** — scheduled `/schedule` reminder per
  REQ-7. Activates feature 113 storage fail-fast; verify all six
  audited interfaces are on Postgres/Redis backends before flipping.
- **Microsoft + Apple providers** — separate features (BACKLOG-2,
  BACKLOG-3).
- **Key Vault for OAuth secrets** — at first Kubernetes deployment
  (BACKLOG-5).
