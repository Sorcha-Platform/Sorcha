// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
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
    /// <summary>TransactionType.Control = 0 — register governance transactions.</summary>
    private const int ControlTransactionType = 0;

    private readonly ICryptoModule _cryptoModule;
    private readonly ILogger<SystemRegisterSyncVerifier> _logger;
    private readonly Lazy<SystemRegisterGenesis?> _trustedGenesis;

    public SystemRegisterSyncVerifier(
        IOptions<SystemRegisterOptions> options,
        ICryptoModule cryptoModule,
        ILogger<SystemRegisterSyncVerifier> logger)
    {
        _cryptoModule = cryptoModule;
        _logger = logger;
        _trustedGenesis = new Lazy<SystemRegisterGenesis?>(() =>
        {
            try
            {
                return GenesisFileLoader.Load(options.Value.GenesisFile);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load trusted genesis file for system register verification");
                return null;
            }
        });
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

        var trustedGenesis = _trustedGenesis.Value;

        if (trustedGenesis is null)
        {
            _logger.LogWarning(
                "No trusted genesis file available — cannot verify system register from peer. " +
                "Configure SystemRegister:GenesisFile or embed a genesis resource.");
            return false;
        }

        // Extract both payload and signature from the control transaction in a single parse
        var extracted = TryExtractControlTransaction(genesisDocket);
        if (extracted is null)
        {
            _logger.LogWarning("System register genesis docket has no valid control transaction — rejecting");
            return false;
        }

        var (controlPayload, peerPublicKey, peerSignatureValue, peerAlgorithm) = extracted.Value;

        try
        {
            // Step 1: Verify the peer's genesis public key matches our trusted fingerprint.
            // Fingerprint is SHA-256 truncated to 128 bits (32 hex chars). This is sufficient
            // for collision resistance as a pre-filter; the full cryptographic signature
            // verification in Step 2 provides the actual security guarantee.
            var peerPublicKeyBytes = Convert.FromBase64String(peerPublicKey);
            var trusted = GenesisSignatureVerifier.MatchesFingerprint(
                peerPublicKeyBytes,
                trustedGenesis.GenesisPublicKeyFingerprint);

            if (!trusted)
            {
                var peerFingerprint = GenesisFileLoader.ComputeFingerprint(peerPublicKeyBytes);
                _logger.LogError(
                    "Peer system register rejected: genesis signed by unknown key. " +
                    "Expected fingerprint: {Expected}, got: {Actual}",
                    trustedGenesis.GenesisPublicKeyFingerprint, peerFingerprint);
                return false;
            }

            // Step 2: Verify the cryptographic signature over the payload.
            // Fingerprint alone is insufficient — a compromised peer could present
            // the real public key with a tampered control record.
            var peerPayloadBytes = Convert.FromBase64String(controlPayload);
            var peerPayloadHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(peerPayloadBytes)).ToLowerInvariant();
            var peerTxId = GenesisSignatureVerifier.ComputeGenesisTxId();
            var dataToSign = $"{peerTxId}:{peerPayloadHash}";
            var signedDataHash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(dataToSign));

            var peerSignatureBytes = Convert.FromBase64String(peerSignatureValue);

            if (!Enum.TryParse<WalletNetworks>(peerAlgorithm, ignoreCase: true, out var network))
            {
                _logger.LogError("Unsupported algorithm in peer genesis: {Algorithm}", peerAlgorithm);
                return false;
            }

            var verifyResult = await _cryptoModule.VerifyAsync(
                peerSignatureBytes, signedDataHash, (byte)network, peerPublicKeyBytes, cancellationToken);

            if (verifyResult != CryptoStatus.Success)
            {
                _logger.LogError(
                    "Peer system register rejected: genesis signature verification failed ({Status}). " +
                    "The control record may have been tampered with.",
                    verifyResult);
                return false;
            }

            _logger.LogInformation(
                "Peer system register genesis verified: fingerprint={Fingerprint}, signature=VALID",
                trustedGenesis.GenesisPublicKeyFingerprint);
            return true;
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Failed to decode peer system register genesis data");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify peer system register genesis signature");
            return false;
        }
    }

    /// <summary>
    /// Extracts payload and signature from the control transaction in a single JSON parse.
    /// Returns null if the docket has no valid control transaction.
    /// </summary>
    private static (string payload, string publicKey, string signatureValue, string algorithm)?
        TryExtractControlTransaction(CachedDocket docket)
    {
        if (docket.Data is null || docket.Data.Length == 0)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(docket.Data);
            var root = doc.RootElement;

            if (!root.TryGetProperty("transactions", out var transactions))
                return null;

            foreach (var tx in transactions.EnumerateArray())
            {
                if (!tx.TryGetProperty("transactionType", out var txType) ||
                    txType.GetInt32() != ControlTransactionType)
                    continue;

                var payload = tx.TryGetProperty("payload", out var p) ? p.GetString() : null;
                if (payload is null)
                    continue;

                if (!tx.TryGetProperty("signature", out var sig))
                    continue;

                var pk = sig.TryGetProperty("publicKey", out var pkEl) ? pkEl.GetString() : null;
                var sv = sig.TryGetProperty("signatureValue", out var svEl) ? svEl.GetString() : null;
                var algo = sig.TryGetProperty("algorithm", out var algoEl) ? algoEl.GetString() : null;

                if (pk is not null && sv is not null && algo is not null)
                    return (payload, pk, sv, algo);
            }
        }
        catch (JsonException ex)
        {
            // Docket JSON is malformed — caller will log and reject
            _ = ex;
        }

        return null;
    }
}
