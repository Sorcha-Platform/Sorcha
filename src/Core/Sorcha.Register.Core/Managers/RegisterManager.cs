// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Register.Core.Managers;

/// <summary>
/// Manages register operations (CRUD, status, height tracking)
/// </summary>
public class RegisterManager
{
    private readonly IRegisterRepository _repository;
    private readonly IEventPublisher _eventPublisher;

    public RegisterManager(
        IRegisterRepository repository,
        IEventPublisher eventPublisher)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    /// <summary>
    /// Creates a new register
    /// </summary>
    /// <param name="name">Register name</param>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="advertise">Whether to advertise this register to peers</param>
    /// <param name="isFullReplica">Whether this is a full replica</param>
    /// <param name="registerId">Optional pre-generated register ID (used by two-phase creation flow)</param>
    /// <param name="description">Optional register description</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task<Models.Register> CreateRegisterAsync(
        string name,
        string tenantId,
        bool advertise = false,
        bool isFullReplica = true,
        string? registerId = null,
        string? description = null,
        bool devMode = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

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
            TenantId = tenantId,
            DevMode = devMode,
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
                TenantId = createdRegister.TenantId,
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
    /// Gets registers for a specific tenant
    /// </summary>
    public async Task<IEnumerable<Models.Register>> GetRegistersByTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return await _repository.QueryRegistersAsync(
            r => r.TenantId == tenantId,
            cancellationToken);
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
                TenantId = register.TenantId,
                OldStatus = oldStatus.ToString(),
                NewStatus = newStatus.ToString(),
                ChangedAt = updated.UpdatedAt
            },
            cancellationToken);

        return updated;
    }

    /// <summary>
    /// Deletes a register and all associated data
    /// </summary>
    public async Task DeleteRegisterAsync(
        string registerId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var register = await _repository.GetRegisterAsync(registerId, cancellationToken);
        if (register == null)
        {
            throw new InvalidOperationException($"Register {registerId} not found");
        }

        // Verify tenant ownership
        if (register.TenantId != tenantId)
        {
            throw new UnauthorizedAccessException($"Register {registerId} does not belong to tenant {tenantId}");
        }

        await _repository.DeleteRegisterAsync(registerId, cancellationToken);

        // Publish register deleted event
        await _eventPublisher.PublishAsync(
            "register:deleted",
            new RegisterDeletedEvent
            {
                RegisterId = registerId,
                TenantId = tenantId,
                DeletedAt = DateTime.UtcNow
            },
            cancellationToken);
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
