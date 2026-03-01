// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using Microsoft.AspNetCore.OData;
using Microsoft.OData.ModelBuilder;
using MongoDB.Driver;
using Scalar.AspNetCore;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Extensions;
using Sorcha.Register.Service.Hubs;
using Sorcha.Register.Service.Repositories;
using Sorcha.Register.Service.Services;
using Microsoft.Extensions.Options;
using Sorcha.Register.Storage.InMemory;
using Sorcha.Register.Storage.MongoDB;
using Sorcha.Register.Storage.Redis;
using Sorcha.ServiceClients.Extensions;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.SystemWallet;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Add structured logging with Serilog (OPS-001)
builder.AddSerilogLogging();

// Add rate limiting (SEC-002)
builder.AddRateLimiting();

// Add input validation (SEC-003)
builder.AddInputValidation();

// Add SignalR for real-time notifications
builder.Services.AddSignalR();

// Configure OData
var modelBuilder = new ODataConventionModelBuilder();
modelBuilder.EntitySet<Sorcha.Register.Models.Register>("Registers");
modelBuilder.EntitySet<TransactionModel>("Transactions");
modelBuilder.EntitySet<Docket>("Dockets");

builder.Services.AddControllers()
    .AddOData(options => options
        .Select()
        .Filter()
        .OrderBy()
        .Expand()
        .Count()
        .SetMaxTop(100)
        .AddRouteComponents("odata", modelBuilder.GetEdmModel()));

// Add OpenAPI services with standard Sorcha metadata
builder.AddSorchaOpenApi("Sorcha Register Service API", "Distributed ledger for storing immutable transaction records with cryptographic chain integrity, OData queries, SignalR real-time notifications, and wallet-based payload encryption.");

// Register storage and event infrastructure
// Smart configuration: Use MongoDB if configured, otherwise InMemory
var storageType = builder.Configuration["RegisterStorage:Type"] ?? "InMemory";
if (storageType.Equals("MongoDB", StringComparison.OrdinalIgnoreCase))
{
    // Configure MongoDB storage
    builder.Services.Configure<MongoRegisterStorageConfiguration>(
        builder.Configuration.GetSection("RegisterStorage:MongoDB"));

    builder.Services.AddSingleton<IMongoClient>(sp =>
    {
        var config = sp.GetRequiredService<IOptions<MongoRegisterStorageConfiguration>>().Value;
        return new MongoClient(config.ConnectionString);
    });

    builder.Services.AddSingleton<IRegisterRepository>(sp =>
    {
        var client = sp.GetRequiredService<IMongoClient>();
        var options = sp.GetRequiredService<IOptions<MongoRegisterStorageConfiguration>>();
        var logger = sp.GetRequiredService<ILogger<MongoRegisterRepository>>();
        return new MongoRegisterRepository(client, options, logger);
    });

    // Register the same instance as IReadOnlyRegisterRepository
    builder.Services.AddSingleton<IReadOnlyRegisterRepository>(sp =>
        sp.GetRequiredService<IRegisterRepository>());

}
else
{
    // Use in-memory storage (default)
    builder.Services.AddSingleton<IRegisterRepository, InMemoryRegisterRepository>();

    // Register the same instance as IReadOnlyRegisterRepository
    builder.Services.AddSingleton<IReadOnlyRegisterRepository>(sp =>
        sp.GetRequiredService<IRegisterRepository>());
}

// Event infrastructure: Redis Streams for durable event publishing/subscribing
builder.Services.AddRedisEventStreams(builder.Configuration);

// Register managers
builder.Services.AddScoped<RegisterManager>();
builder.Services.AddScoped<TransactionManager>();
builder.Services.AddScoped<QueryManager>();

// Register creation orchestration
builder.Services.AddScoped<IRegisterCreationOrchestrator, RegisterCreationOrchestrator>();

// Redis client for distributed state (pending registrations, caching)
builder.AddRedisClient("redis");

// Pending registration storage (Redis-backed for multi-instance deployments)
builder.Services.AddSingleton<IPendingRegistrationStore, PendingRegistrationStore>();

// Register cryptography services (from Sorcha.Cryptography)
builder.Services.AddScoped<IHashProvider, Sorcha.Cryptography.Core.HashProvider>();
builder.Services.AddScoped<ICryptoModule, Sorcha.Cryptography.Core.CryptoModule>();

// Register wallet service client
builder.Services.AddServiceClients(builder.Configuration);

// Register system wallet signing service (opt-in — used for genesis + blueprint publish)
builder.Services.AddSystemWalletSigning(builder.Configuration);

// Register crypto policy service
builder.Services.AddScoped<Sorcha.Register.Service.Services.CryptoPolicyService>();

// Register governance roster service
builder.Services.AddScoped<Sorcha.Register.Core.Services.IGovernanceRosterService,
    Sorcha.Register.Core.Services.GovernanceRosterService>();
builder.Services.AddScoped<Sorcha.Register.Core.Services.IDIDResolver,
    Sorcha.Register.Core.Services.DIDResolver>();

// Register MongoDB for system register
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("MongoDB") ?? "mongodb://localhost:27017";
    return new MongoClient(connectionString);
});

builder.Services.AddSingleton<IMongoDatabase>(sp =>
{
    var client = sp.GetRequiredService<IMongoClient>();
    var databaseName = builder.Configuration["MongoDB:DatabaseName"] ?? "sorcha_system_register";
    return client.GetDatabase(databaseName);
});

// Register system register services
builder.Services.AddSingleton<ISystemRegisterRepository, MongoSystemRegisterRepository>();
builder.Services.AddSingleton<SystemRegisterService>();

// Participant index service (in-memory address → participant mapping)
builder.Services.AddSingleton<ParticipantIndexService>();

// Register advertisement resync background service (FR-003, FR-004)
builder.Services.AddHostedService<AdvertisementResyncService>();

// Register event bridge: subscribes to domain events and broadcasts via SignalR
builder.Services.AddHostedService<RegisterEventBridgeService>();

// Add JWT authentication and authorization (AUTH-002)
// JWT authentication is now configured via shared ServiceDefaults with auto-key generation
builder.AddJwtAuthentication();
builder.Services.AddRegisterAuthorization();

var app = builder.Build();

if (storageType.Equals("MongoDB", StringComparison.OrdinalIgnoreCase))
{
    app.Logger.LogInformation("Register Service using MongoDB storage");
}
else
{
    app.Logger.LogInformation("Register Service using InMemory storage (development mode)");
}

// Map default endpoints (health checks)
app.MapDefaultEndpoints();

// Add Serilog HTTP request logging (OPS-001)
app.UseSerilogLogging();

// Add OWASP security headers (SEC-004)
app.UseApiSecurityHeaders();

// Enable HTTPS enforcement with HSTS (SEC-001)
app.UseHttpsEnforcement();

// Enable input validation (SEC-003)
app.UseInputValidation();

// Configure OpenAPI and Scalar API documentation UI (development only)
app.MapSorchaOpenApiUi("Register Service");

// Map SignalR hub
app.MapHub<RegisterHub>("/hubs/register");

// Add authentication and authorization middleware (AUTH-002)
app.UseAuthentication();
app.UseAuthorization();

// Enable rate limiting (SEC-002)
app.UseRateLimiting();

// ===========================
// Register Management API
// ===========================

var registersGroup = app.MapGroup("/api/registers")
    .WithTags("Registers")
    .RequireAuthorization("CanManageRegisters");

// NOTE: POST /api/registers/ (simple CRUD creation) has been removed.
// All register creation must go through the two-phase initiate/finalize flow.
// See register creation endpoints below (POST /api/registers/initiate and POST /api/registers/finalize).

/// <summary>
/// Get all registers
/// </summary>
registersGroup.MapGet("/", async (
    RegisterManager manager,
    string? tenantId = null) =>
{
    var registers = tenantId != null
        ? await manager.GetRegistersByTenantAsync(tenantId)
        : await manager.GetAllRegistersAsync();

    return Results.Ok(registers);
})
.WithName("GetAllRegisters")
.WithSummary("Get all registers")
.WithDescription("Retrieves all registers, optionally filtered by tenant.");

/// <summary>
/// Get register by ID
/// </summary>
registersGroup.MapGet("/{id}", async (
    RegisterManager manager,
    string id) =>
{
    var register = await manager.GetRegisterAsync(id);
    return register is not null ? Results.Ok(register) : Results.NotFound();
})
.WithName("GetRegister")
.WithSummary("Get register by ID")
.WithDescription("Retrieves a specific register by its unique identifier.");

/// <summary>
/// Update register
/// </summary>
registersGroup.MapPut("/{id}", async (
    RegisterManager manager,
    IPeerServiceClient peerClient,
    ILogger<Program> logger,
    string id,
    UpdateRegisterRequest request) =>
{
    var register = await manager.GetRegisterAsync(id);
    if (register is null)
        return Results.NotFound();

    var advertiseChanged = request.Advertise is not null && register.Advertise != request.Advertise.Value;

    if (request.Name is not null)
        register.Name = request.Name;
    if (request.Status is not null)
        register.Status = request.Status.Value;
    if (request.Advertise is not null)
        register.Advertise = request.Advertise.Value;

    var updated = await manager.UpdateRegisterAsync(register);

    // Notify Peer Service when advertise flag changes (fire-and-forget)
    if (advertiseChanged)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await peerClient.AdvertiseRegisterAsync(register.Id, register.Advertise);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to notify Peer Service about advertise change for register {RegisterId}",
                    register.Id);
            }
        });
    }

    return Results.Ok(updated);
})
.WithName("UpdateRegister")
.WithSummary("Update register")
.WithDescription("Updates register metadata and settings.");

/// <summary>
/// Delete register
/// </summary>
registersGroup.MapDelete("/{id}", async (
    RegisterManager manager,
    string id,
    string tenantId) =>
{
    try
    {
        await manager.DeleteRegisterAsync(id, tenantId);
        // SignalR notification handled by RegisterEventBridgeService via RegisterDeletedEvent
        return Results.NoContent();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
})
.WithName("DeleteRegister")
.WithSummary("Delete register")
.WithDescription("Deletes a register and all associated data.");

/// <summary>
/// Get register count
/// </summary>
registersGroup.MapGet("/stats/count", async (RegisterManager manager) =>
{
    var count = await manager.GetRegisterCountAsync();
    return Results.Ok(new { count });
})
.WithName("GetRegisterCount")
.WithSummary("Get register count")
.WithDescription("Returns the total number of registers.");

// ===========================
// Register Creation with Genesis Transactions (FR-REG-001A)
// ===========================
// Separate endpoint group for register creation workflow (initiate/finalize)
// These endpoints allow anonymous access for walkthrough/testing purposes
var registerCreationGroup = app.MapGroup("/api/registers")
    .WithTags("Register Creation")
    .AllowAnonymous();

/// <summary>
/// Initiate register creation (Phase 1): Generate unsigned control record
/// </summary>
registerCreationGroup.MapPost("/initiate", async (
    IRegisterCreationOrchestrator orchestrator,
    InitiateRegisterCreationRequest request,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await orchestrator.InitiateAsync(request, cancellationToken);
        return Results.Ok(response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message, details = "Invalid request parameters" });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Register initiation failed",
            detail: ex.Message,
            statusCode: 500);
    }
})
.WithName("InitiateRegisterCreation")
.WithSummary("Initiate register creation (Phase 1)")
.WithDescription(@"
**Phase 1: Generate Unsigned Control Record**

Initiates the two-phase register creation workflow by generating a unique register ID
and unsigned control record template. The client must sign the returned `dataToSign` hash
with each admin's wallet before calling the finalize endpoint.

**Workflow:**
1. Server generates unique register ID and control record template
2. Server computes SHA-256 hash of control record for signing
3. Client signs the hash with each admin's wallet (offline/client-side)
4. Client calls /finalize with signed control record

**Control Record:**
The control record establishes administrative control with cryptographic attestations.
At least one 'owner' attestation is required.

**Expiration:**
The pending registration expires after 5 minutes. The client must finalize within this timeframe.

**Returns:**
- `registerId`: Generated unique ID
- `controlRecord`: Template with placeholder signatures
- `dataToSign`: SHA-256 hash to sign with wallets
- `expiresAt`: Expiration timestamp
- `nonce`: Replay protection nonce
");

/// <summary>
/// Finalize register creation (Phase 2): Verify signatures and create register
/// </summary>
registerCreationGroup.MapPost("/finalize", async (
    IRegisterCreationOrchestrator orchestrator,
    FinalizeRegisterCreationRequest request,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await orchestrator.FinalizeAsync(request, cancellationToken);
        return Results.Created($"/api/registers/{response.RegisterId}", response);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("expired"))
    {
        return Results.Problem(
            title: "Registration expired",
            detail: ex.Message,
            statusCode: 408); // Request Timeout
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Problem(
            title: "Signature verification failed",
            detail: ex.Message,
            statusCode: 401); // Unauthorized
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message, details = "Invalid control record or signatures" });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Register finalization failed",
            detail: ex.Message,
            statusCode: 500);
    }
})
.WithName("FinalizeRegisterCreation")
.WithSummary("Finalize register creation (Phase 2)")
.WithDescription(@"
**Phase 2: Verify Signatures and Create Register**

Completes the register creation workflow by verifying all attestation signatures,
creating the register in the database, and generating the genesis transaction.

**Workflow:**
1. Server retrieves pending registration by ID and nonce
2. Server validates control record against JSON Schema
3. Server verifies each attestation signature using public keys
4. Server creates register in database
5. Server creates genesis transaction with control record payload
6. Server submits genesis transaction to Validator Service
7. Validator creates genesis docket (height 0)

**Signature Verification:**
- Each attestation signature is verified using the subject's public key
- Supported algorithms: ED25519, NISTP256, RSA4096
- Signature must match the SHA-256 hash from initiation phase

**Genesis Transaction:**
The genesis transaction contains the signed control record and establishes
an immutable audit trail of register creation and ownership.

**Returns:**
- `registerId`: Created register ID
- `status`: 'created'
- `genesisTransactionId`: Genesis transaction ID
- `genesisDocketId`: '0' (genesis docket)
- `createdAt`: Creation timestamp

**Errors:**
- 400 Bad Request: Invalid control record or validation errors
- 401 Unauthorized: Signature verification failed
- 408 Request Timeout: Pending registration expired
- 500 Internal Server Error: Database or service error
");

// ===========================
// Transaction Management API
// ===========================

var transactionsGroup = app.MapGroup("/api/registers/{registerId}/transactions")
    .WithTags("Transactions");

/// <summary>
/// Submit a transaction
/// </summary>
transactionsGroup.MapPost("/", async (
    TransactionManager manager,
    IEventPublisher eventPublisher,
    string registerId,
    TransactionModel transaction) =>
{
    try
    {
        transaction.RegisterId = registerId;
        var stored = await manager.StoreTransactionAsync(transaction);

        // Publish event — SignalR notification handled by RegisterEventBridgeService
        await eventPublisher.PublishAsync(
            "transaction:confirmed",
            new TransactionConfirmedEvent
            {
                TransactionId = stored.TxId,
                RegisterId = registerId,
                SenderWallet = stored.SenderWallet,
                PreviousTransactionId = stored.PrevTxId,
                MetaData = stored.MetaData,
                ConfirmedAt = DateTime.UtcNow
            });

        return Results.Created($"/api/registers/{registerId}/transactions/{stored.TxId}", stored);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("SubmitTransaction")
.WithSummary("Submit a transaction (internal/diagnostic only)")
.WithDescription("Stores a transaction directly in the register. Action transactions should be submitted via the Validator Service pipeline.")
.RequireAuthorization("CanWriteDockets");

/// <summary>
/// Get transaction by ID
/// </summary>
transactionsGroup.MapGet("/{txId}", async (
    TransactionManager manager,
    string registerId,
    string txId) =>
{
    var transaction = await manager.GetTransactionAsync(registerId, txId);
    return transaction is not null ? Results.Ok(transaction) : Results.NotFound();
})
.WithName("GetTransaction")
.WithSummary("Get transaction by ID")
.WithDescription("Retrieves a specific transaction by its ID.")
.RequireAuthorization("CanReadTransactions");

/// <summary>
/// Get all transactions for a register (queryable)
/// </summary>
transactionsGroup.MapGet("/", async (
    TransactionManager manager,
    string registerId,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "$skip")] int? skip,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "$top")] int? top,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "$count")] bool? count) =>
{
    var odataSkip = skip ?? 0;
    var odataTop = top ?? 20;

    var transactions = await manager.GetTransactionsAsync(registerId);
    var totalCount = transactions.Count();
    var paged = transactions
        .OrderByDescending(t => t.TimeStamp)
        .Skip(odataSkip)
        .Take(odataTop)
        .ToList();

    // OData-style paged response
    var page = odataTop > 0 ? (odataSkip / odataTop) + 1 : 1;
    return Results.Ok(new
    {
        Page = page,
        PageSize = odataTop,
        Total = totalCount,
        Transactions = paged
    });
})
.WithName("GetTransactions")
.WithSummary("Get all transactions")
.WithDescription("Retrieves all transactions for a register with OData pagination ($skip, $top, $count).")
.RequireAuthorization("CanReadTransactions");

// ===========================
// Query API
// ===========================

var queryGroup = app.MapGroup("/api/query")
    .WithTags("Query")
    .RequireAuthorization("CanReadTransactions");

/// <summary>
/// Query transactions by wallet address
/// </summary>
queryGroup.MapGet("/wallets/{address}/transactions", async (
    QueryManager manager,
    string address,
    string? registerId = null,
    int page = 1,
    int pageSize = 20) =>
{
    if (registerId is not null)
    {
        var result = await manager.GetTransactionsByWalletPaginatedAsync(
            registerId,
            address,
            page,
            pageSize);
        return Results.Ok(result);
    }

    // Query across all registers (future enhancement)
    return Results.BadRequest(new { error = "registerId is required" });
})
.WithName("GetTransactionsByWallet")
.WithSummary("Query transactions by wallet")
.WithDescription("Retrieves all transactions for a specific wallet address.");

/// <summary>
/// Query transactions by sender
/// </summary>
queryGroup.MapGet("/senders/{address}/transactions", async (
    QueryManager manager,
    string address,
    string registerId,
    int page = 1,
    int pageSize = 20) =>
{
    var result = await manager.GetTransactionsByWalletPaginatedAsync(
        registerId,
        address,
        page,
        pageSize,
        asSender: true,
        asRecipient: false);
    return Results.Ok(result);
})
.WithName("GetTransactionsBySender")
.WithSummary("Query transactions by sender")
.WithDescription("Retrieves all transactions sent by a specific address.");

/// <summary>
/// Query transactions by blueprint
/// </summary>
queryGroup.MapGet("/blueprints/{blueprintId}/transactions", async (
    QueryManager manager,
    string blueprintId,
    string registerId,
    string? instanceId = null) =>
{
    var result = await manager.GetTransactionsByBlueprintAsync(
        registerId,
        blueprintId,
        instanceId);

    return Results.Ok(result);
})
.WithName("GetTransactionsByBlueprint")
.WithSummary("Query transactions by blueprint")
.WithDescription("Retrieves all transactions for a specific blueprint.");

/// <summary>
/// Get transaction statistics
/// </summary>
queryGroup.MapGet("/stats", async (
    QueryManager manager,
    string registerId) =>
{
    var stats = await manager.GetTransactionStatisticsAsync(registerId);
    return Results.Ok(stats);
})
.WithName("GetTransactionStatistics")
.WithSummary("Get transaction statistics")
.WithDescription("Retrieves comprehensive statistics for a register.");

/// <summary>
/// Query transactions by previous transaction ID (for fork detection and chain traversal)
/// </summary>
queryGroup.MapGet("/previous/{prevTxId}/transactions", async (
    QueryManager manager,
    string prevTxId,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? registerId,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "$skip")] int? skip,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "$top")] int? top,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "$count")] bool? count) =>
{
    if (registerId is null)
    {
        return Results.BadRequest(new { error = "registerId is required" });
    }

    var odataSkip = skip ?? 0;
    var odataTop = top ?? 20;
    var page = odataTop > 0 ? (odataSkip / odataTop) + 1 : 1;

    var result = await manager.GetTransactionsByPrevTxIdPaginatedAsync(
        registerId,
        prevTxId,
        page,
        odataTop);
    return Results.Ok(result);
})
.WithName("GetTransactionsByPrevTxId")
.WithSummary("Query transactions by previous transaction ID")
.WithDescription("Retrieves all transactions that reference a given previous transaction ID. Used for fork detection and chain integrity auditing.");

// ===========================
// Docket Management API
// ===========================

var docketsGroup = app.MapGroup("/api/registers/{registerId}/dockets")
    .WithTags("Dockets")
    .RequireAuthorization("CanReadTransactions");

/// <summary>
/// Get all dockets for a register
/// </summary>
docketsGroup.MapGet("/", async (
    IRegisterRepository repository,
    string registerId) =>
{
    var dockets = await repository.GetDocketsAsync(registerId);
    return Results.Ok(dockets);
})
.WithName("GetDockets")
.WithSummary("Get all dockets")
.WithDescription("Retrieves all dockets for a register.");

/// <summary>
/// Get docket by ID
/// </summary>
docketsGroup.MapGet("/{docketId}", async (
    IRegisterRepository repository,
    string registerId,
    ulong docketId) =>
{
    var docket = await repository.GetDocketAsync(registerId, docketId);
    return docket is not null ? Results.Ok(docket) : Results.NotFound();
})
.WithName("GetDocket")
.WithSummary("Get docket by ID")
.WithDescription("Retrieves a specific docket by its ID (docket height).");

/// <summary>
/// Get transactions in a docket
/// </summary>
docketsGroup.MapGet("/{docketId}/transactions", async (
    IRegisterRepository repository,
    string registerId,
    ulong docketId) =>
{
    var transactions = await repository.GetTransactionsByDocketAsync(registerId, docketId);
    return Results.Ok(transactions);
})
.WithName("GetDocketTransactions")
.WithSummary("Get docket transactions")
.WithDescription("Retrieves all transactions sealed in a specific docket.");

/// <summary>
/// Get the latest docket for a register
/// </summary>
docketsGroup.MapGet("/latest", async (
    IRegisterRepository repository,
    string registerId) =>
{
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
    {
        return Results.NotFound(new { error = "Register not found" });
    }

    if (register.Height == 0)
    {
        return Results.Ok<Docket?>(null);
    }

    // Height is count-based (1 = genesis docket written, 2 = two dockets, etc.)
    // Latest docket ID = Height - 1
    var docket = await repository.GetDocketAsync(registerId, (ulong)(register.Height - 1));
    return docket is not null ? Results.Ok(docket) : Results.NotFound();
})
.WithName("GetLatestDocket")
.WithSummary("Get latest docket")
.WithDescription("Retrieves the most recent docket (block) for a register.");

/// <summary>
/// Write a confirmed docket to the register (Validator Service only)
/// </summary>
docketsGroup.MapPost("/", async (
    IRegisterRepository repository,
    string registerId,
    WriteDocketRequest request) =>
{
    // Validate register exists
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
    {
        return Results.NotFound(new { error = "Register not found" });
    }

    // Create docket from request
    var docket = new Docket
    {
        Id = (ulong)request.DocketNumber,
        RegisterId = registerId,
        PreviousHash = request.PreviousHash ?? string.Empty,
        Hash = request.DocketHash,
        TransactionIds = request.TransactionIds,
        TimeStamp = request.CreatedAt.UtcDateTime,
        State = DocketState.Sealed,
        MetaData = new TransactionMetaData
        {
            RegisterId = registerId
        },
        Votes = request.ProposerValidatorId
    };

    // Insert transaction documents if provided
    if (request.Transactions is not null && request.Transactions.Any())
    {
        var participantIndex = app.Services.GetRequiredService<ParticipantIndexService>();

        foreach (var tx in request.Transactions)
        {
            // Set docket number for each transaction
            tx.DocketNumber = (ulong)request.DocketNumber;
            try
            {
                await repository.InsertTransactionAsync(tx);
            }
            catch (MongoDB.Driver.MongoWriteException ex) when (ex.WriteError?.Category == MongoDB.Driver.ServerErrorCategory.DuplicateKey)
            {
                // Transaction already exists (e.g., genesis transactions stored during register creation).
                // This is expected for docket write-back of transactions that were pre-persisted.
            }

            // Index participant transactions for fast address/ID lookups
            if (tx.MetaData?.TransactionType == TransactionType.Participant &&
                tx.Payloads.Length > 0 && !string.IsNullOrEmpty(tx.Payloads[0].Data))
            {
                try
                {
                    var payloadJson = System.Text.Encoding.UTF8.GetString(
                        Sorcha.TransactionHandler.Services.ContentEncodings.DecodeBase64Auto(tx.Payloads[0].Data));
                    var payloadElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(payloadJson);
                    participantIndex.IndexParticipant(registerId, tx.TxId, payloadElement, tx.TimeStamp);
                }
                catch (Exception ex)
                {
                    app.Logger.LogWarning(ex, "Failed to index participant TX {TxId}", tx.TxId);
                }
            }
        }
    }

    // Insert docket (handle idempotent retries)
    Docket inserted;
    try
    {
        inserted = await repository.InsertDocketAsync(docket);
    }
    catch (MongoDB.Driver.MongoWriteException ex) when (ex.WriteError?.Category == MongoDB.Driver.ServerErrorCategory.DuplicateKey)
    {
        // Docket already written (idempotent retry from Validator). Return success.
        inserted = docket;
    }

    // Update register height (height = number of dockets written, i.e., DocketNumber + 1)
    await repository.UpdateRegisterHeightAsync(registerId, (uint)(request.DocketNumber + 1));

    return Results.Created($"/api/registers/{registerId}/dockets/{inserted.Id}", inserted);
})
.WithName("WriteDocket")
.WithSummary("Write a confirmed docket")
.WithDescription("Writes a consensus-confirmed docket to the register. Used by Validator Service.")
.RequireAuthorization("CanWriteDockets");

// ===========================
// Blueprint Publishing API
// ===========================

/// <summary>
/// Publish a blueprint to a register
/// </summary>
app.MapPost("/api/registers/{registerId}/blueprints/publish", async (
    string registerId,
    PublishBlueprintToRegisterRequest request,
    IRegisterRepository repository,
    SystemRegisterService systemRegister,
    IHashProvider hashProvider,
    Sorcha.Register.Core.Services.IGovernanceRosterService rosterService,
    Sorcha.ServiceClients.Validator.IValidatorServiceClient validatorClient,
    ISystemWalletSigningService signingService) =>
{
    // Verify register exists
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
    {
        return Results.NotFound(new { error = $"Register '{registerId}' not found" });
    }

    // Verify caller has publishing rights via governance roster
    var roster = await rosterService.GetCurrentRosterAsync(registerId);
    if (roster != null)
    {
        var hasPublishRights = roster.ControlRecord.Attestations.Any(a =>
            a.Role.ToString() is "Owner" or "Admin" or "Designer");
        if (!hasPublishRights)
        {
            return Results.Forbid();
        }
    }

    // Publish to system register (global catalog) — idempotent: skip if already exists
    var bsonDocument = MongoDB.Bson.BsonDocument.Parse(request.BlueprintJson);
    long systemVersion = 0;
    var existingEntry = await systemRegister.GetBlueprintAsync(request.BlueprintId);
    if (existingEntry is null)
    {
        var entry = await systemRegister.PublishBlueprintAsync(
            request.BlueprintId, bsonDocument, request.PublishedBy);
        systemVersion = entry.Version;
    }
    else
    {
        systemVersion = existingEntry.Version;
    }

    // Submit a Control transaction to the validator for validation and docket creation.
    // All transactions must go through the validator — never write directly to the register.
    //
    // CRITICAL: Compute payload hash using the same canonical serialization the Validator uses.
    // The Validator re-serializes transaction.Payload with CanonicalJsonOptions before hashing,
    // so we must hash the same canonical form — NOT the raw request JSON string.
    var controlRecordElement = System.Text.Json.JsonDocument.Parse(request.BlueprintJson).RootElement;
    var canonicalJsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    var canonicalJson = System.Text.Json.JsonSerializer.Serialize(controlRecordElement, canonicalJsonOptions);
    var blueprintBytes = System.Text.Encoding.UTF8.GetBytes(canonicalJson);
    var payloadHash = hashProvider.ComputeHash(blueprintBytes, Sorcha.Cryptography.Enums.HashType.SHA256);
    var payloadHashHex = Convert.ToHexString(payloadHash).ToLowerInvariant();

    // Deterministic TxId so re-publishing the same blueprint is idempotent
    var txIdSource = System.Text.Encoding.UTF8.GetBytes($"blueprint-publish-{registerId}-{request.BlueprintId}");
    var txIdHash = hashProvider.ComputeHash(txIdSource, Sorcha.Cryptography.Enums.HashType.SHA256);
    var txId = Convert.ToHexString(txIdHash).ToLowerInvariant();

    // Chain linking: Blueprint publish PrevTxId = latest Control TX on this register.
    // All transactions except genesis must chain from a predecessor. Blueprint publish
    // transactions are Control transactions that chain from the governance control chain
    // (the genesis TX or the most recent governance/blueprint-publish Control TX).
    string? previousControlTxId = null;
    if (roster != null)
    {
        previousControlTxId = roster.LastControlTxId;
    }

    var signResult = await signingService.SignAsync(
        registerId: registerId,
        txId: txId,
        payloadHash: payloadHashHex,
        derivationPath: "sorcha:register-control",
        transactionType: "Control");

    var systemSignature = new Sorcha.ServiceClients.Validator.SignatureInfo
    {
        PublicKey = Base64Url.EncodeToString(signResult.PublicKey),
        SignatureValue = Base64Url.EncodeToString(signResult.Signature),
        Algorithm = signResult.Algorithm
    };

    var submission = new Sorcha.ServiceClients.Validator.TransactionSubmission
    {
        TransactionId = txId,
        RegisterId = registerId,
        BlueprintId = request.BlueprintId,
        ActionId = "blueprint-publish",
        Payload = controlRecordElement,
        PayloadHash = payloadHashHex,
        PreviousTransactionId = previousControlTxId,
        Signatures = new List<Sorcha.ServiceClients.Validator.SignatureInfo> { systemSignature },
        CreatedAt = DateTimeOffset.UtcNow,
        Metadata = new Dictionary<string, string>
        {
            ["Type"] = "Control",
            ["transactionType"] = "BlueprintPublish",
            ["publishedBy"] = request.PublishedBy,
            ["SystemWalletAddress"] = signResult.WalletAddress
        }
    };

    var submissionResult = await validatorClient.SubmitTransactionAsync(submission);
    if (!submissionResult.Success)
    {
        return Results.Problem(
            title: "Validator submission failed",
            detail: submissionResult.ErrorMessage ?? "The validator service rejected the blueprint publish transaction. Check validator logs.",
            statusCode: 502);
    }

    return Results.Ok(new
    {
        blueprintId = request.BlueprintId,
        registerId,
        txId,
        version = systemVersion,
        submitted = true
    });
})
.WithTags("Blueprints")
.WithName("PublishBlueprintToRegister")
.WithSummary("Publish a blueprint to a register")
.WithDescription("Publishes a blueprint to a specific register after verifying governance rights.")
.RequireAuthorization("CanSubmitTransactions");

// ===========================
// Governance API
// ===========================

var governanceGroup = app.MapGroup("/api/registers/{registerId}/governance")
    .WithTags("Governance")
    .RequireAuthorization("CanReadTransactions");

/// <summary>
/// Get the current admin roster for a register
/// </summary>
governanceGroup.MapGet("/roster", async (
    Sorcha.Register.Core.Services.IGovernanceRosterService rosterService,
    string registerId) =>
{
    var roster = await rosterService.GetCurrentRosterAsync(registerId);
    if (roster == null)
    {
        return Results.NotFound(new { error = $"No governance roster found for register '{registerId}'" });
    }

    return Results.Ok(new
    {
        roster.RegisterId,
        Members = roster.ControlRecord.Attestations.Select(a => new
        {
            a.Subject,
            Role = a.Role.ToString(),
            a.Algorithm,
            a.GrantedAt
        }),
        MemberCount = roster.ControlRecord.Attestations.Count,
        roster.ControlTransactionCount,
        roster.LastControlTxId
    });
})
.WithName("GetGovernanceRoster")
.WithSummary("Get current admin roster")
.WithDescription("Reconstructs the current admin roster by replaying all Control transactions for the register.");

/// <summary>
/// Get governance history (Control transactions)
/// </summary>
governanceGroup.MapGet("/history", async (
    IRegisterRepository repository,
    string registerId,
    int page = 1,
    int pageSize = 20) =>
{
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
    {
        return Results.NotFound(new { error = "Register not found" });
    }

    var transactions = await repository.GetTransactionsAsync(registerId);
    var controlTxs = transactions
        .Where(t => t.MetaData != null && t.MetaData.TransactionType == Sorcha.Register.Models.Enums.TransactionType.Control)
        .OrderByDescending(t => t.DocketNumber ?? 0)
        .ToList();

    var pagedTxs = controlTxs
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToList();

    return Results.Ok(new
    {
        Page = page,
        PageSize = pageSize,
        Total = controlTxs.Count,
        Transactions = pagedTxs
    });
})
.WithName("GetGovernanceHistory")
.WithSummary("Get governance history")
.WithDescription("Retrieves paginated Control transactions that make up the governance history for a register.");

/// <summary>
/// Submit a governance proposal (add/remove member, transfer ownership)
/// </summary>
governanceGroup.MapPost("/propose", async (
    string registerId,
    GovernanceProposalRequest request,
    IRegisterRepository repository,
    Sorcha.Register.Core.Services.IGovernanceRosterService rosterService,
    IHashProvider hashProvider,
    Sorcha.ServiceClients.Validator.IValidatorServiceClient validatorClient,
    ISystemWalletSigningService signingService) =>
{
    // 1. Verify register exists
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
    {
        return Results.NotFound(new { error = $"Register '{registerId}' not found" });
    }

    // 2. Reconstruct current roster
    var roster = await rosterService.GetCurrentRosterAsync(registerId);
    if (roster == null)
    {
        return Results.Problem(
            title: "No governance roster",
            detail: $"Register '{registerId}' has no governance roster. A genesis Control transaction is required first.",
            statusCode: 422);
    }

    // 3. Build governance operation from request
    var operation = new GovernanceOperation
    {
        OperationType = request.OperationType,
        ProposerDid = request.ProposerDid,
        TargetDid = request.TargetDid,
        TargetRole = request.TargetRole ?? RegisterRole.Admin,
        ApprovalSignatures = request.ApprovalSignatures ?? [],
        ProposedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        Status = ProposalStatus.Pending,
        Justification = request.Justification
    };

    // 4. Validate proposal against current roster
    var validationResult = rosterService.ValidateProposal(roster, operation);
    if (!validationResult.IsValid)
    {
        return Results.BadRequest(new
        {
            error = "Governance proposal validation failed",
            errors = validationResult.Errors
        });
    }

    // 5. Validate quorum (owner override for Add/Remove, quorum required for Transfer)
    var quorumResult = await rosterService.ValidateQuorumAsync(
        registerId, operation, operation.ApprovalSignatures);
    if (!quorumResult.IsQuorumMet)
    {
        return Results.BadRequest(new
        {
            error = "Quorum not met",
            votesRequired = quorumResult.VotesRequired,
            votesReceived = quorumResult.VotesReceived,
            votingPool = quorumResult.VotingPool,
            isOwnerOverride = quorumResult.IsOwnerOverride
        });
    }

    // 6. Apply operation to produce updated roster
    RegisterAttestation? newAttestation = null;
    if (operation.OperationType == GovernanceOperationType.Add)
    {
        newAttestation = new RegisterAttestation
        {
            Role = operation.TargetRole,
            Subject = operation.TargetDid,
            PublicKey = string.Empty,
            Signature = string.Empty,
            Algorithm = Sorcha.Register.Models.SignatureAlgorithm.ED25519,
            GrantedAt = DateTimeOffset.UtcNow
        };
    }

    operation.Status = ProposalStatus.Approved;
    var updatedRoster = rosterService.ApplyOperation(
        roster.ControlRecord, operation, newAttestation);

    // 7. Build ControlTransactionPayload
    var payload = new ControlTransactionPayload
    {
        Version = 1,
        Roster = updatedRoster,
        Operation = operation
    };

    // 8. Canonical JSON serialization for deterministic hashing
    var canonicalJsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload, canonicalJsonOptions);
    var payloadElement = System.Text.Json.JsonDocument.Parse(payloadJson).RootElement;
    var canonicalJson = System.Text.Json.JsonSerializer.Serialize(payloadElement, canonicalJsonOptions);
    var payloadBytes = System.Text.Encoding.UTF8.GetBytes(canonicalJson);
    var payloadHash = hashProvider.ComputeHash(payloadBytes, Sorcha.Cryptography.Enums.HashType.SHA256);
    var payloadHashHex = Convert.ToHexString(payloadHash).ToLowerInvariant();

    // 9. Deterministic TxId for idempotency
    var opType = operation.OperationType.ToString().ToLowerInvariant();
    var txIdSource = System.Text.Encoding.UTF8.GetBytes(
        $"governance-{opType}-{registerId}-{operation.ProposerDid}-{operation.TargetDid}-{operation.ProposedAt:O}");
    var txIdHash = hashProvider.ComputeHash(txIdSource, Sorcha.Cryptography.Enums.HashType.SHA256);
    var txId = Convert.ToHexString(txIdHash).ToLowerInvariant();

    // 10. Chain linking from latest Control TX
    string? previousControlTxId = roster.LastControlTxId;

    // 11. Sign with system wallet
    var signResult = await signingService.SignAsync(
        registerId: registerId,
        txId: txId,
        payloadHash: payloadHashHex,
        derivationPath: "sorcha:register-control",
        transactionType: "Control");

    var systemSignature = new Sorcha.ServiceClients.Validator.SignatureInfo
    {
        PublicKey = Base64Url.EncodeToString(signResult.PublicKey),
        SignatureValue = Base64Url.EncodeToString(signResult.Signature),
        Algorithm = signResult.Algorithm
    };

    // 12. Submit as Control TX via validator
    var submission = new Sorcha.ServiceClients.Validator.TransactionSubmission
    {
        TransactionId = txId,
        RegisterId = registerId,
        BlueprintId = string.Empty,
        ActionId = $"governance-{opType}",
        Payload = payloadElement,
        PayloadHash = payloadHashHex,
        PreviousTransactionId = previousControlTxId,
        Signatures = new List<Sorcha.ServiceClients.Validator.SignatureInfo> { systemSignature },
        CreatedAt = DateTimeOffset.UtcNow,
        Metadata = new Dictionary<string, string>
        {
            ["Type"] = "Control",
            ["transactionType"] = "GovernanceOperation",
            ["operationType"] = opType,
            ["proposerDid"] = operation.ProposerDid,
            ["targetDid"] = operation.TargetDid,
            ["SystemWalletAddress"] = signResult.WalletAddress
        }
    };

    var submissionResult = await validatorClient.SubmitTransactionAsync(submission);
    if (!submissionResult.Success)
    {
        return Results.Problem(
            title: "Validator submission failed",
            detail: submissionResult.ErrorMessage ?? "The validator rejected the governance transaction.",
            statusCode: 502);
    }

    return Results.Ok(new
    {
        txId,
        registerId,
        operationType = opType,
        proposerDid = operation.ProposerDid,
        targetDid = operation.TargetDid,
        targetRole = operation.TargetRole.ToString(),
        quorum = new
        {
            quorumResult.IsQuorumMet,
            quorumResult.VotesRequired,
            quorumResult.VotesReceived,
            quorumResult.IsOwnerOverride
        },
        submitted = true
    });
})
.WithName("ProposeGovernanceOperation")
.WithSummary("Submit a governance proposal")
.WithDescription("Submits a governance operation (Add, Remove, Transfer) as a Control transaction. Owner can Add/Remove without quorum. Transfer requires quorum.")
.RequireAuthorization("CanSubmitTransactions");

/// <summary>
/// List governance proposals from Control TX history
/// </summary>
governanceGroup.MapGet("/proposals", async (
    IRegisterRepository repository,
    Sorcha.Register.Core.Services.IGovernanceRosterService rosterService,
    string registerId,
    int page = 1,
    int pageSize = 20) =>
{
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
    {
        return Results.NotFound(new { error = "Register not found" });
    }

    // Get all Control transactions and extract those with governance operations
    var transactions = await repository.GetTransactionsAsync(registerId);
    var controlTxs = transactions
        .Where(t => t.MetaData != null && t.MetaData.TransactionType == TransactionType.Control)
        .OrderByDescending(t => t.DocketNumber ?? 0)
        .ToList();

    // Filter to Control TXs that have governance operation metadata
    var governanceProposals = controlTxs
        .Where(t => t.MetaData?.TrackingData != null
            && t.MetaData.TrackingData.ContainsKey("transactionType")
            && t.MetaData.TrackingData["transactionType"] == "GovernanceOperation")
        .ToList();

    var total = governanceProposals.Count;
    var pagedTxs = governanceProposals
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(t => new
        {
            t.TxId,
            t.DocketNumber,
            t.TimeStamp,
            OperationType = t.MetaData?.TrackingData?.GetValueOrDefault("operationType"),
            ProposerDid = t.MetaData?.TrackingData?.GetValueOrDefault("proposerDid"),
            TargetDid = t.MetaData?.TrackingData?.GetValueOrDefault("targetDid")
        })
        .ToList();

    return Results.Ok(new
    {
        Page = page,
        PageSize = pageSize,
        Total = total,
        Proposals = pagedTxs
    });
})
.WithName("GetGovernanceProposals")
.WithSummary("List governance proposals")
.WithDescription("Returns paginated governance operations from Control transaction history.");

// ===========================
// Crypto Policy API
// ===========================

var cryptoPolicyGroup = app.MapGroup("/api/registers/{registerId}/crypto-policy")
    .WithTags("CryptoPolicy")
    .RequireAuthorization("CanReadTransactions");

/// <summary>
/// Get the active crypto policy for a register
/// </summary>
cryptoPolicyGroup.MapGet("/", async (
    Sorcha.Register.Service.Services.CryptoPolicyService cryptoPolicyService,
    string registerId,
    CancellationToken ct) =>
{
    var policy = await cryptoPolicyService.GetActivePolicyAsync(registerId, ct);
    return Results.Ok(policy);
})
.WithName("GetActiveCryptoPolicy")
.WithSummary("Get active crypto policy")
.WithDescription("Returns the active cryptographic policy for this register. If no explicit policy has been set, returns the default permissive policy accepting all algorithms.");

/// <summary>
/// Get crypto policy version history for a register
/// </summary>
cryptoPolicyGroup.MapGet("/history", async (
    Sorcha.Register.Service.Services.CryptoPolicyService cryptoPolicyService,
    string registerId,
    CancellationToken ct) =>
{
    var history = await cryptoPolicyService.GetPolicyHistoryAsync(registerId, ct);
    return Results.Ok(new { Versions = history, Total = history.Count });
})
.WithName("GetCryptoPolicyHistory")
.WithSummary("Get crypto policy version history")
.WithDescription("Returns all crypto policy versions for this register, ordered by version number. Includes the genesis policy and all subsequent updates.");

/// <summary>
/// Submit a crypto policy update as a control transaction
/// </summary>
governanceGroup.MapPost("/crypto-policy", async (
    Sorcha.Register.Service.Services.CryptoPolicyService cryptoPolicyService,
    Sorcha.Register.Core.Managers.TransactionManager transactionManager,
    Sorcha.ServiceClients.SystemWallet.ISystemWalletSigningService systemSigning,
    string registerId,
    Sorcha.Register.Models.CryptoPolicy policyUpdate,
    CancellationToken ct) =>
{
    // Validate the policy
    if (!policyUpdate.IsValid())
    {
        return Results.BadRequest(new { Error = "Invalid crypto policy: RequiredSignatureAlgorithms must be a subset of AcceptedSignatureAlgorithms, and all algorithm arrays must be non-empty." });
    }

    // Serialize policy as payload
    var policyJson = System.Text.Json.JsonSerializer.Serialize(policyUpdate);
    var policyBytes = System.Text.Encoding.UTF8.GetBytes(policyJson);
    var payloadData = Convert.ToBase64String(policyBytes);
    var payloadHash = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(policyBytes)).ToLowerInvariant();

    // Generate TX ID
    var txIdSource = $"crypto-policy-update-{registerId}-v{policyUpdate.Version}";
    var txIdBytes = System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(txIdSource));
    var txId = Convert.ToHexString(txIdBytes).ToLowerInvariant();

    // Find chain head
    var allTxs = await transactionManager.GetTransactionsAsync(registerId, ct);
    var chainHead = allTxs.OrderByDescending(t => t.TimeStamp).FirstOrDefault();

    // Build control transaction
    var tx = new Sorcha.Register.Models.TransactionModel
    {
        TxId = txId,
        RegisterId = registerId,
        SenderWallet = "system",
        PrevTxId = chainHead?.TxId ?? string.Empty,
        PayloadCount = 1,
        Payloads = new[]
        {
            new Sorcha.Register.Models.PayloadModel
            {
                Data = payloadData,
                Hash = payloadHash,
                WalletAccess = Array.Empty<string>(),
                ContentType = "application/json",
                ContentEncoding = "base64"
            }
        },
        TimeStamp = DateTime.UtcNow,
        Signature = string.Empty,
        MetaData = new Sorcha.Register.Models.TransactionMetaData
        {
            RegisterId = registerId,
            TransactionType = Sorcha.Register.Models.Enums.TransactionType.Control,
            TrackingData = new Dictionary<string, string>
            {
                ["transactionType"] = "CryptoPolicyUpdate",
                ["policyVersion"] = policyUpdate.Version.ToString()
            }
        }
    };

    // Sign with system wallet (follows same pattern as RegisterCreationOrchestrator)
    var signResult = await systemSigning.SignAsync(
        registerId: registerId,
        txId: txId,
        payloadHash: payloadHash,
        derivationPath: "sorcha:register-control",
        transactionType: "CryptoPolicyUpdate",
        cancellationToken: ct);
    tx.Signature = Convert.ToBase64String(signResult.Signature);

    // Submit
    await transactionManager.StoreTransactionAsync(tx, ct);

    return Results.Ok(new { TxId = txId, PolicyVersion = policyUpdate.Version, Status = "submitted" });
})
.WithName("UpdateCryptoPolicy")
.WithSummary("Update register crypto policy")
.WithDescription("Submits a crypto policy update as a control transaction. The new policy takes effect immediately for subsequent transactions.");

// ===========================
// Participant Query API
// ===========================

var participantsGroup = app.MapGroup("/api/registers/{registerId}/participants")
    .WithTags("Participants")
    .RequireAuthorization("CanReadTransactions");

/// <summary>
/// List published participants on a register
/// </summary>
participantsGroup.MapGet("/", (
    ParticipantIndexService index,
    string registerId,
    int skip = 0,
    int top = 20,
    string? status = "active") =>
{
    var page = index.List(registerId, skip, top, status);
    return Results.Ok(page);
})
.WithName("ListParticipants")
.WithSummary("List published participants")
.WithDescription("Returns a paginated list of published participant records on this register. Defaults to active participants only. Use status=all to include deprecated/revoked.");

/// <summary>
/// Look up a participant by wallet address
/// </summary>
participantsGroup.MapGet("/by-address/{walletAddress}", (
    ParticipantIndexService index,
    string registerId,
    string walletAddress) =>
{
    var record = index.GetByAddress(registerId, walletAddress);
    return record is not null ? Results.Ok(record) : Results.NotFound(new { error = "No participant found for this wallet address" });
})
.WithName("GetParticipantByAddress")
.WithSummary("Look up participant by wallet address")
.WithDescription("Returns the published participant record that owns the specified wallet address on this register.");

/// <summary>
/// Get a participant by ID
/// </summary>
participantsGroup.MapGet("/{participantId}", (
    ParticipantIndexService index,
    string registerId,
    string participantId) =>
{
    var record = index.GetById(registerId, participantId);
    return record is not null ? Results.Ok(record) : Results.NotFound(new { error = "Participant not found" });
})
.WithName("GetParticipantById")
.WithSummary("Get participant by ID")
.WithDescription("Returns the latest published version of a participant record by participant ID.");

/// <summary>
/// Resolve a participant's public key by wallet address
/// </summary>
participantsGroup.MapGet("/by-address/{walletAddress}/public-key", (
    ParticipantIndexService index,
    string registerId,
    string walletAddress,
    string? algorithm = null) =>
{
    var record = index.GetByAddress(registerId, walletAddress);
    if (record == null)
        return Results.NotFound(new { error = "No participant found for this wallet address" });

    // Revoked participants return 410 Gone
    if (string.Equals(record.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Problem(
            statusCode: StatusCodes.Status410Gone,
            title: "Participant Revoked",
            detail: $"Participant '{record.ParticipantId}' has been revoked");
    }

    // Find the matching address entry
    var addressInfo = !string.IsNullOrEmpty(algorithm)
        ? record.Addresses.FirstOrDefault(a => string.Equals(a.Algorithm, algorithm, StringComparison.OrdinalIgnoreCase))
        : record.Addresses.FirstOrDefault(a => a.Primary) ?? record.Addresses.FirstOrDefault();

    if (addressInfo == null)
        return Results.NotFound(new { error = $"No address found with algorithm '{algorithm}'" });

    return Results.Ok(new Sorcha.ServiceClients.Register.Models.PublicKeyResolution
    {
        ParticipantId = record.ParticipantId,
        ParticipantName = record.ParticipantName,
        WalletAddress = addressInfo.WalletAddress,
        PublicKey = addressInfo.PublicKey,
        Algorithm = addressInfo.Algorithm,
        Status = record.Status
    });
})
.WithName("ResolvePublicKey")
.WithSummary("Resolve public key by wallet address")
.WithDescription("Returns the public key for field-level encryption. Returns 410 Gone if participant is revoked.");

/// <summary>
/// Batch resolve public keys for multiple wallet addresses
/// </summary>
participantsGroup.MapPost("/resolve-public-keys", (
    ParticipantIndexService index,
    string registerId,
    Sorcha.ServiceClients.Register.Models.BatchPublicKeyRequest request) =>
{
    // Validate request
    if (request.WalletAddresses == null || request.WalletAddresses.Length == 0)
        return Results.BadRequest(new { error = "walletAddresses must contain at least one address" });

    if (request.WalletAddresses.Length > 200)
        return Results.BadRequest(new { error = "Maximum 200 addresses per request" });

    var resolved = new Dictionary<string, Sorcha.ServiceClients.Register.Models.PublicKeyResolution>();
    var notFound = new List<string>();
    var revoked = new List<string>();

    foreach (var walletAddress in request.WalletAddresses.Distinct())
    {
        var record = index.GetByAddress(registerId, walletAddress);
        if (record == null)
        {
            notFound.Add(walletAddress);
            continue;
        }

        if (string.Equals(record.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
        {
            revoked.Add(walletAddress);
            continue;
        }

        // Find matching address entry (same logic as single resolve)
        var addressInfo = !string.IsNullOrEmpty(request.Algorithm)
            ? record.Addresses.FirstOrDefault(a => string.Equals(a.Algorithm, request.Algorithm, StringComparison.OrdinalIgnoreCase))
            : record.Addresses.FirstOrDefault(a => a.Primary) ?? record.Addresses.FirstOrDefault();

        if (addressInfo == null)
        {
            notFound.Add(walletAddress);
            continue;
        }

        resolved[walletAddress] = new Sorcha.ServiceClients.Register.Models.PublicKeyResolution
        {
            ParticipantId = record.ParticipantId,
            ParticipantName = record.ParticipantName,
            WalletAddress = addressInfo.WalletAddress,
            PublicKey = addressInfo.PublicKey,
            Algorithm = addressInfo.Algorithm,
            Status = record.Status
        };
    }

    return Results.Ok(new Sorcha.ServiceClients.Register.Models.BatchPublicKeyResponse
    {
        Resolved = resolved,
        NotFound = notFound.ToArray(),
        Revoked = revoked.ToArray()
    });
})
.WithName("ResolvePublicKeysBatch")
.WithSummary("Batch resolve public keys")
.WithDescription("Resolves public keys for multiple wallet addresses. Returns resolved, not-found, and revoked addresses separately. Max 200 addresses per request.");

// ===========================
// Zero-Knowledge Proof API
// ===========================

var proofsGroup = app.MapGroup("/api/registers/{registerId}/proofs")
    .WithTags("ZK Proofs")
    .RequireAuthorization("CanReadTransactions");

/// <summary>
/// Generate a ZK inclusion proof for a transaction in a docket
/// </summary>
proofsGroup.MapPost("/inclusion", async (
    IRegisterRepository repository,
    IHashProvider hashProvider,
    string registerId,
    InclusionProofRequest request) =>
{
    // Validate register exists
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
        return Results.NotFound(new { error = "Register not found" });

    // Validate TxId format (64-char hex SHA-256)
    if (string.IsNullOrWhiteSpace(request.TxId) || request.TxId.Length != 64)
        return Results.BadRequest(new { error = "TxId must be a 64-character hex string (SHA-256)" });

    // Validate docket exists
    if (string.IsNullOrWhiteSpace(request.DocketId))
        return Results.BadRequest(new { error = "DocketId is required" });

    var dockets = await repository.GetDocketsAsync(registerId);
    var docket = dockets.FirstOrDefault(d => d.Id.ToString() == request.DocketId);
    if (docket == null)
        return Results.NotFound(new { error = $"Docket {request.DocketId} not found" });

    // Verify the transaction is in the docket
    var txIds = docket.TransactionIds?.ToList() ?? [];
    if (!txIds.Contains(request.TxId, StringComparer.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Transaction not found in specified docket" });

    // Build Merkle tree and generate proof path
    var merkleTree = new Sorcha.Cryptography.Utilities.MerkleTree(hashProvider);
    var merkleRoot = merkleTree.ComputeMerkleRoot(txIds.AsReadOnly());
    var proofPath = BuildMerkleProofPath(txIds, request.TxId, hashProvider);

    // Generate ZK inclusion proof
    var txHash = Convert.FromHexString(request.TxId);
    var rootBytes = Convert.FromHexString(merkleRoot);
    var proofPathBytes = proofPath.Select(p => Convert.FromHexString(p)).ToArray();

    var zkProvider = new Sorcha.Cryptography.Core.ZKInclusionProofProvider();
    var proof = zkProvider.GenerateInclusionProof(txHash, rootBytes, proofPathBytes, request.DocketId);

    return Results.Ok(new
    {
        RegisterId = registerId,
        DocketId = request.DocketId,
        TxId = request.TxId,
        MerkleRoot = merkleRoot,
        Commitment = Convert.ToBase64String(proof.Commitment),
        ProofData = Convert.ToBase64String(proof.ProofData),
        MerkleProofPath = proofPathBytes.Select(Convert.ToBase64String).ToArray(),
        VerificationKey = Convert.ToBase64String(proof.VerificationKey)
    });
})
.WithName("GenerateInclusionProof")
.WithSummary("Generate ZK inclusion proof")
.WithDescription("Generates a zero-knowledge proof that a transaction is included in a docket's Merkle tree without revealing the transaction content.");

/// <summary>
/// Verify a ZK inclusion proof
/// </summary>
proofsGroup.MapPost("/verify-inclusion", (
    VerifyInclusionProofRequest request) =>
{
    try
    {
        var proof = new Sorcha.Cryptography.Models.ZKInclusionProof
        {
            DocketId = request.DocketId,
            MerkleRoot = Convert.FromBase64String(request.MerkleRoot),
            Commitment = Convert.FromBase64String(request.Commitment),
            ProofData = Convert.FromBase64String(request.ProofData),
            MerkleProofPath = request.MerkleProofPath.Select(Convert.FromBase64String).ToArray(),
            VerificationKey = Convert.FromBase64String(request.VerificationKey)
        };

        var zkProvider = new Sorcha.Cryptography.Core.ZKInclusionProofProvider();
        var result = zkProvider.VerifyInclusionProof(proof);

        return Results.Ok(new
        {
            IsValid = result.IsValid,
            Message = result.Message,
            DocketId = request.DocketId
        });
    }
    catch (FormatException)
    {
        return Results.BadRequest(new { error = "Invalid base64 encoding in proof fields" });
    }
})
.WithName("VerifyInclusionProof")
.WithSummary("Verify ZK inclusion proof")
.WithDescription("Verifies a zero-knowledge proof of transaction inclusion without access to the original transaction data.");

// ===========================
// Admin / Diagnostic Endpoints
// ===========================

var adminGroup = app.MapGroup("/api/admin/registers/{registerId}")
    .WithTags("Admin");

/// <summary>
/// Detect orphan transactions (not referenced by any docket)
/// </summary>
adminGroup.MapGet("/orphan-transactions", async (
    IRegisterRepository repository,
    string registerId) =>
{
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
        return Results.NotFound(new { error = "Register not found" });

    // Get all dockets and collect their transaction IDs
    var dockets = await repository.GetDocketsAsync(registerId);
    var dockedTxIds = new HashSet<string>(
        dockets.SelectMany(d => d.TransactionIds ?? []),
        StringComparer.OrdinalIgnoreCase);

    // Get all transactions
    var allTxQueryable = await repository.GetTransactionsAsync(registerId);
    var allTransactions = allTxQueryable.ToList();

    // Orphans = transactions not referenced by any docket
    var orphans = allTransactions
        .Where(tx => !dockedTxIds.Contains(tx.TxId))
        .Select(tx => new
        {
            tx.TxId,
            tx.RegisterId,
            tx.DocketNumber,
            tx.SenderWallet,
            tx.TimeStamp,
            tx.PrevTxId,
            HasSignature = !string.IsNullOrEmpty(tx.Signature),
            MetadataType = tx.MetaData?.TransactionType.ToString(),
            PayloadCount = tx.PayloadCount
        })
        .ToList();

    return Results.Ok(new
    {
        RegisterId = registerId,
        TotalTransactions = allTransactions.Count,
        TotalDockets = dockets.Count(),
        DockedTransactionCount = dockedTxIds.Count,
        OrphanCount = orphans.Count,
        Orphans = orphans
    });
})
.WithName("DetectOrphanTransactions")
.WithSummary("Detect orphan transactions")
.WithDescription("Finds transactions not referenced by any sealed docket. These are remnants of legacy direct-write paths.")
.RequireAuthorization("CanWriteDockets");

/// <summary>
/// Delete orphan transactions (not referenced by any docket)
/// </summary>
adminGroup.MapDelete("/orphan-transactions", async (
    IRegisterRepository repository,
    string registerId) =>
{
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
        return Results.NotFound(new { error = "Register not found" });

    // Get all dockets and collect their transaction IDs
    var dockets = await repository.GetDocketsAsync(registerId);
    var dockedTxIds = new HashSet<string>(
        dockets.SelectMany(d => d.TransactionIds ?? []),
        StringComparer.OrdinalIgnoreCase);

    // Get all transactions
    var allTxQueryable = await repository.GetTransactionsAsync(registerId);
    var allTransactions = allTxQueryable.ToList();

    // Find orphans
    var orphanTxIds = allTransactions
        .Where(tx => !dockedTxIds.Contains(tx.TxId))
        .Select(tx => tx.TxId)
        .ToList();

    if (orphanTxIds.Count == 0)
        return Results.Ok(new { RegisterId = registerId, DeletedCount = 0, Message = "No orphan transactions found" });

    // Safety check: ensure no other transactions chain from orphans
    var chainedFromOrphans = allTransactions
        .Where(tx => tx.PrevTxId != null && orphanTxIds.Contains(tx.PrevTxId) && !orphanTxIds.Contains(tx.TxId))
        .Select(tx => new { tx.TxId, tx.PrevTxId })
        .ToList();

    if (chainedFromOrphans.Count > 0)
    {
        return Results.Conflict(new
        {
            error = "Cannot delete orphans — some docketed transactions chain from orphan PrevTxIds",
            ChainedTransactions = chainedFromOrphans,
            OrphanTxIds = orphanTxIds
        });
    }

    // Delete each orphan via DeleteTransactionAsync
    var deletedCount = 0;
    foreach (var txId in orphanTxIds)
    {
        try
        {
            await repository.DeleteTransactionAsync(registerId, txId);
            deletedCount++;
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Failed to delete orphan transaction {TxId}", txId);
        }
    }

    return Results.Ok(new
    {
        RegisterId = registerId,
        DeletedCount = deletedCount,
        OrphanTxIds = orphanTxIds
    });
})
.WithName("DeleteOrphanTransactions")
.WithSummary("Delete orphan transactions")
.WithDescription("Removes transactions not referenced by any sealed docket. Refuses if docketed transactions chain from orphans.")
.RequireAuthorization("CanWriteDockets");

// Local function: builds a Merkle proof path (sibling hashes) for a target transaction
List<string> BuildMerkleProofPath(List<string> txIds, string targetTxId, IHashProvider hashProvider)
{
    if (txIds.Count <= 1)
        return [];

    var proofPath = new List<string>();
    var currentLevel = txIds.Select(h => h.ToLowerInvariant()).ToList();
    int targetIdx = currentLevel.FindIndex(h => string.Equals(h, targetTxId, StringComparison.OrdinalIgnoreCase));
    if (targetIdx < 0)
        return [];

    while (currentLevel.Count > 1)
    {
        var nextLevel = new List<string>();
        int nextTargetIdx = targetIdx / 2;

        for (int i = 0; i < currentLevel.Count; i += 2)
        {
            string left = currentLevel[i];
            string right = (i + 1 < currentLevel.Count) ? currentLevel[i + 1] : left;

            // If target is in this pair, add sibling to proof path
            if (i == targetIdx || i + 1 == targetIdx)
            {
                proofPath.Add(i == targetIdx ? right : left);
            }

            // Compute parent hash (matches MerkleTree.CombineAndHash)
            string combined = left + right;
            byte[] combinedBytes = System.Text.Encoding.UTF8.GetBytes(combined);
            byte[] hash = hashProvider.ComputeHash(combinedBytes, Sorcha.Cryptography.Enums.HashType.SHA256);
            nextLevel.Add(Convert.ToHexString(hash).ToLowerInvariant());
        }

        currentLevel = nextLevel;
        targetIdx = nextTargetIdx;
    }

    return proofPath;
}

app.Run();

// ===========================
// Request/Response Models
// ===========================

record CreateRegisterRequest(
    string Name,
    string TenantId,
    bool Advertise = false,
    bool IsFullReplica = true);

record UpdateRegisterRequest(
    string? Name = null,
    RegisterStatus? Status = null,
    bool? Advertise = null);

record PublishBlueprintToRegisterRequest(
    string BlueprintId,
    string BlueprintJson,
    string PublishedBy);

record GovernanceProposalRequest(
    GovernanceOperationType OperationType,
    string ProposerDid,
    string TargetDid,
    RegisterRole? TargetRole = null,
    string? Justification = null,
    List<ApprovalSignature>? ApprovalSignatures = null);

record WriteDocketRequest(
    string DocketId,
    long DocketNumber,
    string? PreviousHash,
    string DocketHash,
    DateTimeOffset CreatedAt,
    List<string> TransactionIds,
    string ProposerValidatorId,
    string MerkleRoot,
    List<TransactionModel>? Transactions = null);

// ZK Proof request models
record InclusionProofRequest(string TxId, string DocketId);
record VerifyInclusionProofRequest(
    string DocketId,
    string MerkleRoot,
    string Commitment,
    string ProofData,
    string[] MerkleProofPath,
    string VerificationKey);
