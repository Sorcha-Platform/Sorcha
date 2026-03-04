// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Core.Services;
using Sorcha.Register.Service.Repositories;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Validates blueprint existence by querying the local <see cref="ISystemRegisterRepository"/> directly,
/// avoiding the HTTP self-reference that occurs when the Register Service calls its own REST API.
/// </summary>
public class SystemBlueprintValidator : ISystemBlueprintValidator
{
    private readonly ISystemRegisterRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemBlueprintValidator"/> class.
    /// </summary>
    /// <param name="repository">System register repository for blueprint lookups</param>
    public SystemBlueprintValidator(ISystemRegisterRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string blueprintId, CancellationToken ct = default)
    {
        var entry = await _repository.GetBlueprintByIdAsync(blueprintId, ct);
        return entry is not null && entry.IsActive;
    }
}
