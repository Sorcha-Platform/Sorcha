// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Default implementation of <see cref="IDidResolverRegistry"/> that delegates
/// resolution to method-specific <see cref="IDidResolver"/> instances.
/// </summary>
public class DidResolverRegistry : IDidResolverRegistry
{
    private static readonly ActivitySource ActivitySourceInstance = new("Sorcha.ServiceClients.Did", "1.0.0");

    private readonly List<IDidResolver> _resolvers = [];
    private readonly ILogger<DidResolverRegistry> _logger;
    private readonly DidResolverCache? _cache;
    private readonly DidResolverMetrics? _metrics;

    /// <summary>DI-friendly constructor (no cache or metrics — used in tests / minimal hosts).</summary>
    public DidResolverRegistry(ILogger<DidResolverRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Production constructor — wires the cross-resolution cache and OTel metrics
    /// (Feature 120 T055-T059).
    /// </summary>
    public DidResolverRegistry(
        ILogger<DidResolverRegistry> logger,
        DidResolverCache cache,
        DidResolverMetrics metrics) : this(logger)
    {
        _cache = cache;
        _metrics = metrics;
    }

    /// <inheritdoc />
    public void Register(IDidResolver resolver)
    {
        _resolvers.Add(resolver);
    }

    /// <inheritdoc />
    public async Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default)
    {
        using var activity = ActivitySourceInstance.StartActivity("did.resolve", ActivityKind.Internal);
        activity?.SetTag("did.input", did);

        var method = ParseMethod(did);
        if (method is null)
        {
            _logger.LogWarning("Invalid DID format: {Did}", did);
            activity?.SetTag("did.method", "invalid");
            activity?.SetTag("did.success", false);
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid DID format");
            return null;
        }

        activity?.SetTag("did.method", method);

        var resolver = _resolvers.FirstOrDefault(r => r.CanResolve(method));
        if (resolver is null)
        {
            _logger.LogWarning("No resolver registered for DID method: {Method} (DID: {Did})", method, did);
            activity?.SetTag("did.success", false);
            activity?.SetStatus(ActivityStatusCode.Error, "No resolver for method");
            return null;
        }

        try
        {
            var result = await resolver.ResolveAsync(did, ct);
            activity?.SetTag("did.success", result != null);
            activity?.SetStatus(result != null ? ActivityStatusCode.Ok : ActivityStatusCode.Unset);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag("did.success", false);
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<DidDocument?> ResolveWithAlsoKnownAsAsync(string did, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(did)) return Task.FromResult<DidDocument?>(null);

        // Feature 120 US4 — deterministic six-step cross-resolution per
        // contracts/did-resolver-registry-contract.md "Resolution algorithm".
        if (_cache is null)
        {
            // Cacheless path — used by tests / minimal hosts.
            return CrossResolveAsync(did, ct);
        }

        return _cache.GetOrAddAsync(did, () => CrossResolveAsync(did, ct));
    }

    private async Task<DidDocument?> CrossResolveAsync(string did, CancellationToken ct)
    {
        using var activity = ActivitySourceInstance.StartActivity("did.resolve.cross", ActivityKind.Internal);
        activity?.SetTag("did.input", did);
        var method = ParseMethod(did) ?? "invalid";
        activity?.SetTag("did.method", method);

        // Step 1 — resolve primary.
        var primary = await ResolveAsync(did, ct).ConfigureAwait(false);
        if (primary is null)
        {
            activity?.SetTag("did.alsoKnownAs.match", "none");
            return null;
        }

        // Step 2 — passthrough if no alsoKnownAs.
        if (primary.AlsoKnownAs is null || primary.AlsoKnownAs.Count == 0)
        {
            activity?.SetTag("did.alsoKnownAs.cross_resolved", false);
            activity?.SetTag("did.alsoKnownAs.link_count", 0);
            return primary;
        }

        activity?.SetTag("did.alsoKnownAs.link_count", primary.AlsoKnownAs.Count);

        // Step 3 — for each linked DID, resolve and intersect VM key material.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { did };
        var verifiedLinks = new List<string>();
        // Track which primary VMs survive intersection across every linked doc.
        var survivors = new HashSet<int>(Enumerable.Range(0, primary.VerificationMethod.Count));

        foreach (var linked in primary.AlsoKnownAs)
        {
            // Step 3a — cycle protection.
            if (string.IsNullOrEmpty(linked) || !visited.Add(linked))
            {
                continue;
            }

            // Step 3b — resolve linked doc.
            var linkedDoc = await ResolveAsync(linked, ct).ConfigureAwait(false);
            if (linkedDoc is null)
            {
                _logger.LogWarning(
                    "alsoKnownAs link unreachable for {Primary}: {Linked}", did, linked);
                activity?.SetTag("did.alsoKnownAs.match", "unreachable");
                _metrics?.AlsoKnownAsUnreachable.Add(1,
                    new KeyValuePair<string, object?>("method", method));
                return null;
            }

            // Step 3c — intersect: drop any primary VM whose key bytes do not
            // appear in this linked doc.
            var newSurvivors = new HashSet<int>();
            foreach (var idx in survivors)
            {
                var primaryVm = primary.VerificationMethod[idx];
                if (linkedDoc.VerificationMethod.Any(vm => VerificationKeyMaterialComparer.KeysMatch(primaryVm, vm)))
                {
                    newSurvivors.Add(idx);
                }
            }
            survivors = newSurvivors;
            verifiedLinks.Add(linked);

            if (survivors.Count == 0) break; // short-circuit; step 4 will reject.
        }

        // Step 4 — reject on zero shared keys.
        if (survivors.Count == 0)
        {
            _logger.LogWarning(
                "alsoKnownAs cross-resolution found no shared keys for {Primary}", did);
            activity?.SetTag("did.alsoKnownAs.match", "mismatch");
            _metrics?.CrossResolveMismatch.Add(1,
                new KeyValuePair<string, object?>("method", method));
            return null;
        }

        // Step 5 — construct merged document.
        var matchedVms = survivors
            .OrderBy(i => i)
            .Select(i => primary.VerificationMethod[i])
            .ToList();
        var matchedIds = matchedVms.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        var merged = new DidDocument
        {
            Id = primary.Id,
            AlsoKnownAs = verifiedLinks,
            VerificationMethod = matchedVms,
            AssertionMethod = primary.AssertionMethod?.Where(matchedIds.Contains).ToList(),
            Authentication = primary.Authentication?.Where(matchedIds.Contains).ToList(),
            Service = primary.Service
        };

        // Step 6 — tag span and return.
        activity?.SetTag("did.alsoKnownAs.cross_resolved", true);
        activity?.SetTag("did.alsoKnownAs.match", "match");
        return merged;
    }

    /// <summary>
    /// Extracts the method component from a DID string (e.g., "sorcha" from "did:sorcha:w:addr").
    /// </summary>
    private static string? ParseMethod(string did)
    {
        if (string.IsNullOrWhiteSpace(did) || !did.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = did.Split(':');
        return parts.Length >= 3 ? parts[1] : null;
    }
}
