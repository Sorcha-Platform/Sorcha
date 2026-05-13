// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Models;
using Sorcha.UI.Core.Models.Explorer;
using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of <see cref="IDocketService"/> calling the Register Service API.
/// </summary>
public class DocketService : IDocketService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DocketService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DocketService(HttpClient httpClient, ILogger<DocketService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<DocketViewModel>> GetDocketsAsync(string registerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var dtos = await _httpClient.GetFromJsonAsync<List<DocketDto>>(
                $"/api/registers/{registerId}/dockets", JsonOptions, cancellationToken);

            if (dtos is null or { Count: 0 })
                return [];

            var sorted = dtos.OrderBy(d => d.Id).ToList();
            var viewModels = new List<DocketViewModel>(sorted.Count);

            for (var i = 0; i < sorted.Count; i++)
            {
                var dto = sorted[i];
                var isIntegrityValid = i == 0
                    ? string.IsNullOrEmpty(dto.PreviousHash)
                    : dto.PreviousHash == sorted[i - 1].Hash;

                viewModels.Add(MapToViewModel(dto, isIntegrityValid));
            }

            return viewModels;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dockets for register {RegisterId}", registerId);
            return [];
        }
    }

    public async Task<DocketViewModel?> GetDocketAsync(string registerId, string docketId, CancellationToken cancellationToken = default)
    {
        try
        {
            var dto = await _httpClient.GetFromJsonAsync<DocketDto>(
                $"/api/registers/{registerId}/dockets/{docketId}", JsonOptions, cancellationToken);

            return dto is not null ? MapToViewModel(dto, true) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching docket {DocketId}", docketId);
            return null;
        }
    }

    public async Task<List<TransactionViewModel>> GetDocketTransactionsAsync(string registerId, string docketId, CancellationToken cancellationToken = default)
    {
        try
        {
            var transactions = await _httpClient.GetFromJsonAsync<List<TransactionModel>>(
                $"/api/registers/{registerId}/dockets/{docketId}/transactions", JsonOptions, cancellationToken);

            return transactions?.Select(MapTransactionToViewModel).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching transactions for docket {DocketId}", docketId);
            return [];
        }
    }

    public async Task<DocketViewModel?> GetLatestDocketAsync(string registerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var dto = await _httpClient.GetFromJsonAsync<DocketDto>(
                $"/api/registers/{registerId}/dockets/latest", JsonOptions, cancellationToken);

            return dto is not null ? MapToViewModel(dto, true) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching latest docket for register {RegisterId}", registerId);
            return null;
        }
    }

    private static DocketViewModel MapToViewModel(DocketDto dto, bool isIntegrityValid)
    {
        return new DocketViewModel
        {
            DocketId = dto.Id.ToString(),
            RegisterId = dto.RegisterId,
            Version = (int)dto.Id,
            Hash = dto.Hash,
            PreviousHash = dto.PreviousHash,
            TransactionCount = dto.TransactionIds.Count,
            TransactionIds = dto.TransactionIds,
            CreatedAt = new DateTimeOffset(dto.TimeStamp, TimeSpan.Zero),
            IsIntegrityValid = isIntegrityValid,
            State = MapDocketState(dto.State)
        };
    }

    private static string MapDocketState(int state) => state switch
    {
        0 => "Init",
        1 => "Proposed",
        2 => "Accepted",
        3 => "Rejected",
        4 => "Sealed",
        _ => $"Unknown ({state})"
    };

    private static TransactionViewModel MapTransactionToViewModel(TransactionModel tx)
    {
        return new TransactionViewModel
        {
            TxId = tx.TxId,
            RegisterId = tx.RegisterId,
            SenderWallet = tx.SenderWallet,
            RecipientsWallets = tx.RecipientsWallets?.ToList() ?? [],
            TimeStamp = tx.TimeStamp,
            DocketNumber = tx.DocketNumber,
            PayloadCount = tx.PayloadCount,
            Payloads = MapPayloads(tx.Payloads),
            Signature = tx.Signature,
            PrevTxId = tx.PrevTxId,
            Version = tx.Version,
            BlueprintId = tx.MetaData?.BlueprintId,
            InstanceId = tx.MetaData?.InstanceId,
            ActionId = tx.MetaData?.ActionId
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
    /// DTO matching the backend Docket JSON (camelCase serialization).
    /// </summary>
    private record DocketDto
    {
        public ulong Id { get; init; }
        public string RegisterId { get; init; } = "";
        public string PreviousHash { get; init; } = "";
        public string Hash { get; init; } = "";
        public List<string> TransactionIds { get; init; } = [];
        public DateTime TimeStamp { get; init; }
        public int State { get; init; }
    }
}
