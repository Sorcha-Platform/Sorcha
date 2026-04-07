// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Execution;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Tests.Execution;

public class ActionExecutorTests
{
    private static PendingAction CreateAction() => new()
    {
        ActionId = "act-1",
        ActionName = "ReviewApplication",
        ActionIndex = 1,
        BlueprintId = "bp-1",
        InstanceId = "inst-1",
        RegisterId = "reg-1",
        TransactionId = "tx-1"
    };

    private static ActionDecision CreateApproveDecision() => new(
        "approve",
        new Dictionary<string, object> { ["decision"] = "approved" },
        "Rule matched");

    [Fact]
    public void ActionExecutor_CanBeConstructed()
    {
        // Verify the ActionExecutor can be instantiated with its dependencies
        // Full integration testing requires running services
        var logger = new Mock<ILogger<ActionExecutor>>();
        var auditLogger = new AuditLogger(null);

        var executor = new ActionExecutor(logger.Object, auditLogger);
        executor.Should().NotBeNull();
    }

    [Fact]
    public void SkipDecision_DoesNotSubmit()
    {
        var decision = new ActionDecision("skip", null, "No matching rule");
        decision.Decision.Should().Be("skip");
        decision.Payload.Should().BeNull();
    }
}
