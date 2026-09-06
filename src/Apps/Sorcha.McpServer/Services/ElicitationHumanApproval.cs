// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using ModelContextProtocol.Protocol;

namespace Sorcha.McpServer.Services;

/// <summary>
/// Obtains approval through the MCP elicitation primitive. The only place in the codebase that
/// calls <see cref="ModelContextProtocol.Server.McpServer.ElicitAsync(ElicitRequestParams, CancellationToken)"/>.
/// </summary>
public sealed class ElicitationHumanApproval : IHumanApproval
{
    /// <inheritdoc />
    public async Task<ApprovalResult> RequestAsync(
        ModelContextProtocol.Server.McpServer server,
        HumanApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(request);

        // Fail closed. A client that cannot ask a person does not get to perform the act —
        // in every environment, with no bypass flag.
        //
        // The request below is always object-schema ("form mode") elicitation, never the
        // URL-redirect mode. A client may declare the 'elicitation' capability while supporting
        // only Url (not Form) — checking Elicitation alone would let that client through this
        // gate and then hit an unhandled InvalidOperationException inside McpServer.ElicitAsync
        // ("Client does not support form mode elicitation requests"), verified against SDK 2.2.0.
        if (server.ClientCapabilities?.Elicitation?.Form is null)
        {
            return new ApprovalResult(
                ApprovalOutcome.NotSupported,
                "This operation needs a person to confirm it, and your MCP client did not " +
                "declare the 'elicitation' capability (form mode) at initialize. Connect with a " +
                "client that supports elicitation, or perform this step in the Sorcha UI.");
        }

        var parameters = new ElicitRequestParams
        {
            Message = request.Message,
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties =
                {
                    ["confirm"] = new ElicitRequestParams.BooleanSchema
                    {
                        Title = request.ConfirmTitle,
                        Description = "Confirm to proceed. Anything else cancels."
                    }
                },
                // RequestSchema.Required defaults to null (not an empty collection), so the
                // collection-initializer shorthand `Required = { "confirm" }` would call Add on
                // a null reference. Assign a new list instead.
                Required = new List<string> { "confirm" }
            }
        };

        var result = await server.ElicitAsync(parameters, cancellationToken).ConfigureAwait(false);

        // IsAccepted is true ONLY for action == "accept". A non-interactive client answers
        // "cancel" automatically, which must not read as approval.
        if (result.IsAccepted)
        {
            return new ApprovalResult(ApprovalOutcome.Approved, "Approved by the user.");
        }

        return new ApprovalResult(
            ApprovalOutcome.Refused,
            $"The user did not approve this operation (action: {result.Action}). Nothing was changed.");
    }
}
