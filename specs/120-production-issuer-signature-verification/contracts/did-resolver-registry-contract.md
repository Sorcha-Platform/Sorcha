# Contract: `IDidResolverRegistry.ResolveWithAlsoKnownAsAsync`

**Feature**: 120-production-issuer-signature-verification
**Phase**: 1 (interface contract)
**Date**: 2026-05-09

## Scope

This document is the contract for the new method added to `IDidResolverRegistry` (`src/Common/Sorcha.ServiceClients.Http/Did/IDidResolverRegistry.cs`). The method packages cross-resolution + key-material verification + caching as a single operation so every credential-consuming surface (verifier, wallet inbox projector, future validator) inherits identical trust semantics.

The contract is consumed by `DidResolverBackedIssuerKeyResolver`, by `CredentialMatcher`'s equivalence-aware matching path, and by any future caller that needs trustworthy DID resolution following `alsoKnownAs` links.

## Interface

```csharp
namespace Sorcha.ServiceClients.Did;

public interface IDidResolverRegistry
{
    // EXISTING — unchanged
    Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default);
    void Register(IDidResolver resolver);

    // NEW for Feature 120
    /// <summary>
    /// Resolves the primary DID and any DIDs declared in its alsoKnownAs property,
    /// verifies the same verification key material appears in every linked document,
    /// and returns the merged DidDocument. An unresolvable link is advisory — it is
    /// skipped and equivalence is withheld from it, never invalidating the primary.
    /// Returns null only if the primary fails to resolve, or if verification keys
    /// diverge across the links that DID resolve.
    /// </summary>
    /// <param name="did">The primary DID to resolve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Merged DidDocument containing the union of verification methods that match
    /// across all resolved equivalent documents, or null on any cross-resolution failure.
    /// </returns>
    Task<DidDocument?> ResolveWithAlsoKnownAsAsync(string did, CancellationToken ct = default);
}
```

## Behavioural contract

### Inputs

- `did`: a DID string. May be of any method registered with the registry. Empty/null returns null.
- `ct`: standard cancellation token. Honoured throughout cross-resolution.

### Outputs

- **Success**: a non-null `DidDocument` whose `verificationMethod` array contains every VM that matches by key material across the entire equivalence chain. The returned document's `id` equals the input DID; `alsoKnownAs` lists the verified equivalent DIDs.
- **Failure**: returns null. Span/log records the specific failure reason via the `did.alsoKnownAs.match` attribute (`unreachable` | `mismatch` | `none`).

### Resolution algorithm (deterministic)

```
1. Resolve primary DID via underlying ResolveAsync(did).
   On null: return null. Tag span outcome=did-unresolved.

2. If primary.alsoKnownAs is null or empty:
   Tag span did.alsoKnownAs.cross_resolved=false.
   Return primary (passthrough).

3. For each linked DID in primary.alsoKnownAs:
   a. Avoid cycles: if linked == did or already-visited, skip.
   b. Resolve linked via ResolveAsync(linked).
      On null (ADVISORY — an unverifiable hint must never veto a verified identity;
      W3C DID Core §5.1.3: "the presence of an alsoKnownAs assertion does not prove
      that this assertion is true"):
        Log warning "alsoKnownAs link unreachable for {did}: {linked}".
        Increment the alsoKnownAs-unreachable metric.
        SKIP this link — equivalence is withheld from it and its key material is
        never merged. Continue to the next link.
   c. Compute the intersection of verification-method key material between
      primary and linked:
        For each VM in primary.verificationMethod:
          Look up matching VM in linked.verificationMethod by raw public key bytes
          (decoded from publicKeyMultibase or publicKeyJwk).
          If no match: this primary VM is NOT cross-verified.
          If match: this primary VM IS cross-verified (and so is the linked VM).
      A cross-verified VM is one whose public key bytes appear in every
      linked document. Other VMs are dropped.

4. If at least one link resolved AND primary.verificationMethod has ZERO
   cross-verified VMs:
   Log warning "alsoKnownAs cross-resolution found no shared keys for {did}".
   Tag span did.alsoKnownAs.match=mismatch.
   Return null.
   (With no link resolved there was no intersection to survive, so this branch
   does not apply — the primary passes through with its own key material.)

5. Construct merged DidDocument:
   - id: primary.id
   - alsoKnownAs: subset of primary.alsoKnownAs that resolved successfully
   - verificationMethod: only the cross-verified VMs from primary
   - assertionMethod, authentication, etc.: preserved from primary, filtered
     to reference only cross-verified VMs

6. Tag span did.alsoKnownAs.cross_resolved=(any link verified),
   did.alsoKnownAs.match=match when a link verified, else unreachable.
   Return merged document.
```

### Caching

Cross-resolution results are cached at the registry layer. Cache key: canonical primary DID. Cache value: the merged document (success) or a sentinel "negative" entry (failure, prevents thundering-herd retries against an unreachable link).

Per-method TTLs:

| Method | Positive TTL | Negative TTL |
|---|---|---|
| `did:web` | 1h | 60s |
| `did:sorcha:*` | infinite (within process); invalidated on `transaction:confirmed` Redis-stream events | 60s |
| `did:key` | infinite | N/A (no resolution can fail for a syntactically valid did:key) |

Negative-TTL entries are explicitly short to avoid masking transient failures (network blip, brief unavailability) for too long.

Configuration override surface: `DidResolver:Cache:WebTtlMinutes` (default 60), `DidResolver:Cache:NegativeTtlSeconds` (default 60). Both honoured by `DidResolverCache`.

### Concurrency

Multiple concurrent calls for the same DID coalesce: only one resolution chain runs; subsequent callers wait on the in-flight task. Implemented via `LazyAsync` pattern over the cache.

### Idempotency

Pure: same input → same output (within cache TTL). No side effects.

### Telemetry

The method participates in the following spans/metrics:

- **Span `did.resolve.cross`** (Internal): parents the underlying primary + per-link `did.resolve` spans. Tagged with `did.input`, `did.method`, `did.alsoKnownAs.cross_resolved`, `did.alsoKnownAs.match`, `did.alsoKnownAs.link_count`.
- **Counter `sorcha_did_resolver_cache_hit_total{method, kind}`** where kind ∈ {primary, alsoKnownAs}.
- **Counter `sorcha_did_resolver_cache_miss_total{method, kind}`**.
- **Counter `sorcha_did_resolver_cross_resolve_mismatch_total`** — incremented on step 4 (no shared keys found).
- **Counter `sorcha_did_resolver_alsoKnownAs_unreachable_total`** — incremented on step 3b (link fails to resolve).

Meter: `Sorcha.ServiceClients.Did` (existing for primary `ResolveAsync` instrumentation).

### Failure modes (matrix)

| Condition | Return | Log level | Span outcome | Counter incremented |
|---|---|---|---|---|
| Primary DID unresolved | `null` | Warning | `did-unresolved` | (existing primary miss counter) |
| Primary has no `alsoKnownAs` | passthrough document | Debug | `cross_resolved=false` | — |
| Linked DID unreachable | `null` | Warning | `unreachable` | `alsoKnownAs_unreachable_total` |
| Cross-resolved keys mismatch | `null` | Warning | `mismatch` | `cross_resolve_mismatch_total` |
| All links resolved + keys match | merged document | Debug | `match` | (existing hit counter on cache replay) |
| Resolver throws exception | propagate | (let caller handle) | (existing primary error attribution) | — |

### Cycle protection

If during step 3, a linked DID's own `alsoKnownAs` references the primary or a previously-visited DID, the visit is skipped (no infinite loop). The first-pass intersection is the canonical answer; transitive cross-resolution is not performed in v1 (see "Out of scope" below).

### Out of scope (this method)

- **Transitive equivalence resolution.** v1 only cross-resolves direct links from the primary's `alsoKnownAs`. If A links to B and B links to C, but A does not link to C, the v1 method returns A's view (with B cross-verified). Transitive walk is deferred — too easy to introduce subtle correctness bugs in v1.
- **Caching of negative cross-resolution outcomes by link identity.** v1 caches by primary DID only; if A's `alsoKnownAs` link to B is unreachable, the cache entry is keyed on A. A different DID D that also links to B incurs a fresh resolution. Optimisation deferred.
- **Forced refresh API.** v1 does not expose a "bypass cache" parameter. Callers that suspect stale data invalidate via the existing `transaction:confirmed` Redis-stream event mechanism (for `did:sorcha:*`) or wait out the `did:web` TTL.

## Consumer expectations

`DidResolverBackedIssuerKeyResolver` (the verifier-side `IIssuerKeyResolver` impl):

```csharp
public async Task<IssuerPublicKey?> ResolveAsync(string issuer, string kid, CancellationToken ct)
{
    var doc = await _registry.ResolveWithAlsoKnownAsAsync(issuer, ct);
    if (doc is null)
    {
        // Three-way failure-mode classification per FR-003:
        //   - did-unresolved if primary failed
        //   - alsoKnownAs-mismatch if primary OK but cross-resolution failed
        // We don't get the distinction from the return value alone; we read it
        // from the OTel span/counter attribution.
        return null;
    }

    var vm = MatchKid(doc, kid);   // exact match → thumbprint fallback
    if (vm is null)
    {
        // FR-003 failure mode: kid-unmatched
        return null;
    }

    return new IssuerPublicKey(vm, /* metadata for sig verification */);
}
```

The classifier above shows why the contract returns `null` rather than throwing — distinct failure modes are surfaced via the existing telemetry, and the consumer's responsibility is to translate `null` into its own three-way bucket per FR-003.

`CredentialMatcher` (allowlist equivalence):

```csharp
async Task<bool> IsIssuerAcceptedAsync(
    string credentialIssuer,
    IEnumerable<string> acceptedIssuers,
    CancellationToken ct)
{
    if (acceptedIssuers.Contains(credentialIssuer))
        return true;

    var doc = await _registry.ResolveWithAlsoKnownAsAsync(credentialIssuer, ct);
    if (doc?.AlsoKnownAs is { Count: > 0 } aka)
    {
        if (acceptedIssuers.Any(aka.Contains))
            return true;
    }

    foreach (var allowed in acceptedIssuers)
    {
        var allowedDoc = await _registry.ResolveWithAlsoKnownAsAsync(allowed, ct);
        if (allowedDoc?.AlsoKnownAs?.Contains(credentialIssuer) == true)
            return true;
    }

    return false;
}
```

(Pseudocode — actual implementation should batch the allowlist resolutions and short-circuit on first match.)

## Testing

Required unit-test coverage (per Constitution IV, ≥85% on new code):

| Test | Scenario | Expected |
|---|---|---|
| `ResolveWithAlsoKnownAs_NoLinks_PassesThrough` | Primary doc has no `alsoKnownAs`. | Returns primary unchanged. |
| `ResolveWithAlsoKnownAs_OneLinkMatching_ReturnsMerged` | Primary + one link, same VM key material. | Returns merged doc with both alsoKnownAs entries. |
| `ResolveWithAlsoKnownAs_OneLinkUnreachable_ReturnsNull` | Link's `ResolveAsync` returns null. | Returns null. Span tagged `unreachable`. |
| `ResolveWithAlsoKnownAs_OneLinkMismatch_ReturnsNull` | Link resolves but VM keys differ. | Returns null. Span tagged `mismatch`. |
| `ResolveWithAlsoKnownAs_TwoLinks_PartialMatch_ReturnsNull` | Two links, one matches, one mismatches. | Returns null (any mismatch fails). |
| `ResolveWithAlsoKnownAs_Cycle_DoesNotInfiniteLoop` | Primary links to B, B links to primary. | Resolves once, skips revisit. |
| `ResolveWithAlsoKnownAs_CacheHit_DoesNotResolve` | Cached entry within TTL. | No underlying resolver invocations. |
| `ResolveWithAlsoKnownAs_CacheInvalidated_ResolvesFresh` | Cache invalidation event fires. | Next call resolves. |
| `ResolveWithAlsoKnownAs_NegativeCacheRespectedShortTtl` | Failure cached. | Repeat call within 60s returns null without re-resolving; after 60s, retries. |
| `ResolveWithAlsoKnownAs_CoalesceConcurrentCalls` | Two concurrent identical calls. | Only one underlying resolution; both receive same result. |

Tests live at `tests/Sorcha.ServiceClients.Tests/Did/DidResolverRegistryCrossResolutionTests.cs`.
