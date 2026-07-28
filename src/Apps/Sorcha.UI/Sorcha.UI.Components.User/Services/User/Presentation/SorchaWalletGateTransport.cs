// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models.User.Presentation;

namespace Sorcha.UI.Core.Services.User.Presentation;

/// <summary>
/// Feature 127 gate transport. Waiting is delegated to <see cref="IPresentationSignal"/>, which
/// already races the BlueprintHub <c>PresentationOutcomeReady</c> event against a 3 s poll of
/// <c>/api/presentations/{id}/status</c>; this class maps that signal onto
/// <see cref="GateOutcome"/> and fetches the disclosed claims with the single-use
/// <c>ClaimsFetchToken</c>.
/// </summary>
/// <remarks>
/// The hub is a latency optimisation, not a guarantee — Feature 119's deferred-outcome path does
/// not publish <c>PresentationOutcomeReady</c> yet, so the signal's poll is load-bearing. That
/// race lives in <see cref="PresentationSignal"/> and is deliberately not duplicated here.
/// </remarks>
public sealed class SorchaWalletGateTransport(
    IPresentationSignal signal,
    HttpClient http,
    ILogger<SorchaWalletGateTransport> logger) : IPresentationGateTransport
{
    /// <inheritdoc />
    public PresentationSource Source => PresentationSource.SorchaWallet;

    /// <inheritdoc />
    public async Task<GateOutcome> WaitForOutcomeAsync(
        Guid requestId, IProgress<GateOutcome>? progress = null, CancellationToken ct = default)
    {
        var completion = new TaskCompletionSource<GateOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task OnOutcome(PresentationSignalOutcome outcome)
        {
            // The signal instance is per-request, but an outcome for a different id would still
            // be a wrong answer here — check rather than assume.
            if (outcome.PresentationRequestId == requestId)
            {
                completion.TrySetResult(MapKind(outcome.Kind));
            }
            return Task.CompletedTask;
        }

        // A 404 streak IS terminal: the lifecycle holds no such request, so no amount of further
        // waiting can succeed.
        void OnUnreachable() => completion.TrySetResult(GateOutcome.Unreachable);

        // Manual-recovery fires after 60 s with no signal. That is NOT terminal and must never be
        // reported as Unreachable: a presentation request is valid for its whole window (ten
        // minutes on n1), and sixty seconds is simply how long it takes a citizen to pick up their
        // phone, unlock it and scan. Ending the wait here told a citizen "nothing was sent from
        // your wallet" about a request that was alive and waiting for them — the same wrong
        // diagnosis this component exists to remove, pointed at a different innocent party.
        // Expiry is the lifecycle's job: /status reports "expired" and that maps to Expired.
        void OnManualRecovery() => progress?.Report(GateOutcome.Pending);

        void OnFallback() => progress?.Report(GateOutcome.Pending);

        signal.OnOutcomeReady += OnOutcome;
        signal.OnRequestUnreachable += OnUnreachable;
        signal.OnManualRecoveryRequired += OnManualRecovery;
        signal.OnFallbackEngaged += OnFallback;

        using var registration = ct.Register(
            () => completion.TrySetResult(GateOutcome.Abandoned));

        try
        {
            await signal.StartAsync(requestId, ct).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A transport that throws leaves the card with nothing to render but a spinner.
            logger.LogError(ex, "Presentation signal failed to start for {RequestId}", requestId);
            return GateOutcome.Unreachable;
        }
        finally
        {
            signal.OnOutcomeReady -= OnOutcome;
            signal.OnRequestUnreachable -= OnUnreachable;
            signal.OnManualRecoveryRequired -= OnManualRecovery;
            signal.OnFallbackEngaged -= OnFallback;
            try { await signal.StopAsync().ConfigureAwait(false); } catch { /* non-fatal */ }
        }
    }

    /// <summary>
    /// Maps a Feature 111 lifecycle state onto the transport-neutral outcome.
    /// <c>abandoned-with-late-outcome</c> is a success: the presentation did arrive, just after
    /// abandonment had already been recorded.
    /// </summary>
    private static GateOutcome MapKind(string kind) => kind switch
    {
        "success" => GateOutcome.Success,
        "abandoned-with-late-outcome" => GateOutcome.Success,
        "decline" => GateOutcome.Declined,
        "expired" => GateOutcome.Expired,
        "abandoned" => GateOutcome.Abandoned,
        _ => GateOutcome.Pending
    };

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, object?>?> FetchClaimsAsync(
        Guid requestId, string? claimsFetchToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(claimsFetchToken))
        {
            logger.LogWarning(
                "No ClaimsFetchToken for {RequestId}; disclosed claims cannot be fetched", requestId);
            return null;
        }

        try
        {
            var url = $"/api/presentations/{requestId:D}/disclosed-claims"
                    + $"?token={Uri.EscapeDataString(claimsFetchToken)}";
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Disclosed-claims fetch for {RequestId} returned {Status}",
                    requestId, response.StatusCode);
                return null;
            }

            var body = await response.Content
                .ReadFromJsonAsync<DisclosedClaimsResponse>(JsonDefaults.Api, ct)
                .ConfigureAwait(false);

            if (body is null || !string.Equals(body.Status, "success", StringComparison.Ordinal))
            {
                logger.LogWarning("Disclosed claims for {RequestId} came back as {Status}",
                    requestId, body?.Status ?? "(null)");
                return null;
            }

            return body.Claims;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Disclosed-claims fetch failed for {RequestId}", requestId);
            return null;
        }
    }
}
