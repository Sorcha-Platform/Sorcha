// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Feature 106 — detects register-native credentials sealed into recipient-addressed
/// disclosure groups on inbound action transactions. Called from
/// <c>NotificationDeliveryService.DeliverAsync</c> step 2b (between wallet/user
/// resolution and preference routing) so the detector runs exactly once per
/// bloom-filter hit, without requiring the Peer Service to replay transactions.
/// </summary>
/// <remarks>
/// Contract: <c>specs/106-register-native-credentials/contracts/inbound-credential-detection.md</c>.
/// The implementation MUST never throw — failure cases return <c>null</c> and are
/// counted via <c>InboundCredentialDetectorMetrics</c>. A successful extraction
/// persists a <c>CredentialEntity</c> in the wallet store with
/// <c>Status = CredentialStatus.PendingAcceptance</c> and returns the extract so
/// the caller can enrich the SignalR <c>InboundActionEvent</c>.
/// </remarks>
public interface IInboundCredentialDetector
{
    /// <summary>
    /// Attempts to extract a register-native credential from the given transaction
    /// for the specified wallet. Returns <c>null</c> when the transaction carries
    /// no recipient-addressed credential disclosure, when decryption fails, when
    /// the credential is a duplicate of an already-stored row, or when any
    /// dependency throws — inspect the metrics for the skip reason.
    /// </summary>
    Task<InboundCredentialExtract?> TryExtractAsync(
        string walletAddress,
        string transactionId,
        string registerId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Snapshot of a successfully extracted register-native credential as returned by
/// <see cref="IInboundCredentialDetector.TryExtractAsync"/>. The wallet store row
/// is already persisted by the time the caller sees this value — this shape exists
/// so <c>NotificationDeliveryService</c> can enrich the outbound SignalR event
/// without a second database read.
/// </summary>
public sealed record InboundCredentialExtract
{
    /// <summary>Unique credential identifier (URN / DID URI).</summary>
    public required string CredentialId { get; init; }

    /// <summary>Credential type (e.g. <c>VerifiedCitizenCredential</c>).</summary>
    public required string CredentialType { get; init; }

    /// <summary>DID URI or wallet address of the credential issuer.</summary>
    public required string IssuerDid { get; init; }

    /// <summary>Raw SD-JWT VC token as extracted from the disclosure.</summary>
    public required string RawToken { get; init; }

    /// <summary>Human-readable name of the issuing organisation.</summary>
    public string? IssuerOrgName { get; init; }

    /// <summary>Serialised JSON of the credential claims.</summary>
    public required string ClaimsJson { get; init; }

    /// <summary>Issuance timestamp embedded in the credential.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>
    /// Optional <c>notValidAfter</c> embedded in the credential. When present the
    /// wallet lazily transitions the row to <c>Expired</c> at next read per
    /// data-model invariant INV-5.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Transaction id the credential was extracted from.</summary>
    public required string TransactionId { get; init; }

    /// <summary>
    /// Blueprint id of the issuing flow, if it was carried in the disclosure.
    /// Lets the holder UI deep-link back to the originating instance.
    /// </summary>
    public string? BlueprintId { get; init; }

    /// <summary>Blueprint instance id, when known.</summary>
    public string? InstanceId { get; init; }

    /// <summary>
    /// Feature 106 SC-003: Action id of the credential issuance action.
    /// </summary>
    public string? ActionId { get; init; }

    /// <summary>
    /// Feature 106 SC-003: Action id of the holder's claim action, determined by
    /// the routing result at issuance time.
    /// </summary>
    public string? ClaimActionId { get; init; }

    /// <summary>
    /// Feature 106 SC-003: Register id where the issuance transaction was sealed.
    /// </summary>
    public string? RegisterId { get; init; }
}
