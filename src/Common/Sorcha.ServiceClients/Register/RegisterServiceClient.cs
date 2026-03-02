// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

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

        _serviceAddress = configuration["ServiceClients:RegisterService:Address"]
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
                Transactions = docket.Transactions
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

            var docket = await response.Content.ReadFromJsonAsync<DocketResponse>(JsonOptions, cancellationToken);
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

            var docket = await response.Content.ReadFromJsonAsync<DocketResponse>(JsonOptions, cancellationToken);
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
        return new DocketModel
        {
            DocketId = docket.Id.ToString(),
            RegisterId = registerId,
            DocketNumber = (long)docket.Id,
            PreviousHash = docket.PreviousHash,
            DocketHash = docket.Hash,
            CreatedAt = docket.TimeStamp,
            Transactions = [], // Transactions need to be fetched separately if needed
            ProposerValidatorId = docket.Votes ?? string.Empty,
            MerkleRoot = string.Empty // Not stored in Register.Models.Docket
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

            var result = await response.Content.ReadFromJsonAsync<TransactionModel>(JsonOptions, cancellationToken);
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

            return await response.Content.ReadFromJsonAsync<TransactionModel>(JsonOptions, cancellationToken);
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

            var result = await response.Content.ReadFromJsonAsync<TransactionPage>(JsonOptions, cancellationToken);
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

            var transactions = await response.Content.ReadFromJsonAsync<List<TransactionModel>>(JsonOptions, cancellationToken);
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

            var result = await response.Content.ReadFromJsonAsync<PrevTxIdQueryResponse>(JsonOptions, cancellationToken);
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

            var transactions = await response.Content.ReadFromJsonAsync<List<TransactionModel>>(JsonOptions, cancellationToken);
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

            var result = await response.Content.ReadFromJsonAsync<TransactionPage>(JsonOptions, cancellationToken);
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
                JsonOptions, cancellationToken);

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
                JsonOptions, cancellationToken);
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

            return await response.Content.ReadFromJsonAsync<GovernanceRosterResponse>(JsonOptions, cancellationToken);
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

            return await response.Content.ReadFromJsonAsync<Sorcha.Register.Models.Register>(JsonOptions, cancellationToken);
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
                TenantId = tenant,
                Advertise = true,
                IsFullReplica = true
            };

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

            var register = await response.Content.ReadFromJsonAsync<Sorcha.Register.Models.Register>(JsonOptions, cancellationToken);
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
                JsonOptions, cancellationToken);
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
                JsonOptions, cancellationToken);
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
                JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error getting participant {ParticipantId} from register {RegisterId}",
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
                JsonOptions, cancellationToken);
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
                JsonOptions, cancellationToken);
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
    // Internal DTOs
    // =========================================================================

    private record WriteDocketRequest
    {
        public required string DocketId { get; init; }
        public required long DocketNumber { get; init; }
        public string? PreviousHash { get; init; }
        public required string DocketHash { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required List<string> TransactionIds { get; init; }
        public required string ProposerValidatorId { get; init; }
        public required string MerkleRoot { get; init; }
        public List<Sorcha.Register.Models.TransactionModel>? Transactions { get; init; }
    }

    private record CreateRegisterRequest
    {
        public required string Name { get; init; }
        public required string TenantId { get; init; }
        public bool Advertise { get; init; } = false;
        public bool IsFullReplica { get; init; } = true;
    }

    private record PublishBlueprintRequest
    {
        public required string BlueprintId { get; init; }
        public required string BlueprintJson { get; init; }
        public required string PublishedBy { get; init; }
    }

    private record PrevTxIdQueryResponse
    {
        public List<TransactionModel> Items { get; init; } = [];
        public int Page { get; init; }
        public int PageSize { get; init; }
        public int TotalCount { get; init; }
        public int TotalPages { get; init; }
    }

    private record DocketResponse
    {
        public ulong Id { get; init; }
        public string RegisterId { get; init; } = string.Empty;
        public string PreviousHash { get; init; } = string.Empty;
        public string Hash { get; init; } = string.Empty;
        public List<string> TransactionIds { get; init; } = [];
        public DateTimeOffset TimeStamp { get; init; }
        public string? Votes { get; init; }
    }
}
