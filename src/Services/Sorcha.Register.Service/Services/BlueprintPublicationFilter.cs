// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// The one rule for deciding whether a ledger transaction PUBLISHES a blueprint, as opposed to
/// merely NAMING one.
/// </summary>
/// <remarks>
/// <para>
/// The distinction is not cosmetic. Every governance transaction and every crypto-policy update
/// carries <c>MetaData.BlueprintId = "register-governance-v1"</c>, because a control transaction is
/// genuinely an operation against the governance workflow. A reader that treats "names a blueprint"
/// as "publishes a blueprint" therefore serves control records as definitions.
/// </para>
/// <para>
/// That has now happened twice, at two different readers. #1515: the system register's blueprint
/// LOOKUP took the newest transaction naming the id, so governing the SSR made "the governance
/// blueprint" resolve to a governance control transaction — a payload that deserialises into a
/// blueprint without complaint, just an empty one, producing <c>VAL_SCHEMA_003: Action 1 not found</c>
/// against a blueprint that was in fact correct. #1587: the published-blueprint LIST did the same
/// thing, and its own comment described the missing gate.
/// </para>
/// <para>
/// It lives here, named for what it decides and not for the caller that first needed it, precisely
/// because the second occurrence was a second rule written under the same name. It is register-
/// agnostic: nothing in it is specific to the system register.
/// </para>
/// </remarks>
public static class BlueprintPublicationFilter
{
    /// <summary>
    /// True when <paramref name="tx"/> publishes a blueprint definition.
    /// </summary>
    /// <param name="tx">A ledger transaction, of any register.</param>
    /// <remarks>
    /// The marker is written alongside the publication rather than inferred, so a future control
    /// transaction that happens to carry a <c>BlueprintId</c> cannot re-open #1515 by accident. The
    /// <c>ActionId</c> arm is the pre-marker fallback: a blueprint publication is not an action
    /// submission and carries no action id, while everything governance writes carries one
    /// (1 propose, 2 approve, 4 enact) — as does a crypto-policy update.
    /// </remarks>
    public static bool IsPublication(TransactionModel tx)
    {
        var meta = tx.MetaData;

        if (meta is null
            || string.IsNullOrEmpty(meta.BlueprintId)
            || meta.BlueprintId == "genesis")
        {
            return false;
        }

        // Post-#876: publications carry their own persisted transaction type.
        if (meta.TransactionType == TransactionType.BlueprintPublish)
        {
            return true;
        }

        // Pre-#876: Control + an explicit marker. Present and not "BlueprintPublish" is a decisive
        // NO — that is the arm the phantom publications (CryptoPolicyUpdate, GovernanceApproval,
        // GovernanceOperation) fall down.
        if (meta.TrackingData is not null
            && meta.TrackingData.TryGetValue("transactionType", out var marker)
            && !string.IsNullOrWhiteSpace(marker))
        {
            return string.Equals(marker, nameof(TransactionType.BlueprintPublish),
                StringComparison.OrdinalIgnoreCase);
        }

        // Pre-marker era: no marker to read, so fall back to the structural difference.
        return meta.ActionId is null;
    }
}
