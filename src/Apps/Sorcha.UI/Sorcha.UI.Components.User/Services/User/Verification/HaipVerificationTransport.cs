// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.UI.Components.User.Models.Verification;

namespace Sorcha.UI.Components.User.Services.Verification;

/// <summary>
/// Live HAIP-backed implementation of <see cref="IVerificationTransport"/> (Feature 164, B3 US1).
/// Creates OID4VP presentation requests via the HAIP verifier service, renders a QR deep link,
/// polls for the holder's submission, and returns the raw <c>vp_token</c> on completion for
/// client-side verdict computation. Single implementation shared by both host apps — per-host
/// variation is encapsulated in the injected <see cref="IVerifierIdentityProvider"/>.
/// WASM-safe — no server-only types.
/// </summary>
public sealed class HaipVerificationTransport : IVerificationTransport
{
    private readonly IHaipVerifierClient _client;
    private readonly IVerifierIdentityProvider _identityProvider;
    private readonly ILogger<HaipVerificationTransport> _logger;

    /// <summary>Initialises the transport.</summary>
    public HaipVerificationTransport(
        IHaipVerifierClient client,
        IVerifierIdentityProvider identityProvider,
        ILogger<HaipVerificationTransport> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<VerificationSessionStarted> StartSessionAsync(
        VerificationPreset question,
        CancellationToken ct = default)
    {
        var session = await StartAsync(question, ct);
        return new VerificationSessionStarted(
            SessionId: session.SessionId,
            QrDeepLink: session.QrDeepLink,
            Purpose: question.Purpose,
            RequiredVct: question.RequiredVct);
    }

    /// <inheritdoc />
    public async Task<VerificationSessionPoll> PollSessionAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        var session = await PollAsync(sessionId, ct);
        var isComplete = session.State == VerificationSessionState.Complete;
        var isTerminal = session.State != VerificationSessionState.Pending;
        return new VerificationSessionPoll(
            IsComplete: isComplete,
            VpToken: session.VpToken,
            PresentationSubmission: null,
            IsTerminal: isTerminal);
    }

    /// <summary>
    /// Starts a verification session by obtaining the verifier identity and creating a HAIP
    /// presentation request. Returns a <see cref="VerificationSession"/> with
    /// <see cref="VerificationSessionState.Pending"/> state and the QR deep link.
    /// </summary>
    public async Task<VerificationSession> StartAsync(
        VerificationPreset question,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var clientId = await _identityProvider.GetClientIdAsync(ct);

        _logger.LogInformation(
            "Creating HAIP presentation request for credential type {CredentialType} with client id {ClientId}.",
            question.RequiredVct, clientId);

        try
        {
            var result = await _client.CreateRequestAsync(
                clientId,
                question.RequiredVct,
                question.RequiredClaims,
                ct);

            _logger.LogInformation(
                "Created presentation request {RequestId} for credential type {CredentialType}.",
                result.RequestId, question.RequiredVct);

            return new VerificationSession(
                SessionId: result.RequestId,
                QrDeepLink: result.AuthorizationRequestUri,
                State: VerificationSessionState.Pending);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("StartAsync cancelled for credential type {CredentialType}.", question.RequiredVct);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create presentation request for credential type {CredentialType}.", question.RequiredVct);
            return new VerificationSession(
                SessionId: "",
                QrDeepLink: "",
                State: VerificationSessionState.Error,
                Error: ex.Message);
        }
    }

    /// <summary>
    /// Polls the HAIP verifier for the holder's submission. Maps the server-side state string
    /// to <see cref="VerificationSessionState"/>. Returns <see cref="VerificationSessionState.Complete"/>
    /// with the raw <c>vp_token</c> once the holder has submitted.
    /// </summary>
    public async Task<VerificationSession> PollAsync(string sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        _logger.LogDebug("Polling HAIP verifier for session {SessionId}.", sessionId);

        try
        {
            var pollResult = await _client.PollResultAsync(sessionId, ct);

            var state = MapState(pollResult.State);

            if (state == VerificationSessionState.Complete)
            {
                _logger.LogInformation(
                    "Session {SessionId} completed; vp_token received ({TokenLength} chars).",
                    sessionId, pollResult.VpToken?.Length ?? 0);
            }
            else if (state == VerificationSessionState.Expired)
            {
                _logger.LogWarning("Session {SessionId} has expired.", sessionId);
            }

            return new VerificationSession(
                SessionId: sessionId,
                QrDeepLink: "",
                State: state,
                VpToken: state == VerificationSessionState.Complete ? pollResult.VpToken : null,
                Error: state == VerificationSessionState.Error
                    ? $"Unexpected state from verifier: {pollResult.State}"
                    : null);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("PollAsync cancelled for session {SessionId}.", sessionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Poll fault for session {SessionId}.", sessionId);
            return new VerificationSession(
                SessionId: sessionId,
                QrDeepLink: "",
                State: VerificationSessionState.Error,
                Error: ex.Message);
        }
    }

    private static VerificationSessionState MapState(string? state) => state switch
    {
        null or "Pending" or "Submitted" => VerificationSessionState.Pending,
        "Verified" => VerificationSessionState.Complete,
        "Expired" or "Cancelled" => VerificationSessionState.Expired,
        _ => VerificationSessionState.Error  // Denied + unknown states
    };
}
