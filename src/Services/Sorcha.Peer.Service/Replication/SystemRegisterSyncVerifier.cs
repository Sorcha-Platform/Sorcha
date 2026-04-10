// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Peer.Service.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Genesis;
using Sorcha.ServiceDefaults;

namespace Sorcha.Peer.Service.Replication;

/// <summary>
/// Verifies the system register genesis transaction signature against the
/// trusted public key from the genesis file/embedded resource.
/// Only applies to <see cref="SystemRegisterConstants.SystemRegisterId"/> —
/// all other registers use the existing self-referential trust model.
/// </summary>
public interface ISystemRegisterSyncVerifier
{
    /// <summary>
    /// Returns true if the given register ID is the system register.
    /// </summary>
    bool IsSystemRegister(string registerId);

    /// <summary>
    /// Verifies a system register genesis docket's control record signature
    /// against the trusted genesis public key.
    /// Returns true if the genesis is trusted, false if rejected.
    /// </summary>
    Task<bool> VerifySystemRegisterGenesisAsync(
        string registerId,
        CachedDocket genesisDocket,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class SystemRegisterSyncVerifier : ISystemRegisterSyncVerifier
{
    private readonly IOptions<SystemRegisterOptions> _options;
    private readonly ICryptoModule _cryptoModule;
    private readonly ILogger<SystemRegisterSyncVerifier> _logger;
    private SystemRegisterGenesis? _trustedGenesis;
    private bool _genesisLoaded;

    public SystemRegisterSyncVerifier(
        IOptions<SystemRegisterOptions> options,
        ICryptoModule cryptoModule,
        ILogger<SystemRegisterSyncVerifier> logger)
    {
        _options = options;
        _cryptoModule = cryptoModule;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsSystemRegister(string registerId) =>
        registerId == SystemRegisterConstants.SystemRegisterId;

    /// <inheritdoc />
    public async Task<bool> VerifySystemRegisterGenesisAsync(
        string registerId,
        CachedDocket genesisDocket,
        CancellationToken cancellationToken = default)
    {
        if (!IsSystemRegister(registerId))
            return true; // Not system register — bypass

        // Load trusted genesis (lazy, once)
        if (!_genesisLoaded)
        {
            try
            {
                _trustedGenesis = GenesisFileLoader.Load(_options.Value.GenesisFile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load trusted genesis file for system register verification");
            }
            _genesisLoaded = true;
        }

        if (_trustedGenesis is null)
        {
            _logger.LogWarning(
                "No trusted genesis file available — cannot verify system register from peer. " +
                "Configure SystemRegister:GenesisFile or embed a genesis resource.");
            return false;
        }

        // Extract the control transaction from the genesis docket
        var controlTxPayload = ExtractControlTransactionPayload(genesisDocket);
        if (controlTxPayload is null)
        {
            _logger.LogWarning("System register genesis docket has no control transaction — rejecting");
            return false;
        }

        // Extract the signature from the docket's control transaction
        var peerSignature = ExtractTransactionSignature(genesisDocket);
        if (peerSignature is null)
        {
            _logger.LogWarning("System register genesis has no transaction signature — rejecting");
            return false;
        }

        // Verify the peer's genesis public key matches our trusted fingerprint
        try
        {
            var peerPublicKeyBytes = Convert.FromBase64String(peerSignature.Value.publicKey);
            var trusted = GenesisSignatureVerifier.MatchesFingerprint(
                peerPublicKeyBytes,
                _trustedGenesis.GenesisPublicKeyFingerprint);

            if (!trusted)
            {
                var peerFingerprint = GenesisFileLoader.ComputeFingerprint(peerPublicKeyBytes);
                _logger.LogError(
                    "Peer system register rejected: genesis signed by unknown key. " +
                    "Expected fingerprint: {Expected}, got: {Actual}",
                    _trustedGenesis.GenesisPublicKeyFingerprint, peerFingerprint);
                return false;
            }

            _logger.LogInformation(
                "Peer system register genesis fingerprint verified: {Fingerprint}",
                _trustedGenesis.GenesisPublicKeyFingerprint);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify peer system register genesis signature");
            return false;
        }
    }

    private static string? ExtractControlTransactionPayload(CachedDocket docket)
    {
        if (docket.Data is null || docket.Data.Length == 0)
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(docket.Data);
            var root = doc.RootElement;

            // Look for transactions array
            if (!root.TryGetProperty("transactions", out var transactions))
                return null;

            foreach (var tx in transactions.EnumerateArray())
            {
                // Find control transaction (type == 0)
                if (tx.TryGetProperty("transactionType", out var txType) &&
                    txType.GetInt32() == 0)
                {
                    if (tx.TryGetProperty("payload", out var payload))
                        return payload.GetString();
                }
            }
        }
        catch
        {
            // JSON parse failure — docket is malformed
        }

        return null;
    }

    private static (string publicKey, string signatureValue, string algorithm)? ExtractTransactionSignature(
        CachedDocket docket)
    {
        if (docket.Data is null || docket.Data.Length == 0)
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(docket.Data);
            var root = doc.RootElement;

            if (!root.TryGetProperty("transactions", out var transactions))
                return null;

            foreach (var tx in transactions.EnumerateArray())
            {
                if (tx.TryGetProperty("transactionType", out var txType) &&
                    txType.GetInt32() == 0)
                {
                    if (tx.TryGetProperty("signature", out var sig))
                    {
                        var pk = sig.TryGetProperty("publicKey", out var pkEl) ? pkEl.GetString() : null;
                        var sv = sig.TryGetProperty("signatureValue", out var svEl) ? svEl.GetString() : null;
                        var algo = sig.TryGetProperty("algorithm", out var algoEl) ? algoEl.GetString() : null;

                        if (pk != null && sv != null && algo != null)
                            return (pk, sv, algo);
                    }
                }
            }
        }
        catch
        {
            // JSON parse failure
        }

        return null;
    }
}
