// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Peer.Service.Communication.Models;

/// <summary>
/// Request to sync dockets from a peer's register via relay
/// </summary>
public class RegisterSyncRequest
{
    /// <summary>
    /// GUID for request/response correlation
    /// </summary>
    public required string CorrelationId { get; set; }

    /// <summary>
    /// Target register to sync
    /// </summary>
    public required string RegisterId { get; set; }

    /// <summary>
    /// Start pulling from this docket version (default 0 = from genesis)
    /// </summary>
    public long FromDocketVersion { get; set; }

    /// <summary>
    /// Maximum dockets per response batch (default 50, range 1-500)
    /// </summary>
    public int MaxDockets { get; set; } = 50;
}

/// <summary>
/// Response containing a batch of dockets from a relay sync request
/// </summary>
public class RegisterSyncResponse
{
    /// <summary>
    /// Matches the request CorrelationId
    /// </summary>
    public required string CorrelationId { get; set; }

    /// <summary>
    /// Register that was synced
    /// </summary>
    public required string RegisterId { get; set; }

    /// <summary>
    /// Batch of docket data
    /// </summary>
    public List<DocketEntry> Dockets { get; set; } = new();

    /// <summary>
    /// True if more dockets are available beyond this batch
    /// </summary>
    public bool HasMore { get; set; }
}

/// <summary>
/// A single docket entry in a relay sync response
/// </summary>
public class DocketEntry
{
    /// <summary>
    /// Docket version number
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// Serialized docket data
    /// </summary>
    public required byte[] Data { get; set; }

    /// <summary>
    /// Hash of docket content
    /// </summary>
    public required string DocketHash { get; set; }

    /// <summary>
    /// Hash chain link to previous docket
    /// </summary>
    public required string PreviousHash { get; set; }

    /// <summary>
    /// 64-char hex SHA-256 transaction IDs in this docket
    /// </summary>
    public List<string> TransactionIds { get; set; } = new();

    /// <summary>
    /// Unix timestamp in milliseconds
    /// </summary>
    public long CreatedAt { get; set; }
}

/// <summary>
/// Request to retrieve transaction data by IDs via relay
/// </summary>
public class TransactionDataRequest
{
    /// <summary>
    /// GUID for request/response correlation
    /// </summary>
    public required string CorrelationId { get; set; }

    /// <summary>
    /// Register containing the transactions
    /// </summary>
    public required string RegisterId { get; set; }

    /// <summary>
    /// 64-char hex SHA-256 transaction IDs to retrieve
    /// </summary>
    public List<string> TransactionIds { get; set; } = new();
}

/// <summary>
/// Response containing transaction data from a relay request
/// </summary>
public class TransactionDataResponse
{
    /// <summary>
    /// Matches the request CorrelationId
    /// </summary>
    public required string CorrelationId { get; set; }

    /// <summary>
    /// Register containing the transactions
    /// </summary>
    public required string RegisterId { get; set; }

    /// <summary>
    /// Transaction data entries
    /// </summary>
    public List<TransactionEntry> Transactions { get; set; } = new();
}

/// <summary>
/// Feature 143. A signed transaction submission forwarded to a NAT'd register owner over its
/// reverse stream so the owner's validator can seal it (relay transport for the submission path
/// that <c>TransactionDistribution.SubmitTransaction</c> serves over direct channels).
/// </summary>
public class SubmitTransactionRelayRequest
{
    /// <summary>GUID for request/response correlation.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>Target register the transaction belongs to.</summary>
    public required string RegisterId { get; set; }

    /// <summary>Serialized <c>TransactionSubmission</c> JSON (camelCase) — the full signed submission.</summary>
    public required byte[] SubmissionJson { get; set; }

    /// <summary>Peer id of the submitting node (origin), for audit.</summary>
    public string OriginPeerId { get; set; } = string.Empty;
}

/// <summary>
/// Feature 143. Result of a relayed submission, returned to the submitter over the reverse stream.
/// Mirrors the direct-path <c>SubmitTransactionResponse</c>.
/// </summary>
public class SubmitTransactionRelayResponse
{
    /// <summary>Matches the request CorrelationId.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>Register the transaction belonged to.</summary>
    public required string RegisterId { get; set; }

    /// <summary>True if the owner's validator accepted the submission into its mempool.</summary>
    public bool Accepted { get; set; }

    /// <summary>Rejection reason when not accepted (empty on success).</summary>
    public string RejectReason { get; set; } = string.Empty;
}

/// <summary>
/// A single transaction entry in a relay data response
/// </summary>
public class TransactionEntry
{
    /// <summary>
    /// 64-char hex SHA-256 hash
    /// </summary>
    public required string TransactionId { get; set; }

    /// <summary>
    /// Full transaction payload
    /// </summary>
    public required byte[] Data { get; set; }

    /// <summary>
    /// Integrity checksum
    /// </summary>
    public required string Checksum { get; set; }

    /// <summary>
    /// Unix timestamp in milliseconds
    /// </summary>
    public long CreatedAt { get; set; }
}
