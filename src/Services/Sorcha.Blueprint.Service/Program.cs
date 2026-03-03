// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Http;
using Polly;
using Polly.Extensions.Http;
using Scalar.AspNetCore;
using System.Buffers.Text;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Extensions;
using Sorcha.Blueprint.Service.Hubs;
using Sorcha.Blueprint.Service.JsonLd;
using Sorcha.Blueprint.Schemas.Services;
using Sorcha.Cryptography.Core;
using Sorcha.ServiceClients.Extensions;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Add structured logging with Serilog (OPS-001)
builder.AddSerilogLogging();

// Add rate limiting (SEC-002)
builder.AddRateLimiting();

// Add input validation (SEC-003)
builder.AddInputValidation();

// Add Redis output caching
builder.AddRedisOutputCache("redis");

// Add Redis client for direct access (blueprint cache population for Validator)
builder.AddRedisClient("redis");

// Add Redis distributed cache for IDistributedCache dependency
builder.AddRedisDistributedCache("redis");

// Add OpenAPI services
builder.Services.AddOpenApi();

// Add in-memory storage (later: replace with EF Core + PostgreSQL)
builder.Services.AddSingleton<IBlueprintStore, InMemoryBlueprintStore>();
builder.Services.AddSingleton<IPublishedBlueprintStore, InMemoryPublishedBlueprintStore>();

// Add Blueprint services
builder.Services.AddScoped<IBlueprintService, BlueprintService>();
builder.Services.AddScoped<IPublishService, PublishService>();

// Add Template services
builder.Services.AddSingleton<Sorcha.Storage.Abstractions.IDocumentStore<Sorcha.Blueprint.Models.BlueprintTemplate, string>>(
    new Sorcha.Storage.InMemory.InMemoryDocumentStore<Sorcha.Blueprint.Models.BlueprintTemplate, string>(t => t.Id));
builder.Services.AddSingleton<Sorcha.Blueprint.Engine.Interfaces.IJsonEEvaluator, Sorcha.Blueprint.Engine.Implementation.JsonEEvaluator>();
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService, Sorcha.Blueprint.Service.Templates.BlueprintTemplateService>();

// Add Cryptography services (required for transaction building)
builder.Services.AddScoped<Sorcha.Cryptography.Interfaces.ICryptoModule, Sorcha.Cryptography.Core.CryptoModule>();
builder.Services.AddScoped<Sorcha.Cryptography.Interfaces.IHashProvider, Sorcha.Cryptography.Core.HashProvider>();
builder.Services.AddScoped<Sorcha.Cryptography.Interfaces.ISymmetricCrypto, Sorcha.Cryptography.Core.SymmetricCrypto>();

// Add Encryption pipeline services (045-encrypted-payload-integration)
builder.Services.AddScoped<Sorcha.TransactionHandler.Encryption.IEncryptionPipelineService, Sorcha.TransactionHandler.Encryption.EncryptionPipelineService>();
builder.Services.AddSingleton<Sorcha.TransactionHandler.Encryption.IDisclosureGroupBuilder, Sorcha.TransactionHandler.Encryption.DisclosureGroupBuilder>();

// Encryption async pipeline - background processing with SignalR notifications (045 Phase 7)
builder.Services.AddSingleton(System.Threading.Channels.Channel.CreateBounded<Sorcha.Blueprint.Service.Models.EncryptionWorkItem>(
    new System.Threading.Channels.BoundedChannelOptions(100)
    {
        FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
    }));
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Interfaces.IEncryptionOperationStore,
    Sorcha.Blueprint.Service.Services.Implementation.InMemoryEncryptionOperationStore>();
builder.Services.AddHostedService<Sorcha.Blueprint.Service.Services.Implementation.EncryptionBackgroundService>();

// Add transaction confirmation options
builder.Services.Configure<Sorcha.Blueprint.Service.Models.TransactionConfirmationOptions>(
    builder.Configuration.GetSection(Sorcha.Blueprint.Service.Models.TransactionConfirmationOptions.SectionName));

// Add Execution Engine services (Sprint 5)
builder.Services.AddSingleton<Sorcha.Blueprint.Engine.Caching.JsonSchemaCache>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Interfaces.ISchemaValidator, Sorcha.Blueprint.Engine.Implementation.SchemaValidator>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Interfaces.IJsonLogicEvaluator, Sorcha.Blueprint.Engine.Implementation.JsonLogicEvaluator>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Interfaces.IDisclosureProcessor, Sorcha.Blueprint.Engine.Implementation.DisclosureProcessor>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Interfaces.IRoutingEngine, Sorcha.Blueprint.Engine.Implementation.RoutingEngine>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ICredentialVerifier, Sorcha.Blueprint.Engine.Credentials.CredentialVerifier>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ICredentialIssuer, Sorcha.Blueprint.Engine.Credentials.CredentialIssuer>();
builder.Services.AddHttpClient<Sorcha.Blueprint.Engine.Credentials.IRevocationChecker, Sorcha.Blueprint.Engine.Credentials.BitstringStatusListChecker>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Interfaces.IActionProcessor, Sorcha.Blueprint.Engine.Implementation.ActionProcessor>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Interfaces.IExecutionEngine, Sorcha.Blueprint.Engine.Implementation.ExecutionEngine>();

// Add SD-JWT service for credential verification
builder.Services.AddSingleton<Sorcha.Cryptography.SdJwt.ISdJwtService, Sorcha.Cryptography.SdJwt.SdJwtService>();

// Add JsonLogic expression cache (singleton - shared across scoped evaluators)
builder.Services.AddSingleton<Sorcha.Blueprint.Engine.Caching.JsonLogicCache>();

// Add Action service layer (Sprint 3)
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.IActionResolverService, Sorcha.Blueprint.Service.Services.Implementation.ActionResolverService>();
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.IPayloadResolverService, Sorcha.Blueprint.Service.Services.Implementation.PayloadResolverService>();
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.ITransactionBuilderService, Sorcha.Blueprint.Service.Services.Implementation.TransactionBuilderService>();

// Add consolidated service clients (Sprint 6)
builder.Services.AddServiceClients(builder.Configuration);

// Add Action storage (Sprint 4)
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.IActionStore, Sorcha.Blueprint.Service.Storage.InMemoryActionStore>();

// Add Instance storage (Sprint 6 - Orchestration)
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.IInstanceStore, Sorcha.Blueprint.Service.Storage.InMemoryInstanceStore>();

// Add Orchestration services (Sprint 6)
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.IStateReconstructionService,
    Sorcha.Blueprint.Service.Services.Implementation.StateReconstructionService>();
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.IActionExecutionService,
    Sorcha.Blueprint.Service.Services.Implementation.ActionExecutionService>();

// Add Transaction Retrieval service (045 - Phase 9: Recipient Decryption)
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.ITransactionRetrievalService,
    Sorcha.Blueprint.Service.Services.Implementation.TransactionRetrievalService>();

// Add Activity Events PostgreSQL context (043)
builder.Services.AddDbContext<Sorcha.Blueprint.Service.Data.BlueprintEventsDbContext>(options =>
{
    var eventsConnStr = builder.Configuration.GetConnectionString("EventsDb");
    if (!string.IsNullOrEmpty(eventsConnStr))
        options.UseNpgsql(eventsConnStr, npgsql => npgsql.EnableRetryOnFailure(3));
    else
        options.UseSqlite("DataSource=BlueprintEvents.db");
});
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.IEventService,
    Sorcha.Blueprint.Service.Services.Implementation.EventService>();
builder.Services.AddHostedService<Sorcha.Blueprint.Service.Services.Implementation.EventCleanupService>();

// Feature 047: Redis pub/sub → SignalR EventsHub bridge for inbound action notifications (US2)
builder.Services.AddHostedService<Sorcha.Blueprint.Service.Services.Implementation.EventsHubNotificationBridge>();

// Add SignalR (Sprint 5)
// TODO: Add Redis backplane when Microsoft.AspNetCore.SignalR.StackExchangeRedis package is added
builder.Services.AddSignalR();

// Add Notification service (Sprint 5)
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.INotificationService,
    Sorcha.Blueprint.Service.Services.Implementation.NotificationService>();

// Add AI-assisted Blueprint Chat services (Sprint 8)
builder.Services.AddChatServices(builder.Configuration);

// Add Schema Store services (Sprint 7)
builder.Services.AddSingleton<SystemSchemaLoader>();
builder.Services.AddScoped<ISchemaStore, SchemaStore>();

// Add External Schema Providers (multiple sources for schema index)
builder.Services.AddHttpClient<SchemaStoreOrgProvider>(client =>
{
    client.BaseAddress = new Uri("https://www.schemastore.org/");
    client.DefaultRequestHeaders.Add("User-Agent", "Sorcha-Blueprint-Service/1.0");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(2, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp => sp.GetRequiredService<SchemaStoreOrgProvider>());

builder.Services.AddHttpClient<SchemaOrgProvider>(client =>
{
    client.BaseAddress = new Uri("https://schema.org/");
    client.DefaultRequestHeaders.Add("User-Agent", "Sorcha-Blueprint-Service/1.0");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(2, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp => sp.GetRequiredService<SchemaOrgProvider>());

builder.Services.AddHttpClient<FhirSchemaProvider>(client =>
{
    client.BaseAddress = new Uri("https://hl7.org/");
    client.DefaultRequestHeaders.Add("User-Agent", "Sorcha-Blueprint-Service/1.0");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(2, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp => sp.GetRequiredService<FhirSchemaProvider>());

// Static providers (no HTTP client needed)
builder.Services.AddSingleton<IExternalSchemaProvider>(sp =>
    new W3cVcProvider(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<W3cVcProvider>>()));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp =>
    new UblSchemaProvider(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UblSchemaProvider>>()));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp =>
    new Iso20022Provider(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Iso20022Provider>>()));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp =>
    StaticFileSchemaProvider.CreateNiemProvider(
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StaticFileSchemaProvider>>()));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp =>
    StaticFileSchemaProvider.CreateIfcProvider(
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StaticFileSchemaProvider>>()));

// Add Schema Library services (034-schema-library)
builder.Services.AddSingleton<Sorcha.Blueprint.Schemas.Repositories.ISchemaIndexRepository>(sp =>
{
    var mongoClient = new MongoDB.Driver.MongoClient(
        builder.Configuration.GetConnectionString("mongodb") ?? "mongodb://localhost:27017");
    var database = mongoClient.GetDatabase("sorcha-blueprints");
    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Sorcha.Blueprint.Schemas.Repositories.MongoSchemaIndexRepository>>();
    return new Sorcha.Blueprint.Schemas.Repositories.MongoSchemaIndexRepository(database, logger);
});
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Interfaces.ISchemaIndexService,
    Sorcha.Blueprint.Service.Services.SchemaIndexService>();
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.SchemaIndexRefreshService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Sorcha.Blueprint.Service.Services.SchemaIndexRefreshService>());
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Interfaces.ISectorFilterService,
    Sorcha.Blueprint.Service.Services.SectorFilterService>();

// Add Status List Manager (039-verifiable-presentations)
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.IStatusListManager,
    Sorcha.Blueprint.Service.Services.StatusListManager>();

// Add JWT authentication and authorization (AUTH-002)
// JWT authentication is now configured via shared ServiceDefaults with auto-key generation
builder.AddJwtAuthentication();
builder.Services.AddBlueprintAuthorization();

// Add CORS policy (SEC-005) - production restriction handled at API Gateway (YARP)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
var logger = app.Logger;

// Configure the HTTP request pipeline
app.MapDefaultEndpoints();

// Add Serilog HTTP request logging (OPS-001)
app.UseSerilogLogging();

// Add OWASP security headers (SEC-004)
app.UseApiSecurityHeaders();

// Enable HTTPS enforcement with HSTS (SEC-001)
app.UseHttpsEnforcement();

// Enable input validation (SEC-003)
app.UseInputValidation();

// Configure OpenAPI (available in all environments for API consumers)
app.MapOpenApi();

// Configure Scalar API documentation UI (development only)
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("Blueprint Service")
            .WithTheme(ScalarTheme.Purple)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseOutputCache();

// Enable JSON-LD content negotiation
app.UseJsonLdContentNegotiation();

// Enable CORS (SEC-005)
app.UseCors();

// Add authentication and authorization middleware (AUTH-002)
app.UseAuthentication();
app.UseAuthorization();

// Enable rate limiting (SEC-002)
app.UseRateLimiting();

// Add Delegation Token Middleware (Sprint 6 - Orchestration)
app.UseMiddleware<Sorcha.Blueprint.Service.Middleware.DelegationTokenMiddleware>();

// Map SignalR hubs (Sprint 5, Sprint 8)
app.MapHub<Sorcha.Blueprint.Service.Hubs.ActionsHub>("/actionshub").RequireAuthorization();
app.MapHub<Sorcha.Blueprint.Service.Hubs.ChatHub>("/hubs/chat").RequireAuthorization();
app.MapHub<Sorcha.Blueprint.Service.Hubs.EventsHub>("/hubs/events").RequireAuthorization();

// Map Activity Events endpoints (043)
app.MapEventEndpoints();

// Map Operations endpoints (045 Phase 7 - async encryption status)
app.MapOperationsEndpoints();

// ===========================
// Blueprint CRUD Endpoints
// ===========================

var blueprintGroup = app.MapGroup("/api/blueprints")
    .WithTags("Blueprints")
    .RequireAuthorization("CanManageBlueprints");

/// <summary>
/// Get all blueprints with pagination
/// Supports JSON-LD via Accept: application/ld+json header
/// </summary>
blueprintGroup.MapGet("/", async (
    HttpContext context,
    IBlueprintService service,
    int page = 1,
    int pageSize = 20,
    string? search = null,
    string? status = null) =>
{
    // Service tokens see all blueprints; user tokens are org-scoped
    var orgId = context.IsServiceToken() ? null : context.GetOrganizationId();
    var blueprints = await service.GetAllAsync(page, pageSize, search, status, orgId);
    return Results.Ok(blueprints);
})
.WithName("GetBlueprints")
.WithSummary("Get all blueprints")
.WithDescription("Retrieve a paginated list of blueprints with optional search and status filtering. Supports JSON-LD via Accept: application/ld+json header.")
.CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(5)).Tag("blueprints"));

/// <summary>
/// Get blueprint by ID
/// Supports JSON-LD via Accept: application/ld+json header
/// </summary>
blueprintGroup.MapGet("/{id}", async (HttpContext context, string id, IBlueprintService service) =>
{
    var orgId = context.IsServiceToken() ? null : context.GetOrganizationId();
    var blueprint = await service.GetByIdAsync(id, orgId);
    if (blueprint is null) return Results.NotFound();

    // Add JSON-LD context if requested
    if (context.AcceptsJsonLd())
    {
        blueprint = JsonLdHelper.EnsureJsonLdContext(blueprint);
    }

    return Results.Ok(blueprint);
})
.WithName("GetBlueprintById")
.WithSummary("Get blueprint by ID")
.WithDescription("Retrieve a specific blueprint by its unique identifier. Supports JSON-LD via Accept: application/ld+json header.")
.CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(5)).Tag("blueprints"));

/// <summary>
/// Create new blueprint
/// Supports JSON-LD via Accept: application/ld+json header
/// </summary>
blueprintGroup.MapPost("/", async (
    HttpContext context,
    BlueprintModel blueprint,
    IBlueprintService service,
    IOutputCacheStore cache) =>
{
    var orgId = context.IsServiceToken() ? null : context.GetOrganizationId();
    var created = await service.CreateAsync(blueprint, orgId);
    await cache.EvictByTagAsync("blueprints", default);

    // Add JSON-LD context if requested
    if (context.AcceptsJsonLd())
    {
        created = JsonLdHelper.EnsureJsonLdContext(created);
    }

    return Results.Created($"/api/blueprints/{created.Id}", created);
})
.WithName("CreateBlueprint")
.WithSummary("Create new blueprint")
.WithDescription("Create a new blueprint with the provided details. Supports JSON-LD via Accept: application/ld+json header.");

/// <summary>
/// Update existing blueprint
/// </summary>
blueprintGroup.MapPut("/{id}", async (HttpContext context, string id, BlueprintModel blueprint, IBlueprintService service, IOutputCacheStore cache) =>
{
    var orgId = context.IsServiceToken() ? null : context.GetOrganizationId();
    var updated = await service.UpdateAsync(id, blueprint, orgId);
    if (updated is null) return Results.NotFound();

    await cache.EvictByTagAsync("blueprints", default);
    return Results.Ok(updated);
})
.WithName("UpdateBlueprint")
.WithSummary("Update blueprint")
.WithDescription("Update an existing blueprint with new details");

/// <summary>
/// Delete blueprint (soft delete)
/// </summary>
blueprintGroup.MapDelete("/{id}", async (HttpContext context, string id, IBlueprintService service, IOutputCacheStore cache) =>
{
    var orgId = context.IsServiceToken() ? null : context.GetOrganizationId();
    var deleted = await service.DeleteAsync(id, orgId);
    if (!deleted) return Results.NotFound();

    await cache.EvictByTagAsync("blueprints", default);
    return Results.NoContent();
})
.WithName("DeleteBlueprint")
.WithSummary("Delete blueprint")
.WithDescription("Soft delete a blueprint (can be recovered)");

// ===========================
// Blueprint Publishing Endpoints
// ===========================

/// <summary>
/// Validate blueprint (no side effects)
/// </summary>
blueprintGroup.MapPost("/{id}/validate", async (string id, IPublishService service) =>
{
    var result = await service.ValidateAsync(id);
    return Results.Ok(result);
})
.WithName("ValidateBlueprint")
.WithSummary("Validate blueprint")
.WithDescription("Validate a blueprint without publishing. Returns validation errors and warnings.")
.RequireAuthorization("CanPublishBlueprints");

/// <summary>
/// Publish blueprint, optionally to a register
/// </summary>
blueprintGroup.MapPost("/{id}/publish", async (string id, IPublishService service, IOutputCacheStore cache, HttpRequest request) =>
{
    // Read optional registerId from JSON body
    string? registerId = null;
    if (request.ContentLength > 0)
    {
        try
        {
            var body = await request.ReadFromJsonAsync<PublishRequest>();
            registerId = body?.RegisterId;
        }
        catch
        {
            // No body or invalid JSON — fall back to legacy behavior
        }
    }

    var result = await service.PublishAsync(id, registerId);

    if (!result.IsSuccess)
    {
        return Results.BadRequest(new { errors = result.Errors });
    }

    await cache.EvictByTagAsync("blueprints", default);
    await cache.EvictByTagAsync("published", default);

    // Include warnings (e.g., cycle detection) in the response
    if (result.Warnings.Length > 0)
    {
        return Results.Ok(new
        {
            blueprintId = result.PublishedBlueprint!.BlueprintId,
            version = result.PublishedBlueprint.Version,
            publishedAt = result.PublishedBlueprint.PublishedAt,
            warnings = result.Warnings
        });
    }

    return Results.Ok(result.PublishedBlueprint);
})
.WithName("PublishBlueprint")
.WithSummary("Publish blueprint")
.WithDescription("Validate and publish a blueprint. Optionally publish to a register by providing { registerId } in the request body.")
.RequireAuthorization("CanPublishBlueprints");

/// <summary>
/// Get all published versions of a blueprint
/// </summary>
blueprintGroup.MapGet("/{id}/versions", async (string id, IPublishedBlueprintStore store) =>
{
    var versions = await store.GetVersionsAsync(id);
    return Results.Ok(versions);
})
.WithName("GetBlueprintVersions")
.WithSummary("Get blueprint versions")
.WithDescription("Retrieve all published versions of a blueprint")
.CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(10)).Tag("published"));

/// <summary>
/// Get specific published version
/// </summary>
blueprintGroup.MapGet("/{id}/versions/{version}", async (string id, int version, IPublishedBlueprintStore store) =>
{
    var published = await store.GetVersionAsync(id, version);
    return published is not null ? Results.Ok(published) : Results.NotFound();
})
.WithName("GetBlueprintVersion")
.WithSummary("Get specific version")
.WithDescription("Retrieve a specific published version of a blueprint (immutable)")
.CacheOutput(policy => policy.Expire(TimeSpan.FromDays(365)).Tag("published")); // Cache permanently - immutable

// ===========================
// Schema Endpoints (Sprint 7 - Schema Store)
// ===========================

// Map schema store endpoints (GET /api/v1/schemas/system, GET /api/v1/schemas/{identifier}, etc.)
app.MapSchemaEndpoints();

// Map schema library endpoints (034-schema-library)
app.MapSchemaLibraryEndpoints();

// Map credential lifecycle endpoints (POST /api/v1/credentials/{credentialId}/revoke)
app.MapCredentialEndpoints();

// Map status list endpoints (GET public, POST/PUT internal)
app.MapStatusListEndpoints();

// ===========================
// Template Endpoints
// ===========================

var templateGroup = app.MapGroup("/api/templates")
    .WithTags("Templates")
    .RequireAuthorization();

/// <summary>
/// Get all published templates
/// </summary>
templateGroup.MapGet("/", async (Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service, string? category = null) =>
{
    var templates = category != null
        ? await service.GetTemplatesByCategoryAsync(category)
        : await service.GetPublishedTemplatesAsync();

    return Results.Ok(templates);
})
.WithName("GetTemplates")
.WithSummary("Get all published templates")
.WithDescription("Retrieve all published blueprint templates, optionally filtered by category")
.CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(10)).Tag("templates"));

/// <summary>
/// Get template by ID
/// </summary>
templateGroup.MapGet("/{id}", async (string id, Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service) =>
{
    var template = await service.GetTemplateAsync(id);
    return template is not null ? Results.Ok(template) : Results.NotFound();
})
.WithName("GetTemplateById")
.WithSummary("Get template by ID")
.WithDescription("Retrieve a specific blueprint template by its unique identifier")
.CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(10)).Tag("templates"));

/// <summary>
/// Create or update a template
/// </summary>
templateGroup.MapPost("/", async (
    Sorcha.Blueprint.Models.BlueprintTemplate template,
    Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service,
    IOutputCacheStore cache) =>
{
    var saved = await service.SaveTemplateAsync(template);
    await cache.EvictByTagAsync("templates", default);

    return Results.Ok(saved);
})
.WithName("SaveTemplate")
.WithSummary("Create or update template")
.WithDescription("Create a new template or update an existing one");

/// <summary>
/// Delete a template
/// </summary>
templateGroup.MapDelete("/{id}", async (
    string id,
    Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service,
    IOutputCacheStore cache) =>
{
    var deleted = await service.DeleteTemplateAsync(id);
    if (!deleted) return Results.NotFound();

    await cache.EvictByTagAsync("templates", default);
    return Results.NoContent();
})
.WithName("DeleteTemplate")
.WithSummary("Delete template")
.WithDescription("Delete a blueprint template");

/// <summary>
/// Evaluate a template with parameters to generate a blueprint
/// </summary>
templateGroup.MapPost("/evaluate", async (
    Sorcha.Blueprint.Models.TemplateEvaluationRequest request,
    Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service) =>
{
    var result = await service.EvaluateTemplateAsync(request);

    if (!result.Success)
    {
        return Results.BadRequest(result);
    }

    return Results.Ok(result);
})
.WithName("EvaluateTemplate")
.WithSummary("Evaluate template")
.WithDescription("Evaluate a blueprint template with specific parameters to generate a blueprint");

/// <summary>
/// Validate template parameters
/// </summary>
templateGroup.MapPost("/{id}/validate", async (
    string id,
    Dictionary<string, object> parameters,
    Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service) =>
{
    var result = await service.ValidateParametersAsync(id, parameters);

    return Results.Ok(new
    {
        valid = result.IsValid,
        errors = result.Errors,
        warnings = result.Warnings
    });
})
.WithName("ValidateTemplateParameters")
.WithSummary("Validate parameters")
.WithDescription("Validate parameters against a template's parameter schema");

/// <summary>
/// Evaluate a template example
/// </summary>
templateGroup.MapGet("/{id}/examples/{exampleName}", async (
    string id,
    string exampleName,
    Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service) =>
{
    var result = await service.EvaluateExampleAsync(id, exampleName);

    if (!result.Success)
    {
        return Results.BadRequest(result);
    }

    return Results.Ok(result);
})
.WithName("EvaluateTemplateExample")
.WithSummary("Evaluate template example")
.WithDescription("Evaluate a predefined example from the template");

templateGroup.MapPost("/{id}/increment-usage", async (
    string id,
    Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service,
    IOutputCacheStore cache) =>
{
    await service.IncrementUsageAsync(id);
    await cache.EvictByTagAsync("templates", default);
    return Results.Ok();
})
.WithName("IncrementTemplateUsage")
.WithSummary("Increment template usage count")
.WithDescription("Increments the usage counter for a template after it has been used to create a blueprint");

// ===========================
// Action API Endpoints (Sprint 4)
// ===========================

var actionsGroup = app.MapGroup("/api/actions")
    .WithTags("Actions")
    .RequireAuthorization("CanExecuteBlueprints");

/// <summary>
/// Get available blueprints for a wallet/register combination
/// </summary>
actionsGroup.MapGet("/{wallet}/{register}/blueprints", async (
    string wallet,
    string register,
    IPublishedBlueprintStore publishedStore,
    IBlueprintStore blueprintStore) =>
{
    // Get all published blueprints
    var blueprints = await blueprintStore.GetAllAsync();
    var availableBlueprints = new List<Sorcha.Blueprint.Service.Models.Responses.BlueprintInfo>();

    foreach (var blueprint in blueprints)
    {
        var versions = await publishedStore.GetVersionsAsync(blueprint.Id);
        var latestVersion = versions.OrderByDescending(v => v.Version).FirstOrDefault();

        if (latestVersion != null)
        {
            // For MVP, all actions are available
            // In future, apply routing rules to filter actions based on workflow state
            var availableActions = blueprint.Actions
                .Select(a => new Sorcha.Blueprint.Service.Models.Responses.ActionInfo
                {
                    ActionId = a.Id.ToString(),
                    Title = a.Title,
                    Description = a.Description,
                    IsAvailable = true, // TODO: Apply routing rules
                    DataSchema = a.DataSchemas?.FirstOrDefault() is { } schema
                        && schema.RootElement.TryGetProperty("$id", out var schemaId)
                        ? schemaId.GetString() : null
                })
                .ToList();

            availableBlueprints.Add(new Sorcha.Blueprint.Service.Models.Responses.BlueprintInfo
            {
                BlueprintId = blueprint.Id,
                Title = blueprint.Title,
                Description = blueprint.Description,
                Version = latestVersion.Version,
                AvailableActions = availableActions
            });
        }
    }

    var response = new Sorcha.Blueprint.Service.Models.Responses.AvailableBlueprintsResponse
    {
        WalletAddress = wallet,
        RegisterAddress = register,
        Blueprints = availableBlueprints
    };

    return Results.Ok(response);
})
.WithName("GetAvailableBlueprints")
.WithSummary("Get available blueprints")
.WithDescription("Retrieve blueprints and actions available to a specific wallet/register combination")
.CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(5)).Tag("blueprints"));

/// <summary>
/// Get actions for a wallet/register (paginated)
/// </summary>
actionsGroup.MapGet("/{wallet}/{register}", async (
    string wallet,
    string register,
    Sorcha.Blueprint.Service.Storage.IActionStore actionStore,
    int page = 1,
    int pageSize = 20) =>
{
    var skip = (page - 1) * pageSize;
    var actions = await actionStore.GetActionsAsync(wallet, register, skip, pageSize);
    var totalCount = await actionStore.GetActionCountAsync(wallet, register);

    var result = new PagedResult<Sorcha.Blueprint.Service.Models.Responses.ActionDetailsResponse>
    {
        Items = actions,
        Page = page,
        PageSize = pageSize,
        TotalCount = totalCount,
        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
    };

    return Results.Ok(result);
})
.WithName("GetActions")
.WithSummary("Get actions for wallet/register")
.WithDescription("Retrieve paginated list of actions for a specific wallet and register");

/// <summary>
/// Get a specific action by transaction hash
/// </summary>
actionsGroup.MapGet("/{wallet}/{register}/{tx}", async (
    string wallet,
    string register,
    string tx,
    Sorcha.Blueprint.Service.Storage.IActionStore actionStore) =>
{
    var action = await actionStore.GetActionAsync(tx);

    if (action == null)
    {
        return Results.NotFound(new { error = "Action not found" });
    }

    // Verify the action belongs to this wallet/register
    if (action.SenderWallet != wallet || action.RegisterAddress != register)
    {
        return Results.NotFound(new { error = "Action not found" });
    }

    return Results.Ok(action);
})
.WithName("GetActionDetails")
.WithSummary("Get action details")
.WithDescription("Retrieve detailed information about a specific action transaction");

/// <summary>
/// Submit a new action
/// </summary>
actionsGroup.MapPost("/", async (
    Sorcha.Blueprint.Service.Models.Requests.ActionSubmissionRequest request,
    HttpContext context,
    Sorcha.Blueprint.Service.Services.Interfaces.IActionResolverService actionResolver,
    Sorcha.Blueprint.Service.Services.Interfaces.IPayloadResolverService payloadResolver,
    Sorcha.Blueprint.Service.Services.Interfaces.ITransactionBuilderService txBuilder,
    Sorcha.ServiceClients.Wallet.IWalletServiceClient walletClient,
    Sorcha.ServiceClients.Register.IRegisterServiceClient registerClient,
    Sorcha.Blueprint.Service.Storage.IActionStore actionStore,
    Sorcha.Cryptography.Interfaces.IHashProvider hashProvider,
    Sorcha.Blueprint.Engine.Interfaces.IDisclosureProcessor disclosureProcessor) =>
{
    try
    {
        // 0. Replay protection — check idempotency key
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            // Auto-generate from request content to prevent duplicate submissions
            var keySource = $"{request.BlueprintId}:{request.ActionId}:{request.InstanceId}:{request.SenderWallet}:{request.RegisterAddress}";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var keyHash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(keySource));
            idempotencyKey = BitConverter.ToString(keyHash).Replace("-", "").ToLowerInvariant();
        }

        var existingTxHash = await actionStore.GetByIdempotencyKeyAsync(idempotencyKey);
        if (existingTxHash != null)
        {
            return Results.Conflict(new { error = "Duplicate submission", transactionHash = existingTxHash });
        }

        // 1. Get blueprint
        var blueprint = await actionResolver.GetBlueprintAsync(request.BlueprintId);
        if (blueprint == null)
        {
            return Results.BadRequest(new { error = "Blueprint not found" });
        }

        // 2. Get action definition
        var actionDef = actionResolver.GetActionDefinition(blueprint, request.ActionId);
        if (actionDef == null)
        {
            return Results.BadRequest(new { error = "Action not found in blueprint" });
        }

        // 3. Process disclosure rules to determine which data each participant receives
        var disclosureResults = new Dictionary<string, object>();
        var participantWalletMap = new Dictionary<string, string>();

        var actionDisclosures = actionDef.Disclosures?.ToList();
        if (actionDisclosures != null && actionDisclosures.Count > 0)
        {
            // Apply disclosure rules: each participant gets only their authorized fields
            var engineDisclosures = disclosureProcessor.CreateDisclosures(
                request.PayloadData,
                actionDisclosures);

            foreach (var disclosure in engineDisclosures)
            {
                // Resolve participant ID to wallet address from blueprint participants
                var participant = blueprint.Participants.FirstOrDefault(p => p.Id == disclosure.ParticipantId);
                var walletAddress = participant?.WalletAddress ?? disclosure.ParticipantId;

                disclosureResults[walletAddress] = disclosure.DisclosedData;
                participantWalletMap[disclosure.ParticipantId] = walletAddress;
            }
        }

        // Ensure sender always receives the full payload
        if (!disclosureResults.ContainsKey(request.SenderWallet))
        {
            disclosureResults[request.SenderWallet] = request.PayloadData;
            participantWalletMap[request.SenderWallet] = request.SenderWallet;
        }

        // 4. Create encrypted payloads using Wallet Service
        var encryptedPayloads = await payloadResolver.CreateEncryptedPayloadsAsync(
            disclosureResults,
            participantWalletMap,
            request.SenderWallet);

        // 5. Compute PrevTxId server-side instead of trusting client.
        // For Action 0 (no prior TX): PrevTxId = blueprint's publish TX on this register.
        // For subsequent actions: use client-provided PrevTxId (from prior step).
        var previousTxId = request.PreviousTransactionHash;
        if (string.IsNullOrEmpty(previousTxId))
        {
            // Action 0: chain from the blueprint publish transaction
            previousTxId = Sorcha.Blueprint.Service.Services.Implementation.ActionExecutionService
                .ComputeBlueprintPublishTxId(request.RegisterAddress, request.BlueprintId);
            logger.LogInformation(
                "Action 0 for blueprint {BlueprintId}: PrevTxId set to blueprint publish TX {TxId}",
                request.BlueprintId, previousTxId);
        }

        var transaction = await txBuilder.BuildActionTransactionAsync(
            request.BlueprintId,
            request.ActionId,
            request.InstanceId,
            previousTxId,
            encryptedPayloads,
            request.SenderWallet,
            request.RegisterAddress);

        // 6. Calculate transaction hash
        var txHashBytes = System.Text.Encoding.UTF8.GetBytes(transaction.TxId ?? Guid.NewGuid().ToString());
        using var txHashStream = new System.IO.MemoryStream(txHashBytes);
        var txHash = await hashProvider.ComputeHashAsync(txHashStream);
        var txHashHex = BitConverter.ToString(txHash).Replace("-", "").ToLowerInvariant();

        // 7. Sign the transaction with Wallet Service
        var transactionBytes = System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(transaction));
        var signResult = await walletClient.SignTransactionAsync(
            request.SenderWallet,
            transactionBytes,
            derivationPath: null); // Use wallet's default signing key

        // 7b. Verify the signature we just created
        var walletInfo = await walletClient.GetWalletAsync(request.SenderWallet);
        if (walletInfo == null)
        {
            return Results.Problem("Sender wallet not found", statusCode: 400);
        }

        var isSignatureValid = await walletClient.VerifySignatureAsync(
            walletInfo.PublicKey,
            Base64Url.EncodeToString(transactionBytes),
            Base64Url.EncodeToString(signResult.Signature),
            walletInfo.Algorithm);

        if (!isSignatureValid)
        {
            logger.LogError("Signature verification failed for wallet {Wallet}", request.SenderWallet);
            return Results.Problem("Transaction signature verification failed", statusCode: 400);
        }

        // 8. Convert to Register TransactionModel and submit to Register Service
        var registerTransaction = new Sorcha.Register.Models.TransactionModel
        {
            TxId = txHashHex,
            RegisterId = request.RegisterAddress,
            SenderWallet = request.SenderWallet,
            TimeStamp = DateTime.UtcNow,
            PrevTxId = previousTxId ?? string.Empty,
            MetaData = transaction.Metadata != null ?
                System.Text.Json.JsonSerializer.Deserialize<Sorcha.Register.Models.TransactionMetaData>(transaction.Metadata) : null,
            Payloads = encryptedPayloads.Select(kvp => new Sorcha.Register.Models.PayloadModel
            {
                Data = Base64Url.EncodeToString(kvp.Value),
                WalletAccess = new[] { kvp.Key },
                ContentEncoding = "base64url"
            }).ToArray(),
            PayloadCount = (ulong)encryptedPayloads.Count,
            Signature = Base64Url.EncodeToString(signResult.Signature)
        };

        // Submit to Register Service
        await registerClient.SubmitTransactionAsync(request.RegisterAddress, registerTransaction);

        // 9. Build file transactions if any
        List<string>? fileHashes = null;
        if (request.Files != null && request.Files.Any())
        {
            var fileAttachments = request.Files.Select(f => new Sorcha.Blueprint.Service.Services.Interfaces.FileAttachment(
                f.FileName,
                f.ContentType,
                Sorcha.TransactionHandler.Services.ContentEncodings.DecodeBase64Auto(f.ContentBase64)
            )).ToList();

            var fileTxs = await txBuilder.BuildFileTransactionsAsync(
                fileAttachments,
                txHashHex,
                request.SenderWallet,
                request.RegisterAddress);

            fileHashes = new List<string>();
            var fileMetadataList = new List<Sorcha.Blueprint.Service.Models.Responses.FileMetadata>();

            for (int i = 0; i < fileTxs.Count; i++)
            {
                var fileTx = fileTxs[i];
                var fileHashBytes = System.Text.Encoding.UTF8.GetBytes(fileTx.TxId ?? Guid.NewGuid().ToString());
                using var fileHashStream = new System.IO.MemoryStream(fileHashBytes);
                var fileHash = await hashProvider.ComputeHashAsync(fileHashStream);
                var fileHashHex = BitConverter.ToString(fileHash).Replace("-", "").ToLowerInvariant();
                fileHashes.Add(fileHashHex);

                // Store file content and metadata
                var fileAttachment = fileAttachments[i];
                await actionStore.StoreFileContentAsync(fileHashHex, fileAttachment.Content);

                var fileMeta = new Sorcha.Blueprint.Service.Models.Responses.FileMetadata
                {
                    FileId = fileHashHex,
                    FileName = fileAttachment.FileName,
                    ContentType = fileAttachment.ContentType,
                    Size = fileAttachment.Content.Length
                };

                await actionStore.StoreFileMetadataAsync(txHashHex, fileHashHex, fileMeta);
                fileMetadataList.Add(fileMeta);
            }
        }

        // 10. Generate instance ID if needed
        var instanceId = request.InstanceId ?? Guid.NewGuid().ToString();

        // 11. Store action locally
        var actionDetails = new Sorcha.Blueprint.Service.Models.Responses.ActionDetailsResponse
        {
            TransactionHash = txHashHex,
            BlueprintId = request.BlueprintId,
            ActionId = request.ActionId,
            InstanceId = instanceId,
            SenderWallet = request.SenderWallet,
            RegisterAddress = request.RegisterAddress,
            PayloadData = request.PayloadData,
            Timestamp = DateTimeOffset.UtcNow,
            PreviousTransactionHash = request.PreviousTransactionHash
        };

        await actionStore.StoreActionAsync(actionDetails);

        // Store idempotency key (24-hour TTL)
        await actionStore.StoreIdempotencyKeyAsync(idempotencyKey, txHashHex, TimeSpan.FromHours(24));

        // 12. Return response
        var response = new Sorcha.Blueprint.Service.Models.Responses.ActionSubmissionResponse
        {
            TransactionId = txHashHex,
            InstanceId = instanceId,
            SerializedTransaction = System.Text.Json.JsonSerializer.Serialize(transaction),
            FileTransactionHashes = fileHashes,
            Timestamp = DateTimeOffset.UtcNow
        };

        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("SubmitAction")
.WithSummary("Submit an action")
.WithDescription("Submit a new action for execution in a blueprint workflow");

/// <summary>
/// Reject a pending action
/// </summary>
actionsGroup.MapPost("/reject", async (
    Sorcha.Blueprint.Service.Models.Requests.ActionRejectionRequest request,
    Sorcha.Blueprint.Service.Services.Interfaces.ITransactionBuilderService txBuilder,
    Sorcha.ServiceClients.Register.IRegisterServiceClient registerClient,
    Sorcha.Blueprint.Service.Storage.IActionStore actionStore,
    Sorcha.Cryptography.Interfaces.IHashProvider hashProvider) =>
{
    try
    {
        // Validate required fields
        if (string.IsNullOrEmpty(request.TransactionHash))
        {
            return Results.BadRequest(new { error = "TransactionHash is required" });
        }
        if (string.IsNullOrEmpty(request.SenderWallet))
        {
            return Results.BadRequest(new { error = "SenderWallet is required" });
        }
        if (string.IsNullOrEmpty(request.RegisterAddress))
        {
            return Results.BadRequest(new { error = "RegisterAddress is required" });
        }

        // 1. Verify original transaction exists
        var originalAction = await actionStore.GetActionAsync(request.TransactionHash);
        if (originalAction == null)
        {
            return Results.NotFound(new { error = "Original transaction not found" });
        }

        // 2. Build rejection transaction
        var rejectionTx = await txBuilder.BuildRejectionTransactionAsync(
            request.TransactionHash,
            request.Reason,
            request.SenderWallet,
            request.RegisterAddress);

        // 3. Calculate rejection transaction hash
        var rejectionHashBytes = System.Text.Encoding.UTF8.GetBytes(rejectionTx.TxId ?? Guid.NewGuid().ToString());
        using var rejectionHashStream = new System.IO.MemoryStream(rejectionHashBytes);
        var rejectionHash = await hashProvider.ComputeHashAsync(rejectionHashStream);
        var rejectionHashHex = BitConverter.ToString(rejectionHash).Replace("-", "").ToLowerInvariant();

        // 4. Convert to Register TransactionModel and submit to Register Service
        var registerRejection = new Sorcha.Register.Models.TransactionModel
        {
            TxId = rejectionHashHex,
            RegisterId = request.RegisterAddress,
            SenderWallet = request.SenderWallet,
            TimeStamp = DateTime.UtcNow,
            PrevTxId = request.TransactionHash,
            MetaData = rejectionTx.Metadata != null ?
                System.Text.Json.JsonSerializer.Deserialize<Sorcha.Register.Models.TransactionMetaData>(rejectionTx.Metadata) : null,
            Payloads = Array.Empty<Sorcha.Register.Models.PayloadModel>()
        };

        // Submit rejection to Register Service
        await registerClient.SubmitTransactionAsync(request.RegisterAddress, registerRejection);

        // 5. Store rejection action locally
        var rejectionDetails = new Sorcha.Blueprint.Service.Models.Responses.ActionDetailsResponse
        {
            TransactionHash = rejectionHashHex,
            BlueprintId = originalAction.BlueprintId,
            ActionId = "rejection",
            InstanceId = originalAction.InstanceId,
            SenderWallet = request.SenderWallet,
            RegisterAddress = request.RegisterAddress,
            PayloadData = new Dictionary<string, object>
            {
                ["rejectedTransactionHash"] = request.TransactionHash,
                ["reason"] = request.Reason
            },
            Timestamp = DateTimeOffset.UtcNow,
            PreviousTransactionHash = request.TransactionHash
        };

        await actionStore.StoreActionAsync(rejectionDetails);

        // 6. Return response
        var response = new Sorcha.Blueprint.Service.Models.Responses.ActionSubmissionResponse
        {
            TransactionId = rejectionHashHex,
            InstanceId = rejectionDetails.InstanceId,
            SerializedTransaction = System.Text.Json.JsonSerializer.Serialize(rejectionTx),
            Timestamp = DateTimeOffset.UtcNow
        };

        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("RejectAction")
.WithSummary("Reject an action")
.WithDescription("Reject a pending action with a reason");

/// <summary>
/// Get file content by file ID
/// </summary>
app.MapGet("/api/files/{wallet}/{register}/{tx}/{fileId}", async (
    string wallet,
    string register,
    string tx,
    string fileId,
    Sorcha.Blueprint.Service.Storage.IActionStore actionStore) =>
{
    // 1. Verify action exists and belongs to wallet/register
    var action = await actionStore.GetActionAsync(tx);
    if (action == null || action.SenderWallet != wallet || action.RegisterAddress != register)
    {
        return Results.NotFound(new { error = "Action not found" });
    }

    // 2. Get file metadata
    var metadata = await actionStore.GetFileMetadataAsync(tx, fileId);
    if (metadata == null)
    {
        return Results.NotFound(new { error = "File not found" });
    }

    // 3. Get file content
    var content = await actionStore.GetFileContentAsync(fileId);
    if (content == null)
    {
        return Results.NotFound(new { error = "File content not found" });
    }

    // 4. Return file
    return Results.File(content, metadata.ContentType, metadata.FileName);
})
.WithName("GetFile")
.WithSummary("Get file attachment")
.WithDescription("Retrieve a file attachment from an action transaction")
.WithTags("Actions")
.RequireAuthorization("CanExecuteBlueprints");

// ===========================
// Execution Helper Endpoints (Sprint 5)
// ===========================

var executionGroup = app.MapGroup("/api/execution")
    .WithTags("Execution")
    .RequireAuthorization("CanExecuteBlueprints");

/// <summary>
/// Validate action data against schema (helper endpoint)
/// </summary>
executionGroup.MapPost("/validate", async (
    ValidateRequest request,
    IBlueprintStore blueprintStore,
    Sorcha.Blueprint.Engine.Interfaces.IExecutionEngine executionEngine) =>
{
    try
    {
        // Get blueprint
        var blueprint = await blueprintStore.GetAsync(request.BlueprintId);
        if (blueprint == null)
        {
            return Results.BadRequest(new { error = "Blueprint not found" });
        }

        // Get action (parse ActionId string to int)
        if (!int.TryParse(request.ActionId, out var actionIdInt))
        {
            return Results.BadRequest(new { error = "Invalid action ID format" });
        }

        var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionIdInt);
        if (action == null)
        {
            return Results.BadRequest(new { error = "Action not found in blueprint" });
        }

        // Validate
        var result = await executionEngine.ValidateAsync(request.Data, action);

        return Results.Ok(new
        {
            isValid = result.IsValid,
            errors = result.Errors.Select(e => new
            {
                path = e.InstanceLocation,
                message = e.Message,
                schemaLocation = e.SchemaLocation,
                keyword = e.Keyword
            })
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("ValidateAction")
.WithSummary("Validate action data")
.WithDescription("Validate action data against the action's JSON Schema without executing the full workflow");

/// <summary>
/// Apply calculations to action data (helper endpoint)
/// </summary>
executionGroup.MapPost("/calculate", async (
    CalculateRequest request,
    IBlueprintStore blueprintStore,
    Sorcha.Blueprint.Engine.Interfaces.IExecutionEngine executionEngine) =>
{
    try
    {
        // Get blueprint
        var blueprint = await blueprintStore.GetAsync(request.BlueprintId);
        if (blueprint == null)
        {
            return Results.BadRequest(new { error = "Blueprint not found" });
        }

        // Get action (parse ActionId string to int)
        if (!int.TryParse(request.ActionId, out var actionIdInt))
        {
            return Results.BadRequest(new { error = "Invalid action ID format" });
        }

        var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionIdInt);
        if (action == null)
        {
            return Results.BadRequest(new { error = "Action not found in blueprint" });
        }

        // Apply calculations
        var result = await executionEngine.ApplyCalculationsAsync(request.Data, action);

        return Results.Ok(new
        {
            processedData = result,
            calculatedFields = result.Keys.Except(request.Data.Keys).ToList()
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("CalculateAction")
.WithSummary("Apply calculations")
.WithDescription("Apply JSON Logic calculations to action data without executing the full workflow");

/// <summary>
/// Determine routing for action (helper endpoint)
/// </summary>
executionGroup.MapPost("/route", async (
    RouteRequest request,
    IBlueprintStore blueprintStore,
    Sorcha.Blueprint.Engine.Interfaces.IExecutionEngine executionEngine) =>
{
    try
    {
        // Get blueprint
        var blueprint = await blueprintStore.GetAsync(request.BlueprintId);
        if (blueprint == null)
        {
            return Results.BadRequest(new { error = "Blueprint not found" });
        }

        // Get action (parse ActionId string to int)
        if (!int.TryParse(request.ActionId, out var actionIdInt))
        {
            return Results.BadRequest(new { error = "Invalid action ID format" });
        }

        var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionIdInt);
        if (action == null)
        {
            return Results.BadRequest(new { error = "Action not found in blueprint" });
        }

        // Determine routing
        var result = await executionEngine.DetermineRoutingAsync(blueprint, action, request.Data);

        return Results.Ok(new
        {
            nextActionId = result.NextActionId,
            nextParticipantId = result.NextParticipantId,
            isWorkflowComplete = result.IsWorkflowComplete,
            rejectedToParticipantId = result.RejectedToParticipantId,
            matchedCondition = result.MatchedCondition
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("DetermineRouting")
.WithSummary("Determine routing")
.WithDescription("Determine the next action and participant based on routing conditions");

/// <summary>
/// Apply disclosure rules (helper endpoint)
/// </summary>
executionGroup.MapPost("/disclose", async (
    DiscloseRequest request,
    IBlueprintStore blueprintStore,
    Sorcha.Blueprint.Engine.Interfaces.IExecutionEngine executionEngine) =>
{
    try
    {
        // Get blueprint
        var blueprint = await blueprintStore.GetAsync(request.BlueprintId);
        if (blueprint == null)
        {
            return Results.BadRequest(new { error = "Blueprint not found" });
        }

        // Get action (parse ActionId string to int)
        if (!int.TryParse(request.ActionId, out var actionIdInt))
        {
            return Results.BadRequest(new { error = "Invalid action ID format" });
        }

        var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionIdInt);
        if (action == null)
        {
            return Results.BadRequest(new { error = "Action not found in blueprint" });
        }

        // Apply disclosures
        var result = executionEngine.ApplyDisclosures(request.Data, action);

        return Results.Ok(new
        {
            disclosures = result.Select(d => new
            {
                participantId = d.ParticipantId,
                disclosedData = d.DisclosedData,
                disclosureId = d.DisclosureId,
                fieldCount = d.DisclosedData.Count
            })
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("ApplyDisclosure")
.WithSummary("Apply disclosure rules")
.WithDescription("Apply selective disclosure rules to see what data each participant will receive");

// ===========================
// Notification Endpoint (Sprint 5)
// ===========================

var notificationGroup = app.MapGroup("/api/notifications")
    .WithTags("Notifications")
    .RequireAuthorization("RequireService");

/// <summary>
/// Internal endpoint for Register Service to notify of transaction confirmations
/// </summary>
notificationGroup.MapPost("/transaction-confirmed", async (
    TransactionConfirmationNotification notification,
    Sorcha.Blueprint.Service.Services.Interfaces.INotificationService notificationService) =>
{
    try
    {
        // Broadcast notification via SignalR
        var actionNotification = new Sorcha.Blueprint.Service.Hubs.ActionNotification
        {
            TransactionHash = notification.TransactionHash,
            WalletAddress = notification.WalletAddress,
            RegisterAddress = notification.RegisterAddress,
            BlueprintId = notification.BlueprintId,
            ActionId = notification.ActionId,
            InstanceId = notification.InstanceId,
            Timestamp = notification.Timestamp,
            Message = "Transaction confirmed"
        };

        await notificationService.NotifyActionConfirmedAsync(actionNotification);

        return Results.Accepted();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("NotifyTransactionConfirmed")
.WithSummary("Notify transaction confirmed")
.WithDescription("Internal endpoint for Register Service to notify of transaction confirmations (requires service authentication)");

// ===========================
// Instance-Based Orchestration Endpoints (Sprint 6)
// ===========================

var instancesGroup = app.MapGroup("/api/instances")
    .WithTags("Instances")
    .RequireAuthorization("CanExecuteBlueprints");

/// <summary>
/// Create a new workflow instance
/// </summary>
instancesGroup.MapPost("/", async (
    CreateInstanceRequest request,
    Sorcha.Blueprint.Service.Storage.IInstanceStore instanceStore,
    IBlueprintStore blueprintStore,
    Sorcha.ServiceClients.Register.IRegisterServiceClient registerClient) =>
{
    try
    {
        // Validate blueprint exists
        var blueprint = await blueprintStore.GetAsync(request.BlueprintId);
        if (blueprint == null)
        {
            return Results.BadRequest(new { error = "Blueprint not found" });
        }

        // Ensure the blueprint publish transaction exists on the register.
        // Action 0 chains from this TX, so it must be created before any actions execute.
        // The Register Service endpoint is idempotent — safe to call on every instance creation.
        try
        {
            var blueprintJson = System.Text.Json.JsonSerializer.Serialize(blueprint, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var published = await registerClient.PublishBlueprintToRegisterAsync(
                request.RegisterId,
                request.BlueprintId,
                blueprintJson,
                request.TenantId ?? "system");

            if (!published)
            {
                logger.LogWarning(
                    "Failed to publish blueprint {BlueprintId} to register {RegisterId} during instance creation",
                    request.BlueprintId, request.RegisterId);
            }
            else
            {
                logger.LogInformation(
                    "Blueprint {BlueprintId} published to register {RegisterId} for instance creation",
                    request.BlueprintId, request.RegisterId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Non-fatal: Could not publish blueprint {BlueprintId} to register {RegisterId}",
                request.BlueprintId, request.RegisterId);
        }

        // Find starting actions
        var startingActions = blueprint.Actions
            .Where(a => a.IsStartingAction)
            .Select(a => a.Id)
            .ToList();

        if (startingActions.Count == 0)
        {
            // Default to first action if none marked as starting
            startingActions = [blueprint.Actions.First().Id];
        }

        // Create instance
        var instance = new Sorcha.Blueprint.Service.Models.Instance
        {
            Id = Guid.NewGuid().ToString(),
            BlueprintId = request.BlueprintId,
            BlueprintVersion = 1, // TODO: Get actual published version
            RegisterId = request.RegisterId,
            CurrentActionIds = startingActions,
            State = Sorcha.Blueprint.Service.Models.InstanceState.Active,
            TenantId = request.TenantId ?? "default",
            Metadata = request.Metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? "")
                ?? new Dictionary<string, string>()
        };

        await instanceStore.CreateAsync(instance);

        return Results.Created($"/api/instances/{instance.Id}", instance);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("CreateInstance")
.WithSummary("Create workflow instance")
.WithDescription("Create a new workflow instance for a published blueprint");

/// <summary>
/// Get workflow instance by ID
/// </summary>
instancesGroup.MapGet("/{instanceId}", async (
    string instanceId,
    Sorcha.Blueprint.Service.Storage.IInstanceStore instanceStore) =>
{
    var instance = await instanceStore.GetAsync(instanceId);
    if (instance == null)
    {
        return Results.NotFound(new { error = "Instance not found" });
    }

    return Results.Ok(instance);
})
.WithName("GetInstance")
.WithSummary("Get workflow instance")
.WithDescription("Retrieve a workflow instance by its ID");

/// <summary>
/// Execute an action in a workflow instance (with orchestration)
/// </summary>
instancesGroup.MapPost("/{instanceId}/actions/{actionId}/execute", async (
    HttpContext context,
    string instanceId,
    int actionId,
    Sorcha.Blueprint.Service.Models.Requests.ActionSubmissionRequest request,
    Sorcha.Blueprint.Service.Services.Interfaces.IActionExecutionService actionExecutionService) =>
{
    try
    {
        // Get delegation token from context (set by middleware)
        var delegationToken = context.Items["DelegationToken"] as string;
        if (string.IsNullOrEmpty(delegationToken))
        {
            return Results.BadRequest(new { error = "X-Delegation-Token header is required for action execution" });
        }

        var response = await actionExecutionService.ExecuteAsync(
            instanceId,
            actionId,
            request,
            delegationToken,
            context.User);

        return Results.Ok(response);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Problem(ex.Message, statusCode: 403);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("ExecuteAction")
.WithSummary("Execute action with orchestration")
.WithDescription("Execute an action in a workflow instance with full orchestration: state reconstruction, validation, routing, transaction building, and notification. Requires X-Delegation-Token header.");

/// <summary>
/// Reject an action in a workflow instance
/// </summary>
instancesGroup.MapPost("/{instanceId}/actions/{actionId}/reject", async (
    HttpContext context,
    string instanceId,
    int actionId,
    Sorcha.Blueprint.Service.Models.Requests.ActionRejectionRequest request,
    Sorcha.Blueprint.Service.Services.Interfaces.IActionExecutionService actionExecutionService) =>
{
    try
    {
        // Get delegation token from context (set by middleware)
        var delegationToken = context.Items["DelegationToken"] as string;
        if (string.IsNullOrEmpty(delegationToken))
        {
            return Results.BadRequest(new { error = "X-Delegation-Token header is required for action rejection" });
        }

        var response = await actionExecutionService.RejectAsync(
            instanceId,
            actionId,
            request,
            delegationToken,
            context.User);

        return Results.Ok(response);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Problem(ex.Message, statusCode: 403);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("RejectActionInInstance")
.WithSummary("Reject action in workflow")
.WithDescription("Reject an action in a workflow instance, routing to the configured rejection target. Requires X-Delegation-Token header.");

/// <summary>
/// Get accumulated state for a workflow instance
/// </summary>
instancesGroup.MapGet("/{instanceId}/state", async (
    HttpContext context,
    string instanceId,
    Sorcha.Blueprint.Service.Services.Interfaces.IStateReconstructionService stateService,
    Sorcha.Blueprint.Service.Storage.IInstanceStore instanceStore,
    IBlueprintStore blueprintStore) =>
{
    try
    {
        // Get delegation token from context (set by middleware)
        var delegationToken = context.Items["DelegationToken"] as string;
        if (string.IsNullOrEmpty(delegationToken))
        {
            return Results.BadRequest(new { error = "X-Delegation-Token header is required to view state" });
        }

        var instance = await instanceStore.GetAsync(instanceId);
        if (instance == null)
        {
            return Results.NotFound(new { error = "Instance not found" });
        }

        var blueprint = await blueprintStore.GetAsync(instance.BlueprintId);
        if (blueprint == null)
        {
            return Results.BadRequest(new { error = "Blueprint not found" });
        }

        // Use the first current action for state reconstruction
        var currentActionId = instance.CurrentActionIds.FirstOrDefault();
        if (currentActionId == 0)
        {
            return Results.Ok(new
            {
                instanceId,
                actionCount = 0,
                previousTransactionId = (string?)null,
                data = new Dictionary<string, object?>(),
                branchStates = new Dictionary<string, object>()
            });
        }

        var state = await stateService.ReconstructAsync(
            blueprint,
            instanceId,
            currentActionId,
            instance.RegisterId,
            delegationToken,
            instance.ParticipantWallets);

        return Results.Ok(new
        {
            instanceId,
            actionCount = state.ActionCount,
            previousTransactionId = state.PreviousTransactionId,
            data = state.GetFlattenedData(),
            branchStates = state.BranchStates
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("GetInstanceState")
.WithSummary("Get accumulated state")
.WithDescription("Get the accumulated state from all prior actions in the workflow. Requires X-Delegation-Token header.");

/// <summary>
/// Get next available actions for a workflow instance
/// </summary>
instancesGroup.MapGet("/{instanceId}/next-actions", async (
    string instanceId,
    Sorcha.Blueprint.Service.Storage.IInstanceStore instanceStore,
    IBlueprintStore blueprintStore) =>
{
    try
    {
        var instance = await instanceStore.GetAsync(instanceId);
        if (instance == null)
        {
            return Results.NotFound(new { error = "Instance not found" });
        }

        var blueprint = await blueprintStore.GetAsync(instance.BlueprintId);
        if (blueprint == null)
        {
            return Results.BadRequest(new { error = "Blueprint not found" });
        }

        var nextActions = new List<object>();
        foreach (var actionId in instance.CurrentActionIds)
        {
            var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionId);
            if (action != null)
            {
                // Get participant info
                var participant = action.Participants?.FirstOrDefault();
                nextActions.Add(new
                {
                    actionId = action.Id,
                    title = action.Title,
                    description = action.Description,
                    participantId = participant?.Principal,
                    branchId = instance.ActiveBranches
                        .FirstOrDefault(b => b.CurrentActionId == actionId)?.Id,
                    blueprintId = instance.BlueprintId,
                    registerId = instance.RegisterId,
                    blueprintName = blueprint.Title
                });
            }
        }

        return Results.Ok(new
        {
            instanceId,
            state = instance.State.ToString().ToLowerInvariant(),
            isComplete = instance.State == Sorcha.Blueprint.Service.Models.InstanceState.Completed,
            nextActions
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("GetNextActions")
.WithSummary("Get next available actions")
.WithDescription("Get the next available actions that can be executed in the workflow instance");

// ===========================
// Health & Status Endpoints
// ===========================

app.MapGet("/api/health", async (IBlueprintStore blueprintStore, IPublishedBlueprintStore publishedStore, Sorcha.Blueprint.Service.Services.IStatusListManager statusListManager) =>
{
    try
    {
        var blueprints = await blueprintStore.GetAllAsync();
        var blueprintCount = blueprints.Count();

        // Count published blueprints
        var publishedCount = 0;
        foreach (var blueprint in blueprints)
        {
            var versions = await publishedStore.GetVersionsAsync(blueprint.Id);
            publishedCount += versions.Count();
        }

        // Probe status list availability
        var statusListAvailable = false;
        try
        {
            // Attempt to fetch a known list; null result is fine (no lists yet)
            await statusListManager.GetListAsync("health-probe");
            statusListAvailable = true;
        }
        catch
        {
            // Status list subsystem unavailable
        }

        return Results.Ok(new
        {
            status = "healthy",
            service = "blueprint-service",
            timestamp = DateTimeOffset.UtcNow,
            version = "1.0.0",
            uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"dd\.hh\:mm\:ss"),
            metrics = new
            {
                totalBlueprints = blueprintCount,
                publishedVersions = publishedCount,
                statusListAvailable
            }
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Health check failed");
        return Results.Json(new
        {
            status = "unhealthy",
            service = "blueprint-service",
            timestamp = DateTimeOffset.UtcNow,
            error = "Service unavailable"
        }, statusCode: 503);
    }
})
.WithName("HealthCheck")
.WithSummary("Service health check with metrics")
.WithTags("Health")
.AllowAnonymous();

app.Run();

// ===========================
// Service Interfaces & Implementations
// ===========================

/// <summary>
/// Blueprint storage interface
/// </summary>
public interface IBlueprintStore
{
    Task<BlueprintModel?> GetAsync(string id);
    Task<IEnumerable<BlueprintModel>> GetAllAsync();
    Task<IEnumerable<BlueprintModel>> GetAllByOrgAsync(string organizationId);
    Task<BlueprintModel> AddAsync(BlueprintModel blueprint);
    Task<BlueprintModel?> UpdateAsync(string id, BlueprintModel blueprint);
    Task<bool> DeleteAsync(string id);
}

/// <summary>
/// Published blueprint storage interface
/// </summary>
public interface IPublishedBlueprintStore
{
    Task<PublishedBlueprint> AddAsync(PublishedBlueprint published);
    Task<PublishedBlueprint?> GetVersionAsync(string blueprintId, int version);
    Task<IEnumerable<PublishedBlueprint>> GetVersionsAsync(string blueprintId);
}

/// <summary>
/// Blueprint service interface
/// </summary>
public interface IBlueprintService
{
    Task<PagedResult<BlueprintSummary>> GetAllAsync(int page, int pageSize, string? search, string? status, string? organizationId = null);
    Task<BlueprintModel?> GetByIdAsync(string id, string? organizationId = null);
    Task<BlueprintModel> CreateAsync(BlueprintModel blueprint, string? organizationId = null);
    Task<BlueprintModel?> UpdateAsync(string id, BlueprintModel blueprint, string? organizationId = null);
    Task<bool> DeleteAsync(string id, string? organizationId = null);
}

/// <summary>
/// Publish service interface
/// </summary>
public interface IPublishService
{
    Task<PublishResult> PublishAsync(string blueprintId, string? registerId = null);
    Task<BlueprintValidationResult> ValidateAsync(string blueprintId);
}

/// <summary>
/// Validation-only result (no publishing side effects)
/// </summary>
public record BlueprintValidationResult(
    string BlueprintId,
    string Title,
    bool IsValid,
    List<ValidationIssueDto> ValidationResults,
    List<string> Warnings);

public record ValidationIssueDto(string Severity, string Message, string? Location = null);

/// <summary>
/// In-memory blueprint store
/// </summary>
public class InMemoryBlueprintStore : IBlueprintStore
{
    private readonly ConcurrentDictionary<string, BlueprintModel> _blueprints = new();

    public Task<BlueprintModel?> GetAsync(string id)
    {
        _blueprints.TryGetValue(id, out var blueprint);
        return Task.FromResult(blueprint);
    }

    public Task<IEnumerable<BlueprintModel>> GetAllAsync()
    {
        return Task.FromResult(_blueprints.Values.AsEnumerable());
    }

    public Task<IEnumerable<BlueprintModel>> GetAllByOrgAsync(string organizationId)
    {
        return Task.FromResult(_blueprints.Values
            .Where(b => b.OrganizationId == organizationId)
            .AsEnumerable());
    }

    public Task<BlueprintModel> AddAsync(BlueprintModel blueprint)
    {
        blueprint.Id = Guid.NewGuid().ToString();
        blueprint.CreatedAt = DateTimeOffset.UtcNow;
        blueprint.UpdatedAt = DateTimeOffset.UtcNow;
        _blueprints[blueprint.Id] = blueprint;
        return Task.FromResult(blueprint);
    }

    public Task<BlueprintModel?> UpdateAsync(string id, BlueprintModel blueprint)
    {
        if (!_blueprints.ContainsKey(id)) return Task.FromResult<BlueprintModel?>(null);

        blueprint.Id = id;
        blueprint.UpdatedAt = DateTimeOffset.UtcNow;
        _blueprints[id] = blueprint;
        return Task.FromResult<BlueprintModel?>(blueprint);
    }

    public Task<bool> DeleteAsync(string id)
    {
        return Task.FromResult(_blueprints.TryRemove(id, out _));
    }
}

/// <summary>
/// In-memory published blueprint store
/// </summary>
public class InMemoryPublishedBlueprintStore : IPublishedBlueprintStore
{
    private readonly ConcurrentDictionary<string, List<PublishedBlueprint>> _published = new();

    public Task<PublishedBlueprint> AddAsync(PublishedBlueprint published)
    {
        var versions = _published.GetOrAdd(published.BlueprintId, _ => []);
        published.Version = versions.Count + 1;
        published.PublishedAt = DateTimeOffset.UtcNow;
        versions.Add(published);
        return Task.FromResult(published);
    }

    public Task<PublishedBlueprint?> GetVersionAsync(string blueprintId, int version)
    {
        if (_published.TryGetValue(blueprintId, out var versions))
        {
            return Task.FromResult(versions.FirstOrDefault(v => v.Version == version));
        }
        return Task.FromResult<PublishedBlueprint?>(null);
    }

    public Task<IEnumerable<PublishedBlueprint>> GetVersionsAsync(string blueprintId)
    {
        if (_published.TryGetValue(blueprintId, out var versions))
        {
            return Task.FromResult(versions.AsEnumerable());
        }
        return Task.FromResult(Enumerable.Empty<PublishedBlueprint>());
    }
}

/// <summary>
/// Blueprint service implementation
/// </summary>
public class BlueprintService(IBlueprintStore store) : IBlueprintService
{
    private readonly IBlueprintStore _store = store;

    public async Task<PagedResult<BlueprintSummary>> GetAllAsync(int page, int pageSize, string? search, string? status, string? organizationId = null)
    {
        var allBlueprints = !string.IsNullOrEmpty(organizationId)
            ? await _store.GetAllByOrgAsync(organizationId)
            : await _store.GetAllAsync();

        // Apply filtering
        var filtered = allBlueprints.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(b =>
                b.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (b.Description ?? "").Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var total = filtered.Count();
        var items = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new BlueprintSummary
            {
                Id = b.Id,
                Title = b.Title,
                Description = b.Description,
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt,
                ParticipantCount = b.Participants.Count,
                ActionCount = b.Actions.Count
            })
            .ToList();

        return new PagedResult<BlueprintSummary>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<BlueprintModel?> GetByIdAsync(string id, string? organizationId = null)
    {
        var blueprint = await _store.GetAsync(id);
        if (blueprint == null) return null;

        // Enforce org ownership if organizationId is provided
        if (!string.IsNullOrEmpty(organizationId) && blueprint.OrganizationId != organizationId)
            return null;

        return blueprint;
    }

    public Task<BlueprintModel> CreateAsync(BlueprintModel blueprint, string? organizationId = null)
    {
        if (!string.IsNullOrEmpty(organizationId))
            blueprint.OrganizationId = organizationId;

        return _store.AddAsync(blueprint);
    }

    public async Task<BlueprintModel?> UpdateAsync(string id, BlueprintModel blueprint, string? organizationId = null)
    {
        if (!string.IsNullOrEmpty(organizationId))
        {
            var existing = await _store.GetAsync(id);
            if (existing == null || existing.OrganizationId != organizationId)
                return null;
        }

        return await _store.UpdateAsync(id, blueprint);
    }

    public async Task<bool> DeleteAsync(string id, string? organizationId = null)
    {
        if (!string.IsNullOrEmpty(organizationId))
        {
            var existing = await _store.GetAsync(id);
            if (existing == null || existing.OrganizationId != organizationId)
                return false;
        }

        return await _store.DeleteAsync(id);
    }
}

/// <summary>
/// Publish service implementation with validation
/// </summary>
public class PublishService(
    IBlueprintStore blueprintStore,
    IPublishedBlueprintStore publishedStore,
    Sorcha.ServiceClients.Register.IRegisterServiceClient? registerClient = null,
    StackExchange.Redis.IConnectionMultiplexer? redis = null,
    ILogger<PublishService>? logger = null) : IPublishService
{
    private readonly IBlueprintStore _blueprintStore = blueprintStore;
    private readonly IPublishedBlueprintStore _publishedStore = publishedStore;
    private readonly Sorcha.ServiceClients.Register.IRegisterServiceClient? _registerClient = registerClient;
    private readonly StackExchange.Redis.IConnectionMultiplexer? _redis = redis;
    private readonly ILogger<PublishService>? _logger = logger;

    public async Task<BlueprintValidationResult> ValidateAsync(string blueprintId)
    {
        var blueprint = await _blueprintStore.GetAsync(blueprintId);
        if (blueprint is null)
        {
            return new BlueprintValidationResult(
                blueprintId,
                string.Empty,
                false,
                [new ValidationIssueDto("error", "Blueprint not found")],
                []);
        }

        var (errors, warnings) = ValidateBlueprint(blueprint);
        var issues = errors.Select(e => new ValidationIssueDto("error", e)).ToList();

        return new BlueprintValidationResult(
            blueprintId,
            blueprint.Title ?? string.Empty,
            errors.Count == 0,
            issues,
            warnings);
    }

    public async Task<PublishResult> PublishAsync(string blueprintId, string? registerId = null)
    {
        var blueprint = await _blueprintStore.GetAsync(blueprintId);
        if (blueprint is null)
        {
            return PublishResult.Failed("Blueprint not found");
        }

        // Validate blueprint — cycle detections are warnings, not errors
        var (errors, warnings) = ValidateBlueprint(blueprint);
        if (errors.Count > 0)
        {
            return PublishResult.Failed(errors.ToArray());
        }

        // Set hasCycles metadata if cycle warnings were detected
        if (warnings.Count > 0)
        {
            blueprint.Metadata ??= new Dictionary<string, string>();
            blueprint.Metadata["hasCycles"] = "true";
        }

        // Create published version (immutable snapshot)
        var published = new PublishedBlueprint
        {
            BlueprintId = blueprint.Id,
            Blueprint = blueprint,
            PublishedAt = DateTimeOffset.UtcNow
        };

        await _publishedStore.AddAsync(published);

        // Populate Validator's blueprint cache in Redis so transactions referencing this blueprint pass validation
        if (_redis is not null)
        {
            try
            {
                var db = _redis.GetDatabase();
                var cacheKey = $"sorcha:validator:blueprint:{blueprintId}";
                var blueprintJson = System.Text.Json.JsonSerializer.Serialize(blueprint, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
                await db.StringSetAsync(cacheKey, blueprintJson);
                _logger?.LogInformation("Blueprint {BlueprintId} cached in Redis for Validator (key: {CacheKey}, no expiry — immutable)", blueprintId, cacheKey);
            }
            catch (Exception ex)
            {
                // Non-fatal: Validator will fail with VAL_SCHEMA_001 but publishing itself succeeded
                _logger?.LogWarning(ex, "Failed to cache blueprint {BlueprintId} in Redis for Validator", blueprintId);
            }
        }

        // If a register is specified, also publish to the register
        if (!string.IsNullOrEmpty(registerId) && _registerClient is not null)
        {
            var blueprintJson = System.Text.Json.JsonSerializer.Serialize(blueprint);
            await _registerClient.PublishBlueprintToRegisterAsync(
                registerId, blueprintId, blueprintJson, "system");
        }

        return PublishResult.Success(published, warnings.ToArray());
    }

    private (List<string> Errors, List<string> Warnings) ValidateBlueprint(BlueprintModel blueprint)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Rule 1: Must have at least 2 participants
        if (blueprint.Participants.Count < 2)
        {
            errors.Add("Blueprint must have at least 2 participants");
        }

        // Rule 2: Must have at least 1 action
        if (blueprint.Actions.Count < 1)
        {
            errors.Add("Blueprint must have at least 1 action");
        }

        // Rule 3: Validate participant references across the blueprint
        var participantIds = blueprint.Participants.Select(p => p.Id).ToHashSet();
        var actionIds = blueprint.Actions.Select(a => a.Id).ToHashSet();

        foreach (var action in blueprint.Actions)
        {
            // 3a: Action.Sender must reference a valid participant
            if (!string.IsNullOrWhiteSpace(action.Sender) && !participantIds.Contains(action.Sender))
            {
                errors.Add($"Action {action.Id} ('{action.Title}'): Sender '{action.Sender}' is not a defined participant");
            }

            // 3b: Legacy participant condition principals must reference valid participants
            if (action.Participants != null)
            {
                foreach (var participant in action.Participants)
                {
                    if (!string.IsNullOrWhiteSpace(participant.Principal) && !participantIds.Contains(participant.Principal))
                    {
                        errors.Add($"Action {action.Id} ('{action.Title}'): Participant principal '{participant.Principal}' is not defined");
                    }
                }
            }

            // 3c: Disclosure participant addresses must reference valid participants
            if (action.Disclosures != null)
            {
                foreach (var disclosure in action.Disclosures)
                {
                    if (!string.IsNullOrWhiteSpace(disclosure.ParticipantAddress) && !participantIds.Contains(disclosure.ParticipantAddress))
                    {
                        warnings.Add($"Action {action.Id} ('{action.Title}'): Disclosure references participant '{disclosure.ParticipantAddress}' which is not defined");
                    }
                }
            }

            // 3d: RejectionConfig.TargetParticipantId must reference valid participant
            if (action.RejectionConfig?.TargetParticipantId != null && !participantIds.Contains(action.RejectionConfig.TargetParticipantId))
            {
                warnings.Add($"Action {action.Id} ('{action.Title}'): Rejection target participant '{action.RejectionConfig.TargetParticipantId}' is not defined");
            }
        }

        // Rule 4: Validate credential requirements on actions (FR-020)
        foreach (var action in blueprint.Actions)
        {
            if (action.CredentialRequirements != null)
            {
                var reqIndex = 0;
                foreach (var req in action.CredentialRequirements)
                {
                    if (string.IsNullOrWhiteSpace(req.Type))
                    {
                        errors.Add($"Action {action.Id}: Credential requirement [{reqIndex}] has an empty type");
                    }

                    if (req.RequiredClaims != null)
                    {
                        foreach (var claim in req.RequiredClaims)
                        {
                            if (string.IsNullOrWhiteSpace(claim.ClaimName))
                            {
                                errors.Add($"Action {action.Id}: Credential requirement '{req.Type}' has a claim constraint with empty name");
                            }
                        }
                    }

                    reqIndex++;
                }
            }

            if (action.CredentialIssuanceConfig != null)
            {
                var config = action.CredentialIssuanceConfig;
                if (string.IsNullOrWhiteSpace(config.CredentialType))
                {
                    errors.Add($"Action {action.Id}: Credential issuance config has an empty credential type");
                }
                if (!config.ClaimMappings.Any())
                {
                    errors.Add($"Action {action.Id}: Credential issuance config must have at least one claim mapping");
                }
                if (string.IsNullOrWhiteSpace(config.RecipientParticipantId))
                {
                    errors.Add($"Action {action.Id}: Credential issuance config has an empty recipient participant ID");
                }
                else if (!participantIds.Contains(config.RecipientParticipantId))
                {
                    warnings.Add($"Action {action.Id}: Credential issuance config references participant '{config.RecipientParticipantId}' which is not defined in this blueprint");
                }
            }
        }

        // Rule 5: Detect cycles — produce warnings instead of errors
        // Cycles are valid workflow patterns (ping-pong, review loops, resubmission flows)
        var cycleDetections = DetectCycles(blueprint);
        foreach (var detection in cycleDetections)
        {
            // Rewrite cycle errors as warnings with helpful context
            var warning = detection.Replace("Circular dependency detected:", "Cyclic route detected:");
            if (!warning.Contains("loop indefinitely"))
            {
                warning += ". This blueprint will loop indefinitely unless routing conditions provide a termination path.";
            }
            warnings.Add(warning);
        }

        // Rule 6: Starting action validation
        var startingActions = blueprint.Actions.Where(a => a.IsStartingAction).ToList();
        if (startingActions.Count == 0)
        {
            warnings.Add("No action has IsStartingAction=true. The first action will be used as the implicit starting action.");
        }

        // Rule 7: Route targets and rejection targets must reference valid actions
        foreach (var action in blueprint.Actions)
        {
            if (action.Routes != null)
            {
                foreach (var route in action.Routes)
                {
                    if (route.NextActionIds != null)
                    {
                        foreach (var targetId in route.NextActionIds)
                        {
                            if (!actionIds.Contains(targetId))
                            {
                                errors.Add($"Action {action.Id} ('{action.Title}'): Route '{route.Id}' references non-existent action {targetId}");
                            }
                        }
                    }
                }
            }

            if (action.RejectionConfig != null && !action.RejectionConfig.IsTerminal)
            {
                if (!actionIds.Contains(action.RejectionConfig.TargetActionId))
                {
                    errors.Add($"Action {action.Id} ('{action.Title}'): Rejection target action {action.RejectionConfig.TargetActionId} does not exist");
                }
            }
        }

        // Rule 7b: Detect orphan actions (unreachable, not starting)
        var reachableActionIds = new HashSet<int>();
        foreach (var action in blueprint.Actions)
        {
            if (action.IsStartingAction || (startingActions.Count == 0 && action.Id == blueprint.Actions.Min(a => a.Id)))
            {
                reachableActionIds.Add(action.Id);
            }

            if (action.Routes != null)
            {
                foreach (var route in action.Routes)
                {
                    if (route.NextActionIds != null)
                    {
                        foreach (var targetId in route.NextActionIds)
                            reachableActionIds.Add(targetId);
                    }
                }
            }

            if (action.RejectionConfig != null)
                reachableActionIds.Add(action.RejectionConfig.TargetActionId);
        }

        foreach (var action in blueprint.Actions)
        {
            if (!reachableActionIds.Contains(action.Id))
            {
                warnings.Add($"Action {action.Id} ('{action.Title}') is unreachable — no route or rejection targets it and it is not a starting action");
            }
        }

        // Rule 8: JSON Pointer syntax validation in disclosures
        foreach (var action in blueprint.Actions)
        {
            if (action.Disclosures == null) continue;

            foreach (var disclosure in action.Disclosures)
            {
                if (disclosure.DataPointers == null || disclosure.DataPointers.Count == 0)
                {
                    errors.Add($"Action {action.Id} ('{action.Title}'): Disclosure for '{disclosure.ParticipantAddress}' has no data pointers");
                    continue;
                }

                foreach (var pointer in disclosure.DataPointers)
                {
                    if (string.IsNullOrWhiteSpace(pointer))
                    {
                        errors.Add($"Action {action.Id} ('{action.Title}'): Disclosure has an empty JSON Pointer");
                    }
                    else if (pointer != "/*" && !pointer.StartsWith("/") && !pointer.StartsWith("#/"))
                    {
                        errors.Add($"Action {action.Id} ('{action.Title}'): JSON Pointer '{pointer}' must start with '/' (RFC 6901)");
                    }
                    else if (pointer.Contains("~") && !System.Text.RegularExpressions.Regex.IsMatch(pointer, @"~[01]|$"))
                    {
                        // Check for invalid escape sequences: ~ must be followed by 0 or 1
                        var segments = pointer.Split('/');
                        foreach (var segment in segments)
                        {
                            for (var ci = 0; ci < segment.Length; ci++)
                            {
                                if (segment[ci] == '~' && (ci + 1 >= segment.Length || (segment[ci + 1] != '0' && segment[ci + 1] != '1')))
                                {
                                    errors.Add($"Action {action.Id} ('{action.Title}'): JSON Pointer '{pointer}' has invalid escape '~' not followed by '0' or '1' (RFC 6901)");
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        // Rule 9: JSON Logic syntax validation in routes and calculations
        foreach (var action in blueprint.Actions)
        {
            if (action.Routes != null)
            {
                foreach (var route in action.Routes)
                {
                    if (route.Condition != null)
                    {
                        try
                        {
                            var ruleJson = route.Condition.ToJsonString();
                            var rule = System.Text.Json.JsonSerializer.Deserialize<Json.Logic.Rule>(ruleJson);
                            if (rule == null)
                            {
                                errors.Add($"Action {action.Id} ('{action.Title}'): Route '{route.Id}' condition is not valid JSON Logic");
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Action {action.Id} ('{action.Title}'): Route '{route.Id}' condition failed to parse as JSON Logic: {ex.Message}");
                        }
                    }
                }
            }

            if (action.Calculations != null)
            {
                foreach (var (fieldName, expression) in action.Calculations)
                {
                    try
                    {
                        var ruleJson = expression.ToJsonString();
                        var rule = System.Text.Json.JsonSerializer.Deserialize<Json.Logic.Rule>(ruleJson);
                        if (rule == null)
                        {
                            errors.Add($"Action {action.Id} ('{action.Title}'): Calculation '{fieldName}' is not valid JSON Logic");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Action {action.Id} ('{action.Title}'): Calculation '{fieldName}' failed to parse as JSON Logic: {ex.Message}");
                    }
                }
            }

            // Rule 9b: Validate Form.Schema is valid JSON Schema (if present)
            if (action.Form?.Schema != null)
            {
                try
                {
                    Json.Schema.JsonSchema.FromText(action.Form.Schema.ToJsonString());
                }
                catch (Exception ex)
                {
                    errors.Add($"Action {action.Id} ('{action.Title}'): Form schema is not valid JSON Schema: {ex.Message}");
                }
            }
        }

        return (errors, warnings);
    }

    /// <summary>
    /// Detects cycles in the blueprint action graph using DFS with coloring.
    /// Checks Routes[].NextActionIds and RejectionConfig.TargetActionId edges.
    /// </summary>
    private List<string> DetectCycles(BlueprintModel blueprint)
    {
        var errors = new List<string>();

        // Build adjacency list: actionId -> list of target actionIds
        var adjacency = new Dictionary<int, List<int>>();
        foreach (var action in blueprint.Actions)
        {
            var targets = new List<int>();

            // Add edges from Routes
            if (action.Routes != null)
            {
                foreach (var route in action.Routes)
                {
                    if (route.NextActionIds != null)
                    {
                        targets.AddRange(route.NextActionIds);
                    }
                }
            }

            // Add edge from RejectionConfig
            if (action.RejectionConfig != null)
            {
                targets.Add(action.RejectionConfig.TargetActionId);
            }

            adjacency[action.Id] = targets;
        }

        // DFS with coloring: 0=White (unvisited), 1=Gray (in path), 2=Black (done)
        var color = new Dictionary<int, int>();
        foreach (var action in blueprint.Actions)
        {
            color[action.Id] = 0;
        }

        var path = new List<int>();

        foreach (var action in blueprint.Actions)
        {
            if (color[action.Id] == 0)
            {
                DfsCycleDetect(action.Id, adjacency, color, path, errors);
            }
        }

        return errors;
    }

    private void DfsCycleDetect(
        int node,
        Dictionary<int, List<int>> adjacency,
        Dictionary<int, int> color,
        List<int> path,
        List<string> errors)
    {
        // Self-reference check
        if (adjacency.TryGetValue(node, out var neighbors) && neighbors.Contains(node))
        {
            errors.Add($"Self-referencing route detected: Action {node} routes to itself");
        }

        color[node] = 1; // Gray - in current path
        path.Add(node);

        if (neighbors != null)
        {
            foreach (var neighbor in neighbors)
            {
                if (neighbor == node) continue; // Already reported self-reference

                if (!color.ContainsKey(neighbor)) continue; // Target action doesn't exist

                if (color[neighbor] == 1)
                {
                    // Cycle detected - build cycle path string
                    var cycleStart = path.IndexOf(neighbor);
                    var cyclePath = path.Skip(cycleStart).Append(neighbor);
                    var cycleStr = string.Join(" → ", cyclePath.Select(id => $"Action {id}"));
                    errors.Add($"Circular dependency detected: {cycleStr}");
                }
                else if (color[neighbor] == 0)
                {
                    DfsCycleDetect(neighbor, adjacency, color, path, errors);
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        color[node] = 2; // Black - fully explored
    }
}

// ===========================
// DTOs & Models
// ===========================

/// <summary>
/// Request body for publish with optional register target
/// </summary>
public record PublishRequest(string? RegisterId = null);

/// <summary>
/// Blueprint summary for list views
/// </summary>
public record BlueprintSummary
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int ParticipantCount { get; init; }
    public int ActionCount { get; init; }
}

/// <summary>
/// Paged result wrapper
/// </summary>
public record PagedResult<T>
{
    public IEnumerable<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}

/// <summary>
/// Published blueprint with version
/// </summary>
public record PublishedBlueprint
{
    public string BlueprintId { get; init; } = string.Empty;
    public int Version { get; set; }
    public BlueprintModel Blueprint { get; init; } = null!;
    public DateTimeOffset PublishedAt { get; set; }
}

/// <summary>
/// Publish result
/// </summary>
public record PublishResult
{
    public bool IsSuccess { get; init; }
    public PublishedBlueprint? PublishedBlueprint { get; init; }
    public string[] Errors { get; init; } = [];
    public string[] Warnings { get; init; } = [];

    public static PublishResult Success(PublishedBlueprint published, string[]? warnings = null) => new()
    {
        IsSuccess = true,
        PublishedBlueprint = published,
        Warnings = warnings ?? []
    };

    public static PublishResult Failed(params string[] errors) => new()
    {
        IsSuccess = false,
        Errors = errors
    };
}

// ===========================
// Execution Endpoint Request DTOs (Sprint 5)
// ===========================

/// <summary>
/// Request for validating action data
/// </summary>
public record ValidateRequest
{
    public required string BlueprintId { get; init; }
    public required string ActionId { get; init; }
    public required Dictionary<string, object> Data { get; init; }
}

/// <summary>
/// Request for applying calculations
/// </summary>
public record CalculateRequest
{
    public required string BlueprintId { get; init; }
    public required string ActionId { get; init; }
    public required Dictionary<string, object> Data { get; init; }
}

/// <summary>
/// Request for determining routing
/// </summary>
public record RouteRequest
{
    public required string BlueprintId { get; init; }
    public required string ActionId { get; init; }
    public required Dictionary<string, object> Data { get; init; }
}

/// <summary>
/// Request for applying disclosure rules
/// </summary>
public record DiscloseRequest
{
    public required string BlueprintId { get; init; }
    public required string ActionId { get; init; }
    public required Dictionary<string, object> Data { get; init; }
}

// ===========================
// Notification DTOs (Sprint 5)
// ===========================

/// <summary>
/// Notification sent by Register Service when a transaction is confirmed
/// </summary>
public record TransactionConfirmationNotification
{
    public required string TransactionHash { get; init; }
    public required string WalletAddress { get; init; }
    public required string RegisterAddress { get; init; }
    public string? BlueprintId { get; init; }
    public string? ActionId { get; init; }
    public string? InstanceId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

// ===========================
// Instance DTOs (Sprint 6)
// ===========================

/// <summary>
/// Request to create a new workflow instance
/// </summary>
public record CreateInstanceRequest
{
    /// <summary>
    /// The ID of the blueprint to instantiate
    /// </summary>
    public required string BlueprintId { get; init; }

    /// <summary>
    /// The register ID where transactions will be stored
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Optional tenant ID for isolation (defaults to "default")
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Optional metadata to associate with the instance
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}
