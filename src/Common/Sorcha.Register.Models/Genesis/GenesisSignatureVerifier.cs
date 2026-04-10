// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using Sorcha.Register.Models.Constants;

namespace Sorcha.Register.Models.Genesis;

/// <summary>
/// Extracts and prepares genesis signature verification data.
/// Does not depend on ICryptoModule — callers perform the actual cryptographic verification.
/// </summary>
public static class GenesisSignatureVerifier
{
    /// <summary>
    /// Data needed to verify a genesis transaction signature.
    /// </summary>
    public record GenesisVerificationData(
        byte[] SignedDataHash,
        byte[] Signature,
        byte[] PublicKey,
        string Algorithm);

    /// <summary>
    /// Extracts the verification data from a genesis file.
    /// The signed data is SHA256(UTF8("{TxId}:{PayloadHash}")).
    /// </summary>
    public static GenesisVerificationData ExtractVerificationData(SystemRegisterGenesis genesis)
    {
        ArgumentNullException.ThrowIfNull(genesis);

        var tx = genesis.GenesisTransaction;
        var dataToSign = $"{tx.TxId}:{tx.PayloadHash}";
        var signedDataHash = SHA256.HashData(Encoding.UTF8.GetBytes(dataToSign));

        return new GenesisVerificationData(
            SignedDataHash: signedDataHash,
            Signature: Convert.FromBase64String(tx.Signature.SignatureValue),
            PublicKey: Convert.FromBase64String(tx.Signature.PublicKey),
            Algorithm: tx.Signature.Algorithm);
    }

    /// <summary>
    /// Validates the structural integrity of a genesis file without cryptographic verification.
    /// Returns a list of validation errors, or empty if valid.
    /// </summary>
    public static IReadOnlyList<string> ValidateStructure(SystemRegisterGenesis genesis)
    {
        var errors = new List<string>();

        if (genesis.Version != SystemRegisterGenesis.CurrentVersion)
            errors.Add($"Unsupported version {genesis.Version}. Expected {SystemRegisterGenesis.CurrentVersion}.");

        if (string.IsNullOrWhiteSpace(genesis.NetworkId))
            errors.Add("NetworkId is required.");
        else if (genesis.NetworkId.Length > 64)
            errors.Add("NetworkId must be 64 characters or fewer.");

        var tx = genesis.GenesisTransaction;
        if (tx is null)
        {
            errors.Add("GenesisTransaction is required.");
            return errors;
        }

        // Verify deterministic TxId
        var expectedTxId = ComputeGenesisTxId();
        if (tx.TxId != expectedTxId)
            errors.Add($"TxId mismatch. Expected '{expectedTxId}', got '{tx.TxId}'.");

        if (string.IsNullOrWhiteSpace(tx.Payload))
            errors.Add("GenesisTransaction.Payload is required.");

        if (string.IsNullOrWhiteSpace(tx.PayloadHash))
            errors.Add("GenesisTransaction.PayloadHash is required.");

        if (tx.Signature is null)
        {
            errors.Add("GenesisTransaction.Signature is required.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(tx.Signature.PublicKey))
            errors.Add("Signature.PublicKey is required.");

        if (string.IsNullOrWhiteSpace(tx.Signature.SignatureValue))
            errors.Add("Signature.SignatureValue is required.");

        if (string.IsNullOrWhiteSpace(tx.Signature.Algorithm))
            errors.Add("Signature.Algorithm is required.");

        // Verify fingerprint matches public key
        if (!string.IsNullOrWhiteSpace(tx.Signature.PublicKey) &&
            !string.IsNullOrWhiteSpace(genesis.GenesisPublicKeyFingerprint))
        {
            try
            {
                var publicKeyBytes = Convert.FromBase64String(tx.Signature.PublicKey);
                var expectedFingerprint = GenesisFileLoader.ComputeFingerprint(publicKeyBytes);
                if (genesis.GenesisPublicKeyFingerprint != expectedFingerprint)
                    errors.Add($"Fingerprint mismatch. Expected '{expectedFingerprint}', got '{genesis.GenesisPublicKeyFingerprint}'.");
            }
            catch (FormatException)
            {
                errors.Add("Signature.PublicKey is not valid Base64.");
            }
        }

        // Verify payload hash matches payload
        if (!string.IsNullOrWhiteSpace(tx.Payload) && !string.IsNullOrWhiteSpace(tx.PayloadHash))
        {
            try
            {
                var payloadBytes = Convert.FromBase64String(tx.Payload);
                var computedHash = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();
                if (tx.PayloadHash != computedHash)
                    errors.Add($"PayloadHash mismatch. Computed '{computedHash}', got '{tx.PayloadHash}'.");
            }
            catch (FormatException)
            {
                errors.Add("Payload is not valid Base64.");
            }
        }

        // Validate roster
        if (genesis.ValidatorRoster is null)
            errors.Add("ValidatorRoster is required.");
        else
        {
            var rosterErrors = genesis.ValidatorRoster.Validate();
            foreach (var err in rosterErrors)
                errors.Add($"ValidatorRoster: {err}");
        }

        return errors;
    }

    /// <summary>
    /// Computes the deterministic genesis transaction ID.
    /// </summary>
    public static string ComputeGenesisTxId()
    {
        var input = $"genesis-{SystemRegisterConstants.SystemRegisterId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Checks if the given public key matches the trusted genesis fingerprint.
    /// </summary>
    public static bool MatchesFingerprint(byte[] publicKey, string trustedFingerprint)
    {
        var fingerprint = GenesisFileLoader.ComputeFingerprint(publicKey);
        return string.Equals(fingerprint, trustedFingerprint, StringComparison.OrdinalIgnoreCase);
    }
}
