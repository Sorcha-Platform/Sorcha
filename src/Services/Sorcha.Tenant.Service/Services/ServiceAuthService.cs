// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service implementation for service-to-service authentication.
/// </summary>
public class ServiceAuthService : IServiceAuthService
{
    private readonly IIdentityRepository _identityRepository;
    private readonly ITokenService _tokenService;
    private readonly ILogger<ServiceAuthService> _logger;

    public ServiceAuthService(
        IIdentityRepository identityRepository,
        ITokenService tokenService,
        ILogger<ServiceAuthService> logger)
    {
        _identityRepository = identityRepository ?? throw new ArgumentNullException(nameof(identityRepository));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TokenResponse?> AuthenticateServiceAsync(
        string clientId,
        string clientSecret,
        string? requestedScopes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return null;
        }

        var servicePrincipal = await _identityRepository.GetServicePrincipalByClientIdAsync(clientId, cancellationToken);
        if (servicePrincipal == null)
        {
            _logger.LogWarning("Service authentication failed: client ID {ClientId} not found", clientId);
            return null;
        }

        if (servicePrincipal.Status != ServicePrincipalStatus.Active)
        {
            _logger.LogWarning("Service authentication failed: client {ClientId} is {Status}", clientId, servicePrincipal.Status);
            return null;
        }

        // Verify client secret
        if (!VerifyClientSecret(clientSecret, servicePrincipal.ClientSecretEncrypted))
        {
            _logger.LogWarning("Service authentication failed: invalid secret for client {ClientId}", clientId);
            return null;
        }

        // Filter requested scopes to allowed scopes
        if (!string.IsNullOrEmpty(requestedScopes))
        {
            var requested = requestedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var allowed = servicePrincipal.Scopes.Intersect(requested).ToArray();
            if (allowed.Length == 0)
            {
                _logger.LogWarning("Service authentication failed: no valid scopes for client {ClientId}", clientId);
                return null;
            }
            // Use filtered scopes for token generation
            servicePrincipal.Scopes = allowed;
        }

        var tokenResponse = await _tokenService.GenerateServiceTokenAsync(servicePrincipal, null, null, cancellationToken);

        _logger.LogInformation("Service {ServiceName} authenticated successfully", servicePrincipal.ServiceName);
        return tokenResponse;
    }

    /// <inheritdoc />
    public async Task<TokenResponse?> AuthenticateWithDelegationAsync(
        string clientId,
        string clientSecret,
        Guid delegatedUserId,
        Guid? delegatedOrgId,
        string? requestedScopes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return null;
        }

        var servicePrincipal = await _identityRepository.GetServicePrincipalByClientIdAsync(clientId, cancellationToken);
        if (servicePrincipal == null)
        {
            _logger.LogWarning("Delegated auth failed: client ID {ClientId} not found", clientId);
            return null;
        }

        if (servicePrincipal.Status != ServicePrincipalStatus.Active)
        {
            _logger.LogWarning("Delegated auth failed: client {ClientId} is {Status}", clientId, servicePrincipal.Status);
            return null;
        }

        if (!VerifyClientSecret(clientSecret, servicePrincipal.ClientSecretEncrypted))
        {
            _logger.LogWarning("Delegated auth failed: invalid secret for client {ClientId}", clientId);
            return null;
        }

        // Verify the service has delegation scope
        if (!servicePrincipal.Scopes.Contains("tenant:delegate"))
        {
            _logger.LogWarning("Delegated auth failed: client {ClientId} does not have delegation scope", clientId);
            return null;
        }

        var tokenResponse = await _tokenService.GenerateServiceTokenAsync(
            servicePrincipal, delegatedUserId, delegatedOrgId, cancellationToken);

        _logger.LogInformation(
            "Service {ServiceName} authenticated with delegation for user {UserId}",
            servicePrincipal.ServiceName, delegatedUserId);

        return tokenResponse;
    }

    /// <inheritdoc />
    public async Task<ServicePrincipalRegistrationResponse> RegisterServicePrincipalAsync(
        string serviceName,
        string[] scopes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Service name is required", nameof(serviceName));
        }

        // Check if service already exists
        var existing = await _identityRepository.GetServicePrincipalByNameAsync(serviceName, cancellationToken);
        if (existing != null)
        {
            throw new InvalidOperationException($"Service principal '{serviceName}' already exists");
        }

        // Generate credentials
        var clientId = $"service-{serviceName.ToLowerInvariant().Replace(" ", "-")}";
        var clientSecret = GenerateClientSecret();
        var encryptedSecret = EncryptClientSecret(clientSecret);

        var servicePrincipal = new ServicePrincipal
        {
            ServiceName = serviceName,
            ClientId = clientId,
            ClientSecretEncrypted = encryptedSecret,
            Scopes = scopes,
            Status = ServicePrincipalStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var created = await _identityRepository.CreateServicePrincipalAsync(servicePrincipal, cancellationToken);

        _logger.LogInformation("Registered service principal {ServiceName} with client ID {ClientId}", serviceName, clientId);

        return new ServicePrincipalRegistrationResponse
        {
            Id = created.Id,
            ServiceName = created.ServiceName,
            ClientId = created.ClientId,
            ClientSecret = clientSecret, // Only time it's returned in plaintext
            Scopes = created.Scopes
        };
    }

    /// <inheritdoc />
    public async Task<ServicePrincipalResponse?> GetServicePrincipalAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var sp = await _identityRepository.GetServicePrincipalByIdAsync(id, cancellationToken);
        return sp != null ? ServicePrincipalResponse.FromEntity(sp) : null;
    }

    /// <inheritdoc />
    public async Task<ServicePrincipalResponse?> GetServicePrincipalByClientIdAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var sp = await _identityRepository.GetServicePrincipalByClientIdAsync(clientId, cancellationToken);
        return sp != null ? ServicePrincipalResponse.FromEntity(sp) : null;
    }

    /// <inheritdoc />
    public async Task<ServicePrincipalListResponse> ListServicePrincipalsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var principals = includeInactive
            ? await _identityRepository.GetAllServicePrincipalsAsync(cancellationToken)
            : await _identityRepository.GetActiveServicePrincipalsAsync(cancellationToken);

        return new ServicePrincipalListResponse
        {
            ServicePrincipals = principals.Select(ServicePrincipalResponse.FromEntity).ToList(),
            TotalCount = principals.Count
        };
    }

    /// <inheritdoc />
    public async Task<ServicePrincipalResponse?> UpdateServicePrincipalScopesAsync(
        Guid id,
        string[] scopes,
        CancellationToken cancellationToken = default)
    {
        var sp = await _identityRepository.GetServicePrincipalByIdAsync(id, cancellationToken);
        if (sp == null)
        {
            return null;
        }

        sp.Scopes = scopes;
        var updated = await _identityRepository.UpdateServicePrincipalAsync(sp, cancellationToken);

        _logger.LogInformation("Updated scopes for service principal {ServiceName}", sp.ServiceName);
        return ServicePrincipalResponse.FromEntity(updated);
    }

    /// <inheritdoc />
    public async Task<RotateSecretResponse?> RotateSecretAsync(
        string clientId,
        string currentSecret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(currentSecret))
        {
            return null;
        }

        var sp = await _identityRepository.GetServicePrincipalByClientIdAsync(clientId, cancellationToken);
        if (sp == null)
        {
            return null;
        }

        if (!VerifyClientSecret(currentSecret, sp.ClientSecretEncrypted))
        {
            _logger.LogWarning("Secret rotation failed: invalid current secret for {ClientId}", clientId);
            return null;
        }

        // Generate new secret
        var newSecret = GenerateClientSecret();
        sp.ClientSecretEncrypted = EncryptClientSecret(newSecret);

        await _identityRepository.UpdateServicePrincipalAsync(sp, cancellationToken);

        _logger.LogInformation("Rotated secret for service principal {ServiceName}", sp.ServiceName);

        return new RotateSecretResponse
        {
            NewClientSecret = newSecret
        };
    }

    /// <inheritdoc />
    public async Task<bool> SuspendServicePrincipalAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var sp = await _identityRepository.GetServicePrincipalByIdAsync(id, cancellationToken);
        if (sp == null)
        {
            return false;
        }

        sp.Status = ServicePrincipalStatus.Suspended;
        await _identityRepository.UpdateServicePrincipalAsync(sp, cancellationToken);

        _logger.LogInformation("Suspended service principal {ServiceName}", sp.ServiceName);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ReactivateServicePrincipalAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var sp = await _identityRepository.GetServicePrincipalByIdAsync(id, cancellationToken);
        if (sp == null || sp.Status == ServicePrincipalStatus.Revoked)
        {
            return false;
        }

        sp.Status = ServicePrincipalStatus.Active;
        await _identityRepository.UpdateServicePrincipalAsync(sp, cancellationToken);

        _logger.LogInformation("Reactivated service principal {ServiceName}", sp.ServiceName);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RevokeServicePrincipalAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var sp = await _identityRepository.GetServicePrincipalByIdAsync(id, cancellationToken);
        if (sp == null)
        {
            return false;
        }

        await _identityRepository.DeactivateServicePrincipalAsync(id, cancellationToken);

        _logger.LogInformation("Revoked service principal {ServiceName}", sp.ServiceName);
        return true;
    }

    /// <summary>
    /// Generates a cryptographically secure client secret.
    /// </summary>
    private static string GenerateClientSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>
    /// Hashes the client secret using Argon2id for secure storage.
    /// Returns salt(16) + hash(32) = 48 bytes.
    /// </summary>
    private static byte[] EncryptClientSecret(string secret)
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);

        var parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithSalt(salt)
            .WithMemoryAsKB(65536)  // 64MB
            .WithIterations(3)
            .WithParallelism(4)
            .Build();

        var generator = new Argon2BytesGenerator();
        generator.Init(parameters);

        var hash = new byte[32];
        generator.GenerateBytes(Encoding.UTF8.GetBytes(secret), hash);

        // Store as: salt(16) + hash(32) = 48 bytes
        var result = new byte[48];
        Buffer.BlockCopy(salt, 0, result, 0, 16);
        Buffer.BlockCopy(hash, 0, result, 16, 32);
        return result;
    }

    /// <summary>
    /// Verifies a client secret against the stored hash.
    /// Supports both legacy SHA256 (32 bytes) and Argon2id (48 bytes) formats.
    /// </summary>
    private static bool VerifyClientSecret(string providedSecret, byte[] storedHash)
    {
        if (storedHash.Length == 32)
        {
            // Legacy SHA256 format — verify old way
            using var sha256 = SHA256.Create();
            var providedHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(providedSecret));
            return CryptographicOperations.FixedTimeEquals(providedHash, storedHash);
        }

        // Argon2id format: salt(16) + hash(32)
        var salt = storedHash[..16];
        var expectedHash = storedHash[16..];

        var parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithSalt(salt)
            .WithMemoryAsKB(65536)
            .WithIterations(3)
            .WithParallelism(4)
            .Build();

        var generator = new Argon2BytesGenerator();
        generator.Init(parameters);

        var computedHash = new byte[32];
        generator.GenerateBytes(Encoding.UTF8.GetBytes(providedSecret), computedHash);

        return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
    }
}

