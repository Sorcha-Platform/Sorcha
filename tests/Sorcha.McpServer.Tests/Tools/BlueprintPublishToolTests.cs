// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tests.Services;
using Sorcha.McpServer.Tools.Designer;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Blueprint.Models;

using SdkMcpServer = ModelContextProtocol.Server.McpServer;

namespace Sorcha.McpServer.Tests.Tools;

/// <summary>
/// <c>sorcha_blueprint_publish</c>. The rehearsal soft gate is the ONLY behavioural check a
/// definition gets before it goes live, so the load-bearing cases here are all about who may
/// waive it: never the agent, only a person, and only on an explicit Approved.
/// </summary>
public class BlueprintPublishToolTests
{
    [Fact]
    public async Task PublishBlueprintAsync_FirstAttemptCarriesNoOverride()
    {
        // The agent must never pre-emptively waive the gate. If it did, the 409 would never be
        // raised and nobody would ever be asked.
        var h = new Harness().WithPublishSuccess();

        await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        h.Client.Verify(c => c.PublishBlueprintAsync(
            "bp-1",
            It.Is<PublishBlueprintRequest>(r => r.RegisterId == "reg-1" && r.Override == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishBlueprintAsync_RehearsedVersion_SucceedsWithoutAskingAnybody()
    {
        var h = new Harness().WithPublishSuccess(version: 7);

        var result = await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        result.Status.Should().Be("Success");
        result.Version.Should().Be(7);
        result.PublishedWithoutRehearsal.Should().BeFalse();
        h.Approval.Verify(a => a.RequestAsync(
            It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PublishBlueprintAsync_RehearsalRequiredAndUserApproves_RetriesWithOverride()
    {
        var h = new Harness().WithRehearsalRequiredThenSuccess().WithApproval(ApprovalOutcome.Approved);

        var result = await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        result.Status.Should().Be("Success");
        result.PublishedWithoutRehearsal.Should().BeTrue();
        h.Client.Verify(c => c.PublishBlueprintAsync(
            "bp-1",
            It.Is<PublishBlueprintRequest>(r => r.Override != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishBlueprintAsync_Override_ConfirmsAndCarriesAnAttributableReason()
    {
        // ProceedWithOverride writes an audit row attributed to the caller's PlatformUserId; the
        // reason is the only free text on it, so it must say where the confirmation came from.
        var h = new Harness().WithRehearsalRequiredThenSuccess().WithApproval(ApprovalOutcome.Approved);

        await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        h.CapturedRequests.Should().HaveCount(2);
        var retry = h.CapturedRequests[1];
        retry.Override.Should().NotBeNull();
        retry.Override!.Confirm.Should().BeTrue();
        retry.Override.Reason.Should().NotBeNullOrWhiteSpace();
        retry.Override.Reason.Should().ContainEquivalentOf("MCP");
    }

    [Theory]
    [InlineData(ApprovalOutcome.Refused)]
    [InlineData(ApprovalOutcome.NotSupported)]
    public async Task PublishBlueprintAsync_RehearsalRequiredAndNotApproved_NeverOverrides(ApprovalOutcome outcome)
    {
        var h = new Harness().WithRehearsalRequiredThenSuccess().WithApproval(outcome);

        var result = await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        result.Status.Should().BeOneOf("RehearsalRequired", "ApprovalRequired");
        result.PublishedWithoutRehearsal.Should().BeFalse();
        h.Client.Verify(c => c.PublishBlueprintAsync(
            It.IsAny<string>(),
            It.Is<PublishBlueprintRequest>(r => r.Override != null),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishBlueprintAsync_Refused_IsReportedSeparatelyFromCannotAsk()
    {
        // "A person said no" and "your client cannot ask anybody" need different responses: one
        // is a decision to respect, the other a client limitation to report.
        var refused = new Harness().WithRehearsalRequiredThenSuccess()
            .WithApproval(ApprovalOutcome.Refused, "The user declined.");
        var cannotAsk = new Harness().WithRehearsalRequiredThenSuccess()
            .WithApproval(ApprovalOutcome.NotSupported, "This client does not support elicitation.");

        (await refused.Sut().PublishBlueprintAsync(refused.Server, "bp-1", "reg-1"))
            .Status.Should().Be("RehearsalRequired");
        (await cannotAsk.Sut().PublishBlueprintAsync(cannotAsk.Server, "bp-1", "reg-1"))
            .Status.Should().Be("ApprovalRequired");
    }

    [Fact]
    public async Task PublishBlueprintAsync_RehearsalRequired_TellsThePersonWhatTheyAreWaiving()
    {
        var h = new Harness().WithRehearsalRequiredThenSuccess().WithApproval(ApprovalOutcome.Approved);

        await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        h.Approval.Verify(a => a.RequestAsync(
            It.IsAny<SdkMcpServer>(),
            It.Is<HumanApprovalRequest>(r =>
                r.Message.Contains("not been rehearsed") && r.Message.Contains("bp-1")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishBlueprintAsync_ApprovalPromptNamesTheRegisterAndTheConcreteConsequence()
    {
        // The person is the entire gate. A prompt that does not say what goes unchecked, or where
        // it lands, makes the confirmation ceremonial.
        var h = new Harness().WithRehearsalRequiredThenSuccess().WithApproval(ApprovalOutcome.Approved);

        await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        h.CapturedApprovalRequest.Should().NotBeNull();
        var message = h.CapturedApprovalRequest!.Message;
        message.Should().Contain("reg-1");
        message.Should().ContainEquivalentOf("routing");
        message.Should().ContainEquivalentOf("credential");
        message.Should().ContainEquivalentOf("ledger");
        h.CapturedApprovalRequest.ConfirmTitle.Should().ContainEquivalentOf("without rehears");
    }

    [Fact]
    public async Task PublishBlueprintAsync_NotEntitled_RefusesWithoutCallingTheServiceOrAnybody()
    {
        var h = new Harness().WithPublishSuccess();
        h.Auth.Setup(a => a.CanInvokeTool("sorcha_blueprint_publish")).Returns(false);

        var result = await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        result.Status.Should().Be("Unauthorized");
        h.Client.Verify(c => c.PublishBlueprintAsync(
            It.IsAny<string>(), It.IsAny<PublishBlueprintRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        h.Approval.Verify(a => a.RequestAsync(
            It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("", "reg-1")]
    [InlineData("   ", "reg-1")]
    [InlineData("bp-1", "")]
    [InlineData("bp-1", "   ")]
    public async Task PublishBlueprintAsync_MissingIds_ReturnsValidationErrorWithoutAskingAnybody(
        string blueprintId, string registerId)
    {
        var h = new Harness().WithPublishSuccess();

        var result = await h.Sut().PublishBlueprintAsync(h.Server, blueprintId, registerId);

        result.Status.Should().Be("ValidationError");
        h.Client.Verify(c => c.PublishBlueprintAsync(
            It.IsAny<string>(), It.IsAny<PublishBlueprintRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        h.Approval.Verify(a => a.RequestAsync(
            It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PublishBlueprintAsync_BlueprintServiceUnavailable_ReturnsUnavailableWithoutAskingAnybody()
    {
        var h = new Harness().WithPublishSuccess();
        h.Availability.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        result.Status.Should().Be("Unavailable");
        h.Approval.Verify(a => a.RequestAsync(
            It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PublishBlueprintAsync_FirstAttemptNull_DoesNotAssertACauseItCannotKnow()
    {
        // The typed client collapses 403 (JWT policy OR register governance roster), 404, the 400
        // that a publish-validation failure produces, 5xx and a transport fault into ONE null.
        // Naming any single one of them would be a confident wrong answer.
        var h = new Harness().WithPublishFailure();

        var result = await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        result.Status.Should().Be("Error");
        result.Version.Should().BeNull();
        result.PublishedWithoutRehearsal.Should().BeFalse();
        result.Message.Should().ContainEquivalentOf("cannot tell");
        // It must NOT single out authorisation, which is only one of the possibilities.
        result.Message.Should().NotMatchEquivalentOf("*you are not authorised to publish.*");
        h.Approval.Verify(a => a.RequestAsync(
            It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PublishBlueprintAsync_OverrideRetryNull_RulesOutAuthorisationBecauseTheGateWasReached()
    {
        // PublishGate evaluates governance FIRST and only then the rehearsal soft gate, so a 409
        // proves the caller already holds publish-governance rights on the register. A null on the
        // retry therefore cannot be an authorisation failure — and saying so is the difference
        // between an actionable message and a shrug.
        var h = new Harness().WithRehearsalRequiredThenFailure().WithApproval(ApprovalOutcome.Approved);

        var result = await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        result.Status.Should().Be("Error");
        result.PublishedWithoutRehearsal.Should().BeFalse();
        result.Message.Should().ContainEquivalentOf("not the cause");
        result.Message.Should().ContainEquivalentOf("nothing was published");
    }

    [Fact]
    public async Task PublishBlueprintAsync_ThrowsHttpRequestException_ReportsErrorNotAFalseSuccess()
    {
        var h = new Harness();
        h.Client.Setup(c => c.PublishBlueprintAsync(
                It.IsAny<string>(), It.IsAny<PublishBlueprintRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("publish: ServiceUnavailable"));

        var result = await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        result.Status.Should().Be("Error");
        result.Version.Should().BeNull();
    }

    [Fact]
    public async Task PublishBlueprintAsync_TimesOut_ReportsTimeoutRatherThanAFailure()
    {
        // BlueprintServiceClient catches HttpRequestException only, so a TaskCanceledException
        // propagates out of the typed method and must not surface as an unexplained error.
        var h = new Harness();
        h.Client.Setup(c => c.PublishBlueprintAsync(
                It.IsAny<string>(), It.IsAny<PublishBlueprintRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("timed out"));

        var result = await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        result.Status.Should().Be("Timeout");
    }

    [Fact]
    public async Task PublishBlueprintAsync_Success_MessageDoesNotClaimTheDefinitionIsSealed()
    {
        // Feature 145 / pattern 22: the publication transaction is submitted to the register, not
        // settled by the time this returns.
        var h = new Harness().WithPublishSuccess();

        var result = await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        result.Message.Should().NotContainEquivalentOf("confirmed");
        result.Message.Should().NotContainEquivalentOf("sealed");
    }

    private sealed class Harness
    {
        public Mock<IMcpAuthorizationService> Auth { get; } = new();
        public Mock<IServiceAvailabilityTracker> Availability { get; } = new();
        public Mock<IBlueprintServiceClient> Client { get; } = new();
        public Mock<IHumanApproval> Approval { get; } = new();

        public List<PublishBlueprintRequest> CapturedRequests { get; } = [];
        public HumanApprovalRequest? CapturedApprovalRequest { get; private set; }

        /// <summary>
        /// A real <c>McpServer</c> stand-in rather than null: the tool hands this straight to
        /// <see cref="IHumanApproval"/>, and the SDK type is non-mockable.
        /// </summary>
        public SdkMcpServer Server { get; } = new FakeMcpServer(new ClientCapabilities());

        public Harness()
        {
            Auth.Setup(a => a.CanInvokeTool("sorcha_blueprint_publish")).Returns(true);
            Availability.Setup(a => a.IsServiceAvailable(It.IsAny<string>())).Returns(true);

            Approval.Setup(a => a.RequestAsync(
                    It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SdkMcpServer, HumanApprovalRequest, CancellationToken>(
                    (_, r, _) => CapturedApprovalRequest = r)
                .ReturnsAsync(new ApprovalResult(ApprovalOutcome.Refused, "default: not approved"));
        }

        public Harness WithApproval(ApprovalOutcome outcome, string detail = "resolved by the harness")
        {
            Approval.Setup(a => a.RequestAsync(
                    It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SdkMcpServer, HumanApprovalRequest, CancellationToken>(
                    (_, r, _) => CapturedApprovalRequest = r)
                .ReturnsAsync(new ApprovalResult(outcome, detail));
            return this;
        }

        public Harness WithPublishSuccess(int version = 1)
        {
            SetupSequence(_ => Success(version));
            return this;
        }

        public Harness WithPublishFailure()
        {
            SetupSequence(_ => null);
            return this;
        }

        public Harness WithRehearsalRequiredThenSuccess(int version = 2)
        {
            SetupSequence(request => request.Override is null ? RehearsalRequired() : Success(version, overridden: true));
            return this;
        }

        public Harness WithRehearsalRequiredThenFailure()
        {
            SetupSequence(request => request.Override is null ? RehearsalRequired() : null);
            return this;
        }

        /// <summary>
        /// Drives the mock off the request itself rather than call order, so a tool that sent the
        /// override on the FIRST attempt cannot accidentally satisfy a "then success" setup.
        /// </summary>
        private void SetupSequence(Func<PublishBlueprintRequest, PublishBlueprintOutcome?> respond) =>
            Client.Setup(c => c.PublishBlueprintAsync(
                    It.IsAny<string>(), It.IsAny<PublishBlueprintRequest>(), It.IsAny<CancellationToken>()))
                .Callback<string, PublishBlueprintRequest, CancellationToken>(
                    (_, r, _) => CapturedRequests.Add(r))
                .ReturnsAsync((string _, PublishBlueprintRequest r, CancellationToken _) => respond(r));

        private static PublishBlueprintOutcome Success(int version, bool overridden = false) => new()
        {
            Result = new PublishBlueprintResult
            {
                BlueprintId = "bp-1",
                Version = version,
                RegisterId = "reg-1",
                PublishedAt = DateTimeOffset.UtcNow,
                Overridden = overridden
            }
        };

        private static PublishBlueprintOutcome RehearsalRequired() => new()
        {
            RehearsalRequired = new RehearsalRequiredError
            {
                Code = "REHEARSAL_REQUIRED",
                ExecDefHash = "abc123",
                Message = "This blueprint version has not been rehearsed."
            }
        };

        public BlueprintPublishTool Sut() => new(
            Auth.Object,
            Availability.Object,
            Client.Object,
            Approval.Object,
            NullLogger<BlueprintPublishTool>.Instance);
    }
}
