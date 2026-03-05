// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for managing service-to-service authentication credentials.
/// </summary>
public interface IServicePrincipalService
{
    Task<ServicePrincipalListResult> ListAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<ServicePrincipalViewModel?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ServicePrincipalSecretViewModel> CreateAsync(CreateServicePrincipalRequest request, CancellationToken ct = default);
    Task<ServicePrincipalViewModel?> UpdateScopesAsync(Guid id, string[] scopes, CancellationToken ct = default);
    Task<bool> SuspendAsync(Guid id, CancellationToken ct = default);
    Task<bool> ReactivateAsync(Guid id, CancellationToken ct = default);
    Task<bool> RevokeAsync(Guid id, CancellationToken ct = default);
    Task<ServicePrincipalSecretViewModel> RotateSecretAsync(Guid id, CancellationToken ct = default);
}
