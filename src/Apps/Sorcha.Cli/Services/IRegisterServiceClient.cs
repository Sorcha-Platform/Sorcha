// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Refit;
using Sorcha.Cli.Models;
using Sorcha.Register.Models;
using Sorcha.Register.Models.LocalRelationship;

namespace Sorcha.Cli.Services;

/// <summary>
/// Refit client interface for the Register Service API.
/// </summary>
public interface IRegisterServiceClient
{
    // --- Registers ---

    /// <summary>
    /// Lists all registers.
    /// </summary>
    [Get("/api/registers")]
    Task<List<Sorcha.Register.Models.Register>> ListRegistersAsync([Header("Authorization")] string authorization);

    /// <summary>
    /// Gets a register by ID.
    /// </summary>
    [Get("/api/registers/{id}")]
    Task<Sorcha.Register.Models.Register> GetRegisterAsync(string id, [Header("Authorization")] string authorization);

    /// <summary>
    /// Deletes a register.
    /// </summary>
    [Delete("/api/registers/{id}")]
    Task DeleteRegisterAsync(string id, [Header("Authorization")] string authorization);

    /// <summary>
    /// Updates a register.
    /// </summary>
    [Put("/api/registers/{id}")]
    Task<Sorcha.Register.Models.Register> UpdateRegisterAsync(string id, [Body] UpdateRegisterRequest request, [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets register statistics.
    /// </summary>
    [Get("/api/registers/stats/count")]
    Task<RegisterCountResponse> GetRegisterStatsAsync([Header("Authorization")] string authorization);

    // --- Two-Phase Register Creation ---

    /// <summary>
    /// Initiates register creation (Phase 1).
    /// </summary>
    [Post("/api/registers/initiate")]
    Task<InitiateRegisterCreationResponse> InitiateRegisterCreationAsync([Body] InitiateRegisterCreationRequest request, [Header("Authorization")] string authorization);

    /// <summary>
    /// Finalizes register creation (Phase 2).
    /// </summary>
    [Post("/api/registers/finalize")]
    Task<FinalizeRegisterCreationResponse> FinalizeRegisterCreationAsync([Body] FinalizeRegisterCreationRequest request, [Header("Authorization")] string authorization);

    // --- Transactions ---

    /// <summary>
    /// Lists all transactions in a register.
    /// </summary>
    [Get("/api/registers/{registerId}/transactions")]
    Task<List<TransactionModel>> ListTransactionsAsync(
        string registerId,
        [Query] int? page,
        [Query] int? pageSize,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets a transaction by ID.
    /// </summary>
    [Get("/api/registers/{registerId}/transactions/{transactionId}")]
    Task<TransactionModel> GetTransactionAsync(
        string registerId,
        string transactionId,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets the lifecycle status of a transaction (active / revoked / superseded).
    /// </summary>
    [Get("/api/registers/{registerId}/transactions/{transactionId}/status")]
    Task<TransactionStatusResponse> GetTransactionStatusAsync(
        string registerId,
        string transactionId,
        [Header("Authorization")] string authorization);

    // --- Trust Hardening (Feature 079) ---

    /// <summary>
    /// Generates a Merkle inclusion proof for a sealed transaction.
    /// </summary>
    [Get("/api/registers/{registerId}/transactions/{txId}/inclusion-proof")]
    Task<MerkleInclusionProof> GetInclusionProofAsync(
        string registerId,
        string txId,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Verifies a standalone Merkle inclusion proof (anonymous endpoint).
    /// </summary>
    [Post("/api/registers/{registerId}/inclusion-proofs/verify")]
    Task<VerifyProofResult> VerifyInclusionProofAsync(
        string registerId,
        [Body] VerifyMerkleInclusionProofRequest request,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets what an approver must sign for a governance proposal (Feature 189 T076).
    /// </summary>
    /// <remarks>
    /// The response carries <b>no digest</b> by design (FR-028) — the client derives it from the
    /// operation it rendered, so a server-supplied digest cannot disagree with what the approver
    /// actually saw. The request and response types are the server's own
    /// (<c>Sorcha.Register.Models</c>), not CLI copies, so there is no wire contract to drift
    /// (CLAUDE.md #18).
    /// </remarks>
    [Get("/api/registers/{registerId}/governance/proposals/{proposalId}/signing-request")]
    Task<GovernanceSigningRequest> GetGovernanceSigningRequestAsync(
        string registerId,
        string proposalId,
        [Query] string approverDid,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Submits a detached approval of a governance proposal (Feature 189 T045).
    /// </summary>
    [Post("/api/registers/{registerId}/governance/proposals/{proposalId}/approve")]
    Task<HttpResponseMessage> ApproveGovernanceProposalAsync(
        string registerId,
        string proposalId,
        [Body] GovernanceApprovalSubmission submission,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Lists governance proposals recorded on a register.
    /// </summary>
    [Get("/api/registers/{registerId}/governance/proposals")]
    Task<HttpResponseMessage> ListGovernanceProposalsAsync(
        string registerId,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Submits a revocation for an existing transaction.
    /// </summary>
    [Post("/api/registers/{registerId}/transactions/revoke")]
    Task<RevokeTransactionResult> RevokeTransactionAsync(
        string registerId,
        [Body] RevokeTransactionRequest request,
        [Header("Authorization")] string authorization);

    // --- Dockets ---

    /// <summary>
    /// Lists all dockets in a register.
    /// </summary>
    [Get("/api/registers/{registerId}/dockets")]
    Task<List<DocketHeader>> ListDocketsAsync(string registerId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets a docket by ID.
    /// </summary>
    [Get("/api/registers/{registerId}/dockets/{docketId}")]
    Task<DocketHeader> GetDocketAsync(string registerId, ulong docketId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets transactions in a docket.
    /// </summary>
    [Get("/api/registers/{registerId}/dockets/{docketId}/transactions")]
    Task<List<TransactionModel>> GetDocketTransactionsAsync(string registerId, ulong docketId, [Header("Authorization")] string authorization);

    // --- Query API ---

    /// <summary>
    /// Queries transactions by wallet address.
    /// </summary>
    [Get("/api/query/wallets/{address}/transactions")]
    Task<PagedQueryResponse<TransactionModel>> QueryByWalletAsync(
        string address,
        [Query] int? page,
        [Query] int? pageSize,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Queries transactions by sender address.
    /// </summary>
    [Get("/api/query/senders/{address}/transactions")]
    Task<PagedQueryResponse<TransactionModel>> QueryBySenderAsync(
        string address,
        [Query] int? page,
        [Query] int? pageSize,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Queries transactions by blueprint ID.
    /// </summary>
    [Get("/api/query/blueprints/{blueprintId}/transactions")]
    Task<PagedQueryResponse<TransactionModel>> QueryByBlueprintAsync(
        string blueprintId,
        [Query] int? page,
        [Query] int? pageSize,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets query statistics.
    /// </summary>
    [Get("/api/query/stats")]
    Task<QueryStatsResponse> GetQueryStatsAsync([Header("Authorization")] string authorization);

    /// <summary>
    /// Executes an OData query.
    /// </summary>
    [Get("/odata/{resource}")]
    Task<HttpResponseMessage> QueryODataAsync(
        string resource,
        [Query("$filter")] string? filter,
        [Query("$orderby")] string? orderby,
        [Query("$top")] int? top,
        [Query("$skip")] int? skip,
        [Query("$select")] string? select,
        [Query("$count")] bool? count,
        [Header("Authorization")] string authorization);

    // --- Policy ---

    /// <summary>
    /// Gets the current register policy.
    /// </summary>
    [Get("/api/registers/{registerId}/policy")]
    Task<HttpResponseMessage> GetPolicyAsync(string registerId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets the register policy version history.
    /// </summary>
    [Get("/api/registers/{registerId}/policy/history")]
    Task<HttpResponseMessage> GetPolicyHistoryAsync(string registerId, [Query] int? page, [Query] int? pageSize, [Header("Authorization")] string authorization);

    /// <summary>
    /// Proposes a policy update for the register.
    /// </summary>
    [Post("/api/registers/{registerId}/policy/update")]
    Task<HttpResponseMessage> ProposePolicyUpdateAsync(string registerId, [Body] PolicyUpdateRequest request, [Header("Authorization")] string authorization);

    // --- System Register ---

    /// <summary>
    /// Gets the system register status.
    /// </summary>
    [Get("/api/system-register")]
    Task<HttpResponseMessage> GetSystemRegisterStatusAsync([Header("Authorization")] string authorization);

    /// <summary>
    /// Gets blueprints published to the system register.
    /// </summary>
    [Get("/api/system-register/blueprints")]
    Task<HttpResponseMessage> GetSystemRegisterBlueprintsAsync([Query] int? page, [Query] int? pageSize, [Header("Authorization")] string authorization);

    // --- Sync Diagnostics (Feature 108) ---

    /// <summary>
    /// Gets the local node's derived relationship (role set) for a register.
    /// </summary>
    [Get("/api/registers/{registerId}/local-relationship")]
    Task<RegisterLocalRelationship> GetLocalRelationshipAsync(string registerId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets a register's sync state (indeterminate / syncing / caught up / error).
    /// </summary>
    [Get("/api/registers/{registerId}/sync-state")]
    Task<RegisterSyncStateView> GetSyncStateAsync(string registerId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets recovery sync health across all registers hosted by the node.
    /// </summary>
    [Get("/health/sync")]
    Task<SyncHealthResponse> GetSyncHealthAsync([Header("Authorization")] string authorization);

}

// --- Request/Response DTOs ---

/// <summary>
/// Request to update a register.
/// </summary>
public class UpdateRegisterRequest
{
    public string? Name { get; set; }
    public string? Status { get; set; }
    public bool? Advertise { get; set; }
}

/// <summary>
/// Response from register statistics.
/// </summary>
/// <summary>
/// Response of <c>GET /api/registers/stats/count</c> — the register count only.
/// </summary>
/// <remarks>
/// Named <c>RegisterCountResponse</c> to stop it colliding with the unrelated
/// <c>Sorcha.ServiceClients.Http</c> <c>RegisterCountResponse</c>, which is a DIFFERENT endpoint's
/// type (the org-scoped dashboard stats, carrying registerCount + transactionCount). The two share
/// nothing but a name; the CLI's single-count shape is correct for the endpoint it actually calls,
/// so the fix is to disambiguate, not to "align" it to a contract it never uses.
/// </remarks>
public class RegisterCountResponse
{
    /// <summary>Total number of registers.</summary>
    public int Count { get; set; }
}

// SubmitTransactionRequest / SubmitTransactionResponse were invented DTOs for a `transaction
// submit` command that could never work: the endpoint consumes a full signed TransactionModel,
// not a flat {payload, signature}. The command now explains that and refuses; the DTOs are gone.

/// <summary>
/// Paged query response.
/// </summary>
public class PagedQueryResponse<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// Query statistics response.
/// </summary>
public class QueryStatsResponse
{
    public long TotalTransactions { get; set; }
    public int TotalRegisters { get; set; }
    public int TotalDockets { get; set; }
}

// PolicyUpdateRequest previously had a SECOND definition here, flat and different again from the
// one in Sorcha.Cli.Models. Two shapes for one request body inside a single assembly, neither
// matching the server. The canonical one lives in Sorcha.Cli.Models.RegisterPolicy.cs and carries
// the real Sorcha.Register.Models.RegisterPolicy.

/// <summary>
/// Request to verify a standalone Merkle inclusion proof.
/// Mirrors the Register Service's VerifyMerkleInclusionProofRequest.
/// </summary>
public class VerifyMerkleInclusionProofRequest
{
    public string TransactionHash { get; set; } = string.Empty;
    public string MerkleRoot { get; set; } = string.Empty;
    public IReadOnlyList<MerkleProofStep> ProofPath { get; set; } = new List<MerkleProofStep>();

    /// <summary>
    /// The docket this proof claims inclusion in, so the server can cross-check the folded root
    /// against the one its proposing validator sealed (issue #1372).
    /// </summary>
    /// <remarks>
    /// Optional on the wire, but the CLI always sends it: every proof it verifies came from a file
    /// that carries the docket number, and without it the server can only confirm the arithmetic —
    /// a proof path always folds to SOME root.
    /// </remarks>
    public long? DocketNumber { get; set; }
}

/// <summary>
/// Result of verifying a Merkle inclusion proof.
/// </summary>
public class VerifyProofResult
{
    public bool IsValid { get; set; }
    public string ComputedRoot { get; set; } = string.Empty;

    /// <summary>
    /// Whether the folded root is the one this register sealed: <c>"verified"</c>, <c>"failed"</c>,
    /// or <c>null</c> when the check could not run (issue #1372). Null on an older node too, which
    /// is why it must be reported as unknown rather than assumed sound.
    /// </summary>
    public string? LedgerAnchored { get; set; }

    /// <summary>Why the anchor check did not run, or why it failed. Null when it succeeded.</summary>
    public string? LedgerAnchorReason { get; set; }
}

/// <summary>
/// Request to revoke a transaction. The reason is sent as a string and parsed
/// server-side against the RevocationReason enum.
/// </summary>
public class RevokeTransactionRequest
{
    public string OriginalTxId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? SupersededByTxId { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public string? SignerWalletAddress { get; set; }
}

/// <summary>
/// Accepted-revocation result returned by the revoke endpoint.
/// </summary>
public class RevokeTransactionResult
{
    public string RevocationTxId { get; set; } = string.Empty;
    public string OriginalTxId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Recovery sync health across all registers on the node (GET /health/sync).
/// </summary>
public class SyncHealthResponse
{
    public string Status { get; set; } = string.Empty;
    public List<RegisterSyncStatus> Registers { get; set; } = new();
    public DateTimeOffset CheckedAt { get; set; }
}

/// <summary>
/// Per-register recovery sync status row.
/// </summary>
public class RegisterSyncStatus
{
    public string RegisterId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long CurrentDocket { get; set; }
    public long TargetDocket { get; set; }
    public int ProgressPercent { get; set; }
    public long DocketsProcessed { get; set; }
    public string? LastError { get; set; }
    public bool IsStale { get; set; }
}

