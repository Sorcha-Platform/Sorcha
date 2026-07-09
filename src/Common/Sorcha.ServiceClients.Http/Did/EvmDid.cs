// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Shared, offline helpers for the address-form EVM DID resolvers (<c>did:pkh</c> / <c>did:ethr</c>,
/// Feature 178): shape validation for Ethereum addresses and decimal chain ids, the recovery
/// verification-method type, and a small known-network table for <c>did:ethr</c> named networks.
/// </summary>
internal static class EvmDid
{
    /// <summary>The W3C verification-method type for an address-only secp256k1 recovery key.</summary>
    internal const string RecoveryMethodType = "EcdsaSecp256k1RecoveryMethod2020";

    private static readonly IReadOnlyDictionary<string, long> KnownNetworks =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["mainnet"] = 1,
            ["goerli"] = 5,
            ["sepolia"] = 11155111,
            ["polygon"] = 137,
        };

    /// <summary>True if <paramref name="value"/> is a 0x-prefixed 40-hex-char Ethereum address.</summary>
    internal static bool IsAddress(string value)
    {
        if (value.Length != 42 || !value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return false;

        for (var i = 2; i < value.Length; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
                return false;
        }

        return true;
    }

    /// <summary>True if <paramref name="value"/> is a non-empty decimal chain id.</summary>
    internal static bool IsChainId(string value) =>
        value.Length > 0 && value.All(char.IsAsciiDigit);

    /// <summary>
    /// Resolve a <c>did:ethr</c> network segment (a known name like <c>mainnet</c>/<c>sepolia</c>, or a
    /// <c>0x</c>-prefixed hex chain id) to its decimal chain id. Returns null for unknown/malformed input.
    /// </summary>
    internal static long? ResolveNetworkChainId(string network)
    {
        if (KnownNetworks.TryGetValue(network, out var known))
            return known;

        if (network.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(network.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
            && hex > 0)
        {
            return hex;
        }

        return null;
    }
}
