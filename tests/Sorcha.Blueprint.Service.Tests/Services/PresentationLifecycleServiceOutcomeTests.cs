// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Configuration;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.Cryptography.Interfaces;
using Sorcha.PresentationLifecycle.Abstractions;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// T036 — unit tests for <see cref="PresentationLifecycleService.HandleOutcomeAsync"/>.
/// Covers idempotency (SET NX), decline/success dispatch, and late-outcome-after-abandonment behaviour.
/// </summary>
public class PresentationLifecycleServiceOutcomeTests
{
    // BuildPresentationOutcomeAsync is an extension method — Moq cannot mock it,
    // so we use a real TransactionBuilderService (self-contained for the outcome path).
    private readonly TransactionBuilderService _builder = new(
        new Mock<ICryptoModule>().Object,
        new Mock<IHashProvider>().Object,
        new Mock<ISymmetricCrypto>().Object,
        new Mock<ILogger<TransactionBuilderService>>().Object);
    private readonly Mock<IWalletServiceClient> _wallet = new();
    private readonly Mock<IValidatorServiceClient> _validator = new();
    private readonly Mock<IPendingPresentationStore> _store = new();

    private PresentationLifecycleService Make(IPresentationConsumer consumer)
    {
        var opts = Options.Create(new PresentationLifecycleOptions());
        _wallet.Setup(w => w.SignTransactionAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = new byte[] { 0x1 },
                PublicKey = new byte[] { 0x2 },
                Algorithm = "ED25519",
                SignedBy = "ws11qcitizen"
            });
        _validator.Setup(v => v.GetNextSequenceNumberAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
        _validator.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "tx-ok" });

        return new PresentationLifecycleService(
            _builder, _wallet.Object, _validator.Object,
            _store.Object, [consumer], opts,
            new Mock<ILogger<PresentationLifecycleService>>().Object);
    }

    private static PendingPresentation MakePending(Guid id, string consumer = "test", string detailLevel = "minimal")
        => new()
        {
            PresentationRequestId = id,
            InstanceId = Guid.NewGuid(),
            ActionId = 3,
            RegisterId = "reg-1",
            BlueprintId = "bp-1",
            SubmitterWallet = "ws11qcitizen",
            ConsumerName = consumer,
            DraftPayloadJson = "{}",
            CredentialRequirementDigestHex = "deadbeef",
            RecordAbandonment = false,
            OutcomeDetailLevel = detailLevel,
            ValidityWindowSeconds = 600,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private sealed class FakeConsumer(PresentationOutcome outcome) : IPresentationConsumer
    {
        public string ConsumerName => "test";
        public Task<PresentationOutcome> VerifyAsync(
            PresentationInitiationContext context, object verifierPayload, CancellationToken ct)
            => Task.FromResult(outcome);
    }

    [Fact]
    public async Task HandleOutcomeAsync_UnknownRequestId_Throws()
    {
        _store.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PendingPresentation?)null);

        var svc = Make(new FakeConsumer(new PresentationOutcome(
            PresentationOutcomeKind.Success, null, null, null, null)));

        var act = () => svc.HandleOutcomeAsync("test", Guid.NewGuid(), new { }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No pending presentation found*");
    }

    [Fact]
    public async Task HandleOutcomeAsync_Success_WritesOutcomeTx_AndMarksSentinelSuccess()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePending(id));
        _store.Setup(s => s.GetOutcomeSentinelAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _store.Setup(s => s.TryClaimOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        string? capturedSentinel = null;
        _store.Setup(s => s.SetOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, CancellationToken>((_, v, _) => capturedSentinel = v)
            .Returns(Task.CompletedTask);

        var svc = Make(new FakeConsumer(new PresentationOutcome(
            PresentationOutcomeKind.Success,
            VerifiedClaims: new Dictionary<string, object> { ["name"] = "Alice" },
            Reason: null,
            VerifierDiagnostics: null,
            PresentationSubmissionHash: "sha256:abc")));

        var result = await svc.HandleOutcomeAsync("test", id, new { }, CancellationToken.None);

        result.Kind.Should().Be(PresentationOutcomeKind.Success);
        result.IsIdempotentReplay.Should().BeFalse();
        result.IsLateAfterAbandonment.Should().BeFalse();
        result.OutcomeTransactionId.Should().NotBeNullOrEmpty();
        capturedSentinel.Should().Be("success");
    }

    [Fact]
    public async Task HandleOutcomeAsync_Decline_WritesDeclineTx_AndMarksSentinelDecline()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(MakePending(id));
        _store.Setup(s => s.GetOutcomeSentinelAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        _store.Setup(s => s.TryClaimOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        string? captured = null;
        _store.Setup(s => s.SetOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, CancellationToken>((_, v, _) => captured = v)
            .Returns(Task.CompletedTask);

        var svc = Make(new FakeConsumer(new PresentationOutcome(
            PresentationOutcomeKind.Decline,
            VerifiedClaims: null,
            Reason: PresentationDeclineReason.ExpiredCredential,
            VerifierDiagnostics: null,
            PresentationSubmissionHash: null)));

        var result = await svc.HandleOutcomeAsync("test", id, new { }, CancellationToken.None);

        result.Kind.Should().Be(PresentationOutcomeKind.Decline);
        result.OutcomeTransactionId.Should().NotBeNullOrEmpty();
        captured.Should().Be("decline");
    }

    [Fact]
    public async Task HandleOutcomeAsync_SentinelAlreadySuccess_DeduplicatesAsReplay()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(MakePending(id));
        _store.Setup(s => s.GetOutcomeSentinelAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync("success");

        var svc = Make(new FakeConsumer(new PresentationOutcome(
            PresentationOutcomeKind.Success,
            new Dictionary<string, object>(), null, null, "h")));

        var result = await svc.HandleOutcomeAsync("test", id, new { }, CancellationToken.None);

        result.IsIdempotentReplay.Should().BeTrue();
        result.OutcomeTransactionId.Should().BeEmpty();
        _store.Verify(s => s.SetOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleOutcomeAsync_LostSentinelRace_DeduplicatesAsReplay()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(MakePending(id));
        _store.Setup(s => s.GetOutcomeSentinelAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        _store.Setup(s => s.TryClaimOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var svc = Make(new FakeConsumer(new PresentationOutcome(
            PresentationOutcomeKind.Success, new Dictionary<string, object>(), null, null, "h")));

        var result = await svc.HandleOutcomeAsync("test", id, new { }, CancellationToken.None);

        result.IsIdempotentReplay.Should().BeTrue();
    }

    [Fact]
    public async Task HandleOutcomeAsync_LateAfterAbandonment_BypassesNx_AndMarksAbandonedWithOutcome()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(MakePending(id));
        _store.Setup(s => s.GetOutcomeSentinelAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync("abandoned");
        string? captured = null;
        _store.Setup(s => s.SetOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, CancellationToken>((_, v, _) => captured = v)
            .Returns(Task.CompletedTask);

        var svc = Make(new FakeConsumer(new PresentationOutcome(
            PresentationOutcomeKind.Success, new Dictionary<string, object>(), null, null, "h")));

        var result = await svc.HandleOutcomeAsync("test", id, new { }, CancellationToken.None);

        result.IsLateAfterAbandonment.Should().BeTrue();
        result.IsIdempotentReplay.Should().BeFalse();
        captured.Should().Be("abandoned+outcome");
        _store.Verify(s => s.TryClaimOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleOutcomeAsync_ConsumerNameMismatch_Throws()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePending(id, consumer: "haip"));

        var svc = Make(new FakeConsumer(new PresentationOutcome(
            PresentationOutcomeKind.Success, new Dictionary<string, object>(), null, null, "h")));

        var act = () => svc.HandleOutcomeAsync("test", id, new { }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not match pending consumer*");
    }
}
