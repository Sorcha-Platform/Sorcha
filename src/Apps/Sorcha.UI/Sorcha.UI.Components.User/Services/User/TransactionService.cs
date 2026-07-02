// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Models;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// HTTP client implementation for Transaction API operations.
/// </summary>
public class TransactionService : ITransactionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TransactionService> _logger;

    public TransactionService(HttpClient httpClient, ILogger<TransactionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TransactionListResponse> GetTransactionsAsync(
        string registerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/api/registers/{Uri.EscapeDataString(registerId)}/transactions?page={page}&pageSize={pageSize}";

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch transactions for register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return new TransactionListResponse();
            }

            var apiResponse = await response.Content.ReadFromJsonAsync<ApiTransactionListResponse>(
                JsonDefaults.Api, cancellationToken);

            if (apiResponse == null)
            {
                return new TransactionListResponse();
            }

            return new TransactionListResponse
            {
                Page = apiResponse.Page,
                PageSize = apiResponse.PageSize,
                Total = apiResponse.Total,
                Transactions = apiResponse.Transactions?.Select(t => MapToViewModel(t)).ToList() ?? []
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching transactions for register {RegisterId}", registerId);
            return new TransactionListResponse();
        }
    }

    /// <inheritdoc />
    public async Task<TransactionViewModel?> GetTransactionAsync(
        string registerId,
        string txId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/api/registers/{Uri.EscapeDataString(registerId)}/transactions/{Uri.EscapeDataString(txId)}";

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }

                _logger.LogWarning("Failed to fetch transaction {TxId} in register {RegisterId}: {StatusCode}",
                    txId, registerId, response.StatusCode);
                return null;
            }

            var transaction = await response.Content.ReadFromJsonAsync<TransactionModel>(
                JsonDefaults.Api, cancellationToken);

            return transaction != null ? MapToViewModel(transaction) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching transaction {TxId} in register {RegisterId}", txId, registerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<TransactionQueryResponse> QueryByWalletAsync(
        string walletAddress,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/api/register/query/wallets/{Uri.EscapeDataString(walletAddress)}/transactions?page={page}&pageSize={pageSize}";

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to query transactions by wallet {WalletAddress}: {StatusCode}",
                    walletAddress, response.StatusCode);
                return new TransactionQueryResponse();
            }

            var apiResponse = await response.Content.ReadFromJsonAsync<ApiTransactionQueryResponse>(
                JsonDefaults.Api, cancellationToken);

            if (apiResponse == null)
            {
                return new TransactionQueryResponse();
            }

            return new TransactionQueryResponse
            {
                Page = apiResponse.Page,
                PageSize = apiResponse.PageSize,
                TotalCount = apiResponse.TotalCount,
                Items = apiResponse.Items?.Select(item => new TransactionQueryResultItem
                {
                    Transaction = MapToViewModel(item.Transaction, walletAddress),
                    RegisterId = item.RegisterId,
                    RegisterName = item.RegisterName
                }).ToList() ?? []
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying transactions by wallet {WalletAddress}", walletAddress);
            return new TransactionQueryResponse();
        }
    }

    /// <inheritdoc />
    public async Task<TransactionGraphResponse> GetTransactionGraphAsync(
        string registerId, int limit = 200, string? before = null, CancellationToken ct = default)
    {
        try
        {
            var url = $"/api/registers/{Uri.EscapeDataString(registerId)}/transactions/graph?limit={limit}";
            if (!string.IsNullOrEmpty(before))
            {
                url += $"&before={Uri.EscapeDataString(before)}";
            }

            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch transaction graph for register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return new TransactionGraphResponse { RegisterId = registerId };
            }

            var apiResponse = await response.Content.ReadFromJsonAsync<ApiTransactionGraphResponse>(
                JsonDefaults.Api, ct);

            if (apiResponse is null)
            {
                return new TransactionGraphResponse { RegisterId = registerId };
            }

            return new TransactionGraphResponse
            {
                RegisterId = apiResponse.RegisterId ?? registerId,
                TotalCount = apiResponse.TotalCount,
                HasMore = apiResponse.HasMore,
                Nodes = apiResponse.Nodes?.Select(MapToGraphNode).ToArray() ?? []
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching transaction graph for register {RegisterId}", registerId);
            return new TransactionGraphResponse { RegisterId = registerId };
        }
    }

    private static TransactionGraphNode MapToGraphNode(ApiGraphNode apiNode)
    {
        // Map transaction type int to string using the same logic as TransactionViewModel
        var transactionType = apiNode.TransactionType switch
        {
            0 => "Control",
            1 => "Action",
            2 => "Docket",
            3 => "Participant",
            _ => apiNode.ActionId.HasValue
                ? "Action"
                : !string.IsNullOrEmpty(apiNode.BlueprintId)
                    ? "Blueprint"
                    : "Transfer"
        };

        return new TransactionGraphNode
        {
            TxId = apiNode.TxId ?? "",
            PrevTxId = apiNode.PrevTxId ?? "",
            SenderWallet = apiNode.SenderWallet ?? "",
            TimeStamp = apiNode.TimeStamp,
            DocketNumber = apiNode.DocketNumber,
            BlueprintId = apiNode.BlueprintId,
            InstanceId = apiNode.InstanceId,
            TransactionType = transactionType
        };
    }

    private static TransactionViewModel MapToViewModel(TransactionModel transaction, string? currentWalletAddress = null)
    {
        // Derive lifecycle fields heuristically from available register data.
        // The Register Service does not store wallet-specific lifecycle state (State, ReceiptId, etc.),
        // so we infer what we can from the transaction's structural properties.
        var isConfirmed = transaction.DocketNumber.HasValue;
        var state = isConfirmed ? "Confirmed" : "Pending";
        var confirmedAt = isConfirmed ? transaction.TimeStamp : (DateTime?)null;

        // Direction: compare sender to the wallet address being queried
        string? direction = null;
        string? counterparty = null;
        if (!string.IsNullOrEmpty(currentWalletAddress))
        {
            var isSender = string.Equals(transaction.SenderWallet, currentWalletAddress, StringComparison.OrdinalIgnoreCase);
            direction = isSender ? "Outbound" : "Inbound";
            counterparty = isSender
                ? transaction.RecipientsWallets?.FirstOrDefault()
                : transaction.SenderWallet;
        }

        return new TransactionViewModel
        {
            TxId = transaction.TxId,
            RegisterId = transaction.RegisterId,
            SenderWallet = transaction.SenderWallet,
            RecipientsWallets = transaction.RecipientsWallets?.ToList() ?? [],
            TimeStamp = transaction.TimeStamp,
            DocketNumber = transaction.DocketNumber,
            PayloadCount = transaction.PayloadCount,
            Payloads = MapPayloads(transaction.Payloads),
            Signature = transaction.Signature,
            PrevTxId = transaction.PrevTxId,
            Version = transaction.Version,
            BlueprintId = transaction.MetaData?.BlueprintId,
            InstanceId = transaction.MetaData?.InstanceId,
            ActionId = transaction.MetaData?.ActionId,
            MetadataTransactionType = transaction.MetaData != null ? (int?)transaction.MetaData.TransactionType : null,
            State = state,
            ConfirmedAt = confirmedAt,
            BlockHeight = transaction.DocketNumber,
            Direction = direction,
            CounterpartyAddress = counterparty
        };
    }

    private static IReadOnlyList<PayloadViewModel> MapPayloads(PayloadModel[]? payloads)
    {
        if (payloads is null or { Length: 0 })
            return [];

        return payloads.Select((p, i) => new PayloadViewModel
        {
            Index = i,
            Hash = p.Hash,
            PayloadSize = p.PayloadSize,
            WalletAccess = p.WalletAccess?.ToList() ?? [],
            PayloadFlags = p.PayloadFlags,
            HasIV = p.IV is not null,
            ChallengeCount = p.Challenges?.Length ?? 0,
            Data = p.Data?.Trim()
        }).ToList();
    }

    /// <summary>
    /// API response model for transaction list endpoint
    /// </summary>
    private record ApiTransactionListResponse
    {
        public int Page { get; init; }
        public int PageSize { get; init; }
        public int Total { get; init; }
        public List<TransactionModel>? Transactions { get; init; }
    }

    /// <summary>
    /// API response model for transaction query endpoint
    /// </summary>
    private record ApiTransactionQueryResponse
    {
        public int Page { get; init; }
        public int PageSize { get; init; }
        public int TotalCount { get; init; }
        public List<ApiTransactionQueryItem>? Items { get; init; }
    }

    /// <summary>
    /// API item in query response
    /// </summary>
    private record ApiTransactionQueryItem
    {
        public required TransactionModel Transaction { get; init; }
        public required string RegisterId { get; init; }
        public required string RegisterName { get; init; }
    }

    /// <summary>
    /// API response model for transaction graph endpoint
    /// </summary>
    private record ApiTransactionGraphResponse
    {
        public string? RegisterId { get; init; }
        public ApiGraphNode[]? Nodes { get; init; }
        public int TotalCount { get; init; }
        public bool HasMore { get; init; }
    }

    /// <summary>
    /// API node model for transaction graph
    /// </summary>
    private record ApiGraphNode
    {
        public string? TxId { get; init; }
        public string? PrevTxId { get; init; }
        public string? SenderWallet { get; init; }
        public DateTime TimeStamp { get; init; }
        public ulong? DocketNumber { get; init; }
        public string? BlueprintId { get; init; }
        public string? InstanceId { get; init; }
        public uint? ActionId { get; init; }
        public int? TransactionType { get; init; }
    }
}
