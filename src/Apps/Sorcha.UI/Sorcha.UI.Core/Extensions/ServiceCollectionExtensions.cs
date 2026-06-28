// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Authentication;
using Sorcha.UI.Core.Services.Configuration;
using Sorcha.UI.Core.Services.Encryption;
using Sorcha.UI.Core.Services.Http;
using Sorcha.UI.Core.Services.Navigation;
using Sorcha.UI.Core.Services.Participants;
using Sorcha.UI.Core.Services.Forms;
using Sorcha.UI.Core.Services.Credentials;
using Sorcha.UI.Core.Services.Admin;
using Sorcha.UI.Core.Services.Identity;
using Sorcha.UI.Core.Services.Wallet;

namespace Sorcha.UI.Core.Extensions;

/// <summary>
/// Extension methods for registering Sorcha.UI.Core services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all core services (authentication, configuration, encryption)
    /// </summary>
    public static IServiceCollection AddCoreServices(this IServiceCollection services, string baseAddress)
    {
        // Encryption
        services.AddScoped<IEncryptionProvider, BrowserEncryptionProvider>();

        // Phase 1 of the Snackbar retirement — page-scoped inline feedback for
        // actor's-own-action results. Replacement for ISnackbar; see
        // .snackbar-allowlist + scripts/check-no-snackbar.ps1 for the ratchet.
        services.AddScoped<Sorcha.UI.Core.Services.Feedback.IInlineFeedback,
                           Sorcha.UI.Core.Services.Feedback.InlineFeedback>();

        // Token cache
        services.AddScoped<ITokenCache, BrowserTokenCache>();

        // Configuration
        services.AddScoped<IConfigurationService, ConfigurationService>();

        // Register a plain HttpClient for AuthenticationService (no message handler to avoid circular dependency)
        services.AddScoped<HttpClient>(sp => new HttpClient
        {
            BaseAddress = new Uri(baseAddress)
        });

        // Authentication
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<CustomAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp =>
            sp.GetRequiredService<CustomAuthenticationStateProvider>());

        // Proactive background token refresh (timer + tab visibility)
        services.AddScoped<ITokenRefreshService, TokenRefreshService>();

        // Auth state sync service (for SSR -> WASM token handoff)
        services.AddScoped<AuthStateSync>();

        // Navigation service for authenticated redirects with return URL support
        services.AddScoped<INavigationService, NavigationService>();

        // Form renderer services
        services.AddScoped<IFormSchemaService, FormSchemaService>();
        services.AddScoped<IFormSigningService, FormSigningService>();
        // Feature 142 / US5 — presentational form-layout authoring (T049). Shared
        // by LayoutToolsPanel direct manipulation and the chat tool path (T051).
        services.AddScoped<IFormLayoutAuthoringService, FormLayoutAuthoringService>();

        // Feature 142 — designer-only quick dry-run harness (D3/T024). Drives the portable
        // Blueprint Engine in-WASM (validate→calc→route→disclose) with no register/backend.
        // Scoped: holds per-session walk-through state. The engine it composes is dependency-free.
        services.AddScoped<Sorcha.UI.Core.Services.Designer.IDryRunHarness>(
            _ => Sorcha.UI.Core.Services.Designer.DryRunHarness.CreateDefault());

        // Feature 107 — client-side portrait token resizer for `x-file.embedAs`
        // fields. The interop is scoped (holds a lazy IJSObjectReference);
        // the resizer itself is stateless quality-stepping over that interop.
        services.AddScoped<IPhotoTokenResizerInterop, BrowserPhotoTokenResizerInterop>();
        services.AddScoped<PhotoTokenResizer>();

        // Review-summary data source — pure shape over FormContext. Registered
        // Transient so any future scoped dependency (e.g. a localisation
        // service for label translation) doesn't silently become a captive
        // singleton. The class itself is stateless today; transient keeps
        // the safety margin for free.
        services.AddTransient<ReviewSummaryDataSource>();

        // Wallet preference service (server-backed with localStorage migration)
        services.AddScoped<IWalletPreferenceService>(sp =>
        {
            var userPreferences = sp.GetRequiredService<IUserPreferencesService>();
            var localStorage = sp.GetRequiredService<ILocalStorageService>();
            var logger = sp.GetRequiredService<ILogger<WalletPreferenceService>>();
            return new WalletPreferenceService(userPreferences, localStorage, logger);
        });

        // HTTP message handler for authenticated API calls (registered but not used by AuthenticationService)
        services.AddTransient<AuthenticatedHttpMessageHandler>();

        // Wallet API Service with authenticated HttpClient
        services.AddScoped<IWalletApiService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            return new WalletApiService(httpClient);
        });

        // Participant API Service with authenticated HttpClient
        services.AddScoped<IParticipantApiService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            return new ParticipantApiService(httpClient);
        });

        // Participant Publishing Service with authenticated HttpClient
        services.AddScoped<IParticipantPublishingService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            return new ParticipantPublishingService(httpClient);
        });

        // Dashboard Service
        services.AddScoped<IDashboardService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<DashboardService>>();
            return new DashboardService(httpClient, logger);
        });

        // Workflow Service
        services.AddScoped<IWorkflowService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<WorkflowService>>();
            return new WorkflowService(httpClient, logger);
        });

        // Blueprint API Service
        services.AddScoped<IBlueprintApiService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<BlueprintApiService>>();
            return new BlueprintApiService(httpClient, logger);
        });

        // Feature 142 — full-rehearsal API service (US2). Authenticated gateway HttpClient,
        // same pattern as the Blueprint API Service above. Drives the server-side rehearsal
        // walk-through (start/get/role/step/delete) against the org sandbox register.
        services.AddScoped<Sorcha.UI.Core.Services.Designer.IRehearsalApiService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<Sorcha.UI.Core.Services.Designer.RehearsalApiService>>();
            return new Sorcha.UI.Core.Services.Designer.RehearsalApiService(httpClient, logger);
        });

        // Template API Service
        services.AddScoped<ITemplateApiService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<TemplateApiService>>();
            return new TemplateApiService(httpClient, logger);
        });

        // Docket Service
        services.AddScoped<IDocketService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<DocketService>>();
            return new DocketService(httpClient, logger);
        });

        // OData Query Service
        services.AddScoped<IODataQueryService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<ODataQueryService>>();
            return new ODataQueryService(httpClient, logger);
        });

        // Schema Library API Service
        services.AddScoped<ISchemaLibraryApiService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<SchemaLibraryApiService>>();
            return new SchemaLibraryApiService(httpClient, logger);
        });

        // Credential API Service with authenticated HttpClient
        services.AddScoped<ICredentialApiService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<CredentialApiService>>();
            return new CredentialApiService(httpClient, logger);
        });

        // Issued Credential Service (admin lifecycle management)
        services.AddScoped<IIssuedCredentialService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<IssuedCredentialService>>();
            return new IssuedCredentialService(httpClient, logger);
        });

        // HAIP Offer Service (polls offer status and verifier results)
        services.AddScoped<IHaipOfferService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<HaipOfferService>>();
            return new HaipOfferService(httpClient, logger);
        });

        // Feature 103 wave 13: HAIP Local Receive Service — drives the
        // OpenID4VCI pre-authorized-code flow with the user's Sorcha wallet
        // as holder, so the resulting SD-JWT VC lands in the wallet's
        // local credential store and surfaces in the My Credentials page.
        services.AddScoped<IHaipLocalReceiveService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<HaipLocalReceiveService>>();
            return new HaipLocalReceiveService(httpClient, logger);
        });

        // QR Presentation Service (no HTTP needed — generates locally)
        services.AddScoped<IQrPresentationService, QrPresentationService>();

        // Admin Services
        services.AddAdminServices(baseAddress);

        // Graph Layout Service (stateless — singleton)
        services.AddSingleton<IGraphLayoutService, GraphLayoutService>();

        // Register Subscription Service
        services.AddScoped<IRegisterSubscriptionService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<RegisterSubscriptionService>>();
            return new RegisterSubscriptionService(httpClient, logger);
        });

        // Register Services
        services.AddRegisterServices(baseAddress);

        // Blueprint Storage Services
        services.AddBlueprintStorageServices(baseAddress);

        // Chat Hub Connection (SignalR for AI-assisted blueprint design)
        services.AddChatHubServices(baseAddress);

        // User Preferences Service (043)
        services.AddScoped<IUserPreferencesService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<UserPreferencesService>>();
            return new UserPreferencesService(httpClient, logger);
        });

        // Consumer Persona Service (092) — HTTP client + cached service wrapper.
        services.AddScoped<Sorcha.UI.Core.Services.Persona.IPersonaClient>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<Sorcha.UI.Core.Services.Persona.PersonaHttpClient>>();
            return new Sorcha.UI.Core.Services.Persona.PersonaHttpClient(httpClient, logger);
        });
        services.AddScoped<Sorcha.UI.Core.Services.Persona.IPersonaService,
            Sorcha.UI.Core.Services.Persona.PersonaService>();

        // Address Lookup Client — required by PostcodeLookupRenderer. Must use
        // AuthenticatedHttpMessageHandler so the user's JWT is attached;
        // otherwise YARP's RequireAuthenticated policy rejects the request at
        // the gateway with a 401 before it reaches tenant-service.
        services.AddScoped<Sorcha.UI.Core.Services.AddressLookup.IAddressLookupClient>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<Sorcha.UI.Core.Services.AddressLookup.AddressLookupHttpClient>>();
            return new Sorcha.UI.Core.Services.AddressLookup.AddressLookupHttpClient(httpClient, logger);
        });

        // Holder-Key Client (Feature 137) — required by HolderKeyRenderer. Auth-wrapped so the
        // citizen's consumer-tier JWT reaches the Wallet Service holder-keys endpoint (which is
        // gated by RequireConsumerAudience); a bare HttpClient would 401 at the gateway.
        services.AddScoped<Sorcha.UI.Core.Services.HolderKeys.IHolderKeyClient>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<Sorcha.UI.Core.Services.HolderKeys.HolderKeyHttpClient>>();
            return new Sorcha.UI.Core.Services.HolderKeys.HolderKeyHttpClient(httpClient, logger);
        });

        // Persona autofill resolver — pure function, singleton.
        services.AddSingleton<Sorcha.UI.Core.Services.Forms.PersonaAutofillResolver>();

        // Actions Hub Connection (SignalR for real-time action notifications)
        services.AddBlueprintHubServices(baseAddress);

        // Tenant Hub Connection (Phase 5 of Feature 118 — durable inbox events)
        services.AddTenantHubServices(baseAddress);

        // Wallet Hub Connection (Feature 114 citizen wallet + future home for
        // wallet-domain encryption / credential / transaction events per Feature 118 US2)
        services.AddWalletHubServices(baseAddress);

        // Theme Service (043 - T042)
        services.AddScoped<IThemeService>(sp =>
        {
            var userPreferences = sp.GetRequiredService<IUserPreferencesService>();
            var jsRuntime = sp.GetRequiredService<IJSRuntime>();
            var logger = sp.GetRequiredService<ILogger<ThemeService>>();
            return new ThemeService(userPreferences, jsRuntime, logger);
        });

        // Localization Service (043 - T043)
        services.AddScoped<ILocalizationService>(sp =>
        {
            var httpClient = sp.GetRequiredService<HttpClient>();
            var userPreferences = sp.GetRequiredService<IUserPreferencesService>();
            var jsRuntime = sp.GetRequiredService<IJSRuntime>();
            var logger = sp.GetRequiredService<ILogger<LocalizationService>>();
            return new LocalizationService(httpClient, userPreferences, jsRuntime, logger);
        });

        // TOTP Client Service (043 - T044)
        services.AddScoped<ITotpClientService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<TotpClientService>>();
            return new TotpClientService(httpClient, logger);
        });

        // Feature 116: Auth Methods aggregate-read client (Accounts tab US4)
        services.AddScoped<IAuthMethodsClientService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<AuthMethodsClientService>>();
            return new AuthMethodsClientService(httpClient, logger);
        });

        // Time Format Service (043 - T045)
        services.AddScoped<ITimeFormatService>(sp =>
        {
            var userPreferences = sp.GetRequiredService<IUserPreferencesService>();
            var jsRuntime = sp.GetRequiredService<IJSRuntime>();
            var logger = sp.GetRequiredService<ILogger<TimeFormatService>>();
            return new TimeFormatService(userPreferences, jsRuntime, logger);
        });

        return services;
    }

    /// <summary>
    /// Registers the Chat Hub connection for AI-assisted blueprint design.
    /// </summary>
    public static IServiceCollection AddChatHubServices(this IServiceCollection services, string baseAddress)
    {
        // Chat Hub Connection (uses active profile from ConfigurationService)
        services.AddScoped<IChatHubConnection>(sp =>
        {
            // The baseAddress includes the WASM app path (e.g., http://localhost/app/ or /app/)
            // but SignalR hubs are at the API Gateway root (e.g., http://localhost/hubs/chat or /hubs/chat)
            // We need to strip off the app path to get the root URL
            string hubBaseUrl;

            if (Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri))
            {
                // Absolute URL: extract just the origin (scheme + host + port)
                hubBaseUrl = $"{uri.Scheme}://{uri.Authority}";
            }
            else
            {
                // Relative URL (like /app/) - use empty string for same-origin relative path
                // The SignalR hub will be accessed at /hubs/chat relative to the current origin
                hubBaseUrl = "";
            }

            var options = new ChatHubOptions
            {
                BlueprintServiceUrl = hubBaseUrl
            };
            var authService = sp.GetRequiredService<IAuthenticationService>();
            var configService = sp.GetRequiredService<IConfigurationService>();
            var logger = sp.GetRequiredService<ILogger<ChatHubConnection>>();
            return new ChatHubConnection(options, authService, configService, logger);
        });

        return services;
    }

    /// <summary>
    /// Registers the Actions Hub connection for real-time action notifications.
    /// </summary>
    public static IServiceCollection AddBlueprintHubServices(this IServiceCollection services, string baseAddress)
    {
        services.AddScoped<BlueprintHubConnection>(sp =>
        {
            string hubBaseUrl;
            if (Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri))
            {
                hubBaseUrl = $"{uri.Scheme}://{uri.Authority}";
            }
            else
            {
                hubBaseUrl = "";
            }

            var authService = sp.GetRequiredService<IAuthenticationService>();
            var configService = sp.GetRequiredService<IConfigurationService>();
            var logger = sp.GetRequiredService<ILogger<BlueprintHubConnection>>();
            return new BlueprintHubConnection(hubBaseUrl, authService, configService, logger);
        });

        // Encryption operation tracker (global state across page navigation)
        services.AddScoped<IEncryptionOperationTracker, EncryptionOperationTracker>();

        return services;
    }

    /// <summary>
    /// Registers the Wallet Hub connection. Subscribes to citizen-wallet device +
    /// credential events today; future home for encryption / org-credential /
    /// transaction-tick events as the server-side migration lands.
    /// </summary>
    public static IServiceCollection AddWalletHubServices(this IServiceCollection services, string baseAddress)
    {
        services.AddScoped<WalletHubConnection>(sp =>
        {
            string hubBaseUrl;
            if (Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri))
            {
                hubBaseUrl = $"{uri.Scheme}://{uri.Authority}";
            }
            else
            {
                hubBaseUrl = "";
            }

            var authService = sp.GetRequiredService<IAuthenticationService>();
            var configService = sp.GetRequiredService<IConfigurationService>();
            var logger = sp.GetRequiredService<ILogger<WalletHubConnection>>();
            return new WalletHubConnection(hubBaseUrl, authService, configService, logger);
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="TenantHubConnection"/> for real-time inbox events
    /// (Phase 5 of Feature 118). Bell badge + activity panel + pending-action
    /// inbox subscribe to <c>OnInboxEntryAdded</c> + <c>OnInboxUnreadCountUpdated</c>.
    /// </summary>
    public static IServiceCollection AddTenantHubServices(this IServiceCollection services, string baseAddress)
    {
        services.AddScoped<TenantHubConnection>(sp =>
        {
            string hubBaseUrl;
            if (Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri))
            {
                hubBaseUrl = $"{uri.Scheme}://{uri.Authority}";
            }
            else
            {
                hubBaseUrl = "";
            }

            var authService = sp.GetRequiredService<IAuthenticationService>();
            var configService = sp.GetRequiredService<IConfigurationService>();
            var logger = sp.GetRequiredService<ILogger<TenantHubConnection>>();
            var inboxApi = sp.GetService<IInboxApiService>();
            return new TenantHubConnection(
                hubBaseUrl,
                accessTokenProvider: async () =>
                {
                    var profileName = await configService.GetActiveProfileNameAsync();
                    return await authService.GetAccessTokenAsync(profileName);
                },
                logger,
                inboxApi);
        });

        return services;
    }

    /// <summary>
    /// Registers admin dashboard services (health check, organization admin, audit).
    /// </summary>
    public static IServiceCollection AddAdminServices(this IServiceCollection services, string baseAddress)
    {
        // Configure health check options with default services
        services.Configure<HealthCheckOptions>(options =>
        {
            options.PollingIntervalMs = 30_000; // 30 seconds
            options.TimeoutMs = 5_000; // 5 seconds
            options.Services =
            [
                new ServiceEndpointConfig { ServiceName = "Blueprint Service", ServiceKey = "blueprint", HealthEndpoint = "/blueprint/health" },
                new ServiceEndpointConfig { ServiceName = "Register Service", ServiceKey = "register", HealthEndpoint = "/register/health" },
                new ServiceEndpointConfig { ServiceName = "Wallet Service", ServiceKey = "wallet", HealthEndpoint = "/wallet/health" },
                new ServiceEndpointConfig { ServiceName = "Tenant Service", ServiceKey = "tenant", HealthEndpoint = "/tenant/health" },
                new ServiceEndpointConfig { ServiceName = "Validator Service", ServiceKey = "validator", HealthEndpoint = "/validator/health" },
                new ServiceEndpointConfig { ServiceName = "Peer Service", ServiceKey = "peer", HealthEndpoint = "/peer/health" },
                new ServiceEndpointConfig { ServiceName = "API Gateway", ServiceKey = "gateway", HealthEndpoint = "/health" }
            ];
        });

        // Audit Service (used by organization admin service, so register first)
        services.AddScoped<IAuditService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<AuditService>>();
            return new AuditService(httpClient, logger);
        });

        // Health Check Service
        services.AddScoped<IHealthCheckService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var options = sp.GetRequiredService<IOptions<HealthCheckOptions>>();
            var logger = sp.GetRequiredService<ILogger<HealthCheckService>>();
            return new HealthCheckService(httpClient, options, logger);
        });

        // Organization Admin Service
        services.AddScoped<IOrganizationAdminService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var auditService = sp.GetRequiredService<IAuditService>();
            var logger = sp.GetRequiredService<ILogger<OrganizationAdminService>>();
            return new OrganizationAdminService(httpClient, auditService, logger);
        });

        // Org Provisioning Service (self-service org creation)
        services.AddScoped<IOrgProvisioningClientService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            return new OrgProvisioningClientService(httpClient);
        });

        // Validator Admin Service
        services.AddScoped<IValidatorAdminService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<ValidatorAdminService>>();
            return new ValidatorAdminService(httpClient, logger);
        });

        // Service Principal Service (049)
        services.AddScoped<IServicePrincipalService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<ServicePrincipalService>>();
            return new ServicePrincipalService(httpClient, logger);
        });

        // System Register Service (049)
        services.AddScoped<ISystemRegisterService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<SystemRegisterService>>();
            return new SystemRegisterService(httpClient, logger);
        });

        // Alert Service
        services.AddScoped<IAlertService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<AlertService>>();
            return new AlertService(httpClient, logger);
        });

        // Alert Dismissal Service (per-user localStorage-based)
        services.AddScoped<IAlertDismissalService, AlertDismissalService>();

        // Wallet Access Service (051 - authenticated)
        services.AddScoped<IWalletAccessService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<WalletAccessService>>();
            return new WalletAccessService(httpClient, logger);
        });

        // Push Notification Service (051 - authenticated)
        services.AddScoped<IPushNotificationService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<PushNotificationService>>();
            return new PushNotificationService(httpClient, logger);
        });

        // Presentation Admin Service (050 - authenticated)
        services.AddScoped<IPresentationAdminService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<PresentationAdminService>>();
            return new PresentationAdminService(httpClient, logger);
        });

        // Operation Status Service (051 - authenticated)
        services.AddScoped<IOperationStatusService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<OperationStatusService>>();
            return new OperationStatusService(httpClient, logger);
        });

        // Feature 118 / US3 — UI-side wrapper for /api/me/inbox/* endpoints.
        services.AddScoped<IInboxApiService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<InboxApiService>>();
            return new InboxApiService(httpClient, logger);
        });

        // IDP Configuration Client Service (054 - authenticated)
        services.AddScoped<IIdpConfigurationClientService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<IdpConfigurationClientService>>();
            return new IdpConfigurationClientService(httpClient, logger);
        });

        // Invitation Client Service (054 - authenticated)
        services.AddScoped<IInvitationClientService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<InvitationClientService>>();
            return new InvitationClientService(httpClient, logger);
        });

        // Register invitation client (private-register invitation envelopes — US10).
        // Uses the same bearer-attaching handler so calls reach the Tenant Service
        // as the signed-in user (endpoint is RequireAdministrator-gated).
        services.AddScoped<Sorcha.ServiceClients.Invitation.IRegisterInvitationServiceClient>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var config = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetRequiredService<ILogger<Sorcha.ServiceClients.Invitation.RegisterInvitationServiceClient>>();
            return new Sorcha.ServiceClients.Invitation.RegisterInvitationServiceClient(httpClient, config, logger);
        });

        // Domain Restriction Client Service (054 - authenticated)
        services.AddScoped<IDomainRestrictionClientService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<DomainRestrictionClientService>>();
            return new DomainRestrictionClientService(httpClient, logger);
        });

        // Platform Settings Admin Service (058 - authenticated, SystemAdmin only)
        services.AddScoped<IPlatformSettingsAdminService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<PlatformSettingsAdminService>>();
            return new PlatformSettingsAdminService(httpClient, logger);
        });

        // Platform Org Admin Service (058 - authenticated, SystemAdmin only)
        services.AddScoped<IPlatformOrgAdminService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            return new PlatformOrgAdminService(httpClient);
        });

        // Org Switcher Service (058 - authenticated)
        services.AddScoped<IOrgSwitcherService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            return new OrgSwitcherService(httpClient);
        });

        // Status List Service (050 - public endpoint, no auth needed)
        services.AddScoped<IStatusListService>(sp =>
        {
            var httpClient = new HttpClient(new HttpClientHandler()) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<StatusListService>>();
            return new StatusListService(httpClient, logger);
        });

        return services;
    }

    /// <summary>
    /// Registers register and transaction services.
    /// </summary>
    public static IServiceCollection AddRegisterServices(this IServiceCollection services, string baseAddress)
    {
        // Register Service — split into IRegisterReadService (user) +
        // IRegisterGovernanceService (admin) by Feature 123. Both interfaces
        // resolve to the same scoped RegisterService instance, matching the
        // pre-refactor behaviour where any IRegisterService injection got one
        // instance per scope.
        services.AddScoped<RegisterService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<RegisterService>>();
            return new RegisterService(httpClient, logger);
        });
        services.AddScoped<IRegisterReadService>(sp => sp.GetRequiredService<RegisterService>());
        services.AddScoped<IRegisterGovernanceService>(sp => sp.GetRequiredService<RegisterService>());

        // Feature 142 — Go-live system-info card aggregator (T035). Pure client-side fan-out over
        // the register read + governance roster reads; no new server endpoint.
        services.AddScoped<IRegisterSystemInfoService, RegisterSystemInfoService>();

        // Payload Decoder Service
        services.AddSingleton<IPayloadDecoderService, PayloadDecoderService>();

        // Blueprint Schema Service (schema overlay for Transaction Explorer)
        services.AddScoped<IBlueprintSchemaService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<BlueprintSchemaService>>();
            return new BlueprintSchemaService(httpClient, logger);
        });

        // Current User Wallet Service (encryption-aware display)
        services.AddScoped<ICurrentUserWalletService, CurrentUserWalletService>();

        // Transaction Service
        services.AddScoped<ITransactionService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var logger = sp.GetRequiredService<ILogger<TransactionService>>();
            return new TransactionService(httpClient, logger);
        });

        // Register Hub Connection (SignalR).
        // Strip the /app/ path prefix — SignalR hubs are at the API Gateway root.
        // Feature 118 / T089 — pass the JWT via ?access_token= so the server can
        // identify the connection. The hub is permissive today; the [Authorize]
        // cutover (T080) lands in a later release.
        services.AddScoped<RegisterHubConnection>(sp =>
        {
            string hubBaseUrl;
            if (Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri))
            {
                hubBaseUrl = $"{uri.Scheme}://{uri.Authority}";
            }
            else
            {
                hubBaseUrl = "";
            }

            var authService = sp.GetRequiredService<IAuthenticationService>();
            var configService = sp.GetRequiredService<IConfigurationService>();
            var logger = sp.GetRequiredService<ILogger<RegisterHubConnection>>();
            return new RegisterHubConnection(
                hubBaseUrl,
                accessTokenProvider: async () =>
                {
                    var profileName = await configService.GetActiveProfileNameAsync();
                    return await authService.GetAccessTokenAsync(profileName);
                },
                logger);
        });

        return services;
    }

    /// <summary>
    /// Registers blueprint storage and offline sync services.
    /// </summary>
    public static IServiceCollection AddBlueprintStorageServices(this IServiceCollection services, string baseAddress)
    {
        // Offline Sync Service (must be registered first as BlueprintStorageService depends on it)
        services.AddScoped<IOfflineSyncService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var localStorage = sp.GetRequiredService<ILocalStorageService>();
            var logger = sp.GetRequiredService<ILogger<OfflineSyncService>>();
            return new OfflineSyncService(httpClient, localStorage, logger);
        });

        // Blueprint Storage Service
        services.AddScoped<IBlueprintStorageService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress)
            };

            var localStorage = sp.GetRequiredService<ILocalStorageService>();
            var syncService = sp.GetRequiredService<IOfflineSyncService>();
            var logger = sp.GetRequiredService<ILogger<BlueprintStorageService>>();
            return new BlueprintStorageService(httpClient, localStorage, syncService, logger);
        });

        return services;
    }
}
