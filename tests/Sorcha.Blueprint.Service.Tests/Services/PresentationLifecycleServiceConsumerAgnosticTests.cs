// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
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
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 111 US5 — architectural guards proving the lifecycle primitive is
/// consumer-agnostic. The abstractions assembly must not reference any HAIP
/// type, and <see cref="PresentationLifecycleService.HandleOutcomeAsync"/>
/// must dispatch through <see cref="IPresentationConsumer"/> without
/// hardcoding "haip".
/// </summary>
public class PresentationLifecycleServiceConsumerAgnosticTests
{
    [Fact]
    public void AbstractionsAssembly_HasNoHaipReferences()
    {
        var abstractions = typeof(IPresentationConsumer).Assembly;

        // No HAIP-named type can appear in the abstractions assembly.
        abstractions.GetTypes()
            .Select(t => t.FullName ?? t.Name)
            .Should().NotContain(n => n.Contains("Haip", StringComparison.OrdinalIgnoreCase),
                "the lifecycle abstractions must be consumer-agnostic (Feature 111 US5 architectural guard)");

        // No referenced assembly may be a HAIP project — the abstractions must
        // be loadable without any HAIP binary on disk.
        abstractions.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Should().NotContain(n => n.Contains("Haip", StringComparison.OrdinalIgnoreCase),
                "the abstractions assembly must not take a dependency on any HAIP assembly");
    }

    [Fact]
    public async Task HandleOutcomeAsync_DispatchesToNonHaipConsumer_WhenRegistered()
    {
        // Prove that a future non-HAIP consumer (e.g. file-upload-deadline) plugs
        // in through the same abstraction without any changes to the lifecycle
        // service. The test mock masquerades as a "file-upload-deadline" consumer
        // and the service dispatches to it correctly.
        var consumerName = "file-upload-deadline";
        var fakeConsumer = new FileUploadDeadlineTestDouble(consumerName);

        var builder = new TransactionBuilderService(
            new Mock<ICryptoModule>().Object,
            new Mock<IHashProvider>().Object,
            new Mock<ISymmetricCrypto>().Object,
            new Mock<ILogger<TransactionBuilderService>>().Object);
        var walletMock = new Mock<IWalletServiceClient>();
        walletMock.Setup(w => w.SignTransactionAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = [0x1], PublicKey = [0x2], Algorithm = "ED25519", SignedBy = "w"
            });
        var validatorMock = new Mock<IValidatorServiceClient>();
        validatorMock.Setup(v => v.GetNextSequenceNumberAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
        validatorMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true });
        var storeMock = new Mock<IPendingPresentationStore>();
        var pendingId = Guid.NewGuid();
        storeMock.Setup(s => s.GetAsync(pendingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PendingPresentation
            {
                PresentationRequestId = pendingId,
                InstanceId = Guid.NewGuid(), ActionId = 3, RegisterId = "reg-1", BlueprintId = "bp-1",
                SubmitterWallet = "w", ConsumerName = consumerName,
                DraftPayloadJson = "{}", CredentialRequirementDigestHex = "deadbeef",
                RecordAbandonment = false, OutcomeDetailLevel = "minimal",
                ValidityWindowSeconds = 600, CreatedAt = DateTimeOffset.UtcNow
            });
        storeMock.Setup(s => s.GetOutcomeSentinelAsync(pendingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        storeMock.Setup(s => s.TryClaimOutcomeSentinelAsync(
                pendingId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // No HAIP client passed — lifecycle service must function end-to-end for
        // outcome handling without HAIP infrastructure.
        var service = new PresentationLifecycleService(
            builder, walletMock.Object, validatorMock.Object, storeMock.Object,
            consumers: [fakeConsumer],
            Options.Create(new PresentationLifecycleOptions()),
            new Mock<ILogger<PresentationLifecycleService>>().Object,
            haipClient: null);

        var result = await service.HandleOutcomeAsync(
            consumerName, pendingId, verifierPayload: new { ok = true }, CancellationToken.None);

        result.Kind.Should().Be(PresentationOutcomeKind.Success);
        fakeConsumer.WasInvoked.Should().BeTrue();
    }

    private sealed class FileUploadDeadlineTestDouble(string name) : IPresentationConsumer
    {
        public bool WasInvoked { get; private set; }
        public string ConsumerName { get; } = name;
        public Task<PresentationOutcome> VerifyAsync(
            PresentationInitiationContext context,
            object verifierPayload,
            CancellationToken ct)
        {
            WasInvoked = true;
            return Task.FromResult(new PresentationOutcome(
                Kind: PresentationOutcomeKind.Success,
                VerifiedClaims: new Dictionary<string, object> { ["fileHash"] = "sha256:1234" },
                Reason: null,
                VerifierDiagnostics: null,
                PresentationSubmissionHash: "sha256:bundle"));
        }
    }
}
