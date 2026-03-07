// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Register.Core.Managers;

namespace Sorcha.Register.Service.Tests.Helpers;

/// <summary>
/// Helper methods for creating test registers directly via the service layer,
/// bypassing the two-phase initiate/finalize API flow.
/// </summary>
public static class TestRegisterHelper
{
    /// <summary>
    /// Creates a test register directly via <see cref="RegisterManager"/>.
    /// </summary>
    public static async Task<Models.Register> CreateTestRegisterAsync(
        this WebApplicationFactory<Program> factory,
        string name,
        string tenantId,
        bool advertise = false,
        bool isFullReplica = true)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<RegisterManager>();
        return await manager.CreateRegisterAsync(name, tenantId, advertise, isFullReplica);
    }
}
