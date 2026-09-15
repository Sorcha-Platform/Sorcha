// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Sorcha.McpServer.Services;

/// <summary>
/// Obtains approval through MCP elicitation, carried by Multi Round-Trip Requests (MRTR). The only
/// place in the codebase that asks a client to put a question to a person.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why MRTR, and not <c>McpServer.ElicitAsync</c> (#1622).</b> The HTTP transport runs stateless
/// for horizontal scale. In that mode SDK 2.2.0 disables every server-to-client request —
/// <c>ElicitAsync</c> included — and <c>McpServer.ClientCapabilities</c> is null even when the client
/// sent <c>"elicitation":{}</c> in the request's <c>_meta</c>. The previous gate
/// (<c>ClientCapabilities?.Elicitation?.Form is null</c>) therefore refused every client on the HTTP
/// transport, permanently, and read as "Claude Code cannot elicit". It can: Claude Code 2.1.272
/// negotiates protocol revision 2026-07-28 and round-trips an MRTR elicitation end to end against a
/// stateless SDK 2.2.0 server (measured 2026-09-15).
/// </para>
/// <para>
/// <b>The tool runs twice.</b> The first invocation throws <see cref="InputRequiredException"/>; the
/// client puts the form to a person and re-sends the SAME tool call with the answer in
/// <c>inputResponses</c> and this class's <c>requestState</c> echoed back. Everything a tool does
/// before asking re-executes on the second round, so it must be side-effect free — which both callers
/// already required, because nobody may be asked to approve something that has already happened.
/// </para>
/// <para>
/// <b>The request state binds the answer to the question.</b> It is a digest of exactly what the
/// person was shown. An answer that arrives with no state (unsolicited) or with state for a different
/// prompt — another register name, another storage mode — is refused, never applied. It is
/// deliberately NOT a MAC: the client is the party that answers the elicitation on every transport, so
/// a client willing to forge the state could forge the <c>accept</c> just as easily. The digest
/// defends against a mismatched or stale answer; no server-side mechanism can defend against a
/// dishonest client, which is why the sign-off lives on the client at all (see
/// <see cref="IHumanApproval"/>).
/// </para>
/// </remarks>
public sealed class ElicitationHumanApproval : IHumanApproval
{
    /// <summary>The <c>inputRequests</c> key the confirmation travels under.</summary>
    public const string InputKey = "sorcha_human_approval";

    /// <summary>The single boolean field on the confirmation form.</summary>
    public const string ConfirmField = "confirm";

    private const string StatePrefix = "sorcha-approval:v1:";

    /// <inheritdoc />
    public ApprovalResult Evaluate(RequestContext<CallToolRequestParams> context, HumanApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);

        var expectedState = BindState(request);

        // Second round: the client is returning a person's answer.
        if (context.Params?.InputResponses is { } responses
            && responses.TryGetValue(InputKey, out var response))
        {
            return Resolve(context.Params.RequestState, expectedState, response);
        }

        // First round. Fail closed: a client that cannot carry the question does not get to perform
        // the act — in every environment, with no bypass flag. Deliberately NOT a check on
        // ClientCapabilities, which the stateless transport leaves null for every client (#1622).
        if (!context.Server.IsMrtrSupported)
        {
            return new ApprovalResult(
                ApprovalOutcome.NotSupported,
                "This operation needs a person to confirm it, and your MCP client cannot be asked: the "
                + "confirmation is an MCP elicitation carried as a multi round-trip request, which needs "
                + "protocol revision 2026-07-28 or later. Connect with a client that supports it, or "
                + "perform this step in the Sorcha UI. Nothing was changed.");
        }

        throw new InputRequiredException(
            new Dictionary<string, InputRequest>
            {
                [InputKey] = InputRequest.ForElicitation(BuildElicitation(request))
            },
            expectedState);
    }

    private static ApprovalResult Resolve(string? returnedState, string expectedState, InputResponse response)
    {
        if (!string.Equals(returnedState, expectedState, StringComparison.Ordinal))
        {
            return new ApprovalResult(
                ApprovalOutcome.Refused,
                "A confirmation came back, but not for this exact request — it carried no request "
                + "state, or state for a different question (the operation's details may have changed "
                + "since the person was asked). It was not applied and nothing was changed. Call the "
                + "tool again to ask afresh.");
        }

        ElicitResult? result;
        try
        {
            result = response.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
        }
        catch (JsonException)
        {
            result = null;
        }

        if (result is null)
        {
            return new ApprovalResult(
                ApprovalOutcome.Refused,
                "The confirmation response could not be read, so it was treated as not approved. "
                + "Nothing was changed.");
        }

        // IsAccepted is true ONLY for action == "accept". Do not claim a person declined: a headless
        // client (Claude Code in `-p` mode, for one) answers "cancel" to every elicitation without a
        // person ever seeing it. Only "approved" is ever a safe claim about what happened.
        if (!result.IsAccepted)
        {
            return new ApprovalResult(
                ApprovalOutcome.Refused,
                $"This operation was not approved (action: {result.Action}). This may be a person "
                + "explicitly declining, or an MCP client that auto-answered without presenting the "
                + "request to anyone — supporting elicitation is not a guarantee a person is asked. "
                + "Nothing was changed.");
        }

        // accept is necessary but not sufficient: the form's one field says "Anything else cancels".
        if (result.Content is null
            || !result.Content.TryGetValue(ConfirmField, out var confirm)
            || confirm.ValueKind != JsonValueKind.True)
        {
            return new ApprovalResult(
                ApprovalOutcome.Refused,
                "The confirmation form was submitted without the confirm box set, so it was treated as "
                + "not approved. Nothing was changed.");
        }

        return new ApprovalResult(ApprovalOutcome.Approved, "Approved by the user.");
    }

    private static ElicitRequestParams BuildElicitation(HumanApprovalRequest request) => new()
    {
        Message = request.Message,
        RequestedSchema = new ElicitRequestParams.RequestSchema
        {
            Properties =
            {
                [ConfirmField] = new ElicitRequestParams.BooleanSchema
                {
                    Title = request.ConfirmTitle,
                    Description = "Confirm to proceed. Anything else cancels."
                }
            },
            // RequestSchema.Required defaults to null (not an empty collection), so the
            // collection-initializer shorthand `Required = { "confirm" }` would call Add on a null
            // reference. Assign a new list instead.
            Required = new List<string> { ConfirmField }
        }
    };

    /// <summary>A digest of exactly what the person is shown — the question, not the tool call.</summary>
    private static string BindState(HumanApprovalRequest request)
    {
        var shown = Encoding.UTF8.GetBytes(request.ConfirmTitle + "␟" + request.Message);
        return StatePrefix + Convert.ToHexStringLower(SHA256.HashData(shown));
    }
}
