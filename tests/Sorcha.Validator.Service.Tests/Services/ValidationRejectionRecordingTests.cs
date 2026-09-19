// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

using Sorcha.Register.Models;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

using Xunit;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// #1669 — when the validator refuses a transaction it must record WHY, somewhere the submitter
/// can reach.
/// </summary>
/// <remarks>
/// Submission is asynchronous: the caller was handed a 202 and a transaction id, and before this
/// the refusal existed only as a WRN line in this service's container log. Cold-start run #5 lost
/// a participant revocation to <c>VAL_CHAIN_FORK</c> exactly that way.
/// </remarks>
public class ValidationRejectionRecordingTests
{
    private const string RegisterId = "4145b4e7102246ae997f2510acba2220";
    private const string TxId = "abe4e5f71553a670e3ea01e0caa60c265052686f5c444cb221e56a1f235e0a00";

    [Fact]
    public async Task ARejectedTransaction_IsRecordedWithItsCodeAndReason()
    {
        var recorded = new List<TransactionRejection>();
        var sut = Build(recorded, IsValid: false);

        await sut.ProcessRegisterAsync(RegisterId);

        recorded.Should().ContainSingle();
        recorded[0].TransactionId.Should().Be(TxId);
        recorded[0].RegisterId.Should().Be(RegisterId);
        recorded[0].Code.Should().Be("VAL_CHAIN_FORK");
        recorded[0].Message.Should().Contain("Fork detected");
    }

    [Fact]
    public async Task AnAcceptedTransaction_RecordsNothing()
    {
        // The counterfactual: a rejection log that fires on success would tell a caller its
        // perfectly good transaction had been refused.
        var recorded = new List<TransactionRejection>();
        var sut = Build(recorded, IsValid: true);

        await sut.ProcessRegisterAsync(RegisterId);

        recorded.Should().BeEmpty();
    }

    private static ValidationEngineService Build(List<TransactionRejection> recorded, bool IsValid)
    {
        var tx = new Sorcha.Validator.Service.Models.Transaction
        {
            TransactionId = TxId,
            RegisterId = RegisterId,
            Payload = System.Text.Json.JsonDocument.Parse("{}").RootElement,
            PayloadHash = new string('0', 64),
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures = [],
        };

        var poller = new Mock<ITransactionPoolPoller>();
        poller.Setup(p => p.PollTransactionsAsync(RegisterId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);

        var result = new ValidationEngineResult
        {
            TransactionId = TxId,
            RegisterId = RegisterId,
            IsValid = IsValid,
            Errors = IsValid
                ? []
                : [new ValidationEngineError
                    {
                        Code = "VAL_CHAIN_FORK",
                        Category = ValidationErrorCategory.Chain,
                        Message = "Fork detected: 1 existing transaction(s) already reference 'd20cfff7'"
                    }],
        };

        var engine = new Mock<IValidationEngine>();
        engine.Setup(e => e.ValidateBatchAsync(It.IsAny<IReadOnlyList<Sorcha.Validator.Service.Models.Transaction>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([result]);

        var services = new ServiceCollection();
        services.AddSingleton(engine.Object);
        var provider = services.BuildServiceProvider();

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(provider);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var rejectionLog = new Mock<ITransactionRejectionLog>();
        rejectionLog.Setup(l => l.RecordAsync(It.IsAny<TransactionRejection>(), It.IsAny<CancellationToken>()))
            .Callback((TransactionRejection r, CancellationToken _) => recorded.Add(r))
            .Returns(Task.CompletedTask);

        var queue = new Mock<IVerifiedTransactionQueue>();
        queue.Setup(q => q.Enqueue(It.IsAny<string>(), It.IsAny<Sorcha.Validator.Service.Models.Transaction>(),
            It.IsAny<int>())).Returns(true);

        return new ValidationEngineService(
            scopeFactory.Object,
            poller.Object,
            queue.Object,
            Mock.Of<IRegisterMonitoringRegistry>(),
            Options.Create(new ValidationEngineConfiguration()),
            new ValidatorMempoolMetrics(
                new ServiceCollection().AddMetrics().BuildServiceProvider()
                    .GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            rejectionLog.Object,
            NullLogger<ValidationEngineService>.Instance);
    }
}
