// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Protos;

namespace Sorcha.Wallet.Service.GrpcServices;

/// <summary>
/// gRPC service implementation for Wallet Service integration.
/// </summary>
/// <remarks>
/// <para>
/// This service provides gRPC endpoints for wallet operations including wallet details
/// retrieval, data signing, signature verification, and derived key access. It serves
/// as the integration point for other Sorcha services (Validator, Peer, etc.) to
/// perform cryptographic operations using managed wallets.
/// </para>
///
/// <para><b>Key Operations:</b></para>
/// <list type="bullet">
///   <item>GetWalletDetails - Retrieve wallet metadata and public key</item>
///   <item>SignData - Sign arbitrary data using wallet private key</item>
///   <item>VerifySignature - Verify signatures using wallet public key</item>
///   <item>GetDerivedKey - Retrieve BIP44-derived private keys (FR-017)</item>
/// </list>
///
/// <para><b>Security:</b></para>
/// <list type="bullet">
///   <item>Root private key never exposed (FR-012)</item>
///   <item>Derived keys only returned for authorized services</item>
///   <item>All operations logged for audit trail</item>
///   <item>Service-to-service authentication required (future: mTLS)</item>
/// </list>
///
/// <para><b>Related Requirements:</b></para>
/// <list type="bullet">
///   <item>FR-001: Provide wallet access to Validator Service</item>
///   <item>FR-012: Root key never exposed</item>
///   <item>FR-017: Support derived key retrieval for performance</item>
///   <item>SC-005: gRPC response time &lt; 100ms (p95)</item>
/// </list>
/// </remarks>
public class WalletGrpcService : Protos.WalletService.WalletServiceBase
{
    private readonly ILogger<WalletGrpcService> _logger;
    private readonly IWalletRepository _repository;
    private readonly IKeyManagementService _keyManagement;
    private readonly ICryptoModule _cryptoModule;

    /// <summary>
    /// Initializes a new instance of the <see cref="WalletGrpcService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="repository">Wallet repository for data access.</param>
    /// <param name="keyManagement">Key management service for decryption and derivation.</param>
    /// <param name="cryptoModule">Cryptographic module for signing and verification.</param>
    public WalletGrpcService(
        ILogger<WalletGrpcService> logger,
        IWalletRepository repository,
        IKeyManagementService keyManagement,
        ICryptoModule cryptoModule)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _keyManagement = keyManagement ?? throw new ArgumentNullException(nameof(keyManagement));
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
    }

    /// <summary>
    /// Retrieves wallet details including public key, address, and algorithm.
    /// </summary>
    /// <param name="request">Request containing wallet ID.</param>
    /// <param name="context">gRPC server call context.</param>
    /// <returns>Wallet details response.</returns>
    /// <remarks>
    /// <para>
    /// This method returns wallet metadata needed by consuming services to identify
    /// the wallet and verify signatures. The public key is included for signature
    /// verification without requiring additional round trips.
    /// </para>
    ///
    /// <para><b>Performance:</b> Target p95 &lt; 50ms (database query + mapping)</para>
    ///
    /// <para><b>Error Handling:</b></para>
    /// <list type="bullet">
    ///   <item>NotFound - Wallet ID does not exist</item>
    ///   <item>Unavailable - Database connection failure</item>
    /// </list>
    /// </remarks>
    public override async Task<WalletDetailsResponse> GetWalletDetails(
        GetWalletDetailsRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation("GetWalletDetails called for wallet ID: {WalletId}", request.WalletId);

        if (string.IsNullOrWhiteSpace(request.WalletId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Wallet ID is required"));

        Core.Domain.Entities.Wallet wallet;
        try
        {
            var result = await _repository.GetByAddressAsync(
                request.WalletId,
                cancellationToken: context.CancellationToken);

            if (result is null)
                throw new RpcException(new Status(StatusCode.NotFound, $"Wallet '{request.WalletId}' not found"));

            wallet = result;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve wallet {WalletId}", request.WalletId);
            throw new RpcException(new Status(StatusCode.Unavailable, "Database connection failure"));
        }

        var response = new WalletDetailsResponse
        {
            WalletId = wallet.Address,
            Address = wallet.Address,
            Algorithm = ConvertAlgorithmToProto(wallet.Algorithm),
            Version = wallet.Version,
            DerivationPath = wallet.Metadata.GetValueOrDefault("DerivationPath", ""),
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(wallet.CreatedAt, DateTimeKind.Utc))
        };

        if (!string.IsNullOrEmpty(wallet.PublicKey))
            response.PublicKey = ByteString.CopyFrom(Convert.FromBase64String(wallet.PublicKey));

        _logger.LogInformation("GetWalletDetails succeeded for wallet {WalletId}", request.WalletId);
        return response;
    }

    /// <summary>
    /// Signs arbitrary data using the wallet's private key.
    /// </summary>
    /// <param name="request">Request containing wallet ID, data to sign, and algorithm.</param>
    /// <param name="context">gRPC server call context.</param>
    /// <returns>Signature response containing signature bytes.</returns>
    /// <remarks>
    /// <para>
    /// This method performs cryptographic signing using the wallet's root private key
    /// or derived key (if derivation path provided). The signature can be verified
    /// using the wallet's public key.
    /// </para>
    ///
    /// <para><b>Performance:</b> Target p95 &lt; 80ms (ED25519), &lt; 150ms (RSA4096)</para>
    ///
    /// <para><b>Security:</b></para>
    /// <list type="bullet">
    ///   <item>Validates data is pre-hashed (32 bytes for SHA-256)</item>
    ///   <item>Logs all sign operations for audit</item>
    ///   <item>Future: Rate limiting per wallet ID</item>
    /// </list>
    ///
    /// <para><b>Error Handling:</b></para>
    /// <list type="bullet">
    ///   <item>NotFound - Wallet ID does not exist</item>
    ///   <item>InvalidArgument - Data is not 32 bytes (hash)</item>
    ///   <item>FailedPrecondition - Wallet is locked or deleted</item>
    /// </list>
    /// </remarks>
    public override async Task<SignDataResponse> SignData(
        SignDataRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation(
            "SignData called for wallet ID: {WalletId}, data hash length: {DataHashLength} bytes",
            request.WalletId,
            request.DataHash.Length);

        if (string.IsNullOrWhiteSpace(request.WalletId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Wallet ID is required"));

        if (request.DataHash.IsEmpty || request.DataHash.Length != 32)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Data hash must be exactly 32 bytes (SHA-256), received {request.DataHash.Length} bytes"));

        var wallet = await _repository.GetByAddressAsync(
            request.WalletId,
            cancellationToken: context.CancellationToken);

        if (wallet is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Wallet '{request.WalletId}' not found"));

        if (wallet.Status != WalletStatus.Active)
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                $"Wallet is {wallet.Status} and cannot sign data"));

        if (!AlgorithmMapper.TryParseAlgorithm(wallet.Algorithm, out var network))
            throw new RpcException(new Status(StatusCode.Internal,
                $"Wallet has unsupported algorithm: {wallet.Algorithm}"));
        byte[] privateKey;
        byte[] publicKey;

        // Decrypt the wallet's private key
        var decryptedKey = await _keyManagement.DecryptPrivateKeyAsync(
            wallet.EncryptedPrivateKey, wallet.EncryptionKeyId);

        if (!string.IsNullOrWhiteSpace(request.DerivationPath))
        {
            // Use derived key at specified path
            DerivationPath path;
            try
            {
                path = new DerivationPath(request.DerivationPath);
            }
            catch (ArgumentException ex)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"Invalid derivation path: {ex.Message}"));
            }

            var derived = await _keyManagement.DeriveKeyAtPathAsync(
                decryptedKey, path, wallet.Algorithm);
            privateKey = derived.PrivateKey;
            publicKey = derived.PublicKey;
        }
        else
        {
            // Use the root key directly
            privateKey = decryptedKey;
            publicKey = !string.IsNullOrEmpty(wallet.PublicKey)
                ? Convert.FromBase64String(wallet.PublicKey)
                : Array.Empty<byte>();
        }

        var signResult = await _cryptoModule.SignAsync(
            request.DataHash.ToByteArray(),
            (byte)network,
            privateKey,
            context.CancellationToken);

        if (!signResult.IsSuccess)
            throw new RpcException(new Status(StatusCode.Internal,
                $"Signing failed: {signResult.ErrorMessage}"));

        var response = new SignDataResponse
        {
            Signature = ByteString.CopyFrom(signResult.Value!),
            PublicKey = ByteString.CopyFrom(publicKey),
            Algorithm = ConvertAlgorithmToProto(wallet.Algorithm),
            Version = wallet.Version,
            SignedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        _logger.LogInformation(
            "SignData succeeded for wallet {WalletId}, signature length: {SignatureLength} bytes",
            request.WalletId, signResult.Value!.Length);

        return response;
    }

    /// <summary>
    /// Verifies a signature against data and public key.
    /// </summary>
    /// <param name="request">Request containing signature, data, public key, and algorithm.</param>
    /// <param name="context">gRPC server call context.</param>
    /// <returns>Verification response indicating if signature is valid.</returns>
    /// <remarks>
    /// <para>
    /// This method performs signature verification using the provided public key.
    /// It does not require wallet access and operates as a stateless cryptography
    /// service.
    /// </para>
    ///
    /// <para><b>Performance:</b> Target p95 &lt; 50ms (all algorithms)</para>
    ///
    /// <para><b>Error Handling:</b></para>
    /// <list type="bullet">
    ///   <item>InvalidArgument - Signature, data, or public key invalid</item>
    ///   <item>Internal - Cryptography module error</item>
    /// </list>
    /// </remarks>
    public override async Task<VerifySignatureResponse> VerifySignature(
        VerifySignatureRequest request,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "VerifySignature called with algorithm: {Algorithm}, signature length: {SignatureLength} bytes",
            request.Algorithm,
            request.Signature.Length);

        if (request.Signature.IsEmpty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Signature is required"));

        if (request.DataHash.IsEmpty || request.DataHash.Length != 32)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Data hash must be exactly 32 bytes (SHA-256), received {request.DataHash.Length} bytes"));

        if (request.PublicKey.IsEmpty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Public key is required"));

        if (request.Algorithm == WalletAlgorithm.Unspecified)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Algorithm must be specified"));

        var network = ConvertProtoToNetwork(request.Algorithm);

        try
        {
            var status = await _cryptoModule.VerifyAsync(
                request.Signature.ToByteArray(),
                request.DataHash.ToByteArray(),
                (byte)network,
                request.PublicKey.ToByteArray(),
                context.CancellationToken);

            var isValid = status == CryptoStatus.Success;

            _logger.LogDebug("VerifySignature result: {IsValid}", isValid);

            return new VerifySignatureResponse
            {
                IsValid = isValid,
                ErrorMessage = isValid ? "" : $"Verification failed: {status}",
                VerifiedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cryptography error during signature verification");
            throw new RpcException(new Status(StatusCode.Internal,
                "Cryptography module error during verification"));
        }
    }

    /// <summary>
    /// Retrieves a BIP44-derived private key for local cryptographic operations.
    /// </summary>
    /// <param name="request">Request containing wallet ID and BIP44 derivation path.</param>
    /// <param name="context">gRPC server call context.</param>
    /// <returns>Derived key response containing private key bytes.</returns>
    /// <remarks>
    /// <para>
    /// This method allows consuming services (e.g., Validator Service) to retrieve
    /// derived private keys for local signing operations, achieving 12x performance
    /// improvement over remote signing. The root private key is NEVER exposed.
    /// </para>
    ///
    /// <para><b>Security:</b></para>
    /// <list type="bullet">
    ///   <item>CRITICAL: Only derived keys returned, never root key (FR-012)</item>
    ///   <item>Derivation path must be valid BIP44 format (m/44'/...)</item>
    ///   <item>Future: Restrict derivation paths to authorized patterns per service</item>
    ///   <item>Log all derived key retrievals for audit</item>
    /// </list>
    ///
    /// <para><b>Performance:</b> Target p95 &lt; 100ms (PBKDF2 + BIP32 derivation)</para>
    ///
    /// <para><b>Supported Derivation Paths:</b></para>
    /// <list type="bullet">
    ///   <item>Docket signing: m/44'/0'/0'/0/0</item>
    ///   <item>Vote signing: m/44'/0'/0'/1/0</item>
    ///   <item>Future: Custom paths per service authorization</item>
    /// </list>
    ///
    /// <para><b>Error Handling:</b></para>
    /// <list type="bullet">
    ///   <item>NotFound - Wallet ID does not exist</item>
    ///   <item>InvalidArgument - Invalid derivation path format</item>
    ///   <item>FailedPrecondition - Wallet is not HD wallet</item>
    /// </list>
    ///
    /// <para><b>Related Requirements:</b></para>
    /// <list type="bullet">
    ///   <item>FR-012: Root private key never exposed</item>
    ///   <item>FR-017: Support derived key retrieval for performance</item>
    ///   <item>SC-006: Private keys never persisted outside secure storage</item>
    /// </list>
    /// </remarks>
    public override async Task<GetDerivedKeyResponse> GetDerivedKey(
        GetDerivedKeyRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation(
            "GetDerivedKey called for wallet ID: {WalletId}, derivation path: {DerivationPath}",
            request.WalletId,
            request.DerivationPath);

        if (string.IsNullOrWhiteSpace(request.WalletId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Wallet ID is required"));

        if (string.IsNullOrWhiteSpace(request.DerivationPath))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Derivation path is required"));

        // Validate derivation path format
        DerivationPath derivationPath;
        try
        {
            derivationPath = new DerivationPath(request.DerivationPath);
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Invalid derivation path format: {ex.Message}"));
        }

        var wallet = await _repository.GetByAddressAsync(
            request.WalletId,
            cancellationToken: context.CancellationToken);

        if (wallet is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Wallet '{request.WalletId}' not found"));

        if (wallet.Status != WalletStatus.Active)
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                $"Wallet is {wallet.Status} and cannot provide derived keys"));

        // Verify this is an HD wallet (has encrypted private key that can derive children)
        if (string.IsNullOrEmpty(wallet.EncryptedPrivateKey))
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Wallet does not support key derivation"));

        // Decrypt master key and derive child key
        var masterKey = await _keyManagement.DecryptPrivateKeyAsync(
            wallet.EncryptedPrivateKey, wallet.EncryptionKeyId);

        var (privateKey, publicKey) = await _keyManagement.DeriveKeyAtPathAsync(
            masterKey, derivationPath, wallet.Algorithm);

        var response = new GetDerivedKeyResponse
        {
            PrivateKey = ByteString.CopyFrom(privateKey),
            PublicKey = ByteString.CopyFrom(publicKey),
            Algorithm = ConvertAlgorithmToProto(wallet.Algorithm),
            DerivationPath = derivationPath.Path
        };

        _logger.LogInformation(
            "GetDerivedKey succeeded for wallet {WalletId} at path {DerivationPath}",
            request.WalletId, derivationPath.Path);

        return response;
    }

    private static Protos.WalletAlgorithm ConvertAlgorithmToProto(string algorithm)
    {
        return algorithm.ToUpperInvariant() switch
        {
            "ED25519" => Protos.WalletAlgorithm.Ed25519,
            "NISTP256" => Protos.WalletAlgorithm.Nistp256,
            "RSA4096" => Protos.WalletAlgorithm.Rsa4096,
            _ => Protos.WalletAlgorithm.Unspecified
        };
    }

    private static WalletNetworks ConvertProtoToNetwork(Protos.WalletAlgorithm algorithm)
    {
        return algorithm switch
        {
            Protos.WalletAlgorithm.Ed25519 => WalletNetworks.ED25519,
            Protos.WalletAlgorithm.Nistp256 => WalletNetworks.NISTP256,
            Protos.WalletAlgorithm.Rsa4096 => WalletNetworks.RSA4096,
            _ => throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Unsupported algorithm: {algorithm}"))
        };
    }

}
