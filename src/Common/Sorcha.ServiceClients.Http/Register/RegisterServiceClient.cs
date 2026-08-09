// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Models;
using Sorcha.Register.Models.LocalRelationship;
using Sorcha.Register.Models.Observations;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Configuration;
using Sorcha.ServiceClients.Helpers;
using Sorcha.Serialization;

namespace Sorcha.ServiceClients.Register;

/// <summary>
/// HTTP client for Register Service operations with JWT authentication support
/// </summary>
public class RegisterServiceClient : IRegisterServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<RegisterServiceClient> _logger;
    private readonly string _serviceAddress;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RegisterServiceClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        IConfiguration configuration,
        ILogger<RegisterServiceClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _serviceAddress = SorchaServiceAddresses.TryResolve(configuration, SorchaService.Register)
            ?? configuration["GrpcClients:RegisterService:Address"]
            ?? throw new InvalidOperationException("Register Service address not configured");

        // Configure HttpClient base address
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(_serviceAddress.TrimEnd('/') + "/");
        }

        _logger.LogInformation(
            "RegisterServiceClient initialized (Address: {Address}, Protocol: HTTP)",
            _serviceAddress);
    }

    private Task SetAuthHeaderAsync(CancellationToken cancellationToken) =>
        ServiceClientAuthHelper.SetAuthHeaderAsync(
            _httpClient, _serviceAuth, _logger, "Register Service", cancellationToken);

    // =========================================================================
    // Sync Status Reporting
    // =========================================================================

    /// <inheritdoc />
    public async Task ReportSyncStatusAsync(
        string registerId,
        string syncState,
        bool peerConnectionActive,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            var payload = new { registerId, syncState, peerConnectionActive };
            var response = await _httpClient.PostAsJsonAsync(
                "api/internal/register-sync-status",
                payload,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to report sync status for register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Failed to report sync status for register {RegisterId} (non-critical)",
                registerId);
        }
    }

    // =========================================================================
    // Docket Operations
    // =========================================================================

    public async Task<bool> WriteDocketAsync(
        DocketModel docket,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Writing docket {DocketNumber} to register {RegisterId}",
                docket.DocketNumber, docket.RegisterId);

            await SetAuthHeaderAsync(cancellationToken);

            var request = new WriteDocketRequest
            {
                DocketId = docket.DocketId,
                DocketNumber = docket.DocketNumber,
                PreviousHash = docket.PreviousHash,
                DocketHash = docket.DocketHash,
                CreatedAt = docket.CreatedAt,
                TransactionIds = docket.Transactions.Select(t => t.TxId ?? t.Id ?? string.Empty).ToList(),
                ProposerValidatorId = docket.ProposerValidatorId,
                MerkleRoot = docket.MerkleRoot,
                Transactions = docket.Transactions,
                Votes = docket.Votes
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"api/registers/{Uri.EscapeDataString(docket.RegisterId)}/dockets",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to write docket {DocketNumber} to register {RegisterId}: {StatusCode}",
                    docket.DocketNumber, docket.RegisterId, response.StatusCode);
                return false;
            }

            _logger.LogInformation(
                "Successfully wrote docket {DocketNumber} to register {RegisterId}",
                docket.DocketNumber, docket.RegisterId);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error writing docket {DocketNumber} to register {RegisterId}",
                docket.DocketNumber, docket.RegisterId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to write docket {DocketNumber} to register {RegisterId}",
                docket.DocketNumber, docket.RegisterId);
            return false;
        }
    }

    public async Task<bool> WriteReceiptBatchAsync(
        string registerId,
        long docketNumber,
        TransactionReceipt[] receipts,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            var request = new { DocketNumber = docketNumber, Receipts = receipts };
            var response = await _httpClient.PostAsJsonAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/receipts/batch",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to write receipt batch for docket {DocketNumber}: {StatusCode}",
                    docketNumber, response.StatusCode);
                return false;
            }

            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "Failed to write receipt batch for docket {DocketNumber}", docketNumber);
            return false;
        }
    }

    public async Task<DocketModel?> ReadDocketAsync(
        string registerId,
        long docketNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Reading docket {DocketNumber} from register {RegisterId}",
                docketNumber, registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/dockets/{docketNumber}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Docket {DocketNumber} not found in register {RegisterId}", docketNumber, registerId);
                    return null;
                }

                _logger.LogWarning(
                    "Failed to read docket {DocketNumber} from register {RegisterId}: {StatusCode}",
                    docketNumber, registerId, response.StatusCode);
                return null;
            }

            var docket = await response.Content.ReadFromJsonAsync<DocketResponse>(SorchaJson.Options, cancellationToken);
            if (docket == null)
            {
                return null;
            }

            return MapToDocketModel(docket, registerId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error reading docket {DocketNumber} from register {RegisterId}",
                docketNumber, registerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to read docket {DocketNumber} from register {RegisterId}",
                docketNumber, registerId);
            return null;
        }
    }

    public async Task<DocketModel?> ReadLatestDocketAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Reading latest docket from register {RegisterId}", registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/dockets/latest",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("No dockets found for register {RegisterId}", registerId);
                    return null;
                }

                _logger.LogWarning(
                    "Failed to read latest docket from register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return null;
            }

            var docket = await response.Content.ReadFromJsonAsync<DocketResponse>(SorchaJson.Options, cancellationToken);
            if (docket == null)
            {
                return null;
            }

            return MapToDocketModel(docket, registerId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error reading latest docket from register {RegisterId}", registerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read latest docket from register {RegisterId}", registerId);
            return null;
        }
    }

    public async Task<long> GetRegisterHeightAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting register height for {RegisterId}", registerId);

            var register = await GetRegisterAsync(registerId, cancellationToken);
            if (register == null)
            {
                _logger.LogWarning("Register {RegisterId} not found", registerId);
                return -1L;
            }

            return register.Height;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get register height for {RegisterId}", registerId);
            return -1L;
        }
    }

    private static DocketModel MapToDocketModel(DocketResponse docket, string registerId)
    {
        // Build minimal TransactionModel stubs with TxId so callers can enumerate
        // transaction IDs without a separate fetch. Full transaction data is fetched
        // via GetTransactionAsync when needed.
        var txStubs = docket.TransactionIds
            .Select(txId => new TransactionModel { RegisterId = registerId, TxId = txId })
            .ToList();

        return new DocketModel
        {
            DocketId = docket.Id.ToString(),
            RegisterId = registerId,
            DocketNumber = (long)docket.Id,
            PreviousHash = docket.PreviousHash,
            DocketHash = docket.Hash,
            CreatedAt = docket.TimeStamp,
            Transactions = txStubs,
            // Feature 187 (#1371/#1372): read each from its own field. This used to read
            // ProposerValidatorId out of `Votes` (where the write side had smuggled it) and
            // hard-code MerkleRoot to empty because it was not persisted at all. A mapper having to
            // invent values is the tell that the persistence model is under-specified.
            ProposerValidatorId = docket.ProposerValidatorId,
            MerkleRoot = docket.MerkleRoot,
            Votes = docket.Votes
        };
    }

    // =========================================================================
    // Transaction Operations
    // =========================================================================

    public async Task<TransactionModel> SubmitTransactionAsync(
        string registerId,
        TransactionModel transaction,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Submitting transaction {TransactionId} to register {RegisterId}",
                transaction.Id, registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.PostAsJsonAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/transactions",
                transaction,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Failed to submit transaction {TransactionId} to register {RegisterId}: {StatusCode} - {Error}",
                    transaction.Id, registerId, response.StatusCode, error);
                throw new HttpRequestException($"Failed to submit transaction: {response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync<TransactionModel>(SorchaJson.Options, cancellationToken);
            _logger.LogInformation(
                "Successfully submitted transaction {TransactionId} to register {RegisterId}",
                transaction.Id, registerId);
            return result ?? transaction;
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to submit transaction {TransactionId} to register {RegisterId}",
                transaction.Id, registerId);
            throw;
        }
    }

    public async Task<TransactionModel?> GetTransactionAsync(
        string registerId,
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Getting transaction {TransactionId} from register {RegisterId}",
                transactionId, registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/transactions/{Uri.EscapeDataString(transactionId)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Transaction {TransactionId} not found in register {RegisterId}", transactionId, registerId);
                    return null;
                }

                _logger.LogWarning(
                    "Failed to get transaction {TransactionId} from register {RegisterId}: {StatusCode}",
                    transactionId, registerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<TransactionModel>(SorchaJson.Options, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error getting transaction {TransactionId} from register {RegisterId}",
                transactionId, registerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get transaction {TransactionId} from register {RegisterId}",
                transactionId, registerId);
            return null;
        }
    }

    public async Task<TransactionPage> GetTransactionsAsync(
        string registerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var skip = (page - 1) * pageSize;
            _logger.LogDebug(
                "Getting transactions from register {RegisterId} ($skip={Skip}, $top={Top})",
                registerId, skip, pageSize);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/transactions?$skip={skip}&$top={pageSize}&$count=true",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to get transactions from register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return new TransactionPage { Page = page, PageSize = pageSize };
            }

            var result = await response.Content.ReadFromJsonAsync<TransactionPage>(SorchaJson.Options, cancellationToken);
            return result ?? new TransactionPage { Page = page, PageSize = pageSize };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error getting transactions from register {RegisterId}", registerId);
            return new TransactionPage { Page = page, PageSize = pageSize };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get transactions from register {RegisterId}", registerId);
            throw;
        }
    }

    public async Task<TransactionPage> GetTransactionsByWalletAsync(
        string registerId,
        string walletAddress,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Getting transactions for wallet {WalletAddress} from register {RegisterId}",
                walletAddress, registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/query/wallet/{Uri.EscapeDataString(walletAddress)}/transactions/{Uri.EscapeDataString(registerId)}?$skip={(page - 1) * pageSize}&$top={pageSize}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to get transactions for wallet {WalletAddress} from register {RegisterId}: {StatusCode}",
                    walletAddress, registerId, response.StatusCode);
                return new TransactionPage { Page = page, PageSize = pageSize };
            }

            var transactions = await response.Content.ReadFromJsonAsync<List<TransactionModel>>(SorchaJson.Options, cancellationToken);
            return new TransactionPage
            {
                Page = page,
                PageSize = pageSize,
                Transactions = transactions ?? []
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error getting transactions for wallet {WalletAddress} from register {RegisterId}",
                walletAddress, registerId);
            return new TransactionPage { Page = page, PageSize = pageSize };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get transactions for wallet {WalletAddress} from register {RegisterId}",
                walletAddress, registerId);
            throw;
        }
    }

    public async Task<TransactionPage> GetTransactionsByPrevTxIdAsync(
        string registerId,
        string prevTxId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Getting transactions by prevTxId {PrevTxId} from register {RegisterId}",
                prevTxId, registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/query/previous/{Uri.EscapeDataString(prevTxId)}/transactions?registerId={Uri.EscapeDataString(registerId)}&$skip={(page - 1) * pageSize}&$top={pageSize}&$count=true",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug(
                        "No transactions found for prevTxId {PrevTxId} in register {RegisterId}",
                        prevTxId, registerId);
                    return new TransactionPage { Page = page, PageSize = pageSize };
                }

                _logger.LogWarning(
                    "Failed to get transactions by prevTxId {PrevTxId} from register {RegisterId}: {StatusCode}",
                    prevTxId, registerId, response.StatusCode);
                return new TransactionPage { Page = page, PageSize = pageSize };
            }

            var result = await response.Content.ReadFromJsonAsync<PrevTxIdQueryResponse>(SorchaJson.Options, cancellationToken);
            if (result == null)
            {
                return new TransactionPage { Page = page, PageSize = pageSize };
            }

            return new TransactionPage
            {
                Page = result.Page,
                PageSize = result.PageSize,
                Total = result.TotalCount,
                Transactions = result.Items ?? []
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error getting transactions by prevTxId {PrevTxId} from register {RegisterId}",
                prevTxId, registerId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get transactions by prevTxId {PrevTxId} from register {RegisterId}",
                prevTxId, registerId);
            throw;
        }
    }

    public async Task<List<TransactionModel>> GetTransactionsByInstanceIdAsync(
        string registerId,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Getting transactions for instance {InstanceId} from register {RegisterId}",
                instanceId, registerId);

            // Query endpoints are gated by CanReadTransactions. Without the service
            // bearer the call falls back to 401 and Tier 3 validator lookup sees an
            // empty chain, causing VAL_BP_002 on every late-bound participant reuse.
            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/query/instance/{Uri.EscapeDataString(instanceId)}/transactions/{Uri.EscapeDataString(registerId)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to get transactions for instance {InstanceId} from register {RegisterId}: {StatusCode}",
                    instanceId, registerId, response.StatusCode);
                return [];
            }

            var transactions = await response.Content.ReadFromJsonAsync<List<TransactionModel>>(SorchaJson.Options, cancellationToken);
            return transactions ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error getting transactions for instance {InstanceId} from register {RegisterId}",
                instanceId, registerId);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get transactions for instance {InstanceId} from register {RegisterId}",
                instanceId, registerId);
            throw;
        }
    }

    // =========================================================================
    // Governance Operations
    // =========================================================================

    public async Task<TransactionPage> GetControlTransactionsAsync(
        string registerId,
        int page = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Getting Control transactions from register {RegisterId} (page {Page})",
                registerId, page);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/transactions?type=Control&page={page}&pageSize={pageSize}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug(
                        "No Control transactions found for register {RegisterId}",
                        registerId);
                    return new TransactionPage { Page = page, PageSize = pageSize };
                }

                _logger.LogWarning(
                    "Failed to get Control transactions from register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return new TransactionPage { Page = page, PageSize = pageSize };
            }

            var result = await response.Content.ReadFromJsonAsync<TransactionPage>(SorchaJson.Options, cancellationToken);
            return result ?? new TransactionPage { Page = page, PageSize = pageSize };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error getting Control transactions from register {RegisterId}",
                registerId);
            return new TransactionPage { Page = page, PageSize = pageSize };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get Control transactions from register {RegisterId}",
                registerId);
            throw;
        }
    }

    public async Task<Sorcha.ServiceClients.Register.Models.GovernanceProposalResponse?> ProposeGovernanceOperationAsync(
        string registerId,
        Sorcha.ServiceClients.Register.Models.GovernanceProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Submitting governance proposal ({OperationType}) to register {RegisterId}",
                request.OperationType, registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.PostAsJsonAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/governance/propose",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Failed to submit governance proposal to register {RegisterId}: {StatusCode} - {Error}",
                    registerId, response.StatusCode, error);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<Sorcha.ServiceClients.Register.Models.GovernanceProposalResponse>(
                SorchaJson.Options, cancellationToken);

            _logger.LogInformation(
                "Successfully submitted governance proposal ({OperationType}) to register {RegisterId}",
                request.OperationType, registerId);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error submitting governance proposal to register {RegisterId}",
                registerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to submit governance proposal to register {RegisterId}",
                registerId);
            return null;
        }
    }

    public async Task<Sorcha.ServiceClients.Register.Models.GovernanceProposalPage> GetGovernanceProposalsAsync(
        string registerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Getting governance proposals from register {RegisterId} (page {Page})",
                registerId, page);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/governance/proposals?page={page}&pageSize={pageSize}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug(
                        "No governance proposals found for register {RegisterId}",
                        registerId);
                    return new Sorcha.ServiceClients.Register.Models.GovernanceProposalPage { Page = page, PageSize = pageSize };
                }

                _logger.LogWarning(
                    "Failed to get governance proposals from register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return new Sorcha.ServiceClients.Register.Models.GovernanceProposalPage { Page = page, PageSize = pageSize };
            }

            var result = await response.Content.ReadFromJsonAsync<Sorcha.ServiceClients.Register.Models.GovernanceProposalPage>(
                SorchaJson.Options, cancellationToken);
            return result ?? new Sorcha.ServiceClients.Register.Models.GovernanceProposalPage { Page = page, PageSize = pageSize };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error getting governance proposals from register {RegisterId}",
                registerId);
            return new Sorcha.ServiceClients.Register.Models.GovernanceProposalPage { Page = page, PageSize = pageSize };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get governance proposals from register {RegisterId}",
                registerId);
            throw;
        }
    }

    // =========================================================================
    // Blueprint Publishing
    // =========================================================================

    public async Task<GovernanceRosterResponse?> GetGovernanceRosterAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting governance roster for register {RegisterId}", registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/governance/roster",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("No governance roster found for register {RegisterId}", registerId);
                    return null;
                }

                _logger.LogWarning(
                    "Failed to get governance roster for register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<GovernanceRosterResponse>(SorchaJson.Options, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error getting governance roster for register {RegisterId}", registerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get governance roster for register {RegisterId}", registerId);
            return null;
        }
    }

    public async Task<bool> PublishBlueprintToRegisterAsync(
        string registerId,
        string blueprintId,
        string blueprintJson,
        string publishedBy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Publishing blueprint {BlueprintId} to register {RegisterId}",
                blueprintId, registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var request = new PublishBlueprintRequest
            {
                BlueprintId = blueprintId,
                BlueprintJson = blueprintJson,
                PublishedBy = publishedBy
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/blueprints/publish",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Failed to publish blueprint {BlueprintId} to register {RegisterId}: {StatusCode} - {Error}",
                    blueprintId, registerId, response.StatusCode, error);
                return false;
            }

            _logger.LogInformation(
                "Successfully published blueprint {BlueprintId} to register {RegisterId}",
                blueprintId, registerId);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error publishing blueprint {BlueprintId} to register {RegisterId}",
                blueprintId, registerId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish blueprint {BlueprintId} to register {RegisterId}",
                blueprintId, registerId);
            return false;
        }
    }

    // =========================================================================
    // System Register Operations
    // =========================================================================

    /// <inheritdoc />
    public async Task<bool> SystemRegisterBlueprintExistsAsync(
        string blueprintId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Checking system register for blueprint {BlueprintId}", blueprintId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/system-register/blueprints/{Uri.EscapeDataString(blueprintId)}",
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to check system register for blueprint {BlueprintId} — assuming not found",
                blueprintId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetSystemRegisterBlueprintJsonAsync(
        string blueprintId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Fetching blueprint {BlueprintId} from the system register", blueprintId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/system-register/blueprints/{Uri.EscapeDataString(blueprintId)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 404 is the ordinary "not a system blueprint" answer, not a fault.
                _logger.LogDebug(
                    "System register has no blueprint {BlueprintId} ({StatusCode})",
                    blueprintId, response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body)) return null;

            using var doc = JsonDocument.Parse(body);

            // SystemRegisterEntry.Document carries the definition. The Register Service configures
            // no JSON options, so its minimal APIs serialise with JsonSerializerOptions.Web —
            // camelCase — but read case-insensitively here rather than depending on that.
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, "document", StringComparison.OrdinalIgnoreCase))
                    continue;

                return property.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : property.Value.GetRawText();
            }

            _logger.LogWarning(
                "System register returned blueprint {BlueprintId} with no document body", blueprintId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch blueprint {BlueprintId} from the system register", blueprintId);
            return null;
        }
    }

    // =========================================================================
    // Register Management
    // =========================================================================

    public async Task<Sorcha.Register.Models.Register?> GetRegisterAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting register info for {RegisterId}", registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Register {RegisterId} not found", registerId);
                    return null;
                }

                _logger.LogWarning(
                    "Failed to get register info for {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<Sorcha.Register.Models.Register>(SorchaJson.Options, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error getting register info for {RegisterId}", registerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get register info for {RegisterId}", registerId);
            return null;
        }
    }

    public async Task<Sorcha.Register.Models.Register> CreateRegisterAsync(
        string registerId,
        string name,
        string blueprintId,
        string owner,
        string tenant,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Creating register {RegisterId} with name {Name}", registerId, name);

            var request = new CreateRegisterRequest
            {
                Name = name,
                Advertise = true,
                IsFullReplica = true
            };

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.PostAsJsonAsync(
                "api/registers",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Failed to create register {RegisterId}: {StatusCode} - {Error}",
                    registerId, response.StatusCode, error);
                throw new HttpRequestException($"Failed to create register: {response.StatusCode}");
            }

            var register = await response.Content.ReadFromJsonAsync<Sorcha.Register.Models.Register>(SorchaJson.Options, cancellationToken);
            _logger.LogInformation("Successfully created register {RegisterId}", registerId);
            return register ?? throw new InvalidOperationException("Failed to deserialize register response");
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create register {RegisterId}", registerId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<InitiateRegisterCreationResponse> InitiateRegisterCreationAsync(
        InitiateRegisterCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            _logger.LogDebug(
                "Initiating register creation for '{Name}' with {OwnerCount} owner(s) (devMode={DevMode})",
                request.Name, request.Owners.Count, request.DevMode);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.PostAsJsonAsync(
                "api/registers/initiate",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Failed to initiate register creation for '{Name}': {StatusCode} - {Error}",
                    request.Name, response.StatusCode, error);
                throw new HttpRequestException(
                    $"Failed to initiate register creation: {response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync<InitiateRegisterCreationResponse>(
                SorchaJson.Options, cancellationToken);
            return result ?? throw new InvalidOperationException(
                "Failed to deserialize register initiation response");
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate register creation for '{Name}'", request.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<FinalizeRegisterCreationResponse> FinalizeRegisterCreationAsync(
        FinalizeRegisterCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            _logger.LogDebug(
                "Finalizing register creation for {RegisterId} with {AttestationCount} signed attestation(s)",
                request.RegisterId, request.SignedAttestations.Count);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.PostAsJsonAsync(
                "api/registers/finalize",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Failed to finalize register creation for {RegisterId}: {StatusCode} - {Error}",
                    request.RegisterId, response.StatusCode, error);
                throw new HttpRequestException(
                    $"Failed to finalize register creation: {response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync<FinalizeRegisterCreationResponse>(
                SorchaJson.Options, cancellationToken);
            return result ?? throw new InvalidOperationException(
                "Failed to deserialize register finalization response");
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finalize register creation for {RegisterId}", request.RegisterId);
            throw;
        }
    }

    // =========================================================================
    // Participant Query Operations
    // =========================================================================

    public async Task<Sorcha.ServiceClients.Register.Models.ParticipantPage> GetPublishedParticipantsAsync(
        string registerId,
        int skip = 0,
        int top = 20,
        string? statusFilter = "active",
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting participants from register {RegisterId} (skip={Skip}, top={Top}, status={Status})",
                registerId, skip, top, statusFilter);

            await SetAuthHeaderAsync(cancellationToken);

            var url = $"api/registers/{Uri.EscapeDataString(registerId)}/participants?skip={skip}&top={top}";
            if (!string.IsNullOrEmpty(statusFilter))
                url += $"&status={Uri.EscapeDataString(statusFilter)}";

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get participants from register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return new Sorcha.ServiceClients.Register.Models.ParticipantPage { PageSize = top };
            }

            var result = await response.Content.ReadFromJsonAsync<Sorcha.ServiceClients.Register.Models.ParticipantPage>(
                SorchaJson.Options, cancellationToken);
            return result ?? new Sorcha.ServiceClients.Register.Models.ParticipantPage { PageSize = top };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error getting participants from register {RegisterId}", registerId);
            return new Sorcha.ServiceClients.Register.Models.ParticipantPage { PageSize = top };
        }
    }

    public async Task<Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord?> GetPublishedParticipantByAddressAsync(
        string registerId,
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Looking up participant by address {WalletAddress} on register {RegisterId}",
                walletAddress, registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/participants/by-address/{Uri.EscapeDataString(walletAddress)}",
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord>(
                SorchaJson.Options, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error looking up participant by address on register {RegisterId}", registerId);
            return null;
        }
    }

    public async Task<Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord?> GetPublishedParticipantByIdAsync(
        string registerId,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting participant {ParticipantId} from register {RegisterId}",
                participantId, registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/participants/{Uri.EscapeDataString(participantId)}",
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord>(
                SorchaJson.Options, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error getting participant {ParticipantId} from register {RegisterId}",
                participantId, registerId);
            return null;
        }
    }

    // =========================================================================
    // Participant Resolution
    // =========================================================================

    /// <inheritdoc/>
    public async Task<Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord?> ResolveParticipantAsync(
        string registerId,
        string participantId,
        string? orgName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Resolving participant {ParticipantId} (org: {OrgName}) on register {RegisterId}",
                participantId, orgName ?? "(any)", registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var url = $"api/registers/{Uri.EscapeDataString(registerId)}/participants/resolve?participantId={Uri.EscapeDataString(participantId)}";
            if (!string.IsNullOrEmpty(orgName))
                url += $"&orgName={Uri.EscapeDataString(orgName)}";

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            if (response.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                _logger.LogWarning("Participant {ParticipantId} has been revoked on register {RegisterId}",
                    participantId, registerId);
                return null;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord>(
                SorchaJson.Options, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error resolving participant {ParticipantId} on register {RegisterId}",
                participantId, registerId);
            return null;
        }
    }

    // =========================================================================
    // Public Key Resolution
    // =========================================================================

    public async Task<Sorcha.ServiceClients.Register.Models.PublicKeyResolution?> ResolvePublicKeyAsync(
        string registerId,
        string walletAddress,
        string? algorithm = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Resolving public key for address {WalletAddress} on register {RegisterId}",
                walletAddress, registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var url = $"api/registers/{Uri.EscapeDataString(registerId)}/participants/by-address/{Uri.EscapeDataString(walletAddress)}/public-key";
            if (!string.IsNullOrEmpty(algorithm))
                url += $"?algorithm={Uri.EscapeDataString(algorithm)}";

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            if (response.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                throw new InvalidOperationException(
                    $"Participant for wallet address '{walletAddress}' has been revoked");
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<Sorcha.ServiceClients.Register.Models.PublicKeyResolution>(
                SorchaJson.Options, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error resolving public key for address {WalletAddress}", walletAddress);
            return null;
        }
    }

    // =========================================================================
    // Batch Public Key Resolution
    // =========================================================================

    /// <inheritdoc />
    public async Task<Sorcha.ServiceClients.Register.Models.BatchPublicKeyResponse> ResolvePublicKeysBatchAsync(
        string registerId,
        Sorcha.ServiceClients.Register.Models.BatchPublicKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Batch resolving {Count} public keys from register {RegisterId}",
                request.WalletAddresses.Length, registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.PostAsJsonAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/participants/resolve-public-keys",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Batch public key resolution failed for register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return new Sorcha.ServiceClients.Register.Models.BatchPublicKeyResponse();
            }

            var result = await response.Content.ReadFromJsonAsync<Sorcha.ServiceClients.Register.Models.BatchPublicKeyResponse>(
                SorchaJson.Options, cancellationToken);
            return result ?? new Sorcha.ServiceClients.Register.Models.BatchPublicKeyResponse();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error during batch public key resolution for register {RegisterId}", registerId);
            return new Sorcha.ServiceClients.Register.Models.BatchPublicKeyResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch resolve public keys for register {RegisterId}", registerId);
            throw;
        }
    }

    // =========================================================================
    // Policy Operations
    // =========================================================================

    /// <inheritdoc />
    public async Task<RegisterPolicyResponse?> GetRegisterPolicyAsync(
        string registerId,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Getting policy for register {RegisterId}", registerId);

            await SetAuthHeaderAsync(ct);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/policy",
                ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("No policy found for register {RegisterId}", registerId);
                    return null;
                }

                _logger.LogWarning(
                    "Failed to get policy for register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<RegisterPolicyResponse>(SorchaJson.Options, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error getting policy for register {RegisterId}", registerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get policy for register {RegisterId}", registerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<PolicyHistoryResponse?> GetPolicyHistoryAsync(
        string registerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug(
                "Getting policy history for register {RegisterId} (page {Page})",
                registerId, page);

            await SetAuthHeaderAsync(ct);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/policy/history?page={page}&pageSize={pageSize}",
                ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("No policy history found for register {RegisterId}", registerId);
                    return null;
                }

                _logger.LogWarning(
                    "Failed to get policy history for register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PolicyHistoryResponse>(SorchaJson.Options, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error getting policy history for register {RegisterId}", registerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get policy history for register {RegisterId}", registerId);
            return null;
        }
    }

    // =========================================================================
    // Internal DTOs
    // =========================================================================

    private record WriteDocketRequest
    {
        /// <summary>Identifier of the docket.</summary>
        public required string DocketId { get; init; }
        /// <summary>Numeric value for docket number.</summary>
        public required long DocketNumber { get; init; }
        /// <summary>The previous hash.</summary>
        public string? PreviousHash { get; init; }
        /// <summary>The docket hash.</summary>
        public required string DocketHash { get; init; }
        /// <summary>Server timestamp when the record was created (UTC).</summary>
        public required DateTimeOffset CreatedAt { get; init; }
        /// <summary>Collection of transaction ids associated with this resource.</summary>
        public required List<string> TransactionIds { get; init; }
        /// <summary>Identifier of the proposer validator.</summary>
        public required string ProposerValidatorId { get; init; }
        /// <summary>The merkle root.</summary>
        public required string MerkleRoot { get; init; }
        /// <summary>Collection of transactions associated with this resource.</summary>
        public List<Sorcha.Register.Models.TransactionModel>? Transactions { get; init; }
        /// <summary>
        /// Validator votes that carried the docket to consensus (Feature 187 / #1371). Empty in
        /// single-validator mode, which is valid. Must mirror the server's WriteDocketRequest — a
        /// field present here but absent there (or vice versa) is dropped silently on the wire.
        /// </summary>
        public List<Sorcha.Register.Models.ConsensusVote>? Votes { get; init; }
    }

    private record CreateRegisterRequest
    {
        /// <summary>Human-readable name.</summary>
        public required string Name { get; init; }
        /// <summary>Flag indicating advertise.</summary>
        public bool Advertise { get; init; } = false;
        /// <summary>Indicates whether full replica.</summary>
        public bool IsFullReplica { get; init; } = true;
    }

    private record PublishBlueprintRequest
    {
        /// <summary>Identifier of the blueprint.</summary>
        public required string BlueprintId { get; init; }
        /// <summary>The blueprint json.</summary>
        public required string BlueprintJson { get; init; }
        /// <summary>The published by.</summary>
        public required string PublishedBy { get; init; }
    }

    // =========================================================================
    // Recovery / Internal Discovery
    // =========================================================================

    public async Task<List<InternalRegisterInfo>> GetInternalRegistersAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Fetching internal register list for recovery");

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                "api/internal/registers",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to fetch internal registers: {StatusCode}",
                    response.StatusCode);
                return [];
            }

            var registers = await response.Content.ReadFromJsonAsync<List<InternalRegisterInfo>>(
                SorchaJson.Options, cancellationToken);

            return registers ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching internal registers");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch internal registers");
            return [];
        }
    }

    public async Task<SubscriptionNotificationResponse?> NotifySubscriptionAsync(
        SubscriptionNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Notifying Register Service of subscription {Action} for register {RegisterId}",
                request.Action, request.RegisterId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.PostAsJsonAsync(
                "api/internal/register-subscriptions",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to notify subscription {Action} for register {RegisterId}: {StatusCode}",
                    request.Action, request.RegisterId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<SubscriptionNotificationResponse>(
                SorchaJson.Options, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Register Service unavailable — cannot notify subscription {Action} for register {RegisterId}",
                request.Action, request.RegisterId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to notify subscription {Action} for register {RegisterId}",
                request.Action, request.RegisterId);
            return null;
        }
    }

    public async Task<PublishedBlueprintsResponse?> GetPublishedBlueprintsAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Fetching published blueprints for register {RegisterId}", registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/blueprints/published",
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Register {RegisterId} not found", registerId);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to fetch published blueprints for register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PublishedBlueprintsResponse>(
                SorchaJson.Options, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching published blueprints for register {RegisterId}", registerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch published blueprints for register {RegisterId}", registerId);
            return null;
        }
    }

    // =========================================================================
    // Private Records
    // =========================================================================

    private record PrevTxIdQueryResponse
    {
        /// <summary>Collection of items in the result set.</summary>
        public List<TransactionModel> Items { get; init; } = [];
        /// <summary>One-based page number for paginated results.</summary>
        public int Page { get; init; }
        /// <summary>Number of items per page.</summary>
        public int PageSize { get; init; }
        /// <summary>Total number of items available.</summary>
        public int TotalCount { get; init; }
        /// <summary>Numeric value for total pages.</summary>
        public int TotalPages { get; init; }
    }

    private record DocketResponse
    {
        /// <summary>Unique identifier for the resource.</summary>
        public ulong Id { get; init; }
        /// <summary>Identifier of the register.</summary>
        public string RegisterId { get; init; } = string.Empty;
        /// <summary>The previous hash.</summary>
        public string PreviousHash { get; init; } = string.Empty;
        /// <summary>Cryptographic hash of the payload.</summary>
        public string Hash { get; init; } = string.Empty;
        /// <summary>Collection of transaction ids associated with this resource.</summary>
        public List<string> TransactionIds { get; init; } = [];
        /// <summary>Timestamp associated with this record (UTC).</summary>
        public DateTimeOffset TimeStamp { get; init; }
        /// <summary>Identifier of the validator that proposed the docket.</summary>
        public string ProposerValidatorId { get; init; } = string.Empty;
        /// <summary>Merkle root over the docket's transaction set, as sealed.</summary>
        public string MerkleRoot { get; init; } = string.Empty;
        /// <summary>Validator votes that carried the docket to consensus. Empty is valid.</summary>
        public List<Sorcha.Register.Models.ConsensusVote> Votes { get; init; } = [];
    }

    // =========================================================================
    // Feature 108 — Local relationship + sync state + observation intake
    // =========================================================================

    /// <inheritdoc />
    public async Task ReportPeerHeightAsync(
        PeerHeightObservation observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        try
        {
            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.PostAsJsonAsync(
                $"api/internal/registers/{Uri.EscapeDataString(observation.RegisterId)}/peer-height-observation",
                observation,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to report peer height observation for register {RegisterId}: {StatusCode}",
                    observation.RegisterId, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error reporting peer height observation for register {RegisterId}",
                observation.RegisterId);
        }
    }

    /// <inheritdoc />
    public async Task ReportValidatorSealingAsync(
        ValidatorSealingObservation observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        try
        {
            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.PostAsJsonAsync(
                $"api/internal/registers/{Uri.EscapeDataString(observation.RegisterId)}/validator-observation",
                observation,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to report validator sealing observation for register {RegisterId}: {StatusCode}",
                    observation.RegisterId, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error reporting validator sealing observation for register {RegisterId}",
                observation.RegisterId);
        }
    }

    /// <inheritdoc />
    public async Task<RegisterLocalRelationship?> GetLocalRelationshipAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/local-relationship",
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GetLocalRelationshipAsync failed for register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<RegisterLocalRelationship>(SorchaJson.Options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get local relationship for register {RegisterId}", registerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<RegisterSyncStateView?> GetSyncStateAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/sync-state",
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GetSyncStateAsync failed for register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<RegisterSyncStateView>(SorchaJson.Options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get sync state for register {RegisterId}", registerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>?> GetMyValidatedRegistersAsync(
        byte[] validatorPublicKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validatorPublicKey);
        if (validatorPublicKey.Length == 0)
            throw new ArgumentException("Validator public key cannot be empty.", nameof(validatorPublicKey));

        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, "api/internal/my-validated-registers");
            request.Headers.Add("X-Validator-Public-Key", Convert.ToBase64String(validatorPublicKey));

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Return null (NOT empty) — empty means "validator is on no rosters" and would
                // cause RegisterMonitoringBootstrap to prune every monitored register. A
                // transient HTTP failure must NOT trigger a prune. See issue #787 for the
                // original wedge incident.
                _logger.LogWarning(
                    "GetMyValidatedRegistersAsync failed: {StatusCode}", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<MyValidatedRegistersResponse>(SorchaJson.Options, cancellationToken);
            // A successful response with a null/missing RegisterIds field is treated as an empty
            // set (validator legitimately on no rosters), not a failure.
            return (IReadOnlyList<string>)(payload?.RegisterIds ?? Array.Empty<string>());
        }
        catch (Exception ex)
        {
            // Return null on any exception — same rationale as the HTTP-failure branch above.
            _logger.LogError(ex, "Failed to enumerate validated registers for local validator key");
            return null;
        }
    }

    private sealed record MyValidatedRegistersResponse(IReadOnlyList<string> RegisterIds);

    /// <inheritdoc />
    public async Task<RegisterStatsResponse> GetStatsAsync(
        IReadOnlyList<string>? registerIds = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Feature 131 — anonymous endpoint (no SetAuthHeaderAsync). When
            // registerIds is non-empty, append as a comma-separated query string;
            // entries are URL-encoded individually to defend against odd id chars.
            var url = "api/stats";
            if (registerIds is { Count: > 0 })
            {
                var joined = string.Join(",", registerIds.Select(Uri.EscapeDataString));
                url = $"{url}?registerIds={joined}";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GetStatsAsync failed: {StatusCode}", response.StatusCode);
                return new RegisterStatsResponse();
            }

            var payload = await response.Content.ReadFromJsonAsync<RegisterStatsResponse>(
                SorchaJson.Options, cancellationToken);
            return payload ?? new RegisterStatsResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch register statistics");
            return new RegisterStatsResponse();
        }
    }

    /// <inheritdoc />
    public async Task<RegisterTransactionStatistics?> GetRegisterTransactionStatsAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/query/stats?registerId={Uri.EscapeDataString(registerId)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GetRegisterTransactionStatsAsync failed for {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<RegisterTransactionStatistics>(
                SorchaJson.Options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch transaction statistics for register {RegisterId}", registerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegisterSummaryInfo>> GetRecentRegistersAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync("api/registers/", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GetRecentRegistersAsync failed: {StatusCode}", response.StatusCode);
                return [];
            }

            var registers = await response.Content.ReadFromJsonAsync<List<RegisterSummaryInfo>>(
                SorchaJson.Options, cancellationToken);

            if (registers == null)
            {
                return [];
            }

            return registers
                .OrderByDescending(r => r.CreatedAt)
                .Take(limit < 1 ? 10 : limit)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch recent registers");
            return [];
        }
    }

    // =========================================================================
    // Feature 079 — Transaction verification + lifecycle (Feature 140 MCP surface)
    // =========================================================================

    /// <inheritdoc />
    public async Task<TransactionStatusResponse?> GetTransactionStatusAsync(
        string registerId,
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/transactions/{Uri.EscapeDataString(transactionId)}/status",
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GetTransactionStatusAsync failed for {TransactionId} on register {RegisterId}: {StatusCode}",
                    transactionId, registerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<TransactionStatusResponse>(SorchaJson.Options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get transaction status for {TransactionId} on register {RegisterId}",
                transactionId, registerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<MerkleInclusionProof?> GetInclusionProofAsync(
        string registerId,
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/transactions/{Uri.EscapeDataString(transactionId)}/inclusion-proof",
                cancellationToken);

            // 404 (no such tx) and 409 (not sealed yet) both map to "no proof available".
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Conflict)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GetInclusionProofAsync failed for {TransactionId} on register {RegisterId}: {StatusCode}",
                    transactionId, registerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<MerkleInclusionProof>(SorchaJson.Options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get inclusion proof for {TransactionId} on register {RegisterId}",
                transactionId, registerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<VerificationBundle?> GetVerificationBundleAsync(
        string registerId,
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/transactions/{Uri.EscapeDataString(transactionId)}/verification-bundle",
                cancellationToken);

            // 404 (no such tx) and 409 (not sealed yet) both map to "no bundle available".
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Conflict)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GetVerificationBundleAsync failed for {TransactionId} on register {RegisterId}: {StatusCode}",
                    transactionId, registerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<VerificationBundle>(SorchaJson.Options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get verification bundle for {TransactionId} on register {RegisterId}",
                transactionId, registerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<RevokeTransactionResult?> RevokeTransactionAsync(
        string registerId,
        RevokeTransactionClientRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.PostAsJsonAsync(
                $"api/registers/{Uri.EscapeDataString(registerId)}/transactions/revoke",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "RevokeTransactionAsync failed for {TransactionId} on register {RegisterId}: {StatusCode} - {Error}",
                    request.OriginalTxId, registerId, response.StatusCode, error);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<RevokeTransactionResult>(SorchaJson.Options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to revoke transaction {TransactionId} on register {RegisterId}",
                request.OriginalTxId, registerId);
            return null;
        }
    }
}
