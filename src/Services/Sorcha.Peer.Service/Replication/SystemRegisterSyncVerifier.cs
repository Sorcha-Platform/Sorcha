// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
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
    private readonly ICryptoModule _cryptoModule;
    private readonly RegisterCache _registerCache;
    private readonly ILogger<SystemRegisterSyncVerifier> _logger;
    private readonly Lazy<SystemRegisterGenesis?> _trustedGenesis;

    public SystemRegisterSyncVerifier(
        IOptions<SystemRegisterOptions> options,
        ICryptoModule cryptoModule,
        RegisterCache registerCache,
        ILogger<SystemRegisterSyncVerifier> logger)
    {
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _registerCache = registerCache ?? throw new ArgumentNullException(nameof(registerCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        // Locate the control transaction via the register cache. The docket itself
        // only carries TransactionIds — full transactions are pulled separately and
        // stored in the cache by RegisterReplicationService.
        var controlTx = GenesisControlTransactionLookup.TryFindControl(
            registerId, genesisDocket, _registerCache, _logger);

        if (controlTx is null)
        {
            _logger.LogWarning(
                "System register genesis docket has no resolvable control transaction in cache " +
                "(register {RegisterId}, docket {Docket}, transactionIds={Count}) — rejecting",
                registerId, genesisDocket.Version, genesisDocket.TransactionIds?.Count ?? 0);
            return false;
        }

        var encodedPayload = controlTx.Payloads is { Length: > 0 } ? controlTx.Payloads[0].Data : null;
        var encodedSignature = controlTx.Signature;
        if (string.IsNullOrEmpty(encodedPayload) || string.IsNullOrEmpty(encodedSignature))
        {
            _logger.LogWarning(
                "System register genesis control tx {TxId} for register {RegisterId} " +
                "is missing payload or signature — rejecting",
                controlTx.TxId, registerId);
            return false;
        }

        try
        {
            // Persisted TransactionModel doesn't carry the signing pubkey or algorithm
            // (the validator strips them down to a Base64Url signature string + sender
            // wallet at seal time). Verification therefore uses the trusted genesis
            // pubkey/algorithm directly — a forged transaction would fail this check
            // because no peer can produce a valid signature without the genesis private
            // key, which is destroyed after ceremony.
            var trustedPubKey = Convert.FromBase64String(
                trustedGenesis.GenesisTransaction.Signature.PublicKey);
            var trustedAlgorithm = trustedGenesis.GenesisTransaction.Signature.Algorithm;

            if (!Enum.TryParse<WalletNetworks>(trustedAlgorithm, ignoreCase: true, out var network))
            {
                _logger.LogError(
                    "Trusted genesis algorithm {Algorithm} is not a recognised WalletNetworks value",
                    trustedAlgorithm);
                return false;
            }

            // Recompute the signed-data hash the same way the CLI ceremony did:
            //   payloadHash    = SHA256(decoded_control_record_bytes)
            //   dataToSign     = SHA256(UTF8("{txId}:{payloadHash}"))
            // The ContentEncoding on docket payloads is "base64url" (see
            // DocketBuildTriggerService:583), so decode accordingly.
            var peerPayloadBytes = DecodeBase64Auto(encodedPayload);
            var peerPayloadHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(peerPayloadBytes)).ToLowerInvariant();
            var peerTxId = GenesisSignatureVerifier.ComputeGenesisTxId();
            var dataToSign = $"{peerTxId}:{peerPayloadHash}";
            var signedDataHash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(dataToSign));

            // Validator persists the signature as Base64Url; ceremony emits Base64.
            // Try both — same DecodeBase64Auto helper accepts either form.
            var peerSignatureBytes = DecodeBase64Auto(encodedSignature);

            var verifyResult = await _cryptoModule.VerifyAsync(
                peerSignatureBytes, signedDataHash, (byte)network, trustedPubKey, cancellationToken);

            if (verifyResult != CryptoStatus.Success)
            {
                _logger.LogError(
                    "Peer system register rejected: genesis signature verification failed " +
                    "({Status}) — register {RegisterId}. The control record may have been " +
                    "tampered with, or the peer's genesis key does not match this network's " +
                    "trust anchor (fingerprint={Fingerprint}).",
                    verifyResult, registerId, trustedGenesis.GenesisPublicKeyFingerprint);
                return false;
            }

            _logger.LogInformation(
                "Peer system register genesis verified for register {RegisterId} " +
                "(trustedFingerprint={Fingerprint}, signature=VALID)",
                registerId, trustedGenesis.GenesisPublicKeyFingerprint);
            return true;
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex,
                "Failed to decode peer system register genesis data for register {RegisterId}",
                registerId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to verify peer system register genesis signature for register {RegisterId}",
                registerId);
            return false;
        }
    }

    /// <summary>
    /// Tries Base64Url decoding first then standard Base64.
    /// CLI ceremony writes Base64; validator persists Base64Url. Either may reach us.
    /// </summary>
    private static byte[] DecodeBase64Auto(string encoded)
    {
        var s = encoded.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

}
