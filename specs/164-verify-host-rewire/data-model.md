# Phase 1 Data Model: Verify Host Rewire (B3)

B3 introduces **no new persisted entities**. The verify session lives in HAIP's existing
`PresentationRequestStore`; the verdict is computed client-side and not stored by B3 (the PWA's existing
`IVerificationHistoryStore` continues to persist its own record, unchanged). The entities below are the
in-flight / view models that flow through the shared control and the new transport. Most are **owned by B2**
and consumed here; only the transport's session DTO may be new to B3.

## Entity: Verification session (in-flight)

The live verify exchange, created and polled via `IVerificationTransport`. Not stored in any host-local
store after B3 (the desk `InMemoryVerifierSessionStore` is removed).

| Field | Type | Notes |
|---|---|---|
| `SessionId` | string (non-empty) | Returned by the transport on create; opaque HAIP request id. |
| `Question` | `VerificationPreset` | The chosen "what to verify" (see below). |
| `RequestUri` / `QrDeepLink` | string (URI) | Scannable deep-link the holder's Present flow consumes. |
| `State` | enum: `Pending` \| `Complete` \| `Expired` \| `Error` | Drives the control's UI states. |
| `VpToken` | string? | Raw `vp_token`, present only when `State == Complete`. |
| `Delegation` | string? | Optional delegation credential, present when supplied by the holder. |
| `Error` | string? | Terminal error detail when `State == Error`. |

**Validation / rules**:
- On create: `SessionId` non-empty and `QrDeepLink` present, else the transport surfaces `Error` (never a
  "not configured" sentinel — that path belongs only to the retired stub).
- `VpToken` MUST be null unless `State == Complete` (FR-001 — no early disclosure).
- State is monotonic toward a terminal state (`Complete` / `Expired` / `Error`); the poll never silently
  completes (FR-013).

**State transitions**:

```text
(create) ──> Pending ──poll(holder responded)──> Complete
                │
                ├──poll(session TTL elapsed)────> Expired
                └──poll(transport/network/tier)─> Error
```

## Entity: Verification preset (question) — owned by B2

The "what to verify" definition, sourced from `IVerificationPresetCatalogue` (`DefaultPresetCatalogue`
bundled default, offline fallback). Consumed by `QuestionSelectionPanel`; identical on both hosts.

| Field | Type | Notes |
|---|---|---|
| `Key` | string | Stable preset identifier. |
| `Label` | string | Display name in the selection panel. |
| `Purpose` | string | Human-readable reason shown to the holder. |
| `CredentialType` | string | Target credential type. |
| `RequiredClaims` | string[] | Claims the holder must disclose. |
| `OptionalClaims` | string[] | Claims the holder may disclose. |

**Rules**: B3 does not redefine presets; any host-local duplicate is removed (FR-011). A custom question is a
preset built ad-hoc in the panel from the same shape.

## Entity: Verifier identity (pluggable per host)

The requester identity embedded in the create-request so the holder's wallet shows the correct requester.

| Variant | Source | Shape | Lifetime |
|---|---|---|---|
| Ephemeral (PWA) | `IEphemeralVerifierIdentityService` (WebCrypto) | EC P-256 JWK; `client_id` = JWK thumbprint (RFC 7638) | fresh per session |
| Stable (desk) | desk DI (`PresentationRequestBuilder` replacement) | `did:sorcha:verifier:{orgId:N}` | org/deployment-scoped |

**Rules**: The transport consumes the identity via an abstraction (not hard-coded). The identity actually
used MUST be reflected in the presentation request the holder sees (FR-005).

## Entity: Verdict trail (view model) — owned by B2

The client-side 4-layer outcome rendered by `VerdictTrailPanel`. Computed by `IVerifiablePresentationValidator`
from the returned `vp_token`; `VerdictViewModel` is the shared view model (no host-local duplicate).

| Layer | Meaning |
|---|---|
| Selective disclosure | Which claims the holder disclosed vs. the request. |
| Live presentation / KB-JWT | Holder proof-of-possession / key-binding freshness. |
| Issuer signature | Issuer SD-JWT VC signature validity. |
| Revocation | Status-list / revocation check. |
| Register-anchor (affordance) | On-demand cross-check against the public register via `IRegisterAnchorClient`. |

**Rules**:
- Headline is `Pass` / `Warn` / `Fail`. The `Warn` state is **preserved by the client-side verdict** even
  though HAIP's flat server verdict has none (FR-006/007; edge case "vp_token validation fails").
- The register-anchor cross-check is on-demand; if the public register read is unavailable, that layer
  reports "could not complete" **without** invalidating the already-computed crypto layers (edge case).

## Relationships

```text
VerificationPreset ──(chosen in)──> Verification session ──(returns)──> vp_token
        ▲                                   │
        │ from IVerificationPresetCatalogue  │ validated by IVerifiablePresentationValidator
        │                                    ▼
   QuestionSelectionPanel            VerdictViewModel (4 layers + anchor) ──> VerdictTrailPanel

Verifier identity ──(embedded in create-request by)──> IVerificationTransport (HaipVerificationTransport)
```
