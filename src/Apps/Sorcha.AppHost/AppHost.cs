// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;

var builder = DistributedApplication.CreateBuilder(args);

// Generate or retrieve a shared JWT signing key for all services
// This key is automatically generated on first run and persists in local app data
var jwtSigningKey = GetOrCreateJwtSigningKey();

// Add PostgreSQL for service databases
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin(); // Adds pgAdmin UI for development

var tenantDb = postgres.AddDatabase("tenant-db", "sorcha_tenant");
var walletDb = postgres.AddDatabase("wallet-db", "sorcha_wallet");

// Add MongoDB for Register Service transaction storage
var mongodb = builder.AddMongoDB("mongodb")
    .WithMongoExpress(); // Adds Mongo Express UI for development

var registerDb = mongodb.AddDatabase("register-db", "sorcha_register");

// Add Redis for distributed caching and output caching
var redis = builder.AddRedis("redis")
    .WithRedisCommander(); // Adds Redis Commander UI for development

// Add Tenant Service (authentication and authorization)
var tenantService = builder.AddProject<Projects.Sorcha_Tenant_Service>("tenant-service")
    .WithReference(tenantDb)
    .WithReference(redis)
    .WithEnvironment("JwtSettings__SigningKey", jwtSigningKey)
    .WithEnvironment("JwtSettings__Issuer", "https://localhost:7110")
    .WithEnvironment("JwtSettings__Audience__0", "https://sorcha.local");

// Add Blueprint Service with Redis reference (internal only)
var blueprintService = builder.AddProject<Projects.Sorcha_Blueprint_Service>("blueprint-service")
    .WithReference(redis)
    .WithEnvironment("JwtSettings__SigningKey", jwtSigningKey)
    .WithEnvironment("JwtSettings__Issuer", "https://localhost:7110")
    .WithEnvironment("JwtSettings__Audience", "https://sorcha.local")
    .WithEnvironment("ServiceAuth__ClientId", "service-blueprint")
    .WithEnvironment("ServiceAuth__ClientSecret", "blueprint-service-secret")
    .WithEnvironment("ServiceAuth__Scopes", "wallets:sign registers:write blueprints:manage");

// Add Wallet Service with database and Redis reference (internal only)
var walletService = builder.AddProject<Projects.Sorcha_Wallet_Service>("wallet-service")
    .WithReference(walletDb)
    .WithReference(redis)
    .WithEnvironment("JwtSettings__SigningKey", jwtSigningKey)
    .WithEnvironment("JwtSettings__Issuer", "https://localhost:7110")
    .WithEnvironment("JwtSettings__Audience", "https://sorcha.local")
    .WithEnvironment("ServiceAuth__ClientId", "service-wallet")
    .WithEnvironment("ServiceAuth__ClientSecret", "wallet-service-secret")
    .WithEnvironment("ServiceAuth__Scopes", "registers:write");

// Add Peer Service with Redis reference (internal only)
var peerService = builder.AddProject<Projects.Sorcha_Peer_Service>("peer-service")
    .WithReference(redis)
    .WithEnvironment("JwtSettings__SigningKey", jwtSigningKey)
    .WithEnvironment("JwtSettings__Issuer", "https://localhost:7110")
    .WithEnvironment("JwtSettings__Audience", "https://sorcha.local")
    .WithEnvironment("ServiceAuth__ClientId", "service-peer")
    .WithEnvironment("ServiceAuth__ClientSecret", "peer-service-secret")
    .WithEnvironment("ServiceAuth__Scopes", "registers:write registers:read")
    // Azure-hosted PeerRouter seed node
    .WithEnvironment("PeerService__SeedNodes__SeedNodes__0__NodeId", "n0")
    .WithEnvironment("PeerService__SeedNodes__SeedNodes__0__Hostname", "n0.sorcha.dev")
    .WithEnvironment("PeerService__SeedNodes__SeedNodes__0__Port", "443")
    .WithEnvironment("PeerService__SeedNodes__SeedNodes__0__EnableTls", "true");

// Add Register Service with MongoDB, Redis, and Peer Service reference
var registerService = builder.AddProject<Projects.Sorcha_Register_Service>("register-service")
    .WithReference(registerDb)
    .WithReference(redis)
    .WithReference(peerService)
    .WithEnvironment("JwtSettings__SigningKey", jwtSigningKey)
    .WithEnvironment("JwtSettings__Issuer", "https://localhost:7110")
    .WithEnvironment("JwtSettings__Audience", "https://sorcha.local")
    .WithEnvironment("ServiceAuth__ClientId", "service-register")
    .WithEnvironment("ServiceAuth__ClientSecret", "register-service-secret")
    .WithEnvironment("ServiceAuth__Scopes", "validators:notify")
    .WithExternalHttpEndpoints(); // Exposed for walkthrough testing

// Add Validator Service with dependencies
var validatorService = builder.AddProject<Projects.Sorcha_Validator_Service>("validator-service")
    .WithReference(redis)
    .WithReference(walletService)
    .WithReference(registerService)
    .WithReference(peerService)
    .WithReference(blueprintService)
    .WithEnvironment("JwtSettings__SigningKey", jwtSigningKey)
    .WithEnvironment("JwtSettings__Issuer", "https://localhost:7110")
    .WithEnvironment("JwtSettings__Audience", "https://sorcha.local")
    .WithEnvironment("ServiceAuth__ClientId", "service-validator")
    .WithEnvironment("ServiceAuth__ClientSecret", "validator-service-secret")
    .WithEnvironment("ServiceAuth__Scopes", "registers:write registers:read")
    .WithExternalHttpEndpoints(); // Exposed for walkthrough testing

// Add API Gateway as the API entry point for backend services
var apiGateway = builder.AddProject<Projects.Sorcha_ApiGateway>("api-gateway")
    .WithReference(tenantService)
    .WithReference(blueprintService)
    .WithReference(walletService)
    .WithReference(registerService)
    .WithReference(peerService)
    .WithReference(validatorService)
    .WithReference(redis)
    .WithExternalHttpEndpoints(); // Exposed for API calls from UI

// Add Blazor WebAssembly UI as the default homepage
// Note: This is a Blazor Web App with Server + WASM render modes
var uiWeb = builder.AddProject<Projects.Sorcha_UI_Web>("ui-web")
    .WithReference(apiGateway) // UI can discover and call API Gateway
    .WithExternalHttpEndpoints(); // Primary external entry point for users

builder.Build().Run();

/// <summary>
/// Gets or creates a JWT signing key for development.
/// The key is stored in the user's local application data folder to persist across restarts.
/// </summary>
static string GetOrCreateJwtSigningKey()
{
    var keyFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sorcha",
        "dev-jwt-signing-key.txt");

    try
    {
        // Check if we already have a key
        if (File.Exists(keyFilePath))
        {
            var existingKey = File.ReadAllText(keyFilePath).Trim();
            if (!string.IsNullOrEmpty(existingKey) && existingKey.Length >= 32)
            {
                Console.WriteLine($"[JWT] Using existing development signing key from: {keyFilePath}");
                return existingKey;
            }
        }

        // Generate a new secure key
        var keyBytes = new byte[32]; // 256 bits
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(keyBytes);
        }
        var newKey = Convert.ToBase64String(keyBytes);

        // Ensure directory exists
        var directory = Path.GetDirectoryName(keyFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Save the key
        File.WriteAllText(keyFilePath, newKey);

        Console.WriteLine($"[JWT] Generated new development signing key at: {keyFilePath}");

        return newKey;
    }
    catch (Exception ex)
    {
        // If we can't persist, generate one in memory (services will still share via env var)
        Console.WriteLine($"[JWT] Warning: Could not persist development key: {ex.Message}");

        var keyBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(keyBytes);
        }
        return Convert.ToBase64String(keyBytes);
    }
}
