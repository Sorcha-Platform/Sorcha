// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using Sorcha.Cryptography.SdJwt;

namespace Sorcha.Cryptography.Extensions;

/// <summary>
/// Extension methods for registering cryptography services in the DI container.
/// </summary>
public static class CryptographyServiceExtensions
{
    /// <summary>
    /// Adds SD-JWT VC services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSdJwtServices(this IServiceCollection services)
    {
        services.AddSingleton<ISdJwtService, SdJwtService>();
        services.AddSingleton<ISdJwtSigner, SdJwtSigner>();
        return services;
    }
}
