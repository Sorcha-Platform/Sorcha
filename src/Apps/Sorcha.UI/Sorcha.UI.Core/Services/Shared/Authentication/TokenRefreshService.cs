// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Sorcha.UI.Core.Services.Configuration;

namespace Sorcha.UI.Core.Services.Authentication;

/// <summary>
/// Proactive background token refresh service.
/// Schedules a timer to fire 5 minutes before token expiry and handles
/// tab visibility changes to refresh stale tokens when the user returns.
/// </summary>
public class TokenRefreshService : ITokenRefreshService
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ITokenCache _tokenCache;
    private readonly IConfigurationService _configurationService;
    private readonly CustomAuthenticationStateProvider _authStateProvider;
    private readonly ILogger<TokenRefreshService> _logger;

    private Timer? _refreshTimer;
    private DotNetObjectReference<TokenRefreshService>? _dotNetRef;
    private IJSRuntime? _jsRuntime;
    private bool _disposed;

    /// <summary>
    /// Minimum delay before scheduling a refresh (prevents immediate re-fire).
    /// </summary>
    internal static readonly TimeSpan MinRefreshDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How far before expiry to schedule the refresh.
    /// </summary>
    internal static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);

    public TokenRefreshService(
        IAuthenticationService authenticationService,
        ITokenCache tokenCache,
        IConfigurationService configurationService,
        CustomAuthenticationStateProvider authStateProvider,
        ILogger<TokenRefreshService> logger)
    {
        _authenticationService = authenticationService;
        _tokenCache = tokenCache;
        _configurationService = configurationService;
        _authStateProvider = authStateProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;

        // Register JS visibility change callback
        _dotNetRef = DotNetObjectReference.Create(this);
        try
        {
            await _jsRuntime.InvokeVoidAsync("TokenLifecycle.register", _dotNetRef);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register token lifecycle JS interop — tab visibility detection disabled");
        }

        // Schedule first refresh based on current token
        await ScheduleNextRefreshAsync();
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        _refreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _refreshTimer?.Dispose();
        _refreshTimer = null;

        if (_jsRuntime != null && _dotNetRef != null)
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("TokenLifecycle.unregister");
            }
            catch
            {
                // Best-effort cleanup — JS context may already be gone
            }
        }

        _dotNetRef?.Dispose();
        _dotNetRef = null;
    }

    /// <summary>
    /// Called from JS when the tab becomes visible again.
    /// </summary>
    [JSInvokable]
    public async Task OnTabVisible()
    {
        if (_disposed) return;

        _logger.LogDebug("Tab became visible — checking token state");
        await RefreshAndRescheduleAsync();
    }

    /// <summary>
    /// Reads the current token and schedules the timer to fire at (expiresAt - 5 min).
    /// If the token is already expired or near-expiry, performs an immediate refresh first.
    /// </summary>
    internal async Task ScheduleNextRefreshAsync()
    {
        try
        {
            var profileName = await _configurationService.GetActiveProfileNameAsync();
            var entry = await _tokenCache.GetTokenAsync(profileName);

            if (entry == null || string.IsNullOrEmpty(entry.RefreshToken))
            {
                // No token or no refresh token — nothing to schedule
                _refreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            if (entry.IsExpired || entry.IsNearExpiration)
            {
                // Already expired/near-expiry — refresh immediately then reschedule
                await RefreshAndRescheduleAsync();
                return;
            }

            var delay = entry.ExpiresAt - DateTime.UtcNow - RefreshBuffer;
            if (delay < MinRefreshDelay)
                delay = MinRefreshDelay;

            _logger.LogDebug("Scheduling token refresh in {Delay}", delay);

            if (_refreshTimer == null)
            {
                _refreshTimer = new Timer(OnTimerElapsed, null, delay, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _refreshTimer.Change(delay, Timeout.InfiniteTimeSpan);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to schedule token refresh");
        }
    }

    private void OnTimerElapsed(object? state)
    {
        if (_disposed) return;

        // Fire-and-forget on the thread pool — timer callback can't be async
        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshAndRescheduleAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background token refresh failed");
            }
        });
    }

    /// <summary>
    /// Attempts a token refresh if needed, then schedules the next timer tick.
    /// This method never recurses — it reads token state, optionally refreshes, then schedules.
    /// </summary>
    private async Task RefreshAndRescheduleAsync()
    {
        try
        {
            var profileName = await _configurationService.GetActiveProfileNameAsync();
            var entry = await _tokenCache.GetTokenAsync(profileName);

            if (entry == null || string.IsNullOrEmpty(entry.RefreshToken))
                return;

            // If the token is still valid and not near expiry, just schedule the timer
            if (!entry.IsExpired && !entry.IsNearExpiration)
            {
                ScheduleTimerForToken(entry);
                return;
            }

            _logger.LogInformation("Refreshing token (expired={IsExpired}, nearExpiry={IsNearExpiration})",
                entry.IsExpired, entry.IsNearExpiration);

            var success = await _authenticationService.RefreshTokenAsync(profileName);

            if (success)
            {
                _logger.LogInformation("Background token refresh succeeded");
                _authStateProvider.NotifyAuthenticationStateChanged();

                // Re-read token to schedule timer for the new expiry
                var newEntry = await _tokenCache.GetTokenAsync(profileName);
                if (newEntry != null && !string.IsNullOrEmpty(newEntry.RefreshToken))
                {
                    ScheduleTimerForToken(newEntry);
                }
            }
            else
            {
                _logger.LogWarning("Background token refresh failed — user will be redirected on next API call");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during background token refresh");
        }
    }

    /// <summary>
    /// Schedules the timer based on a token's expiry. Does not check or refresh.
    /// </summary>
    private void ScheduleTimerForToken(Models.Authentication.TokenCacheEntry entry)
    {
        var delay = entry.ExpiresAt - DateTime.UtcNow - RefreshBuffer;
        if (delay < MinRefreshDelay)
            delay = MinRefreshDelay;

        _logger.LogDebug("Scheduling token refresh in {Delay}", delay);

        if (_refreshTimer == null)
        {
            _refreshTimer = new Timer(OnTimerElapsed, null, delay, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _refreshTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _refreshTimer?.Dispose();
        _refreshTimer = null;
        _dotNetRef?.Dispose();
        _dotNetRef = null;
    }
}
