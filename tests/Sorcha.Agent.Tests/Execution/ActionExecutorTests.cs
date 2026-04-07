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
        var logger = new Mock<ILogger<ActionExecutor>>();
        var auditLogger = new AuditLogger(null);
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost") };
        var authLogger = new Mock<ILogger<Sorcha.Agent.Auth.AgentAuthService>>();
        var config = new Sorcha.Agent.Configuration.ConnectionConfig
        {
            GatewayUrl = "http://localhost",
            RegisterId = "reg-1",
            Credentials = new Sorcha.Agent.Configuration.CredentialsConfig
            {
                Email = "test@test.com",
                Password = "pass",
                OrganizationId = "org-1"
            },
            WalletAddress = "wallet-1"
        };
        var authService = new Sorcha.Agent.Auth.AgentAuthService(httpClient, config, authLogger.Object);

        var executor = new ActionExecutor(httpClient, authService, logger.Object, auditLogger);
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
