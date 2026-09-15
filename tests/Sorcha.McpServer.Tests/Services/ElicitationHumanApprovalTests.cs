// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tests.Services;

/// <summary>
/// The approval seam, carried by MCP Multi Round-Trip Requests (MRTR). A client may support
/// elicitation and still answer automatically: Claude Code in non-interactive mode answers
/// <c>{"action":"cancel"}</c> without a person ever seeing the question. So only an explicit
/// <c>accept</c> with the confirm field set approves, and the answer must be for THIS question.
/// </summary>
public class ElicitationHumanApprovalTests
{
    private static readonly HumanApprovalRequest Request =
        new("Create register 'Acme Supply'?\nStorage: encrypted payloads.", "Create the register");

    private readonly ElicitationHumanApproval _sut = new();

    /// <summary>
    /// #1622. On the stateless HTTP transport the SDK leaves <c>ClientCapabilities</c> null for
    /// every client — even one that sent <c>"elicitation":{}</c> in the request's <c>_meta</c>. The
    /// old gate read that null as "cannot elicit" and refused Claude Code permanently.
    /// </summary>
    [Fact]
    public void Evaluate_StatelessTransportLeavesCapabilitiesNull_StillAsksWhenTheClientSupportsMrtr()
    {
        var context = FakeMcpServer.ContextFor(capabilities: null, isMrtrSupported: true);

        var act = () => _sut.Evaluate(context, Request);

        act.Should().Throw<InputRequiredException>(
            "a null ClientCapabilities is what the stateless transport reports for EVERY client");
    }

    [Fact]
    public void Evaluate_ClientCannotCarryMrtr_ReturnsNotSupportedWithoutAsking()
    {
        var context = FakeMcpServer.ContextFor(isMrtrSupported: false);

        var result = _sut.Evaluate(context, Request);

        result.Outcome.Should().Be(ApprovalOutcome.NotSupported);
        result.Detail.Should().Contain("2026-07-28");
    }

    [Fact]
    public void Evaluate_FirstRound_AsksWithTheMessageAndABooleanConfirmSchema()
    {
        var question = AskFirstRound(Request);

        question.Result.InputRequests.Should().ContainKey(ElicitationHumanApproval.InputKey);
        var elicitation = question.Result.InputRequests![ElicitationHumanApproval.InputKey].ElicitationParams;
        elicitation.Should().NotBeNull();
        elicitation!.Message.Should().Be(Request.Message);
        elicitation.RequestedSchema.Should().NotBeNull();
        elicitation.RequestedSchema!.Properties.Should().ContainKey(ElicitationHumanApproval.ConfirmField);
        elicitation.RequestedSchema.Required.Should().Contain(ElicitationHumanApproval.ConfirmField);
        question.Result.RequestState.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Evaluate_PersonAcceptsAndConfirms_ReturnsApproved()
    {
        var result = AnswerSecondRound(asked: Request, answer: Accept(confirm: true));

        result.Outcome.Should().Be(ApprovalOutcome.Approved);
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    public void Evaluate_PersonDeclinesOrClientCancels_ReturnsRefused(string action)
    {
        var result = AnswerSecondRound(asked: Request, answer: new ElicitResult { Action = action });

        result.Outcome.Should().Be(ApprovalOutcome.Refused);
        result.Detail.Should().Contain(action);
    }

    /// <summary>The form says "Anything else cancels" — an accept that leaves the box clear is not a yes.</summary>
    [Fact]
    public void Evaluate_AcceptWithConfirmFalse_ReturnsRefused()
    {
        var result = AnswerSecondRound(asked: Request, answer: Accept(confirm: false));

        result.Outcome.Should().Be(ApprovalOutcome.Refused);
    }

    [Fact]
    public void Evaluate_AcceptWithNoContent_ReturnsRefused()
    {
        var result = AnswerSecondRound(asked: Request, answer: Accept(confirm: null));

        result.Outcome.Should().Be(ApprovalOutcome.Refused);
    }

    /// <summary>
    /// An "accept" sent on the very first call, with no request state, answers a question nobody was
    /// ever asked. It must not be applied.
    /// </summary>
    [Fact]
    public void Evaluate_AcceptWithNoRequestState_IsRefusedNotApplied()
    {
        var context = FakeMcpServer.ContextFor(
            responses: Responses(Accept(confirm: true)),
            requestState: null);

        var result = _sut.Evaluate(context, Request);

        result.Outcome.Should().Be(ApprovalOutcome.Refused);
    }

    /// <summary>
    /// A person who approved an ENCRYPTED register did not approve a PLAINTEXT one. The answer is
    /// bound to exactly what they were shown.
    /// </summary>
    [Fact]
    public void Evaluate_AcceptBoundToADifferentQuestion_IsRefusedNotApplied()
    {
        var plaintext = Request with { Message = "Create register 'Acme Supply'?\nStorage: DEVELOPMENT MODE — payloads stored as PLAINTEXT." };

        var result = AnswerSecondRound(asked: Request, answer: Accept(confirm: true), evaluated: plaintext);

        result.Outcome.Should().Be(ApprovalOutcome.Refused);
    }

    /// <summary>
    /// The pairing that keeps the binding test honest: without it, a state that changed on every
    /// call would make "different question ⇒ refused" pass while refusing the SAME question too.
    /// </summary>
    [Fact]
    public void Evaluate_SameQuestionAskedTwice_BindsToTheSameState()
    {
        AskFirstRound(Request).Result.RequestState
            .Should().Be(AskFirstRound(Request with { }).Result.RequestState);
    }

    /// <summary>
    /// The legacy server-to-client request (<c>McpServer.ElicitAsync</c>) is unavailable on the
    /// stateless transport and throws there. Neither round may touch it.
    /// </summary>
    [Fact]
    public void Evaluate_BothRounds_NeverSendAServerToClientRequest()
    {
        var firstContext = FakeMcpServer.ContextFor();
        var question = FluentActions.Invoking(() => _sut.Evaluate(firstContext, Request))
            .Should().Throw<InputRequiredException>().Which;

        var secondContext = FakeMcpServer.ContextFor(
            responses: Responses(Accept(confirm: true)),
            requestState: question.Result.RequestState);
        _sut.Evaluate(secondContext, Request);

        ((FakeMcpServer)firstContext.Server).SendRequestCount.Should().Be(0);
        ((FakeMcpServer)secondContext.Server).SendRequestCount.Should().Be(0);
    }

    private InputRequiredException AskFirstRound(HumanApprovalRequest request)
    {
        var context = FakeMcpServer.ContextFor();
        return FluentActions.Invoking(() => _sut.Evaluate(context, request))
            .Should().Throw<InputRequiredException>().Which;
    }

    private ApprovalResult AnswerSecondRound(
        HumanApprovalRequest asked, ElicitResult answer, HumanApprovalRequest? evaluated = null)
    {
        var state = AskFirstRound(asked).Result.RequestState;
        var context = FakeMcpServer.ContextFor(responses: Responses(answer), requestState: state);
        return _sut.Evaluate(context, evaluated ?? asked);
    }

    private static Dictionary<string, InputResponse> Responses(ElicitResult answer) =>
        new() { [ElicitationHumanApproval.InputKey] = InputResponse.FromElicitResult(answer) };

    private static ElicitResult Accept(bool? confirm) => new()
    {
        Action = "accept",
        Content = confirm is null
            ? null
            : new Dictionary<string, JsonElement>
            {
                [ElicitationHumanApproval.ConfirmField] =
                    JsonDocument.Parse(confirm.Value ? "true" : "false").RootElement.Clone()
            }
    };
}

/// <summary>
/// Minimal <see cref="ModelContextProtocol.Server.McpServer"/> stand-in for driving the approval
/// seam, plus a factory for the <see cref="RequestContext{TParams}"/> a tool receives.
/// <para>
/// Only <c>ClientCapabilities</c> and <c>IsMrtrSupported</c> carry values. Everything else throws
/// <see cref="NotSupportedException"/> — and <see cref="SendRequestAsync"/> counts, then throws, so a
/// test can prove the seam never falls back to a server-to-client request.
/// </para>
/// </summary>
// Fully qualified: the project namespace Sorcha.McpServer shadows the SDK type (CS0118).
internal sealed class FakeMcpServer : ModelContextProtocol.Server.McpServer
{
#pragma warning disable MCPEXP002 // McpServer's parameterless constructor is marked experimental
                                   // in SDK 2.2.0; this fake deliberately extends it.
    public FakeMcpServer(ClientCapabilities? capabilities = null, bool isMrtrSupported = true)
#pragma warning restore MCPEXP002
    {
        ClientCapabilities = capabilities;
        IsMrtrSupported = isMrtrSupported;
    }

    /// <summary>Builds the request context a tool invocation receives.</summary>
    public static RequestContext<CallToolRequestParams> ContextFor(
        string toolName = "sorcha_test_tool",
        ClientCapabilities? capabilities = null,
        bool isMrtrSupported = true,
        IDictionary<string, InputResponse>? responses = null,
        string? requestState = null)
    {
        var server = new FakeMcpServer(capabilities, isMrtrSupported);
        var parameters = new CallToolRequestParams
        {
            Name = toolName,
            InputResponses = responses,
            RequestState = requestState
        };

        return new RequestContext<CallToolRequestParams>(
            server,
            new JsonRpcRequest { Method = RequestMethods.ToolsCall },
            parameters);
    }

    public override ClientCapabilities? ClientCapabilities { get; }

    public override bool IsMrtrSupported { get; }

    public int SendRequestCount { get; private set; }

    /// <inheritdoc />
    public override Implementation? ClientInfo => throw new NotSupportedException();

    /// <inheritdoc />
    public override McpServerOptions ServerOptions => throw new NotSupportedException();

    /// <inheritdoc />
    public override IServiceProvider? Services => null;

    /// <inheritdoc />
#pragma warning disable CS0672, MCP9005 // Logging is deprecated in the SDK; the abstract member
                                        // still must be overridden, and this fake never uses it.
    public override LoggingLevel? LoggingLevel => throw new NotSupportedException();
#pragma warning restore CS0672, MCP9005

    /// <inheritdoc />
    public override string? SessionId => null;

    /// <inheritdoc />
    public override string? NegotiatedProtocolVersion => "2026-07-28";

    /// <inheritdoc />
    public override Task RunAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public override Task SendMessageAsync(
        JsonRpcMessage message, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public override IAsyncDisposable RegisterNotificationHandler(
        string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public override Task<JsonRpcResponse> SendRequestAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken = default)
    {
        SendRequestCount++;
        throw new InvalidOperationException(
            "Server-to-client requests are unavailable on the stateless HTTP transport; "
            + "use MRTR (InputRequiredException) instead.");
    }
}
