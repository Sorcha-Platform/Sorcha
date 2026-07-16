// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.PresentationLifecycle.Abstractions;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Models;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// #1195 Phase 2 (final review C-1) — the end-to-end proof the prior tests mocked past. Drives the
/// REAL <see cref="SorchaWalletPresentationConsumer"/> over the REAL
/// <see cref="VerifiablePresentationValidator"/> (no mock at that boundary) with a delegation-free,
/// SERVER-CUSTODY presentation: a holder-<c>cnf</c> credential whose KB-JWT is signed directly by the
/// credential's own holder key, exactly as the Task-6 "bind to device" flow posts it. This is the
/// bind-gate path that must pass for device-bound re-issuance to work.
/// </summary>
public sealed class SorchaWalletServerCustodyE2ETests
{
    private const string Vct = "https://sorcha.dev/vc/assured-identity/v1";
    private const string ClientId = "did:sorcha:org:aias";
    private const string Nonce = "server-custody-nonce-1";

    /// <summary>The REAL validator — offline v1 posture (opt-out issuer resolver, no require-issuer).</summary>
    private static VerifiablePresentationValidator RealValidator()
    {
        var statusList = new Mock<IStatusListCache>();
        statusList
            .Setup(s => s.CheckAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatusListVerdict.Active);

        return new VerifiablePresentationValidator(
            statusList.Object,
            new OptOutIssuerKeyResolver(),
            TimeProvider.System,
            NullLogger<VerifiablePresentationValidator>.Instance,
            requireIssuerSignature: false);
    }

    private static VerifierSession Session() => new()
    {
        SessionId = "sess-server-custody",
        ClientId = ClientId,
        Nonce = Nonce,
        RequiredVct = Vct,
        RequiredClaims = ["givenName"],
        Purpose = "credential-gate",
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
    };

    private static PresentationInitiationContext Context() => new(
        PresentationRequestId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
        InstanceId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
        ActionId: 1,
        RegisterId: "reg-test",
        BlueprintId: "bp-test",
        SubmitterWallet: "ws11qqtest",
        RequirementsDigest: new byte[32],
        InitiatedAt: DateTimeOffset.UtcNow,
        VerifierClientId: ClientId,
        CredentialType: Vct,
        RequiredClaimNames: ["givenName"],
        PublicBaseUrl: "https://gateway.example");

    [Fact]
    public async Task VerifyAsync_ServerCustody_HolderSignedNoDelegation_Succeeds_AndEmitsClaims()
    {
        // Holder-signed, delegation-free vp_token through the real validator → Success + claims.
        // Success here IS the "HolderKeyVerified == true" signal at the consumer boundary (the
        // consumer's PresentationOutcome exposes no separate layer field).
        var vpToken = MintServerCustodyVp(kbSignedByHolder: true);
        var consumer = new SorchaWalletPresentationConsumer(
            RealValidator(), NullLogger<SorchaWalletPresentationConsumer>.Instance);

        var outcome = await consumer.VerifyAsync(
            Context(),
            new SorchaWalletVerificationPayload { VpToken = vpToken, DelegationCredential = null, Session = Session() },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Success);
        outcome.VerifiedClaims.Should().NotBeNull();
        outcome.VerifiedClaims!.Should().ContainKey("givenName");
        outcome.PresentationSubmissionHash.Should().StartWith("sha256:");
    }

    [Fact]
    public async Task VerifyAsync_ServerCustody_DeviceSignedNoDelegation_Declines_WithNoClaims()
    {
        // The wrong-key negative on the real path: cnf is the holder key, the KB-JWT is signed by a
        // DIFFERENT device key, no delegation. The real validator raises the named key-binding
        // mismatch → the consumer maps it to SignatureInvalid and emits zero claims.
        var vpToken = MintServerCustodyVp(kbSignedByHolder: false);
        var consumer = new SorchaWalletPresentationConsumer(
            RealValidator(), NullLogger<SorchaWalletPresentationConsumer>.Instance);

        var outcome = await consumer.VerifyAsync(
            Context(),
            new SorchaWalletVerificationPayload { VpToken = vpToken, DelegationCredential = null, Session = Session() },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(PresentationDeclineReason.SignatureInvalid);
        outcome.VerifiedClaims.Should().BeNull();
    }

    // ─────────────────────────── self-contained SD-JWT minter ───────────────────────────────────
    //   Mirrors the on-the-wire shape a server-custody wallet produces: SD-JWT VC (issuer-signed,
    //   cnf = holder P-256 key) + a KB-JWT signed by the holder key itself (no delegation). This test
    //   project cannot see Verifier.Tests' TestVpFactory, so it mints locally with the BCL only.

    private static string MintServerCustodyVp(bool kbSignedByHolder)
    {
        using var issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var holder = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var (disclosure, hash) = MintDisclosure("givenName", "Sarah");

        var credentialPayload = new Dictionary<string, object>
        {
            ["iss"] = "did:sorcha:org:test",
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["vct"] = Vct,
            ["_sd"] = new[] { hash },
            ["_sd_alg"] = "sha-256",
            ["cnf"] = new Dictionary<string, object> { ["jwk"] = Jwk(holder) },
        };
        var credentialJwt = SignEs256(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "dc+sd-jwt" },
            credentialPayload, issuer);

        var kbPayload = new Dictionary<string, object>
        {
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["exp"] = DateTimeOffset.UtcNow.AddSeconds(120).ToUnixTimeSeconds(),
            ["aud"] = ClientId,
            ["nonce"] = Nonce,
            ["sd_hash"] = "placeholder",
        };
        using var wrongDevice = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var kbSigner = kbSignedByHolder ? holder : wrongDevice;
        var kbJwt = SignEs256(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "kb+jwt" },
            kbPayload, kbSigner);

        return $"{credentialJwt}~{disclosure}~{kbJwt}";
    }

    private static (string Segment, string Hash) MintDisclosure(string name, string value)
    {
        Span<byte> salt = stackalloc byte[16];
        RandomNumberGenerator.Fill(salt);
        var segment = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new object[] { Base64Url.EncodeToString(salt), name, value }));
        var hash = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(segment)));
        return (segment, hash);
    }

    private static JsonElement Jwk(ECDsa ecdsa)
    {
        var p = ecdsa.ExportParameters(false);
        var json = JsonSerializer.Serialize(new
        {
            kty = "EC",
            crv = "P-256",
            x = Base64Url.EncodeToString(p.Q.X!),
            y = Base64Url.EncodeToString(p.Q.Y!),
        });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static string SignEs256(Dictionary<string, object> header, Dictionary<string, object> payload, ECDsa signer)
    {
        var headerSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.ASCII.GetBytes($"{headerSeg}.{payloadSeg}");
        var sig = signer.SignData(signingInput, HashAlgorithmName.SHA256);
        return $"{headerSeg}.{payloadSeg}.{Base64Url.EncodeToString(sig)}";
    }
}
