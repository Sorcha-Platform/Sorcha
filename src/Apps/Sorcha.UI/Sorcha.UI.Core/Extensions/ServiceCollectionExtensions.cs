// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
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

        // QR Presentation Service (no HTTP needed — generates locally)
        services.AddScoped<IQrPresentationService, QrPresentationService>();

        // Admin Services
        services.AddAdminServices(baseAddress);

        // Register Services
        services.AddRegisterServices(baseAddress);

        // Blueprint Storage Services
        services.AddBlueprintStorageServices(baseAddress);

        // Chat Hub Connection (SignalR for AI-assisted blueprint design)
        services.AddChatHubServices(baseAddress);

        // Activity Log Service (043)
        services.AddScoped<IActivityLogService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<ActivityLogService>>();
            return new ActivityLogService(httpClient, logger);
        });

        // User Preferences Service (043)
        services.AddScoped<IUserPreferencesService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<UserPreferencesService>>();
            return new UserPreferencesService(httpClient, logger);
        });

        // Actions Hub Connection (SignalR for real-time action notifications)
        services.AddActionsHubServices(baseAddress);

        // Events Hub Connection (SignalR for real-time activity event notifications)
        services.AddEventsHubServices(baseAddress);

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
    public static IServiceCollection AddActionsHubServices(this IServiceCollection services, string baseAddress)
    {
        services.AddScoped<ActionsHubConnection>(sp =>
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
            var logger = sp.GetRequiredService<ILogger<ActionsHubConnection>>();
            return new ActionsHubConnection(hubBaseUrl, authService, configService, logger);
        });

        return services;
    }

    /// <summary>
    /// Registers the Events Hub connection for real-time activity event notifications.
    /// </summary>
    public static IServiceCollection AddEventsHubServices(this IServiceCollection services, string baseAddress)
    {
        services.AddScoped<EventsHubConnection>(sp =>
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
            var logger = sp.GetRequiredService<ILogger<EventsHubConnection>>();
            return new EventsHubConnection(hubBaseUrl, authService, configService, logger);
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

        // Event Admin Service (051 - authenticated)
        services.AddScoped<IEventAdminService>(sp =>
        {
            var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
            var logger = sp.GetRequiredService<ILogger<EventAdminService>>();
            return new EventAdminService(httpClient, logger);
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
        // Register Service
        services.AddScoped<IRegisterService>(sp =>
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

        // Register Hub Connection (SignalR)
        // Strip the /app/ path prefix — SignalR hubs are at the API Gateway root
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

            var logger = sp.GetRequiredService<ILogger<RegisterHubConnection>>();
            return new RegisterHubConnection(hubBaseUrl, logger);
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
