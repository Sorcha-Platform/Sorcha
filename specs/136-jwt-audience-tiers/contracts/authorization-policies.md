# Contract: Authorization policies & endpoint tier classification

Authenticate-broad / authorize-narrow. Authentication accepts any installation-tier audience; these policies enforce the tier per endpoint.

## Policies (in `Sorcha.ServiceDefaults`)

| Policy | Succeeds iff | Notes |
|--------|--------------|-------|
| `RequireConsumerAudience` | `aud == SorchaAudiences.For(Consumer)` | + standard authenticated checks |
| `RequirePlatformAudience` | `aud == SorchaAudiences.For(Platform)` | the default for unclassified human endpoints |
| `RequireService` (extended) | `aud == SorchaAudiences.For(Service)` **AND** `token_type == "service"` | today checks only `token_type`; add the audience assertion |
| (enrol-session) | existing single-use validation against `*:enrol-session` | unchanged |

Existing role policies (`RequireAdministrator`, `RequireDesigner`, …) remain and compose **on top of** `RequirePlatformAudience` (tier gate first, capability gate second).

## Endpoint classification rules

| Surface | Tier policy |
|---------|-------------|
| `/api/internal/*` (all services) | `RequireService` |
| Wallet `/api/v1/wallet/*`; citizen `/me/*` consumer reads; persona consumer surface | `RequireConsumerAudience` |
| Admin / designer / org-management / `/platform/*` / IdP config | `RequirePlatformAudience` |
| **Unclassified authenticated endpoint** | **`RequirePlatformAudience` (safe default)** |

## Invariants

- A consumer token is refused at every platform and internal endpoint; a platform token at every consumer and internal endpoint; a service token at every human endpoint. (SC-002)
- No endpoint is left with "any tier accepted" — the unclassified default is platform, never permissive. (SC-007)
- Tier enforcement is independent of and additional to role checks: a platform token with an admin role is still refused at a consumer endpoint.
