# Contract: `GET /api/auth/me` — add `emailVerified`

**Service**: Sorcha.Tenant.Service · **Endpoint**: `AuthEndpoints.GetCurrentUser` · **Auth**: bearer
(any tier). Existing endpoint; this feature adds one field (FR-010, FR-011).

## Request

`GET /api/auth/me`
`Authorization: Bearer <token>`

No body, no query parameters.

## Response — `200 OK` (`CurrentUserResponse`)

```jsonc
{
  "userId": "…",
  "email": "alice@example.com",
  "displayName": "Alice",
  "organizationId": "…",
  "organizationName": "…",
  "roles": [],
  "tokenType": "user",
  "scopes": [],
  "authMethod": "passkey",
  "emailVerified": true        // NEW — non-nullable bool
}
```

### `emailVerified` semantics

| Account state | Token carries `email_verified` claim | `emailVerified` returned |
|---------------|--------------------------------------|--------------------------|
| Email verified | `true` | `true` |
| Email not verified | `false` (or absent) | `false` |
| No email / status unknown (e.g. some social auth) | absent | `false` (unambiguous "not verified", FR-011) |

- Non-nullable `bool`, default `false`. **Never** implies "verified" when unknown.
- Sourced from the `email_verified` claim, minted from `PlatformUser.EmailVerified`. Read claims-only in
  the handler (no DB round-trip) — see research Decision 4.

## OpenAPI

`.WithSummary(...)` / `.WithDescription(...)` updated to note the response now reports
email-verification status. New DTO property carries a `/// <summary>` (Constitution III).

## Test expectations

- Verified user → `emailVerified == true` (integration: `AuthApiTests`).
- Unverified user → `emailVerified == false`.
- Token without the claim → `emailVerified == false` (no exception, unambiguous).
- No regression to existing fields.
