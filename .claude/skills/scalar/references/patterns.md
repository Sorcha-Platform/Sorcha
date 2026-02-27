# Scalar Patterns Reference

## Contents
- Service Configuration Pattern
- Endpoint Documentation Pattern
- OpenAPI Document Transformer
- API Gateway Aggregation
- Anti-Patterns

---

## Service Configuration Pattern

All Sorcha services follow this exact pattern for Scalar configuration:

```csharp
// Program.cs
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();  // Aspire integration
builder.Services.AddOpenApi();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapOpenApi();

// Development-only Scalar UI
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("Service Name")
            .WithTheme(ScalarTheme.Purple)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}
```

**Why this pattern:**
- `AddServiceDefaults()` provides Aspire health checks and telemetry
- Development-only Scalar prevents exposing docs in production
- Purple theme maintains brand consistency
- C# HttpClient default targets the primary developer audience

---

## Endpoint Documentation Pattern

### DO: Use Fluent Documentation Methods

```csharp
// GOOD - Complete endpoint documentation
app.MapPost("/api/registers/{registerId}/transactions", handler)
    .WithName("SubmitTransaction")
    .WithSummary("Submit a signed transaction")
    .WithDescription("""
        Submit a cryptographically signed transaction to the register.
        
        **Requirements:**
        - Valid JWT with `CanSubmitTransactions` scope
        - Transaction must reference valid sender wallet
        - Signature must verify against wallet's public key
        """)
    .WithTags("Transactions")
    .RequireAuthorization("CanSubmitTransactions");
```

### DON'T: Skip Documentation Metadata

```csharp
// BAD - Missing documentation, shows as unnamed in Scalar
app.MapPost("/api/registers/{registerId}/transactions", handler)
    .RequireAuthorization();
```

**Why this matters:** Undocumented endpoints appear as "Unknown" in Scalar, making API discovery impossible.

---

## OpenAPI Document Transformer

For services requiring rich documentation, use document transformers:

```csharp
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Sorcha Register Service API";
        document.Info.Version = "1.0.0";
        document.Info.Description = """
            # Register Service API
            
            ## Overview
            The Register Service provides a **distributed ledger** for storing 
            immutable transaction records in the Sorcha platform.
            
            ## Key Concepts
            
            ### Registers
            - **Register ID**: Unique identifier for the ledger
            - **Isolation**: Transactions cannot cross register boundaries
            
            ### Transactions
            - **Immutable**: Cannot be altered once committed
            - **Chained**: References previous transaction hash
            """;
        
        document.Info.Contact = new() 
        { 
            Name = "Sorcha Contributors",
            Url = new Uri("https://github.com/sorcha-platform/sorcha")
        };
        
        document.Info.License = new() 
        { 
            Name = "MIT License",
            Url = new Uri("https://opensource.org/licenses/MIT")
        };
        
        return Task.CompletedTask;
    });
});
```

---

## API Gateway Aggregation

The API Gateway aggregates OpenAPI specs from all services:

```csharp
// Register aggregation service
builder.Services.AddSingleton<OpenApiAggregationService>();

// Aggregated endpoint
app.MapGet("/openapi/aggregated.json", async (OpenApiAggregationService service) =>
{
    var spec = await service.GetAggregatedOpenApiAsync();
    return Results.Json(spec);
})
.WithName("AggregatedOpenApi")
.ExcludeFromDescription();

// Scalar pointing to aggregated spec
app.MapScalarApiReference(options =>
{
    options
        .WithTitle("Sorcha API Gateway - All Services")
        .WithTheme(ScalarTheme.Purple)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .WithOpenApiRoutePattern("/openapi/aggregated.json");
});
```

**Service Configuration for Aggregation:**

```csharp
var serviceConfigs = new Dictionary<string, ServiceOpenApiConfig>
{
    { "blueprint", new ServiceOpenApiConfig
        {
            Name = "Blueprint API",
            BaseUrl = configuration["Services:Blueprint:Url"] ?? "http://blueprint-api",
            OpenApiPath = "/openapi/v1.json",
            PathPrefix = "/api/blueprint"
        }
    }
    // ... other services
};
```

---

## Anti-Patterns

### WARNING: Using Swagger Instead of Scalar

**The Problem:**

```csharp
// BAD - Project mandates Scalar, not Swagger
builder.Services.AddSwaggerGen();
app.UseSwagger();
app.UseSwaggerUI();
```

**Why This Breaks:**
1. Violates project standards (see CLAUDE.md "Critical Patterns")
2. Swagger UI is heavier and less modern
3. Inconsistent documentation across services

**The Fix:**

```csharp
// GOOD - Use Scalar
builder.Services.AddOpenApi();
app.MapOpenApi();
app.MapScalarApiReference(options => { ... });
```

---

### WARNING: Exposing Scalar in Production

**The Problem:**

```csharp
// BAD - No environment check
app.MapScalarApiReference(options =>
{
    options.WithTitle("Service API");
});
```

**Why This Breaks:**
1. Exposes internal API structure in production
2. Security vulnerability - attackers can map your API
3. Performance overhead serving documentation UI

**The Fix:**

```csharp
// GOOD - Development only
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Service API");
    });
}
```

---

### WARNING: Inconsistent Theme Configuration

**The Problem:**

```csharp
// BAD - Each service with different theme
// Service A
options.WithTheme(ScalarTheme.Moon);

// Service B
options.WithTheme(ScalarTheme.Solarized);
```

**Why This Breaks:**
1. Inconsistent brand experience
2. Confusing when switching between service docs
3. Violates project conventions

**The Fix:**

```csharp
// GOOD - Consistent Purple theme (project standard)
options.WithTheme(ScalarTheme.Purple);
```

---

## Available Themes

| Theme | Description |
|-------|-------------|
| `ScalarTheme.Default` | Standard light/dark |
| `ScalarTheme.Purple` | **Project standard** |
| `ScalarTheme.Moon` | Dark blue theme |
| `ScalarTheme.Solarized` | Solarized colors |
| `ScalarTheme.BluePlanet` | Blue-toned |
| `ScalarTheme.Saturn` | Saturn-inspired |
| `ScalarTheme.Mars` | Red-toned |
| `ScalarTheme.DeepSpace` | Dark space theme |
| `ScalarTheme.Laserwave` | Synthwave aesthetic |
| `ScalarTheme.Alternate` | Alternative styling |