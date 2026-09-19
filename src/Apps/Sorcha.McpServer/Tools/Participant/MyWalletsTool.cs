// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;

using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Lists the wallets the caller holds.
/// </summary>
/// <remarks>
/// <para>
/// Cold-start run #5 had no way to answer "which wallets do I hold?". <c>sorcha_wallet_info</c> is
/// a lookup <em>by address</em>, so it can only confirm a wallet you can already name. Both agents
/// concluded they held none, bound their organisation's wallet to their role instead, and were
/// then refused when they tried to sign — because the wallet in the record was not one their
/// session controls.
/// </para>
/// <para>
/// One of them put it exactly right: "the only way to find it would be to publish without a wallet
/// address and let it default, which writes to the register." Discovering your own identity should
/// not require a ledger write.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class MyWalletsTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IWalletServiceClient _walletClient;
    private readonly ILogger<MyWalletsTool> _logger;

    /// <summary>Initialises a new instance of <see cref="MyWalletsTool"/>.</summary>
    public MyWalletsTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IWalletServiceClient walletClient,
        ILogger<MyWalletsTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _walletClient = walletClient;
        _logger = logger;
    }

    /// <summary>Lists the wallets held by the caller.</summary>
    [McpServerTool(Name = "sorcha_my_wallets")]
    [Description("List the wallets this session can sign with: address, name, key algorithm and status, newest first. Call this before binding a role to a wallet, before submitting an action, or whenever a tool reports that a wallet is not linked to you — signing, publishing a participant record and reading disclosed data are all matched on the wallet the session controls, so knowing which one that is settles most refusals. Use sorcha_wallet_info rather than this tool when you already know an address and want that one wallet's public metadata, including wallets belonging to other people. No private key material or recovery phrase is ever returned. An empty list means this identity genuinely holds no wallet and must create one before it can sign.")]
    public async Task<MyWalletsResult> GetMyWalletsAsync(CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool("sorcha_my_wallets"))
        {
            return new MyWalletsResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires an authenticated consumer- or platform-tier caller.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable("Wallet"))
        {
            return new MyWalletsResult
            {
                Status = "Unavailable",
                Message = "Wallet service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        var stopwatch = Stopwatch.StartNew();
        var lookup = await _walletClient.GetMyWalletsAsync(cancellationToken);
        stopwatch.Stop();

        // A lookup that could not be answered is NOT an empty list. Reporting "you hold no wallet"
        // on the strength of a failed read is the run #5 defect this tool exists to end.
        if (lookup.Status == CallerWalletLookupStatus.Unavailable)
        {
            _availabilityTracker.RecordFailure("Wallet");
            return new MyWalletsResult
            {
                Status = "Unavailable",
                Message = $"Your wallets could not be read ({lookup.Reason}), so which wallets you "
                    + "hold is unknown — this is not the same as holding none.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }

        _availabilityTracker.RecordSuccess("Wallet");
        _logger.LogInformation("Listed {Count} wallet(s) for the caller", lookup.Wallets.Count);

        return new MyWalletsResult
        {
            Status = "Success",
            Message = lookup.Wallets.Count == 0
                ? "You hold no wallet. Create one before binding a role or submitting an action."
                : $"You hold {lookup.Wallets.Count} wallet(s).",
            CheckedAt = DateTimeOffset.UtcNow,
            ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
            Wallets = lookup.Wallets.Select(w => new MyWallet
            {
                Address = w.Address,
                Name = w.Name,
                Algorithm = w.Algorithm,
                Status = w.Status,
                PublicKey = w.PublicKey
            }).ToList()
        };
    }
}

/// <summary>One wallet the caller holds. Public surface only.</summary>
public sealed record MyWallet
{
    /// <summary>The wallet address — what a participant record binds and what signing matches on.</summary>
    public required string Address { get; init; }

    /// <summary>Human-readable wallet name.</summary>
    public required string Name { get; init; }

    /// <summary>Key algorithm (ED25519, NIST-P256, RSA-4096).</summary>
    public required string Algorithm { get; init; }

    /// <summary>Wallet status, e.g. Active.</summary>
    public required string Status { get; init; }

    /// <summary>The public key, as a participant record would carry it.</summary>
    public required string PublicKey { get; init; }
}

/// <summary>Result of listing the caller's wallets.</summary>
public sealed record MyWalletsResult
{
    /// <summary>Success, Unauthorized or Unavailable.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable outcome.</summary>
    public required string Message { get; init; }

    /// <summary>When the check ran.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Round-trip time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The wallets found. Empty only when the lookup was answered.</summary>
    public IReadOnlyList<MyWallet> Wallets { get; init; } = [];
}
