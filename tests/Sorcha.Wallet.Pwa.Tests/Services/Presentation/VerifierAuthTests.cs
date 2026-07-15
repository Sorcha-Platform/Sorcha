// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Dcql;
using Sorcha.Wallet.Pwa.Services.Presentation;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Presentation;

/// <summary>
/// Feature 181 US6 (T058 / SC-007) — verifier authentication wired into the wallet's presentation
/// parse path. Drives the full <see cref="PresentationEngine.ParseAsync"/> (its request-object
/// fetcher is a plain delegate, so a signed request object can be fed with no IO). Asserts:
/// a tampered signature and a SAN-mismatched <c>client_id</c> are HARD refusals (the parse throws,
/// so no consent is shown); a valid signed request parses and renders <c>AuthenticUntrusted</c>
/// (v1 passes null anchors — absent anchors never block, FR-027).
/// </summary>
public sealed class VerifierAuthTests
{
    private const string Host = "verifier.sorcha.test";
    private const string Vct = "https://sorcha.dev/vc/test/v1";

    private readonly PresentationEngine _engine = new(TimeProvider.System,
        NullLogger<PresentationEngine>.Instance);

    [Fact]
    public async Task ParseAsync_ValidSignedRequest_NoAnchors_RendersAuthenticUntrusted()
    {
        var (jwt, _) = SignedRequestObject(sanHost: Host, clientId: $"x509_san_dns:{Host}", Query());

        var parsed = await _engine.ParseAsync(MakeDeepLink($"x509_san_dns:{Host}"), Fetch(jwt));

        parsed.VerifierAuthentication.Should().NotBeNull();
        parsed.VerifierAuthentication!.Status.Should().Be(VerifierAuthStatus.AuthenticUntrusted);
        parsed.VerifierAuthentication.VerifierHost.Should().Be(Host);
        // Sanity: the DCQL still parsed through.
        parsed.RequiredVct.Should().Be(Vct);
    }

    [Fact]
    public async Task ParseAsync_TamperedSignature_IsRefused_NoConsent()
    {
        var (jwt, _) = SignedRequestObject(sanHost: Host, clientId: $"x509_san_dns:{Host}", Query());
        var parts = jwt.Split('.');
        var sig = parts[2].ToCharArray();
        sig[^1] = sig[^1] == 'A' ? 'B' : 'A';
        var tampered = $"{parts[0]}.{parts[1]}.{new string(sig)}";

        Func<Task> act = () => _engine.ParseAsync(MakeDeepLink($"x509_san_dns:{Host}"), Fetch(tampered));

        (await act.Should().ThrowAsync<DcqlParseException>())
            .Which.Code.Should().Be(RequestObjectErrorCodes.RequestObjectInvalid);
    }

    [Fact]
    public async Task ParseAsync_SanMismatchedClientId_IsRefused_NoConsent()
    {
        // Signed by a cert whose SAN dNSName is Host, but the client_id claims a different host.
        var (jwt, _) = SignedRequestObject(sanHost: Host, clientId: "x509_san_dns:attacker.example", Query());

        Func<Task> act = () => _engine.ParseAsync(MakeDeepLink("x509_san_dns:attacker.example"), Fetch(jwt));

        (await act.Should().ThrowAsync<DcqlParseException>())
            .Which.Code.Should().Be(RequestObjectErrorCodes.RequestHostMismatch);
    }

    // ────────────────────────── helpers ──────────────────────────

    private static DcqlQuery Query()
        => DcqlRequestBuilder.Build([DcqlCredentialAsk.SdJwt("cred1", Vct, ["givenName"], ["familyName"])]);

    /// <summary>
    /// Mint a self-signed ES256 verifier cert with a SAN dNSName of <paramref name="sanHost"/>, and
    /// sign a full request object (header <c>alg=ES256, typ=oauth-authz-req+jwt, x5c=[cert]</c>; payload
    /// carrying <paramref name="clientId"/>, response_uri, nonce and the dcql_query). Mirrors
    /// <c>RequestObjectValidatorTests.SignRequestObject</c>.
    /// </summary>
    private static (string Jwt, byte[] LeafDer) SignedRequestObject(string sanHost, string clientId, DcqlQuery query)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest($"CN={sanHost}", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(sanHost);
        req.CertificateExtensions.Add(san.Build());
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));

        var header = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["typ"] = "oauth-authz-req+jwt",
            ["x5c"] = new[] { Convert.ToBase64String(cert.RawData) },
        };
        var payload = new Dictionary<string, object>
        {
            ["client_id"] = clientId,
            ["response_uri"] = "https://verify.test/r/sess-1/response",
            ["nonce"] = "n0nce",
            ["state"] = "state-1",
            ["response_mode"] = "direct_post",
            ["dcql_query"] = JsonSerializer.Deserialize<JsonElement>(DcqlRequestBuilder.ToJson(query)),
        };

        var h = B64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var p = B64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.ASCII.GetBytes($"{h}.{p}");
        var sig = key.SignData(signingInput, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return ($"{h}.{p}.{B64Url(sig)}", cert.RawData);
    }

    private static string MakeDeepLink(string clientId, string requestUri = "https://verify.test/request/sess-1")
        => $"openid4vp://?client_id={Uri.EscapeDataString(clientId)}" +
           $"&request_uri={Uri.EscapeDataString(requestUri)}";

    private static Func<string, CancellationToken, Task<string>> Fetch(string requestObjectJwt)
        => (_, _) => Task.FromResult(requestObjectJwt);

    private static string B64Url(byte[] data) => Base64Url.EncodeToString(data);
}
