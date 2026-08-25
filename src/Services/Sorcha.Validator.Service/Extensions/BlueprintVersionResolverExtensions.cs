// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Extensions;

/// <summary>
/// Extension methods for registering Control Blueprint Version Resolver services.
/// </summary>
/// <remarks>
/// Feature 194 removed <c>AddBlueprintVersionResolver</c> along with its subject. That resolver
/// walked the publication-transaction chain to answer "which version", then fetched the definition
/// from the id-keyed cache — returning a version <i>number</i> attached to the <i>latest</i>
/// definition. No production code called any of its resolution methods; its one caller invalidated
/// a cache nothing read. Blueprint definitions are now resolved by content hash
/// (<c>ValidationEngine.ResolveBlueprintAsync</c>), so keeping a dormant near-miss beside the real
/// mechanism would only invite someone to resolve the wrong one.
/// <para>
/// The CONTROL blueprint version resolver below is unrelated and live — it tracks governance
/// configuration versions and returns <c>ResolvedControlBlueprintVersion</c>, not a blueprint.
/// </para>
/// </remarks>
public static class BlueprintVersionResolverExtensions
{
    /// <summary>
    /// Adds the Control Blueprint Version Resolver to the service collection.
    /// The Control Blueprint Version Resolver tracks governance configuration
    /// versions through the control transaction chain.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddControlBlueprintVersionResolver(this IServiceCollection services)
    {
        // Control blueprint version resolver (scoped - depends on scoped services)
        services.AddScoped<IControlBlueprintVersionResolver, ControlBlueprintVersionResolver>();

        return services;
    }

    /// <summary>
    /// Adds the version resolvers this service still has.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddAllVersionResolvers(this IServiceCollection services)
    {
        services.AddControlBlueprintVersionResolver();

        return services;
    }
}
