// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Haip.Service.Models;
using Sorcha.Haip.Service.Services;
using Sorcha.PresentationLifecycle.Abstractions;
using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="HaipPresentationConsumer"/> — Feature 111 consumer adapter.
/// Covers JsonElement deserialisation (regression guard for claude-review bug #1
/// on PR #382) and the reason-code inference table.
/// </summary>
public class HaipPresentationConsumerTests
{
    private readonly HaipPresentationConsumer _consumer = new(
        new Mock<ILogger<HaipPresentationConsumer>>().Object);

    private static PresentationInitiationContext MakeContext() => new(
        PresentationRequestId: Guid.NewGuid(),
        InstanceId: Guid.NewGuid(),
        ActionId: 3,
        RegisterId: "reg-1",
        BlueprintId: "bp-1",
        SubmitterWallet: "ws11qcitizen",
        RequirementsDigest: new byte[] { 0xAB },
        InitiatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task VerifyAsync_AcceptsJsonElementPayload_FromHttpCallback()
    {
        // Regression guard: the Blueprint callback endpoint receives the body
        // as [FromBody] JsonElement. Previously the consumer pattern-matched
        // against VerificationResult only and every presentation produced a
        // VerifierError decline.
        var result = new VerificationResult
        {
            IsValid = true,
            VerifiedClaims = new Dictionary<string, object> { ["name"] = "Alice" },
            Issuer = "did:sorcha:org:acme"
        };
        var json = JsonSerializer.SerializeToElement(result);

        var outcome = await _consumer.VerifyAsync(MakeContext(), json, CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Success);
        outcome.VerifiedClaims.Should().ContainKey("name");
        outcome.PresentationSubmissionHash.Should().StartWith("sha256:");
    }

    [Fact]
    public async Task VerifyAsync_AcceptsDirectVerificationResult_InProcess()
    {
        // In-process test doubles may pass VerificationResult directly without
        // JSON serialisation. That path must still work.
        var result = new VerificationResult { IsValid = true, Issuer = "did:sorcha:org:acme" };

        var outcome = await _consumer.VerifyAsync(MakeContext(), result, CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Success);
    }

    [Fact]
    public async Task VerifyAsync_UnsupportedPayloadType_ReturnsDeclineWithVerifierError()
    {
        var outcome = await _consumer.VerifyAsync(MakeContext(), "not-a-result", CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(PresentationDeclineReason.VerifierError);
    }

    [Theory]
    [InlineData("credential expired", PresentationDeclineReason.ExpiredCredential)]
    [InlineData("issuer not trusted", PresentationDeclineReason.WrongIssuer)]
    [InlineData("schema mismatch on input_descriptor_0", PresentationDeclineReason.SchemaMismatch)]
    [InlineData("signature invalid: x5c chain broken", PresentationDeclineReason.SignatureInvalid)]
    [InlineData("something wildly different", PresentationDeclineReason.VerifierError)]
    public async Task VerifyAsync_DeclineReasonInferredFromErrorText(string errorMessage, PresentationDeclineReason expected)
    {
        var result = new VerificationResult
        {
            IsValid = false,
            Errors = new List<string> { errorMessage }
        };
        var json = JsonSerializer.SerializeToElement(result);

        var outcome = await _consumer.VerifyAsync(MakeContext(), json, CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(expected);
    }

    [Fact]
    public async Task VerifyAsync_RevokedViaStatusCheck_ReturnsRevoked()
    {
        var result = new VerificationResult
        {
            IsValid = false,
            Errors = new List<string> { "Credential status check failed: revoked" },
            StatusCheckResult = "revoked"
        };
        var json = JsonSerializer.SerializeToElement(result);

        var outcome = await _consumer.VerifyAsync(MakeContext(), json, CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(PresentationDeclineReason.Revoked);
    }

    /// <summary>
    /// Feature 192 — the decline reason is written into the presentation-outcome transaction, so it
    /// lands on the citizen's own application record. A suspension recorded there as Revoked tells
    /// someone their credential was cancelled when it was paused.
    /// </summary>
    /// <remarks>
    /// This is the arm the compiler could not have demanded. <c>MapReason</c> is a chain of equality
    /// tests, so before the suspension arm existed a suspended credential matched nothing and fell
    /// through to <see cref="PresentationDeclineReason.VerifierError"/> — "the verifier broke" —
    /// which is worse than the revocation it used to claim, and would have shipped green.
    /// </remarks>
    [Fact]
    public async Task VerifyAsync_SuspendedViaStatusCheck_ReturnsSuspendedNotRevokedOrVerifierError()
    {
        var result = new VerificationResult
        {
            IsValid = false,
            Errors = new List<string> { "Credential is suspended (status list purpose 'suspension')." },
            StatusCheckResult = "suspended"
        };
        var json = JsonSerializer.SerializeToElement(result);

        var outcome = await _consumer.VerifyAsync(MakeContext(), json, CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(PresentationDeclineReason.Suspended);
    }

    /// <summary>
    /// A credential that is somehow both keeps the TERMINAL reason. Revocation cannot be lifted, so
    /// reporting the reversible status would imply it could come back.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_BothRevokedAndSuspended_KeepsTheTerminalReason()
    {
        var result = new VerificationResult
        {
            IsValid = false,
            Errors = new List<string> { "Credential is revoked.", "Credential is suspended." },
            StatusCheckResult = "revoked"
        };
        var json = JsonSerializer.SerializeToElement(result);

        var outcome = await _consumer.VerifyAsync(MakeContext(), json, CancellationToken.None);

        outcome.Reason.Should().Be(PresentationDeclineReason.Revoked);
    }
}
