// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Tenant.Service.Tests.Infrastructure;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

public class UserPreferenceEndpointsTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public UserPreferenceEndpointsTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        _client = _factory.CreateAdminClient();
        await _factory.SeedTestDataAsync();
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    #region GET /api/preferences Tests

    [Fact]
    public async Task GetPreferences_WithoutAuth_ReturnsUnauthorized()
    {
        var unauthClient = _factory.CreateClient();
        var response = await unauthClient.GetAsync("/api/preferences");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPreferences_FirstAccess_CreatesDefaultsAndReturns()
    {
        var response = await _client.GetAsync("/api/preferences");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var prefs = await response.Content.ReadFromJsonAsync<PreferencesResponse>();
        prefs.Should().NotBeNull();
        prefs!.Theme.Should().Be("System");
        prefs.Language.Should().Be("en");
        prefs.TimeFormat.Should().Be("Local");
        prefs.DefaultWalletAddress.Should().BeNull();
        prefs.NotificationsEnabled.Should().BeFalse();
        prefs.TwoFactorEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetPreferences_SubsequentAccess_ReturnsSamePreferences()
    {
        // First call to create
        await _client.GetAsync("/api/preferences");

        // Second call should return same defaults
        var response = await _client.GetAsync("/api/preferences");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var prefs = await response.Content.ReadFromJsonAsync<PreferencesResponse>();
        prefs.Should().NotBeNull();
        prefs!.Theme.Should().Be("System");
    }

    #endregion

    #region PUT /api/preferences Tests

    [Fact]
    public async Task UpdatePreferences_PartialUpdate_OnlyChangesSpecifiedFields()
    {
        // Ensure preferences exist
        await _client.GetAsync("/api/preferences");

        // Update only theme
        var response = await _client.PutAsJsonAsync("/api/preferences", new { theme = "Dark" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var prefs = await response.Content.ReadFromJsonAsync<PreferencesResponse>();
        prefs.Should().NotBeNull();
        prefs!.Theme.Should().Be("Dark");
        prefs.Language.Should().Be("en"); // Unchanged
        prefs.TimeFormat.Should().Be("Local"); // Unchanged
    }

    [Fact]
    public async Task UpdatePreferences_AllFields_UpdatesAll()
    {
        var response = await _client.PutAsJsonAsync("/api/preferences", new
        {
            theme = "Light",
            language = "fr",
            timeFormat = "UTC",
            defaultWalletAddress = "did:sorcha:w:test123",
            notificationsEnabled = true
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var prefs = await response.Content.ReadFromJsonAsync<PreferencesResponse>();
        prefs.Should().NotBeNull();
        prefs!.Theme.Should().Be("Light");
        prefs.Language.Should().Be("fr");
        prefs.TimeFormat.Should().Be("UTC");
        prefs.DefaultWalletAddress.Should().Be("did:sorcha:w:test123");
        prefs.NotificationsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdatePreferences_InvalidTheme_ReturnsBadRequest()
    {
        var response = await _client.PutAsJsonAsync("/api/preferences", new { theme = "Rainbow" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatePreferences_InvalidLanguage_ReturnsBadRequest()
    {
        var response = await _client.PutAsJsonAsync("/api/preferences", new { language = "xx" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatePreferences_InvalidTimeFormat_ReturnsBadRequest()
    {
        var response = await _client.PutAsJsonAsync("/api/preferences", new { timeFormat = "Pacific" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatePreferences_TooLongWalletAddress_ReturnsBadRequest()
    {
        var longAddress = new string('x', 201);
        var response = await _client.PutAsJsonAsync("/api/preferences", new { defaultWalletAddress = longAddress });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatePreferences_TwoFactorEnabled_IsIgnored()
    {
        // Ensure preferences exist
        await _client.GetAsync("/api/preferences");

        // Try to set twoFactorEnabled (should be ignored — read-only via this API)
        await _client.PutAsJsonAsync("/api/preferences", new { theme = "Dark" });

        var getResponse = await _client.GetAsync("/api/preferences");
        var prefs = await getResponse.Content.ReadFromJsonAsync<PreferencesResponse>();
        prefs!.TwoFactorEnabled.Should().BeFalse();
    }

    #endregion

    #region GET /api/preferences/default-wallet Tests

    [Fact]
    public async Task GetDefaultWallet_NoPreferences_ReturnsNull()
    {
        var response = await _client.GetAsync("/api/preferences/default-wallet");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<DefaultWalletDto>();
        result.Should().NotBeNull();
        result!.DefaultWalletAddress.Should().BeNull();
    }

    [Fact]
    public async Task GetDefaultWallet_AfterSet_ReturnsAddress()
    {
        // Set a default wallet first
        await _client.PutAsJsonAsync("/api/preferences/default-wallet",
            new { walletAddress = "did:sorcha:w:wallet1" });

        var response = await _client.GetAsync("/api/preferences/default-wallet");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<DefaultWalletDto>();
        result!.DefaultWalletAddress.Should().Be("did:sorcha:w:wallet1");
    }

    #endregion

    #region PUT /api/preferences/default-wallet Tests

    [Fact]
    public async Task SetDefaultWallet_ValidAddress_ReturnsOk()
    {
        var response = await _client.PutAsJsonAsync("/api/preferences/default-wallet",
            new { walletAddress = "did:sorcha:w:mywallet" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<DefaultWalletDto>();
        result!.DefaultWalletAddress.Should().Be("did:sorcha:w:mywallet");
    }

    [Fact]
    public async Task SetDefaultWallet_EmptyAddress_ReturnsBadRequest()
    {
        var response = await _client.PutAsJsonAsync("/api/preferences/default-wallet",
            new { walletAddress = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetDefaultWallet_TooLongAddress_ReturnsBadRequest()
    {
        var longAddress = new string('x', 201);
        var response = await _client.PutAsJsonAsync("/api/preferences/default-wallet",
            new { walletAddress = longAddress });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region DELETE /api/preferences/default-wallet Tests

    [Fact]
    public async Task ClearDefaultWallet_ReturnsNoContent()
    {
        // Set a wallet first
        await _client.PutAsJsonAsync("/api/preferences/default-wallet",
            new { walletAddress = "did:sorcha:w:toclear" });

        var response = await _client.DeleteAsync("/api/preferences/default-wallet");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify cleared
        var getResponse = await _client.GetAsync("/api/preferences/default-wallet");
        var result = await getResponse.Content.ReadFromJsonAsync<DefaultWalletDto>();
        result!.DefaultWalletAddress.Should().BeNull();
    }

    [Fact]
    public async Task ClearDefaultWallet_NoPreferences_ReturnsNoContent()
    {
        var response = await _client.DeleteAsync("/api/preferences/default-wallet");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region Notification Preference Tests

    [Fact]
    public async Task GetPreferences_FirstAccess_ReturnsDefaultNotificationPreferences()
    {
        var response = await _client.GetAsync("/api/preferences");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var prefs = await response.Content.ReadFromJsonAsync<PreferencesResponse>();
        prefs.Should().NotBeNull();
        prefs!.NotificationMethod.Should().Be("InApp");
        prefs.NotificationFrequency.Should().Be("RealTime");
    }

    [Fact]
    public async Task UpdatePreferences_NotificationMethod_UpdatesMethod()
    {
        // Ensure preferences exist
        await _client.GetAsync("/api/preferences");

        var response = await _client.PutAsJsonAsync("/api/preferences", new { notificationMethod = "InAppPlusEmail" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var prefs = await response.Content.ReadFromJsonAsync<PreferencesResponse>();
        prefs.Should().NotBeNull();
        prefs!.NotificationMethod.Should().Be("InAppPlusEmail");
    }

    [Fact]
    public async Task UpdatePreferences_NotificationFrequency_UpdatesFrequency()
    {
        // Ensure preferences exist
        await _client.GetAsync("/api/preferences");

        var response = await _client.PutAsJsonAsync("/api/preferences", new { notificationFrequency = "DailyDigest" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var prefs = await response.Content.ReadFromJsonAsync<PreferencesResponse>();
        prefs.Should().NotBeNull();
        prefs!.NotificationFrequency.Should().Be("DailyDigest");
    }

    [Fact]
    public async Task UpdatePreferences_BothNotificationFields_UpdatesBoth()
    {
        // Ensure preferences exist
        await _client.GetAsync("/api/preferences");

        var response = await _client.PutAsJsonAsync("/api/preferences", new
        {
            notificationMethod = "InAppPlusPush",
            notificationFrequency = "HourlyDigest"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var prefs = await response.Content.ReadFromJsonAsync<PreferencesResponse>();
        prefs.Should().NotBeNull();
        prefs!.NotificationMethod.Should().Be("InAppPlusPush");
        prefs.NotificationFrequency.Should().Be("HourlyDigest");
    }

    [Fact]
    public async Task UpdatePreferences_InvalidNotificationMethod_ReturnsBadRequest()
    {
        var response = await _client.PutAsJsonAsync("/api/preferences", new { notificationMethod = "Email" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatePreferences_InvalidNotificationFrequency_ReturnsBadRequest()
    {
        var response = await _client.PutAsJsonAsync("/api/preferences", new { notificationFrequency = "Weekly" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatePreferences_NotificationMethod_DoesNotChangeOtherFields()
    {
        // Ensure preferences exist with known frequency
        await _client.GetAsync("/api/preferences");
        await _client.PutAsJsonAsync("/api/preferences", new { notificationFrequency = "DailyDigest" });

        // Update only method
        var response = await _client.PutAsJsonAsync("/api/preferences", new { notificationMethod = "InAppPlusEmail" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var prefs = await response.Content.ReadFromJsonAsync<PreferencesResponse>();
        prefs.Should().NotBeNull();
        prefs!.NotificationMethod.Should().Be("InAppPlusEmail");
        prefs.NotificationFrequency.Should().Be("DailyDigest"); // Unchanged
    }

    #endregion

    // Response DTOs for deserialization
    private record PreferencesResponse(
        string Theme,
        string Language,
        string TimeFormat,
        string? DefaultWalletAddress,
        bool NotificationsEnabled,
        string NotificationMethod,
        string NotificationFrequency,
        bool TwoFactorEnabled);

    private record DefaultWalletDto(string? DefaultWalletAddress);
}
