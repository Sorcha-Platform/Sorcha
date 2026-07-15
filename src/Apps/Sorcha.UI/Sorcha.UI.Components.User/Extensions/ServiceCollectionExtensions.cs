// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sorcha.UI.Components.User.Services.Capture;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.UI.Core.Services;
using Sorcha.Verifier.Engine;

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
    /// Registers the services consumed by components in <c>Sorcha.UI.Components.User</c>,
    /// including the verification seams introduced in Feature 163 (PR B2-components).
    /// All registrations use <c>TryAdd*</c> semantics so a host can override the defaults
    /// before or after calling this method (FR-005/FR-006, R-005).
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="configuration">Application configuration; used to bind <c>VerifierPresets</c> options.</param>
    /// <param name="haipBaseUrl">
    /// Optional base URL for the HAIP verifier HTTP client. Required for WASM hosts where
    /// <see cref="System.Net.Http.IHttpClientFactory"/>-created clients have no <c>BaseAddress</c>
    /// — without it, relative URIs in <see cref="HaipVerifierClient"/> throw
    /// <see cref="InvalidOperationException"/>. Server-side Aspire hosts may omit this and rely
    /// on service-discovery configuration instead.
    /// </param>
    /// <param name="configureHaipClient">
    /// Optional hook to extend the <see cref="IHaipVerifierClient"/> typed-client pipeline with the
    /// host's authentication handler. The HAIP verify endpoints (<c>/api/v1/verifier/requests</c>)
    /// require an authenticated caller (Feature 164), so a host that overrides
    /// <see cref="IVerificationTransport"/> with <see cref="HaipVerificationTransport"/> MUST pass
    /// its bearer/auth <see cref="System.Net.Http.DelegatingHandler"/> here — otherwise the call
    /// goes out unauthenticated, 401s, and is mis-rendered as "Verification is not yet configured".
    /// </param>
    public static IServiceCollection AddSorchaUserComponents(
        this IServiceCollection services,
        IConfiguration configuration,
        string? haipBaseUrl = null,
        Action<IHttpClientBuilder>? configureHaipClient = null)
    {
        // Feature 173: anonymous client for the three social-link step-up endpoints (F168 contract).
        services.AddScoped<IAnonymousSocialLinkClientService, AnonymousSocialLinkClientService>();

        // Citizen OID4VP device-bound cnf (Phase 1, #1195): default device-key provider so DeviceKeyRenderer
        // stays DI-resolvable in the web host (where it renders its error state — the device-bound apply is
        // PWA-only for Phase 1). TryAdd so a PWA host override (PwaDeviceKeyProvider) wins.
        services.TryAddSingleton<Sorcha.UI.Core.Services.DeviceKeys.IDeviceKeyProvider,
                                 Sorcha.UI.Core.Services.DeviceKeys.NullDeviceKeyProvider>();

        // Feature 174 follow-up: getUserMedia-backed camera for portrait-capture form fields
        // (backs PortraitCaptureControl). TryAdd so a host stub (e.g. a PWA without the JS module)
        // can override it.
        services.TryAddScoped<IWebCameraService, WebCameraService>();

        // Feature 183 (US1): seeds x-claim-source form fields from the authenticated principal's
        // JWT claims at form init (SorchaFormRenderer), so a page-less read-only field like the AIAS
        // emailVerified signal is carried onto the wallet-signed submission. Pure/stateless singleton.
        services.TryAddSingleton<Sorcha.UI.Core.Services.Forms.IClaimSourceSeeder,
                                 Sorcha.UI.Core.Services.Forms.ClaimSourceSeeder>();

        // Verify seams — Feature 163 (PR B2-components).
        // TryAdd* ensures a host override registered before this call wins.
        services.TryAddSingleton<IVerificationPresetCatalogue, DefaultPresetCatalogue>();
        services.Configure<VerifierPresetsOptions>(configuration.GetSection("VerifierPresets"));

        services.TryAddSingleton<IVerificationTransport, NotConfiguredVerificationTransport>();

        // Feature 164 B3 (US1): typed HTTP client for the HAIP verifier endpoints. Hosts that override
        // IVerificationTransport with HaipVerificationTransport depend on this registration. WASM hosts
        // must supply haipBaseUrl — IHttpClientFactory-created clients have no BaseAddress by default,
        // so relative URIs would throw InvalidOperationException at runtime.
        var haipClientBuilder = services.AddHttpClient<IHaipVerifierClient, HaipVerifierClient>();
        if (!string.IsNullOrEmpty(haipBaseUrl))
            haipClientBuilder.ConfigureHttpClient(c => c.BaseAddress = new Uri(haipBaseUrl));

        // Let the host attach its authentication handler to the HAIP verifier client (Feature 164).
        // Without this the verify request is unauthenticated and the endpoint 401s.
        configureHaipClient?.Invoke(haipClientBuilder);

        // Guard so a host that registered IRegisterAnchorClient before calling AddSorchaUserComponents
        // keeps its own registration rather than being shadowed by the typed HttpClient registration.
        if (services.All(d => d.ServiceType != typeof(IRegisterAnchorClient)))
        {
            services.AddHttpClient<IRegisterAnchorClient, RegisterAnchorClient>();
        }

        return services;
    }
}
