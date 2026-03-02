// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

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
    public required string DocketId { get; init; }
    public required string RegisterId { get; init; }
    public required long DocketNumber { get; init; }
    public string? PreviousHash { get; init; }
    public required string DocketHash { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required List<TransactionModel> Transactions { get; init; }
    public required string ProposerValidatorId { get; init; }
    public required string MerkleRoot { get; init; }
}

/// <summary>
/// Response from the governance roster endpoint
/// </summary>
public class GovernanceRosterResponse
{
    public string RegisterId { get; set; } = string.Empty;
    public List<RosterMember> Members { get; set; } = [];
    public int MemberCount { get; set; }
    public int ControlTransactionCount { get; set; }
    public string? LastControlTxId { get; set; }
}

/// <summary>
/// A member of the governance roster
/// </summary>
public class RosterMember
{
    public string Subject { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public DateTimeOffset GrantedAt { get; set; }
}
