# Contract — OAuth Callback URL

**Phase**: 1 — Design
**Scope**: The single per-environment redirect URI registered with each
OAuth provider, and the route that handles it.

## Endpoint

| Property | Value |
|---|---|
| **HTTP Method** | `GET` |
| **Path** | `/auth/social/callback` |
| **Handler** | `Sorcha.Tenant.Service.Pages.Auth.SocialCallback` (Razor page, `@page "/auth/social/callback"`) |
| **Authentication** | Anonymous (visitor is mid-OAuth flow, has no session yet) |

## Per-environment URIs

| Environment | Full URL |
|---|---|
| n1 | `https://n1.sorcha.dev/auth/social/callback` |
| Local dev | `https://localhost:7110/auth/social/callback` |

These URIs MUST match exactly what is registered at each OAuth
provider's developer console. Per FR-021, exactly **one URI per
environment** is registered — no per-provider variants.

## Query parameters (set by the OAuth provider)

| Parameter | Source | Used by |
|---|---|---|
| `code` | OAuth provider | `SocialCallback.OnGetAsync` → `ExchangeCodeAsync` |
| `state` | OAuth provider (echoed from initiate) | Validated against distributed cache key `social:state:{state}` |
| `error` | OAuth provider on user-cancel or failure | Renders the cancellation message (FR-017) |

The `provider` query param previously expected by `SocialCallback.OnGetAsync`
is REMOVED — provider identity is read from the cached `SocialStateData`
keyed by `state`.

## Response shapes

| Outcome | Response |
|---|---|
| Success (resolve returns `User != null`) | 302 redirect to `/app/#token=<jwt>&refresh=<refresh>` |
| Refusal (`SocialLoginRefusal != None`) | 200, renders `SocialCallback.cshtml` with `ErrorMessage` set to the matching copy from FR-016 |
| Provider error / state invalid / code-exchange failure | 200, renders the page with `ErrorMessage = "The sign-in was cancelled or failed. Please try again."` |

## Security

- TLS-only — providers reject `http://` redirect URIs in production.
- Anonymous access is required; the visitor is mid-OAuth and has no
  Sorcha session.
- The `state` parameter is single-use: cache entry is removed on first
  read in `ExchangeCodeAsync`. Replay of a `state` value will hit the
  "Invalid or expired state parameter" path.
- The handler MUST NOT log `code`, `state`, or any portion of the JWT
  it issues. Telemetry refusal counters use a hash-based redacted
  email tag (FR-018).

## Bug being fixed

Today (master before this feature): `SocialLoginEndpoints.cs:99,262`
construct the redirect URI as
`{baseUrl}/api/auth/social/callback-redirect`. There is no handler at
that path. After provider consent, the user lands on a 404 instead of
the Razor page.

The fix: change both call sites to
`{baseUrl}/auth/social/callback`.

## Provider-side registration

For each provider, register exactly the URIs above. The provider's
developer console is the source of truth for what URIs the provider
will accept; mismatch ⇒ provider returns `redirect_uri_mismatch` error
to the user. Operator runbook in
[`docs/guides/SOCIAL-LOGIN-SETUP.md`](../../docs/guides/SOCIAL-LOGIN-SETUP.md)
covers the click-by-click steps.
