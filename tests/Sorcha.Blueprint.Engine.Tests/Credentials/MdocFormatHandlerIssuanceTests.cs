// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using FluentAssertions;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Credentials.Sources;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Mdoc;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Feature 135 / T054 + T057 — MdocFormatHandler.IssueAsync honours format + trust anchor and fails
/// closed on unsupported combinations (no silent substitution): mso_mdoc requires an X.509 anchor
/// with a chain; the register anchor and a missing chain are rejected (FR-020/FR-022).
/// </summary>
public class MdocFormatHandlerIssuanceTests
{
    private static MdocFormatHandler Handler() =>
        new(new MdocService(),
            new TrustEvaluator(new TrustResolverRegistry([new RegisterTrustSourceResolver(new FakeDir())])));

    private sealed class FakeDir : IIssuerDirectory
    {
        public Task<IssuerDirectoryEntry> LookupAsync(string issuerId, CancellationToken ct = default) =>
            Task.FromResult(new IssuerDirectoryEntry { Resolved = true });
    }

    private static (byte[] priv, byte[] certDer) NewIssuer()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cert = new CertificateRequest("CN=Issuer", key, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return (key.ExportECPrivateKey(), cert.Export(X509ContentType.Cert));
    }

    private static byte[] HolderCose()
    {
        using var holder = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = holder.ExportParameters(false);
        return Sorcha.Mdoc.Cbor.MdocCbor.Encode(w =>
        {
            w.WriteStartMap(4);
            w.WriteInt32(1); w.WriteInt32(2);
            w.WriteInt32(-1); w.WriteInt32(1);
            w.WriteInt32(-2); w.WriteByteString(p.Q.X!);
            w.WriteInt32(-3); w.WriteByteString(p.Q.Y!);
            w.WriteEndMap();
        });
    }

    private static CredentialIssuanceConfig Config(TrustAnchor anchor, CredentialFormat format = CredentialFormat.MsoMdoc) => new()
    {
        CredentialType = "eu.europa.ec.eudi.pid.1",
        RecipientParticipantId = "holder",
        ClaimMappings = [new ClaimMapping { ClaimName = "family_name", SourceField = "/family_name" }],
        Format = format,
        TrustAnchor = anchor,
        ExpiryDuration = "P365D"
    };

    [Fact]
    public async Task IssueAsync_X509Tenant_WithChain_ProducesVerifiableMdoc()
    {
        var (priv, certDer) = NewIssuer();
        var bytes = await Handler().IssueAsync(
            Config(TrustAnchor.X509Tenant),
            new Dictionary<string, object> { ["family_name"] = "Andersson" },
            priv, "ES256", HolderCose(), [certDer]);

        var issued = MdocCodec.DecodeIssuerSigned(bytes);
        issued.NameSpaces.Should().ContainKey("eu.europa.ec.eudi.pid.1");
        // The issued credential's issuer signature verifies against the cert key.
        using var cert = X509CertificateLoader.LoadCertificate(certDer);
        issued.IssuerAuth.VerifyEmbedded(cert.GetECDsaPublicKey()!).Should().BeTrue();
    }

    [Fact]
    public async Task IssueAsync_RegisterAnchor_FailsClosed()
    {
        var (priv, _) = NewIssuer();
        var act = async () => await Handler().IssueAsync(
            Config(TrustAnchor.Register),
            new Dictionary<string, object> { ["family_name"] = "Andersson" },
            priv, "ES256", HolderCose(), x5cChain: null);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("X.509");
    }

    [Fact]
    public async Task IssueAsync_X509Anchor_NoChain_FailsClosed()
    {
        var (priv, _) = NewIssuer();
        var act = async () => await Handler().IssueAsync(
            Config(TrustAnchor.X509Tenant),
            new Dictionary<string, object> { ["family_name"] = "Andersson" },
            priv, "ES256", HolderCose(), x5cChain: null);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("chain");
    }

    [Fact]
    public async Task IssueAsync_WrongFormat_Throws()
    {
        var (priv, certDer) = NewIssuer();
        var act = async () => await Handler().IssueAsync(
            Config(TrustAnchor.X509Tenant, CredentialFormat.SdJwtVc),
            new Dictionary<string, object> { ["family_name"] = "Andersson" },
            priv, "ES256", HolderCose(), [certDer]);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
