// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;
using Sorcha.Register.Models.LocalRelationship;
using Sorcha.Register.Models.Observations;

namespace Sorcha.ServiceClients.Register;

/// <summary>
/// Unified client interface for Register Service operations
/// </summary>
/// <remarks>
/// This interface combines all Register Service operations needed across all consuming services:
/// - Validator Service: Docket read/write, chain height queries
/// - Blueprint Service: Transaction submission, queries, instance tracking
/// - CLI: Transaction and register queries
///
/// All methods use gRPC when available, falling back to HTTP REST endpoints.
/// </remarks>
public interface IRegisterServiceClient
{
    // =========================================================================
    // Docket Operations (Validator Service)
    // =========================================================================

    /// <summary>
    /// Writes a confirmed docket to the Register Service
    /// </summary>
    /// <param name="docket">Confirmed docket with consensus signatures</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if write succeeded</returns>
    /// <remarks>
    /// Used by Validator Service to persist confirmed dockets.
    /// Only validators can write dockets.
    /// </remarks>
    Task<bool> WriteDocketAsync(
        DocketModel docket,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Receipt Operations
    // =========================================================================

    /// <summary>
    /// Writes a batch of receipts to the Register Service.
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="docketNumber">Docket number the receipts belong to</param>
    /// <param name="receipts">Transaction receipts to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if write succeeded</returns>
    Task<bool> WriteReceiptBatchAsync(
        string registerId,
        long docketNumber,
        TransactionReceipt[] receipts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a docket by number from the Register Service
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="docketNumber">Docket number (0 = genesis)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Docket, or null if not found</returns>
    Task<DocketModel?> ReadDocketAsync(
        string registerId,
        long docketNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the latest docket for a register
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Latest docket, or null if register is empty</returns>
    Task<DocketModel?> ReadLatestDocketAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current height (latest docket number) for a register
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Latest docket number, or -1 if register is empty</returns>
    Task<long> GetRegisterHeightAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Sync Status Reporting (Peer Service)
    // =========================================================================

    /// <summary>
    /// Reports peer sync state changes for a register.
    /// Maps sync state to RegisterStatus and updates the register accordingly.
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="syncState">Peer sync state (Subscribing, Syncing, FullyReplicated, Active, Error)</param>
    /// <param name="peerConnectionActive">Whether at least one source peer is connected</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ReportSyncStatusAsync(
        string registerId,
        string syncState,
        bool peerConnectionActive,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Transaction Operations (Blueprint Service, CLI)
    // =========================================================================

    /// <summary>
    /// Submits a transaction to a register
    /// </summary>
    /// <param name="registerId">Register ID to submit to</param>
    /// <param name="transaction">Transaction to submit</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stored transaction with confirmation</returns>
    /// <remarks>
    /// Used by Blueprint Service to submit workflow action transactions.
    /// Transaction goes to memory pool awaiting validation.
    /// </remarks>
    Task<TransactionModel> SubmitTransactionAsync(
        string registerId,
        TransactionModel transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a transaction by ID from a register
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="transactionId">Transaction ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transaction, or null if not found</returns>
    Task<TransactionModel?> GetTransactionAsync(
        string registerId,
        string transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a paginated list of transactions from a register
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of transactions per page</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated transaction list</returns>
    Task<TransactionPage> GetTransactionsAsync(
        string registerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets transactions for a specific wallet address
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="walletAddress">Wallet address to query</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of transactions per page</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated transaction list</returns>
    Task<TransactionPage> GetTransactionsByWalletAsync(
        string registerId,
        string walletAddress,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets transactions that reference a given previous transaction ID.
    /// Used for fork detection and chain integrity auditing.
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="prevTxId">Previous transaction ID to query</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of transactions per page</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated transaction list</returns>
    Task<TransactionPage> GetTransactionsByPrevTxIdAsync(
        string registerId,
        string prevTxId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all transactions associated with a workflow instance
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="instanceId">Workflow instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of transactions for the instance, ordered by execution time</returns>
    /// <remarks>
    /// Used by Blueprint Service for state reconstruction during action execution.
    /// Returns all transactions that belong to the same workflow instance.
    /// </remarks>
    Task<List<TransactionModel>> GetTransactionsByInstanceIdAsync(
        string registerId,
        string instanceId,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Governance Operations
    // =========================================================================

    /// <summary>
    /// Gets all Control transactions for a register (governance operations)
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of transactions per page</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated list of Control transactions</returns>
    Task<TransactionPage> GetControlTransactionsAsync(
        string registerId,
        int page = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a governance proposal (add/remove member, transfer ownership)
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="request">Governance proposal details</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Proposal response with transaction ID, or null on failure</returns>
    Task<Models.GovernanceProposalResponse?> ProposeGovernanceOperationAsync(
        string registerId,
        Models.GovernanceProposalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paginated governance proposals from Control TX history
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of proposals per page</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated list of governance proposals</returns>
    Task<Models.GovernanceProposalPage> GetGovernanceProposalsAsync(
        string registerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Blueprint Publishing (Blueprint Service → Register Service)
    // =========================================================================

    /// <summary>
    /// Gets the governance roster for a register
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Governance roster response, or null if not found</returns>
    Task<GovernanceRosterResponse?> GetGovernanceRosterAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a blueprint to a register
    /// </summary>
    /// <param name="registerId">Target register ID</param>
    /// <param name="blueprintId">Blueprint ID</param>
    /// <param name="blueprintJson">Serialized blueprint JSON</param>
    /// <param name="publishedBy">Publisher identity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if published successfully</returns>
    Task<bool> PublishBlueprintToRegisterAsync(
        string registerId,
        string blueprintId,
        string blueprintJson,
        string publishedBy,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Participant Query Operations
    // =========================================================================

    /// <summary>
    /// Gets a paginated list of published participants on a register
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="top">Number of items to return</param>
    /// <param name="statusFilter">Status filter (active, deprecated, revoked, all)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated participant list</returns>
    Task<Sorcha.ServiceClients.Register.Models.ParticipantPage> GetPublishedParticipantsAsync(
        string registerId,
        int skip = 0,
        int top = 20,
        string? statusFilter = "active",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a published participant by wallet address
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="walletAddress">Wallet address to look up</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Published participant record, or null if not found</returns>
    Task<Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord?> GetPublishedParticipantByAddressAsync(
        string registerId,
        string walletAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a published participant by ID
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="participantId">Participant ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Published participant record, or null if not found</returns>
    Task<Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord?> GetPublishedParticipantByIdAsync(
        string registerId,
        string participantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a participant by blueprint role ID and optional organisation name.
    /// Used for dynamic participant resolution at action execution time.
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="participantId">Blueprint participant/role ID</param>
    /// <param name="orgName">Optional organisation name filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Published participant record, or null if not found. Returns null if revoked.</returns>
    Task<Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord?> ResolveParticipantAsync(
        string registerId,
        string participantId,
        string? orgName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a participant's public key by wallet address for field-level encryption
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="walletAddress">Wallet address to resolve</param>
    /// <param name="algorithm">Optional algorithm filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Public key resolution, or null if not found. Throws if revoked (410).</returns>
    Task<Sorcha.ServiceClients.Register.Models.PublicKeyResolution?> ResolvePublicKeyAsync(
        string registerId,
        string walletAddress,
        string? algorithm = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch resolves public keys for multiple wallet addresses
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="request">Batch request with wallet addresses and optional algorithm filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Batch response with resolved, not-found, and revoked addresses</returns>
    Task<Sorcha.ServiceClients.Register.Models.BatchPublicKeyResponse> ResolvePublicKeysBatchAsync(
        string registerId,
        Sorcha.ServiceClients.Register.Models.BatchPublicKeyRequest request,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // System Register Operations
    // =========================================================================

    /// <summary>
    /// Checks whether a blueprint exists in the system register
    /// </summary>
    /// <param name="blueprintId">Blueprint identifier to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the blueprint exists and is active in the system register</returns>
    Task<bool> SystemRegisterBlueprintExistsAsync(
        string blueprintId,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Recovery / Internal Discovery
    // =========================================================================

    /// <summary>
    /// Gets all registers via the internal discovery endpoint (no auth).
    /// Used by Blueprint Service during startup recovery.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of register summaries, or empty list on failure</returns>
    Task<List<InternalRegisterInfo>> GetInternalRegistersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the Register Service of a subscription change (subscribe or unsubscribe).
    /// Called by the Tenant Service after creating or removing a register subscription.
    /// Uses the internal anonymous endpoint — no auth header required.
    /// </summary>
    /// <param name="request">Subscription notification with action, registerId, and optional metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response with current sync state, or null on failure</returns>
    Task<SubscriptionNotificationResponse?> NotifySubscriptionAsync(
        SubscriptionNotificationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets published blueprints for a register (recovery/discovery).
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Published blueprints response, or null on failure</returns>
    Task<PublishedBlueprintsResponse?> GetPublishedBlueprintsAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Register Management (All Services)
    // =========================================================================

    /// <summary>
    /// Gets register information by ID
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Register information, or null if not found</returns>
    Task<Sorcha.Register.Models.Register?> GetRegisterAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new register
    /// </summary>
    /// <param name="registerId">Unique register ID</param>
    /// <param name="name">Register name</param>
    /// <param name="blueprintId">Associated blueprint ID</param>
    /// <param name="owner">Owner principal</param>
    /// <param name="tenant">Tenant ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created register information</returns>
    Task<Sorcha.Register.Models.Register> CreateRegisterAsync(
        string registerId,
        string name,
        string blueprintId,
        string owner,
        string tenant,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Policy Operations
    // =========================================================================

    /// <summary>
    /// Gets the current policy for a register
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Register policy response, or null if not found</returns>
    Task<RegisterPolicyResponse?> GetRegisterPolicyAsync(
        string registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the policy version history for a register
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of versions per page</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Policy history response, or null if not found</returns>
    Task<PolicyHistoryResponse?> GetPolicyHistoryAsync(
        string registerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    // =========================================================================
    // Feature 108 — Local relationship + sync state + observation intake
    // =========================================================================

    /// <summary>
    /// Reports a peer-advert height observation to Register.Service for inclusion in the
    /// sync-state network-height high-water-mark (Feature 108).
    /// </summary>
    Task ReportPeerHeightAsync(
        PeerHeightObservation observation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports a validator sealing progress observation to Register.Service (Feature 108).
    /// Rejected by the server if the caller's validator key is not on the register's roster.
    /// </summary>
    Task ReportValidatorSealingAsync(
        ValidatorSealingObservation observation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the local node's derived role set for the register (Feature 108).
    /// </summary>
    /// <returns>Relationship record, or null when the register is not held locally.</returns>
    Task<RegisterLocalRelationship?> GetLocalRelationshipAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current typed sync state view for the register (Feature 108).
    /// </summary>
    /// <returns>Sync-state view, or null when the register is not held locally.</returns>
    Task<RegisterSyncStateView?> GetSyncStateAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists register IDs on whose roster the caller's validator public key appears (Feature 108).
    /// Used by Validator.Service at startup and on relationship-change events to seed its
    /// monitoring enrolment. The public key is passed via the <c>X-Validator-Public-Key</c> header
    /// by the client implementation.
    /// </summary>
    Task<IReadOnlyList<string>> GetMyValidatedRegistersAsync(
        byte[] validatorPublicKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch register-service statistics. Optional <paramref name="registerIds"/> scopes the counts
    /// to the listed registers (used by Tenant Service to build org-scoped dashboard stats).
    /// </summary>
    /// <param name="registerIds">Optional register-id filter; null/empty returns platform-wide counts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Register count + transaction count, scoped per the filter.</returns>
    Task<RegisterStatsResponse> GetStatsAsync(
        IReadOnlyList<string>? registerIds = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Register-service statistics payload.
/// </summary>
public class RegisterStatsResponse
{
    /// <summary>Count of registers (platform-wide or, when filtered, the listed register count).</summary>
    public int RegisterCount { get; set; }

    /// <summary>Sum of transaction counts across the in-scope registers.</summary>
    public int TransactionCount { get; set; }
}

/// <summary>
/// Paginated transaction results
/// </summary>
public class TransactionPage
{
    /// <summary>
    /// Current page number (1-based)
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Total number of transactions across all pages
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;

    /// <summary>
    /// Transactions for this page
    /// </summary>
    public List<TransactionModel> Transactions { get; set; } = new();
}

/// <summary>
/// Docket model used by Validator Service
/// </summary>
/// <remarks>
/// This is a simplified docket model for the consolidated client.
/// Validator Service has its own more detailed Docket model.
/// </remarks>
public class DocketModel
{
    /// <summary>Identifier of the docket.</summary>
    public required string DocketId { get; init; }
    /// <summary>Identifier of the register.</summary>
    public required string RegisterId { get; init; }
    /// <summary>Numeric value for docket number.</summary>
    public required long DocketNumber { get; init; }
    /// <summary>The previous hash.</summary>
    public string? PreviousHash { get; init; }
    /// <summary>The docket hash.</summary>
    public required string DocketHash { get; init; }
    /// <summary>Server timestamp when the record was created (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
    /// <summary>Collection of transactions associated with this resource.</summary>
    public required List<TransactionModel> Transactions { get; init; }
    /// <summary>Identifier of the proposer validator.</summary>
    public required string ProposerValidatorId { get; init; }
    /// <summary>The merkle root.</summary>
    public required string MerkleRoot { get; init; }
}

/// <summary>
/// Response from the governance roster endpoint
/// </summary>
public class GovernanceRosterResponse
{
    /// <summary>Identifier of the register.</summary>
    public string RegisterId { get; set; } = string.Empty;
    /// <summary>Collection of members associated with this resource.</summary>
    public List<RosterMember> Members { get; set; } = [];
    /// <summary>Numeric value for member count.</summary>
    public int MemberCount { get; set; }
    /// <summary>Numeric value for control transaction count.</summary>
    public int ControlTransactionCount { get; set; }
    /// <summary>Identifier of the last control tx.</summary>
    public string? LastControlTxId { get; set; }
}

/// <summary>
/// A member of the governance roster
/// </summary>
public class RosterMember
{
    /// <summary>The subject.</summary>
    public string Subject { get; set; } = string.Empty;
    /// <summary>The role.</summary>
    public string Role { get; set; } = string.Empty;
    /// <summary>Cryptographic algorithm identifier.</summary>
    public string Algorithm { get; set; } = string.Empty;
    /// <summary>Timestamp at which granted occurred (UTC).</summary>
    public DateTimeOffset GrantedAt { get; set; }
}

/// <summary>
/// Response containing the current policy for a register
/// </summary>
public class RegisterPolicyResponse
{
    /// <summary>
    /// Register ID the policy belongs to
    /// </summary>
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// The current register policy, or null if no policy is set
    /// </summary>
    public RegisterPolicy? Policy { get; set; }

    /// <summary>
    /// Whether this is the default policy (no custom policy has been set)
    /// </summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// Paginated response containing policy version history for a register
/// </summary>
public class PolicyHistoryResponse
{
    /// <summary>
    /// Register ID the policy history belongs to
    /// </summary>
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// List of policy version entries
    /// </summary>
    public List<PolicyVersionEntry> Versions { get; set; } = [];

    /// <summary>
    /// Current page number (1-based)
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Total number of versions across all pages
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages { get; set; }
}

/// <summary>
/// A single version entry in the policy history
/// </summary>
public class PolicyVersionEntry
{
    /// <summary>
    /// Policy version number
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// The policy at this version
    /// </summary>
    public RegisterPolicy? Policy { get; set; }

    /// <summary>
    /// Transaction ID that committed this policy version
    /// </summary>
    public string? TxId { get; set; }

    /// <summary>
    /// When this policy version was committed
    /// </summary>
    public DateTimeOffset CommittedAt { get; set; }
}

/// <summary>
/// Minimal register info returned by the internal discovery endpoint
/// </summary>
public class InternalRegisterInfo
{
    /// <summary>Unique identifier for the resource.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Human-readable name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Numeric value for height.</summary>
    public long Height { get; set; }
    /// <summary>Current status of the resource.</summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Response from the published blueprints recovery endpoint
/// </summary>
public class PublishedBlueprintsResponse
{
    /// <summary>Identifier of the register.</summary>
    public string RegisterId { get; set; } = string.Empty;
    /// <summary>Collection of blueprints associated with this resource.</summary>
    public List<PublishedBlueprintEntry> Blueprints { get; set; } = [];
    /// <summary>Numeric value for register height.</summary>
    public long RegisterHeight { get; set; }
    /// <summary>Timestamp at which queried occurred (UTC).</summary>
    public DateTimeOffset QueriedAt { get; set; }
}

/// <summary>
/// A single published blueprint entry
/// </summary>
public class PublishedBlueprintEntry
{
    /// <summary>Identifier of the blueprint.</summary>
    public string BlueprintId { get; set; } = string.Empty;
    /// <summary>Identifier of the transaction.</summary>
    public string TransactionId { get; set; } = string.Empty;
    /// <summary>The published by.</summary>
    public string PublishedBy { get; set; } = string.Empty;
    /// <summary>Timestamp at which published occurred (UTC).</summary>
    public DateTimeOffset PublishedAt { get; set; }
    /// <summary>The blueprint json.</summary>
    public string BlueprintJson { get; set; } = string.Empty;
}
