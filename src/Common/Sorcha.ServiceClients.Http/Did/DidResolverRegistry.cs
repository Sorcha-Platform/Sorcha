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

    public DidResolverRegistry(ILogger<DidResolverRegistry> logger)
    {
        _logger = logger;
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
        // TODO(Feature 120 US4): cross-resolve alsoKnownAs with key-material verification.
        // Phase 1 stub delegates to ResolveAsync so US1 work can compile against the
        // contract while the deterministic six-step algorithm in
        // contracts/did-resolver-registry-contract.md is built out in T055-T059.
        return ResolveAsync(did, ct);
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
