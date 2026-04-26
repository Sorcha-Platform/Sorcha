# Social Login Setup

This guide walks operators through enabling public-user social signup on
a Sorcha environment. The current production target is `n1.sorcha.dev`
with two providers (Google and GitHub).

## Overview

Sorcha supports OAuth2/OIDC social signup via a configurable provider
list. To enable a provider on an environment you need three things:

1. An OAuth application registered with the provider (yields a Client
   ID + Client Secret)
2. The provider's redirect URI registered to point at this environment
   (`https://<env-host>/auth/social/callback`)
3. Configuration on the host that exposes the credentials to the
   running Tenant Service container

The signup and login pages render a "Continue with `<provider>`" button
only for providers that are configured with non-empty credentials.
Adding or removing a provider is a configuration change followed by a
service restart — no code changes required.

## Per-environment redirect URIs

Each environment uses a single canonical callback URL. Register exactly
this URL with each provider — typo-driven mismatches yield
`redirect_uri_mismatch` errors at the provider's consent screen.

| Environment | Redirect URI |
|---|---|
| n1 | `https://n1.sorcha.dev/auth/social/callback` |
| Local development | `https://localhost:7110/auth/social/callback` |

## Provider 1 — Google

### Register the OAuth application

1. Open <https://console.cloud.google.com> and sign in with the Google
   account that should own the OAuth app.
2. Create or select the project that will host this Sorcha environment
   (e.g. "Sorcha n1").
3. Navigate to **APIs & Services → OAuth consent screen**:
   - User type: **External**
   - App name: e.g. "Sorcha (n1 demonstrator)"
   - Support email: your email
   - Authorised domains: add `sorcha.dev`
   - Developer contact: your email
   - Scopes: `openid`, `email`, `profile` (no other scopes are required)
   - Save and continue. Test mode is sufficient for ≤100 users.
4. Navigate to **APIs & Services → Credentials → Create Credentials →
   OAuth client ID**:
   - Application type: **Web application**
   - Name: e.g. "Sorcha n1 Web"
   - Authorised redirect URIs:
     - `https://n1.sorcha.dev/auth/social/callback`
     - (optional) `https://localhost:7110/auth/social/callback` for dev
   - Click **Create**.
5. Copy the **Client ID** and **Client Secret** shown in the dialog.

### Capture the values

You should now have two strings — keep them somewhere secure (a
password manager). They are the values for `GOOGLE_OAUTH_CLIENT_ID` and
`GOOGLE_OAUTH_CLIENT_SECRET` in the next step.

## Provider 2 — GitHub

### Register the OAuth application

1. Open <https://github.com/settings/developers> (Settings → Developer
   settings → OAuth Apps).
2. Click **New OAuth App**:
   - Application name: e.g. "Sorcha n1"
   - Homepage URL: `https://n1.sorcha.dev`
   - Authorization callback URL:
     `https://n1.sorcha.dev/auth/social/callback`
   - Click **Register application**.
3. On the resulting page, click **Generate a new client secret**.
4. Copy the **Client ID** and the **Client Secret** (the secret is
   displayed only once — save it immediately).

### Capture the values

These are `GITHUB_OAUTH_CLIENT_ID` and `GITHUB_OAUTH_CLIENT_SECRET`.

## Deploy on n1

### One-time `.env` seed

Once you have the four credentials in hand:

```bash
ssh sorcha@n1.sorcha.dev
cd /opt/sorcha

# If .env does not exist, copy from the template:
# scp .env.example sorcha@51.105.7.135:/opt/sorcha/.env

nano .env
# Set the four values:
#   GOOGLE_OAUTH_CLIENT_ID=...
#   GOOGLE_OAUTH_CLIENT_SECRET=...
#   GITHUB_OAUTH_CLIENT_ID=...
#   GITHUB_OAUTH_CLIENT_SECRET=...

chmod 600 .env
```

The `.env` file is read by Docker Compose during `docker compose up`
and interpolated into the canonical .NET configuration keys
(`SocialProviders__0__ClientId` etc.) per
`docker-compose.n1.yml:tenant-service.environment`.

### Apply the configuration

After editing `.env`, restart the Tenant Service container:

```bash
docker compose -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml \
  restart tenant-service
```

A full reset (`scripts/n1-reset.ps1`) is needed only when you have new
images to pull; for a credentials-only change a restart is sufficient.

## Verify the deploy

1. Open `https://n1.sorcha.dev/auth/signup` in a browser.
2. Confirm the demonstrator banner is visible at the top of the page.
3. Click the **Social** tab. Two buttons should appear: "Continue with
   Google" and "Continue with GitHub". No Microsoft or Apple buttons
   (those providers are out of scope for this environment).
4. Click **Continue with Google**. You should be redirected to
   Google's consent screen, then back to Sorcha, and land signed in to
   the application home (`/app/`).
5. The first sign-in dispatches a welcome email — check the inbox.
6. Sign out and try **Continue with GitHub** with a different account
   to confirm the second provider works.
7. (Optional) Confirm telemetry: check the Aspire dashboard's metrics
   pane for `sorcha_social_login_refusal_total` — it should be zero
   for happy-path sign-ins. To exercise the refusal path, register a
   password account with an email and don't verify it; then attempt
   social signup with the same email at Google. The attempt should be
   refused with a clear message and the counter should increment with
   `reason=existing_unverified`.

## Rotate credentials

To rotate a provider's client secret:

1. Generate a new secret at the provider's developer console (Google
   Cloud Console → Credentials → your OAuth client → Add secret;
   GitHub → OAuth Apps → your app → Generate a new client secret).
2. SSH to the n1 host: `ssh sorcha@n1.sorcha.dev`
3. `nano /opt/sorcha/.env` — replace the old secret with the new one
4. `cd /opt/sorcha && docker compose -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml restart tenant-service`
5. Verify with a sign-in, then revoke the old secret at the provider.

The whole rotation should take under five minutes.

## Troubleshooting

### `redirect_uri_mismatch` from the provider

- The redirect URI in the provider's OAuth app does not exactly match
  `https://n1.sorcha.dev/auth/social/callback`. Common causes: trailing
  slash, `http` instead of `https`, typo in the hostname.

### Buttons don't appear on `/auth/signup`

- The Tenant Service is not picking up the env vars. Check
  `docker compose -f docker-compose.yml -f docker-compose.n1.yml exec tenant-service env | grep -i social`
  to confirm the values are present in the running container. If
  they're empty, verify `/opt/sorcha/.env` has the four lines and the
  host shell isn't inadvertently exporting blank values.

### `invalid_client` from the provider after consent

- The Client ID or Client Secret is wrong. Re-copy from the provider's
  developer console; secrets often have leading/trailing whitespace
  when copy-pasted from web pages.

### "Public organisation is not enabled" error from the initiate API

- `PlatformSettings.PublicOrgEnabled = false` in the database. On a
  fresh n1 reset this is set from `PlatformSettings__SeedPublicOrgEnabled`
  in `docker-compose.n1.yml`. If you've toggled it off via the admin UI,
  toggle it back on at `https://n1.sorcha.dev/admin/settings/platform`.

## Adding a new provider later

The codebase already supports Microsoft and Apple as recognised
provider names with well-known endpoint defaults. To enable Microsoft
on this environment in future:

1. Register a Microsoft Entra app and grab its Client ID + Secret.
2. Add `SocialProviders__2__Name: Microsoft`,
   `SocialProviders__2__ClientId: ${MICROSOFT_OAUTH_CLIENT_ID}`,
   `SocialProviders__2__ClientSecret: ${MICROSOFT_OAUTH_CLIENT_SECRET}`
   to `docker-compose.n1.yml`.
3. Add the matching env-var lines to `.env.example` and the host's
   `/opt/sorcha/.env`.
4. Restart the Tenant Service.

A "Continue with Microsoft" button will appear automatically. No code
change required. Apple is currently deferred — it requires a JWT-based
client-secret flow that the codebase does not implement today.
