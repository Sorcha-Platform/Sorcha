// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;
using Sorcha.Wallet.Pwa.Services;

namespace Sorcha.Wallet.Pwa.Services.Context;

/// <summary>
/// Holds the wallet's active organisational context — the user's current
/// "I am acting as X" choice that scopes credentials, applications, persona
/// autofill, and activity history on Home (Feature 125, T066).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ActiveContextOrgId"/> null means the Personal context — the
/// user's individual citizen identity. Non-null means an organisational
/// membership the user holds. Switching to a non-Personal context posts to
/// <c>/api/auth/switch-org</c> to acquire a JWT scoped to that org;
/// switching back to Personal in v1 keeps the user's existing token
/// (Personal-as-Public-Org switch is a small follow-up).
/// </para>
/// <para>
/// The security boundary lives at the JWT, not in this client-side store.
/// Client-side filtering by context is a presentation-layer optimisation
/// only — every call still carries a bearer the server verifies.
/// </para>
/// </remarks>
public interface IUserContext
{
    /// <summary>The currently-active organisational context; null = Personal.</summary>
    Guid? ActiveContextOrgId { get; }

    /// <summary>Fires after the active context changes, with the from/to ids.</summary>
    event Func<UserContextChangedEventArgs, Task>? OnContextChanged;

    /// <summary>
    /// Load the persisted active context from <see cref="IActiveContextStore"/>
    /// on wallet boot. Idempotent — safe to call from <c>OnInitializedAsync</c>.
    /// Defaults to Personal when nothing persisted.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Switch to the requested context. Returns true on success, false if the
    /// switch was rejected (e.g., the server returned 403 because the user no
    /// longer holds the membership). On failure, <see cref="ActiveContextOrgId"/>
    /// is unchanged and no event fires.
    /// </summary>
    Task<bool> SetActiveContextAsync(Guid? orgId, CancellationToken ct = default);
}

/// <summary>Event payload for <see cref="IUserContext.OnContextChanged"/>.</summary>
/// <param name="FromContextOrgId">Previous context; null = was Personal.</param>
/// <param name="ToContextOrgId">New context; null = is Personal.</param>
public sealed record UserContextChangedEventArgs(Guid? FromContextOrgId, Guid? ToContextOrgId);

/// <summary>
/// v1 managed-mode <see cref="IUserContext"/>. Hydrates the active context
/// from the persistent store, drives <c>/api/auth/switch-org</c> + access-token
/// rotation on switch, and broadcasts the change to subscribed components
/// (Home, Activity, persona autofill).
/// </summary>
public sealed class ManagedUserContext : IUserContext
{
    private readonly IActiveContextStore _store;
    private readonly IAccessTokenStore _tokenStore;
    private readonly HttpClient _http;
    private readonly ILogger<ManagedUserContext> _logger;
    private Guid? _active;
    private bool _initialised;

    /// <summary>Initialise a new managed context.</summary>
    public ManagedUserContext(
        IActiveContextStore store,
        IAccessTokenStore tokenStore,
        HttpClient http,
        ILogger<ManagedUserContext> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Guid? ActiveContextOrgId => _active;

    /// <inheritdoc />
    public event Func<UserContextChangedEventArgs, Task>? OnContextChanged;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialised) return;
        var record = await _store.GetAsync(ct).ConfigureAwait(false);
        _active = record?.ContextOrgId;
        _initialised = true;
    }

    /// <inheritdoc />
    public async Task<bool> SetActiveContextAsync(Guid? orgId, CancellationToken ct = default)
    {
        if (!_initialised) await InitializeAsync(ct).ConfigureAwait(false);
        if (orgId == _active) return true; // no-op

        if (orgId is { } targetOrg)
        {
            // Non-Personal switch: acquire a JWT scoped to the target org.
            try
            {
                // Feature 153 — leaving Personal: snapshot the current (consumer) token as the home
                // token so we can restore personal capacity on return. switch-org cannot mint a
                // consumer token (it requires an org), so this snapshot is the only way back.
                if (_active is null)
                {
                    var current = await _tokenStore.GetAsync(ct).ConfigureAwait(false);
                    if (current is not null)
                    {
                        await _tokenStore.SetHomeAsync(current, ct).ConfigureAwait(false);
                    }
                }

                var response = await _http.PostAsJsonAsync(
                    "/api/auth/switch-org", new { OrganizationId = targetOrg }, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Context switch rejected by server: {Status} for org {OrgId}.",
                        response.StatusCode, targetOrg);
                    return false;
                }

                var payload = await response.Content
                    .ReadFromJsonAsync<SwitchOrgResponse>(JsonDefaults.Api, ct).ConfigureAwait(false);
                if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
                {
                    _logger.LogWarning("Context switch returned an empty token payload.");
                    return false;
                }

                await _tokenStore.SetAsync(
                    new AccessTokenRecord(payload.AccessToken,
                        DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn),
                        Email: null),
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Context switch to {OrgId} failed.", targetOrg);
                return false;
            }
        }
        else
        {
            // Feature 153 — returning to Personal: restore the snapshotted personal/home (consumer)
            // token so consumer-gated surfaces keep working (a platform/org token would 403 them).
            // If the home token is missing/expired, leave the current token; the normal expiry/
            // re-auth flow handles it.
            var home = await _tokenStore.GetHomeAsync(ct).ConfigureAwait(false);
            if (home is not null)
            {
                await _tokenStore.SetAsync(home, ct).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Return to Personal: no valid home token to restore; keeping current token.");
            }
        }

        var previous = _active;
        _active = orgId;
        await _store.SetAsync(new ActiveContextRecord(orgId, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);

        // Structured log stands in for the eventual `sorcha_wallet_context_switch_total{from,to}`
        // OpenTelemetry counter (Feature 125, T078). Client-side OTel
        // export from the PWA is a follow-up; for now operators can aggregate
        // these via Serilog → Aspire log pipeline.
        _logger.LogInformation("Context switch succeeded from {FromContextOrgId} to {ToContextOrgId}.", previous, orgId);

        var handler = OnContextChanged;
        if (handler is not null)
        {
            try
            {
                await handler.Invoke(new UserContextChangedEventArgs(previous, orgId)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnContextChanged subscriber threw during {From} → {To} switch.", previous, orgId);
            }
        }
        return true;
    }

    private sealed record SwitchOrgResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("expiresIn")] int ExpiresIn);
}

/// <summary>In-memory <see cref="IUserContext"/> for unit tests.</summary>
public sealed class InMemoryUserContext : IUserContext
{
    private Guid? _active;

    /// <inheritdoc />
    public Guid? ActiveContextOrgId => _active;

    /// <inheritdoc />
    public event Func<UserContextChangedEventArgs, Task>? OnContextChanged;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task<bool> SetActiveContextAsync(Guid? orgId, CancellationToken ct = default)
    {
        if (orgId == _active) return true;
        var previous = _active;
        _active = orgId;
        var handler = OnContextChanged;
        if (handler is not null)
        {
            await handler.Invoke(new UserContextChangedEventArgs(previous, orgId)).ConfigureAwait(false);
        }
        return true;
    }
}
