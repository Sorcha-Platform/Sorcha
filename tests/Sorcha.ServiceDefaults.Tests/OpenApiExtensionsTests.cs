// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Sorcha.ServiceDefaults.Tests;

/// <summary>
/// Tests for <see cref="OpenApiExtensions"/> which configure OpenAPI document generation
/// with standard Sorcha metadata across all services.
/// These are integration tests that start a live HTTP server — use the "Integration"
/// trait to filter them from fast unit test runs.
/// </summary>
[Trait("Category", "Integration")]
public class OpenApiExtensionsTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;

    private const string TestTitle = "Test Service API";
    private const string TestDescription = "A test API for unit testing";

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddSorchaOpenApi(TestTitle, TestDescription);

        // Use a random port to avoid conflicts
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _app = builder.Build();
        _app.MapOpenApi();

        await _app.StartAsync();

        var address = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// Helper to fetch and parse the generated OpenAPI document.
    /// </summary>
    private async Task<JsonElement> GetOpenApiDocumentAsync()
    {
        var response = await _client!.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    [Fact]
    public void AddSorchaOpenApi_ReturnsBuilder_ForChaining()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        // Act
        var result = builder.AddSorchaOpenApi("API", "Description");

        // Assert
        result.Should().BeSameAs(builder, "the method should return the builder for chaining");
    }

    [Fact]
    public void AddSorchaOpenApi_RegistersOpenApiServices()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        // Act
        builder.AddSorchaOpenApi("API", "Description");

        // Assert — OpenAPI registration adds IOptionsMonitor<OpenApiOptions>
        var serviceProvider = builder.Services.BuildServiceProvider();
        var optionsMonitor = serviceProvider
            .GetService<Microsoft.Extensions.Options.IOptionsMonitor<OpenApiOptions>>();
        optionsMonitor.Should().NotBeNull("AddOpenApi should register OpenApiOptions in the service collection");
    }

    [Fact]
    public async Task AddSorchaOpenApi_DocumentTransformer_SetsTitle()
    {
        // Act
        var doc = await GetOpenApiDocumentAsync();

        // Assert
        var title = doc.GetProperty("info").GetProperty("title").GetString();
        title.Should().Be(TestTitle);
    }

    [Fact]
    public async Task AddSorchaOpenApi_DocumentTransformer_SetsVersion()
    {
        // Act
        var doc = await GetOpenApiDocumentAsync();

        // Assert
        var version = doc.GetProperty("info").GetProperty("version").GetString();
        version.Should().Be("1.0.0");
    }

    [Fact]
    public async Task AddSorchaOpenApi_DocumentTransformer_SetsDescription()
    {
        // Act
        var doc = await GetOpenApiDocumentAsync();

        // Assert
        var description = doc.GetProperty("info").GetProperty("description").GetString();
        description.Should().Be(TestDescription);
    }

    [Fact]
    public async Task AddSorchaOpenApi_DocumentTransformer_SetsContactName()
    {
        // Act
        var doc = await GetOpenApiDocumentAsync();

        // Assert
        var contactName = doc.GetProperty("info").GetProperty("contact").GetProperty("name").GetString();
        contactName.Should().Be("Sorcha Contributors");
    }

    [Fact]
    public async Task AddSorchaOpenApi_DocumentTransformer_SetsContactUrl()
    {
        // Act
        var doc = await GetOpenApiDocumentAsync();

        // Assert
        var contactUrl = doc.GetProperty("info").GetProperty("contact").GetProperty("url").GetString();
        contactUrl.Should().Be("https://github.com/sorcha-platform/sorcha");
    }

    [Fact]
    public async Task AddSorchaOpenApi_DocumentTransformer_SetsLicenseName()
    {
        // Act
        var doc = await GetOpenApiDocumentAsync();

        // Assert
        var licenseName = doc.GetProperty("info").GetProperty("license").GetProperty("name").GetString();
        licenseName.Should().Be("MIT License");
    }

    [Fact]
    public async Task AddSorchaOpenApi_DocumentTransformer_SetsLicenseUrl()
    {
        // Act
        var doc = await GetOpenApiDocumentAsync();

        // Assert
        var licenseUrl = doc.GetProperty("info").GetProperty("license").GetProperty("url").GetString();
        licenseUrl.Should().Be("https://opensource.org/licenses/MIT");
    }

    [Fact]
    public void MapSorchaOpenApiUi_ReturnsWebApplication_ForChaining()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.AddSorchaOpenApi("Test API", "A test API");
        var app = builder.Build();

        // Act
        var result = app.MapSorchaOpenApiUi("Test API");

        // Assert
        result.Should().BeSameAs(app, "the method should return the app for chaining");
    }
}
