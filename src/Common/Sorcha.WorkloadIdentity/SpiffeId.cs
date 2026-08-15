// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.WorkloadIdentity;

/// <summary>
/// SPIFFE-style workload identifier for a Sorcha service principal:
/// <c>spiffe://{installation}/service/{client_id}</c>.
/// The trust domain is the installation name (the same value that namespaces JWT issuer and
/// audiences — never configured independently); the path names the service principal's
/// <c>client_id</c> exactly. Comparison is Ordinal on both halves: a case-insensitive match
/// would let one principal impersonate another.
/// </summary>
public sealed record SpiffeId
{
    private const string Scheme = "spiffe";
    private const string ServiceSegment = "service";

    /// <summary>Installation name (lowercased — URI authorities are case-insensitive).</summary>
    public string TrustDomain { get; }

    /// <summary>Service principal client id, preserved exactly (Ordinal identity).</summary>
    public string ClientId { get; }

    private SpiffeId(string trustDomain, string clientId)
    {
        TrustDomain = trustDomain;
        ClientId = clientId;
    }

    /// <summary>Builds the workload identifier for a service principal of an installation.</summary>
    /// <exception cref="ArgumentException">Either half is null, empty, or whitespace.</exception>
    public static SpiffeId ForService(string installationName, string clientId)
    {
        if (string.IsNullOrWhiteSpace(installationName))
            throw new ArgumentException("Installation name is required.", nameof(installationName));
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Client id is required.", nameof(clientId));

        return new SpiffeId(installationName.Trim().ToLowerInvariant(), clientId.Trim());
    }

    /// <summary>
    /// Parses a candidate URI string; only the exact <c>spiffe://{domain}/service/{clientId}</c>
    /// shape is accepted — any other scheme, path shape, or segment count is rejected.
    /// </summary>
    public static bool TryParse(string? value, out SpiffeId? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrEmpty(uri.Host))
            return false;

        // AbsolutePath must be exactly "/service/{clientId}" — two segments, second non-empty.
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.None);
        // "/service/x" splits to ["", "service", "x"]
        if (segments.Length != 3)
            return false;
        if (!string.Equals(segments[1], ServiceSegment, StringComparison.Ordinal))
            return false;
        if (string.IsNullOrEmpty(segments[2]))
            return false;

        id = new SpiffeId(uri.Host.ToLowerInvariant(), segments[2]);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Scheme}://{TrustDomain}/{ServiceSegment}/{ClientId}";

    /// <summary>The identifier as a <see cref="Uri"/> (for SAN construction).</summary>
    public Uri ToUri() => new(ToString());
}
