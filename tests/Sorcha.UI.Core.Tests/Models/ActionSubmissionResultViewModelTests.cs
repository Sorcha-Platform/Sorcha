// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Models.Workflows;
using Xunit;

namespace Sorcha.UI.Core.Tests.Models;

/// <summary>
/// Unit tests for ActionSubmissionResultViewModel including async operation fields.
/// </summary>
public class ActionSubmissionResultViewModelTests
{
    [Fact]
    public void HasAsyncOperation_WhenIsAsyncAndOperationIdPresent_ReturnsTrue()
    {
        var result = new ActionSubmissionResultViewModel
        {
            IsAsync = true,
            OperationId = "op-123"
        };

        result.HasAsyncOperation.Should().BeTrue();
    }

    [Fact]
    public void HasAsyncOperation_WhenIsAsyncButNoOperationId_ReturnsFalse()
    {
        var result = new ActionSubmissionResultViewModel
        {
            IsAsync = true,
            OperationId = null
        };

        result.HasAsyncOperation.Should().BeFalse();
    }

    [Fact]
    public void HasAsyncOperation_WhenIsAsyncButEmptyOperationId_ReturnsFalse()
    {
        var result = new ActionSubmissionResultViewModel
        {
            IsAsync = true,
            OperationId = ""
        };

        result.HasAsyncOperation.Should().BeFalse();
    }

    [Fact]
    public void HasAsyncOperation_WhenNotAsync_ReturnsFalse()
    {
        var result = new ActionSubmissionResultViewModel
        {
            IsAsync = false,
            OperationId = "op-123"
        };

        result.HasAsyncOperation.Should().BeFalse();
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var result = new ActionSubmissionResultViewModel();

        result.TransactionId.Should().BeEmpty();
        result.InstanceId.Should().BeEmpty();
        result.IsComplete.Should().BeFalse();
        result.IsAsync.Should().BeFalse();
        result.OperationId.Should().BeNull();
        result.NextActions.Should().BeNull();
        result.Warnings.Should().BeNull();
        result.HasAsyncOperation.Should().BeFalse();
    }

    [Fact]
    public void SyncResponse_HasExpectedShape()
    {
        var result = new ActionSubmissionResultViewModel
        {
            TransactionId = "a1b2c3d4e5f6",
            InstanceId = "inst-001",
            IsAsync = false,
            OperationId = null,
            NextActions = [new NextActionInfo { ActionId = 2, ActionTitle = "Review" }]
        };

        result.HasAsyncOperation.Should().BeFalse();
        result.TransactionId.Should().NotBeEmpty();
        result.NextActions.Should().HaveCount(1);
    }

    [Fact]
    public void AsyncResponse_HasExpectedShape()
    {
        var result = new ActionSubmissionResultViewModel
        {
            TransactionId = "",
            InstanceId = "inst-001",
            IsAsync = true,
            OperationId = "abc123def456",
            IsComplete = false,
            NextActions = []
        };

        result.HasAsyncOperation.Should().BeTrue();
        result.TransactionId.Should().BeEmpty();
        result.IsComplete.Should().BeFalse();
    }
}
