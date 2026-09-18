// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Participant;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.McpServer.Tests.Tools.Participant;

/// <summary>
/// #1658 — <c>sorcha_action_submit</c> posted the agent's data as the whole request body, so every
/// submission was a 400 that surfaced as a bare "Action submission failed.". The tool now reads the
/// action's submission context (blueprint id, register id, which of the caller's wallets to send), builds
/// the real <see cref="ExecuteActionRequest"/>, and passes the endpoint's own reason through on a refusal.
/// These tests mock the client, so the bytes on the wire are guarded separately:
/// <c>BlueprintServiceClientExecuteActionTests</c> and <c>ActionSubmissionRequestWireContractTests</c>.
/// </summary>
public sealed class ActionSubmitToolTests
{
    private const string Mine = "ws1qmine";
    private const string AlsoMine = "ws1qalso";

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

    private static string ContextBody(
        string status, string? senderWallet = null, string[]? candidates = null, string? unboundParticipantId = null) =>
        JsonSerializer.Serialize(new
        {
            actionId = 2,
            title = "Review",
            submission = new
            {
                blueprintId = "bp-1",
                registerId = "reg-1",
                senderWalletStatus = status,
                senderWallet,
                candidateWallets = candidates ?? [],
                unboundParticipantId,
            },
        });

    private void ContextReturns(HttpStatusCode status, string body) =>
        _blueprintClientMock
            .Setup(c => c.GetActionForSubmissionAsync("wf-1", "2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((status, body));

    private void ExecuteReturns(HttpStatusCode status, string body) =>
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync("wf-1", "2", It.IsAny<ExecuteActionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((status, body));

    private void VerifyNeverSubmitted() =>
        _blueprintClientMock.Verify(
            c => c.ExecuteActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ExecuteActionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private const string AcceptedBody = """{"transactionId":"tx-1","instanceId":"wf-1","isComplete":false,"nextActions":[],"isAsync":true}""";

    // ---- input and gate ----

    [Fact]
    public async Task SubmitActionAsync_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(false);

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Unauthorized");
    }

    [Theory]
    [InlineData("", "2", "{}")]
    [InlineData("wf-1", "", "{}")]
    [InlineData("wf-1", "2", "")]
    [InlineData("wf-1", "2", "not valid json {")]
    [InlineData("wf-1", "2", "[1,2]")]
    public async Task SubmitActionAsync_WithInvalidInput_ReturnsErrorAndCallsNothing(string instanceId, string actionId, string data)
    {
        Allow();

        var result = await _tool.SubmitActionAsync(instanceId, actionId, data);

        result.Status.Should().Be("Error");
        _blueprintClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SubmitActionAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await _tool.SubmitActionAsync("wf-1", "2", """{"name":"test"}""");

        result.Status.Should().Be("Unavailable");
    }

    // ---- building the real request ----

    [Fact]
    public async Task SubmitActionAsync_BuildsTheExecuteRequestFromTheSubmissionContext()
    {
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("resolved", Mine));
        ExecuteActionRequest? sent = null;
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync("wf-1", "2", It.IsAny<ExecuteActionRequest>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, ExecuteActionRequest r, CancellationToken _) => sent = r)
            .ReturnsAsync((HttpStatusCode.OK, AcceptedBody));

        var result = await _tool.SubmitActionAsync("wf-1", "2", """{"decision":"approved","amount":1200}""");

        result.Status.Should().Be("Success");
        result.TransactionId.Should().Be("tx-1");
        sent.Should().NotBeNull();
        sent!.BlueprintId.Should().Be("bp-1");
        sent.RegisterAddress.Should().Be("reg-1");
        sent.SenderWallet.Should().Be(Mine);
        sent.InstanceId.Should().Be("wf-1");
        sent.ActionId.Should().Be("2");
        sent.PayloadData.GetProperty("decision").GetString().Should().Be("approved");
        sent.PayloadData.GetProperty("amount").GetInt32().Should().Be(1200);
    }

    [Fact]
    public async Task SubmitActionAsync_ExplicitSenderWallet_IsSentEvenWhenTheServerCannotChoose()
    {
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("ambiguous", candidates: [Mine, AlsoMine]));
        ExecuteActionRequest? sent = null;
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync("wf-1", "2", It.IsAny<ExecuteActionRequest>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, ExecuteActionRequest r, CancellationToken _) => sent = r)
            .ReturnsAsync((HttpStatusCode.OK, AcceptedBody));

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}", senderWallet: AlsoMine);

        result.Status.Should().Be("Success");
        sent!.SenderWallet.Should().Be(AlsoMine);
    }

    // ---- when no wallet can be chosen, nothing is submitted ----

    [Fact]
    public async Task SubmitActionAsync_Ambiguous_DoesNotSubmit_AndNamesTheCandidates()
    {
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("ambiguous", candidates: [Mine, AlsoMine]));

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain(Mine).And.Contain(AlsoMine).And.Contain("senderWallet");
        VerifyNeverSubmitted();
    }

    [Fact]
    public async Task SubmitActionAsync_NotYours_IsRefused_AndDoesNotSubmit()
    {
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("notYours"));

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("bound to a wallet you do not hold");
        VerifyNeverSubmitted();
    }

    [Fact]
    public async Task SubmitActionAsync_AwaitingParticipantRecord_IsRefused_AndNamesTheRoleAndRemedy()
    {
        // #1664: submitting anyway is accepted with a 202 and then refused by the validator with
        // VAL_BP_002, which reaches no audit log. Cold-start run #4 retried that blind, twice.
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("awaitingParticipantRecord", unboundParticipantId: "provider"));

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("provider").And.Contain("participant record").And.Contain("reg-1");
        result.Message.Should().Contain("Nobody can submit this action");
        VerifyNeverSubmitted();
    }

    [Fact]
    public async Task SubmitActionAsync_AwaitingParticipantRecord_ExplicitSenderWalletStillSubmits()
    {
        // The override stays available: the server and validator remain the authority, and a caller who
        // knows better (a record published moments ago) must not be blocked by the tool's own advice.
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("awaitingParticipantRecord", unboundParticipantId: "provider"));
        ExecuteReturns(HttpStatusCode.OK, AcceptedBody);

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}", senderWallet: Mine);

        result.Status.Should().Be("Success");
    }

    [Fact]
    public async Task SubmitActionAsync_NoWallet_DoesNotSubmit()
    {
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("noWallet"));

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("no wallet");
        VerifyNeverSubmitted();
    }

    [Fact]
    public async Task SubmitActionAsync_ContextForbidden_IsRefusedWithTheServersReason()
    {
        Allow();
        ContextReturns(HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403,"detail":"You are not a participant on this instance."}""");

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("You are not a participant on this instance.");
        VerifyNeverSubmitted();
    }

    [Fact]
    public async Task SubmitActionAsync_ContextNotFound_PassesTheServersReasonThrough()
    {
        Allow();
        ContextReturns(HttpStatusCode.NotFound, """{"error":"Instance not found"}""");

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Instance not found").And.Contain("404");
        VerifyNeverSubmitted();
    }

    [Fact]
    public async Task SubmitActionAsync_ContextWithoutSubmissionBlock_SaysTheServiceIsTooOld()
    {
        Allow();
        ContextReturns(HttpStatusCode.OK, """{"actionId":2,"title":"Review"}""");

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("does not report a submission context");
        VerifyNeverSubmitted();
    }

    // ---- the endpoint's reason reaches the agent ----

    [Fact]
    public async Task SubmitActionAsync_ExecuteBadRequest_PassesTheServersErrorThrough()
    {
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("resolved", Mine));
        ExecuteReturns(HttpStatusCode.BadRequest, """{"error":"Action 2 is not a current action for instance wf-1"}""");

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Action 2 is not a current action for instance wf-1").And.Contain("400");
        result.Message.Should().NotBe("Action submission failed.");
        result.Message.Should().NotContain("{", "the reason is read out of the body, not pasted in as raw JSON");
    }

    [Fact]
    public async Task SubmitActionAsync_ExecuteForbidden_IsRefusedWithTheServersDetail()
    {
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("resolved", Mine));
        ExecuteReturns(HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403,"detail":"No participant profile linked to authenticated user."}""");

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("No participant profile linked to authenticated user.")
            .And.NotContain("\"title\"", "problem+json detail is extracted, not the whole document");
    }

    [Fact]
    public async Task SubmitActionAsync_ExecuteValidationProblem_ReportsEachFieldError()
    {
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("resolved", Mine));
        ExecuteReturns(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"SenderWallet":["The SenderWallet field is required."],"PayloadData":["The PayloadData field is required."]}}""");

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Error");
        result.ValidationErrors.Should().Contain("SenderWallet: The SenderWallet field is required.")
            .And.Contain("PayloadData: The PayloadData field is required.");
    }

    [Fact]
    public async Task SubmitActionAsync_AHttpRefusal_IsNotCountedAsTheServiceBeingDown()
    {
        // A refusal is deterministic. Counting it as an outage trips the breaker and disables every
        // Blueprint tool behind a false "service unavailable" (found in P1; see the design doc).
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("resolved", Mine));
        ExecuteReturns(HttpStatusCode.BadRequest, """{"error":"nope"}""");

        await _tool.SubmitActionAsync("wf-1", "2", "{}");

        _availabilityTrackerMock.Verify(x => x.RecordFailure("Blueprint", It.IsAny<Exception?>()), Times.Never);
    }

    // ---- success messages ----

    [Fact]
    public async Task SubmitActionAsync_WithRealEndpointShape_MessageExplainsAsyncProcessing()
    {
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("resolved", Mine));
        ExecuteReturns(HttpStatusCode.OK, AcceptedBody);

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Success");
        result.Message.Should().Contain("asynchronous").And.Contain("sorcha_workflow_status");
        result.NextActions.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitActionAsync_AwaitingPresentation_SaysACredentialPresentationIsNeeded()
    {
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("resolved", Mine));
        ExecuteReturns(HttpStatusCode.Accepted,
            """{"transactionId":"","instanceId":"wf-1","isComplete":false,"nextActions":[],"awaitingPresentation":true,"presentationRequest":{"requestId":"pr-1"}}""");

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Success");
        result.Message.Should().Contain("credential presentation").And.Contain("pr-1");
    }

    [Fact]
    public void BuildSuccessMessage_WhenWorkflowComplete_SaysComplete()
    {
        // Feature 145 hard-codes isComplete=false and nextActions=[] on every live execute response, so
        // these branches model a shape the endpoint cannot produce today; they guard the shared
        // NextActionResponse wire mapping (actionTitle / participantId) for other consumers.
        var parsed = ActionSubmitTool.ParseSubmitResponse("""{"transactionId":"tx","isComplete":true,"nextActions":[]}""");

        ActionSubmitTool.BuildSuccessMessage(parsed).Should().Contain("Workflow is complete");
    }

    [Fact]
    public void ParseSubmitResponse_ReadsNextActionWireNames()
    {
        var parsed = ActionSubmitTool.ParseSubmitResponse(
            """{"transactionId":"tx","isComplete":false,"nextActions":[{"actionId":3,"actionTitle":"Approve","participantId":"reviewer"}]}""");

        parsed!.NextActions![0].ActionTitle.Should().Be("Approve");
        parsed.NextActions[0].ParticipantId.Should().Be("reviewer");
        ActionSubmitTool.BuildSuccessMessage(parsed).Should().Contain("'Approve'");
    }

    // ---- transport failures ----

    [Fact]
    public async Task SubmitActionAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetActionForSubmissionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Timeout");
    }

    [Fact]
    public async Task SubmitActionAsync_WithHttpException_ReturnsErrorAndRecordsFailure()
    {
        Allow();
        ContextReturns(HttpStatusCode.OK, ContextBody("resolved", Mine));
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ExecuteActionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _tool.SubmitActionAsync("wf-1", "2", "{}");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Connection refused");
        _availabilityTrackerMock.Verify(x => x.RecordFailure("Blueprint", It.IsAny<Exception?>()), Times.Once);
    }
}
