// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Minimal API endpoints for user preferences management.
/// Preferences are lazily created on first access with sensible defaults.
/// </summary>
public static class UserPreferenceEndpoints
{
    private static readonly string[] ValidThemes = ["Light", "Dark", "System"];
    private static readonly string[] ValidLanguages = ["en", "fr", "de", "es"];
    private static readonly string[] ValidTimeFormats = ["UTC", "Local"];
    private static readonly string[] ValidNotificationMethods = ["InApp", "InAppPlusEmail", "InAppPlusPush"];
    private static readonly string[] ValidNotificationFrequencies = ["RealTime", "HourlyDigest", "DailyDigest"];

    /// <summary>
    /// Maps user preference endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapUserPreferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/preferences")
            .WithTags("UserPreferences")
            .RequireAuthorization();

        group.MapGet("/", GetPreferences)
            .WithName("GetUserPreferences")
            .WithSummary("Get authenticated user's preferences (lazy-creates with defaults if not exist)");

        group.MapPut("/", UpdatePreferences)
            .WithName("UpdateUserPreferences")
            .WithSummary("Partial update of user preferences");

        group.MapGet("/default-wallet", GetDefaultWallet)
            .WithName("GetDefaultWallet")
            .WithSummary("Get default wallet address for signing flows");

        group.MapPut("/default-wallet", SetDefaultWallet)
            .WithName("SetDefaultWallet")
            .WithSummary("Set default wallet address");

        group.MapDelete("/default-wallet", ClearDefaultWallet)
            .WithName("ClearDefaultWallet")
            .WithSummary("Clear default wallet address");

        return app;
    }

    private static async Task<IResult> GetPreferences(
        HttpContext context,
        TenantDbContext db,
        CancellationToken ct)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        var prefs = await db.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (prefs is null)
        {
            prefs = new UserPreferences { UserId = userId };
            db.UserPreferences.Add(prefs);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(MapToDto(prefs));
    }

    private static async Task<IResult> UpdatePreferences(
        HttpContext context,
        TenantDbContext db,
        UpdatePreferencesRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        // Validate fields
        var errors = new Dictionary<string, string[]>();
        if (request.Theme is not null && !ValidThemes.Contains(request.Theme))
            errors["theme"] = [$"Must be one of: {string.Join(", ", ValidThemes)}"];
        if (request.Language is not null && !ValidLanguages.Contains(request.Language))
            errors["language"] = [$"Must be one of: {string.Join(", ", ValidLanguages)}"];
        if (request.TimeFormat is not null && !ValidTimeFormats.Contains(request.TimeFormat))
            errors["timeFormat"] = [$"Must be one of: {string.Join(", ", ValidTimeFormats)}"];
        if (request.DefaultWalletAddress is not null && request.DefaultWalletAddress.Length > 200)
            errors["defaultWalletAddress"] = ["Must be 200 characters or fewer"];
        if (request.NotificationMethod is not null && !ValidNotificationMethods.Contains(request.NotificationMethod))
            errors["notificationMethod"] = [$"Must be one of: {string.Join(", ", ValidNotificationMethods)}"];
        if (request.NotificationFrequency is not null && !ValidNotificationFrequencies.Contains(request.NotificationFrequency))
            errors["notificationFrequency"] = [$"Must be one of: {string.Join(", ", ValidNotificationFrequencies)}"];

        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var prefs = await db.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (prefs is null)
        {
            prefs = new UserPreferences { UserId = userId };
            db.UserPreferences.Add(prefs);
        }

        // Apply partial updates (only non-null fields)
        if (request.Theme is not null)
            prefs.Theme = Enum.Parse<ThemePreference>(request.Theme);
        if (request.Language is not null)
            prefs.Language = request.Language;
        if (request.TimeFormat is not null)
            prefs.TimeFormat = Enum.Parse<TimeFormatPreference>(request.TimeFormat);
        if (request.DefaultWalletAddress is not null)
            prefs.DefaultWalletAddress = request.DefaultWalletAddress;
        if (request.NotificationsEnabled.HasValue)
            prefs.NotificationsEnabled = request.NotificationsEnabled.Value;
        if (request.NotificationMethod is not null)
            prefs.NotificationMethod = Enum.Parse<NotificationMethod>(request.NotificationMethod);
        if (request.NotificationFrequency is not null)
            prefs.NotificationFrequency = Enum.Parse<NotificationFrequency>(request.NotificationFrequency);
        // Note: TwoFactorEnabled is intentionally read-only via this API

        prefs.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(MapToDto(prefs));
    }

    private static async Task<IResult> GetDefaultWallet(
        HttpContext context,
        TenantDbContext db,
        CancellationToken ct)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        var address = await db.UserPreferences
            .Where(p => p.UserId == userId)
            .Select(p => p.DefaultWalletAddress)
            .FirstOrDefaultAsync(ct);

        return TypedResults.Ok(new { defaultWalletAddress = address });
    }

    private static async Task<IResult> SetDefaultWallet(
        HttpContext context,
        TenantDbContext db,
        SetDefaultWalletRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.WalletAddress))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["walletAddress"] = ["Wallet address is required"]
            });

        if (request.WalletAddress.Length > 200)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["walletAddress"] = ["Must be 200 characters or fewer"]
            });

        var prefs = await db.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (prefs is null)
        {
            prefs = new UserPreferences { UserId = userId };
            db.UserPreferences.Add(prefs);
        }

        prefs.DefaultWalletAddress = request.WalletAddress;
        prefs.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new { defaultWalletAddress = prefs.DefaultWalletAddress });
    }

    private static async Task<IResult> ClearDefaultWallet(
        HttpContext context,
        TenantDbContext db,
        CancellationToken ct)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        var prefs = await db.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (prefs is not null)
        {
            prefs.DefaultWalletAddress = null;
            prefs.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static Guid GetUserId(HttpContext context)
    {
        var sub = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    private static object MapToDto(UserPreferences p) => new
    {
        theme = p.Theme.ToString(),
        language = p.Language,
        timeFormat = p.TimeFormat.ToString(),
        defaultWalletAddress = p.DefaultWalletAddress,
        notificationsEnabled = p.NotificationsEnabled,
        notificationMethod = p.NotificationMethod.ToString(),
        notificationFrequency = p.NotificationFrequency.ToString(),
        twoFactorEnabled = p.TwoFactorEnabled
    };
}

/// <summary>
/// Request to partially update user preferences. Omitted fields are not changed.
/// </summary>
public record UpdatePreferencesRequest(
    string? Theme = null,
    string? Language = null,
    string? TimeFormat = null,
    string? DefaultWalletAddress = null,
    bool? NotificationsEnabled = null,
    string? NotificationMethod = null,
    string? NotificationFrequency = null);

/// <summary>
/// Request to set default wallet address.
/// </summary>
public record SetDefaultWalletRequest(string WalletAddress = "");
