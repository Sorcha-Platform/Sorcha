// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sorcha.Peer.Service.Identity;

/// <summary>
/// Supplies the node's federation identity: a self-signed P-256 certificate whose thumbprint IS the
/// node's installation-neutral identity (Feature 175). This is deliberately NOT chained to any CA or
/// installation issuer — cross-installation federation authenticates the *channel* by node identity
/// (mTLS client cert), while trust in the *data* comes from the register's own cryptography, not from
/// this certificate. Additive: generating/loading the cert does not by itself change transport
/// behaviour; the mTLS wiring that consumes it lands separately.
/// </summary>
public interface INodeIdentityProvider
{
    /// <summary>The node's self-signed certificate (with private key) used as its mTLS client/server identity.</summary>
    X509Certificate2 Certificate { get; }

    /// <summary>The certificate thumbprint — the stable, installation-neutral node identity string.</summary>
    string NodeIdentityThumbprint { get; }
}

/// <summary>
/// Default <see cref="INodeIdentityProvider"/>: loads the node certificate from
/// <c>PeerService:NodeCertificatePath</c> (a <c>.pfx</c>) when present, otherwise generates a fresh
/// self-signed P-256 certificate and — if a path is configured — persists it so the node identity is
/// stable across restarts. The certificate subject carries the node id (<c>CN={nodeId}</c>).
/// </summary>
public sealed class NodeIdentityProvider : INodeIdentityProvider
{
    /// <summary>Default validity for a generated node certificate.</summary>
    private static readonly TimeSpan CertificateLifetime = TimeSpan.FromDays(3650);

    /// <summary>
    /// Creates the provider, loading or generating the node certificate.
    /// </summary>
    /// <param name="nodeId">The node id used as the certificate subject (CN). Falls back to the machine name.</param>
    /// <param name="certificatePath">Optional <c>.pfx</c> path to load from / persist to. Null ⇒ ephemeral (regenerated each start).</param>
    /// <param name="certificatePassword">Optional password protecting the persisted <c>.pfx</c>.</param>
    /// <param name="now">Injectable clock for the certificate not-before (test seam).</param>
    public NodeIdentityProvider(
        string? nodeId,
        string? certificatePath = null,
        string? certificatePassword = null,
        DateTimeOffset? now = null)
    {
        var subject = string.IsNullOrWhiteSpace(nodeId) ? Environment.MachineName : nodeId;
        var pwd = certificatePassword ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(certificatePath) && File.Exists(certificatePath))
        {
            Certificate = X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath, pwd, X509KeyStorageFlags.Exportable);
        }
        else
        {
            Certificate = GenerateSelfSigned(subject, now ?? DateTimeOffset.UtcNow);

            if (!string.IsNullOrWhiteSpace(certificatePath))
            {
                var dir = Path.GetDirectoryName(certificatePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(certificatePath, Certificate.Export(X509ContentType.Pkcs12, pwd));
            }
        }

        NodeIdentityThumbprint = Certificate.Thumbprint;
    }

    /// <inheritdoc />
    public X509Certificate2 Certificate { get; }

    /// <inheritdoc />
    public string NodeIdentityThumbprint { get; }

    private static X509Certificate2 GenerateSelfSigned(string subject, DateTimeOffset notBefore)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={subject}", ecdsa, HashAlgorithmName.SHA256);

        // Node cert is used for TLS client + server auth on the peer channel.
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyAgreement, critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new Oid("1.3.6.1.5.5.7.3.1"), // serverAuth
                    new Oid("1.3.6.1.5.5.7.3.2"), // clientAuth
                },
                critical: false));

        var cert = request.CreateSelfSigned(notBefore, notBefore.Add(CertificateLifetime));

        // Round-trip through PKCS#12 so the private key is persistable/exportable on all platforms.
        return X509CertificateLoader.LoadPkcs12(
            cert.Export(X509ContentType.Pkcs12), password: null, X509KeyStorageFlags.Exportable);
    }
}
