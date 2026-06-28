// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Components.User.Services.Signing;
using Sorcha.UI.Components.User.Services.Verification;

namespace Sorcha.Wallet.Pwa.Services.Signing;

/// <summary>
/// PWA implementation of <see cref="IVerifierIdentityProvider"/> (Feature 164, B3 US2).
/// Delegates to <see cref="IEphemeralVerifierIdentityService"/> to mint a fresh ephemeral
/// P-256 key per session, returning the JWK thumbprint as the OID4VP <c>client_id</c>.
/// WASM-safe — depends only on the shared interface, not WebCrypto directly.
/// </summary>
public sealed class EphemeralVerifierIdentityAdapter : IVerifierIdentityProvider
{
    private readonly IEphemeralVerifierIdentityService _identityService;

    /// <summary>Initialises the adapter.</summary>
    public EphemeralVerifierIdentityAdapter(IEphemeralVerifierIdentityService identityService)
        => _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));

    /// <inheritdoc />
    public async Task<string> GetClientIdAsync(CancellationToken ct = default)
    {
        await using var identity = await _identityService.BeginSessionAsync(ct);
        return identity.ClientId;
    }
}
