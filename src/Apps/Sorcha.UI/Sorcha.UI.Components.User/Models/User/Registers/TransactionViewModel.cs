// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Registers;

/// <summary>
/// View model for displaying transaction information in the UI.
/// Wraps the TransactionModel with UI-specific formatting.
/// </summary>
public record TransactionViewModel
{
    /// <summary>
    /// Transaction identifier (64-char hex hash)
    /// </summary>
    public required string TxId { get; init; }

    /// <summary>
    /// Register identifier this transaction belongs to
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Sender wallet address (Base58 encoded)
    /// </summary>
    public required string SenderWallet { get; init; }

    /// <summary>
    /// Recipient wallet addresses
    /// </summary>
    public IReadOnlyList<string> RecipientsWallets { get; init; } = [];

    /// <summary>
    /// Transaction timestamp (UTC)
    /// </summary>
    public DateTime TimeStamp { get; init; }

    /// <summary>
    /// Docket number this transaction is sealed in
    /// </summary>
    public ulong? DocketNumber { get; init; }

    /// <summary>
    /// Number of payloads in transaction
    /// </summary>
    public ulong PayloadCount { get; init; }

    /// <summary>
    /// Payload details for expandable display
    /// </summary>
    public IReadOnlyList<PayloadViewModel> Payloads { get; init; } = [];

    /// <summary>
    /// Cryptographic signature of transaction
    /// </summary>
    public required string Signature { get; init; }

    /// <summary>
    /// Previous transaction ID for blockchain chain
    /// </summary>
    public string? PrevTxId { get; init; }

    /// <summary>
    /// Transaction format version
    /// </summary>
    public uint Version { get; init; } = 1;

    /// <summary>
    /// Blueprint ID from metadata (if present)
    /// </summary>
    public string? BlueprintId { get; init; }

    /// <summary>
    /// Instance ID from metadata (if present)
    /// </summary>
    public string? InstanceId { get; init; }

    /// <summary>
    /// Action ID from metadata (if present)
    /// </summary>
    public uint? ActionId { get; init; }

    /// <summary>
    /// Transaction type from metadata enum (if present).
    /// Maps to TransactionType enum: 0=Control, 1=Action, 2=Docket, 3=Participant
    /// </summary>
    public int? MetadataTransactionType { get; init; }

    /// <summary>Transaction lifecycle state (Pending, Submitted, Confirmed, Receipted).</summary>
    public string? State { get; init; }

    /// <summary>Receipt ID — set when cryptographic receipt is confirmed.</summary>
    public string? ReceiptId { get; init; }

    /// <summary>When the transaction was sealed in a docket.</summary>
    public DateTime? ConfirmedAt { get; init; }

    /// <summary>When the receipt was confirmed (cryptographic proof of finality).</summary>
    public DateTime? ReceiptedAt { get; init; }

    /// <summary>Transaction direction: Outbound or Inbound.</summary>
    public string? Direction { get; init; }

    /// <summary>Block/docket height when confirmed.</summary>
    public ulong? BlockHeight { get; init; }

    /// <summary>Counterparty wallet address (sender for inbound, recipient for outbound).</summary>
    public string? CounterpartyAddress { get; init; }

    /// <summary>
    /// Computed: Formatted timestamp (relative or absolute)
    /// </summary>
    public string TimeStampFormatted => GetFormattedTime(TimeStamp, DateTime.UtcNow);

    /// <summary>
    /// Computed: Whether this transaction is recent (within last 5 seconds)
    /// </summary>
    public bool IsRecent => (DateTime.UtcNow - TimeStamp).TotalSeconds < 5;

    /// <summary>
    /// Computed: Transaction type derived from metadata enum or heuristic fallback
    /// </summary>
    public string TransactionType => MetadataTransactionType switch
    {
        0 => "Control",
        1 => "Action",
        2 => "Docket",
        3 => "Participant",
        _ => ActionId.HasValue
            ? "Action"
            : !string.IsNullOrEmpty(BlueprintId)
                ? "Blueprint"
                : "Transfer"
    };

    /// <summary>
    /// Computed: Truncated TxId for compact display (first 8 chars + ellipsis)
    /// </summary>
    public string TxIdTruncated => TxId.Length > 8 ? $"{TxId[..8]}..." : TxId;

    /// <summary>
    /// Computed: Truncated sender wallet for compact display (first 8 + last 4 chars)
    /// </summary>
    public string SenderTruncated => SenderWallet.Length > 12
        ? $"{SenderWallet[..8]}...{SenderWallet[^4..]}"
        : SenderWallet;

    /// <summary>
    /// Computed: Full DID URI for this transaction
    /// </summary>
    public string DidUri => $"did:sorcha:register:{RegisterId}/tx/{TxId}";

    // <c>now</c> is injected (rather than read from the clock inside) so the
    // calendar-boundary branches below are deterministically unit-testable.
    // Reading DateTime.UtcNow here made the "today" check flaky when a test ran
    // in the first couple of hours of a UTC day (a "2 hours ago" timestamp fell
    // on the previous calendar day). Callers pass DateTime.UtcNow.
    internal static string GetFormattedTime(DateTime dateTime, DateTime now)
    {
        var timeSpan = now - dateTime;

        // For recent transactions, show relative time
        if (timeSpan.TotalMinutes < 60)
        {
            return timeSpan.TotalSeconds < 60
                ? "just now"
                : $"{(int)timeSpan.TotalMinutes}m ago";
        }

        // For today's transactions, show time only
        if (dateTime.Date == now.Date)
        {
            return dateTime.ToString("HH:mm:ss");
        }

        // For older transactions, show full date and time
        return dateTime.ToString("MMM dd, HH:mm");
    }
}
