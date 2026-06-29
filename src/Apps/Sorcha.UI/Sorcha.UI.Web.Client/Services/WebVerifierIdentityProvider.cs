// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Sorcha.UI.Components.User.Services.Verification;

namespace Sorcha.UI.Web.Client.Services;

/// <summary>
/// Web-host implementation of <see cref="IVerifierIdentityProvider"/> (Feature 164). The platform
/// web client acts as a relying-party verifier; it returns a stable <c>client_id</c> derived from
/// configuration (<c>Verifier:OrgId</c>, defaulting to <c>"web"</c>). The HAIP endpoint derives the
/// authoritative client_id server-side, so this value is used for request correlation / logging.
/// Mirrors the desk verifier's stable-identity model (rather than the PWA's ephemeral identity).
/// </summary>
public sealed class WebVerifierIdentityProvider(IConfiguration configuration) : IVerifierIdentityProvider
{
    private readonly string _clientId =
        $"did:sorcha:verifier:{configuration["Verifier:OrgId"] ?? "web"}";

    /// <inheritdoc />
    public Task<string> GetClientIdAsync(CancellationToken ct = default) => Task.FromResult(_clientId);
}
