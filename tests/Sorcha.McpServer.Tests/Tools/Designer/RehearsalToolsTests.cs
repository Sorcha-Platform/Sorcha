// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Designer;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.McpServer.Tests.Tools.Designer;

/// <summary>
/// #1691 — the rehearsal tools. Until they existed nothing on the MCP surface could record a
/// <c>RehearsalPass</c>, so every MCP publish needed a human override. These tests pin what an
/// agent is told at each state, because an agent acts on the message: a pass must say the publish
/// gate is cleared, a failure must carry the rehearsal log's reason, and a refusal must carry the
/// server's.
/// </summary>
public class RehearsalToolsTests
{
    private const string BlueprintId = "bp-1";
    private static readonly Guid RehearsalId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly Mock<IMcpAuthorizationService> _auth = new();
    private readonly Mock<IServiceAvailabilityTracker> _availability = new();
    private readonly Mock<IBlueprintServiceClient> _client = new();

    private RehearsalStartTool Start() => new(_auth.Object, _availability.Object, _client.Object, Mock.Of<ILogger<RehearsalStartTool>>());

    private RehearsalStepTool Step() => new(_auth.Object, _availability.Object, _client.Object, Mock.Of<ILogger<RehearsalStepTool>>());

    private RehearsalGetTool Get() => new(_auth.Object, _availability.Object, _client.Object, Mock.Of<ILogger<RehearsalGetTool>>());

    private void Allow(string tool)
    {
        _auth.Setup(a => a.CanInvokeTool(tool)).Returns(true);
        _availability.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    private static Rehearsal Rehearsal(
        RehearsalOutcome outcome,
        IReadOnlyList<RehearsalStep> steps,
        params RehearsalEvent[] log) => new()
        {
            RehearsalId = RehearsalId,
            BlueprintId = BlueprintId,
            ExecDefHash = "hash-abc",
            Mode = RehearsalMode.Full,
            SandboxRegisterId = "sandbox-1",
            CurrentActingRole = steps.FirstOrDefault(s => s.Status == RehearsalStepStatus.Current)?.ActingRole ?? string.Empty,
            Outcome = outcome,
            Steps = steps,
            Log = log,
        };

    private static RehearsalStep S(int id, string role, RehearsalStepStatus status) =>
        new() { ActionId = id, ActingRole = role, Status = status };

    private static RehearsalEvent E(RehearsalEventKind kind, string message) =>
        new() { At = DateTimeOffset.UtcNow, Kind = kind, Message = message };

    private static RehearsalCallResult Ok(Rehearsal r) => new() { Rehearsal = r };

    private static RehearsalCallResult Refused(int status, string? reason, params string[] errors) =>
        new() { Refusal = new RehearsalRefusal(status, reason, errors) };

    // ── start ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_Unauthorized_DoesNotCallTheService()
    {
        _auth.Setup(a => a.CanInvokeTool("sorcha_rehearsal_start")).Returns(false);

        var result = await Start().StartRehearsalAsync(BlueprintId);

        result.Status.Should().Be("Unauthorized");
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Start_MissingBlueprintId_IsAValidationError()
    {
        Allow("sorcha_rehearsal_start");

        var result = await Start().StartRehearsalAsync(" ");

        result.Status.Should().Be("ValidationError");
        result.ValidationErrors.Should().Contain("blueprintId is required");
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Start_InProgress_NamesTheFirstActionAndTheToolToSubmitItWith()
    {
        Allow("sorcha_rehearsal_start");
        _client.Setup(c => c.StartRehearsalAsync(BlueprintId, It.Is<StartRehearsalRequest>(r => r.Mode == RehearsalMode.Full), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(Rehearsal(RehearsalOutcome.InProgress,
                [S(1, "applicant", RehearsalStepStatus.Current), S(2, "officer", RehearsalStepStatus.Pending)])));

        var result = await Start().StartRehearsalAsync(BlueprintId);

        result.Status.Should().Be("InProgress");
        result.RehearsalId.Should().Be(RehearsalId);
        result.ExecDefHash.Should().Be("hash-abc");
        result.CurrentActionId.Should().Be(1);
        result.CurrentActingRole.Should().Be("applicant");
        result.Steps.Should().HaveCount(2);
        result.Message.Should().Contain("Submit action 1").And.Contain("sorcha_rehearsal_step").And.Contain("'applicant'");
        _availability.Verify(a => a.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task Start_BlockingValidationErrors_AreListedAndDoNotCountAsAnOutage()
    {
        Allow("sorcha_rehearsal_start");
        _client.Setup(c => c.StartRehearsalAsync(BlueprintId, It.IsAny<StartRehearsalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Refused(409, "Blueprint has blocking validation errors.", "Action 2 has no sender"));

        var result = await Start().StartRehearsalAsync(BlueprintId);

        result.Status.Should().Be("ValidationError");
        result.ValidationErrors.Should().Equal("Action 2 has no sender");
        result.Message.Should().Contain("Blueprint has blocking validation errors").And.Contain("sorcha_blueprint_update");

        // A refusal is a deterministic answer from a live service. Counting it as a failure is how
        // three governance 403s once disabled every Blueprint tool behind a false "unavailable".
        _availability.Verify(a => a.RecordSuccess("Blueprint"), Times.Once);
        _availability.Verify(a => a.RecordFailure(It.IsAny<string>(), It.IsAny<Exception?>()), Times.Never);
    }

    [Fact]
    public async Task Start_Forbidden_SaysWhatAuthorityIsNeeded()
    {
        Allow("sorcha_rehearsal_start");
        _client.Setup(c => c.StartRehearsalAsync(BlueprintId, It.IsAny<StartRehearsalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Refused(403, null));

        var result = await Start().StartRehearsalAsync(BlueprintId);

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("403").And.Contain("organisation context");
    }

    [Fact]
    public async Task Start_TransportFault_IsRecordedAsAnOutage()
    {
        Allow("sorcha_rehearsal_start");
        _client.Setup(c => c.StartRehearsalAsync(BlueprintId, It.IsAny<StartRehearsalRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var result = await Start().StartRehearsalAsync(BlueprintId);

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("connection refused");
        _availability.Verify(a => a.RecordFailure("Blueprint", It.IsAny<Exception?>()), Times.Once);
    }

    // ── step ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("not-a-guid", "1", "{}", "rehearsalId must be the GUID")]
    [InlineData("11111111-2222-3333-4444-555555555555", "one", "{}", "actionId must be the current step's integer action id")]
    [InlineData("11111111-2222-3333-4444-555555555555", "1", "", "payloadJson is required")]
    [InlineData("11111111-2222-3333-4444-555555555555", "1", "[1]", "payloadJson must be a JSON object")]
    [InlineData("11111111-2222-3333-4444-555555555555", "1", "{oops", "payloadJson is not valid JSON")]
    public async Task Step_BadInput_IsRefusedLocally_AndSpendsNoRehearsalStep(
        string rehearsalId, string actionId, string payloadJson, string expected)
    {
        // A step that reaches the server and fails ENDS the rehearsal, so input the tool can check
        // itself must never be sent.
        Allow("sorcha_rehearsal_step");

        var result = await Step().SubmitRehearsalStepAsync(BlueprintId, rehearsalId, actionId, payloadJson);

        result.Status.Should().Be("ValidationError");
        result.ValidationErrors.Should().Contain(e => e.Contains(expected));
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Step_PassesThePayloadThroughUnchanged()
    {
        Allow("sorcha_rehearsal_step");
        const string payload = """{"decision":"approved","score":3}""";
        _client.Setup(c => c.SubmitRehearsalStepAsync(BlueprintId, RehearsalId, It.IsAny<SubmitRehearsalStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(Rehearsal(RehearsalOutcome.InProgress,
                [S(1, "applicant", RehearsalStepStatus.Done), S(2, "officer", RehearsalStepStatus.Current)])));

        await Step().SubmitRehearsalStepAsync(BlueprintId, RehearsalId.ToString(), "1", payload);

        _client.Verify(c => c.SubmitRehearsalStepAsync(
            BlueprintId, RehearsalId,
            It.Is<SubmitRehearsalStepRequest>(r => r.ActionId == 1 && r.PayloadJson == payload),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Step_Advanced_SaysItWasAppliedAndNamesTheNextAction()
    {
        Allow("sorcha_rehearsal_step");
        _client.Setup(c => c.SubmitRehearsalStepAsync(BlueprintId, RehearsalId, It.IsAny<SubmitRehearsalStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(Rehearsal(RehearsalOutcome.InProgress,
                [S(1, "applicant", RehearsalStepStatus.Done), S(2, "officer", RehearsalStepStatus.Current)],
                E(RehearsalEventKind.Routed, "Routed to action 2 (officer)."))));

        var result = await Step().SubmitRehearsalStepAsync(BlueprintId, RehearsalId.ToString(), "1", "{}");

        result.Status.Should().Be("InProgress");
        result.CurrentActionId.Should().Be(2);
        result.CurrentActingRole.Should().Be("officer");
        result.Message.Should().StartWith("Action 1 was applied").And.Contain("Submit action 2");
    }

    [Fact]
    public async Task Step_Passed_SaysThePublishGateIsCleared()
    {
        Allow("sorcha_rehearsal_step");
        _client.Setup(c => c.SubmitRehearsalStepAsync(BlueprintId, RehearsalId, It.IsAny<SubmitRehearsalStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(Rehearsal(RehearsalOutcome.Passed,
                [S(1, "applicant", RehearsalStepStatus.Done), S(2, "officer", RehearsalStepStatus.Done)],
                E(RehearsalEventKind.Gate, "Rehearsal passed — this version is cleared for Go live."))));

        var result = await Step().SubmitRehearsalStepAsync(BlueprintId, RehearsalId.ToString(), "2", "{}");

        result.Status.Should().Be("Passed");
        result.CurrentActionId.Should().BeNull();
        result.Message.Should().Contain("PASSED").And.Contain("hash-abc").And.Contain("sorcha_blueprint_publish")
            .And.Contain("no person has to waive it");
    }

    [Fact]
    public async Task Step_Failed_CarriesTheReasonFromTheRehearsalLog()
    {
        // A step that ran and failed is a 200 from the server — the reason lives only in the log.
        Allow("sorcha_rehearsal_step");
        _client.Setup(c => c.SubmitRehearsalStepAsync(BlueprintId, RehearsalId, It.IsAny<SubmitRehearsalStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(Rehearsal(RehearsalOutcome.Failed,
                [S(1, "applicant", RehearsalStepStatus.Current)],
                E(RehearsalEventKind.Info, "Sandbox provisioned."),
                E(RehearsalEventKind.Error, "Step 'applicant' failed: VAL_SCHEMA_004 required property 'name' missing"),
                E(RehearsalEventKind.Info, "later informational entry"))));

        var result = await Step().SubmitRehearsalStepAsync(BlueprintId, RehearsalId.ToString(), "1", "{}");

        result.Status.Should().Be("Failed");
        result.Message.Should().Contain("VAL_SCHEMA_004 required property 'name' missing")
            .And.Contain("no rehearsal pass was recorded")
            .And.Contain("sorcha_rehearsal_start");
    }

    [Fact]
    public async Task Step_AcceptedButNotAdvanced_SaysSoAndQuotesTheLatestLogEntry()
    {
        // e.g. an action gated on a credential presentation: the server leaves it current.
        Allow("sorcha_rehearsal_step");
        _client.Setup(c => c.SubmitRehearsalStepAsync(BlueprintId, RehearsalId, It.IsAny<SubmitRehearsalStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(Rehearsal(RehearsalOutcome.InProgress,
                [S(1, "applicant", RehearsalStepStatus.Current)],
                E(RehearsalEventKind.Gate, "Action 1 is awaiting a credential presentation."))));

        var result = await Step().SubmitRehearsalStepAsync(BlueprintId, RehearsalId.ToString(), "1", "{}");

        result.Status.Should().Be("InProgress");
        result.Message.Should().Contain("did not advance").And.Contain("awaiting a credential presentation");
        result.Message.Should().NotContain("was applied");
    }

    [Fact]
    public async Task Step_NotTheCurrentStep_CarriesTheServersReason()
    {
        Allow("sorcha_rehearsal_step");
        _client.Setup(c => c.SubmitRehearsalStepAsync(BlueprintId, RehearsalId, It.IsAny<SubmitRehearsalStepRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Refused(422, "Action 3 is not the current rehearsal step."));

        var result = await Step().SubmitRehearsalStepAsync(BlueprintId, RehearsalId.ToString(), "3", "{}");

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("Action 3 is not the current rehearsal step.");
    }

    [Fact]
    public async Task Step_Timeout_SaysTheOutcomeIsUnknownAndPointsAtGet()
    {
        // The step may still seal after the client gives up; a blind resubmit would then be
        // refused as not-current, or worse, be read as a failure.
        Allow("sorcha_rehearsal_step");
        _client.Setup(c => c.SubmitRehearsalStepAsync(BlueprintId, RehearsalId, It.IsAny<SubmitRehearsalStepRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await Step().SubmitRehearsalStepAsync(BlueprintId, RehearsalId.ToString(), "1", "{}");

        result.Status.Should().Be("Timeout");
        result.Message.Should().Contain("not known whether the step was applied").And.Contain("sorcha_rehearsal_get");
    }

    // ── get ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Unknown_ExplainsThatRehearsalsDoNotSurviveARestart()
    {
        Allow("sorcha_rehearsal_get");
        _client.Setup(c => c.GetRehearsalAsync(BlueprintId, RehearsalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Refused(404, null));

        var result = await Get().GetRehearsalAsync(BlueprintId, RehearsalId.ToString());

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("404").And.Contain("restart");
    }

    [Fact]
    public async Task Get_ReturnsTheSameMappingAsAStep()
    {
        Allow("sorcha_rehearsal_get");
        _client.Setup(c => c.GetRehearsalAsync(BlueprintId, RehearsalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(Rehearsal(RehearsalOutcome.InProgress,
                [S(1, "applicant", RehearsalStepStatus.Done), S(2, "officer", RehearsalStepStatus.Current)])));

        var result = await Get().GetRehearsalAsync(BlueprintId, RehearsalId.ToString());

        result.Status.Should().Be("InProgress");
        result.CurrentActionId.Should().Be(2);
        // No step was submitted by this call, so it must not claim one was applied.
        result.Message.Should().StartWith("Submit action 2");
    }
}
