// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// EF Core entity for stored verifiable credentials in a wallet.
/// </summary>
public class CredentialEntity
{
    /// <summary>
    /// Primary key — unique credential identifier (DID URI).
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Credential type (e.g., "LicenseCredential", "IdentityAttestation").
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// DID URI or wallet address of the credential issuer.
    /// </summary>
    public required string IssuerDid { get; set; }

    /// <summary>
    /// DID URI or wallet address of the credential subject (holder).
    /// </summary>
    public required string SubjectDid { get; set; }

    /// <summary>
    /// All credential claims stored as JSON.
    /// </summary>
    public required string ClaimsJson { get; set; }

    /// <summary>
    /// When the credential was issued.
    /// </summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>
    /// When the credential expires. Null means no expiry.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// The complete SD-JWT VC raw token (with all disclosures).
    /// </summary>
    public required string RawToken { get; set; }

    /// <summary>
    /// Credential status: Active, Revoked, Expired.
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Ledger transaction ID that recorded the issuance event.
    /// </summary>
    public string? IssuanceTxId { get; set; }

    /// <summary>
    /// Blueprint ID of the blueprint flow that issued this credential.
    /// </summary>
    public string? IssuanceBlueprintId { get; set; }

    /// <summary>
    /// The wallet address this credential is stored under.
    /// </summary>
    public required string WalletAddress { get; set; }

    /// <summary>
    /// When the credential was stored in this wallet.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Usage policy: Reusable, SingleUse, or LimitedUse.
    /// </summary>
    public string UsagePolicy { get; set; } = "Reusable";

    /// <summary>
    /// Maximum presentations allowed (only for LimitedUse policy).
    /// </summary>
    public int? MaxPresentations { get; set; }

    /// <summary>
    /// Number of times the credential has been successfully presented.
    /// </summary>
    public int PresentationCount { get; set; }

    /// <summary>
    /// URL of the issuer's Bitstring Status List endpoint.
    /// </summary>
    public string? StatusListUrl { get; set; }

    /// <summary>
    /// Index position in the issuer's status list bitstring.
    /// </summary>
    public int? StatusListIndex { get; set; }

    /// <summary>
    /// Serialized CredentialDisplayConfig JSON for wallet rendering.
    /// </summary>
    public string? DisplayConfigJson { get; set; }
}
