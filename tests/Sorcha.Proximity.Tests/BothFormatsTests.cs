// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Mdoc.Proximity;
using Sorcha.Proximity;
using Sorcha.Proximity.Tests.Support;
using Xunit;

namespace Sorcha.Proximity.Tests;

/// <summary>
/// <b>Both</b> credential formats travel over the one ISO session — and are <b>equally</b> protected against
/// replay (FR-005).
/// </summary>
/// <remarks>
/// <para>
/// mdoc is what the world can read; SD-JWT VC is what Sorcha citizens actually hold. Shipping only mdoc would
/// make in-person sharing useless to every current holder; shipping only SD-JWT would dead-end it against the
/// wider ecosystem.
/// </para>
/// <para>
/// The load-bearing claim these tests exist to make true rather than merely asserted: <b>replay resistance
/// holds identically for both</b>. mdoc gets it from the session transcript inside
/// <c>DeviceAuthentication</c>; SD-JWT gets it from a KB-JWT whose <c>aud</c> and <c>nonce</c> bind to the
/// hash of the same transcript. Without that binding the SD-JWT rail would have <em>no</em> session binding at
/// all, and a captured presentation would replay anywhere — so "both formats" would be true of the transport
/// and false of the security.
/// </para>
/// </remarks>
public class BothFormatsTests
{
    private const string IdentityVct = "https://sorcha.dev/vc/assured-identity/v1";

    /// <summary>
    /// Builds an SD-JWT credential whose presentation binds to whatever session binding it is handed.
    /// </summary>
    /// <remarks>
    /// A stand-in for the wallet's real SD-JWT presenter (which signs the KB-JWT with the non-extractable
    /// holder key). What matters to the protocol — and what is tested here — is that the binding reaches the
    /// KB-JWT's <c>aud</c> and <c>nonce</c>.
    /// </remarks>
    private static ProximityCredential.SdJwt AnSdJwtCredential(
        IList<string>? capturedBindings = null) =>
        new(IdentityVct,
            ["family_name", "age_over_18"],
            (approvedClaims, sessionBinding, _) =>
            {
                capturedBindings?.Add(sessionBinding);
                return Task.FromResult(BuildPresentation(approvedClaims, sessionBinding));
            });

    /// <summary>Builds a compact SD-JWT presentation whose KB-JWT binds aud + nonce to the session.</summary>
    private static string BuildPresentation(IReadOnlyList<string> claims, string sessionBinding)
    {
        var kbPayload = JsonSerializer.Serialize(new
        {
            aud = sessionBinding,
            nonce = sessionBinding,
            iat = 1_700_000_000L
        });

        var kbJwt = $"{Base64Url("{\"alg\":\"ES256\",\"typ\":\"kb+jwt\"}")}.{Base64Url(kbPayload)}.{Base64Url("signature")}";

        var disclosures = string.Join('~', claims.Select(c => Base64Url($"[\"salt\",\"{c}\",\"value\"]")));

        return $"issuer.jwt.sig~{disclosures}~{kbJwt}";
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static MdocDeviceRequest RequestFor(string identifier, params string[] elements) => new()
    {
        DocRequests =
        [
            new ItemsRequest
            {
                DocType = identifier,
                Elements = elements.Select(e =>
                    new RequestedElement(identifier, e, IntentToRetain: false)).ToList(),
            }
        ]
    };

    /// <summary>An mdoc travels in person, and verifies. (The rail proven in US1, restated here for contrast.)</summary>
    [Fact]
    public async Task AnMdoc_TravelsInPerson_AndVerifies()
    {
        await using var fixture = ProximityFixture.Create();

        var verdict = await fixture.RunAsync(approveAll: true);

        verdict.Accepted.Should().BeTrue(because: string.Join("; ", verdict.Errors));
        verdict.DeviceBindingValid.Should().BeTrue();
    }

    /// <summary>A Sorcha-native SD-JWT travels in person, bound to the session.</summary>
    [Fact]
    public async Task AnSdJwt_TravelsInPerson_BoundToTheSession()
    {
        var bindings = new List<string>();

        var (holderTransport, readerTransport) = LoopbackProximityTransport.CreatePair();
        var (privateKey, publicKey) = TestMdoc.GenerateDeviceKey();

        await using var holder = new ProximityHolderSession(
            holderTransport,
            [AnSdJwtCredential(bindings)],
            HolderDeviceKey.FromInMemoryKey(privateKey));

        await using var reader = new ProximityReaderSession(readerTransport);

        var engagement = await holder.StartAsync();
        await reader.RequestAsync(engagement, RequestFor(IdentityVct, "age_over_18"));
        await holder.WaitForRequestAsync();

        holder.SatisfiableElements.Should().ContainSingle(because:
            "the SD-JWT carries age_over_18, so the wallet can answer the question");

        await holder.ApproveAsync(holder.SatisfiableElements);

        var verdict = await reader.WaitForVerdictAsync();

        bindings.Should().ContainSingle(because: "the presentation was bound to exactly one session");

        verdict.DeviceBindingValid.Should().BeTrue(because:
            "the KB-JWT's aud AND nonce both name this session's transcript hash");

        // Honest: the proximity layer checks the SESSION BINDING. It does not claim to have verified the
        // credential chain — that belongs to the verifier engine — and it must not pretend otherwise.
        verdict.Accepted.Should().BeFalse();
        verdict.Errors.Should().ContainMatch("*verifier engine*");
    }

    /// <summary>
    /// <b>The claim that matters.</b> An SD-JWT presentation captured from one session is worthless in another
    /// — exactly as an mdoc response is.
    /// </summary>
    /// <remarks>
    /// This is the test that turns "both formats travel" into "both formats are equally safe". Without the
    /// session binding, the SD-JWT path would happily accept a presentation lifted from an earlier exchange.
    /// </remarks>
    [Fact]
    public async Task AnSdJwtPresentationFromAnotherSession_IsRefused()
    {
        // Session 1: a genuine presentation, bound to session 1.
        var firstBindings = new List<string>();
        var (h1, r1) = LoopbackProximityTransport.CreatePair();
        var (privateKey, _) = TestMdoc.GenerateDeviceKey();

        await using (var holder1 = new ProximityHolderSession(
            h1, [AnSdJwtCredential(firstBindings)], HolderDeviceKey.FromInMemoryKey(privateKey)))
        await using (var reader1 = new ProximityReaderSession(r1))
        {
            var engagement1 = await holder1.StartAsync();
            await reader1.RequestAsync(engagement1, RequestFor(IdentityVct, "age_over_18"));
            await holder1.WaitForRequestAsync();
            await holder1.ApproveAsync(holder1.SatisfiableElements);
            await reader1.WaitForVerdictAsync();
        }

        var stolenBinding = firstBindings.Single();

        // Session 2: a holder that replays session 1's presentation verbatim — the classic attack.
        var replayer = new ProximityCredential.SdJwt(
            IdentityVct,
            ["family_name", "age_over_18"],
            (claims, _, _) => Task.FromResult(BuildPresentation(claims, stolenBinding)));

        var (h2, r2) = LoopbackProximityTransport.CreatePair();

        await using var holder2 = new ProximityHolderSession(
            h2, [replayer], HolderDeviceKey.FromInMemoryKey(privateKey));
        await using var reader2 = new ProximityReaderSession(r2);

        var engagement2 = await holder2.StartAsync();
        await reader2.RequestAsync(engagement2, RequestFor(IdentityVct, "age_over_18"));
        await holder2.WaitForRequestAsync();
        await holder2.ApproveAsync(holder2.SatisfiableElements);

        var verdict = await reader2.WaitForVerdictAsync();

        verdict.DeviceBindingValid.Should().BeFalse(because:
            "the presentation names the FIRST session's transcript, not this one — which is exactly what a " +
            "replayed presentation looks like");

        verdict.Errors.Should().ContainMatch("*replay*");
        verdict.Accepted.Should().BeFalse();
    }

    /// <summary>
    /// A bare <c>DeviceResponse</c> — what a conformant third-party wallet sends — is still read.
    /// </summary>
    /// <remarks>
    /// A wallet that has never heard of Sorcha will not wrap its response in our envelope. Refusing that would
    /// make this reader able to read only our own wallet, which would defeat the point of implementing the
    /// standard at all.
    /// </remarks>
    [Fact]
    public void ABareDeviceResponse_IsTreatedAsMdoc()
    {
        var bare = new byte[] { 0xa3, 0x67, 0x76, 0x65, 0x72, 0x73, 0x69, 0x6f, 0x6e };  // {"version"...

        var envelope = SorchaProximityEnvelope.Decode(bare);

        envelope.Format.Should().Be(ProximityPayloadFormat.MsoMdoc);
        envelope.Payload.Should().Equal(bare);
    }

    /// <summary>Our envelope round-trips, and is distinguishable from a bare DeviceResponse.</summary>
    [Fact]
    public void TheEnvelope_RoundTrips()
    {
        var original = new SorchaProximityEnvelope
        {
            Format = ProximityPayloadFormat.SdJwtVc,
            Payload = "issuer.jwt.sig~disclosure~kb.jwt.sig"u8.ToArray()
        };

        var decoded = SorchaProximityEnvelope.Decode(original.Encode());

        decoded.Format.Should().Be(ProximityPayloadFormat.SdJwtVc);
        decoded.Payload.Should().Equal(original.Payload);
    }

    /// <summary>The session binding is a function of the transcript — different session, different binding.</summary>
    [Fact]
    public void TheSessionBinding_DiffersPerSession()
    {
        // Tag-24-wrapped, because that is what the transcript splices — raw bytes are not valid CBOR.
        var a = ProximitySessionTranscript.ForQrEngagement(
            Sorcha.Mdoc.Cbor.MdocCbor.WrapTag24([0xa0]),
            Sorcha.Mdoc.Cbor.MdocCbor.WrapTag24([0xa1, 0x01, 0x02]));

        var b = ProximitySessionTranscript.ForQrEngagement(
            Sorcha.Mdoc.Cbor.MdocCbor.WrapTag24([0xa1, 0x03, 0x04]),
            Sorcha.Mdoc.Cbor.MdocCbor.WrapTag24([0xa1, 0x05, 0x06]));

        SorchaProximityEnvelope.SessionBinding(a)
            .Should().NotBe(SorchaProximityEnvelope.SessionBinding(b), because:
                "if two sessions shared a binding, a presentation could be moved between them");
    }
}
