# Contract: Verification behaviour

No new HTTP endpoints. These are the behavioural contracts each component must satisfy after this feature.

## H3 — Device (PWA) verifier outcome

| Condition | `VerificationOutcome` | PWA `VerifyOutcome` |
|-----------|-----------------------|---------------------|
| Issuer key resolves, issuer JWS valid, holder-chain + status OK | `Accepted=true`, `IssuerSignature=Verified` | `Pass` |
| Issuer key unresolved, `requireIssuerSignature=false`, holder-chain + status OK | `Accepted=true`, `IssuerSignature=NotVerified` | `Warn` (+ "issuer not verified — offline / reduced assurance") |
| Issuer key resolves but JWS **invalid** | `Accepted=false` | `Fail` |
| Issuer key unresolved, `requireIssuerSignature=true` (server gate) | `Accepted=false` | (server rejects; not a PWA path) |
| Holder-chain or status-list check fails | `Accepted=false` | `Fail` |

**Server-verifier invariant:** Blueprint Service + desk `Sorcha.Verifier` (both `requireIssuerSignature=true`) — an `Accepted` outcome always carries `IssuerSignature=Verified`; **no behaviour change**.

## M3a — OIDC ID-token validation (`ValidateIdTokenAsync`)

| Condition | Result |
|-----------|--------|
| Signature valid against provider JWKS, and iss/aud/exp/nonce all pass | **Accept** — claims returned |
| Signature invalid / token tampered | **Reject** (throw) |
| No JWKS key matches the token `kid` (after one refresh) | **Reject** (throw) |
| Provider JWKS unobtainable (network / unconfigured key location) | **Reject** (throw, fail-closed) |
| Any existing check fails (iss / aud / exp / nonce) | **Reject** (throw) — unchanged |

## M3b — Recovery guard

| Condition | Result |
|-----------|--------|
| `Features:WalletRecoveryEnabled` disabled (default) | Endpoint refused by the feature gate — unchanged |
| Feature enabled, passkey-recovery path reaches unwrap | **Throw `NotSupportedException`** — no wallet re-key |
| Feature enabled, org-recovery path reaches unwrap | **Throw `NotSupportedException`** — no wallet re-key |

## Cross-cutting

- All three are fail-closed / honest: nothing is presented as fully verified, trusted, or recovered without the corresponding cryptographic check — or, for the offline device case, the absence of the issuer check is made visible (`Warn`).
