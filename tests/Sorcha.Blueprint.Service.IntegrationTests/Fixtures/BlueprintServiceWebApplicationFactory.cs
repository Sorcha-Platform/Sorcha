// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sorcha.Blueprint.Schemas.Models;
using Sorcha.Blueprint.Schemas.Repositories;
using Sorcha.Testing;

namespace Sorcha.Blueprint.Service.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for Blueprint Service integration tests.
/// Uses in-memory services for fast testing.
/// </summary>
public class BlueprintServiceWebApplicationFactory : SorchaWebApplicationFactory<Program>
{
    protected override void ConfigureTestAuth(TestAuthHandlerOptions options)
    {
        options.DefaultClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-user-id"),
            new(ClaimTypes.Name, "Test User"),
            new(ClaimTypes.Email, "test@sorcha.io"),
            new("organization_id", "test-org-id"),
            new("org_id", "test-org-id"),
        };
        options.DefaultRole = "User";
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // In-memory schema repository for CRUD operations.
        services.RemoveAll<ISchemaRepository>();
        services.AddSingleton<ISchemaRepository, InMemorySchemaRepository>();
    }

    /// <summary>
    /// Creates an HttpClient configured for an organization member (with org_id claim).
    /// Uses default organization from TestAuthHandler (test-org-id).
    /// </summary>
    public HttpClient CreateOrganizationMemberClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        return client;
    }
}

/// <summary>
/// Collection definition for shared test context.
/// </summary>
[CollectionDefinition("BlueprintService")]
public class BlueprintServiceCollection : ICollectionFixture<BlueprintServiceWebApplicationFactory>
{
}

/// <summary>
/// In-memory implementation of ISchemaRepository for testing.
/// </summary>
internal sealed class InMemorySchemaRepository : ISchemaRepository
{
    private readonly ConcurrentDictionary<string, SchemaEntry> _schemas = new();

    private static string GetKey(string identifier, string? organizationId)
        => $"{organizationId ?? "global"}:{identifier}";

    public Task<SchemaEntry?> GetByIdentifierAsync(
        string identifier,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (organizationId is not null && _schemas.TryGetValue(GetKey(identifier, organizationId), out var orgSchema))
        {
            return Task.FromResult<SchemaEntry?>(orgSchema);
        }

        if (_schemas.TryGetValue(GetKey(identifier, null), out var globalSchema))
        {
            return Task.FromResult<SchemaEntry?>(globalSchema);
        }

        return Task.FromResult<SchemaEntry?>(null);
    }

    public Task<(IReadOnlyList<SchemaEntry> Schemas, int TotalCount, string? NextCursor)> ListAsync(
        SchemaCategory? category = null,
        SchemaStatus? status = null,
        string? search = null,
        string? organizationId = null,
        int limit = 50,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var query = _schemas.Values.AsEnumerable();

        if (category.HasValue)
        {
            query = query.Where(s => s.Category == category.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(s => s.Status == status.Value);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(s =>
                s.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                s.Identifier.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(organizationId))
        {
            query = query.Where(s =>
                s.OrganizationId == organizationId ||
                s.IsGloballyPublished ||
                s.Category == SchemaCategory.System ||
                s.Category == SchemaCategory.External);
        }

        var list = query.Take(limit).ToList();
        return Task.FromResult<(IReadOnlyList<SchemaEntry>, int, string?)>((list, list.Count, null));
    }

    public Task<SchemaEntry> CreateAsync(SchemaEntry entry, CancellationToken cancellationToken = default)
    {
        var key = GetKey(entry.Identifier, entry.OrganizationId);
        if (!_schemas.TryAdd(key, entry))
        {
            throw new InvalidOperationException($"Schema '{entry.Identifier}' already exists.");
        }
        return Task.FromResult(entry);
    }

    public Task<SchemaEntry> UpdateAsync(SchemaEntry entry, CancellationToken cancellationToken = default)
    {
        var key = GetKey(entry.Identifier, entry.OrganizationId);
        if (!_schemas.ContainsKey(key))
        {
            throw new KeyNotFoundException($"Schema '{entry.Identifier}' not found.");
        }
        _schemas[key] = entry;
        return Task.FromResult(entry);
    }

    public Task<bool> DeleteAsync(
        string identifier,
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(identifier, organizationId);
        return Task.FromResult(_schemas.TryRemove(key, out _));
    }

    public Task<bool> ExistsAsync(
        string identifier,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_schemas.ContainsKey(GetKey(identifier, organizationId)));
    }

    public Task<bool> ExistsGloballyAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_schemas.Values.Any(s =>
            s.Identifier == identifier &&
            (s.IsGloballyPublished || s.Category == SchemaCategory.External)));
    }
}
