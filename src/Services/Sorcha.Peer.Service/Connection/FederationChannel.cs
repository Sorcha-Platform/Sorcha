// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Sorcha.Peer.Service.Connection;

/// <summary>
/// Builds the outbound gRPC transport handler for peer federation (Feature 175). Cross-installation
/// federation authenticates the <em>channel</em> by node identity — the node's self-signed
/// <see cref="INodeIdentityProvider">certificate</see> is presented as an mTLS client certificate —
/// while trust in the replicated <em>data</em> comes from the register's own cryptography, not from a
/// shared certificate authority. Consequently the peer's <em>server</em> certificate is accepted
/// regardless of PKI chain: a self-signed foreign-installation node (e.g. n1) must not fail TLS
/// negotiation on a chain/name mismatch. On a cleartext (<c>http://</c>) peer endpoint the TLS
/// options are simply unused.
/// </summary>
public static class FederationChannel
{
    /// <summary>
    /// Creates the <see cref="SocketsHttpHandler"/> used for peer gRPC channels. When
    /// <paramref name="nodeCertificate"/> is supplied the handler presents it for mTLS and accepts the
    /// peer's self-signed server certificate; when it is <c>null</c> the handler is the plain
    /// keep-alive handler used before node identity was wired (backward-compatible fallback).
    /// </summary>
    /// <param name="nodeCertificate">The node identity certificate to present, or <c>null</c> for no client certificate.</param>
    public static SocketsHttpHandler CreateHandler(X509Certificate2? nodeCertificate)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            EnableMultipleHttp2Connections = true
        };

        if (nodeCertificate is not null)
        {
            handler.SslOptions.ClientCertificates = new X509CertificateCollection { nodeCertificate };
            handler.SslOptions.RemoteCertificateValidationCallback = AcceptPeerServerCertificate;
        }

        return handler;
    }

    /// <summary>
    /// Remote-certificate validation callback for peer channels: always accepts. Federation trust is
    /// carried by register cryptography (docket/attestation/validator signatures verified on ingest),
    /// not by TLS PKI, so a self-signed or name-mismatched peer server certificate is not a failure.
    /// </summary>
    public static bool AcceptPeerServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors) => true;
}
