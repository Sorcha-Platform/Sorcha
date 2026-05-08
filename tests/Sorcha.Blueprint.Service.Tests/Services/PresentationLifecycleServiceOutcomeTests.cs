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
using Sorcha.ServiceClients.Register;
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
        _store.Setup(s => s.TryClaimOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        string? capturedSentinel = null;
        _store.Setup(s => s.SetOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, int, CancellationToken>((_, v, _, _) => capturedSentinel = v)
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
        _store.Setup(s => s.TryClaimOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        string? captured = null;
        _store.Setup(s => s.SetOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, int, CancellationToken>((_, v, _, _) => captured = v)
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
        _store.Verify(s => s.SetOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleOutcomeAsync_LostSentinelRace_DeduplicatesAsReplay()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(MakePending(id));
        _store.Setup(s => s.GetOutcomeSentinelAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        _store.Setup(s => s.TryClaimOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
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
        _store.Setup(s => s.SetOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, int, CancellationToken>((_, v, _, _) => captured = v)
            .Returns(Task.CompletedTask);

        var svc = Make(new FakeConsumer(new PresentationOutcome(
            PresentationOutcomeKind.Success, new Dictionary<string, object>(), null, null, "h")));

        var result = await svc.HandleOutcomeAsync("test", id, new { }, CancellationToken.None);

        result.IsLateAfterAbandonment.Should().BeTrue();
        result.IsIdempotentReplay.Should().BeFalse();
        captured.Should().Be("abandoned+outcome");
        _store.Verify(s => s.TryClaimOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
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

    // -------------------------------------------------------------------------
    // Feature 119 — seal-aware ordering tests (T014, T015).
    // -------------------------------------------------------------------------

    private static PendingPresentation MakePendingWithInitiated(Guid id, string consumer = "test")
        => MakePending(id, consumer) with { InitiatedTransactionId = "tx-init-1" };

    private PresentationLifecycleService MakeWithCoordinator(
        IPresentationConsumer consumer,
        Mock<IRegisterServiceClient> registerMock,
        Mock<Sorcha.Blueprint.Service.Services.Interfaces.IPresentationSealCoordinator> coordMock)
    {
        var opts = Options.Create(new PresentationLifecycleOptions());
        _wallet.Setup(w => w.SignTransactionAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = new byte[] { 0x1 }, PublicKey = new byte[] { 0x2 },
                Algorithm = "ED25519", SignedBy = "ws11qcitizen"
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
            new Mock<ILogger<PresentationLifecycleService>>().Object,
            haipClient: null, metrics: null, clock: null,
            serviceProvider: null,
            registerClient: registerMock.Object,
            sealCoordinator: coordMock.Object);
    }

    [Fact]
    public async Task HandleOutcomeAsync_PredecessorSealed_SubmitsInline_NoEnqueue()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePendingWithInitiated(id));
        _store.Setup(s => s.GetOutcomeSentinelAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        _store.Setup(s => s.TryClaimOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var register = new Mock<IRegisterServiceClient>();
        register.Setup(r => r.GetTransactionAsync("reg-1", "tx-init-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel { TxId = "tx-init-1", RegisterId = "reg-1" });
        var coord = new Mock<Sorcha.Blueprint.Service.Services.Interfaces.IPresentationSealCoordinator>();

        var svc = MakeWithCoordinator(
            new FakeConsumer(new PresentationOutcome(
                PresentationOutcomeKind.Success, new Dictionary<string, object>(), null, null, "h")),
            register, coord);

        var result = await svc.HandleOutcomeAsync("test", id, new { }, CancellationToken.None);

        result.IsIdempotentReplay.Should().BeFalse();
        // Inline submit happened.
        _validator.Verify(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()),
            Times.Once);
        // Coordinator NOT used for the submission queue.
        coord.Verify(c => c.EnqueueSubmissionAsync(It.IsAny<SealAwaitingSubmission>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // FR-015 advancement still goes through coordinator on success.
        coord.Verify(c => c.EnqueueAdvancementAsync(It.IsAny<SealAwaitingAdvancement>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleOutcomeAsync_PredecessorPending_EnqueuesSubmission_AndAdvancement_AndSetsSealSentinel()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePendingWithInitiated(id));
        _store.Setup(s => s.GetOutcomeSentinelAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        _store.Setup(s => s.TryClaimOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        string? capturedSentinel = null;
        _store.Setup(s => s.SetOutcomeSentinelAsync(id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, int, CancellationToken>((_, v, _, _) => capturedSentinel = v)
            .Returns(Task.CompletedTask);

        var register = new Mock<IRegisterServiceClient>();
        register.Setup(r => r.GetTransactionAsync("reg-1", "tx-init-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Register.Models.TransactionModel?)null);   // not yet sealed

        SealAwaitingSubmission? capturedSubmission = null;
        SealAwaitingAdvancement? capturedAdvance = null;
        var coord = new Mock<Sorcha.Blueprint.Service.Services.Interfaces.IPresentationSealCoordinator>();
        coord.Setup(c => c.EnqueueSubmissionAsync(It.IsAny<SealAwaitingSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<SealAwaitingSubmission, CancellationToken>((s, _) => capturedSubmission = s)
            .Returns(Task.CompletedTask);
        coord.Setup(c => c.EnqueueAdvancementAsync(It.IsAny<SealAwaitingAdvancement>(), It.IsAny<CancellationToken>()))
            .Callback<SealAwaitingAdvancement, CancellationToken>((a, _) => capturedAdvance = a)
            .Returns(Task.CompletedTask);

        var svc = MakeWithCoordinator(
            new FakeConsumer(new PresentationOutcome(
                PresentationOutcomeKind.Success, new Dictionary<string, object>(), null, null, "h")),
            register, coord);

        var result = await svc.HandleOutcomeAsync("test", id, new { }, CancellationToken.None);

        result.IsIdempotentReplay.Should().BeFalse();
        _validator.Verify(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()),
            Times.Never, "submission should be deferred to coordinator");
        capturedSentinel.Should().Be("outcome-pending-seal");
        capturedSubmission.Should().NotBeNull();
        capturedSubmission!.Site.Should().Be(SealAwaitingSubmissionSite.Outcome);
        capturedSubmission.PredecessorTxId.Should().Be("tx-init-1");
        capturedSubmission.TargetSentinelOnSuccess.Should().Be("success");
        capturedAdvance.Should().NotBeNull();
        capturedAdvance!.PresentationRequestId.Should().Be(id);
        capturedAdvance.CompletedActionId.Should().Be(3);
    }

    [Fact]
    public async Task HandleOutcomeAsync_SentinelOutcomePendingSeal_DeduplicatesAsReplay()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(MakePending(id));
        _store.Setup(s => s.GetOutcomeSentinelAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync("outcome-pending-seal");

        var svc = Make(new FakeConsumer(new PresentationOutcome(
            PresentationOutcomeKind.Success, new Dictionary<string, object>(), null, null, "h")));

        var result = await svc.HandleOutcomeAsync("test", id, new { }, CancellationToken.None);

        result.IsIdempotentReplay.Should().BeTrue();
    }
}
