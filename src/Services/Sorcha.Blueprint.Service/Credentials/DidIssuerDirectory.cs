// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.ServiceClients.Did;

namespace Sorcha.Blueprint.Service.Credentials;

/// <summary>
/// Service-layer adapter implementing the engine-local <see cref="IIssuerDirectory"/> over the
/// platform <see cref="IDidResolverRegistry"/> (feature 135, T034). Projects a resolved DID
/// document down to what the register and did-allowlist trust sources need: the currently
/// authorised assertionMethod key ids and the alsoKnownAs equivalence set. Keeps the engine
/// WASM-friendly — the network dependency lives here, not in the Engine project.
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
