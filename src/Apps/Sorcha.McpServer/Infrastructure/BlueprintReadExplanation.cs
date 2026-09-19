// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;

using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.McpServer.Infrastructure;

/// <summary>
/// Turns a failed Blueprint read into a sentence that says WHICH failure it was.
/// </summary>
/// <remarks>
/// <para>
/// Cold-start run #5: the counterparty organisation was refused an instance it is a participant on
/// (403, because the wallet its session controls is not the one bound to its role) and the tools
/// reported "Workflow not found." and "Action not found." It reasonably concluded the instance did
/// not exist or predated its own participant record, and spent its remaining turns on that.
/// </para>
/// <para>
/// A 403 and a 404 call for opposite responses — one is "you cannot see this", the other "this is
/// not there" — so collapsing them costs the caller the only clue it had. Same defect class as
/// #1659 and #1641.
/// </para>
/// </remarks>
internal static class BlueprintReadExplanation
{
    /// <summary>Explains a failed read of a whole instance.</summary>
    internal static string ForInstance(BlueprintReadResult read, string instanceId) => read.Status switch
    {
        HttpStatusCode.Forbidden =>
            $"You are not permitted to read instance '{instanceId}'. It exists — this is an "
            + "authorisation refusal, not a missing instance. Access is matched on the wallet your "
            + "session controls against the wallets bound to this instance's participants, so check "
            + "with sorcha_participant_list that your role is bound to a wallet you actually hold.",

        HttpStatusCode.NotFound =>
            $"No instance '{instanceId}' exists on this node.",

        HttpStatusCode.Unauthorized =>
            "Your session is not authenticated. The access token has probably expired.",

        _ => $"Reading instance '{instanceId}' failed: the service answered {(int)read.Status}. "
             + "Nothing about the instance can be concluded from this.",
    };

    /// <summary>Explains a failed read of one action on an instance.</summary>
    internal static string ForAction(BlueprintReadResult read, string instanceId, string actionId) =>
        read.Status switch
        {
            HttpStatusCode.Forbidden =>
                $"You are not permitted to read action {actionId} on instance '{instanceId}'. It "
                + "exists — this is an authorisation refusal, not a missing action. Access is "
                + "matched on the wallet your session controls against the wallets bound to this "
                + "instance's participants, so check with sorcha_participant_list that your role is "
                + "bound to a wallet you actually hold.",

            HttpStatusCode.NotFound =>
                $"No action {actionId} exists on instance '{instanceId}'.",

            HttpStatusCode.Unauthorized =>
                "Your session is not authenticated. The access token has probably expired.",

            _ => $"Reading action {actionId} on instance '{instanceId}' failed: the service answered "
                 + $"{(int)read.Status}. Nothing about the action can be concluded from this.",
        };
}
