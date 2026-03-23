// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Sorcha.Register.Models;

/// <summary>
/// The type of Sorcha DID identifier
/// </summary>
public enum SorchaDidType
{
    /// <summary>
    /// Wallet-based DID: did:sorcha:w:{walletAddress}
    /// </summary>
    Wallet,

    /// <summary>
    /// Register-based DID: did:sorcha:r:{registerId}:t:{transactionId}
    /// </summary>
    Register,

    /// <summary>
    /// Organization-based DID: did:sorcha:org:{walletAddress}
    /// </summary>
    Organization
}

/// <summary>
/// Value object representing a Sorcha DID identifier.
/// Supports three formats:
/// - Wallet: did:sorcha:w:{walletAddress}
/// - Register: did:sorcha:r:{registerId}:t:{transactionId}
/// - Organization: did:sorcha:org:{walletAddress}
/// </summary>
public sealed class SorchaDidIdentifier : IEquatable<SorchaDidIdentifier>
{
    private const string Prefix = "did:sorcha:";
    private static readonly Regex WalletPattern = new(@"^did:sorcha:w:([A-Za-z1-9]+)$", RegexOptions.Compiled);
    private static readonly Regex RegisterPattern = new(@"^did:sorcha:r:([a-f0-9]{32}):t:([a-f0-9]{64})$", RegexOptions.Compiled);
    private static readonly Regex OrganizationPattern = new(@"^did:sorcha:org:([A-Za-z1-9]+)$", RegexOptions.Compiled);

    /// <summary>
    /// The DID type (Wallet or Register)
    /// </summary>
    public SorchaDidType Type { get; }

    /// <summary>
    /// The wallet address (for Wallet DIDs) or register ID (for Register DIDs)
    /// </summary>
    public string Locator { get; }

    /// <summary>
    /// The transaction ID (only for Register DIDs, null for Wallet DIDs)
    /// </summary>
    public string? TransactionId { get; }

    private SorchaDidIdentifier(SorchaDidType type, string locator, string? transactionId = null)
    {
        Type = type;
        Locator = locator;
        TransactionId = transactionId;
    }

    /// <summary>
    /// Creates a wallet DID from a wallet address
    /// </summary>
    public static SorchaDidIdentifier FromWallet(string walletAddress)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty.", nameof(walletAddress));

        return new SorchaDidIdentifier(SorchaDidType.Wallet, walletAddress);
    }

    /// <summary>
    /// Creates an organization DID from an organization's wallet address
    /// </summary>
    public static SorchaDidIdentifier FromOrganization(string walletAddress)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty.", nameof(walletAddress));

        return new SorchaDidIdentifier(SorchaDidType.Organization, walletAddress);
    }

    /// <summary>
    /// Creates a register DID from a register ID and transaction ID
    /// </summary>
    public static SorchaDidIdentifier FromRegister(string registerId, string transactionId)
    {
        if (string.IsNullOrWhiteSpace(registerId))
            throw new ArgumentException("Register ID cannot be empty.", nameof(registerId));
        if (string.IsNullOrWhiteSpace(transactionId))
            throw new ArgumentException("Transaction ID cannot be empty.", nameof(transactionId));

        return new SorchaDidIdentifier(SorchaDidType.Register, registerId, transactionId);
    }

    /// <summary>
    /// Parses a DID string into a SorchaDidIdentifier
    /// </summary>
    public static SorchaDidIdentifier Parse(string did)
    {
        if (!TryParse(did, out var result))
            throw new FormatException($"Invalid Sorcha DID format: '{did}'");

        return result;
    }

    /// <summary>
    /// Attempts to parse a DID string into a SorchaDidIdentifier
    /// </summary>
    public static bool TryParse(string? did, [NotNullWhen(true)] out SorchaDidIdentifier? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(did) || !did.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var orgMatch = OrganizationPattern.Match(did);
        if (orgMatch.Success)
        {
            result = new SorchaDidIdentifier(SorchaDidType.Organization, orgMatch.Groups[1].Value);
            return true;
        }

        var walletMatch = WalletPattern.Match(did);
        if (walletMatch.Success)
        {
            result = new SorchaDidIdentifier(SorchaDidType.Wallet, walletMatch.Groups[1].Value);
            return true;
        }

        var registerMatch = RegisterPattern.Match(did);
        if (registerMatch.Success)
        {
            result = new SorchaDidIdentifier(
                SorchaDidType.Register,
                registerMatch.Groups[1].Value,
                registerMatch.Groups[2].Value);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether the given string is a valid Sorcha DID
    /// </summary>
    public static bool IsValid(string? did) => TryParse(did, out _);

    /// <summary>
    /// Returns the canonical string representation of the DID
    /// </summary>
    public override string ToString() => Type switch
    {
        SorchaDidType.Wallet => $"did:sorcha:w:{Locator}",
        SorchaDidType.Register => $"did:sorcha:r:{Locator}:t:{TransactionId}",
        SorchaDidType.Organization => $"did:sorcha:org:{Locator}",
        _ => throw new InvalidOperationException($"Unknown DID type: {Type}")
    };

    public bool Equals(SorchaDidIdentifier? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Type == other.Type
            && Locator == other.Locator
            && TransactionId == other.TransactionId;
    }

    public override bool Equals(object? obj) => Equals(obj as SorchaDidIdentifier);

    public override int GetHashCode() => HashCode.Combine(Type, Locator, TransactionId);

    public static bool operator ==(SorchaDidIdentifier? left, SorchaDidIdentifier? right) =>
        Equals(left, right);

    public static bool operator !=(SorchaDidIdentifier? left, SorchaDidIdentifier? right) =>
        !Equals(left, right);
}
