// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Lifecycle status of a credential held in a Sorcha wallet. See
/// <c>specs/106-register-native-credentials/data-model.md §2</c> for the full state machine and
/// invariants INV-1 through INV-4 enforced by <c>ICredentialRepository.PatchStatusAsync</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CredentialStatus
{
    /// <summary>The credential is valid and presentable.</summary>
    Active = 0,

    /// <summary>Embedded <c>notValidAfter</c> has passed. Terminal.</summary>
    Expired = 1,

    /// <summary>Issuer revoked via status list. Terminal.</summary>
    Revoked = 2,

    /// <summary>Temporarily suspended by the issuer. May transition back to <see cref="Active"/>.</summary>
    Suspended = 3,

    /// <summary>
    /// Feature 106: Inbound credential has been detected and stored by the Wallet Service but the
    /// holder has not yet accepted it. Initial state for register-native deliveries. Transitions
    /// to <see cref="Active"/> on accept, <see cref="Declined"/> on decline, or <see cref="Expired"/>
    /// if the embedded <c>notValidAfter</c> passes before the holder acts.
    /// </summary>
    PendingAcceptance = 4,

    /// <summary>
    /// Feature 106: Holder explicitly declined a <see cref="PendingAcceptance"/> credential. The
    /// row is retained for audit (INV-3) until the holder issues an explicit
    /// <c>DELETE /api/v1/wallets/{address}/credentials/{id}</c>. Terminal.
    /// </summary>
    Declined = 5,

    /// <summary>
    /// Single-use or limited-use credential whose presentation budget has been exhausted.
    /// Pre-existing behaviour preserved from <c>CredentialStore.RecordPresentationAsync</c>.
    /// Terminal.
    /// </summary>
    Consumed = 6
}

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
    /// Credential lifecycle status. Persisted as its string name via EF value conversion.
    /// See <see cref="CredentialStatus"/> for allowed transitions.
    /// </summary>
    public required CredentialStatus Status { get; set; }

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
