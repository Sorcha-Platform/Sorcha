// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Notification preference provider that calls Tenant Service GET /api/preferences
/// to resolve the user's actual notification preferences.
/// Caches per-user results for 5 minutes to avoid per-notification API calls.
/// </summary>
public sealed class TenantNotificationPreferenceProvider : INotificationPreferenceProvider
{
    private readonly HttpClient _httpClient;
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
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<TenantNotificationPreferenceProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var serviceAddress = configuration["ServiceClients:TenantService:Address"]
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

        try
        {
            var response = await _httpClient.GetAsync($"api/preferences", cancellationToken);

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
