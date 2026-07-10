// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Cryptography.Secp256k1.Siwe;

/// <summary>
/// A Sign-In With Ethereum (EIP-4361) message — a prove-control challenge a wallet signs to demonstrate
/// control of its Ethereum address (Feature 180). Required fields: <see cref="Domain"/>,
/// <see cref="Address"/>, <see cref="Uri"/>, <see cref="Version"/>, <see cref="ChainId"/>,
/// <see cref="Nonce"/>, <see cref="IssuedAt"/>.
/// </summary>
public sealed class SiweMessage
{
    /// <summary>Optional URI scheme prefixing the domain (e.g. <c>https</c>).</summary>
    public string? Scheme { get; set; }

    /// <summary>The relying-party domain requesting the sign-in.</summary>
    public required string Domain { get; set; }

    /// <summary>The signer's EIP-55 checksummed Ethereum address.</summary>
    public required string Address { get; set; }

    /// <summary>Optional human-readable statement the user agrees to.</summary>
    public string? Statement { get; set; }

    /// <summary>The URI the sign-in is scoped to.</summary>
    public required string Uri { get; set; }

    /// <summary>SIWE version (currently <c>"1"</c>).</summary>
    public string Version { get; set; } = "1";

    /// <summary>The EIP-155 chain id.</summary>
    public long ChainId { get; set; } = 1;

    /// <summary>A relying-party-issued one-time nonce (replay protection).</summary>
    public required string Nonce { get; set; }

    /// <summary>ISO-8601 issuance timestamp.</summary>
    public required string IssuedAt { get; set; }

    /// <summary>Optional ISO-8601 expiry; the message is invalid after this instant.</summary>
    public string? ExpirationTime { get; set; }

    /// <summary>Optional ISO-8601 not-before; the message is invalid before this instant.</summary>
    public string? NotBefore { get; set; }

    /// <summary>Optional relying-party request id.</summary>
    public string? RequestId { get; set; }

    /// <summary>Optional list of resources the sign-in authorises.</summary>
    public IReadOnlyList<string>? Resources { get; set; }
}
