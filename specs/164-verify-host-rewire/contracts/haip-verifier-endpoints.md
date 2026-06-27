# Contract: HAIP verifier endpoints (consumed by `HaipVerificationTransport`)

These endpoints live in `Sorcha.Haip.Service` (`Endpoints/VerifierEndpoints.cs`) and are **consumed, not
modified** by B3. HAIP's server-side validation behaviour is untouched (FR-007). The transport binds to the
**merged (post-B1)** shape, where the result poll returns the raw `vp_token`.

> Worktree-staleness note: the *local* worktree's poll returns a flat `VerificationResult` **without**
> `vp_token` (pre-B1 shape). The transport must target the merged poll that returns `vp_token` (B1 #1044).
> Confirm the live shape against a running node before/while implementing.

## Endpoints

| Operation | Route | Caller tier | Purpose for B3 |
|---|---|---|---|
| Create presentation request | `POST /api/v1/verifier/requests` | consumer (PWA) / org (desk) | Transport `StartAsync` — create the OID4VP request from the chosen question + verifier identity. |
| Request object | `GET /api/v1/verifier/requests/{requestId}/request-object` | public | Provides the signed Request Object the QR deep-link points at (holder fetches it). |
| Direct-post (holder) | `POST /api/v1/verifier/requests/{requestId}/direct-post` | public (holder) | Holder posts the `vp_token` — driven by the holder's Present flow, not by B3's transport. |
| Result poll | `GET /api/v1/verifier/requests/{requestId}/result` | consumer (PWA) / org (desk) | Transport `PollAsync` — returns state and, **post-B1, the raw `vp_token`** (+ delegation). |

## Create-request (StartAsync) — expected I/O

- **In**: chosen question (credential type, required/optional claims, purpose) + the verifier identity
  (`client_id`: P-256 JWK thumbprint for PWA, `did:sorcha:verifier:{orgId}` for desk).
- **Out**: a request id (→ `SessionId`) and the data needed to build the QR deep-link (request URI pointing
  at the request-object endpoint). `response_type=vp_token`, `response_mode=direct_post`.

## Result-poll (PollAsync) — expected I/O

- **In**: request id, verifier token (tier-scoped).
- **Out (pending)**: state indicating not-yet-responded; **no `vp_token`**.
- **Out (complete)**: state complete **plus the raw `vp_token`** and optional delegation — the transport
  surfaces these for client-side verdict computation by `IVerifiablePresentationValidator`. B3 does **not**
  consume HAIP's server-side verdict for the human-verifier trail (FR-007).
- **Out (expired/error)**: distinguishable terminal states so the transport maps to `Expired` / `Error`.

## Tier / audience (FR-008 — confirm live)

The Feature 136 audience policies gate these endpoints. B3 requires that **both**:
- the **consumer** tier (PWA citizen token), and
- the **org/desk** tier (desk verifier token)

are accepted on create-request and result-poll. This MUST be confirmed against a **live node**; if a tier is
rejected, apply the minimal HAIP-side allowance and record the observed status codes in `quickstart.md`. The
public request-object and direct-post endpoints stay anonymous (holder-facing).
