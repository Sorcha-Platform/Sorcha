// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Core.Services;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Extensions;

/// <summary>
/// Extension methods for registering validation engine services
/// </summary>
public static class ValidationEngineExtensions
{
    /// <summary>
    /// Adds the validation engine and related services to the service collection
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddValidationEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register configuration
        services.Configure<ValidationEngineConfiguration>(
            configuration.GetSection(ValidationEngineConfiguration.SectionName));

        // L1+L2 cache for VAL_CHAIN_PREDECESSOR_LOOKUP. Singleton so workflow-
        // batch validations (which share the docket's chain prefix) hit the
        // same cached entries; Redis L2 lets cross-validator nodes share too.
        // Mirrors the BlueprintCache architecture.
        services.Configure<ChainTransactionCacheConfiguration>(
            configuration.GetSection(ChainTransactionCacheConfiguration.SectionName));
        services.AddSingleton<IChainTransactionCache, ChainTransactionCache>();

        // Register wallet utilities for blueprint conformance validation
        services.AddSingleton<IWalletUtilities, WalletUtilities>();

        // Register governance services (scoped to match IRegisterServiceClient lifetime)
        services.AddScoped<IGovernanceRosterService, GovernanceRosterService>();
        // Feature 189 — governance decision counters. Singleton: it owns a Meter.
        services.AddSingleton<GovernanceMetrics>();

        // Feature 189 T079 — re-verifies the accountability block of every approval this node counts.
        // The SAME implementation the Register Service runs at intake: without it here, a replicated
        // approval would be counted on the organisation's signature alone, so accountability would be
        // verified once (on whichever node received the submission) rather than by every node that
        // acts on the sealed result. RightsEnforcementService fails an approval closed when this is
        // absent, so a missing registration costs quorum rather than silently skipping the check.
        services.AddScoped<Sorcha.Validator.Core.Validators.IDetachedApprovalVerifier,
            Sorcha.Validator.Core.Validators.DetachedApprovalVerifier>();

        services.AddScoped<IRightsEnforcementService, RightsEnforcementService>();

        // Register the validation engine as scoped (matches IRegisterServiceClient lifetime)
        services.AddScoped<IValidationEngine, ValidationEngine>();

        // Register the background service
        services.AddHostedService<ValidationEngineService>();

        return services;
    }
}
