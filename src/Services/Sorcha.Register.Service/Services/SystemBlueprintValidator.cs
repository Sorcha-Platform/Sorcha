// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Core.Services;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Validates blueprint existence by querying the <see cref="SystemRegisterService"/> directly,
/// avoiding the HTTP self-reference that occurs when the Register Service calls its own REST API.
/// </summary>
public class SystemBlueprintValidator : ISystemBlueprintValidator
{
    private readonly SystemRegisterService _systemRegisterService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemBlueprintValidator"/> class.
    /// </summary>
    /// <param name="systemRegisterService">System register service for blueprint lookups</param>
    public SystemBlueprintValidator(SystemRegisterService systemRegisterService)
    {
        _systemRegisterService = systemRegisterService ?? throw new ArgumentNullException(nameof(systemRegisterService));
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string blueprintId, CancellationToken ct = default)
    {
        return await _systemRegisterService.BlueprintExistsAsync(blueprintId, ct);
    }
}
