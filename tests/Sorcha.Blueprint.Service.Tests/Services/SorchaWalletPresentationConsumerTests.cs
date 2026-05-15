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
        InitiatedAt: DateTimeOffset.UtcNow);

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
    public async Task BuildInitiationAsync_ReturnsOID4VP_UriCarryingRequestIdAndNonce()
    {
        var ctx = NewContext();

        var descriptor = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        descriptor.Should().NotBeNull();
        descriptor.AuthorizationRequestUri.Should().StartWith("openid4vp://");
        descriptor.AuthorizationRequestUri.Should().Contain("response_type=vp_token");
        descriptor.AuthorizationRequestUri.Should().Contain(ctx.PresentationRequestId.ToString("N"));
        descriptor.Nonce.Should().NotBeNullOrWhiteSpace();
        descriptor.AuthorizationRequestUri.Should().Contain(Uri.EscapeDataString(descriptor.Nonce!));
    }

    [Fact]
    public async Task BuildInitiationAsync_GeneratesFreshNoncePerCall()
    {
        var ctx = NewContext();

        var first = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);
        var second = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        first.Nonce.Should().NotBe(second.Nonce);
    }
}
