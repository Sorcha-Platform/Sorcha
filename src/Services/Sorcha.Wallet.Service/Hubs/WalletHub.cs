// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Sorcha.Wallet.Service.Hubs;

/// <summary>
/// SignalR hub for citizen wallet PWA real-time notifications (Feature 114).
/// </summary>
/// <remarks>
/// Hub URL: <c>/hubs/wallet</c> (proxied through API Gateway from <c>/hubs/wallet</c>).
/// Authentication: existing JWT via query string <c>?access_token={jwt}</c>; the JWT
/// MUST carry the <c>sorcha:citizen-wallet</c> audience or the connection is closed.
///
/// On connect each client is added to a group keyed by the authenticated
/// PlatformUser id, so server-side broadcasters (issuance pipelines, revocation
/// flows) can target a single citizen across all their enrolled devices in a
/// single <c>Clients.Group(...)</c> call.
///
/// Server-to-client methods (added incrementally as features need them):
/// - <c>CredentialAvailable(string credentialId)</c> — US4 (auto-receive new credentials)
/// - <c>DeviceRevoked(Guid deviceId)</c> — US3 (lock the wallet if its own device id matches)
///
/// SignalR push is treated as an <em>optimisation</em>, not authoritative — the
/// pull-on-open <c>/api/v1/wallet/sync</c> endpoint remains the source of truth.
/// </remarks>
[Authorize(AuthenticationSchemes = "Bearer")]
public sealed class WalletHub : Hub
{
    private readonly ILogger<WalletHub> _logger;

    /// <summary>Initialises a new instance of the <see cref="WalletHub"/> class.</summary>
    public WalletHub(ILogger<WalletHub> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Standard PlatformUserId claim type used by Sorcha JWTs.</summary>
    public const string PlatformUserIdClaim = "platform_user_id";

    /// <summary>SignalR group prefix for per-citizen broadcasts.</summary>
    public const string GroupPrefix = "wallet:platform-user:";

    /// <summary>
    /// Returns the SignalR group name for a given PlatformUser. Server-side broadcasters
    /// use this to target all of a citizen's connected devices.
    /// </summary>
    public static string GroupNameFor(Guid platformUserId) => GroupPrefix + platformUserId.ToString("N");

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        var platformUserId = ResolvePlatformUserId();
        if (platformUserId is null)
        {
            _logger.LogWarning(
                "WalletHub connection {ConnectionId} rejected — JWT missing platform_user_id claim",
                Context.ConnectionId);
            Context.Abort();
            return;
        }

        var group = GroupNameFor(platformUserId.Value);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);

        _logger.LogInformation(
            "WalletHub: PlatformUser {PlatformUserId} connected (ConnectionId={ConnectionId}, Group={Group})",
            platformUserId.Value, Context.ConnectionId, group);

        await base.OnConnectedAsync();
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var platformUserId = ResolvePlatformUserId();
        if (platformUserId is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNameFor(platformUserId.Value));
        }

        _logger.LogInformation(
            "WalletHub: connection {ConnectionId} disconnected (PlatformUser={PlatformUserId})",
            Context.ConnectionId, platformUserId?.ToString() ?? "anonymous");

        await base.OnDisconnectedAsync(exception);
    }

    private Guid? ResolvePlatformUserId()
    {
        var raw =
            Context.User?.FindFirst(PlatformUserIdClaim)?.Value
            ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
