// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Subscription;

namespace Sorcha.Register.Core.Managers;

/// <summary>
/// Manages register operations (CRUD, status, height tracking)
/// </summary>
public class RegisterManager
{
    private readonly IRegisterRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ISubscriptionServiceClient? _subscriptionClient;
    private readonly ILogger<RegisterManager>? _logger;

    public RegisterManager(
        IRegisterRepository repository,
        IEventPublisher eventPublisher,
        ISubscriptionServiceClient? subscriptionClient = null,
        ILogger<RegisterManager>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _subscriptionClient = subscriptionClient;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new register
    /// </summary>
    /// <param name="name">Register name</param>
    /// <param name="advertise">Whether to advertise this register to peers</param>
    /// <param name="isFullReplica">Whether this is a full replica</param>
    /// <param name="registerId">Optional pre-generated register ID (used by two-phase creation flow)</param>
    /// <param name="description">Optional register description</param>
    /// <param name="purpose">Register purpose classification</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task<Models.Register> CreateRegisterAsync(
        string name,
        bool advertise = false,
        bool isFullReplica = true,
        string? registerId = null,
        string? description = null,
        bool devMode = false,
        RegisterPurpose purpose = RegisterPurpose.General,
        string? syncState = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Length > 38 || name.Length < 1)
        {
            throw new ArgumentException("Register name must be between 1 and 38 characters", nameof(name));
        }

        var register = new Models.Register
        {
            Id = registerId ?? Guid.NewGuid().ToString("N"), // Use provided ID or generate new one
            Name = name,
            Description = description,
            Height = 0,
            Status = RegisterStatus.Offline,
            Advertise = advertise,
            IsFullReplica = isFullReplica,
            Purpose = purpose,
            DevMode = devMode,
            SyncState = syncState,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var createdRegister = await _repository.InsertRegisterAsync(register, cancellationToken);

        // Publish register created event
        await _eventPublisher.PublishAsync(
            "register:created",
            new RegisterCreatedEvent
            {
                RegisterId = createdRegister.Id,
                Name = createdRegister.Name,
                Purpose = createdRegister.Purpose,
                CreatedAt = createdRegister.CreatedAt
            },
            cancellationToken);

        return createdRegister;
    }

    /// <summary>
    /// Gets a register by ID
    /// </summary>
    public async Task<Models.Register?> GetRegisterAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        return await _repository.GetRegisterAsync(registerId, cancellationToken);
    }

    /// <summary>
    /// Gets all registers
    /// </summary>
    public async Task<IEnumerable<Models.Register>> GetAllRegistersAsync(
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetRegistersAsync(cancellationToken);
    }

    /// <summary>
    /// Updates register metadata
    /// </summary>
    public async Task<Models.Register> UpdateRegisterAsync(
        Models.Register register,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(register);

        var existingRegister = await _repository.GetRegisterAsync(register.Id, cancellationToken);
        if (existingRegister == null)
        {
            throw new InvalidOperationException($"Register {register.Id} not found");
        }

        return await _repository.UpdateRegisterAsync(register, cancellationToken);
    }

    /// <summary>
    /// Updates register status
    /// </summary>
    public virtual async Task<Models.Register> UpdateRegisterStatusAsync(
        string registerId,
        RegisterStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        var register = await _repository.GetRegisterAsync(registerId, cancellationToken);
        if (register == null)
        {
            throw new InvalidOperationException($"Register {registerId} not found");
        }

        var oldStatus = register.Status;
        register.Status = newStatus;
        register.UpdatedAt = DateTime.UtcNow;

        var updated = await _repository.UpdateRegisterAsync(register, cancellationToken);

        // Publish status changed event
        await _eventPublisher.PublishAsync(
            "register:status-changed",
            new RegisterStatusChangedEvent
            {
                RegisterId = registerId,
                OldStatus = oldStatus.ToString(),
                NewStatus = newStatus.ToString(),
                ChangedAt = updated.UpdatedAt
            },
            cancellationToken);

        return updated;
    }

    /// <summary>
    /// Updates the sync state of a register and publishes a sync-state-changed event.
    /// </summary>
    /// <param name="registerId">Register ID to update</param>
    /// <param name="syncState">New sync state (null to clear)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated register, or null if not found</returns>
    public virtual async Task<Models.Register?> UpdateSyncStateAsync(
        string registerId,
        string? syncState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        var register = await _repository.GetRegisterAsync(registerId, cancellationToken);
        if (register == null)
        {
            _logger?.LogWarning("Cannot update sync state: register {RegisterId} not found", registerId);
            return null;
        }

        var previousSyncState = register.SyncState;
        register.SyncState = syncState;
        register.UpdatedAt = DateTime.UtcNow;

        var updated = await _repository.UpdateRegisterAsync(register, cancellationToken);

        _logger?.LogInformation(
            "Register {RegisterId} sync state changed: {PreviousState} → {NewState}",
            registerId, previousSyncState ?? "null", syncState ?? "null");

        await _eventPublisher.PublishAsync(
            "register:sync-state-changed",
            new RegisterSyncStateChangedEvent
            {
                RegisterId = registerId,
                SyncState = syncState ?? string.Empty,
                PreviousSyncState = previousSyncState,
                ChangedAt = updated.UpdatedAt
            },
            cancellationToken);

        return updated;
    }

    /// <summary>
    /// Irreversibly disables dev mode on a register, enabling mandatory field-level encryption.
    /// Once disabled, dev mode cannot be re-enabled.
    /// </summary>
    /// <returns>True if dev mode was disabled, false if already disabled</returns>
    public virtual async Task<bool> DisableDevModeAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        var register = await _repository.GetRegisterAsync(registerId, cancellationToken);
        if (register == null)
        {
            throw new InvalidOperationException($"Register {registerId} not found");
        }

        if (!register.DevMode)
        {
            return false; // Already disabled
        }

        register.DevMode = false;
        register.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateRegisterAsync(register, cancellationToken);

        _logger?.LogInformation(
            "Dev mode disabled for register {RegisterId} — field-level encryption now required",
            registerId);

        // Publish event so UI updates via SignalR
        await _eventPublisher.PublishAsync(
            "register:status-changed",
            new RegisterStatusChangedEvent
            {
                RegisterId = registerId,
                OldStatus = register.Status.ToString(),
                NewStatus = register.Status.ToString(),
                ChangedAt = register.UpdatedAt
            },
            cancellationToken);

        return true;
    }

    /// <summary>
    /// Deletes a remote (synced) register stub without auth checks.
    /// Only for internal use when processing unsubscribe notifications.
    /// Will not delete System registers or registers without a SyncState (locally owned).
    /// </summary>
    public virtual async Task DeleteRemoteRegisterAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        var register = await _repository.GetRegisterAsync(registerId, cancellationToken);
        if (register == null)
        {
            _logger?.LogDebug("Register {RegisterId} not found for remote delete — already removed", registerId);
            return;
        }

        if (register.Purpose == RegisterPurpose.System)
        {
            _logger?.LogWarning("Cannot delete system register {RegisterId} via remote delete", registerId);
            return;
        }

        if (register.SyncState == null)
        {
            _logger?.LogWarning("Register {RegisterId} is locally owned (SyncState=null), refusing remote delete", registerId);
            return;
        }

        await _repository.DeleteRegisterAsync(registerId, cancellationToken);

        await _eventPublisher.PublishAsync(
            "register:deleted",
            new RegisterDeletedEvent
            {
                RegisterId = registerId,
                DeletedAt = DateTime.UtcNow
            },
            cancellationToken);

        _logger?.LogInformation("Deleted remote register stub {RegisterId}", registerId);
    }

    /// <summary>
    /// Gets registers accessible to an organisation: registers they are subscribed to plus all System registers.
    /// Implements fail-closed: returns only System registers if subscription resolution fails.
    /// </summary>
    /// <param name="orgId">Organisation ID from JWT org_id claim</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<IEnumerable<Models.Register>> GetRegistersForOrgAsync(
        Guid orgId,
        CancellationToken cancellationToken = default)
    {
        var allRegisters = (await _repository.GetRegistersAsync(cancellationToken)).ToList();

        // Always include System registers
        var systemRegisters = allRegisters.Where(r => r.Purpose == RegisterPurpose.System).ToList();

        if (_subscriptionClient is null)
        {
            _logger?.LogWarning("SubscriptionServiceClient not available — fail-closed, returning only system registers");
            return systemRegisters;
        }

        var subscribedIds = await _subscriptionClient.GetActiveRegisterIdsForOrgAsync(orgId, cancellationToken);

        _logger?.LogDebug(
            "Subscription resolution for org {OrgId}: {SubscribedCount} subscribed registers, {SystemCount} system registers",
            orgId, subscribedIds.Count, systemRegisters.Count);

        if (subscribedIds.Count == 0)
        {
            return systemRegisters;
        }

        var subscribedIdSet = new HashSet<string>(subscribedIds, StringComparer.OrdinalIgnoreCase);
        var subscribedRegisters = allRegisters
            .Where(r => r.Purpose != RegisterPurpose.System && subscribedIdSet.Contains(r.Id));

        return systemRegisters.Concat(subscribedRegisters);
    }

    /// <summary>
    /// Deletes a register. System registers cannot be deleted.
    /// Caller wallet address is verified against control record attestations.
    /// </summary>
    /// <param name="registerId">Register ID to delete</param>
    /// <param name="callerWalletAddress">Caller's wallet address from JWT (required for attestation matching)</param>
    /// <param name="controlRecordAttestations">Attestation subjects from the register's control record (required)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task DeleteRegisterAsync(
        string registerId,
        string? callerWalletAddress,
        IReadOnlyList<RegisterAttestation> controlRecordAttestations,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(controlRecordAttestations);

        var register = await _repository.GetRegisterAsync(registerId, cancellationToken);
        if (register == null)
        {
            throw new InvalidOperationException($"Register {registerId} not found");
        }

        // System registers cannot be deleted
        if (register.Purpose == RegisterPurpose.System)
        {
            throw new UnauthorizedAccessException("System registers cannot be deleted");
        }

        // Guard against corrupted control records with no attestations
        if (controlRecordAttestations.Count == 0)
        {
            throw new InvalidOperationException(
                $"Register {registerId} has no attestations in its control record — cannot verify authorization. This may indicate data corruption.");
        }

        // Verify caller wallet address is present
        if (string.IsNullOrEmpty(callerWalletAddress))
        {
            throw new UnauthorizedAccessException("Caller wallet address is required for register deletion");
        }

        // Verify caller is an Owner or Admin in the control record
        var isAuthorized = controlRecordAttestations.Any(a =>
            (a.Role == RegisterRole.Owner || a.Role == RegisterRole.Admin)
            && MatchesWalletAddress(a.Subject, callerWalletAddress));

        if (!isAuthorized)
        {
            throw new UnauthorizedAccessException("Caller is not authorized to delete this register");
        }

        await _repository.DeleteRegisterAsync(registerId, cancellationToken);

        // Publish register deleted event
        await _eventPublisher.PublishAsync(
            "register:deleted",
            new RegisterDeletedEvent
            {
                RegisterId = registerId,
                DeletedAt = DateTime.UtcNow
            },
            cancellationToken);
    }

    /// <summary>
    /// Matches a wallet address against an attestation subject DID.
    /// Attestation subjects use DID format "did:sorcha:org:{walletAddress}" or may be a plain address.
    /// </summary>
    private static bool MatchesWalletAddress(string attestationSubject, string walletAddress)
    {
        if (string.IsNullOrEmpty(attestationSubject) || string.IsNullOrEmpty(walletAddress))
            return false;

        // Direct match
        if (string.Equals(attestationSubject, walletAddress, StringComparison.OrdinalIgnoreCase))
            return true;

        // Strip DID prefix for comparison
        const string orgDidPrefix = "did:sorcha:org:";
        if (attestationSubject.StartsWith(orgDidPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var extractedAddress = attestationSubject[orgDidPrefix.Length..];
            return string.Equals(extractedAddress, walletAddress, StringComparison.OrdinalIgnoreCase);
        }

        // Handle did:sorcha:w: prefix (wallet-based DIDs used in attestations)
        const string walletDidPrefix = "did:sorcha:w:";
        if (attestationSubject.StartsWith(walletDidPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var extractedAddress = attestationSubject[walletDidPrefix.Length..];
            return string.Equals(extractedAddress, walletAddress, StringComparison.OrdinalIgnoreCase);
        }

        // Handle did:sorcha:user: prefix
        const string userDidPrefix = "did:sorcha:user:";
        if (attestationSubject.StartsWith(userDidPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var extractedAddress = attestationSubject[userDidPrefix.Length..];
            return string.Equals(extractedAddress, walletAddress, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Checks if a register exists
    /// </summary>
    public async Task<bool> RegisterExistsAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        return await _repository.IsLocalRegisterAsync(registerId, cancellationToken);
    }

    /// <summary>
    /// Gets total count of registers
    /// </summary>
    public async Task<int> GetRegisterCountAsync(
        CancellationToken cancellationToken = default)
    {
        return await _repository.CountRegistersAsync(cancellationToken);
    }
}
