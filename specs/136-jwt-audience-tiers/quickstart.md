# Quickstart: Tiered-Audience JWT Identity Model

## For operators — configuring an installation

A single key drives identity. Set the installation name (and, in production, the signing secret):

```jsonc
// appsettings.{env}.json  (or env vars / Key Vault)
"JwtSettings": {
  "InstallationName": "n1",          // → issuer urn:sorcha:n1, audiences n1:consumer|platform|service|enrol-session
  "SigningKey": "<from Key Vault>"   // required in Production (HMAC secret, per installation)
  // "Issuer": "https://id.acme.example"  // OPTIONAL explicit override; omit to use urn:sorcha:{InstallationName}
}
```

Rules:
- **No shared default issuer.** In Production/Staging the service **fails to start** if neither `Issuer` nor `InstallationName` is set. In Development it falls back to `urn:sorcha:dev-local`.
- `InstallationName` defaults to `sorcha` if you set it explicitly to that; the *absence* of any identity config is what fails closed.
- White-label: set `InstallationName` to the operator's namespace; all four audiences and the issuer follow automatically.

## What you get

| `InstallationName` | Issuer | Audiences |
|--------------------|--------|-----------|
| `n1` | `urn:sorcha:n1` | `n1:consumer`, `n1:platform`, `n1:service`, `n1:enrol-session` |
| `acme` (+ explicit `Issuer`) | your value | `acme:*` |

## For developers — verifying the boundaries

1. **Mint tokens** for the same person at each human tier (consumer login vs platform/switch-org).
2. **Tier isolation**: present the consumer token to a `/platform/*` endpoint → refused; present the platform token to `/api/v1/wallet/*` → refused.
3. **Service isolation**: present a human token to `/api/internal/*` → refused; present a service token there → accepted; present the service token to a human endpoint → refused.
4. **Cross-installation**: configure a second installation with a different `InstallationName`+`SigningKey`; a token from the first is rejected by the second (signature fails first).
5. **Fail-closed**: unset `Issuer` and `InstallationName` in a Production profile → service refuses to start.
6. **Over-request**: as a roleless user, request `tier=platform` → 403 (not a consumer token).

## Running the tests

```bash
# Unit (SorchaAudiences, TierResolver, issuer resolution, policies)
dotnet test tests/Sorcha.ServiceDefaults.Tests
dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~Tier|FullyQualifiedName~Issuer|FullyQualifiedName~Audience"

# Integration (endpoint tier enforcement) — per service test project
dotnet test tests/Sorcha.Wallet.Service.IntegrationTests

# E2E: wallet accepts a :consumer token (Docker up; use vstest, NOT dotnet test --filter — see issue #818)
dotnet build tests/Sorcha.UI.E2E.Tests -c Debug
dotnet vstest tests/Sorcha.UI.E2E.Tests/bin/Debug/net10.0/Sorcha.UI.E2E.Tests.dll --TestCaseFilter:"FullyQualifiedName~ConsumerAudience"
```

## Rollout note

No migration: deploy the config change; existing tokens expire and holders re-authenticate. There is no dual-audience grace period (that would re-open the boundary).
