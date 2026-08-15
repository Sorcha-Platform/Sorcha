# Contract: Service-Auth Token Mint under mTLS (F191)

Endpoint: `POST /api/internal/service-auth/token` (unchanged path). Additionally reachable on
Tenant's mTLS listener (`https://tenant-service:8443`); the plaintext internal listener keeps
serving the secret path unchanged.

## Request (form-urlencoded, unchanged shape)

| Field | Secret path | Cert path |
|---|---|---|
| `grant_type` | `client_credentials` (required) | same |
| `client_id` | required | required |
| `client_secret` | required | **absent** (presence ⇒ secret path is taken) |
| `scope` | optional (space-separated) | same semantics |

Credential in cert path = the TLS-layer client certificate (chain-validated against the Workload
CA bundle at handshake; requests without a validated cert never reach the handler on that
listener).

## Response

Success: identical to today in every respect — same JSON shape, same JWT claims
(`sub`=principal id, `client_id`, `service_name`, `token_type=service`, per-scope `scope`
claims), same `{installation}:service` audience, same lifetime. Downstream consumers cannot
distinguish mint paths.

## Refusal matrix (each row = distinguishable log line; no token issued)

| Condition | HTTP result |
|---|---|
| Cert chains to unknown CA / expired / not-yet-valid | TLS handshake rejection (never reaches handler) |
| No client cert on mTLS listener | handshake rejection (RequireCertificate) |
| SPIFFE SAN ≠ expected id for `client_id` | 400/401 invalid_client — log carries both identities |
| Principal missing or not Active | 401 (same as secret path today) |
| Empty scope intersection | same as secret path today (unchanged code) |
| `client_secret` present ∧ `DisableSharedSecrets=true` | explicit "shared secrets disabled" error (400 invalid_client family) |
| `client_secret` present ∧ flag false | legacy Argon2id path, byte-for-byte unchanged |

## `POST /api/internal/service-auth/token/delegated`

Same credential substitution: a chain-validated client cert with matching SPIFFE id replaces
`client_id`+`client_secret`; delegation semantics (`tenant:delegate` scope requirement, 5-min
user-bound token) unchanged. `DisableSharedSecrets=true` refuses its secret form too.

## `POST /api/internal/service-auth/rotate-secret`

Unchanged. Inherently secret-bound; becomes inert once a deployment disables shared secrets
(documented in AUTHENTICATION-SETUP).

## OpenAPI

`.WithSummary()`/`.WithDescription()` updated on the token endpoints to state that
`client_secret` is optional when the request arrives over the mutual-TLS listener with a valid
workload certificate.
