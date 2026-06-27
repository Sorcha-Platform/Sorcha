// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Signs and verifies the stateless link-pending token returned on the social callback
/// LinkRequired branch and redeemed at link-confirm.
/// The token is HMAC-SHA256 signed with a key derived from the deployment JWT signing key
/// via HKDF with a distinct info label — no new persistent storage is needed.
/// </summary>
public interface ILinkPendingTokenService
{
    /// <summary>
    /// Serialises <paramref name="token"/> (claims + expiry) and appends an HMAC-SHA256
    /// signature, returning an opaque string safe to include in a JSON response.
    /// </summary>
    /// <param name="token">The claims to embed. <see cref="LinkPendingToken.ExpiresAt"/> is enforced at verify time.</param>
    /// <returns>Opaque, URL-safe token string.</returns>
    string Mint(LinkPendingToken token);

    /// <summary>
    /// Verifies the signature and expiry of <paramref name="raw"/>, populating <paramref name="token"/>
    /// on success or setting <paramref name="error"/> to the failure reason.
    /// Signature comparison is constant-time.
    /// </summary>
    /// <param name="raw">The opaque string from the client.</param>
    /// <param name="token">Populated with the decoded claims on <see cref="LinkPendingTokenError.None"/>; default otherwise.</param>
    /// <param name="error">Set to the failure reason when the return value is <c>false</c>.</param>
    /// <returns><c>true</c> when the token is valid and unexpired.</returns>
    bool TryVerify(string raw, out LinkPendingToken token, out LinkPendingTokenError error);
}
