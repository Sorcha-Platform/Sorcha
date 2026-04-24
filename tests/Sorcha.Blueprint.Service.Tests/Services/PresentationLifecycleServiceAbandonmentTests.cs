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
/// Feature 111 US4 — unit tests for <see cref="PresentationLifecycleService.HandleAbandonmentAsync"/>.
/// Covers opt-in gating, outcome-sentinel deference, and sentinel claim races.
/// </summary>
public class PresentationLifecycleServiceAbandonmentTests
{
    private readonly TransactionBuilderService _builder = new(
        new Mock<ICryptoModule>().Object,
        new Mock<IHashProvider>().Object,
        new Mock<ISymmetricCrypto>().Object,
        new Mock<ILogger<TransactionBuilderService>>().Object);
    private readonly Mock<IWalletServiceClient> _wallet = new();
    private readonly Mock<IValidatorServiceClient> _validator = new();
    private readonly Mock<IPendingPresentationStore> _store = new();

    public PresentationLifecycleServiceAbandonmentTests()
    {
        _wallet.Setup(w => w.SignTransactionAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = [0x1], PublicKey = [0x2], Algorithm = "ED25519", SignedBy = "w"
            });
        _validator.Setup(v => v.GetNextSequenceNumberAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
        _validator.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true });
    }

    private PresentationLifecycleService Make() => new(
        _builder, _wallet.Object, _validator.Object, _store.Object,
        [new Mock<IPresentationConsumer>().Object],
        Options.Create(new PresentationLifecycleOptions()),
        new Mock<ILogger<PresentationLifecycleService>>().Object);

    private static PendingPresentation Pending(Guid id, bool recordAbandonment)
        => new()
        {
            PresentationRequestId = id,
            InstanceId = Guid.NewGuid(), ActionId = 3, RegisterId = "reg-1", BlueprintId = "bp-1",
            SubmitterWallet = "w", ConsumerName = "haip", DraftPayloadJson = "{}",
            CredentialRequirementDigestHex = "deadbeef",
            RecordAbandonment = recordAbandonment, OutcomeDetailLevel = "minimal",
            ValidityWindowSeconds = 600, CreatedAt = DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task HandleAbandonmentAsync_UnknownRequestId_NoOp()
    {
        _store.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PendingPresentation?)null);

        await Make().HandleAbandonmentAsync(Guid.NewGuid(), CancellationToken.None);

        _validator.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAbandonmentAsync_BlueprintOptedOut_DoesNotWriteTx_AndDeletesPending()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pending(id, recordAbandonment: false));

        await Make().HandleAbandonmentAsync(id, CancellationToken.None);

        _validator.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.DeleteAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAbandonmentAsync_SentinelAlreadySuccess_SkipsWrite()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pending(id, recordAbandonment: true));
        _store.Setup(s => s.GetOutcomeSentinelAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("success");

        await Make().HandleAbandonmentAsync(id, CancellationToken.None);

        _validator.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.TryClaimOutcomeSentinelAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAbandonmentAsync_OptedIn_NoSentinel_WritesAbandonmentTx()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pending(id, recordAbandonment: true));
        _store.Setup(s => s.GetOutcomeSentinelAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        string? claimedValue = null;
        _store.Setup(s => s.TryClaimOutcomeSentinelAsync(
                id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, int, CancellationToken>((_, v, _, _) => claimedValue = v)
            .ReturnsAsync(true);

        await Make().HandleAbandonmentAsync(id, CancellationToken.None);

        claimedValue.Should().Be("abandoned");
        _validator.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAbandonmentAsync_ValidatorRejects_RollsBackSentinelClaim()
    {
        // Round-2 review bug: on validator rejection, the "abandoned" sentinel
        // must be rolled back so a later outcome callback isn't misclassified
        // as late-after-abandonment when no tx was actually written.
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pending(id, recordAbandonment: true));
        _store.Setup(s => s.GetOutcomeSentinelAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _store.Setup(s => s.TryClaimOutcomeSentinelAsync(
                id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _validator.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = false, ErrorCode = "ERR" });

        await Make().HandleAbandonmentAsync(id, CancellationToken.None);

        _store.Verify(s => s.DeleteOutcomeSentinelAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAbandonmentAsync_LostSentinelRace_DoesNotWriteTx()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pending(id, recordAbandonment: true));
        _store.Setup(s => s.GetOutcomeSentinelAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _store.Setup(s => s.TryClaimOutcomeSentinelAsync(
                id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Make().HandleAbandonmentAsync(id, CancellationToken.None);

        _validator.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
