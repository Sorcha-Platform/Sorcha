// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.Register.Core.LocalRelationship;
using Sorcha.ServiceClients.SystemWallet;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Resolves this node's identity (Feature 108) from the <b>system wallet it actually signs with</b>,
/// rather than from static configuration.
/// </summary>
/// <remarks>
/// <para>
/// The static <see cref="ConfiguredLocalIdentityProvider"/> reads <c>LocalIdentity:*</c> from
/// appsettings — and nothing sets those keys, on any deployment. So every node resolved an
/// <b>empty</b> identity and therefore derived <i>every</i> register as
/// <c>IsSubscriber</c>: a node was a mere subscriber to registers it owned, validated and sealed.
/// Two things fell out of that on n1: the system register sat permanently in "Syncing"/"Recovery"
/// (the peer service tried to pull a register this node is itself the source of), and the F145/T017
/// roster-based sealer selection always answered "subscriber" — right outcome on a single node only
/// because the fan-out found no peers.
/// </para>
/// <para>
/// The node's real claim on a register is <b>Validator</b>: its <c>sorcha:docket-signing</c> key is on
/// the register's roster and it seals the dockets. That key is already derivable here — it is exactly
/// how <c>RegisterCreationOrchestrator</c> populates the roster of every register this node creates —
/// so this provider resolves it from the same source of truth. Deriving it any other way would
/// invite the two to drift.
/// </para>
/// <para>
/// <b>Fail-safe:</b> if the wallet cannot be reached, this falls back to the configured identity and
/// ultimately to <see cref="LocalIdentitySnapshot.Empty"/> — i.e. plain subscriber, which claims
/// nothing. A node must never assert ownership or validator rights it cannot prove.
/// </para>
/// </remarks>
public sealed class SystemWalletLocalIdentityProvider : ILocalIdentityProvider
{
    /// <summary>The derivation context whose public key appears on a register's validator roster.</summary>
    private const string DocketSigningPath = "sorcha:docket-signing";

    /// <summary>
    /// A zeroed hash. We sign it only to learn the derived public key — the signature is discarded.
    /// (Same trick as <c>RegisterCreationOrchestrator</c>; both want a "derive public key" call the
    /// wallet client does not yet expose.)
    /// </summary>
    private const string ZeroHash = "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly ISystemWalletSigningService _signingService;
    private readonly IOptionsMonitor<LocalIdentityOptions> _options;
    private readonly ILogger<SystemWalletLocalIdentityProvider> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private LocalIdentitySnapshot? _cached;

    /// <summary>Initialises a new <see cref="SystemWalletLocalIdentityProvider"/>.</summary>
    public SystemWalletLocalIdentityProvider(
        ISystemWalletSigningService signingService,
        IOptionsMonitor<LocalIdentityOptions> options,
        ILogger<SystemWalletLocalIdentityProvider> logger)
    {
        _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options.OnChange(_ => Invalidate());
    }

    /// <inheritdoc />
    public async ValueTask<LocalIdentitySnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is { } cached)
            return cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is { } again)
                return again;

            _cached = await ResolveAsync(cancellationToken).ConfigureAwait(false);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Invalidate() => _cached = null;

    private async Task<LocalIdentitySnapshot> ResolveAsync(CancellationToken ct)
    {
        // Configured values are an override / test seam; they win when present.
        var configured = _options.CurrentValue;
        var wallets = new List<string>(configured.WalletAddresses ?? []);
        byte[]? validatorKey = TryDecodeConfiguredValidatorKey(configured);

        if (validatorKey is null || wallets.Count == 0)
        {
            try
            {
                var derived = await _signingService.SignAsync(
                    registerId: "local-identity",
                    txId: "local-identity-key-derivation",
                    payloadHash: ZeroHash,
                    derivationPath: DocketSigningPath,
                    transactionType: "ValidatorKeyDerivation",
                    ct).ConfigureAwait(false);

                validatorKey ??= derived.PublicKey;

                if (!string.IsNullOrEmpty(derived.WalletAddress) &&
                    !wallets.Contains(derived.WalletAddress, StringComparer.Ordinal))
                {
                    wallets.Add(derived.WalletAddress);
                }
            }
            catch (Exception ex)
            {
                // Fail safe: claim nothing. A node that cannot prove its validator key must not
                // assert validator rights — it simply derives as a subscriber, as it did before.
                _logger.LogWarning(ex,
                    "Could not resolve the local validator key from the system wallet. This node will "
                    + "derive every register relationship as Subscriber, so it will not recognise "
                    + "registers it owns or validates.");
            }
        }

        var snapshot = new LocalIdentitySnapshot(wallets, validatorKey);

        _logger.LogInformation(
            "LocalIdentity resolved from the system wallet — {WalletCount} wallet(s), validator key {KeyPresent}",
            snapshot.WalletAddresses.Count,
            snapshot.ValidatorPublicKey is null ? "absent" : "present");

        return snapshot;
    }

    private byte[]? TryDecodeConfiguredValidatorKey(LocalIdentityOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ValidatorPublicKeyBase64))
            return null;

        try
        {
            return Convert.FromBase64String(options.ValidatorPublicKeyBase64);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex,
                "LocalIdentity:ValidatorPublicKeyBase64 is not valid Base64 — deriving the key from the "
                + "system wallet instead");
            return null;
        }
    }
}
