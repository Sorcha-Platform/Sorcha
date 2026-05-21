# Contract: Issuer resolution & fail-closed startup

## Resolution order (at service startup, in `JwtAuthenticationExtensions`)

1. **Explicit** `JwtSettings:Issuer` set → use it verbatim (operators owning a domain may set a URL).
2. Else `JwtSettings:InstallationName` set → derive **`urn:sorcha:{InstallationName}`** (opaque, non-domain).
3. Else:
   - **Production / Staging** → **throw at startup** with an actionable message (mirrors the existing `SigningKey` requirement). The service does not start.
   - **Development** → `urn:sorcha:dev-local` (clearly local, never a real domain).

The `https://tenant.sorcha.io` default is **removed**.

## Validation

- `ValidateIssuer = true`, `ValidIssuer = <resolved issuer>` (unchanged flags; the value is now installation-specific with no shared fallback).
- `InstallationName` drives **both** the issuer (above) and the audience namespace (`SorchaAudiences`), so they cannot be configured inconsistently.

## Configuration keys

| Key | Meaning | Default |
|-----|---------|---------|
| `JwtSettings:InstallationName` | installation namespace for issuer + audiences | `sorcha` |
| `JwtSettings:Issuer` | explicit issuer override | (none — derived) |
| `JwtSettings:SigningKey` | HMAC secret (per installation) | required in prod (unchanged) |

## Examples

| Config | Resolved issuer | Audiences |
|--------|-----------------|-----------|
| `InstallationName=n1` | `urn:sorcha:n1` | `n1:consumer`, `n1:platform`, `n1:service`, `n1:enrol-session` |
| `InstallationName=acme`, `Issuer=https://id.acme.example` | `https://id.acme.example` | `acme:*` |
| nothing set, Production | **startup fails** | — |
| nothing set, Development | `urn:sorcha:dev-local` | `sorcha:*` |

## Invariants

- Two installations with distinct `InstallationName`/`SigningKey` never accept each other's tokens (signature fails first; issuer + audience are secondary). (SC-003)
- A production-like installation with neither issuer nor installation name fails to start. (SC-004)
