// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tests.Services;

/// <summary>
/// The three client states. A client may DECLARE elicitation and still answer automatically:
/// the spike measured Claude Code 2.1.261 in non-interactive mode returning
/// <c>{"action":"cancel"}</c>. A capability-only check would let a headless agent straight
/// through, so only <c>accept</c> may approve.
/// </summary>
public class ElicitationHumanApprovalTests
{
    private static readonly HumanApprovalRequest Request =
        new("Create register 'Acme Supply'?", "Confirm");

    [Fact]
    public async Task RequestAsync_ClientDidNotDeclareElicitation_ReturnsNotSupported()
    {
        var server = new FakeMcpServer(capabilities: new ClientCapabilities());
        var sut = new ElicitationHumanApproval();

        var result = await sut.RequestAsync(server, Request);

        result.Outcome.Should().Be(ApprovalOutcome.NotSupported);
        server.ElicitCallCount.Should().Be(0, "no person can be asked, so nothing should be sent");
    }

    /// <summary>
    /// MCP elicitation has two independent sub-capabilities, <c>Form</c> (object-schema) and
    /// <c>Url</c> (redirect). The request this seam sends is always Form-mode. A client that
    /// declares <c>elicitation</c> but only <c>Url</c> support must still be treated as
    /// NotSupported — calling <c>ElicitAsync</c> against it throws an unhandled
    /// <see cref="InvalidOperationException"/> inside the SDK rather than returning a result,
    /// which a capability-presence-only check would not catch.
    /// </summary>
    [Fact]
    public async Task RequestAsync_ClientDeclaresElicitationWithoutFormSupport_ReturnsNotSupported()
    {
        var server = new FakeMcpServer(
            capabilities: new ClientCapabilities
            {
                Elicitation = new ElicitationCapability { Url = new UrlElicitationCapability() }
            });
        var sut = new ElicitationHumanApproval();

        var result = await sut.RequestAsync(server, Request);

        result.Outcome.Should().Be(ApprovalOutcome.NotSupported);
        server.ElicitCallCount.Should().Be(0, "form-mode is unsupported, so nothing should be sent");
    }

    [Fact]
    public async Task RequestAsync_UserAccepts_ReturnsApproved()
    {
        var server = new FakeMcpServer(
            capabilities: new ClientCapabilities
            {
                Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() }
            },
            response: new ElicitResult { Action = "accept" });
        var sut = new ElicitationHumanApproval();

        var result = await sut.RequestAsync(server, Request);

        result.Outcome.Should().Be(ApprovalOutcome.Approved);
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task RequestAsync_UserDeclinesOrClientCancels_ReturnsRefused(string action)
    {
        var server = new FakeMcpServer(
            capabilities: new ClientCapabilities
            {
                Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() }
            },
            response: new ElicitResult { Action = action });
        var sut = new ElicitationHumanApproval();

        var result = await sut.RequestAsync(server, Request);

        result.Outcome.Should().Be(ApprovalOutcome.Refused);
        result.Detail.Should().Contain(action);
    }

    [Fact]
    public async Task RequestAsync_SendsTheMessageAndABooleanConfirmSchema()
    {
        var server = new FakeMcpServer(
            capabilities: new ClientCapabilities
            {
                Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() }
            },
            response: new ElicitResult { Action = "accept" });
        var sut = new ElicitationHumanApproval();

        await sut.RequestAsync(server, Request);

        server.LastParams.Should().NotBeNull();
        server.LastParams!.Message.Should().Be("Create register 'Acme Supply'?");
        server.LastParams.RequestedSchema.Properties.Should().ContainKey("confirm");
        server.LastParams.RequestedSchema.Required.Should().Contain("confirm");
    }
}

/// <summary>
/// Minimal <see cref="ModelContextProtocol.Server.McpServer"/> stand-in. <c>ElicitAsync</c> is
/// non-virtual, so it cannot be overridden; it is intercepted at <c>SendRequestAsync</c>, which
/// is the primitive it calls.
/// <para>
/// <see cref="ModelContextProtocol.Server.McpServer"/> and its base <c>McpSession</c> carry a
/// wider abstract surface than the elicitation path needs (transport-level session plumbing,
/// server options, logging level, etc.). Everything outside the elicitation path throws
/// <see cref="NotSupportedException"/> — the tests in this file must only ever exercise
/// <c>ClientCapabilities</c> and <c>SendRequestAsync</c>.
/// </para>
/// </summary>
internal sealed class FakeMcpServer : ModelContextProtocol.Server.McpServer
{
    private readonly ElicitResult? _response;

#pragma warning disable MCPEXP002 // McpServer's parameterless constructor is marked experimental
                                   // in SDK 2.2.0; this fake deliberately extends it to intercept
                                   // SendRequestAsync for testing (ElicitAsync itself is sealed).
    public FakeMcpServer(ClientCapabilities capabilities, ElicitResult? response = null)
#pragma warning restore MCPEXP002
    {
        ClientCapabilities = capabilities;
        _response = response;
    }

    public override ClientCapabilities? ClientCapabilities { get; }

    public int ElicitCallCount { get; private set; }

    public ElicitRequestParams? LastParams { get; private set; }

    /// <inheritdoc />
    public override Implementation? ClientInfo => throw new NotSupportedException();

    /// <inheritdoc />
    public override McpServerOptions ServerOptions => throw new NotSupportedException();

    /// <inheritdoc />
    public override IServiceProvider? Services => throw new NotSupportedException();

    /// <inheritdoc />
#pragma warning disable CS0672, MCP9005 // Logging is deprecated in the SDK; the abstract member
                                        // still must be overridden, and this fake never uses it.
    public override LoggingLevel? LoggingLevel => throw new NotSupportedException();
#pragma warning restore CS0672, MCP9005

    /// <inheritdoc />
    public override string? SessionId => throw new NotSupportedException();

    /// <inheritdoc />
    public override string? NegotiatedProtocolVersion => throw new NotSupportedException();

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
    public override ValueTask DisposeAsync() => throw new NotSupportedException();

    /// <inheritdoc />
    public override async Task<JsonRpcResponse> SendRequestAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken = default)
    {
        ElicitCallCount++;
        LastParams = JsonSerializer.Deserialize<ElicitRequestParams>(
            request.Params, McpJsonUtilities.DefaultOptions);
        await Task.Yield();
        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = JsonSerializer.SerializeToNode(
                _response ?? new ElicitResult { Action = "cancel" },
                McpJsonUtilities.DefaultOptions)
        };
    }
}
