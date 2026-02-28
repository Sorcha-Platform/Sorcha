// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;

namespace Sorcha.Cryptography.Utilities;

/// <summary>
/// Implements wallet address and key formatting utilities.
/// </summary>
public class WalletUtilities : IWalletUtilities
{
    private const string WalletPrefix = "ws1"; // Sorcha classical wallet prefix
    private const string PqcWalletPrefix = "ws2"; // Sorcha PQC wallet prefix (Bech32m)

    private static bool IsPqcNetwork(byte network) =>
        network is (byte)WalletNetworks.ML_DSA_65
            or (byte)WalletNetworks.SLH_DSA_128s
            or (byte)WalletNetworks.SLH_DSA_192s
            or (byte)WalletNetworks.ML_KEM_768;

    /// <summary>
    /// Converts a public key to a wallet address.
    /// For PQC networks, the large public key is SHA-256 hashed and encoded with Bech32m (ws2 prefix).
    /// For classical networks, the raw public key is encoded with Bech32 (ws1 prefix).
    /// </summary>
    public string? PublicKeyToWallet(byte[] publicKey, byte network)
    {
        try
        {
            if (publicKey == null || publicKey.Length == 0)
                return null;

            if (IsPqcNetwork(network))
            {
                // PQC: SHA-256(network_byte + public_key) → Bech32m with ws2 prefix
                byte[] toHash = new byte[publicKey.Length + 1];
                toHash[0] = network;
                Array.Copy(publicKey, 0, toHash, 1, publicKey.Length);
                byte[] hash = SHA256.HashData(toHash);

                // Prepend network byte to hash for decoding
                byte[] data = new byte[hash.Length + 1];
                data[0] = network;
                Array.Copy(hash, 0, data, 1, hash.Length);

                return Bech32m.Encode(PqcWalletPrefix, data);
            }

            // Classical: raw public key with Bech32
            byte[] classicalData = new byte[publicKey.Length + 1];
            classicalData[0] = network;
            Array.Copy(publicKey, 0, classicalData, 1, publicKey.Length);

            return Bech32.Encode(WalletPrefix, classicalData);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a wallet address to public key (or hash) and network.
    /// For ws2 (PQC) addresses, returns the SHA-256 hash of the public key — the full key
    /// must be obtained from transaction witness data.
    /// </summary>
    public (byte Network, byte[] PublicKey)? WalletToPublicKey(string walletAddress)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(walletAddress))
                return null;

            // Try ws2 (PQC/Bech32m) first
            if (walletAddress.StartsWith(PqcWalletPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var decoded = Bech32m.Decode(walletAddress);
                if (decoded == null)
                    return null;

                var (hrp, data) = decoded.Value;
                if (hrp != PqcWalletPrefix || data.Length < 2)
                    return null;

                byte network = data[0];
                byte[] publicKeyHash = data.Skip(1).ToArray();
                return (network, publicKeyHash);
            }

            // Classical ws1/Bech32
            var bech32Decoded = Bech32.Decode(walletAddress);
            if (bech32Decoded == null)
                return null;

            var (bhrp, bdata) = bech32Decoded.Value;
            if (bhrp != WalletPrefix || bdata.Length < 2)
                return null;

            byte bnetwork = bdata[0];
            byte[] publicKey = bdata.Skip(1).ToArray();
            return (bnetwork, publicKey);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Validates multiple wallet addresses.
    /// </summary>
    public (bool[] Valid, CryptoKey[] ValidWallets) ValidateWallets(string[] walletAddresses)
    {
        if (walletAddresses == null || walletAddresses.Length == 0)
            return (Array.Empty<bool>(), Array.Empty<CryptoKey>());

        var validList = new List<bool>();
        var validWalletsList = new List<CryptoKey>();

        foreach (string address in walletAddresses)
        {
            var result = WalletToPublicKey(address);
            bool isValid = result.HasValue;

            validList.Add(isValid);

            if (result.HasValue)
            {
                var (network, publicKey) = result.Value;
                var cryptoKey = new CryptoKey((WalletNetworks)network, publicKey);
                validWalletsList.Add(cryptoKey);
            }
        }

        return (validList.ToArray(), validWalletsList.ToArray());
    }

    /// <summary>
    /// Converts a private key to WIF (Wallet Import Format).
    /// </summary>
    public string? PrivateKeyToWIF(byte[] privateKey, byte network)
    {
        try
        {
            if (privateKey == null || privateKey.Length == 0)
                return null;

            // Build WIF data: [version byte][private key]
            // Version byte indicates network
            byte[] wifData = new byte[privateKey.Length + 1];
            wifData[0] = network;
            Array.Copy(privateKey, 0, wifData, 1, privateKey.Length);

            // Encode with Base58Check
            return Base58.EncodeCheck(wifData);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts WIF to private key and network.
    /// </summary>
    public (byte Network, byte[] PrivateKey)? WIFToPrivateKey(string wif)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(wif))
                return null;

            // Decode Base58Check
            byte[]? decoded = Base58.DecodeCheck(wif);
            if (decoded == null || decoded.Length < 2)
                return null;

            // Extract network byte and private key
            byte network = decoded[0];
            byte[] privateKey = decoded.Skip(1).ToArray();

            return (network, privateKey);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException or OverflowException)
        {
            return null;
        }
    }
}
