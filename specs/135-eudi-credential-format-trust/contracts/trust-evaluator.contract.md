# Contract: ITrustEvaluator / ITrustResolverRegistry (service contract, not REST)

The single trust authority consulted by **both** verification paths (FR-007). Lives in `Sorcha.Blueprint.Engine.Credentials` (WASM-friendly; network sources injected).

## ITrustEvaluator

```
Task<TrustDecision> EvaluateAsync(
    IssuerContext issuer,         // resolved issuer id, presented key(s), format, raw signature material
    TrustPolicy policy,           // from the requirement (or synthesised default)
    CancellationToken ct);
```

Behaviour:
1. Resolve the issuer key (x5c → trust source / DID) and **verify the issuer signature** — `TrustDecision.SignatureValid` is set truthfully (no `=false` shortcut).
2. For each `TrustSourceRef`, call the matching `ITrustSourceResolver.VouchAsync`; combine per `combinator`.
3. Check revocation via `IStatusListChecker` (fail-closed by `RevocationCheckPolicy`).
4. Establish assurance (source-tier + upward-only claim override; default Low) and compare to `minAssuranceLevel`.
5. Produce `TrustEvidence` (vouching source, register height / CRL version / trust-list id+freshness, policy digest).
6. **Fail closed** on any unresolved input (untrusted, invalid sig, revoked, unavailable, insufficient assurance).

## ITrustResolverRegistry (mirrors IDidResolverRegistry)

```
void Register(ITrustSourceResolver resolver);
ITrustSourceResolver Resolve(TrustSourceKind kind);
```

## ITrustSourceResolver (one per kind)

```
TrustSourceKind Kind { get; }
Task<TrustSourceVouch> VouchAsync(IssuerContext issuer, TrustSourceRef source, CancellationToken ct);
// TrustSourceVouch: { Vouched:bool, Assurance:AssuranceLevel, Evidence-fragment, Reason? }
```

| Kind | Network dependency (injected) | WASM/offline variant |
|---|---|---|
| `register` | `IDidResolverRegistry` + `IssuerEquivalenceMatcher` | in-memory pinned DID docs |
| `x509-tenant` | `ITrustProvider` (root + CRL) | pinned root + CRL snapshot |
| `trustlist` | `ITrustListProvider` | pinned snapshot |
| `did-allowlist` | `ResolveWithAlsoKnownAsAsync` | pinned allowlist |

## IStatusListChecker (unifies W3C + IETF)

```
Task<StatusListBit> CheckAsync(StatusReference statusRef, CancellationToken ct);
```
`BitstringStatusListChecker` (W3C) and `IetfTokenStatusListChecker` (IETF) both implement/adapt to it.

## ICredentialFormatHandler (the format seam)

```
CredentialFormat Format { get; }
Task<FormatVerifyResult> VerifyAsync(PresentedCredential pres, CredentialRequirement req, ITrustEvaluator trust, CancellationToken ct);
Task<IssuedCredential>   IssueAsync(CredentialIssuanceConfig cfg, IReadOnlyDictionary<string,object> claims, IIssuerSigner signer, CancellationToken ct);
```
Implementations: `SdJwtVcFormatHandler`, `MdocFormatHandler`. Both call `ITrustEvaluator` for the trust decision — the handler owns format/crypto, the evaluator owns trust.

## Acceptance mapping

- FR-007/008/014/015/016, SC-001/002/005/007.
