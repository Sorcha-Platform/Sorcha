// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.Wallet;

/// <summary>
/// HTTP client for Wallet Service operations
/// </summary>
public class WalletServiceClient : IWalletServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<WalletServiceClient> _logger;
    private readonly string _serviceAddress;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public WalletServiceClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        IConfiguration configuration,
        ILogger<WalletServiceClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _serviceAddress = configuration["ServiceClients:WalletService:Address"]
            ?? configuration["GrpcClients:WalletService:Address"]
            ?? "http://wallet-service";

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_serviceAddress);
        }

        _logger.LogInformation(
            "WalletServiceClient initialized (Address: {Address})", _serviceAddress);
    }

    // =========================================================================
    // System Wallet Operations
    // =========================================================================

    public async Task<string> CreateOrRetrieveSystemWalletAsync(
        string validatorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Creating or retrieving system wallet for validator {ValidatorId}", validatorId);

            await SetAuthHeaderAsync(cancellationToken);

            var request = new { validatorId };
            var response = await _httpClient.PostAsJsonAsync(
                "/api/v1/wallets/system", request, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SystemWalletResponse>(cancellationToken);
            return result?.Address ?? throw new InvalidOperationException("System wallet response missing address");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create/retrieve system wallet for validator {ValidatorId}", validatorId);
            throw;
        }
    }

    // =========================================================================
    // Signing Operations
    // =========================================================================

    public async Task<WalletSignResult> SignDataAsync(
        string walletId,
        string dataToSign,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Signing data with wallet {WalletId}", walletId);

            // Convert hex data to bytes and sign as pre-hashed
            var dataBytes = Convert.FromHexString(dataToSign);
            return await SignTransactionAsync(walletId, dataBytes, isPreHashed: true, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sign data with wallet {WalletId}", walletId);
            throw;
        }
    }

    public async Task<WalletSignResult> SignTransactionAsync(
        string walletAddress,
        byte[] transactionData,
        string? derivationPath = null,
        bool isPreHashed = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Signing transaction with wallet {WalletAddress} (preHashed: {IsPreHashed}, path: {DerivationPath})",
                walletAddress, isPreHashed, derivationPath ?? "default");

            await SetAuthHeaderAsync(cancellationToken);

            var requestBody = new SignRequest
            {
                TransactionData = Convert.ToBase64String(transactionData),
                DerivationPath = derivationPath,
                IsPreHashed = isPreHashed
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/api/v1/wallets/{walletAddress}/sign", requestBody, JsonOptions, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"Wallet {walletAddress} not found");
            }

            response.EnsureSuccessStatusCode();

            var signResponse = await response.Content.ReadFromJsonAsync<SignResponse>(cancellationToken);
            if (signResponse is null)
            {
                throw new InvalidOperationException("Sign response was null");
            }

            return new WalletSignResult
            {
                Signature = Convert.FromBase64String(signResponse.Signature),
                PublicKey = Convert.FromBase64String(signResponse.PublicKey),
                SignedBy = signResponse.SignedBy,
                Algorithm = signResponse.Algorithm ?? "ED25519"
            };
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogError(ex, "Authentication failed when signing with wallet {WalletAddress}", walletAddress);
            throw new InvalidOperationException($"Authentication failed for wallet signing: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to sign transaction with wallet {WalletAddress}", walletAddress);
            throw;
        }
    }

    public async Task<bool> VerifySignatureAsync(
        string publicKey,
        string data,
        string signature,
        string algorithm,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Verifying signature with public key");

            await SetAuthHeaderAsync(cancellationToken);

            var requestBody = new { publicKey, data, signature, algorithm };
            var response = await _httpClient.PostAsJsonAsync(
                "/api/v1/wallets/verify", requestBody, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<VerifyResponse>(cancellationToken);
            return result?.IsValid ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify signature");
            return false;
        }
    }

    // =========================================================================
    // Encryption Operations
    // =========================================================================

    public async Task<byte[]> EncryptPayloadAsync(
        string recipientWalletAddress,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Encrypting payload for wallet {WalletAddress}", recipientWalletAddress);

            await SetAuthHeaderAsync(cancellationToken);

            var requestBody = new { payload = Convert.ToBase64String(payload) };
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/v1/wallets/{recipientWalletAddress}/encrypt", requestBody, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<EncryptResponse>(cancellationToken);
            return Convert.FromBase64String(result?.EncryptedPayload
                ?? throw new InvalidOperationException("Encrypt response missing payload"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt payload for wallet {WalletAddress}", recipientWalletAddress);
            throw;
        }
    }

    public async Task<byte[]> DecryptPayloadAsync(
        string walletAddress,
        byte[] encryptedPayload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Decrypting payload with wallet {WalletAddress}", walletAddress);

            await SetAuthHeaderAsync(cancellationToken);

            var requestBody = new { encryptedPayload = Convert.ToBase64String(encryptedPayload) };
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/v1/wallets/{walletAddress}/decrypt", requestBody, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<DecryptResponse>(cancellationToken);
            return Convert.FromBase64String(result?.DecryptedPayload
                ?? throw new InvalidOperationException("Decrypt response missing payload"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt payload with wallet {WalletAddress}", walletAddress);
            throw;
        }
    }

    public async Task<byte[]> DecryptWithDelegationAsync(
        string walletAddress,
        byte[] encryptedPayload,
        string delegationToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Decrypting payload with delegation for wallet {WalletAddress}", walletAddress);

            await SetAuthHeaderAsync(cancellationToken);

            var requestBody = new
            {
                encryptedPayload = Convert.ToBase64String(encryptedPayload),
                delegationToken
            };
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/v1/wallets/{walletAddress}/decrypt", requestBody, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<DecryptResponse>(cancellationToken);
            return Convert.FromBase64String(result?.DecryptedPayload
                ?? throw new InvalidOperationException("Decrypt response missing payload"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt payload with delegation for wallet {WalletAddress}", walletAddress);
            throw;
        }
    }

    // =========================================================================
    // Credential Operations
    // =========================================================================

    public async Task<CredentialIssuanceResult> IssueCredentialAsync(
        string issuerWalletAddress,
        string credentialType,
        Dictionary<string, object> claims,
        string recipientWallet,
        string? expiryDuration = null,
        List<string>? disclosableClaims = null,
        string? issuanceBlueprintId = null,
        string? statusListUrl = null,
        int? statusListIndex = null,
        string? statusListPurpose = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Issuing credential of type {CredentialType} from {IssuerWallet} to {RecipientWallet}",
                credentialType, issuerWalletAddress, recipientWallet);

            await SetAuthHeaderAsync(cancellationToken);

            var requestBody = new
            {
                credentialType,
                claims,
                recipientWallet,
                expiryDuration,
                disclosableClaims,
                issuanceBlueprintId,
                statusListUrl,
                statusListIndex,
                statusListPurpose
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/api/v1/wallets/{issuerWalletAddress}/credentials/issue",
                requestBody, JsonOptions, cancellationToken);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<CredentialIssuanceResult>(cancellationToken)
                ?? throw new InvalidOperationException("Credential issuance response was null");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex,
                "Failed to issue credential of type {CredentialType} from {IssuerWallet}",
                credentialType, issuerWalletAddress);
            throw;
        }
    }

    public async Task StoreCredentialAsync(
        string walletAddress,
        string credentialId,
        string type,
        string issuerDid,
        string subjectDid,
        string claimsJson,
        DateTimeOffset issuedAt,
        DateTimeOffset? expiresAt,
        string rawToken,
        string? issuanceTxId = null,
        string? issuanceBlueprintId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Storing credential {CredentialId} in wallet {WalletAddress}",
                credentialId, walletAddress);

            await SetAuthHeaderAsync(cancellationToken);

            var requestBody = new
            {
                credentialId,
                type,
                issuerDid,
                subjectDid,
                claimsJson,
                issuedAt,
                expiresAt,
                rawToken,
                issuanceTxId,
                issuanceBlueprintId
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/api/v1/wallets/{walletAddress}/credentials",
                requestBody, JsonOptions, cancellationToken);

            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store credential {CredentialId} in wallet {WalletAddress}",
                credentialId, walletAddress);
            throw;
        }
    }

    public async Task<CredentialIssuanceResult?> GetCredentialAsync(
        string walletAddress,
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"/api/v1/wallets/{walletAddress}/credentials/{credentialId}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<CredentialIssuanceResult>(
                JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get credential {CredentialId} from wallet {WalletAddress}",
                credentialId, walletAddress);
            return null;
        }
    }

    public async Task<bool> UpdateCredentialStatusAsync(
        string walletAddress,
        string credentialId,
        string status,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Updating credential {CredentialId} status to {Status} in wallet {WalletAddress}",
                credentialId, status, walletAddress);

            await SetAuthHeaderAsync(cancellationToken);

            var requestBody = new { status };
            var response = await _httpClient.PatchAsJsonAsync(
                $"/api/v1/wallets/{walletAddress}/credentials/{credentialId}/status",
                requestBody, JsonOptions, cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update credential {CredentialId} status in wallet {WalletAddress}",
                credentialId, walletAddress);
            return false;
        }
    }

    // =========================================================================
    // Wallet Management
    // =========================================================================

    public async Task<WalletInfo?> GetWalletAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting wallet info for {WalletAddress}", walletAddress);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"/api/v1/wallets/{walletAddress}", cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<WalletInfo>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get wallet info for {WalletAddress}", walletAddress);
            return null;
        }
    }

    public async Task<IReadOnlyList<WalletInfo>> GetWalletsByOwnerAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            return Array.Empty<WalletInfo>();

        try
        {
            _logger.LogDebug("Listing wallets for owner {OwnerId}", ownerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"/api/v1/wallets/by-owner/{Uri.EscapeDataString(ownerId)}", cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return Array.Empty<WalletInfo>();
            }

            response.EnsureSuccessStatusCode();
            var wallets = await response.Content.ReadFromJsonAsync<List<WalletInfo>>(
                JsonOptions, cancellationToken);
            return wallets ?? (IReadOnlyList<WalletInfo>)Array.Empty<WalletInfo>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cancellation is caller-initiated (tab close, request abort)
            // and must propagate cleanly rather than be logged as an error
            // and turned into a silent empty list.
            _logger.LogError(ex, "Failed to list wallets for owner {OwnerId}", ownerId);
            return Array.Empty<WalletInfo>();
        }
    }

    public async Task<WalletInfo> CreateWalletAsync(
        string name,
        string algorithm,
        string owner,
        string tenant,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Creating wallet {Name} with algorithm {Algorithm}", name, algorithm);

            await SetAuthHeaderAsync(cancellationToken);

            var requestBody = new { name, algorithm, owner, tenant };
            var response = await _httpClient.PostAsJsonAsync(
                "/api/v1/wallets", requestBody, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();

            var createResponse = await response.Content.ReadFromJsonAsync<CreateWalletResponse>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Create wallet response was null");
            return createResponse.Wallet
                ?? throw new InvalidOperationException("Create wallet response had no wallet data");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create wallet {Name}", name);
            throw;
        }
    }

    // =========================================================================
    // Org Key Management Operations
    // =========================================================================

    public async Task<OrgMasterKeyProvisionResponse?> ProvisionOrgMasterKeyAsync(
        string orgId,
        string algorithm = "ED25519",
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Provisioning org master key for {OrgId} with algorithm {Algorithm}", orgId, algorithm);

            await SetAuthHeaderAsync(ct);

            var requestBody = new { algorithm };
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/wallets/org/{orgId}/master-key", requestBody, JsonOptions, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogWarning("Organisation {OrgId} already has a master key provisioned", orgId);
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<OrgMasterKeyProvisionResponse>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision org master key for {OrgId}", orgId);
            throw;
        }
    }

    public async Task<DerivedKeyResponse?> DeriveOrgKeyAsync(
        string orgId,
        string userId,
        uint departmentId,
        string keyUsage,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug(
                "Deriving org key for {OrgId}, user {UserId}, dept {DepartmentId}, usage {KeyUsage}",
                orgId, userId, departmentId, keyUsage);

            await SetAuthHeaderAsync(ct);

            var requestBody = new { userId, departmentId, keyUsage };
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/wallets/org/{orgId}/derive-key", requestBody, JsonOptions, ct);

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<DerivedKeyResponse>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to derive org key for {OrgId}, user {UserId}", orgId, userId);
            throw;
        }
    }

    public async Task<DerivedKeyResponse?> RotateOrgKeyAsync(
        string orgId,
        Guid derivedKeyId,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Rotating org key {DerivedKeyId} for {OrgId}", derivedKeyId, orgId);

            await SetAuthHeaderAsync(ct);

            var response = await _httpClient.PostAsync(
                $"/api/wallets/org/{orgId}/keys/{derivedKeyId}/rotate", null, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Derived key {DerivedKeyId} not found for rotation", derivedKeyId);
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<DerivedKeyResponse>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate org key {DerivedKeyId} for {OrgId}", derivedKeyId, orgId);
            throw;
        }
    }

    public async Task<RevokeKeyResponse?> RevokeOrgKeyAsync(
        string orgId,
        Guid derivedKeyId,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Revoking org key {DerivedKeyId} for {OrgId}", derivedKeyId, orgId);

            await SetAuthHeaderAsync(ct);

            var response = await _httpClient.DeleteAsync(
                $"/api/wallets/org/{orgId}/keys/{derivedKeyId}", ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Derived key {DerivedKeyId} not found for revocation", derivedKeyId);
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<RevokeKeyResponse>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke org key {DerivedKeyId} for {OrgId}", derivedKeyId, orgId);
            throw;
        }
    }

    // =========================================================================
    // Private Helpers
    // =========================================================================

    private Task SetAuthHeaderAsync(CancellationToken cancellationToken) =>
        ServiceClientAuthHelper.SetAuthHeaderAsync(
            _httpClient, _serviceAuth, _logger, "Wallet Service", cancellationToken);

    // =========================================================================
    // Response DTOs
    // =========================================================================

    private sealed class CreateWalletResponse
    {
        [JsonPropertyName("wallet")]
        public WalletInfo? Wallet { get; set; }
    }

    private sealed class SystemWalletResponse
    {
        [JsonPropertyName("address")]
        public string? Address { get; set; }
    }

    private sealed class SignRequest
    {
        [JsonPropertyName("transactionData")]
        public string TransactionData { get; set; } = string.Empty;

        [JsonPropertyName("derivationPath")]
        public string? DerivationPath { get; set; }

        [JsonPropertyName("isPreHashed")]
        public bool IsPreHashed { get; set; }
    }

    private sealed class SignResponse
    {
        [JsonPropertyName("signature")]
        public string Signature { get; set; } = string.Empty;

        [JsonPropertyName("signedBy")]
        public string SignedBy { get; set; } = string.Empty;

        [JsonPropertyName("signedAt")]
        public DateTime SignedAt { get; set; }

        [JsonPropertyName("publicKey")]
        public string PublicKey { get; set; } = string.Empty;

        [JsonPropertyName("algorithm")]
        public string? Algorithm { get; set; }
    }

    private sealed class VerifyResponse
    {
        [JsonPropertyName("isValid")]
        public bool IsValid { get; set; }
    }

    /// <inheritdoc/>
    public async Task<FileDownloadStreamResult?> DownloadFileAsync(
        string walletAddress,
        string registerId,
        string txId,
        string fieldName,
        int fileIndex = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{_serviceAddress}/api/v1/wallets/{Uri.EscapeDataString(walletAddress)}/files/download" +
                      $"?registerId={Uri.EscapeDataString(registerId)}" +
                      $"&txId={Uri.EscapeDataString(txId)}" +
                      $"&fieldName={Uri.EscapeDataString(fieldName)}" +
                      $"&fileIndex={fileIndex}";

            await SetAuthHeaderAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("File download failed for wallet {Address}, action {TxId}: {Status}",
                    walletAddress, txId, response.StatusCode);
                return null;
            }

            var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                        ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                        ?? "download";
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

            return new FileDownloadStreamResult(fileName, contentType, contentStream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file for wallet {Address}, action {TxId}",
                walletAddress, txId);
            return null;
        }
    }

    private sealed class EncryptResponse
    {
        [JsonPropertyName("encryptedPayload")]
        public string? EncryptedPayload { get; set; }
    }

    private sealed class DecryptResponse
    {
        [JsonPropertyName("decryptedPayload")]
        public string? DecryptedPayload { get; set; }
    }
}
