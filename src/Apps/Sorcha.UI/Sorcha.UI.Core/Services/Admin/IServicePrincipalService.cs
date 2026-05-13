// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for managing service principal CRUD operations.
/// </summary>
public interface IServicePrincipalService
{
    /// <summary>List service principals, optionally including inactive.</summary>
    Task<ServicePrincipalListResult> ListAsync(bool includeInactive = false, CancellationToken ct = default);

    /// <summary>Get a single service principal by ID.</summary>
    Task<ServicePrincipalViewModel?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Create a new service principal and return the one-time secret.</summary>
    Task<ServicePrincipalSecretViewModel> CreateAsync(CreateServicePrincipalRequest request, CancellationToken ct = default);

    /// <summary>Update the scopes of an existing service principal.</summary>
    Task<ServicePrincipalViewModel?> UpdateScopesAsync(Guid id, string[] scopes, CancellationToken ct = default);

    /// <summary>Suspend a service principal.</summary>
    Task<bool> SuspendAsync(Guid id, CancellationToken ct = default);

    /// <summary>Reactivate a suspended service principal.</summary>
    Task<bool> ReactivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Permanently revoke a service principal.</summary>
    Task<bool> RevokeAsync(Guid id, CancellationToken ct = default);

    /// <summary>Rotate the secret for a service principal.</summary>
    Task<ServicePrincipalSecretViewModel> RotateSecretAsync(Guid id, CancellationToken ct = default);
}
