// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Trust;

/// <summary>
/// Feature 096 US3 consumer client — fetches an organisation's X.509
/// certificate chain (leaf + tenant root) from the Tenant Service for use as
/// the <c>x5c</c> JWS header when issuing HAIP credentials. Return value
/// order matches the JWS <c>x5c</c> conventions (leaf first, root last).
/// </summary>
public interface IOrgCertChainProvider
{
    /// <summary>
    /// Returns the cert chain for <paramref name="orgWalletAddress"/> under
    /// <paramref name="tenantId"/>. Null when the Tenant Service has no
    /// active enrolment (404) or the request failed transiently.
    /// </summary>
    Task<OrgCertChain?> GetChainForAsync(
        string tenantId,
        string orgWalletAddress,
        CancellationToken ct = default);
}

/// <summary>
/// DER-encoded certificate pair. <see cref="OrgCertDer"/> is the leaf cert
/// bound to the org's HAIP classical co-key; <see cref="RootCertDer"/> is
/// the tenant root CA. The <see cref="AsJwsChain"/> helper returns both in
/// the order the JWS spec expects (leaf first).
/// </summary>
public sealed record OrgCertChain(byte[] OrgCertDer, byte[] RootCertDer)
{
    /// <summary>
    /// Returns the chain as a list ready for <c>ISdJwtService.CreateTokenAsync(..., x5cChain: ...)</c>.
    /// Leaf first, root last — matches RFC 7515 §4.1.6.
    /// </summary>
    public IReadOnlyList<byte[]> AsJwsChain() => new[] { OrgCertDer, RootCertDer };
}
