# Research — Open Verifier PWA

Phase 0. All decisions grounded in the existing codebase (verifier engine + Register Service F079 +
AssuredIdentity walkthrough). No open NEEDS CLARIFICATION items remain.

## R-001 — Where the per-layer validation results live

**Decision**: Enrich `VerificationOutcome` (`Sorcha.Verifier.Engine/Models/VerifierSession.cs`) with a
structured `IReadOnlyList<ValidationLayerResult>` populated by `VerifiablePresentationValidator`. Add the
register-anchor layer as a result the **verifier app** appends after calling the anchor endpoint (the
engine stays HttpClient-free).

**Rationale**: Today the validator collapses everything into `Errors: IReadOnlyList<string>` + a single
`Accepted` bool + `IssuerSignature` enum. The trail UI (US2) needs per-step status + human-readable
detail, and FR-013 needs "failed" vs "unverified" per layer. The validator already computes each check
(presentation/KB-JWT, issuer signature, delegation status-list); it just discards the structure. We
surface it instead of re-deriving.

**Alternatives considered**: (a) Re-parse `Errors` strings in the UI — brittle, rejected. (b) A second
verification pass in the app — duplicates engine logic, rejected.

## R-002 — Layer 3 (revocation) is already wired; only surfaced

**Decision**: Reuse `IStatusListCache.CheckAsync(uri, index, expectedIssuer)` → `StatusListVerdict`
(Active/Revoked/Unverifiable). Map `Active→pass`, `Revoked→fail`, `Unverifiable→unverified`. The status
list URI is read from the credential/delegation (not hard-coded), so the open verifier dereferences
whatever public list the credential points at (IETF Token Status List JWT, `application/statuslist+jwt`,
served anonymously per `StatusListEndpoints.cs`).

**Rationale**: F138 already hardened this (fail-closed, issuer-pinned, freshness). No new logic.

## R-003 — Issuer signature (layer 2) and the "fully open" posture

**Decision**: Run with `requireIssuerSignature: true` and the **composite** resolver
(`DidResolverBackedIssuerKeyResolver` → `JwkRegistryIssuerKeyResolver`). No allowlist. Surface the
resolved issuer org identity (DID + display name) on the verdict. The verifier app MUST be configured
with a `ServiceAuth:ClientId` so the DID-backed resolver is active (resolves `did:sorcha:org:` via the
F120 registry).

**Rationale**: "Open" = resolve-and-verify, not match-a-list. The signature must genuinely verify for the
"✓ verified" demo. The composite already exists; the demo-mint path keeps the JWK registry fallback.

**Dependency / setup**: the AssuredIdentity issuing org MUST have an org master key
(`Set-SorchaOrgMasterKey`) so its `iss` is a resolvable `did:sorcha:org:{C}` with a `#vc-issuance-n` kid
— otherwise it falls to the bare-wallet-`iss` path and the signature is unresolvable (the documented
split-brain). This is a setup prerequisite, not a verifier defect.

## R-004 — Layer 4 anchor: discovery key and the new public read

**Decision**: The credential carries a **registerId** anchor claim (disclosable); the credential's own
**jti / credentialId** is the lookup key (it is already in the SD-JWT, no need to inject it pre-issuance).
Add a new **public** Register Service endpoint
`GET /api/registers/{registerId}/credentials/{credentialId}/anchor` that finds the credential-issuance
transaction (via a new `GetCredentialIssuanceTransactionAsync` repo method querying
`MetaData.TrackingData["credentialId"]`, which `ActionExecutionService.RecordCredentialOnRegisterAsync`
already writes), and returns `{ txId, docketNumber, sealedAt, inclusionProof, status }`.

**Rationale**: F079 already exposes inclusion proofs, but the GET fetch requires `CanReadTransactions`
auth, and there is no find-by-credentialId. The open verifier needs an **anonymous** path keyed off data
the credential already carries. Resolving credentialId pre-issuance is circular (the SD-JWT is built
before the tx seals); using the credential's existing jti sidesteps that entirely. Only `registerId` need
be added as a claim.

**Alternatives considered**: (a) Embed `did:sorcha:r:{registerId}:t:{txId}` — impossible (circular txId),
rejected. (b) Operator-selected register (explorer style) — rejected in brainstorm (less open). (c)
Post-seal re-anchor + re-deliver — two-pass issuance, heavier, deferred.

**Security note**: anonymous exposure is limited to public-register issuance facts + Merkle proof;
high-entropy credentialId mitigates enumeration; documented accepted exposure (Constitution II note).

## R-005 — "Age over 18?" expressed as a pre-issued boolean

**Decision**: AssuredIdentity issues `age_over_18: true` as a selectively-disclosable claim (computed
from DOB at issuance by the verification analyst action / a derived field). The "Age over 18?" preset
requests only `age_over_18` + `portrait`. DOB/name/address are issued but not requested, so they are
withheld.

**Rationale**: Real selective disclosure on the genuine OID4VP/SD-JWT path; ISO 18013-5 defines
`age_over_NN` for exactly this. Avoids revealing DOB. ZK predicates (BBS) are out of scope.

**Edit points**: `assured-identity.json` `credentialIssuanceConfig.claimMappings` + `disclosable[]`; the
action data must carry the boolean (derive in the analyst action or compute pre-mint). `portrait` already
exists as `/portrait/tokenImageBase64`.

## R-006 — PWA installability on Blazor Server (path A)

**Decision**: Add `manifest.webmanifest` + icons + a hand-written `service-worker.js` (cache static shell
+ offline-fallback page; the SignalR circuit is not cached) + a `beforeinstallprompt` install button.
Register the SW from `App.razor`. **SW + manifest must be served and scoped under the `/verify/` gateway
mount** — `start_url`/`scope` set to the `/verify/` base, and the gateway must pass the SW path through
without rewriting its scope.

**Rationale**: Smallest path to installability; the verifier is inherently online so no offline crypto is
needed. WASM/offline (path B) is documented roadmap, out of scope.

**Gotcha**: the wallet PWA's path-prefix + nginx-immutable-cache lessons apply — do not cache the SW or
host page as immutable; scope the SW to `/verify/`.

## R-007 — Transport unchanged

**Decision**: Keep `PresentationRequestBuilder` (OID4VP `openid4vp://`, PEX `presentation_definition`,
`direct_post`), `IVerifierSessionStore`, and `PresentationResponseEndpoints` as-is. Only the request's
requested-claim set changes per preset, and `/status` returns the enriched outcome.

**Rationale**: The transport works (post issue-#808 fixes); rebuilding it is out of scope and risky.
DCQL / W3C Digital Credentials API are noted as future interop upgrades, not in scope.
