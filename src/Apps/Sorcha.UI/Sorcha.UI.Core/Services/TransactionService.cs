// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Models;
using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// HTTP client implementation for Transaction API operations.
/// </summary>
public class TransactionService : ITransactionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TransactionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
                JsonOptions, cancellationToken);

            if (apiResponse == null)
            {
                return new TransactionListResponse();
            }

            return new TransactionListResponse
            {
                Page = apiResponse.Page,
                PageSize = apiResponse.PageSize,
                Total = apiResponse.Total,
                Transactions = apiResponse.Transactions?.Select(MapToViewModel).ToList() ?? []
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
                JsonOptions, cancellationToken);

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
                JsonOptions, cancellationToken);

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
                    Transaction = MapToViewModel(item.Transaction),
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

    private static TransactionViewModel MapToViewModel(TransactionModel transaction)
    {
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
            MetadataTransactionType = transaction.MetaData != null ? (int?)transaction.MetaData.TransactionType : null
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
            Data = p.Data
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
}
