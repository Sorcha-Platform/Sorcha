// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sorcha.Testing;

/// <summary>
/// DI helpers for replacing production authentication with <see cref="TestAuthHandler"/>.
/// </summary>
public static class TestAuthenticationExtensions
{
    /// <summary>
    /// Removes every existing authentication scheme/handler/service registration
    /// and installs <see cref="TestAuthHandler"/> as the sole default scheme.
    /// </summary>
    public static IServiceCollection AddTestAuthentication(
        this IServiceCollection services,
        Action<TestAuthHandlerOptions>? configure = null)
    {
        services.RemoveAll<IAuthenticationService>();
        services.RemoveAll<IAuthenticationHandlerProvider>();
        services.RemoveAll<IAuthenticationSchemeProvider>();

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<TestAuthHandlerOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName,
                opts => configure?.Invoke(opts));

        return services;
    }
}
