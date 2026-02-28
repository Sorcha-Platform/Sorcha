// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Refit;
using Sorcha.Register.Models;

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
    Task<RegisterStatsResponse> GetRegisterStatsAsync([Header("Authorization")] string authorization);

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
    /// Submits a new transaction to a register.
    /// </summary>
    [Post("/api/registers/{registerId}/transactions")]
    Task<SubmitTransactionResponse> SubmitTransactionAsync(
        string registerId,
        [Body] SubmitTransactionRequest request,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets the status of a transaction.
    /// </summary>
    [Get("/api/registers/{registerId}/transactions/{transactionId}/status")]
    Task<SubmitTransactionResponse> GetTransactionStatusAsync(
        string registerId,
        string transactionId,
        [Header("Authorization")] string authorization);

    // --- Dockets ---

    /// <summary>
    /// Lists all dockets in a register.
    /// </summary>
    [Get("/api/registers/{registerId}/dockets")]
    Task<List<Docket>> ListDocketsAsync(string registerId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets a docket by ID.
    /// </summary>
    [Get("/api/registers/{registerId}/dockets/{docketId}")]
    Task<Docket> GetDocketAsync(string registerId, ulong docketId, [Header("Authorization")] string authorization);

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
public class RegisterStatsResponse
{
    public int Count { get; set; }
}

/// <summary>
/// Request to submit a new transaction.
/// </summary>
public class SubmitTransactionRequest
{
    public string RegisterId { get; set; } = string.Empty;
    public string TxType { get; set; } = string.Empty;
    public string SenderWallet { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string? PreviousTxId { get; set; }
}

/// <summary>
/// Response after submitting a transaction.
/// </summary>
public class SubmitTransactionResponse
{
    public string TransactionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
}

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

