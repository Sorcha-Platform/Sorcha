// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Sorcha.Tenant.Service.Hubs;

/// <summary>
/// SignalR hub for tenant-domain real-time events: identity, membership,
/// admin, system messages, and the durable user inbox.
/// </summary>
/// <remarks>
/// <para>
/// Created in Feature 118 Phase 4 (US2 — topology consolidation). This hub
/// is auth-only at this phase — inbox events land in Phase 5 (US3 — durable
/// user inbox), membership / security / system announcement events follow.
/// </para>
/// <para>
/// On connect, each authenticated client is added to the
/// <see cref="TenantHubGroups.User"/> group keyed by their
/// <c>platform_user_id</c> claim. This lets server-side broadcasters target a
/// single user across all their connected sessions in a single
/// <c>Clients.Group(...)</c> call.
/// </para>
/// <para>
/// Connection URL: <c>/hubs/tenant</c>.
/// Authentication: Bearer JWT (via query string <c>?access_token=</c> for browsers).
/// The <c>platform_user_id</c> claim is required — connections without it are aborted.
/// </para>
/// </remarks>
[Authorize(AuthenticationSchemes = "Bearer")]
public sealed class TenantHub : Hub<ITenantHubClient>
{
    /// <summary>Standard PlatformUserId claim type used by Sorcha JWTs.</summary>
    public const string PlatformUserIdClaim = "platform_user_id";

    private readonly ILogger<TenantHub> _logger;

    /// <summary>Initialises a new instance of the <see cref="TenantHub"/> class.</summary>
    public TenantHub(ILogger<TenantHub> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        var platformUserId = ResolvePlatformUserId();
        if (platformUserId is null)
        {
            _logger.LogWarning(
                "TenantHub connection rejected — missing or invalid platform_user_id claim. ConnectionId: {ConnectionId}",
                Context.ConnectionId);
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TenantHubGroups.User(platformUserId.Value));
        _logger.LogInformation(
            "TenantHub client connected. PlatformUserId: {PlatformUserId}, ConnectionId: {ConnectionId}",
            platformUserId.Value,
            Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var platformUserId = ResolvePlatformUserId();
        if (exception is not null)
        {
            _logger.LogWarning(
                exception,
                "TenantHub client disconnected with error. PlatformUserId: {PlatformUserId}, ConnectionId: {ConnectionId}",
                platformUserId,
                Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Reads <see cref="PlatformUserIdClaim"/> from the connection's principal,
    /// returning <c>null</c> if missing or not a parseable GUID.
    /// </summary>
    private Guid? ResolvePlatformUserId()
    {
        var raw = Context.User?.FindFirst(PlatformUserIdClaim)?.Value;
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }
}
