// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Shared;
using System.Net;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Participant;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Blueprint.Models;

using Xunit;

namespace Sorcha.McpServer.Tests.Tools.Participant;

/// <summary>
/// Cold-start run #5 — an authorisation refusal must not be reported as an absence.
/// </summary>
/// <remarks>
/// <para>
/// The counterparty organisation was refused an instance it is a participant on (403: the wallet
/// its session controls is not the one bound to its role) and the tools answered "Workflow not
/// found." and "Action not found." It concluded, reasonably, that the instance did not exist or
/// predated its own participant record, and spent the rest of the run on that hypothesis.
/// </para>
/// <para>
/// A 403 and a 404 demand opposite responses — "you cannot see this" versus "this is not there" —
/// so collapsing them destroys the only clue the caller had. The same defect class as #1659 and
/// #1641, which is three times in one workstream.
/// </para>
/// </remarks>
public sealed class RefusalIsNotAbsenceTests
{
    private const string InstanceId = "8808a450-5b4d-4e79-b51e-6e0a3b753984";

    private readonly Mock<IMcpAuthorizationService> _auth = new();
    private readonly Mock<IServiceAvailabilityTracker> _availability = new();
    private readonly Mock<IBlueprintServiceClient> _client = new();

    private void Allow(string toolName)
    {
        _auth.Setup(x => x.CanInvokeTool(toolName)).Returns(true);
        _availability.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task WorkflowStatus_WhenForbidden_SaysRefusedAndThatTheInstanceExists()
    {
        Allow("sorcha_workflow_status");
        _client.Setup(c => c.GetWorkflowStatusAsync(InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.Forbidden, null));

        var result = await WorkflowTool().GetWorkflowStatusAsync(InstanceId);

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("not permitted").And.Contain("It exists");
        result.Message.Should().NotContain("not found");
        // The clue that actually resolves it.
        result.Message.Should().Contain("sorcha_participant_list");
    }

    [Fact]
    public async Task WorkflowStatus_WhenGenuinelyMissing_StillSaysSo()
    {
        // The counterfactual: the fix must not turn a real 404 into a refusal.
        Allow("sorcha_workflow_status");
        _client.Setup(c => c.GetWorkflowStatusAsync(InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.NotFound, null));

        var result = await WorkflowTool().GetWorkflowStatusAsync(InstanceId);

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("No instance");
        result.Message.Should().NotContain("not permitted");
    }

    [Fact]
    public async Task WorkflowStatus_WhenTheTokenHasExpired_SaysThatRatherThanMissing()
    {
        // Run #5 lost two hours to an expired token that surfaced as something else entirely.
        Allow("sorcha_workflow_status");
        _client.Setup(c => c.GetWorkflowStatusAsync(InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.Unauthorized, null));

        var result = await WorkflowTool().GetWorkflowStatusAsync(InstanceId);

        result.Message.Should().Contain("expired");
    }

    [Fact]
    public async Task ActionDetails_WhenForbidden_SaysRefusedAndThatTheActionExists()
    {
        Allow("sorcha_action_details");
        _client.Setup(c => c.GetActionDetailsAsync(InstanceId, "2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.Forbidden, null));

        var result = await ActionTool().GetActionDetailsAsync(InstanceId, "2");

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("not permitted").And.Contain("It exists");
        result.Message.Should().NotContain("not found");
    }

    [Fact]
    public async Task DisclosedData_WhenForbidden_IsRefusedNotAnEmptyResult()
    {
        Allow("sorcha_disclosed_data");
        _client.Setup(c => c.GetDisclosedDataAsync(InstanceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.Forbidden, null));

        var result = await DisclosedTool().GetDisclosedDataAsync(InstanceId);

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("not permitted");
    }

    [Fact]
    public async Task DisclosedData_WhenEmpty_SaysItIsScopedToTheWalletThisSessionControls()
    {
        // The other half of run #5's confusion: a 200 with zero fields read as "nothing was
        // disclosed to us", when action 1 HAD been encrypted to that organisation's other wallet.
        Allow("sorcha_disclosed_data");
        _client.Setup(c => c.GetDisclosedDataAsync(InstanceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(
                HttpStatusCode.OK,
                JsonSerializer.Serialize(new { disclosures = Array.Empty<object>() })));

        var result = await DisclosedTool().GetDisclosedDataAsync(InstanceId);

        result.Status.Should().Be("Success");
        result.TotalFields.Should().Be(0);
        result.Message.Should().Contain("wallet this session controls");
        result.Message.Should().Contain("sorcha_participant_list");
    }

    private WorkflowStatusTool WorkflowTool() => new(
        _auth.Object, _availability.Object, _client.Object, Mock.Of<ILogger<WorkflowStatusTool>>());

    private ActionDetailsTool ActionTool() => new(
        _auth.Object, _availability.Object, _client.Object, Mock.Of<ILogger<ActionDetailsTool>>());

    private DisclosedDataTool DisclosedTool() => new(
        _auth.Object, _availability.Object, _client.Object, Mock.Of<ILogger<DisclosedDataTool>>());
}
