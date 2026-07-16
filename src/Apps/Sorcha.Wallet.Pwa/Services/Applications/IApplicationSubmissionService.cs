// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Components.User.Services.Signing;
using Sorcha.Wallet.Pwa.Services.Context;

namespace Sorcha.Wallet.Pwa.Services.Applications;

/// <summary>
/// Submits a council application's action payload on behalf of the
/// signed-in user (Feature 125, T057). Signs the payload via
/// <see cref="IUserSigner"/> under the active context and hands off to the
/// blueprint instance endpoint. The full HTTP path lands in a follow-up
/// alongside the application catalogue; v1 returns a structured result so
/// the UI can render the in-progress state immediately.
/// </summary>
public interface IApplicationSubmissionService
{
    /// <summary>Submit a signed application payload; returns the outcome.</summary>
    Task<ApplicationSubmissionResult> SubmitAsync(
        ApplicationSubmissionRequest request,
        CancellationToken ct = default);
}

/// <summary>Input shape for the submission service.</summary>
/// <param name="BlueprintId">The blueprint being applied to.</param>
/// <param name="ApplicationLabel">User-facing label (e.g. "Driving Licence") — surfaced in the pending-app notice.</param>
/// <param name="ActionId">The action id within the blueprint instance being submitted.</param>
/// <param name="Payload">The canonicalised JSON payload bytes to sign.</param>
public sealed record ApplicationSubmissionRequest(
    Guid BlueprintId,
    string ApplicationLabel,
    int ActionId,
    byte[] Payload);

/// <summary>Outcome of a submission.</summary>
/// <param name="Status">Submission outcome class.</param>
/// <param name="InstanceId">Created blueprint-instance id on success; null otherwise.</param>
/// <param name="ErrorCode">Stable error code on failure; null on success.</param>
/// <param name="ErrorDetail">Human-readable diagnostic on failure; null on success.</param>
/// <param name="AwaitingPresentation">
/// #1195 Phase 2 — true when the submitted action is gated on a credential presentation
/// (F111): the action has NOT completed; the server minted a presentation request the
/// wallet must now fulfil. Mirrors <c>ActionSubmissionResponse.AwaitingPresentation</c>.
/// </param>
/// <param name="PresentationRequestId">The F111 presentation request id, when awaiting.</param>
/// <param name="PresentationRequestUri">The <c>openid4vp://…</c> authorization request URI, when awaiting.</param>
public sealed record ApplicationSubmissionResult(
    ApplicationSubmissionStatus Status,
    Guid? InstanceId,
    string? ErrorCode,
    string? ErrorDetail,
    bool AwaitingPresentation = false,
    Guid? PresentationRequestId = null,
    string? PresentationRequestUri = null);

/// <summary>Submission outcome class — drives UI branching.</summary>
public enum ApplicationSubmissionStatus
{
    /// <summary>Action accepted by the blueprint service; in-progress state shown on Home.</summary>
    Success,
    /// <summary>Validation failed before signing; form should re-render with errors.</summary>
    ValidationFailed,
    /// <summary>Signing failed (e.g. device key error); user-safe recovery path.</summary>
    SigningFailed,
    /// <summary>Server returned a non-success status; transient — caller can retry.</summary>
    ServerError
}

/// <summary>
/// v1 stub <see cref="IApplicationSubmissionService"/>. Signs the payload
/// via <see cref="IUserSigner"/> so the seam is exercised end-to-end, then
/// returns a synthetic <see cref="ApplicationSubmissionStatus.Success"/>
/// alongside a deterministic instance id derived from the request. The
/// real submission (blueprint instance create + action submit) lands in
/// a follow-up; this implementation lets the UI flow ship and proves the
/// IUserSigner integration end-to-end.
/// </summary>
public sealed class StubApplicationSubmissionService : IApplicationSubmissionService
{
    private readonly IUserSigner _signer;
    private readonly IUserContext _userContext;
    private readonly ILogger<StubApplicationSubmissionService> _logger;

    /// <summary>Initialise a new stub.</summary>
    public StubApplicationSubmissionService(
        IUserSigner signer,
        IUserContext userContext,
        ILogger<StubApplicationSubmissionService> logger)
    {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ApplicationSubmissionResult> SubmitAsync(
        ApplicationSubmissionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Payload is null || request.Payload.Length == 0)
        {
            return new ApplicationSubmissionResult(
                ApplicationSubmissionStatus.ValidationFailed,
                InstanceId: null,
                ErrorCode: "ERR_APPSUBMIT_EMPTY_PAYLOAD",
                ErrorDetail: "The application has no data to submit. Fill in the form and try again.");
        }

        var signing = await _signer.SignAsync(
            new SigningRequest(
                Operation: SigningOperation.ActionSubmission,
                PayloadToSign: request.Payload,
                AudienceClientId: null,
                ActiveContextOrgId: _userContext.ActiveContextOrgId),
            ct).ConfigureAwait(false);

        if (!signing.Success)
        {
            _logger.LogWarning(
                "Application submission signing failed: {Code} {Detail}",
                signing.ErrorCode, signing.ErrorDetail);
            return new ApplicationSubmissionResult(
                ApplicationSubmissionStatus.SigningFailed,
                InstanceId: null,
                ErrorCode: signing.ErrorCode ?? "ERR_APPSUBMIT_SIGNING_FAILED",
                ErrorDetail: signing.ErrorDetail
                    ?? "Couldn't sign the application on this device. Try again or sign in on another device.");
        }

        // Real HTTP submission lands in the follow-up; for now we synthesise
        // a stable instance id so the UI can move to in-progress state and
        // the pending-app notice surface picks it up via the F124 mechanism.
        var instanceId = SynthesiseInstanceId(request);
        _logger.LogInformation(
            "Application submission signed locally (stub): blueprint {Blueprint}, action {Action}, instance {Instance}.",
            request.BlueprintId, request.ActionId, instanceId);
        return new ApplicationSubmissionResult(
            ApplicationSubmissionStatus.Success,
            InstanceId: instanceId,
            ErrorCode: null,
            ErrorDetail: null);
    }

    private static Guid SynthesiseInstanceId(ApplicationSubmissionRequest request)
    {
        // Deterministic per (BlueprintId, ActionId, payload hash). Stable
        // enough for UI continuity across retries while the real submission
        // path isn't wired.
        using var sha = System.Security.Cryptography.SHA256.Create();
        var seed = Encoding.UTF8.GetBytes($"{request.BlueprintId:N}/{request.ActionId}/");
        var bytes = new byte[seed.Length + request.Payload.Length];
        Buffer.BlockCopy(seed, 0, bytes, 0, seed.Length);
        Buffer.BlockCopy(request.Payload, 0, bytes, seed.Length, request.Payload.Length);
        var hash = sha.ComputeHash(bytes);
        var guidBytes = new byte[16];
        Buffer.BlockCopy(hash, 0, guidBytes, 0, 16);
        return new Guid(guidBytes);
    }
}
