// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.ServiceClients.Did;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Feature 135 (T034) — supplies the X.509 trust anchors for the HAIP x509-tenant trust source.
/// Sources the roots from <c>Haip:TrustedRootCertificates</c> (base64 DER), the same configuration
/// that previously seeded the verifier's static trusted-root list — now consumed by the unified
/// evaluator instead. Returns null when no roots are configured, so the x509-tenant source fails
/// closed. <c>Haip:VerifyRevocation</c> toggles CRL checking during chain build.
/// </summary>
public sealed class ConfiguredTenantTrustAnchorProvider : ITenantTrustAnchorProvider
{
    private readonly TrustAnchorSet? _anchors;

    public ConfiguredTenantTrustAnchorProvider(IConfiguration configuration, ILogger<ConfiguredTenantTrustAnchorProvider> logger)
    {
        var configured = configuration.GetSection("Haip:TrustedRootCertificates").Get<string[]>() ?? [];
        var roots = new List<byte[]>();
        foreach (var base64 in configured)
        {
            if (string.IsNullOrWhiteSpace(base64))
                continue;
            try
            {
                roots.Add(Convert.FromBase64String(base64));
            }
            catch (FormatException ex)
            {
                logger.LogWarning(ex, "Skipping a malformed Haip:TrustedRootCertificates entry");
            }
        }

        _anchors = roots.Count > 0
            ? new TrustAnchorSet
            {
                Roots = roots,
                CheckRevocation = configuration.GetValue<bool>("Haip:VerifyRevocation"),
                AnchorSetId = "haip-configured-roots"
            }
            : null;

        if (_anchors is null)
            logger.LogInformation("No Haip:TrustedRootCertificates configured — the x509-tenant trust source will fail closed.");
    }

    /// <inheritdoc />
    public Task<TrustAnchorSet?> GetAnchorsAsync(string? anchorId, CancellationToken cancellationToken = default)
        => Task.FromResult(_anchors);
}

/// <summary>
/// Feature 135 (T034) — adapts the engine-local <see cref="IIssuerDirectory"/> to the platform
/// <see cref="IDidResolverRegistry"/> for the HAIP register / did-allowlist trust sources. Mirrors
/// the Blueprint.Service adapter (each service owns its own thin adapter; the engine stays
/// dependency-free of the resolver registry).
/// </summary>
public sealed class DidIssuerDirectory : IIssuerDirectory
{
    private readonly IDidResolverRegistry? _didResolver;

    public DidIssuerDirectory(IDidResolverRegistry? didResolver = null)
    {
        _didResolver = didResolver;
    }

    /// <inheritdoc />
    public async Task<IssuerDirectoryEntry> LookupAsync(string issuerId, CancellationToken cancellationToken = default)
    {
        if (_didResolver is null || string.IsNullOrWhiteSpace(issuerId) || !issuerId.StartsWith("did:", StringComparison.Ordinal))
            return new IssuerDirectoryEntry { Resolved = false };

        var document = await _didResolver.ResolveAsync(issuerId, cancellationToken).ConfigureAwait(false);
        if (document is null)
            return new IssuerDirectoryEntry { Resolved = false };

        return new IssuerDirectoryEntry
        {
            Resolved = true,
            AssertionMethodKeyIds = document.AssertionMethod ?? [],
            AlsoKnownAs = document.AlsoKnownAs ?? []
        };
    }
}
