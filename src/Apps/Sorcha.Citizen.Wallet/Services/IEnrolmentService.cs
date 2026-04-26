// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.ServiceClients.CitizenWallet;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// Orchestrates the citizen wallet enrolment ceremony (Feature 114, T109
/// part 2). Composes <see cref="IDeviceKeyService"/>,
/// <see cref="ICitizenWalletClient.EnrolDeviceAsync"/>,
/// <see cref="IDelegationStore"/>, and <see cref="ISyncService"/> into one
/// idempotent call so the EnrolmentWizard UI stays focused on the user
/// interaction and so the same ceremony can be re-run on, e.g., delegation
/// renewal.
/// </summary>
public interface IEnrolmentService
{
    /// <summary>
    /// Generates the device key (if absent), enrols the device with the
    /// server, persists the issued delegation locally, and pulls the initial
    /// credential snapshot.
    /// </summary>
    /// <param name="deviceLabel">Citizen-editable device label, 1..120 chars.</param>
    /// <param name="platform">Free-form platform descriptor, e.g. "iOS 19 / Safari 19". ≤120 chars.</param>
    /// <param name="userAgent">Raw user agent at enrolment, ≤512 chars.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<EnrolmentResult> EnrolAsync(
        string deviceLabel,
        string platform,
        string userAgent,
        CancellationToken ct = default);
}

/// <summary>Outcome of one enrolment ceremony.</summary>
/// <param name="DeviceId">Server-assigned device id (also stored client-side as enrolment metadata).</param>
/// <param name="DelegationExpiresAt">UTC expiry of the issued delegation credential.</param>
/// <param name="CredentialsLoaded">Number of credentials pulled by the initial snapshot.</param>
public sealed record EnrolmentResult(Guid DeviceId, DateTimeOffset DelegationExpiresAt, int CredentialsLoaded);

/// <summary>Default <see cref="IEnrolmentService"/>.</summary>
public sealed class EnrolmentService : IEnrolmentService
{
    private readonly IDeviceKeyService _deviceKeys;
    private readonly ICitizenWalletClient _walletClient;
    private readonly IDelegationStore _delegations;
    private readonly IDeviceMetaStore _deviceMeta;
    private readonly ISyncService _sync;
    private readonly ICredentialCache _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<EnrolmentService> _logger;

    /// <summary>Initialises a new instance.</summary>
    public EnrolmentService(
        IDeviceKeyService deviceKeys,
        ICitizenWalletClient walletClient,
        IDelegationStore delegations,
        IDeviceMetaStore deviceMeta,
        ISyncService sync,
        ICredentialCache cache,
        TimeProvider clock,
        ILogger<EnrolmentService> logger)
    {
        _deviceKeys = deviceKeys ?? throw new ArgumentNullException(nameof(deviceKeys));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _delegations = delegations ?? throw new ArgumentNullException(nameof(delegations));
        _deviceMeta = deviceMeta ?? throw new ArgumentNullException(nameof(deviceMeta));
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<EnrolmentResult> EnrolAsync(
        string deviceLabel,
        string platform,
        string userAgent,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceLabel);
        if (deviceLabel.Length > 120) throw new ArgumentException("Label must be ≤ 120 characters.", nameof(deviceLabel));

        // 1. Generate (or reuse) the non-extractable device key in WebCrypto.
        var jwkElement = await _deviceKeys.GetPublicJwkAsync(ct);
        var jwk = ParseJwk(jwkElement);

        // 2. Call /devices/enrol with the public JWK.
        var request = new DeviceEnrolmentRequest
        {
            DeviceLabel = deviceLabel.Trim(),
            DevicePublicJwk = jwk,
            Platform = (platform ?? string.Empty).Trim(),
            UserAgent = (userAgent ?? string.Empty).Trim(),
        };
        var response = await _walletClient.EnrolDeviceAsync(request, ct);
        _logger.LogInformation("Device enrolled deviceId={DeviceId} jti=(in delegation) exp={ExpiresAt}",
            response.DeviceId, response.DelegationExpiresAt);

        // 3. Persist the delegation locally so offline presentations can use it.
        await _delegations.SetAsync(response.DelegationCredential, ct);

        // 3b. Persist enrolment metadata so the renewal trigger can find the
        //     deviceId + expiry without re-asking the server.
        await _deviceMeta.SetAsync(new DeviceMetaRecord(
            response.DeviceId,
            request.DeviceLabel,
            EnrolledAt: _clock.GetUtcNow(),
            response.DelegationExpiresAt), ct);

        // 4. Pull the initial credential snapshot via the sync orchestrator —
        //    it handles the cursor bootstrap on top of the snapshot fetch.
        var sync = await _sync.SyncAsync(ct);
        var loaded = (await _cache.ListAsync(ct)).Count;

        return new EnrolmentResult(response.DeviceId, response.DelegationExpiresAt, loaded);
    }

    private static EcP256PublicJwk ParseJwk(JsonElement jwk) => new()
    {
        Kty = jwk.GetProperty("kty").GetString() ?? "EC",
        Crv = jwk.GetProperty("crv").GetString() ?? "P-256",
        X = jwk.GetProperty("x").GetString() ?? string.Empty,
        Y = jwk.TryGetProperty("y", out var y) ? y.GetString() ?? string.Empty : string.Empty,
    };
}
