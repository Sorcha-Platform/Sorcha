// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
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
/// Feature 127 — verifies the Sorcha-wallet consumer correctly bridges
/// F111's <see cref="IPresentationConsumer"/> contract onto
/// <see cref="IVerifiablePresentationValidator"/>. Covers the
/// success path, the decline-reason mappings, missing-claim
/// detection, payload-type robustness, and the
/// <see cref="IPresentationConsumer.BuildInitiationAsync"/>
/// extension contract.
/// </summary>
public sealed class SorchaWalletPresentationConsumerTests
{
    private readonly Mock<IVerifiablePresentationValidator> _validator = new();
    private readonly SorchaWalletPresentationConsumer _sut;

    public SorchaWalletPresentationConsumerTests()
    {
        _sut = new SorchaWalletPresentationConsumer(_validator.Object, NullLogger<SorchaWalletPresentationConsumer>.Instance);
    }

    private static PresentationInitiationContext NewContext() => new(
        PresentationRequestId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        InstanceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        ActionId: 1,
        RegisterId: "reg-test",
        BlueprintId: "bp-test",
        SubmitterWallet: "ws11qqtest",
        RequirementsDigest: new byte[32],
        InitiatedAt: DateTimeOffset.UtcNow,
        VerifierClientId: null,
        CredentialType: "AssuredIdentityCredential",
        RequiredClaimNames: ["givenName", "familyName"],
        PublicBaseUrl: "https://gateway.example");

    /// <summary>Decode the base64url payload (middle segment) of a compact JWT.</summary>
    private static JsonElement DecodeJwtPayload(string jwt)
    {
        var segment = jwt.Split('.')[1];
        var padded = segment.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static VerifierSession NewSession(params string[] requiredClaims) => new()
    {
        SessionId = "sess-1",
        ClientId = "did:sorcha:org:strathcarron-council",
        Nonce = "n-1",
        RequiredVct = "AssuredIdentityCredential",
        RequiredClaims = requiredClaims,
        Purpose = "test",
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
    };

    [Fact]
    public void ConsumerName_IsTheRegisteredString()
    {
        _sut.ConsumerName.Should().Be("sorcha-wallet");
    }

    [Fact]
    public async Task VerifyAsync_ReturnsSuccess_WhenValidatorAccepts()
    {
        var session = NewSession("givenName", "familyName");
        _validator
            .Setup(v => v.ValidateAsync(session, "vp-token", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = new Dictionary<string, object?>
                {
                    ["givenName"] = "Sarah",
                    ["familyName"] = "Example"
                },
                Errors = [],
                CompletedAt = DateTimeOffset.UtcNow
            });

        var payload = new SorchaWalletVerificationPayload
        {
            VpToken = "vp-token",
            Session = session
        };

        var outcome = await _sut.VerifyAsync(NewContext(), payload, CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Success);
        outcome.VerifiedClaims.Should().NotBeNull();
        outcome.VerifiedClaims!.Should().ContainKey("givenName");
        outcome.VerifiedClaims!.Should().ContainKey("familyName");
        outcome.PresentationSubmissionHash.Should().StartWith("sha256:");
        outcome.Reason.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_FiltersClaimsToRequiredSet_MinimalDisclosure()
    {
        // Validator returns more claims than asked for; the consumer must
        // filter to the strict required set per the IPresentationConsumer
        // contract's minimal-disclosure invariant.
        var session = NewSession("givenName");
        _validator
            .Setup(v => v.ValidateAsync(session, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = new Dictionary<string, object?>
                {
                    ["givenName"] = "Sarah",
                    ["familyName"] = "Example",  // not in required — must be filtered out
                    ["secretClaim"] = "private"  // not in required — must be filtered out
                },
                Errors = [],
                CompletedAt = DateTimeOffset.UtcNow
            });

        var outcome = await _sut.VerifyAsync(NewContext(),
            new SorchaWalletVerificationPayload { VpToken = "vp", Session = session },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Success);
        outcome.VerifiedClaims!.Keys.Should().BeEquivalentTo(new[] { "givenName" });
    }

    [Fact]
    public async Task VerifyAsync_ReturnsSchemaMismatch_WhenRequiredClaimMissing()
    {
        var session = NewSession("givenName", "familyName");
        _validator
            .Setup(v => v.ValidateAsync(session, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = new Dictionary<string, object?> { ["givenName"] = "Sarah" },
                Errors = [],
                CompletedAt = DateTimeOffset.UtcNow
            });

        var outcome = await _sut.VerifyAsync(NewContext(),
            new SorchaWalletVerificationPayload { VpToken = "vp", Session = session },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(PresentationDeclineReason.SchemaMismatch);
    }

    [Theory]
    [InlineData("credential revoked",            PresentationDeclineReason.Revoked)]
    [InlineData("credential expired",            PresentationDeclineReason.ExpiredCredential)]
    [InlineData("issuer not trusted",            PresentationDeclineReason.WrongIssuer)]
    [InlineData("KB-JWT signature invalid",      PresentationDeclineReason.SignatureInvalid)]
    [InlineData("claim disclosure mismatch",     PresentationDeclineReason.SchemaMismatch)]
    [InlineData("network timeout",               PresentationDeclineReason.VerifierError)]
    public async Task VerifyAsync_MapsValidatorErrors_ToDeclineReason(string error, PresentationDeclineReason expected)
    {
        var session = NewSession("givenName");
        _validator
            .Setup(v => v.ValidateAsync(session, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = false,
                DisclosedClaims = new Dictionary<string, object?>(),
                Errors = [error],
                CompletedAt = DateTimeOffset.UtcNow
            });

        var outcome = await _sut.VerifyAsync(NewContext(),
            new SorchaWalletVerificationPayload { VpToken = "vp", Session = session },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(expected);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsVerifierError_WhenPayloadHasNoSession()
    {
        var payload = new SorchaWalletVerificationPayload { VpToken = "vp", Session = null };

        var outcome = await _sut.VerifyAsync(NewContext(), payload, CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(PresentationDeclineReason.VerifierError);
        outcome.VerifierDiagnostics.Should().NotBeNull();
        outcome.VerifierDiagnostics!["error"].Should().Be("session-missing");
    }

    [Fact]
    public async Task VerifyAsync_ReturnsVerifierError_WhenPayloadTypeIsUnexpected()
    {
        var outcome = await _sut.VerifyAsync(NewContext(), 42, CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(PresentationDeclineReason.VerifierError);
    }

    [Fact]
    public async Task VerifyAsync_AcceptsJsonElementPayload_AndDeserialises()
    {
        var session = NewSession("givenName");
        _validator
            .Setup(v => v.ValidateAsync(It.IsAny<VerifierSession>(), "vp-from-json", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = new Dictionary<string, object?> { ["givenName"] = "S" },
                Errors = [],
                CompletedAt = DateTimeOffset.UtcNow
            });

        var json = JsonSerializer.Serialize(new SorchaWalletVerificationPayload
        {
            VpToken = "vp-from-json",
            Session = session
        });
        using var doc = JsonDocument.Parse(json);

        var outcome = await _sut.VerifyAsync(NewContext(), doc.RootElement.Clone(), CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Success);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsVerifierError_WhenValidatorThrows()
    {
        var session = NewSession("givenName");
        _validator
            .Setup(v => v.ValidateAsync(session, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var outcome = await _sut.VerifyAsync(NewContext(),
            new SorchaWalletVerificationPayload { VpToken = "vp", Session = session },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(PresentationDeclineReason.VerifierError);
        outcome.VerifierDiagnostics!["error"].Should().Be(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task BuildInitiationAsync_ReturnsAuthorizeUri_WithRequestUriPointingAtServedRequestObject()
    {
        // Feature 181 (T014) — the authorize URI is the request_uri form: it carries ONLY
        // client_id + request_uri; the ask itself lives in the served request object.
        var ctx = NewContext();

        var descriptor = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        descriptor.Should().NotBeNull();
        descriptor.AuthorizationRequestUri.Should().StartWith("openid4vp://authorize?client_id=");
        descriptor.AuthorizationRequestUri.Should().Contain("&request_uri=");

        var expectedRequestUri =
            $"https://gateway.example/api/presentations/{ctx.PresentationRequestId:N}/request-object";
        descriptor.RequestUri.Should().Be(expectedRequestUri);
        descriptor.AuthorizationRequestUri.Should().Contain(Uri.EscapeDataString(expectedRequestUri));
        descriptor.Nonce.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task BuildInitiationAsync_RequestObjectJwt_CarriesDcqlQueryAndCallbackUris()
    {
        var ctx = NewContext();

        var descriptor = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        descriptor.RequestObjectJwt.Should().NotBeNullOrWhiteSpace();
        var payload = DecodeJwtPayload(descriptor.RequestObjectJwt!);

        payload.GetProperty("client_id").GetString().Should().Be("did:sorcha:org:UNKNOWN");
        payload.GetProperty("response_type").GetString().Should().Be("vp_token");
        payload.GetProperty("response_mode").GetString().Should().Be("direct_post");
        payload.GetProperty("response_uri").GetString().Should().EndWith(
            $"/api/presentations/callbacks/sorcha-wallet/{ctx.PresentationRequestId}");
        payload.GetProperty("nonce").GetString().Should().Be(descriptor.Nonce);
        payload.GetProperty("state").GetString().Should().Be(ctx.PresentationRequestId.ToString());

        // dcql_query — single ask keyed "credential" carrying vct + claim paths.
        var dcql = payload.GetProperty("dcql_query");
        var credentials = dcql.GetProperty("credentials");
        credentials.GetArrayLength().Should().Be(1);
        var credential = credentials[0];
        credential.GetProperty("id").GetString().Should().Be("credential");
        credential.GetProperty("format").GetString().Should().Be("dc+sd-jwt");
        credential.GetProperty("meta").GetProperty("vct_values")[0].GetString()
            .Should().Be("AssuredIdentityCredential");

        var claimPaths = credential.GetProperty("claims").EnumerateArray()
            .Select(c => c.GetProperty("path")[0].GetString())
            .ToList();
        claimPaths.Should().BeEquivalentTo(new[] { "givenName", "familyName" });
    }

    [Fact]
    public async Task BuildInitiationAsync_RequestObjectJwt_IsUnsignedWithAuthzReqType()
    {
        var descriptor = await _sut.BuildInitiationAsync(NewContext(), CancellationToken.None);

        descriptor.RequestObjectJwt!.Should().EndWith(".", "the unsigned JWT carries an empty signature segment");
        var headerSegment = descriptor.RequestObjectJwt!.Split('.')[0];
        var padded = headerSegment.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        var header = JsonSerializer.Deserialize<JsonElement>(
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded)));

        header.GetProperty("alg").GetString().Should().Be("none");
        header.GetProperty("typ").GetString().Should().Be("oauth-authz-req+jwt");
    }

    [Fact]
    public async Task BuildInitiationAsync_GeneratesFreshNoncePerCall()
    {
        var ctx = NewContext();

        var first = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);
        var second = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        first.Nonce.Should().NotBe(second.Nonce);
        DecodeJwtPayload(first.RequestObjectJwt!).GetProperty("nonce").GetString()
            .Should().Be(first.Nonce);
        DecodeJwtPayload(second.RequestObjectJwt!).GetProperty("nonce").GetString()
            .Should().Be(second.Nonce);
    }

    [Fact]
    public async Task BuildInitiationAsync_EmitsResolvedVerifierDid_AsClientId()
    {
        // Spec 5 — the lifecycle service supplies the council org DID via VerifierClientId.
        var ctx = NewContext() with { VerifierClientId = "did:sorcha:org:ws11qstrathcarron" };

        var descriptor = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        descriptor.AuthorizationRequestUri.Should().Contain(
            "client_id=" + Uri.EscapeDataString("did:sorcha:org:ws11qstrathcarron"));
        descriptor.AuthorizationRequestUri.Should().NotContain("did:sorcha:org:UNKNOWN");
        DecodeJwtPayload(descriptor.RequestObjectJwt!).GetProperty("client_id").GetString()
            .Should().Be("did:sorcha:org:ws11qstrathcarron");
    }

    [Fact]
    public async Task BuildInitiationAsync_FallsBackToPlaceholder_WhenVerifierDidNull()
    {
        // Graceful degradation — unresolved org DID never blocks the gate.
        var ctx = NewContext() with { VerifierClientId = null };

        var descriptor = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        descriptor.AuthorizationRequestUri.Should().Contain(
            "client_id=" + Uri.EscapeDataString("did:sorcha:org:UNKNOWN"));
        DecodeJwtPayload(descriptor.RequestObjectJwt!).GetProperty("client_id").GetString()
            .Should().Be("did:sorcha:org:UNKNOWN");
    }
}
