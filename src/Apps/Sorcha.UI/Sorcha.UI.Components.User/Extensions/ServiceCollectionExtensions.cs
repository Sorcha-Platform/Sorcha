// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sorcha.UI.Components.User.Extensions;

/// <summary>
/// Dependency-injection extension points for the shared user-facing UI component library.
/// Host applications (Sorcha.UI.* web apps and Sorcha.Wallet.Pwa PWA) call
/// <see cref="AddSorchaUserComponents"/> from their <c>Program.cs</c> to register the
/// services the shared components consume via <c>@inject</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services consumed by components in <c>Sorcha.UI.Components.User</c>.
    /// Stub implementation — concrete service registrations are filled in by Feature 122
    /// task T037 once the migrating services (Forms, Persona, Credentials, AddressLookup)
    /// have moved into this library.
    /// </summary>
    public static IServiceCollection AddSorchaUserComponents(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Intentionally empty until Phase 5 (T037) wires the moved services.
        return services;
    }
}
