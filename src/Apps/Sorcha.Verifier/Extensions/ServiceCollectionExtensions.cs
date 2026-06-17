// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Sorcha.Verifier.Services;
using Sorcha.ServiceClients.Http.Extensions;
using Sorcha.ServiceDefaults;
using Sorcha.Verifier.Engine;


namespace Sorcha.Verifier.Extensions;

/// <summary>
/// DI extensions for the Sorcha citizen reference verifier (Feature 114).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers verifier services — status list cache, presentation request
    /// builder, session store, and the VP validator.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration. Required for Feature 120
    /// hardening (<c>IssuerSignature:Required</c>); when absent, <c>RequireIssuerSignature</c>
    /// defaults to <c>true</c> per FR-019.</param>
    public static IServiceCollection AddCitizenVerifier(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IVerifierSessionStore, InMemoryVerifierSessionStore>();
        services.AddSingleton<IPresentationRequestBuilder, PresentationRequestBuilder>();

        // Feature 138 US1 — verifier trust-rejection metrics + clock-skew tolerance.
        services.AddSingleton<FederationVerifierMetrics>();
        services.AddSecurityPostureSignal();
        var clockSkew = TimeSpan.FromSeconds(
            configuration?.GetValue<int?>("Verifier:ClockSkewSeconds") ?? 60);
        var kbJwtMaxLifetime = TimeSpan.FromSeconds(
            configuration?.GetValue<int?>("Verifier:KbJwtMaxLifetimeSeconds") ?? 120);

        // StatusListCache now verifies the list signature against the sealed-state issuer key, pins iss,
        // and fails closed (Feature 138). Registered via a factory so the configured skew + metrics flow
        // in; SCOPED so it can depend on the scoped IIssuerKeyResolver (the DID-backed tier is scoped).
        services.AddHttpClient(nameof(StatusListCache));
        services.AddScoped<IStatusListCache>(sp => new StatusListCache(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(StatusListCache)),
            sp.GetRequiredService<IIssuerKeyResolver>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<StatusListCache>>(),
            sp.GetService<FederationVerifierMetrics>(),
            clockSkew));

        // Feature 120 US1 — issuer key resolution. The JWK registry tier always exists
        // (demo-mint populates it; empty in production). The DID-backed tier verifies
        // real did:sorcha:org credentials but its dependency graph
        // (IDidResolverRegistry → SorchaDidResolver → IWalletServiceClient → ServiceAuthClient)
        // needs an authenticated service principal. A standalone reference/demo verifier
        // has no principal, so wiring the DID-backed tier unconditionally crashed the
        // response callback at DI activation (issue #808): first "SorchaDidResolver has no
        // constructable ctor" (IWalletServiceClient unregistered), then once the client
        // graph was added, "ServiceAuth:ClientId not configured" from ServiceAuthClient.
        //
        // So the DID-backed tier is wired ONLY when a service principal is configured.
        // Without one, issuer signatures verify via the JWK registry alone — which is all
        // the demo (did:sorcha:org:demo, a non-resolvable fake) ever needed.
        //
        // SCOPED, not Singleton: the DID resolver registry + SorchaDidResolver +
        // IWalletServiceClient are scoped, so a singleton resolver would be a captive
        // dependency. The JWK registry stays a singleton — demo-mint writes to it and every
        // request's composite reads the same instance.
        services.AddSingleton<JwkRegistryIssuerKeyResolver>();

        var hasServicePrincipal = configuration is not null
            && !string.IsNullOrWhiteSpace(configuration["ServiceAuth:ClientId"]);

        if (hasServicePrincipal)
        {
            services.AddHttpServiceClients(configuration!);
            services.AddScoped<DidResolverBackedIssuerKeyResolver>();
            services.AddScoped<IIssuerKeyResolver>(sp => new CompositeIssuerKeyResolver(
            [
                sp.GetRequiredService<DidResolverBackedIssuerKeyResolver>(),
                sp.GetRequiredService<JwkRegistryIssuerKeyResolver>()
            ]));
        }
        else
        {
            services.AddScoped<IIssuerKeyResolver>(sp => new CompositeIssuerKeyResolver(
            [
                sp.GetRequiredService<JwkRegistryIssuerKeyResolver>()
            ]));
        }

        // Feature 120 T022 — production default is REQUIRED (FR-019 / D5).
        var requireIssuerSignature = configuration?
            .GetValue<bool?>("IssuerSignature:Required")
            ?? configuration?.GetValue<bool?>("Verifier:RequireIssuerSignature")
            ?? true;
        services.AddScoped<IVerifiablePresentationValidator>(sp =>
            new VerifiablePresentationValidator(
                sp.GetRequiredService<IStatusListCache>(),
                sp.GetRequiredService<IIssuerKeyResolver>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<VerifiablePresentationValidator>>(),
                requireIssuerSignature,
                sp.GetService<FederationVerifierMetrics>(),
                clockSkew,
                kbJwtMaxLifetime));

        services.AddSingleton<QrRenderer>();

        // Feature 155 — layer-4 register-anchor cross-check. Calls the public anchor-read endpoint on
        // the Register Service (base address from RegisterService:PublicBaseUrl) and re-verifies the
        // returned Merkle inclusion proof. No service principal needed — the endpoint is anonymous.
        services.AddHttpClient<IRegisterAnchorClient, RegisterAnchorClient>();

        return services;
    }
}
