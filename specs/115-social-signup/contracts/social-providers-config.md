# Contract — `SocialProviders` Configuration Shape

**Phase**: 1 — Design
**Scope**: How OAuth provider credentials enter the running service, and
how the .env-file convenience layer maps to the canonical .NET config
shape.

## .NET configuration shape (canonical)

`Sorcha.Tenant.Service` reads its provider list at construction via
`configuration.GetSection("SocialProviders").Get<List<SocialProviderConfig>>()`.

```jsonc
{
  "SocialProviders": [
    {
      "Name": "Google",
      "ClientId": "<google-client-id>",
      "ClientSecret": "<google-client-secret>"
      // SupportsPkce defaults to true for OIDC providers
    },
    {
      "Name": "GitHub",
      "ClientId": "<github-client-id>",
      "ClientSecret": "<github-client-secret>"
      // SupportsPkce auto-set to false (GitHub does not support PKCE)
    }
  ]
}
```

The `SocialProviderConfig` type allows additional fields
(`AuthorizationEndpoint`, `TokenEndpoint`, `UserInfoEndpoint`, `Scopes`)
which fall back to well-known endpoints for recognised provider names.
For Google and GitHub the well-known fallbacks are correct, so only
`Name`, `ClientId`, `ClientSecret` need to be set.

## Environment-variable shape (.NET convention)

Same shape via env vars uses the standard `__N__` indexed binding:

```bash
SocialProviders__0__Name=Google
SocialProviders__0__ClientId=<google-client-id>
SocialProviders__0__ClientSecret=<google-client-secret>
SocialProviders__1__Name=GitHub
SocialProviders__1__ClientId=<github-client-id>
SocialProviders__1__ClientSecret=<github-client-secret>
```

This is what `docker-compose.n1.yml` injects into the `tenant-service`
container's `environment:` block.

## .env-file convenience layer (operator-readable)

For human-friendly secret rotation (REQ-5), the n1 host carries
`/opt/sorcha/.env` with friendly names:

```bash
GOOGLE_OAUTH_CLIENT_ID=<value>
GOOGLE_OAUTH_CLIENT_SECRET=<value>
GITHUB_OAUTH_CLIENT_ID=<value>
GITHUB_OAUTH_CLIENT_SECRET=<value>
```

`docker-compose.n1.yml` interpolates these into the canonical .NET
shape:

```yaml
tenant-service:
  environment:
    SocialProviders__0__Name: Google
    SocialProviders__0__ClientId: ${GOOGLE_OAUTH_CLIENT_ID}
    SocialProviders__0__ClientSecret: ${GOOGLE_OAUTH_CLIENT_SECRET}
    SocialProviders__1__Name: GitHub
    SocialProviders__1__ClientId: ${GITHUB_OAUTH_CLIENT_ID}
    SocialProviders__1__ClientSecret: ${GITHUB_OAUTH_CLIENT_SECRET}
```

`.env.example` (committed) ships placeholder lines so operators know
which keys to set. `.env` (gitignored) is created on the n1 host
manually per **REQ-5 / FR-003**.

## Provider-list visibility surface (FR-001 / FR-002)

`ISocialLoginService` gains:

```csharp
public interface ISocialLoginService
{
    // existing members…
    IReadOnlyList<string> GetConfiguredProviderNames();
}
```

Returns the list of provider names that have **non-empty `ClientId` AND
non-empty `ClientSecret`**. Providers configured with empty credentials
are excluded.

`SignupModel.OnGet` and `LoginModel.OnGet` consume this and expose
`Model.AvailableProviders` to the views. Views render one button per
configured provider; no buttons for unconfigured providers (FR-002).

## Environment-specific notes

| Environment | Configured providers (target state) |
|---|---|
| Local dev (developer's machine) | None — `SocialProviders` empty in `appsettings.Development.json`. Buttons hidden. Developers test via password / passkey. |
| n1 | Google + GitHub (set via `/opt/sorcha/.env`). |
| Future Microsoft / Apple environments | Add `SocialProviders__2__*` entries; .env gets new vars. (Out of scope for this feature.) |

## Validation expectations

- **No client-side validation required**. The provider list is read at
  service construction and immutable for the process lifetime;
  configuration changes require a service restart.
- **Empty / missing config is valid** — yields zero buttons, signup
  page renders without the social tab having any options. Visitor
  falls back to passkey or email/password.
- **Invalid credentials** (typo in client secret) cannot be detected
  pre-flight without making a token-exchange call. Failure manifests
  during the OAuth flow at the provider end (`invalid_client` error).
  The visitor sees the cancellation message; the operator confirms by
  checking the refusal counter (`reason=code_exchange_failed`).

## Ancillary: `PublicOrgEnabled` seed default

Distinct from `SocialProviders` but shipped alongside per FR-019:

```yaml
tenant-service:
  environment:
    PlatformSettings__SeedPublicOrgEnabled: "true"
```

`DatabaseInitializer` reads this on a fresh DB seed only. After the
row exists in `tenant.platform_settings`, the admin UI / API toggle is
the source of truth.

## Provider scopes (informational)

| Provider | Default scopes |
|---|---|
| Google / Microsoft / Apple | `openid email profile` (set in `SocialProviderConfig.Scopes` default) |
| GitHub | `openid email profile` (GitHub ignores `openid`; the equivalent is the `read:user` and `user:email` scopes implied by `email` + `profile` here) |

For Google, the configured scopes match the required Google Cloud
Console "scopes for verification" set, so the OAuth consent screen
will show correctly without app-verification when in test mode.
