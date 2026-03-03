// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Service.Extensions;
using Sorcha.Wallet.Service.Endpoints;
using Sorcha.Wallet.Service.GrpcServices;
using Sorcha.Wallet.Service.Services;
using Sorcha.ServiceClients.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (health checks, OpenTelemetry, service discovery)
builder.AddServiceDefaults();

// Add structured logging with Serilog (OPS-001)
builder.AddSerilogLogging();

// Add rate limiting (SEC-002)
builder.AddRateLimiting();

// Add input validation (SEC-003)
builder.AddInputValidation();

// Add Wallet Service infrastructure and domain services
builder.Services.AddWalletService(builder.Configuration);

// Add DID resolvers for credential verification
builder.Services.AddDidResolvers();

// Add presentation request service (OID4VP)
builder.Services.AddSingleton<IPresentationRequestService, PresentationRequestService>();

// Feature 047: Address registration service (US1) + notification pipeline (US2)
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IAddressRegistrationService,
    Sorcha.Wallet.Service.Services.Implementation.AddressRegistrationService>();
builder.Services.AddSingleton<Sorcha.Wallet.Service.Services.Interfaces.INotificationRateLimiter,
    Sorcha.Wallet.Service.Services.Implementation.NotificationRateLimiter>();
builder.Services.AddSingleton<Sorcha.Wallet.Service.Services.Interfaces.INotificationPreferenceProvider,
    Sorcha.Wallet.Service.Services.Implementation.DefaultNotificationPreferenceProvider>();
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.INotificationDeliveryService,
    Sorcha.Wallet.Service.Services.Implementation.NotificationDeliveryService>();

// Feature 047: Notification metrics (T047 — observability)
builder.Services.AddSingleton<Sorcha.Wallet.Service.Services.Implementation.NotificationMetrics>();

// Feature 047: Digest notification batching (US5)
builder.Services.AddHostedService<Sorcha.Wallet.Service.Services.Implementation.NotificationDigestWorker>();

// Add Redis for notification rate limiting and pub/sub
builder.AddRedisClient("redis");

// Add service clients for inter-service communication
builder.Services.AddServiceClients(builder.Configuration);

// Add gRPC services for inter-service communication (Validator, Peer, etc.)
builder.Services.AddGrpc();

// Add OpenAPI services with standard Sorcha metadata
builder.AddSorchaOpenApi("Sorcha Wallet Service API", "Cryptographic wallet management and transaction signing with HD wallets (BIP32/39/44), multi-algorithm support (ED25519, P-256, RSA-4096), and secure key storage.");

// Add Wallet Service health checks
builder.Services.AddHealthChecks()
    .AddWalletServiceHealthChecks(builder.Configuration);

// Configure CORS - production restriction handled at API Gateway (YARP)
builder.AddSorchaCors();

// Add JWT authentication and authorization (AUTH-002)
// JWT authentication is now configured via shared ServiceDefaults with auto-key generation
builder.AddJwtAuthentication();
builder.Services.AddWalletAuthorization();

var app = builder.Build();

// Apply database migrations automatically (only if PostgreSQL is configured)
await app.Services.ApplyWalletDatabaseMigrationsAsync();

// Map default Aspire endpoints (/health, /alive)
app.MapDefaultEndpoints();

// Add Serilog HTTP request logging (OPS-001)
app.UseSerilogLogging();

// Add OWASP security headers (SEC-004)
app.UseApiSecurityHeaders();

// Enable HTTPS enforcement with HSTS (SEC-001) -- must precede input validation
app.UseHttpsEnforcement();

// Enable input validation (SEC-003)
app.UseInputValidation();

// Configure OpenAPI and Scalar API documentation UI (development only)
app.MapSorchaOpenApiUi("Wallet Service");

// Enable CORS
app.UseCors();

// Add authentication and authorization middleware (AUTH-002)
app.UseAuthentication();
app.UseAuthorization();

// Enable rate limiting (SEC-002)
app.UseRateLimiting();

// Map gRPC services for inter-service communication
app.MapGrpcService<WalletGrpcService>();
app.MapGrpcService<WalletNotificationGrpcService>();

// Map Wallet API endpoints
app.MapWalletEndpoints();
app.MapDelegationEndpoints();
app.MapCredentialEndpoints();
app.MapPresentationEndpoints();

app.Run();

// Make the implicit Program class public for integration tests
public partial class Program { }
