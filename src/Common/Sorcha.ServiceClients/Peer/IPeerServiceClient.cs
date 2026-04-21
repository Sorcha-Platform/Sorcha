// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Peer;

/// <summary>
/// Unified client interface for Peer Service operations
/// </summary>
public interface IPeerServiceClient
{
    /// <summary>
    /// Queries for active validators for a register
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active validators with reputation scores</returns>
    Task<List<ValidatorInfo>> QueryValidatorsAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a proposed docket to the peer network for consensus
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="docketId">Docket ID</param>
    /// <param name="docketData">Serialized docket data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishProposedDocketAsync(
        string registerId,
        string docketId,
        byte[] docketData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts a confirmed docket to the peer network
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="docketId">Docket ID</param>
    /// <param name="docketData">Serialized docket data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BroadcastConfirmedDocketAsync(
        string registerId,
        string docketId,
        byte[] docketData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the Peer Service to advertise or remove advertisement for a register
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="isPublic">True to advertise, false to remove advertisement</param>
    /// <param name="name">Human-readable register name to include in advertisements</param>
    /// <param name="description">Register description to include in advertisements</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AdvertiseRegisterAsync(
        string registerId,
        bool isPublic,
        string? name = null,
        string? description = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk advertises or syncs register advertisements with the Peer Service.
    /// Used on startup and during periodic reconciliation.
    /// </summary>
    /// <param name="request">Bulk advertise request with advertisements and full-sync flag</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response with processed/added/updated/removed counts, or null if unavailable</returns>
    Task<BulkAdvertiseResponse?> BulkAdvertiseAsync(
        BulkAdvertiseRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to a register for peer replication via POST /api/registers/{registerId}/subscribe.
    /// </summary>
    /// <param name="registerId">Register ID to subscribe to</param>
    /// <param name="mode">Replication mode: "forward-only" or "full-replica"</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SubscribeToRegisterAsync(
        string registerId,
        string mode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes from a register and stops replication via DELETE /api/registers/{registerId}/subscribe.
    /// </summary>
    /// <param name="registerId">Register ID to unsubscribe from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UnsubscribeFromRegisterAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports validator behavior to Peer Service for reputation scoring
    /// </summary>
    /// <param name="validatorId">Validator ID to report</param>
    /// <param name="behavior">Behavior type (e.g., "ProposedInvalidDocket", "DoubleVote")</param>
    /// <param name="details">Details about the behavior</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ReportValidatorBehaviorAsync(
        string validatorId,
        string behavior,
        string details,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Feature 108. Asks the local Peer.Service to fan out a signed transaction submission to
    /// the peers that can seal it (the register's source peers for subscribed registers; no-op
    /// for locally-owned registers). This is the owner-agnostic counterpart to
    /// <c>IValidatorServiceClient.SubmitTransactionAsync</c> — ActionExecutionService calls both
    /// concurrently and each downstream decides based on its derived relationship.
    /// </summary>
    /// <param name="registerId">Register the transaction targets.</param>
    /// <param name="submissionJson">JSON-encoded <c>TransactionSubmission</c> bytes (opaque to peer-service).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Best-effort distribution result.</returns>
    Task<DistributeTransactionResult> DistributeTransactionAsync(
        string registerId,
        byte[] submissionJson,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a peer-service fan-out for a signed submission (Feature 108).
/// </summary>
/// <param name="TargetPeerCount">Number of peers the submission was attempted against.</param>
/// <param name="AcceptedCount">Number of peers that accepted the submission into their local mempool.</param>
/// <param name="LocallyOwned">
/// True when the local node owns the register — no fan-out was attempted.
/// The caller's concurrent local-validator submission is sufficient in this case.
/// </param>
public sealed record DistributeTransactionResult(
    int TargetPeerCount,
    int AcceptedCount,
    bool LocallyOwned);

/// <summary>
/// Validator information from Peer Service
/// </summary>
public record ValidatorInfo
{
    /// <summary>
    /// Validator ID
    /// </summary>
    public required string ValidatorId { get; init; }

    /// <summary>
    /// gRPC endpoint address
    /// </summary>
    public required string GrpcEndpoint { get; init; }

    /// <summary>
    /// Reputation score (0.0-1.0, where 1.0 is perfect)
    /// </summary>
    public double ReputationScore { get; init; } = 1.0;

    /// <summary>
    /// Whether validator is currently active
    /// </summary>
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// Request body for bulk advertising multiple registers at once.
/// </summary>
public class BulkAdvertiseRequest
{
    public List<AdvertisementItem> Advertisements { get; set; } = [];
    public bool FullSync { get; set; }
}

/// <summary>
/// A single advertisement entry within a bulk request.
/// </summary>
public class AdvertisementItem
{
    public string RegisterId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public long LatestVersion { get; set; }
    public long LatestDocketVersion { get; set; }
}

/// <summary>
/// Response from the bulk advertisement endpoint.
/// </summary>
public class BulkAdvertiseResponse
{
    public int Processed { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
}
