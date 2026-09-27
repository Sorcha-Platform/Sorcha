// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Service.Services.Interfaces;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Configuration;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Notification preference provider that resolves a person's notification preferences from the
/// Tenant Service's internal, service-authenticated route
/// <c>GET api/internal/users/{userId}/notification-preferences</c>.
/// Caches per-user results for 5 minutes to avoid per-notification API calls.
/// </summary>
/// <remarks>
/// #1694 — this used to call the USER endpoint <c>GET api/preferences?userId=</c> on an
/// unauthenticated client. That endpoint reads the caller's own JWT and has no override, so it
/// answered 401 on every call, and even an authenticated call would have missed: preferences were
/// keyed by the per-org <c>UserIdentity</c> id while the Wallet passes a wallet's <c>Owner</c>
/// (normally a <c>PlatformUser</c> id). No user's notification preferences had ever been honoured.
/// </remarks>
public sealed class TenantNotificationPreferenceProvider : INotificationPreferenceProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantNotificationPreferenceProvider> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public TenantNotificationPreferenceProvider(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<TenantNotificationPreferenceProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var serviceAddress = SorchaServiceAddresses.TryResolve(configuration, SorchaService.Tenant)
            ?? "https+http://tenant-service";

        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(serviceAddress.TrimEnd('/') + "/");
        }
    }

    /// <inheritdoc/>
    public async Task<NotificationPreferences> GetPreferencesAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"notification-prefs:{userId}";

        if (_cache.TryGetValue(cacheKey, out NotificationPreferences? cached) && cached is not null)
        {
            return cached;
        }

        // A wallet Owner that is not a person id (e.g. "validator:{id}" system wallets) has no
        // preferences to read; don't make a call that can only 404.
        if (!Guid.TryParse(userId, out var ownerId))
            return NotificationPreferences.Default;

        try
        {
            await ServiceClientAuthHelper.SetAuthHeaderAsync(
                _httpClient, _serviceAuth, _logger, "Tenant Service (notification preferences)", cancellationToken);

            // Accepts a PlatformUser id OR a UserIdentity id — Wallet.Owner is genuinely either kind.
            var response = await _httpClient.GetAsync(
                $"api/internal/users/{ownerId}/notification-preferences", cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // The person never saved preferences (or the id is unknown): the documented
                // default applies. Routine, so cached and not warned about.
                _cache.Set(cacheKey, NotificationPreferences.Default, CacheDuration);
                return NotificationPreferences.Default;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Tenant Service returned {StatusCode} for preferences, using defaults for user {UserId}",
                    response.StatusCode, userId);
                return NotificationPreferences.Default;
            }

            var tenantPrefs = await response.Content.ReadFromJsonAsync<TenantUserPreferences>(JsonOptions, cancellationToken);
            if (tenantPrefs is null)
                return NotificationPreferences.Default;

            var preferences = MapToNotificationPreferences(tenantPrefs);

            _cache.Set(cacheKey, preferences, CacheDuration);

            _logger.LogDebug(
                "Resolved notification preferences for user {UserId}: Enabled={Enabled}, RealTime={RealTime}",
                userId, preferences.NotificationsEnabled, preferences.IsRealTime);

            return preferences;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to resolve notification preferences for user {UserId}, using defaults",
                userId);
            return NotificationPreferences.Default;
        }
    }

    private static NotificationPreferences MapToNotificationPreferences(TenantUserPreferences prefs)
    {
        var isRealTime = prefs.NotificationFrequency?.Equals("RealTime", StringComparison.OrdinalIgnoreCase) ?? true;
        var wantsEmail = prefs.NotificationMethod?.Contains("Email", StringComparison.OrdinalIgnoreCase) ?? false;
        var wantsPush = prefs.NotificationMethod?.Contains("Push", StringComparison.OrdinalIgnoreCase) ?? false;

        return new NotificationPreferences
        {
            NotificationsEnabled = prefs.NotificationsEnabled ?? true,
            IsRealTime = isRealTime,
            WantsEmail = wantsEmail,
            WantsPush = wantsPush
        };
    }

    /// <summary>
    /// Subset of Tenant Service UserPreferences response relevant to notifications.
    /// </summary>
    private sealed class TenantUserPreferences
    {
        [JsonPropertyName("notificationsEnabled")]
        public bool? NotificationsEnabled { get; set; }

        [JsonPropertyName("notificationMethod")]
        public string? NotificationMethod { get; set; }

        [JsonPropertyName("notificationFrequency")]
        public string? NotificationFrequency { get; set; }
    }
}
