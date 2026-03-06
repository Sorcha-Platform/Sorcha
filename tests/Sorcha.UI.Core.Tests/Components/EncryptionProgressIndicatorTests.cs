// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Admin;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Models.Workflows;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Admin;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components;

/// <summary>
/// Tests for the EncryptionProgressIndicator Blazor component.
/// Covers core polling behavior (T012) and retry functionality (T013).
/// </summary>
public class EncryptionProgressIndicatorTests : BunitContext
{
    private readonly Mock<IOperationStatusService> _operationServiceMock;
    private readonly Mock<IWorkflowService> _workflowServiceMock;

    public EncryptionProgressIndicatorTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        _operationServiceMock = new Mock<IOperationStatusService>();
        _workflowServiceMock = new Mock<IWorkflowService>();

        Services.AddSingleton(_operationServiceMock.Object);
    }

    // ---------------------------------------------------------------
    // T012: Core functionality tests
    // ---------------------------------------------------------------

    [Fact]
    public void GetStatusAsync_InProgress_ReturnsOperationWithStage()
    {
        // Arrange
        var operation = CreateOperation(
            stage: OperationStage.EncryptingPerRecipient,
            percentComplete: 50,
            recipientCount: 4,
            processedRecipients: 2);

        _operationServiceMock
            .Setup(s => s.GetStatusAsync("op-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Act
        var cut = Render<EncryptionProgressIndicator>(parameters => parameters
            .Add(p => p.OperationId, "op-123"));

        // Assert — component renders progress state (not complete/failed alerts)
        cut.Markup.Should().Contain("2 of 4 recipients processed");
        cut.Markup.Should().Contain("Encrypting Per Recipient");
        cut.Markup.Should().NotContain("Encryption complete");
        cut.Markup.Should().NotContain("Encryption failed");
    }

    [Fact]
    public void GetStatusAsync_Complete_ReturnsIsCompleteTrue()
    {
        // Arrange
        var operation = CreateOperation(
            stage: OperationStage.Complete,
            percentComplete: 100,
            recipientCount: 3,
            processedRecipients: 3);

        _operationServiceMock
            .Setup(s => s.GetStatusAsync("op-done", It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Act
        var cut = Render<EncryptionProgressIndicator>(parameters => parameters
            .Add(p => p.OperationId, "op-done"));

        // Assert — model properties
        operation.IsComplete.Should().BeTrue();
        operation.IsSuccess.Should().BeTrue();

        // Assert — component renders success alert
        cut.Markup.Should().Contain("Encryption complete");
    }

    [Fact]
    public void GetStatusAsync_Failed_ReturnsIsCompleteTrueIsSuccessFalse()
    {
        // Arrange
        var operation = CreateOperation(
            stage: OperationStage.Failed,
            errorMessage: "Key derivation error");

        _operationServiceMock
            .Setup(s => s.GetStatusAsync("op-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Act
        var cut = Render<EncryptionProgressIndicator>(parameters => parameters
            .Add(p => p.OperationId, "op-fail"));

        // Assert — model properties
        operation.IsComplete.Should().BeTrue();
        operation.IsSuccess.Should().BeFalse();

        // Assert — component renders error with message
        cut.Markup.Should().Contain("Encryption failed");
        cut.Markup.Should().Contain("Key derivation error");
    }

    [Fact]
    public void EncryptionOperationViewModel_CompleteWithTransactionHash_HasHash()
    {
        // Arrange
        var txHash = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
        var operation = CreateOperation(
            stage: OperationStage.Complete,
            percentComplete: 100,
            transactionHash: txHash);

        _operationServiceMock
            .Setup(s => s.GetStatusAsync("op-tx", It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Act
        var cut = Render<EncryptionProgressIndicator>(parameters => parameters
            .Add(p => p.OperationId, "op-tx"));

        // Assert — model property
        operation.TransactionHash.Should().Be(txHash);

        // Assert — component renders transaction hash
        cut.Markup.Should().Contain(txHash);
        cut.Markup.Should().Contain("Transaction:");
    }

    [Fact]
    public void OnInitializedAsync_Complete_InvokesOnCompletedCallback()
    {
        // Arrange
        var completedCalled = false;
        var operation = CreateOperation(stage: OperationStage.Complete, percentComplete: 100);

        _operationServiceMock
            .Setup(s => s.GetStatusAsync("op-cb", It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Act
        var cut = Render<EncryptionProgressIndicator>(parameters => parameters
            .Add(p => p.OperationId, "op-cb")
            .Add(p => p.OnCompleted, () => { completedCalled = true; }));

        // Assert — callback was invoked on first poll seeing complete
        completedCalled.Should().BeTrue();
    }

    [Fact]
    public void OnInitializedAsync_Failed_InvokesOnFailedCallback()
    {
        // Arrange
        var failedCalled = false;
        var operation = CreateOperation(stage: OperationStage.Failed, errorMessage: "timeout");

        _operationServiceMock
            .Setup(s => s.GetStatusAsync("op-fcb", It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Act
        var cut = Render<EncryptionProgressIndicator>(parameters => parameters
            .Add(p => p.OperationId, "op-fcb")
            .Add(p => p.OnFailed, () => { failedCalled = true; }));

        // Assert — callback was invoked on failure
        failedCalled.Should().BeTrue();
    }

    [Fact]
    public void Failed_WithoutOriginalRequest_DoesNotShowRetryButton()
    {
        // Arrange
        var operation = CreateOperation(stage: OperationStage.Failed, errorMessage: "error");

        _operationServiceMock
            .Setup(s => s.GetStatusAsync("op-noretry", It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Act
        var cut = Render<EncryptionProgressIndicator>(parameters => parameters
            .Add(p => p.OperationId, "op-noretry"));

        // Assert — no retry button when OriginalRequest is null
        cut.Markup.Should().NotContain("Retry");
    }

    // ---------------------------------------------------------------
    // T013: Retry functionality tests
    // ---------------------------------------------------------------

    [Fact]
    public void Failed_WithOriginalRequest_ShowsRetryButton()
    {
        // Arrange
        var operation = CreateOperation(stage: OperationStage.Failed, errorMessage: "network error");
        var request = CreateTestRequest();

        _operationServiceMock
            .Setup(s => s.GetStatusAsync("op-retry-show", It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Act
        var cut = Render<EncryptionProgressIndicator>(parameters => parameters
            .Add(p => p.OperationId, "op-retry-show")
            .Add(p => p.OriginalRequest, request)
            .Add(p => p.WorkflowService, _workflowServiceMock.Object));

        // Assert — retry button is rendered
        cut.Markup.Should().Contain("Retry");
    }

    [Fact]
    public async Task RetryFlow_FailedOperation_CanResubmit()
    {
        // Arrange
        var request = CreateTestRequest();
        var failedOperation = CreateOperation(stage: OperationStage.Failed, errorMessage: "transient error");
        var newOperation = CreateOperation(
            operationId: "op-retry-new",
            stage: OperationStage.EncryptingPerRecipient,
            percentComplete: 10);

        _operationServiceMock
            .Setup(s => s.GetStatusAsync("op-retry-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedOperation);

        _operationServiceMock
            .Setup(s => s.GetStatusAsync("op-retry-new", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newOperation);

        _workflowServiceMock
            .Setup(s => s.SubmitActionExecuteAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActionSubmissionResultViewModel
            {
                TransactionId = "tx-retry",
                InstanceId = "inst-1",
                IsAsync = true,
                OperationId = "op-retry-new"
            });

        var cut = Render<EncryptionProgressIndicator>(parameters => parameters
            .Add(p => p.OperationId, "op-retry-1")
            .Add(p => p.OriginalRequest, request)
            .Add(p => p.WorkflowService, _workflowServiceMock.Object));

        // Act — click the retry button
        var retryButton = cut.Find("button");
        await cut.InvokeAsync(() => retryButton.Click());

        // Assert — WorkflowService was called with original request
        _workflowServiceMock.Verify(
            s => s.SubmitActionExecuteAsync(request, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RetryFlow_AsyncRetryResponse_ReturnsNewOperationId()
    {
        // Arrange
        var request = CreateTestRequest();
        var failedOperation = CreateOperation(stage: OperationStage.Failed, errorMessage: "error");
        var newInProgress = CreateOperation(
            operationId: "op-new-async",
            stage: OperationStage.EncryptingPerRecipient,
            percentComplete: 25,
            recipientCount: 4,
            processedRecipients: 1);

        _operationServiceMock
            .Setup(s => s.GetStatusAsync("op-retry-async", It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedOperation);

        _operationServiceMock
            .Setup(s => s.GetStatusAsync("op-new-async", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newInProgress);

        var asyncResult = new ActionSubmissionResultViewModel
        {
            TransactionId = "tx-2",
            InstanceId = "inst-2",
            IsAsync = true,
            OperationId = "op-new-async"
        };
        asyncResult.HasAsyncOperation.Should().BeTrue("result must indicate async operation");

        _workflowServiceMock
            .Setup(s => s.SubmitActionExecuteAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(asyncResult);

        var cut = Render<EncryptionProgressIndicator>(parameters => parameters
            .Add(p => p.OperationId, "op-retry-async")
            .Add(p => p.OriginalRequest, request)
            .Add(p => p.WorkflowService, _workflowServiceMock.Object));

        // Act — trigger retry
        var retryButton = cut.Find("button");
        await cut.InvokeAsync(() => retryButton.Click());

        // Assert — component now polls the new operation ID, shows new progress
        _operationServiceMock.Verify(
            s => s.GetStatusAsync("op-new-async", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        cut.Markup.Should().Contain("1 of 4 recipients processed");
    }

    [Fact]
    public async Task RetryFlow_SyncRetryResponse_CompletesImmediately()
    {
        // Arrange
        var request = CreateTestRequest();
        var failedOperation = CreateOperation(stage: OperationStage.Failed, errorMessage: "error");
        var completedCalled = false;

        _operationServiceMock
            .Setup(s => s.GetStatusAsync("op-retry-sync", It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedOperation);

        var syncResult = new ActionSubmissionResultViewModel
        {
            TransactionId = "tx-sync",
            InstanceId = "inst-3",
            IsAsync = false,
            OperationId = null
        };
        syncResult.HasAsyncOperation.Should().BeFalse("sync result should not have async operation");

        _workflowServiceMock
            .Setup(s => s.SubmitActionExecuteAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncResult);

        var cut = Render<EncryptionProgressIndicator>(parameters => parameters
            .Add(p => p.OperationId, "op-retry-sync")
            .Add(p => p.OriginalRequest, request)
            .Add(p => p.WorkflowService, _workflowServiceMock.Object)
            .Add(p => p.OnCompleted, () => { completedCalled = true; }));

        // Act — trigger retry
        var retryButton = cut.Find("button");
        await cut.InvokeAsync(() => retryButton.Click());

        // Assert — sync response triggers OnCompleted immediately
        completedCalled.Should().BeTrue("sync retry should invoke OnCompleted callback");
    }

    // ---------------------------------------------------------------
    // Model-level tests
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(OperationStage.Complete, true, true)]
    [InlineData(OperationStage.Failed, true, false)]
    [InlineData(OperationStage.Queued, false, false)]
    [InlineData(OperationStage.EncryptingPerRecipient, false, false)]
    public void EncryptionOperationViewModel_StageProperties_MatchExpected(
        string stage, bool expectedIsComplete, bool expectedIsSuccess)
    {
        var vm = new EncryptionOperationViewModel { Stage = stage };

        vm.IsComplete.Should().Be(expectedIsComplete);
        vm.IsSuccess.Should().Be(expectedIsSuccess);
    }

    [Fact]
    public void ActionSubmissionResultViewModel_HasAsyncOperation_TrueWhenAsyncWithOperationId()
    {
        var result = new ActionSubmissionResultViewModel
        {
            IsAsync = true,
            OperationId = "op-test"
        };

        result.HasAsyncOperation.Should().BeTrue();
    }

    [Fact]
    public void ActionSubmissionResultViewModel_HasAsyncOperation_FalseWhenSyncOrMissingId()
    {
        var syncResult = new ActionSubmissionResultViewModel { IsAsync = false, OperationId = "op" };
        syncResult.HasAsyncOperation.Should().BeFalse();

        var missingId = new ActionSubmissionResultViewModel { IsAsync = true, OperationId = null };
        missingId.HasAsyncOperation.Should().BeFalse();

        var emptyId = new ActionSubmissionResultViewModel { IsAsync = true, OperationId = "" };
        emptyId.HasAsyncOperation.Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static EncryptionOperationViewModel CreateOperation(
        string operationId = "op-test",
        string stage = OperationStage.Queued,
        int percentComplete = 0,
        int recipientCount = 3,
        int processedRecipients = 0,
        string? errorMessage = null,
        string? transactionHash = null)
    {
        return new EncryptionOperationViewModel
        {
            OperationId = operationId,
            Stage = stage,
            PercentComplete = percentComplete,
            RecipientCount = recipientCount,
            ProcessedRecipients = processedRecipients,
            ErrorMessage = errorMessage,
            TransactionHash = transactionHash,
            BlueprintId = "bp-001",
            ActionTitle = "Submit Data",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = stage is OperationStage.Complete or OperationStage.Failed
                ? DateTimeOffset.UtcNow
                : null
        };
    }

    private static ActionExecuteRequest CreateTestRequest()
    {
        return new ActionExecuteRequest
        {
            BlueprintId = "bp-001",
            ActionId = "action-1",
            InstanceId = "inst-1",
            SenderWallet = "wallet-abc",
            RegisterAddress = "register-xyz",
            PayloadData = new Dictionary<string, object>
            {
                ["field1"] = "value1",
                ["field2"] = 42
            }
        };
    }
}
