// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Schemas.DTOs;
using Sorcha.Blueprint.Service.IntegrationTests.Fixtures;

namespace Sorcha.Blueprint.Service.IntegrationTests;

/// <summary>
/// Integration tests for Schema API endpoints.
/// </summary>
[Collection("BlueprintService")]
public class SchemaEndpointsTests : IAsyncLifetime
{
    private readonly BlueprintServiceWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public SchemaEndpointsTests(BlueprintServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public ValueTask InitializeAsync()
    {
        _client = _factory.CreateAuthenticatedClient();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task GetSystemSchemas_ReturnsAllFourSystemSchemas()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/schemas/system");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var schemas = await response.Content.ReadFromJsonAsync<List<SchemaEntryDto>>();
        schemas.Should().NotBeNull();
        schemas.Should().HaveCount(4);
        schemas!.Select(s => s.Identifier).Should().Contain(new[]
        {
            "installation",
            "organisation",
            "participant",
            "register"
        });
    }

    [Fact]
    public async Task GetSystemSchemas_AllSchemasHaveSystemCategory()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/schemas/system");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var schemas = await response.Content.ReadFromJsonAsync<List<SchemaEntryDto>>();
        schemas.Should().NotBeNull();
        schemas!.Should().OnlyContain(s => s.Category == "System");
    }

    [Fact]
    public async Task GetSystemSchemas_AllSchemasHaveActiveStatus()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/schemas/system");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var schemas = await response.Content.ReadFromJsonAsync<List<SchemaEntryDto>>();
        schemas.Should().NotBeNull();
        schemas!.Should().OnlyContain(s => s.Status == "Active");
    }

    [Fact]
    public async Task GetSystemSchemas_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await unauthenticatedClient.GetAsync("/api/v1/schemas/system");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("installation")]
    [InlineData("organisation")]
    [InlineData("participant")]
    [InlineData("register")]
    public async Task GetSchemaByIdentifier_ReturnsCorrectSchema(string identifier)
    {
        // Act
        var response = await _client.GetAsync($"/api/v1/schemas/{identifier}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var schema = await response.Content.ReadFromJsonAsync<SchemaContentDto>();
        schema.Should().NotBeNull();
        schema!.Identifier.Should().Be(identifier);
    }

    [Fact]
    public async Task GetSchemaByIdentifier_ReturnsETagHeader()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/schemas/installation");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.Tag.Should().StartWith("\"installation-");
    }

    [Fact]
    public async Task GetSchemaByIdentifier_WithMatchingETag_ReturnsNotModified()
    {
        // Arrange - Get the schema first to obtain the ETag
        var initialResponse = await _client.GetAsync("/api/v1/schemas/installation");
        initialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = initialResponse.Headers.ETag?.Tag;
        etag.Should().NotBeNullOrEmpty();

        // Act - Request with If-None-Match header
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/schemas/installation");
        request.Headers.Add("If-None-Match", etag);
        request.Headers.Add("Authorization", "Bearer test-token");
        var response = await _factory.CreateClient().SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task GetSchemaByIdentifier_WithNonExistentIdentifier_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/schemas/non-existent-schema");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSchemaByIdentifier_ReturnsValidJsonSchemaContent()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/schemas/installation");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var schema = await response.Content.ReadFromJsonAsync<SchemaContentDto>();
        schema.Should().NotBeNull();
        schema!.Content.Should().NotBeNull();

        // Verify the content is a valid JSON schema
        using var doc = JsonDocument.Parse(schema.Content.ToString()!);
        doc.RootElement.TryGetProperty("$schema", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("type", out var typeProp).Should().BeTrue();
        typeProp.GetString().Should().Be("object");
    }

    [Fact]
    public async Task GetSchemaByIdentifier_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await unauthenticatedClient.GetAsync("/api/v1/schemas/installation");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListSchemas_ReturnsSystemSchemas()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/schemas/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SchemaListResponse>();
        result.Should().NotBeNull();
        result!.Schemas.Should().HaveCountGreaterThanOrEqualTo(4);
        result.TotalCount.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task ListSchemas_WithCategoryFilter_ReturnsFilteredResults()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/schemas/?category=System");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SchemaListResponse>();
        result.Should().NotBeNull();
        result!.Schemas.Should().HaveCount(4);
        result.Schemas.Should().OnlyContain(s => s.Category == "System");
    }

    [Fact]
    public async Task ListSchemas_WithSearchFilter_ReturnsMatchingResults()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/schemas/?search=installation");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SchemaListResponse>();
        result.Should().NotBeNull();
        result!.Schemas.Should().HaveCount(1);
        result.Schemas.First().Identifier.Should().Be("installation");
    }

    [Fact]
    public async Task ListSchemas_WithLimit_RespectsLimit()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/schemas/?limit=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SchemaListResponse>();
        result.Should().NotBeNull();
        result!.Schemas.Should().HaveCount(2);
        result.TotalCount.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task ListSchemas_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await unauthenticatedClient.GetAsync("/api/v1/schemas/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ====================================
    // External Schema Endpoints (T045-T046)
    // ====================================

    [Fact]
    public async Task SearchExternalSchemas_WithQuery_ReturnsResults()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/schemas/external/search?query=package");

        // Assert
        // Note: This test depends on external SchemaStore.org availability
        // If service is unavailable, we expect 503 with partial result
        var statusCode = response.StatusCode;
        statusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);

        if (statusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<ExternalSchemaSearchResponse>();
            result.Should().NotBeNull();
            result!.Provider.Should().NotBeNullOrWhiteSpace();
            result.Query.Should().Be("package");
        }
    }

    [Fact]
    public async Task SearchExternalSchemas_WithEmptyQuery_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/schemas/external/search?query=");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SearchExternalSchemas_WithoutQuery_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/schemas/external/search");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SearchExternalSchemas_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await unauthenticatedClient.GetAsync("/api/v1/schemas/external/search?query=test");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ImportExternalSchema_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();
        var request = new ImportExternalSchemaRequest("https://json.schemastore.org/package.json");

        // Act
        var response = await unauthenticatedClient.PostAsJsonAsync("/api/v1/schemas/external/import", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ImportExternalSchema_RequiresAdministratorRole()
    {
        // Arrange - Regular authenticated user (not admin)
        var request = new ImportExternalSchemaRequest("https://json.schemastore.org/package.json");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/schemas/external/import", request);

        // Assert
        // Should return 403 Forbidden for non-admin users
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ImportExternalSchema_WithEmptyUrl_ReturnsBadRequest()
    {
        // Arrange - Create admin client
        using var adminClient = _factory.CreateAdminClient();
        var request = new ImportExternalSchemaRequest("");

        // Act
        var response = await adminClient.PostAsJsonAsync("/api/v1/schemas/external/import", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ====================================
    // Custom Schema CRUD Endpoints (T060-T061, T065-T073)
    // ====================================

    [Fact]
    public async Task CreateSchema_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();
        var request = new CreateSchemaRequest(
            "test-schema",
            "Test Schema",
            "A test schema",
            "1.0.0",
            JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone());

        // Act
        var response = await unauthenticatedClient.PostAsJsonAsync("/api/v1/schemas/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateSchema_WithValidContent_ReturnsCreated()
    {
        // Arrange
        using var orgClient = _factory.CreateOrganizationMemberClient();
        var request = new CreateSchemaRequest(
            $"test-schema-{Guid.NewGuid():N}",
            "Test Schema",
            "A test schema for integration testing",
            "1.0.0",
            JsonDocument.Parse("{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}").RootElement.Clone());

        // Act
        var response = await orgClient.PostAsJsonAsync("/api/v1/schemas/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var content = await response.Content.ReadFromJsonAsync<SchemaContentDto>();
        content.Should().NotBeNull();
        content!.Identifier.Should().Be(request.Identifier);
    }

    [Fact]
    public async Task CreateSchema_WithMissingIdentifier_ReturnsBadRequest()
    {
        // Arrange
        using var orgClient = _factory.CreateOrganizationMemberClient();
        var request = new CreateSchemaRequest(
            "",
            "Test Schema",
            "A test schema",
            "1.0.0",
            JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone());

        // Act
        var response = await orgClient.PostAsJsonAsync("/api/v1/schemas/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateSchema_WithInvalidJsonSchema_ReturnsBadRequest()
    {
        // Arrange
        using var orgClient = _factory.CreateOrganizationMemberClient();
        var request = new CreateSchemaRequest(
            $"invalid-schema-{Guid.NewGuid():N}",
            "Invalid Schema",
            "A schema with invalid content",
            "1.0.0",
            JsonDocument.Parse("{\"invalid\":\"this is not a json schema\"}").RootElement.Clone()); // Missing type or $schema

        // Act
        var response = await orgClient.PostAsJsonAsync("/api/v1/schemas/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSchema_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();
        var request = new UpdateSchemaRequest("Updated Title", null, null, null);

        // Act
        var response = await unauthenticatedClient.PutAsJsonAsync("/api/v1/schemas/test-schema", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateSchema_NonExistentSchema_ReturnsNotFound()
    {
        // Arrange
        using var orgClient = _factory.CreateOrganizationMemberClient();
        var request = new UpdateSchemaRequest("Updated Title", null, null, null);

        // Act
        var response = await orgClient.PutAsJsonAsync("/api/v1/schemas/non-existent-schema", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateSchema_SystemSchema_ReturnsBadRequest()
    {
        // Arrange
        using var orgClient = _factory.CreateOrganizationMemberClient();
        var request = new UpdateSchemaRequest("Updated Title", null, null, null);

        // Act
        var response = await orgClient.PutAsJsonAsync("/api/v1/schemas/installation", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteSchema_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await unauthenticatedClient.DeleteAsync("/api/v1/schemas/test-schema");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteSchema_NonExistentSchema_ReturnsNotFound()
    {
        // Arrange
        using var orgClient = _factory.CreateOrganizationMemberClient();

        // Act
        var response = await orgClient.DeleteAsync("/api/v1/schemas/non-existent-schema");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteSchema_SystemSchema_ReturnsBadRequest()
    {
        // Arrange
        using var orgClient = _factory.CreateOrganizationMemberClient();

        // Act
        var response = await orgClient.DeleteAsync("/api/v1/schemas/installation");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeprecateSchema_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await unauthenticatedClient.PostAsync("/api/v1/schemas/installation/deprecate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeprecateSchema_SystemSchema_ReturnsBadRequest()
    {
        // Arrange
        using var orgClient = _factory.CreateOrganizationMemberClient();

        // Act
        var response = await orgClient.PostAsync("/api/v1/schemas/installation/deprecate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeprecateSchema_NonExistentSchema_ReturnsNotFound()
    {
        // Arrange
        using var orgClient = _factory.CreateOrganizationMemberClient();

        // Act
        var response = await orgClient.PostAsync("/api/v1/schemas/non-existent-schema/deprecate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ActivateSchema_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await unauthenticatedClient.PostAsync("/api/v1/schemas/installation/activate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ActivateSchema_NonExistentSchema_ReturnsNotFound()
    {
        // Arrange
        using var orgClient = _factory.CreateOrganizationMemberClient();

        // Act
        var response = await orgClient.PostAsync("/api/v1/schemas/non-existent-schema/activate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublishSchema_RequiresAdministratorRole()
    {
        // Arrange - Regular authenticated user (not admin)

        // Act
        var response = await _client.PostAsync("/api/v1/schemas/test-schema/publish", null);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PublishSchema_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await unauthenticatedClient.PostAsync("/api/v1/schemas/test-schema/publish", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PublishSchema_NonExistentSchema_ReturnsNotFound()
    {
        // Arrange
        using var adminClient = _factory.CreateAdminClient();

        // Act
        var response = await adminClient.PostAsync("/api/v1/schemas/non-existent-schema/publish", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
