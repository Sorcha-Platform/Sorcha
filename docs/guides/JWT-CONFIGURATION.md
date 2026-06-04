# JWT Configuration Guide

Sorcha uses **JWT Bearer authentication** with the **Tenant Service** as the sole token issuer.
Every other service validates tokens using shared `JwtSettings` from `Sorcha.ServiceDefaults`. Since
Feature 136 the configuration model is **tiered audiences + a hardened, installation-namespaced
issuer** — the `aud` claim is the trust-tier boundary, not a single flat URL.

## Installation name

`JwtSettings:InstallationName` is the unique identifier for a deployment (docker-compose defaults to
`localhost`; when unset entirely it defaults to `sorcha`). It drives **both** the issuer and the
audience namespace, so every service in one installation must use the **same** value.

```yaml
# Shared across all services (docker-compose x-jwt-env)
JwtSettings__InstallationName: ${INSTALLATION_NAME:-localhost}
JwtSettings__SigningKey:       ${JWT_SIGNING_KEY:-<base64 dev key>}
```

## Issuer (hardened — Feature 136)

The issuer is resolved by `SorchaIssuer.Resolve`, in order:

1. An explicit `JwtSettings:Issuer`, if set (wins — e.g. the AppHost sets `https://localhost:7110`).
2. Otherwise **`urn:sorcha:{InstallationName}`** (e.g. `urn:sorcha:localhost`).
3. In non-production (Development/Testing) only, the fallback **`urn:sorcha:dev-local`**.
4. Otherwise it **throws at startup** — there is **no shared default**, so a misconfigured
   Production/Staging host fails closed rather than minting unverifiable tokens.

## Audiences = trust tiers (Feature 136)

Audiences are **always derived** from `SorchaAudiences(InstallationName)` — `JwtSettings:Audience`
config is **intentionally ignored**. Each token carries exactly one installation-namespaced,
tier-scoped audience:

| Tier | Audience (`localhost` install) | Who | Notable claims |
|------|--------------------------------|-----|----------------|
| **Consumer** | `localhost:consumer` | citizen / wallet holder (web + PWA) | `sub`, `email`, `platform_user_id`, `org_id`/`org_name`; **no `roles`, no `wallet_address`** |
| **Platform** | `localhost:platform` | admin / designer / auditor / org operator | + `org_id`, `org_name`, `roles[]`, `wallet_address?` |
| **Service** | `localhost:service` | service-to-service / internal | `client_id`, `service_name`, `scope[]`, `delegated_*?` |
| **Enrol-session** | `localhost:enrol-session` | one-time device pairing | `scope:"enrol"`, single-use `jti` |

**Authenticate broad, authorize narrow.** The bearer pipeline accepts **any** of the installation's
four tier audiences (`ValidAudiences = SorchaAudiences.All`) and rejects tokens from other
installations. The *specific* tier is then enforced **per endpoint** by policies (below). Mint and
validate both resolve issuer + audiences through the same `SorchaIssuer`/`SorchaAudiences`, or
tokens self-reject.

### Authorization policies

Registered by `AddSorchaAuthorizationPolicies` (every service calls it):

| Policy | Admits |
|--------|--------|
| `RequireConsumerAudience` | consumer-tier tokens — citizen/wallet surfaces (`/api/v1/wallet`, application submission) |
| `RequirePlatformAudience` | platform-tier tokens — **compose on top of a role policy**, e.g. `RequireAdministrator` + `RequirePlatformAudience` |
| `RequireService` | `token_type==service` **and** `:service` audience — `/api/internal/*`, docket writes |

Genuinely cross-tier endpoints (e.g. `/me/inbox`) stay plain `.RequireAuthorization()`. For gates
that must admit a service caller **or** a specific human tier, fold the audience into the
requirement (a consumer token also carries `org_id`, so `hasOrgId || isService` alone would leak) —
`CanManageBlueprints` and `CanRecoverSystemWallet` are the canonical tier-folded examples.

## Configuration reference

```yaml
environment:
  JwtSettings__InstallationName: ${INSTALLATION_NAME:-localhost}   # drives issuer + audiences
  JwtSettings__SigningKey:       ${JWT_SIGNING_KEY}                # shared HMAC key (all services)

  # Optional
  JwtSettings__Issuer: "https://auth.example.com"   # explicit issuer override (audiences are NOT overridable)
  JwtSettings__AccessTokenLifetimeMinutes: 60
  JwtSettings__RefreshTokenLifetimeHours:  24
  JwtSettings__ServiceTokenLifetimeHours:  8
  JwtSettings__ClockSkewMinutes: 5
```

> The pre-136 keys `JwtSettings__Audience__0/__1` no longer do anything — remove them. Audiences are
> the four derived tier strings.

## Signing key management

- **Development:** a shared key is auto-generated and persisted (`%LOCALAPPDATA%\Sorcha\dev-jwt-signing-key.txt`); docker-compose uses the `JWT_SIGNING_KEY` default. Never use these in production.
- **Production:** supply `JwtSettings__SigningKey` from a secret store (Azure Key Vault recommended; the wallet/issuance crypto already integrates Azure KMS, Feature 082). HS256 (HMAC-SHA256). Rotate periodically; all services must share the same key.

## Token structure

**Platform (admin/designer) token**

```json
{
  "sub": "00000000-0000-0000-0001-000000000001",
  "email": "admin@sorcha.local",
  "platform_user_id": "…",
  "org_id": "00000000-0000-0000-0000-000000000001",
  "org_name": "Sorcha Local",
  "roles": ["Administrator", "SystemAdmin"],
  "token_type": "user",
  "iss": "urn:sorcha:localhost",
  "aud": "localhost:platform"
}
```

**Consumer (citizen / wallet) token** — note: no `roles`, no `wallet_address`; carries home/public `org_id`:

```json
{
  "sub": "…", "email": "citizen@example.com", "platform_user_id": "…",
  "org_id": "…", "org_name": "…",
  "token_type": "user",
  "iss": "urn:sorcha:localhost", "aud": "localhost:consumer"
}
```

**Service token**

```json
{
  "client_id": "service-blueprint", "service_name": "Blueprint Service",
  "scope": ["blueprints:read", "blueprints:write"],
  "token_type": "service",
  "iss": "urn:sorcha:localhost", "aud": "localhost:service"
}
```

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `Invalid audience` | Token's `aud` isn't one of this installation's four tiers — usually a mismatched `InstallationName` across services, or a token from a different installation. Confirm `docker-compose config \| grep InstallationName` is identical everywhere. |
| `403` on an endpoint despite a valid token | The token authenticated (right installation) but is the **wrong tier** for that endpoint (e.g. a consumer token on a `RequirePlatformAudience` admin route). Expected — use a platform-tier token. |
| `Invalid signature` | Services have different `JwtSettings__SigningKey`. Make them identical and restart. |
| Host **fails to start** in Production/Staging with an issuer error | Issuer could not be resolved (no explicit issuer and no `InstallationName`). Set `JwtSettings__InstallationName` (or an explicit `JwtSettings__Issuer`). This is the intended fail-closed behaviour. |

## Testing

```bash
# Service token (client credentials)
curl -X POST http://localhost/api/service-auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=service-blueprint&client_secret=blueprint-service-secret"

# Inspect the token at jwt.io — confirm iss=urn:sorcha:<install> and aud=<install>:<tier>
```

## References

- `src/Common/Sorcha.ServiceDefaults/Auth/SorchaAudiences.cs` · `SorchaIssuer.cs`
- `src/Common/Sorcha.ServiceDefaults/JwtAuthenticationExtensions.cs` · `AuthorizationPolicyExtensions.cs`
- `src/Services/Sorcha.Tenant.Service/Services/TokenService.cs`
- [Authentication Setup](AUTHENTICATION-SETUP.md) · [Bootstrap Credentials](../getting-started/BOOTSTRAP-CREDENTIALS.md)
