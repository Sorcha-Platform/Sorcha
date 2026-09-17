// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Responses;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Chooses which of the caller's wallets submits an action (#1658). This is the one home of that rule, so
/// that a client never has to re-derive participant bindings to learn which wallet to send.
/// </summary>
/// <remarks>
/// <para>
/// It mirrors what <c>ActionExecutionService.ExecuteAsync</c> will accept, in the same precedence:
/// </para>
/// <list type="number">
/// <item>A wallet hard-coded on the sender participant in the published blueprint is matched strictly
/// (execute step 4c), whatever the instance records.</item>
/// <item>Otherwise an existing instance binding for the sender participant is immutable (execute step 4d).</item>
/// <item>Otherwise the sender is unbound, and the first submission binds it. One caller wallet is
/// unambiguous; several are not, because choosing wrongly binds that wallet for the life of the
/// instance.</item>
/// </list>
/// <para>
/// Wallet comparisons are ordinal-ignore-case, as in execute. It only ever returns wallets the caller
/// holds: it advises, and the execute path and the Wallet Service remain the authority.
/// </para>
/// </remarks>
public static class SenderWalletResolver
{
    /// <summary>Resolves the submission context for <paramref name="action"/> on <paramref name="instance"/>.</summary>
    /// <param name="blueprint">The instance's pinned published definition.</param>
    /// <param name="action">The action being submitted.</param>
    /// <param name="instance">The workflow instance.</param>
    /// <param name="callerWallets">Every wallet resolved for the caller.</param>
    public static ActionSubmissionContext Resolve(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        Sorcha.Blueprint.Models.Action action,
        Instance instance,
        IReadOnlyCollection<string> callerWallets)
    {
        var bound = BoundWallet(blueprint, action, instance);

        if (bound is not null)
        {
            var holdsIt = callerWallets.Any(w => string.Equals(w, bound, StringComparison.OrdinalIgnoreCase));
            return holdsIt
                ? Context(instance, SenderWalletStatus.Resolved, bound)
                : Context(instance, SenderWalletStatus.NotYours, null);
        }

        var distinct = callerWallets
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinct.Count switch
        {
            0 => Context(instance, SenderWalletStatus.NoWallet, null),
            1 => Context(instance, SenderWalletStatus.Resolved, distinct[0]),
            _ => Context(instance, SenderWalletStatus.Ambiguous, null) with { CandidateWallets = distinct },
        };
    }

    private static string? BoundWallet(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        Sorcha.Blueprint.Models.Action action,
        Instance instance)
    {
        if (string.IsNullOrWhiteSpace(action.Sender))
        {
            return null;
        }

        var hardcoded = blueprint.Participants?
            .FirstOrDefault(p => string.Equals(p.Id, action.Sender, StringComparison.OrdinalIgnoreCase))?
            .WalletAddress;
        if (!string.IsNullOrWhiteSpace(hardcoded))
        {
            return hardcoded;
        }

        return instance.ParticipantWallets.TryGetValue(action.Sender, out var boundWallet)
               && !string.IsNullOrWhiteSpace(boundWallet)
            ? boundWallet
            : null;
    }

    private static ActionSubmissionContext Context(Instance instance, SenderWalletStatus status, string? wallet) => new()
    {
        BlueprintId = instance.BlueprintId,
        RegisterId = instance.RegisterId,
        SenderWalletStatus = status,
        SenderWallet = wallet,
    };
}
