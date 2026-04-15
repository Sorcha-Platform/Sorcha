# Contract: Inbound credential detection in the Wallet Service

**Feature**: 106-register-native-credentials
**Surface**: `IInboundCredentialDetector` + extension to `NotificationDeliveryService.DeliverAsync`
**Layer**: Wallet Service background path — extends existing bloom-filter notification pipeline
**Binds**: FR-005, FR-006, FR-007, FR-008

## Responsibility

When a peer-replicated transaction reaches a node whose bloom filter matches one of the transaction's recipient wallets, the Wallet Service's existing `NotificationDeliveryService.DeliverAsync` is invoked. Feature 106 extends that path to detect when the transaction carries a recipient-encrypted credential offer, decrypt it with the matched local wallet's private key, persist the decoded credential as `PendingAcceptance`, and enrich the outbound SignalR notification so the UI can distinguish a credential arrival from a plain action notification.

## Interface

```csharp
namespace Sorcha.Wallet.Service.Services.Interfaces;

public interface IInboundCredentialDetector
{
    /// <summary>
    /// Inspects a peer-replicated transaction for a recipient-addressed credential
    /// payload targeted at the specified local wallet. Fetches the transaction from
    /// the Register Service, attempts to decrypt any recipient-addressed disclosure
    /// groups with the local wallet's private key, and returns the extracted
    /// credential if one is found.
    ///
    /// Returns null when:
    ///   - The transaction cannot be fetched from the Register Service
    ///   - No disclosure group targets the local wallet
    ///   - Decryption with the local wallet key fails
    ///   - The decrypted payload is not a credential offer shape
    ///   - The credential ID is already present in the local wallet store
    ///     (deduplication — silent no-op on replay)
    ///
    /// MUST NOT throw on any of the above. Any thrown exception from this method
    /// is a bug: the notification pipeline treats a null return as "this wasn't
    /// a credential, keep going with the normal notification flow".
    ///
    /// Logs and metrics:
    ///   - Debug log on every invocation with the wallet address and transaction id
    ///   - Info log on successful extraction with the credential type and issuer DID
    ///   - Warning log (rate-limited) on repeated decryption failures against the
    ///     same wallet — may indicate a key rotation issue
    ///   - OpenTelemetry metric: inbound_credential_detected_total with labels
    ///     { outcome = "extracted" | "skipped" | "duplicate" | "error" }
    /// </summary>
    Task<InboundCredentialExtract?> TryExtractAsync(
        string walletAddress,
        string transactionId,
        string registerId,
        CancellationToken cancellationToken);
}

public sealed record InboundCredentialExtract
{
    public required string CredentialId { get; init; }
    public required string CredentialType { get; init; }
    public required string IssuerDid { get; init; }
    public required string RawToken { get; init; }
    public required string ClaimsJson { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required string TransactionId { get; init; }
    public string? BlueprintId { get; init; }
    public string? InstanceId { get; init; }
}
```

## Implementation shape

### Default `InboundCredentialDetector`

```csharp
internal sealed class InboundCredentialDetector : IInboundCredentialDetector
{
    private readonly IRegisterServiceClient _registerClient;
    private readonly IEncryptionPipelineService _encryptionPipeline;
    private readonly IWalletManager _walletManager;
    private readonly ICredentialRepository _credentialRepository;
    private readonly ILogger<InboundCredentialDetector> _logger;
    private readonly InboundCredentialDetectorMetrics _metrics;

    public async Task<InboundCredentialExtract?> TryExtractAsync(
        string walletAddress, string transactionId, string registerId,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Fetch the sealed transaction
            var tx = await _registerClient.GetTransactionAsync(registerId, transactionId, cancellationToken);
            if (tx is null) { _metrics.RecordSkipped("tx-not-found"); return null; }

            // 2. Find disclosure groups targeting this wallet
            var recipientGroup = FindRecipientDisclosureGroup(tx, walletAddress);
            if (recipientGroup is null) { _metrics.RecordSkipped("no-recipient-disclosure"); return null; }

            // 3. Decrypt via IWalletManager which holds the wallet's private key
            var decryptedBytes = await _walletManager.DecryptRecipientPayloadAsync(
                walletAddress, recipientGroup, cancellationToken);
            if (decryptedBytes is null) { _metrics.RecordSkipped("decrypt-failed"); return null; }

            // 4. Parse the decrypted payload — must match the credential-offer-v1 shape
            var credentialOffer = TryParseCredentialOfferV1(decryptedBytes);
            if (credentialOffer is null) { _metrics.RecordSkipped("not-credential-shape"); return null; }

            // 5. Dedup by credential id
            var existing = await _credentialRepository.GetByIdAsync(credentialOffer.CredentialId, cancellationToken);
            if (existing is not null) { _metrics.RecordSkipped("duplicate"); return null; }

            _metrics.RecordExtracted();
            return credentialOffer;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inbound credential detection failed for wallet {Wallet} tx {Tx}",
                walletAddress, transactionId);
            _metrics.RecordError();
            return null;  // CRITICAL: never propagate — the caller expects graceful null
        }
    }
}
```

### Hook point: `NotificationDeliveryService.DeliverAsync`

A new Step 2b between "Resolve address → wallet → user" (existing) and "Check notification preferences" (existing):

```csharp
// Step 2b: Inbound credential detection (Feature 106)
// If this sealed transaction carries a recipient-addressed credential payload
// targeted at the matched wallet, extract and persist it as PendingAcceptance
// BEFORE notifying the user. The SignalR event carries the CredentialOfferId
// so the UI can render a "new credential" indicator rather than just a plain
// "new action" indicator.
InboundCredentialExtract? credentialExtract = null;
try
{
    credentialExtract = await _inboundCredentialDetector.TryExtractAsync(
        recipientAddress, transactionId, registerId, cancellationToken);

    if (credentialExtract is not null)
    {
        var entity = new CredentialEntity
        {
            Id = credentialExtract.CredentialId,
            Type = credentialExtract.CredentialType,
            IssuerDid = credentialExtract.IssuerDid,
            SubjectDid = $"did:sorcha:wallet:{recipientAddress}",
            RawToken = credentialExtract.RawToken,
            ClaimsJson = credentialExtract.ClaimsJson,
            WalletAddress = recipientAddress,
            IssuedAt = credentialExtract.IssuedAt,
            ExpiresAt = credentialExtract.ExpiresAt,
            Status = CredentialStatus.PendingAcceptance,
            IssuanceTxId = credentialExtract.TransactionId,
            IssuanceBlueprintId = credentialExtract.BlueprintId,
        };
        await _credentialRepository.AddAsync(entity, cancellationToken);
    }
}
catch (Exception ex)
{
    // Non-fatal — log and continue. The notification still fires so the user
    // sees the transaction even if credential detection failed on our side.
    _logger.LogError(ex, "InboundCredentialDetector threw for wallet {Wallet} tx {Tx} — continuing with plain notification",
        recipientAddress, transactionId);
}
```

The rest of `DeliverAsync` is unchanged. If `credentialExtract` is non-null, the subsequent `InboundActionEvent` has its new `CredentialOfferId` property populated so the SignalR consumer can distinguish the two cases.

## `InboundActionEvent` shape extension

Existing `InboundActionEvent` gains one new nullable property:

```csharp
public sealed record InboundActionEvent
{
    // ... existing properties ...

    /// <summary>
    /// When non-null, this event was triggered by a transaction that carries
    /// a new credential offer for the recipient wallet. The credential has
    /// already been persisted to the wallet store with Status = PendingAcceptance
    /// by the time this event fires. UI consumers should surface this as a
    /// "new credential to review" notification and drive the user to the
    /// MyCredentials PENDING tab rather than treating it as a plain action
    /// notification.
    /// </summary>
    public string? CredentialOfferId { get; init; }
}
```

Existing consumers (e.g. the Blazor UI's `EventsHubClient`) check for non-null and fork UI handling accordingly.

## Detection rules (primary + fallback)

### Primary: blueprint action metadata

If the transaction includes an `actionId` + `blueprintId` referencing an action whose `credentialIssuanceConfig.targetAudience == SorchaLocalWallet`, the detector knows this is a credential-offer transaction before attempting decryption. The detector fetches the blueprint via cached `IBlueprintCache` lookup and inspects the action definition.

**Advantages**: Fast short-circuit — no decryption attempt needed if the blueprint declares it. Allows targeted metrics ("how many credential offers are flowing through this node this hour").

### Fallback: decrypted payload shape check

If the blueprint is not yet synced on this node, or the action metadata is missing, the detector falls through to a decryption attempt and checks whether the decrypted payload's first-level field `Type` equals the literal string `"credential-offer-v1"`.

**Advantages**: Works even when the blueprint is out of sync, and protects forward compatibility if future transaction shapes want to deliver credentials via other mechanisms (e.g. direct peer sync of a pre-signed credential envelope).

### Shape tolerance

The detector MUST be tolerant of unknown fields in the decrypted payload. A forward-compatible shape means older detectors see new fields they don't understand and ignore them. The shape version (`credential-offer-v1`) advances only when the required fields themselves change, not when new optional fields are added.

## Metrics (new)

```
inbound_credential_detected_total{outcome="extracted"}
  — count of transactions that yielded a new pending credential
inbound_credential_detected_total{outcome="skipped",reason="no-recipient-disclosure"}
  — bloom-filter false positive or non-credential tx
inbound_credential_detected_total{outcome="skipped",reason="decrypt-failed"}
  — wallet private key couldn't decrypt (not a recipient)
inbound_credential_detected_total{outcome="skipped",reason="not-credential-shape"}
  — decrypted but payload isn't a credential offer
inbound_credential_detected_total{outcome="skipped",reason="duplicate"}
  — already present in local store (replay)
inbound_credential_detected_total{outcome="skipped",reason="tx-not-found"}
  — couldn't fetch the underlying transaction
inbound_credential_detected_total{outcome="error"}
  — unexpected exception in the detector pipeline

inbound_credential_detection_duration_seconds{outcome=...}
  — histogram of detection path latency per outcome
```

The existing `NotificationMetrics` class already exposes the overall notification delivery metrics; the new credential metrics live alongside them in `InboundCredentialDetectorMetrics`.

## Testing contract

- **Unit tests** (new, in `Sorcha.Wallet.Core.Tests` since `Sorcha.Wallet.Service.Tests` has pre-existing constructor failures):
  - Happy path: supply a mock transaction with a recipient disclosure for wallet X, mock wallet manager decrypts successfully, mock repository returns null on existence check, assert extraction succeeds.
  - False positive: supply a transaction with no recipient disclosure → returns null without throwing.
  - Decrypt failure: mock wallet manager returns null → detector returns null.
  - Duplicate: mock repository returns existing row → detector returns null with `duplicate` metric.
  - Malformed shape: decrypted payload doesn't match `credential-offer-v1` → returns null with `not-credential-shape` metric.
  - Exception path: any dependency throws → detector returns null with `error` metric, logs warning, never propagates exception.
- **Integration test**: full path with a real `EncryptionPipelineService` — seal a credential for wallet A, feed the transaction to the detector, assert the extracted credential matches what was sealed.
