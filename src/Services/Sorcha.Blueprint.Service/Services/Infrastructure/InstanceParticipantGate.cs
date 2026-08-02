// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Models;
using Sorcha.ServiceClients.Wallet;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Services.Infrastructure;

/// <summary>
/// Issue #1182 — the single authority for "may this authenticated caller read this workflow
/// instance?". Extracted from the inline gate in
/// <see cref="Sorcha.Blueprint.Service.Endpoints.InstanceActionEndpoints"/> once three more
/// endpoints needed the identical decision; one seam rather than four copies that drift apart.
///
/// <para><b>Why a gate is needed at all.</b> The <c>/api/instances</c> group carries only
/// <c>CanExecuteBlueprints</c>, which resolves to a bare <c>RequireAuthenticatedUser()</c>. It
/// asserts "someone is signed in" and nothing else — no participant check, no org scoping, no tier
/// check. Every endpoint returning instance content must therefore add its own check on top.</para>
///
/// <para><b>The two traps this type exists to make unrepeatable.</b></para>
/// <list type="number">
/// <item><description><b>Reading <c>wallet_address</c> off the claim set.</b> Under Feature 136 a
/// consumer-tier token — every real citizen sign-in, web and PWA — deliberately OMITS that claim
/// (<c>TokenService.AddWalletAddressClaimAsync</c> fires only for <c>Tier.Platform</c>). A
/// claim-only gate is therefore unconditionally false for the exact population these endpoints
/// serve: it 403s every genuine citizen while leaving the hole wide open for platform-tier callers.
/// Resolution MUST go through <see cref="ParticipantWalletResolver"/>, which falls back to a
/// Wallet-Service lookup keyed on the owner. This mistake has now been made twice — once on
/// <c>InstanceActionEndpoints</c> before it shipped, and once in that endpoint's own tests, which
/// claimed to exercise a consumer-tier principal while actually building a platform-tier one.</description></item>
/// <item><description><b>Gating on participation alone.</b> <c>CreateInstance</c> pre-populates
/// <see cref="Instance.ParticipantWallets"/> only from blueprint participants that already carry a
/// wallet — which by construction EXCLUDES the Feature 103 open participant, i.e. the walk-in
/// citizen the workflow exists for. Between <c>POST /api/instances/</c> and their starting action
/// sealing, the applicant is not a participant on their own instance, so a participation-only gate
/// breaks the apply flow for every citizen (the PWA reads <c>GET /api/instances/{id}</c> to find the
/// current action before submitting anything). See <see cref="IsAwaitingOpenParticipant"/>.</description></item>
/// </list>
/// </summary>
public static class InstanceParticipantGate
{
    /// <summary>
    /// Whether any wallet the caller controls is a recorded participant on <paramref name="instance"/>.
    ///
    /// <para>The caller's wallets are resolved via
    /// <see cref="ParticipantWalletResolver.ResolveUserWalletAddressesAsync"/> (claim fast path, then
    /// Wallet-Service-by-owner fallback) rather than read off a single claim — see the type remarks.
    /// A caller may control more than one wallet, so membership matches if ANY of them is a
    /// participant.</para>
    ///
    /// <para>An empty resolved set means "could not resolve any wallet for this caller", not "did not
    /// look" — <see cref="ParticipantWalletResolver"/> only returns empty after exhausting both the
    /// claim and the owner-keyed lookup. It therefore fails closed to <c>false</c>, like any other
    /// non-match.</para>
    /// </summary>
    public static async Task<bool> IsParticipantAsync(
        HttpContext httpContext,
        Instance instance,
        IWalletServiceClient walletClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var callerWallets = await ParticipantWalletResolver.ResolveUserWalletAddressesAsync(
            httpContext, walletClient, logger, cancellationToken);

        return callerWallets.Count > 0
            && instance.ParticipantWallets.Values.Any(participantWallet =>
                callerWallets.Any(w => string.Equals(w, participantWallet, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Whether <paramref name="instance"/> is an untouched shell still waiting for the open
    /// (Feature 103) participant who will be late-bound to it on first submission — in which case any
    /// authenticated caller may read it.
    ///
    /// <para><b>Why this carve-out is required rather than merely convenient.</b> The citizen an open
    /// starting action exists for is NOT in <see cref="Instance.ParticipantWallets"/> until they
    /// submit and the projection folds the late-bind. The Wallet PWA reads
    /// <c>GET /api/instances/{id}</c> to discover the current action BEFORE the citizen has typed
    /// anything, so without this the apply flow 403s for every citizen — the same regression class as
    /// #1183 on the sibling action-schema endpoint, whose <c>IsOpenStartingAction</c> this mirrors at
    /// instance scope.</para>
    ///
    /// <para><b>Why it is safe.</b> Both conditions must hold, and together they mean the instance
    /// contains nothing a stranger could learn from. <see cref="Instance.CompletedActionCount"/> of 0
    /// guarantees <see cref="Instance.AccumulatedData"/> is empty — no name, no date of birth, no
    /// address, no portrait — and the only entries in <see cref="Instance.ParticipantWallets"/> are
    /// the wallets the published blueprint already carries in the clear on the register, which are
    /// public by construction. The carve-out closes at exactly the moment content appears: the first
    /// completed action makes <c>CompletedActionCount</c> non-zero, and from then on only genuine
    /// participants read the instance.</para>
    ///
    /// <para>Fails closed when the blueprint cannot be resolved (a replica that has not finished
    /// replicating, per <c>CreateInstance</c>'s own fallback) — an unresolvable blueprint means the
    /// open-participant claim cannot be verified, so it is not granted.</para>
    /// </summary>
    public static bool IsAwaitingOpenParticipant(Instance instance, BlueprintModel? blueprint)
    {
        if (blueprint is null || instance.CompletedActionCount != 0)
        {
            return false;
        }

        // Every current action must be checked, not just the first: a blueprint may declare parallel
        // starting actions, and it is enough that one of them is still awaiting its open participant.
        return instance.CurrentActionIds.Any(actionId =>
        {
            var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionId);
            if (action is null || !action.IsStartingAction)
            {
                return false;
            }

            var sender = blueprint.Participants
                .FirstOrDefault(p => string.Equals(p.Id, action.Sender, StringComparison.OrdinalIgnoreCase));

            // No resolvable sender participant is treated as NOT open — fail closed.
            return sender is not null && string.IsNullOrEmpty(sender.WalletAddress);
        });
    }
}
