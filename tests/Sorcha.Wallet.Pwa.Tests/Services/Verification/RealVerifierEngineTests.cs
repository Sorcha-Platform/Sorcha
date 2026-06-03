// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Models;
using Sorcha.Wallet.Pwa.Services.Verification;
using LibVerifyOutcome = Sorcha.UI.Components.User.Models.Verification.VerifyOutcome;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Verification;

/// <summary>
/// Tests for <see cref="RealVerifierEngine"/> — the adapter that wraps
/// <see cref="IVerifiablePresentationValidator"/> behind the citizen-as-
/// verifier <see cref="IVerifierEngine"/> contract. Mocks the validator
/// so the adapter logic (offer parsing, session build, outcome mapping,
/// trust-panel JSON) can be exercised without standing up the real
/// validator pipeline.
/// </summary>
public sealed class RealVerifierEngineTests
{
    private static VerifierEngineRequest Request(string offer)
        => new(offer, VerifierClientId: "ephemeral-thumbprint", Nonce: "n-123");

    private static RealVerifierEngine NewSut(VerificationOutcome outcome, out Mock<IVerifiablePresentationValidator> validator)
    {
        validator = new Mock<IVerifiablePresentationValidator>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<VerifierSession>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(outcome);
        return new RealVerifierEngine(validator.Object, NullLogger<RealVerifierEngine>.Instance);
    }

    private static VerificationOutcome Accept(IReadOnlyDictionary<string, object?>? claims = null) =>
        new()
        {
            Accepted = true,
            DisclosedClaims = claims ?? new Dictionary<string, object?>(),
            Errors = Array.Empty<string>(),
            CompletedAt = DateTimeOffset.UtcNow,
            IssuerSignature = IssuerSignatureStatus.Verified
        };

    private static VerificationOutcome AcceptIssuerUnverified(IReadOnlyDictionary<string, object?>? claims = null) =>
        new()
        {
            Accepted = true,
            DisclosedClaims = claims ?? new Dictionary<string, object?>(),
            Errors = Array.Empty<string>(),
            CompletedAt = DateTimeOffset.UtcNow,
            IssuerSignature = IssuerSignatureStatus.NotVerified
        };

    private static VerificationOutcome Reject(string reason) =>
        new()
        {
            Accepted = false,
            DisclosedClaims = new Dictionary<string, object?>(),
            Errors = new[] { reason },
            CompletedAt = DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task VerifyAsync_MalformedJson_ReturnsFail_WithoutCallingValidator()
    {
        var sut = NewSut(Accept(), out var validator);
        var result = await sut.VerifyAsync(Request("not json at all"));

        result.Outcome.Should().Be(LibVerifyOutcome.Fail);
        result.Messages.Should().ContainSingle(m => m.Contains("Couldn't read", System.StringComparison.OrdinalIgnoreCase));
        validator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifyAsync_MissingVpToken_ReturnsFail_WithoutCallingValidator()
    {
        var sut = NewSut(Accept(), out var validator);
        var result = await sut.VerifyAsync(Request("""{"requiredVct":"x/v1"}"""));

        result.Outcome.Should().Be(LibVerifyOutcome.Fail);
        result.Messages.Should().NotBeEmpty();
        validator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifyAsync_AcceptedOutcome_MapsToPass_PreservesDisplayFields()
    {
        var claims = new Dictionary<string, object?> { ["givenName"] = "Liam", ["familyName"] = "Buchanan" };
        var sut = NewSut(Accept(claims), out var validator);
        var offer = """
            {"vpToken":"vp.token.here","delegationCredential":"d.c.here","requiredVct":"WaterEngineerCredential/v1","purpose":"Doorstep verification","holderDisplayName":"Liam Buchanan","issuerOrgName":"Caledonian Water"}
            """;

        var result = await sut.VerifyAsync(Request(offer));

        result.Outcome.Should().Be(LibVerifyOutcome.Pass);
        result.HolderDisplayName.Should().Be("Liam Buchanan");
        result.IssuerOrgName.Should().Be("Caledonian Water");
        result.CredentialType.Should().Be("WaterEngineerCredential/v1");
        result.DisclosedClaims.Should().ContainKey("givenName");
        result.TrustPanelJson.Should().NotBeNullOrEmpty();
        result.TrustPanelJson.Should().Contain("Liam Buchanan");
    }

    [Fact]
    public async Task VerifyAsync_AcceptedButIssuerUnverified_MapsToWarn_WithReducedAssuranceMessage()
    {
        // Review H3: an offline doorstep accept where the issuer signature could not be verified must
        // surface as a reduced-assurance Warn, never a plain Pass.
        var claims = new Dictionary<string, object?> { ["givenName"] = "Liam" };
        var sut = NewSut(AcceptIssuerUnverified(claims), out _);
        var offer = """
            {"vpToken":"vp.token.here","delegationCredential":"d.c.here","requiredVct":"WaterEngineerCredential/v1","holderDisplayName":"Liam Buchanan","issuerOrgName":"Caledonian Water"}
            """;

        var result = await sut.VerifyAsync(Request(offer));

        result.Outcome.Should().Be(LibVerifyOutcome.Warn,
            "an accepted credential whose issuer signature was not verified is reduced-assurance, not a full Pass");
        result.Messages.Should().ContainSingle()
            .Which.Should().Contain("Issuer not verified");
    }

    [Fact]
    public async Task VerifyAsync_RejectedOutcome_MapsToFail_SurfacesValidatorErrors()
    {
        var sut = NewSut(Reject("Status list bit set — credential revoked."), out _);
        var offer = """{"vpToken":"vp","delegationCredential":"dc","requiredVct":"x/v1"}""";

        var result = await sut.VerifyAsync(Request(offer));

        result.Outcome.Should().Be(LibVerifyOutcome.Fail);
        result.Messages.Should().ContainSingle()
            .Which.Should().Contain("revoked");
    }

    [Fact]
    public async Task VerifyAsync_PropagatesNonceAndClientId_ToValidatorSession()
    {
        var sut = NewSut(Accept(), out var validator);
        var offer = """{"vpToken":"vp","delegationCredential":"dc","requiredVct":"x/v1","purpose":"Test"}""";

        await sut.VerifyAsync(Request(offer));

        validator.Verify(v => v.ValidateAsync(
            It.Is<VerifierSession>(s =>
                s.ClientId == "ephemeral-thumbprint"
                && s.Nonce == "n-123"
                && s.RequiredVct == "x/v1"
                && s.Purpose == "Test"),
            "vp",
            "dc",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_ValidatorThrows_ReturnsFail_DoesNotPropagate()
    {
        var validator = new Mock<IVerifiablePresentationValidator>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<VerifierSession>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("pipeline blew up"));
        var sut = new RealVerifierEngine(validator.Object, NullLogger<RealVerifierEngine>.Instance);
        var offer = """{"vpToken":"vp","delegationCredential":"dc"}""";

        var result = await sut.VerifyAsync(Request(offer));

        result.Outcome.Should().Be(LibVerifyOutcome.Fail);
        result.Messages[0].Should().NotContain("pipeline blew up", "validator exception detail must not leak to the user.");
    }
}
