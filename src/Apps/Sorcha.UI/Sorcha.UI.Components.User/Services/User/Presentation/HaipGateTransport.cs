// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Services.Credentials;

namespace Sorcha.UI.Core.Services.User.Presentation;

/// <summary>
/// HAIP gate transport — polls the verifier's result endpoint. Claims arrive inline with the
/// outcome, so <see cref="FetchClaimsAsync"/> replays what the last poll carried rather than
/// making a second call.
/// </summary>
public sealed class HaipGateTransport(
    IHaipOfferService haip,
    TimeProvider time,
    ILogger<HaipGateTransport> logger) : IPresentationGateTransport
{
    private IReadOnlyDictionary<string, object?>? _lastClaims;

    /// <inheritdoc />
    public PresentationSource Source => PresentationSource.HaipExternalWallet;

    /// <inheritdoc />
    public async Task<GateOutcome> WaitForOutcomeAsync(
        Guid requestId, IProgress<GateOutcome>? progress = null, CancellationToken ct = default)
    {
        var notFoundStreak = 0;
        var lastReported = GateOutcome.Pending;

        for (var tick = 0; tick < HaipPollingDefaults.MaxPollTicks; tick++)
        {
            if (ct.IsCancellationRequested) return GateOutcome.Abandoned;

            var poll = await haip.PollVerificationResultAsync(requestId, ct).ConfigureAwait(false);

            // A 404 is permanent — this verifier has no such request. Tolerate a couple (a
            // just-created request can 404 briefly), then stop and SAY SO. Collapsing it into
            // "not scanned yet" is what let a doomed request poll for the full five-minute window
            // and then get reported as Expired, sending the citizen to check their own wallet.
            if (poll.RequestNotFound)
            {
                if (++notFoundStreak >= HaipPollingDefaults.MaxConsecutiveNotFound)
                {
                    logger.LogError(
                        "Verifier has no request {RequestId} after {Streak} consecutive 404s. If "
                        + "this gate is presentationSource 'SorchaWallet', its request lives in the "
                        + "Blueprint presentation lifecycle, not HAIP.",
                        requestId, notFoundStreak);
                    return GateOutcome.Unreachable;
                }
            }
            else
            {
                notFoundStreak = 0;

                if (poll.Result is { } result)
                {
                    _lastClaims = result.VerifiedClaims?
                        .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

                    var outcome = MapState(result.State);
                    if (GateOutcomes.IsTerminal(outcome)) return outcome;

                    if (outcome != lastReported)
                    {
                        lastReported = outcome;
                        progress?.Report(outcome);
                    }
                }
            }

            try
            {
                await Task.Delay(HaipPollingDefaults.PollInterval, time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return GateOutcome.Abandoned;
            }
        }

        logger.LogInformation("Poll budget exhausted for {RequestId}", requestId);
        return GateOutcome.Expired;
    }

    /// <summary>Maps a HAIP verification state onto the transport-neutral outcome.</summary>
    private static GateOutcome MapState(string state) => state switch
    {
        HaipVerificationStates.Verified => GateOutcome.Success,
        HaipVerificationStates.Denied => GateOutcome.Declined,
        HaipVerificationStates.Expired => GateOutcome.Expired,
        HaipVerificationStates.Cancelled => GateOutcome.Abandoned,
        HaipVerificationStates.Submitted => GateOutcome.Submitted,
        _ => GateOutcome.Pending
    };

    /// <inheritdoc />
    /// <remarks>
    /// HAIP returns verified claims with the outcome itself, so there is no token and no second
    /// round trip — this replays what the last poll carried.
    /// </remarks>
    public Task<IReadOnlyDictionary<string, object?>?> FetchClaimsAsync(
        Guid requestId, string? claimsFetchToken, CancellationToken ct = default)
        => Task.FromResult(_lastClaims);
}
