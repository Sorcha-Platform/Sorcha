// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.McpServer.Services;

/// <summary>How a request for human approval resolved.</summary>
public enum ApprovalOutcome
{
    /// <summary>A person explicitly accepted. The ONLY outcome that permits the operation.</summary>
    Approved,

    /// <summary>A person declined, or the client dismissed the request without a choice.</summary>
    Refused,

    /// <summary>The client did not declare the elicitation capability, so no person can be asked.</summary>
    NotSupported
}

/// <summary>What the person is being asked to approve.</summary>
/// <param name="Message">The full question, naming the concrete consequence.</param>
/// <param name="ConfirmTitle">The label on the confirmation field.</param>
public sealed record HumanApprovalRequest(string Message, string ConfirmTitle);

/// <summary>The resolved approval, with operator-facing detail for the refusal paths.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Detail">Human-readable explanation, surfaced to the agent verbatim.</param>
public sealed record ApprovalResult(ApprovalOutcome Outcome, string Detail);

/// <summary>
/// Obtains a real person's approval before an operationally-consequential act.
/// <para>
/// This exists as a seam for two reasons. First, <c>McpServer.ElicitAsync</c> is non-virtual in
/// SDK 2.2.0 and therefore cannot be mocked, so a tool calling it directly is untestable.
/// Second, the platform genuinely cannot tell an agent from the human whose bearer token it
/// forwards — so the sign-off must happen at the client, where the person actually is, and this
/// is the single place that contract is expressed.
/// </para>
/// </summary>
public interface IHumanApproval
{
    /// <summary>Asks the caller's client to put <paramref name="request"/> to a person.</summary>
    /// <param name="server">The live MCP server for this invocation (from <c>RequestContext.Server</c>).</param>
    /// <param name="request">What the person is being asked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The approval outcome. Treat anything but <see cref="ApprovalOutcome.Approved"/> as a refusal.</returns>
    Task<ApprovalResult> RequestAsync(
        ModelContextProtocol.Server.McpServer server,
        HumanApprovalRequest request,
        CancellationToken cancellationToken = default);
}
