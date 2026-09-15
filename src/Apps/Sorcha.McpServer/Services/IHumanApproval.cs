// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Sorcha.McpServer.Services;

/// <summary>How a request for human approval resolved.</summary>
public enum ApprovalOutcome
{
    /// <summary>
    /// A person explicitly accepted AND set the confirm field. The ONLY outcome that permits the
    /// operation.
    /// </summary>
    Approved,

    /// <summary>
    /// A person declined, the client dismissed the request without a choice, the form came back
    /// without the confirm field set, or the answer was not for this exact request.
    /// </summary>
    Refused,

    /// <summary>
    /// The client cannot carry an elicitation as a multi round-trip request (a protocol revision
    /// earlier than 2026-07-28 against the stateless HTTP transport), so no person can be asked.
    /// </summary>
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
/// This exists as a seam for two reasons. First, the SDK's round-trip machinery is awkward to drive
/// from a tool test, so tools depend on this and the mechanics are tested once, here. Second, the
/// platform genuinely cannot tell an agent from the human whose bearer token it forwards — so the
/// sign-off must happen at the client, where the person actually is, and this is the single place
/// that contract is expressed.
/// </para>
/// </summary>
public interface IHumanApproval
{
    /// <summary>
    /// Resolves approval for <paramref name="request"/> — or, when the person has not been asked
    /// yet, asks by THROWING.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the first round this throws <see cref="InputRequiredException"/>. That is not an error: it
    /// is how a stateless MCP server hands a question to the client (Multi Round-Trip Requests). The
    /// client puts it to a person and re-invokes the SAME tool call with the answer, at which point
    /// this returns an outcome.
    /// </para>
    /// <para>
    /// <b>Callers MUST let that exception reach the SDK.</b> A <c>catch (Exception)</c> around this
    /// call swallows the question, and the person is never asked. And because the whole tool method
    /// runs again on the second round, everything before this call must be side-effect free.
    /// </para>
    /// </remarks>
    /// <param name="context">The tool invocation's request context, injected by the SDK.</param>
    /// <param name="request">What the person is being asked.</param>
    /// <returns>The approval outcome. Treat anything but <see cref="ApprovalOutcome.Approved"/> as a refusal.</returns>
    /// <exception cref="InputRequiredException">The person has not been asked yet; let it propagate.</exception>
    ApprovalResult Evaluate(RequestContext<CallToolRequestParams> context, HumanApprovalRequest request);
}
