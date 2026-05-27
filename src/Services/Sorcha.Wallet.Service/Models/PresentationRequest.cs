// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;

namespace Sorcha.Wallet.Service.Models;

/// <summary>
/// Represents an OID4VP presentation request from a verifier.
/// </summary>
public class PresentationRequest
{
    /// <summary>Unique identifier for the resource.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>The verifier identity.</summary>
    public required string VerifierIdentity { get; set; }
    /// <summary>The credential type.</summary>
    public required string CredentialType { get; set; }
    /// <summary>Collection of accepted issuers associated with this resource.</summary>
    public string[]? AcceptedIssuers { get; set; }
    /// <summary>Collection of required claims associated with this resource.</summary>
    public ClaimConstraint[]? RequiredClaims { get; set; }
    /// <summary>The nonce.</summary>
    public string Nonce { get; set; } = GenerateNonce();
    /// <summary>The callback url.</summary>
    public required string CallbackUrl { get; set; }
    /// <summary>The target wallet address.</summary>
    public string? TargetWalletAddress { get; set; }
    /// <summary>Current status of the resource.</summary>
    public string Status { get; set; } = PresentationStatus.Pending;
    /// <summary>Server timestamp when the record was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Timestamp at which the record expires (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>The vp token.</summary>
    public string? VpToken { get; set; }
    /// <summary>The verification result.</summary>
    public string? VerificationResult { get; set; }

    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

    private static string GenerateNonce()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }
}

/// <summary>
/// Constraint on a specific claim in a credential.
/// </summary>
public class ClaimConstraint
{
    /// <summary>The claim name.</summary>
    public required string ClaimName { get; set; }
    /// <summary>The expected value.</summary>
    public string? ExpectedValue { get; set; }
}

/// <summary>
/// Valid presentation request statuses.
/// </summary>
public static class PresentationStatus
{
    public const string Pending = "Pending";
    public const string Submitted = "Submitted";
    public const string Verified = "Verified";
    public const string Denied = "Denied";
    public const string Expired = "Expired";

    public static readonly HashSet<string> ValidStatuses =
        [Pending, Submitted, Verified, Denied, Expired];
}

/// <summary>
/// Result of verifying a presentation.
/// </summary>
public class VerificationResult
{
    /// <summary>Indicates whether validation passed.</summary>
    public required bool IsValid { get; set; }
    /// <summary>Map of verified claims keyed by string.</summary>
    public Dictionary<string, object>? VerifiedClaims { get; set; }
    /// <summary>The credential type.</summary>
    public string? CredentialType { get; set; }
    /// <summary>Identifier of the issuer did.</summary>
    public string? IssuerDid { get; set; }
    /// <summary>The status list check.</summary>
    public string? StatusListCheck { get; set; }
    /// <summary>Collection of error details when the operation did not succeed.</summary>
    public List<VerificationError>? Errors { get; set; }
}

/// <summary>
/// A single verification failure.
/// </summary>
public class VerificationError
{
    /// <summary>The requirement type.</summary>
    public required string RequirementType { get; set; }
    /// <summary>The failure reason.</summary>
    public required string FailureReason { get; set; }
    /// <summary>Human-readable message.</summary>
    public required string Message { get; set; }
}
