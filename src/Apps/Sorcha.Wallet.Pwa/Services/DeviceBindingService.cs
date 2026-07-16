// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sorcha.CitizenWallet.Abstractions.Constants;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.ServiceClients.CitizenWallet;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.UI.Core.Services.HolderKeys;
using Sorcha.Wallet.Pwa.Services.Applications;
using Sorcha.Wallet.Pwa.Services.Catalogue;
using Sorcha.Wallet.Pwa.Services.Presentation;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// #1195 Phase 2 (Task 6) — the "Bind to device" flow behind the AIAS root credential's
/// ID-card action. Orchestrates: capture the device public JWK → start + submit the
/// device-registration blueprint's gated starting action (which mints the F127
/// presentation request) → present the holder-<c>cnf</c> root SERVER-CUSTODY (the
/// wallet service signs the KB-JWT via <c>POST /api/v1/wallet/presentations/sign-kb</c>;
/// the holder private key never reaches the device) → bounded wait for the device-<c>cnf</c>
/// copy to arrive through the F114 sync path.
/// </summary>
public interface IDeviceBindingService
{
    /// <summary>
    /// Prime the root/copy discriminator: resolves this device's key thumbprint and a
    /// snapshot of the cached credentials. Idempotent; safe to call on page load. A device
    /// without a usable key (non-PWA host) primes to a state where <see cref="CanBind"/> is
    /// always false — device binding is phone-only by design.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// True when <paramref name="credential"/> is an AssuredIdentityCredential ROOT
    /// (holder-<c>cnf</c> — its <c>cnf.jwk</c> thumbprint differs from this device's key)
    /// and THIS device has no live device-bound copy of it yet. Requires
    /// <see cref="InitializeAsync"/> to have completed; returns false before that.
    /// </summary>
    bool CanBind(CachedCredential credential);

    /// <summary>
    /// Captures the device key, presents the root (server custody), submits the
    /// device-registration blueprint, and waits (bounded) for the device-<c>cnf</c> copy to
    /// land in the local cache. Throws <see cref="DeviceBindingException"/> on any gate/mint
    /// failure — loudly and distinguishably (surface inline); nothing is cached on failure.
    /// A <see cref="DeviceBindingFailure.DeliveryTimeout"/> is an explicit PENDING outcome:
    /// the presentation succeeded but the copy hasn't synced yet.
    /// </summary>
    Task<CachedCredential> BindToThisDeviceAsync(CachedCredential root, CancellationToken ct);
}

/// <summary>Which leg of the bind flow failed — drives distinguishable UI copy.</summary>
public enum DeviceBindingFailure
{
    /// <summary>The device key is unavailable (non-PWA host / WebCrypto bridge failure).</summary>
    DeviceKeyUnavailable,
    /// <summary>A supporting call (catalogue, instance-create, holder keys) failed — transient.</summary>
    ServiceUnavailable,
    /// <summary>The device-registration blueprint is not published on this service.</summary>
    BlueprintNotFound,
    /// <summary>The starting action's form context could not be loaded.</summary>
    FormLoadFailed,
    /// <summary>The starting-action submission was rejected (validation / server error).</summary>
    SubmitRejected,
    /// <summary>A policy said no (409 — e.g. the device cap could not be honoured). Retrying cannot change the answer.</summary>
    PolicyRefused,
    /// <summary>The server accepted the action but did not start the identity check (SorchaWallet presentation initiation missing server-side).</summary>
    PresentationNotInitiated,
    /// <summary>The presentation request could not be fetched/parsed.</summary>
    PresentationRequestInvalid,
    /// <summary>The root credential cannot satisfy the presentation request.</summary>
    RootNotEligible,
    /// <summary>The server-custody KB-JWT signing call failed.</summary>
    HolderSigningFailed,
    /// <summary>The presentation outcome post (direct_post) was refused.</summary>
    PresentationSubmitRejected,
    /// <summary>The presentation completed but the device copy hasn't synced within the wait window — PENDING, not failed.</summary>
    DeliveryTimeout,
}

/// <summary>
/// A named, distinguishable bind-flow failure. Verification/presentation failures are NEVER
/// silent — every leg throws with a citizen-readable message and a machine-readable
/// <see cref="Failure"/> kind, and <see cref="Retryable"/> says whether trying again can help.
/// </summary>
public sealed class DeviceBindingException : Exception
{
    /// <summary>Which leg failed.</summary>
    public DeviceBindingFailure Failure { get; }

    /// <summary>True when the failure is transient (e.g. 503) and a retry may succeed.</summary>
    public bool Retryable { get; }

    /// <summary>Initialises a new instance.</summary>
    public DeviceBindingException(
        DeviceBindingFailure failure, string message, bool retryable = false, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
        Retryable = retryable;
    }
}

/// <summary>Tuning knobs for the bounded post-presentation delivery wait.</summary>
public sealed record DeviceBindingOptions
{
    /// <summary>Delay between sync/cache polls while waiting for the device copy.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Total wait for the device copy before reporting the explicit pending outcome.</summary>
    public TimeSpan DeliveryTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>Default <see cref="IDeviceBindingService"/>.</summary>
public sealed class DeviceBindingService : IDeviceBindingService
{
    /// <summary>
    /// Published device-registration blueprint ids are <c>{templateId}-{timestamp}</c>; the
    /// stable discriminator is the template id prefix (see
    /// <c>demos/AIAS/blueprints/aias-device-registration.template.json</c>). The catalogue
    /// carries no tags/vct metadata, so the id prefix is the most robust discovery available.
    /// </summary>
    internal const string DeviceRegistrationBlueprintIdPrefix = "aias-device-registration";

    /// <summary>The blueprint slot the issuance action reads the device JWK from (<c>holderKeySourceField</c>).</summary>
    internal const string DeviceKeySlot = "/deviceKey/holderJwk";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IDeviceKeyService _deviceKeys;
    private readonly ICatalogueClient _catalogue;
    private readonly IApplicationActionClient _actions;
    private readonly IPresentationEngine _engine;
    private readonly IHolderKeyClient _holderKeyClient;
    private readonly ICitizenWalletClient _walletClient;
    private readonly ICredentialCache _cache;
    private readonly ISyncService _sync;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly ILogger<DeviceBindingService> _logger;
    private readonly DeviceBindingOptions _options;

    private bool _initialized;
    private string? _deviceThumbprint;
    private IReadOnlyList<CachedCredential> _snapshot = [];

    /// <summary>Initialises a new instance.</summary>
    public DeviceBindingService(
        IDeviceKeyService deviceKeys,
        ICatalogueClient catalogue,
        IApplicationActionClient actions,
        IPresentationEngine engine,
        IHolderKeyClient holderKeyClient,
        ICitizenWalletClient walletClient,
        ICredentialCache cache,
        ISyncService sync,
        HttpClient http,
        TimeProvider time,
        ILogger<DeviceBindingService> logger,
        DeviceBindingOptions? options = null)
    {
        _deviceKeys = deviceKeys ?? throw new ArgumentNullException(nameof(deviceKeys));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _holderKeyClient = holderKeyClient ?? throw new ArgumentNullException(nameof(holderKeyClient));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new DeviceBindingOptions();
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            _deviceThumbprint = await _deviceKeys.GetThumbprintAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A host without a usable device key (e.g. the bridge is absent) is a normal
            // answer: device binding is phone-only. CanBind stays false; never a crash.
            _logger.LogInformation(ex, "Device key unavailable — device binding disabled on this host.");
            _deviceThumbprint = null;
        }

        try
        {
            _snapshot = await _cache.ListAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Credential snapshot unavailable — CanBind will answer false.");
            _snapshot = [];
        }

        _initialized = true;
    }

    /// <inheritdoc />
    public bool CanBind(CachedCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        if (!_initialized || _deviceThumbprint is null) return false;
        if (credential.Format != CachedCredentialFormat.SdJwtVc) return false;
        // vct matching is case-sensitive Ordinal (VCT decoupling, #1187).
        if (!string.Equals(credential.Vct, VctUris.AssuredIdentityV1, StringComparison.Ordinal)) return false;

        // Root discriminator: the root's cnf is the HOLDER key; a device-bound copy's cnf
        // is THIS device's key. No readable cnf ⇒ we cannot prove it is a root ⇒ not bindable.
        if (!TryGetCnfJwkThumbprint(credential.RawSdJwt, out var cnfThumbprint)) return false;
        if (string.Equals(cnfThumbprint, _deviceThumbprint, StringComparison.Ordinal)) return false;

        // Already bound: any OTHER live AIAS credential whose cnf is this device's key.
        foreach (var other in _snapshot)
        {
            if (other.Id == credential.Id) continue;
            if (!string.Equals(other.Vct, VctUris.AssuredIdentityV1, StringComparison.Ordinal)) continue;
            if (TryGetCnfJwkThumbprint(other.RawSdJwt, out var otherThumbprint) &&
                string.Equals(otherThumbprint, _deviceThumbprint, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<CachedCredential> BindToThisDeviceAsync(CachedCredential root, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(root);

        // ── 1. Capture: this device's public JWK + thumbprint ────────────────────────────
        JsonElement deviceJwk;
        string deviceThumbprint;
        try
        {
            deviceJwk = await _deviceKeys.GetPublicJwkAsync(ct);
            deviceThumbprint = await _deviceKeys.GetThumbprintAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new DeviceBindingException(
                DeviceBindingFailure.DeviceKeyUnavailable,
                "This device's key isn't available, so it can't be bound. Device binding only works in the wallet app.",
                retryable: false, ex);
        }

        // ── 2. Discover the device-registration service + start an instance ──────────────
        IReadOnlyList<CatalogueItem> services;
        try
        {
            services = await _catalogue.GetServicesAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new DeviceBindingException(
                DeviceBindingFailure.ServiceUnavailable,
                "Couldn't reach the service catalogue to start device binding. Check your connection and try again.",
                retryable: true, ex);
        }

        var item = services.FirstOrDefault(s =>
            s.BlueprintId.StartsWith(DeviceRegistrationBlueprintIdPrefix, StringComparison.Ordinal));
        if (item is null)
        {
            throw new DeviceBindingException(
                DeviceBindingFailure.BlueprintNotFound,
                "Device binding isn't available on this service yet (the device-registration workflow is not published).",
                retryable: false);
        }

        var instanceIdRaw = await _catalogue.StartAsync(item, ct);
        if (!Guid.TryParse(instanceIdRaw, out var instanceId))
        {
            throw new DeviceBindingException(
                DeviceBindingFailure.ServiceUnavailable,
                "The device-binding workflow could not be started. Try again in a moment.",
                retryable: true);
        }

        // ── 3. Load + submit the gated starting action (device JWK payload) ───────────────
        // Submitting the SorchaWallet-gated action is what MINTS the presentation request
        // (F127/F111) — so the submit leg precedes the present leg by construction.
        var load = await _actions.LoadFormAsync(instanceId, ct);
        if (load.Status != ApplicationFormLoadStatus.Loaded || load.Context is null)
        {
            throw new DeviceBindingException(
                DeviceBindingFailure.FormLoadFailed,
                load.Status == ApplicationFormLoadStatus.NetworkError
                    ? "Couldn't reach the service to prepare device binding. Check your connection and try again."
                    : $"The device-binding workflow could not be prepared ({load.Status}).",
                retryable: load.Status == ApplicationFormLoadStatus.NetworkError);
        }

        var formData = new Dictionary<string, object?> { [DeviceKeySlot] = deviceJwk };
        var submit = await _actions.SubmitAsync(load.Context, formData, ct);
        if (submit.Status != ApplicationSubmissionStatus.Success)
        {
            if (string.Equals(submit.ErrorCode, "HTTP_409", StringComparison.Ordinal))
            {
                throw new DeviceBindingException(
                    DeviceBindingFailure.PolicyRefused,
                    "The service refused this binding (the device limit could not be honoured, or a binding is already in flight). Retrying won't change the answer.",
                    retryable: false);
            }

            var transient = submit.Status == ApplicationSubmissionStatus.ServerError;
            throw new DeviceBindingException(
                DeviceBindingFailure.SubmitRejected,
                $"The device-binding request was rejected{(submit.ErrorDetail is null ? "." : $": {submit.ErrorDetail}")}"
                    + (transient ? " Try again in a moment." : string.Empty),
                retryable: transient);
        }

        if (!submit.AwaitingPresentation || string.IsNullOrEmpty(submit.PresentationRequestUri))
        {
            // Named server gap — NEVER silent. The SorchaWallet presentation initiation is not
            // wired on the action-execute path yet; without it no identity check ever starts.
            throw new DeviceBindingException(
                DeviceBindingFailure.PresentationNotInitiated,
                "The service accepted the request but did not start the identity check, so nothing was bound. " +
                "(Server-side: SorchaWallet presentation initiation is not enabled on action submission.)",
                retryable: false);
        }

        // ── 4. Present the ROOT, server-custody ───────────────────────────────────────────
        ParsedPresentationRequest request;
        try
        {
            request = await _engine.ParseAsync(submit.PresentationRequestUri, FetchRequestObjectAsync, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new DeviceBindingException(
                DeviceBindingFailure.PresentationRequestInvalid,
                $"The identity check could not be started: {ex.Message}",
                retryable: false, ex);
        }

        var matches = _engine.Match(request, [root]);
        if (matches.Count == 0)
        {
            throw new DeviceBindingException(
                DeviceBindingFailure.RootNotEligible,
                "Your Assured Identity credential can't satisfy the identity check this service asked for, so nothing was bound.",
                retryable: false);
        }
        var match = matches[0];

        var holderKeys = await _holderKeyClient.GetHolderKeysAsync(ct);
        if (holderKeys is null)
        {
            throw new DeviceBindingException(
                DeviceBindingFailure.ServiceUnavailable,
                "Couldn't resolve your wallet's holder key for the identity check. Try again in a moment.",
                retryable: true);
        }

        // Full disclosure of the root (design §4.1): the bind flow is automated — the citizen
        // is not hand-picking claims — so every disclosable claim is approved. The engine
        // validates that the approved set covers the request's required claims.
        var approved = root.AvailableClaimNames
            .Union(match.SatisfiedRequired, StringComparer.Ordinal)
            .ToList();

        string vpToken;
        try
        {
            vpToken = await _engine.BuildVpTokenAsync(
                match, approved, request, holderKeys.HolderJwk, SignWithHolderKeyAsync, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (DeviceBindingException) { throw; }
        catch (Exception ex)
        {
            throw new DeviceBindingException(
                DeviceBindingFailure.HolderSigningFailed,
                $"The identity check could not be signed: {ex.Message}",
                retryable: false, ex);
        }

        // ── 5. direct_post the outcome to the request's response_uri ──────────────────────
        // Wire shape per SorchaWalletVerificationPayload: JSON { vpToken } (the compact
        // SD-JWT presentation; the sorcha-wallet consumer validates it server-side).
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(
                request.ResponseUri, new SorchaWalletOutcomePost(vpToken), JsonOptions, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new DeviceBindingException(
                DeviceBindingFailure.PresentationSubmitRejected,
                "The identity check couldn't be submitted — the service was unreachable. Try again in a moment.",
                retryable: true, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var transient = status is 502 or 503 or 504;
            throw new DeviceBindingException(
                DeviceBindingFailure.PresentationSubmitRejected,
                $"The identity check was refused (HTTP {status})." +
                (transient ? " This is temporary — try again in a moment." : " Nothing was bound."),
                retryable: transient);
        }

        _logger.LogInformation(
            "Device-binding presentation submitted for instance {InstanceId}; awaiting device-cnf copy (requestId {RequestId})",
            instanceId, submit.PresentationRequestId);

        // ── 6. Bounded wait: the copy arrives async via the F114 sync path ────────────────
        var deadline = _time.GetUtcNow() + _options.DeliveryTimeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await _sync.SyncAsync(ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // A single failed sync poll is not fatal; the deadline bounds the loop.
                _logger.LogWarning(ex, "Sync poll failed while waiting for the device-bound copy; will retry.");
            }

            var all = await _cache.ListAsync(ct);
            var copy = all.FirstOrDefault(c =>
                c.Id != root.Id &&
                string.Equals(c.Vct, VctUris.AssuredIdentityV1, StringComparison.Ordinal) &&
                TryGetCnfJwkThumbprint(c.RawSdJwt, out var thumbprint) &&
                string.Equals(thumbprint, deviceThumbprint, StringComparison.Ordinal));
            if (copy is not null)
            {
                _snapshot = all; // keep CanBind's view current — the root is now bound.
                return copy;
            }

            if (_time.GetUtcNow() >= deadline)
            {
                // Explicit PENDING outcome — a timeout must never present as success OR as a
                // dead generic error: the presentation succeeded and the mint may still land.
                throw new DeviceBindingException(
                    DeviceBindingFailure.DeliveryTimeout,
                    "Your identity was verified, but the device-bound copy is still processing. " +
                    "It should appear in your wallet shortly — check back in a minute.",
                    retryable: false);
            }

            await Task.Delay(_options.PollInterval, _time, ct);
        }
    }

    /// <summary>
    /// The server-custody holder signer handed to the presentation engine: round-trips the
    /// KB-JWT signing input through <c>POST /api/v1/wallet/presentations/sign-kb</c> (Task 6a).
    /// </summary>
    private async Task<byte[]> SignWithHolderKeyAsync(byte[] signingInput, CancellationToken ct)
    {
        try
        {
            var response = await _walletClient.SignKbJwtAsync(
                new KbJwtSignRequest { SigningInput = Encoding.ASCII.GetString(signingInput) }, ct);
            return Base64Url.DecodeFromChars(response.Signature);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new DeviceBindingException(
                DeviceBindingFailure.HolderSigningFailed,
                "Your wallet couldn't sign the identity check (server-custody signing failed). Try again in a moment.",
                retryable: true, ex);
        }
    }

    /// <summary>Fetches the OpenID4VP request object (anonymous GET) for <see cref="IPresentationEngine.ParseAsync"/>.</summary>
    private async Task<string> FetchRequestObjectAsync(string url, CancellationToken ct)
    {
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Reads the credential's <c>cnf.jwk</c> from the SD-JWT payload and computes its RFC 7638
    /// thumbprint (EC P-256 and OKP Ed25519). False when the credential carries no readable cnf.
    /// </summary>
    internal static bool TryGetCnfJwkThumbprint(string rawSdJwt, out string thumbprint)
    {
        thumbprint = string.Empty;
        if (string.IsNullOrWhiteSpace(rawSdJwt)) return false;

        try
        {
            var jwt = rawSdJwt.Split('~')[0];
            var parts = jwt.Split('.');
            if (parts.Length < 2) return false;

            using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            if (!payload.RootElement.TryGetProperty("cnf", out var cnf) ||
                cnf.ValueKind != JsonValueKind.Object ||
                !cnf.TryGetProperty("jwk", out var jwk) ||
                jwk.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            thumbprint = PresentationEngine.ComputeJwkThumbprint(jwk);
            return thumbprint.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The documented sorcha-wallet outcome wire shape (server: <c>SorchaWalletVerificationPayload</c>).
    /// No delegation credential — Phase 2 presents the root server-custody, delegation-free.
    /// </summary>
    private sealed record SorchaWalletOutcomePost([property: JsonPropertyName("vpToken")] string VpToken);
}
