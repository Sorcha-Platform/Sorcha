// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Participant;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tests.Tools.Participant;

/// <summary>
/// Spec 139 US4: ActionSubmitTool executes via the typed <see cref="IBlueprintServiceClient.ExecuteActionAsync"/>
/// (real execute route, caller token forwarded). The tool now takes instanceId + actionId (the old
/// single actionInstanceId targeted a non-existent /submit route). These tests mock the client.
/// </summary>
public sealed class ActionSubmitToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();
    private readonly ActionSubmitTool _tool;

    public ActionSubmitToolTests()
    {
        _tool = new ActionSubmitTool(
            _authServiceMock.Object,
            _availabilityTrackerMock.Object,
            _blueprintClientMock.Object,
            Mock.Of<ILogger<ActionSubmitTool>>());
    }

    private void Allow()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task SubmitActionAsync_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(false);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{}");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task SubmitActionAsync_WithEmptyInstanceId_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(true);

        var result = await _tool.SubmitActionAsync("", "1", "{}");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task SubmitActionAsync_WithEmptyActionId_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(true);

        var result = await _tool.SubmitActionAsync("wf-1", "", "{}");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task SubmitActionAsync_WithEmptyData_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(true);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task SubmitActionAsync_WithInvalidJson_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(true);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "not valid json {");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task SubmitActionAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{\"name\":\"test\"}");

        result.Status.Should().Be("Unavailable");
    }

    // NOTE ON THE NEXT TWO TESTS — read before trusting their fixtures as evidence of live
    // behaviour. Post-Feature-145, `ActionExecutionService.ExecuteAsync` hard-codes
    // `IsComplete = false` and `NextActions = []` on every one of its three return paths (see
    // `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`,
    // return sites ~358/~1211/~1411 — one of them logs "returning 202 — instance advances on
    // projection of the sealed docket"). The real routing decision rides on the sealed
    // transaction's metadata for the InstanceProjector to fold, not on this HTTP response; this
    // is also documented in `RehearsalOrchestrationService.cs`'s "F145 reconciliation" comment
    // block. So `sorcha_action_submit` cannot currently receive a non-empty `nextActions` or
    // `isComplete: true` from a live call — these fixtures model a shape THIS ENDPOINT CANNOT
    // PRODUCE TODAY. They are kept because they guard: (a) the `ActionTitle`/`ParticipantId` wire
    // mapping, which is shared, correct infrastructure other consumers rely on (e.g.
    // `Sorcha.UI.Components.User`'s `ActionSubmissionResultViewModel` deserializes the same
    // `ActionSubmissionResponse`/`NextActionResponse` shape), and (b) `BuildSuccessMessage`'s
    // enriched branches, which exist for whatever response shape a future change might produce.
    // Do NOT read a pass here as proof `sorcha_action_submit` returns next-action data today —
    // see `SubmitActionAsync_WithRealEndpointShape_MessageExplainsAsyncProcessing` below for the
    // shape every live call actually takes.
    [Fact]
    public async Task SubmitActionAsync_WithSuccessfulSubmission_ReturnsSuccess()
    {
        Allow();

        // Field names as the Blueprint Service's ActionSubmissionResponse / NextActionResponse
        // actually serialize them (actionTitle / participantId) — NOT title / assignedTo.
        var response = JsonSerializer.Serialize(new
        {
            transactionId = "tx-789",
            instanceId = "wf-1",
            isComplete = false,
            nextActions = new[] { new { actionId = 2, actionTitle = "Next Action", participantId = "participant-2" } }
        });
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync("wf-1", "1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{\"name\":\"John Doe\"}");

        result.Status.Should().Be("Success");
        result.TransactionId.Should().Be("tx-789");
        result.NextActions.Should().HaveCount(1);
        result.NextActions[0].Title.Should().Be("Next Action");
        result.NextActions[0].AssignedTo.Should().Be("participant-2");
        result.Message.Should().Contain("Next Action");
        _availabilityTrackerMock.Verify(x => x.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task SubmitActionAsync_WhenWorkflowComplete_MessageSaysComplete()
    {
        Allow();

        var response = JsonSerializer.Serialize(new
        {
            transactionId = "tx-999",
            instanceId = "wf-1",
            isComplete = true,
            nextActions = Array.Empty<object>()
        });
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync("wf-1", "1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{\"name\":\"John Doe\"}");

        result.Message.Should().Contain("complete");
        result.NextActions.Should().BeEmpty();
    }

    /// <summary>
    /// THE shape every live call through this tool actually receives today: Feature 145's
    /// <c>ActionExecutionService.ExecuteAsync</c> hard-codes <c>isComplete: false</c> and
    /// <c>nextActions: []</c> on every return path. Unlike the two tests above, this fixture is
    /// not hypothetical — it is what production sends — so the message it produces must not
    /// silently look identical to a bare "submitted successfully" that implies nothing further
    /// happens. It must tell the agent submission is asynchronous and name the tool to poll.
    /// </summary>
    [Fact]
    public async Task SubmitActionAsync_WithRealEndpointShape_MessageExplainsAsyncProcessing()
    {
        Allow();

        // The ONLY shape ActionExecutionService.ExecuteAsync can actually send: isComplete=false,
        // nextActions=[] (every one of its three return paths hard-codes both).
        var response = JsonSerializer.Serialize(new
        {
            transactionId = "tx-real",
            instanceId = "wf-1",
            isComplete = false,
            nextActions = Array.Empty<object>()
        });
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync("wf-1", "1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{\"name\":\"John Doe\"}");

        result.Status.Should().Be("Success");
        result.Message.Should().ContainEquivalentOf("asynchronous");
        result.Message.Should().Contain("sorcha_workflow_status");
    }

    [Fact]
    public async Task SubmitActionAsync_ExecutePathUsesInstanceAndActionId()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync("wf-1", "2", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { transactionId = "tx-1" }));

        await _tool.SubmitActionAsync("wf-1", "2", "{\"x\":1}");

        _blueprintClientMock.Verify(
            c => c.ExecuteActionAsync("wf-1", "2", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitActionAsync_WithNull_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{\"data\":\"test\"}");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task SubmitActionAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{\"data\":\"test\"}");

        result.Status.Should().Be("Timeout");
    }

    [Fact]
    public async Task SubmitActionAsync_WithHttpException_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{\"data\":\"test\"}");

        result.Status.Should().Be("Error");
    }
}

/// <summary>
/// Fixture-level tests against the Blueprint Service's real wire shape, taken from
/// <c>src/Services/Sorcha.Blueprint.Service/Models/Responses/ActionSubmissionResponse.cs</c>:
/// <c>ActionSubmissionResponse</c> has no <c>message</c> property, and its nested
/// <c>NextActionResponse</c> serializes <c>actionTitle</c> / <c>participantId</c> — never
/// <c>title</c> / <c>assignedTo</c>. A fixture written with the tool's original (wrong) field
/// names would pass against the tool's own bug; these use the server's real names instead, so
/// they fail against the pre-fix mapping and pass only once the DTO matches the wire.
/// </summary>
/// <remarks>
/// <c>ServerBody</c> below has a non-empty <c>nextActions</c>, which — post-Feature-145 —
/// <c>ActionExecutionService.ExecuteAsync</c> cannot actually send: all three of its return paths
/// hard-code <c>nextActions: []</c> and <c>isComplete: false</c> (see
/// <c>src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs</c>;
/// also documented in <c>RehearsalOrchestrationService.cs</c>'s "F145 reconciliation" comment).
/// These tests are unit tests of the DESERIALIZATION MAPPING and of <c>BuildSuccessMessage</c>'s
/// pure logic in isolation — legitimate because the same <c>NextActionResponse</c> shape is shared
/// wire infrastructure other consumers deserialize (e.g. <c>Sorcha.UI.Components.User</c>'s
/// <c>ActionSubmissionResultViewModel</c>). They are NOT evidence that a live
/// <c>sorcha_action_submit</c> call ever receives a populated <c>nextActions</c> array — it does
/// not, today. See <c>ActionSubmitToolTests.SubmitActionAsync_WithRealEndpointShape_MessageExplainsAsyncProcessing</c>
/// for the shape production actually sends.
/// </remarks>
public sealed class ActionSubmitResponseParsingTests
{
    private const string ServerBody = """
        {
          "transactionId": "tx-789",
          "instanceId": "wf-1",
          "isComplete": false,
          "nextActions": [
            { "actionId": 2, "actionTitle": "Next Action", "participantId": "participant-2" }
          ]
        }
        """;

    [Fact]
    public void Parse_ServerBody_ReadsActionTitleNotTitle()
    {
        var parsed = ActionSubmitTool.ParseSubmitResponse(ServerBody);

        parsed.Should().NotBeNull();
        parsed!.NextActions.Should().ContainSingle();
        parsed.NextActions![0].ActionTitle.Should().Be("Next Action");
    }

    [Fact]
    public void Parse_ServerBody_ReadsParticipantIdNotAssignedTo()
    {
        var parsed = ActionSubmitTool.ParseSubmitResponse(ServerBody);

        parsed.Should().NotBeNull();
        parsed!.NextActions![0].ParticipantId.Should().Be("participant-2");
    }

    [Fact]
    public void BuildSuccessMessage_WithNextAction_NamesItInsteadOfReadingMissingMessage()
    {
        // The server never sends 'message' at all, so the text must come from real fields. NOTE:
        // ActionExecutionService.ExecuteAsync cannot actually populate NextActions post-Feature-145
        // (see class remarks) — this exercises the branch in isolation for whatever response shape
        // might reach it, not a shape sorcha_action_submit returns live today.
        var parsed = ActionSubmitTool.ParseSubmitResponse(ServerBody);

        var message = ActionSubmitTool.BuildSuccessMessage(parsed);

        message.Should().Contain("Next Action");
    }

    [Fact]
    public void BuildSuccessMessage_WithNullResult_ReturnsGenericSuccess()
    {
        var message = ActionSubmitTool.BuildSuccessMessage(null);

        message.Should().Be("Action submitted successfully.");
    }

    [Fact]
    public void BuildSuccessMessage_WithRealEndpointShape_ExplainsAsyncProcessing()
    {
        // isComplete=false + nextActions=[] is the ONLY shape ActionExecutionService.ExecuteAsync
        // can send (see class remarks) — this is the branch every live call actually takes.
        var parsed = ActionSubmitTool.ParseSubmitResponse("""
            { "transactionId": "tx-real", "instanceId": "wf-1", "isComplete": false, "nextActions": [] }
            """);

        var message = ActionSubmitTool.BuildSuccessMessage(parsed);

        message.Should().ContainEquivalentOf("asynchronous");
        message.Should().Contain("sorcha_workflow_status");
    }

    [Fact]
    public void SubmitResponse_HasNoMessageProperty()
    {
        // ActionSubmissionResponse has no "message" property at all — the DTO must not declare
        // one either, since it can only ever deserialize to a misleading always-null value.
        var properties = typeof(ActionSubmitTool.SubmitResponse).GetProperties();

        properties.Should().NotContain(p => p.Name == "Message");
    }
}
