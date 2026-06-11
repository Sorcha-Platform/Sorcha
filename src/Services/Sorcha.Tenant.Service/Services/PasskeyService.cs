// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

using Fido2NetLib;
using Fido2NetLib.Objects;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// FIDO2/WebAuthn passkey service implementation.
/// Uses Fido2NetLib for credential creation/assertion and IDistributedCache for challenge storage.
/// Implements cloned authenticator detection via signature counter regression.
/// </summary>
public class PasskeyService : IPasskeyService
{
    private static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(5);

    private readonly IFido2 _fido2;
    private readonly IDistributedCache _cache;
    private readonly TenantDbContext _db;
    private readonly IAuthMethodService _authMethodService;
    private readonly ISecurityChangeNotifier _notifier;
    private readonly ILogger<PasskeyService> _logger;

    /// <summary>
    /// Creates a new PasskeyService instance.
    /// </summary>
    /// <param name="fido2">FIDO2 library for credential operations.</param>
    /// <param name="cache">Distributed cache for storing challenge state.</param>
    /// <param name="db">Tenant database context.</param>
    /// <param name="authMethodService">Floor service used during revocation to enforce the last-method floor (Feature 116 US2).</param>
    /// <param name="notifier">Always-notify sink for passkey add/remove/rename (Feature 150).</param>
    /// <param name="logger">Logger instance.</param>
    public PasskeyService(
        IFido2 fido2,
        IDistributedCache cache,
        TenantDbContext db,
        IAuthMethodService authMethodService,
        ISecurityChangeNotifier notifier,
        ILogger<PasskeyService> logger)
    {
        ArgumentNullException.ThrowIfNull(fido2);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(authMethodService);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(logger);

        _fido2 = fido2;
        _cache = cache;
        _db = db;
        _authMethodService = authMethodService;
        _notifier = notifier;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RegistrationOptionsResult> CreateRegistrationOptionsAsync(
        Guid platformUserId,
        string displayName,
        IEnumerable<byte[]>? existingCredentialIds = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Creating passkey registration options for PlatformUser {PlatformUserId}",
            platformUserId);

        var user = new Fido2User
        {
            Id = platformUserId.ToByteArray(),
            Name = displayName,
            DisplayName = displayName
        };

        var excludeCredentials = existingCredentialIds?
            .Select(id => new PublicKeyCredentialDescriptor(id))
            .ToList() ?? [];

        var authenticatorSelection = new AuthenticatorSelection
        {
            ResidentKey = ResidentKeyRequirement.Preferred,
            UserVerification = UserVerificationRequirement.Preferred
        };

        var options = _fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = user,
            ExcludeCredentials = excludeCredentials,
            AuthenticatorSelection = authenticatorSelection,
            AttestationPreference = AttestationConveyancePreference.None
        });

        var transactionId = Guid.NewGuid().ToString();

        var cacheEntry = new RegistrationCacheEntry
        {
            OptionsJson = options.ToJson(),
            PlatformUserId = platformUserId,
            DisplayName = displayName
        };

        var cacheKey = $"passkey:challenge:{transactionId}";
        var cacheBytes = JsonSerializer.SerializeToUtf8Bytes(cacheEntry);

        await _cache.SetAsync(
            cacheKey,
            cacheBytes,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ChallengeTtl },
            cancellationToken);

        _logger.LogInformation(
            "Created passkey registration options for PlatformUser {PlatformUserId}, transactionId={TransactionId}",
            platformUserId, transactionId);

        return new RegistrationOptionsResult(transactionId, options);
    }

    /// <inheritdoc />
    public async Task<PasskeyCredential> VerifyRegistrationAsync(
        string transactionId,
        AuthenticatorAttestationRawResponse attestationResponse,
        bool persist = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Verifying passkey registration for transactionId={TransactionId}", transactionId);

        var cacheKey = $"passkey:challenge:{transactionId}";
        var cacheBytes = await _cache.GetAsync(cacheKey, cancellationToken);

        if (cacheBytes is null)
        {
            throw new InvalidOperationException("Challenge expired or not found");
        }

        // Remove challenge (one-time use)
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        var cacheEntry = JsonSerializer.Deserialize<RegistrationCacheEntry>(cacheBytes)
            ?? throw new InvalidOperationException("Failed to deserialize registration challenge");

        var originalOptions = CredentialCreateOptions.FromJson(cacheEntry.OptionsJson);

        var result = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = attestationResponse,
            OriginalOptions = originalOptions,
            IsCredentialIdUniqueToUserCallback = async (args, ct) =>
            {
                var credentialId = args.CredentialId;
                var exists = await _db.PasskeyCredentials
                    .AnyAsync(c => c.CredentialId == credentialId, ct);
                return !exists;
            }
        }, cancellationToken);

        var credential = new PasskeyCredential
        {
            CredentialId = result.Id,
            PublicKeyCose = result.PublicKey,
            SignatureCounter = (long)result.SignCount,
            PlatformUserId = cacheEntry.PlatformUserId,
            DisplayName = cacheEntry.DisplayName,
            AttestationType = "none",
            AaGuid = result.AaGuid,
            Status = CredentialStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        if (persist)
        {
            _db.PasskeyCredentials.Add(credential);
            await _db.SaveChangesAsync(cancellationToken);
            await _notifier.NotifyAsync(credential.PlatformUserId, SecurityChangeKind.PasskeyAdded, cancellationToken);
        }

        _logger.LogInformation(
            "Passkey registered for PlatformUser {PlatformUserId}, credentialId={CredentialId}",
            credential.PlatformUserId, credential.Id);

        return credential;
    }

    /// <inheritdoc />
    public async Task<AssertionOptionsResult> CreateAssertionOptionsAsync(
        string? email = null,
        IEnumerable<byte[]>? allowedCredentialIds = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating passkey assertion options, email={Email}", email ?? "(none)");

        var allowCredentials = allowedCredentialIds?
            .Select(id => new PublicKeyCredentialDescriptor(id))
            .ToList() ?? [];

        var options = _fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowCredentials,
            UserVerification = UserVerificationRequirement.Preferred
        });

        var transactionId = Guid.NewGuid().ToString();

        var cacheKey = $"passkey:challenge:{transactionId}";
        var cacheBytes = Encoding.UTF8.GetBytes(options.ToJson());

        await _cache.SetAsync(
            cacheKey,
            cacheBytes,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ChallengeTtl },
            cancellationToken);

        _logger.LogInformation(
            "Created passkey assertion options, transactionId={TransactionId}",
            transactionId);

        return new AssertionOptionsResult(transactionId, options);
    }

    /// <inheritdoc />
    public async Task<AssertionVerificationResult> VerifyAssertionAsync(
        string transactionId,
        AuthenticatorAssertionRawResponse assertionResponse,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Verifying passkey assertion for transactionId={TransactionId}", transactionId);

        var cacheKey = $"passkey:challenge:{transactionId}";
        var cacheBytes = await _cache.GetAsync(cacheKey, cancellationToken);

        if (cacheBytes is null)
        {
            throw new InvalidOperationException("Challenge expired or not found");
        }

        // Remove challenge (one-time use)
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        var originalOptions = AssertionOptions.FromJson(Encoding.UTF8.GetString(cacheBytes));

        // Look up the credential by the credential ID from the assertion response
        var credential = await _db.PasskeyCredentials
            .FirstOrDefaultAsync(c => c.CredentialId == assertionResponse.RawId, cancellationToken);

        if (credential is null)
        {
            throw new InvalidOperationException("Credential not found");
        }

        if (credential.Status != CredentialStatus.Active)
        {
            throw new InvalidOperationException($"Credential is {credential.Status}");
        }

        var assertionResult = await _fido2.MakeAssertionAsync(new MakeAssertionParams
        {
            AssertionResponse = assertionResponse,
            OriginalOptions = originalOptions,
            StoredPublicKey = credential.PublicKeyCose,
            StoredSignatureCounter = (uint)credential.SignatureCounter,
            IsUserHandleOwnerOfCredentialIdCallback = (args, ct) =>
            {
                // Verify the userHandle from the authenticator matches the credential owner
                if (args.UserHandle is null || args.UserHandle.Length == 0)
                    return Task.FromResult(true); // Non-discoverable flow: no userHandle to verify

                var claimedOwnerId = new Guid(args.UserHandle);
                return Task.FromResult(claimedOwnerId == credential.PlatformUserId);
            }
        }, cancellationToken);

        // Cloned authenticator detection: counter regression
        if (assertionResult.SignCount < (uint)credential.SignatureCounter)
        {
            _logger.LogWarning(
                "Signature counter regression detected for credential {CredentialId}. " +
                "Stored={StoredCounter}, Received={ReceivedCounter}. Disabling credential.",
                credential.Id, credential.SignatureCounter, assertionResult.SignCount);

            credential.Status = CredentialStatus.Disabled;
            credential.DisabledAt = DateTimeOffset.UtcNow;
            credential.DisabledReason = "Signature counter regression detected";
            await _db.SaveChangesAsync(cancellationToken);

            throw new InvalidOperationException(
                "Signature counter regression detected — possible cloned authenticator. Credential has been disabled.");
        }

        // Update counter and last used timestamp
        credential.SignatureCounter = (long)assertionResult.SignCount;
        credential.LastUsedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Passkey assertion verified for PlatformUser {PlatformUserId}, credentialId={CredentialId}",
            credential.PlatformUserId, credential.Id);

        return new AssertionVerificationResult(credential, credential.PlatformUserId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PasskeyCredential>> GetCredentialsByOwnerAsync(
        Guid platformUserId,
        CancellationToken cancellationToken = default)
    {
        return await _db.PasskeyCredentials
            .Where(c => c.PlatformUserId == platformUserId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PasskeyCredential?> GetCredentialAsync(
        Guid credentialId,
        Guid platformUserId,
        CancellationToken cancellationToken = default)
    {
        return await _db.PasskeyCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Id == credentialId && c.PlatformUserId == platformUserId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PasskeyRevocationOutcome> RevokeCredentialAsync(
        Guid credentialId,
        Guid platformUserId,
        CancellationToken cancellationToken = default)
    {
        // Tracking read inside the same SaveChanges batch as the mutation so the
        // floor check, the status flip, and the audit-reason write all commit
        // atomically. The endpoint does its own status read for challenge-gating
        // decisions; the prior status seen here is authoritative for the reason
        // string regardless of any intervening Active→Disabled flip.
        var credential = await _db.PasskeyCredentials
            .FirstOrDefaultAsync(
                c => c.Id == credentialId && c.PlatformUserId == platformUserId,
                cancellationToken);

        if (credential is null)
        {
            _logger.LogWarning(
                "Passkey credential {CredentialId} not found for PlatformUser {PlatformUserId} during revocation",
                credentialId, platformUserId);
            return PasskeyRevocationOutcome.NotFound;
        }

        if (credential.Status == CredentialStatus.Revoked)
        {
            // Idempotency: a previously revoked row is treated as not-found by
            // the API surface to avoid leaking the existence of revoked rows.
            return PasskeyRevocationOutcome.AlreadyRevoked;
        }

        var priorStatus = credential.Status;

        if (priorStatus == CredentialStatus.Active)
        {
            // Floor enforcement only fires for Active passkeys — Disabled rows
            // do not count toward the active-method total in
            // AuthMethodService.GetCountsAsync, so removing one cannot violate
            // the floor.
            var leavesZero = await _authMethodService.WouldRemovingLeaveZeroAsync(
                platformUserId,
                AuthMethodKind.Passkey,
                credentialId,
                cancellationToken);

            if (leavesZero)
            {
                _logger.LogWarning(
                    "Passkey revocation blocked by floor for PlatformUser {PlatformUserId} CredentialId {CredentialId}",
                    platformUserId, credentialId);
                return PasskeyRevocationOutcome.BlockedByFloor;
            }
        }

        credential.Status = CredentialStatus.Revoked;
        credential.DisabledAt = DateTimeOffset.UtcNow;
        credential.DisabledReason = priorStatus == CredentialStatus.Active
            ? "user-removed"
            : "user-removed-after-disable";
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Passkey credential {CredentialId} revoked for PlatformUser {PlatformUserId} prior={PriorStatus}",
            credentialId, platformUserId, priorStatus);

        await _notifier.NotifyAsync(platformUserId, SecurityChangeKind.PasskeyRemoved, cancellationToken);

        return priorStatus == CredentialStatus.Active
            ? PasskeyRevocationOutcome.RevokedFromActive
            : PasskeyRevocationOutcome.RevokedFromDisabled;
    }

    /// <inheritdoc />
    public async Task<PasskeyRenameOutcome> RenameCredentialAsync(
        Guid credentialId,
        Guid platformUserId,
        string newDisplayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newDisplayName);

        var credential = await _db.PasskeyCredentials
            .FirstOrDefaultAsync(
                c => c.Id == credentialId && c.PlatformUserId == platformUserId,
                cancellationToken);

        if (credential is null)
        {
            return PasskeyRenameOutcome.NotFound;
        }

        // Disabled / Revoked rows are read-only by design — the audit row's
        // identity is anchored in time. The contract returns 409 from the
        // endpoint to make this explicit to API consumers.
        if (credential.Status == CredentialStatus.Disabled)
        {
            return PasskeyRenameOutcome.BlockedByDisabled;
        }
        if (credential.Status == CredentialStatus.Revoked)
        {
            return PasskeyRenameOutcome.BlockedByRevoked;
        }

        credential.DisplayName = newDisplayName.Trim();
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Passkey credential {CredentialId} renamed for PlatformUser {PlatformUserId}",
            credentialId, platformUserId);

        await _notifier.NotifyAsync(platformUserId, SecurityChangeKind.PasskeyRenamed, cancellationToken);

        return PasskeyRenameOutcome.Renamed;
    }

    /// <summary>
    /// Cache entry for storing registration options alongside owner metadata.
    /// </summary>
    private sealed class RegistrationCacheEntry
    {
        /// <summary>
        /// Serialized CredentialCreateOptions JSON.
        /// </summary>
        public string OptionsJson { get; set; } = string.Empty;

        /// <summary>
        /// Platform user ID that will own the credential.
        /// </summary>
        public Guid PlatformUserId { get; set; }

        /// <summary>
        /// Display name for the credential.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;
    }
}
