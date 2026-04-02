// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO.Compression;
using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace Sorcha.Gateway.Integration.Tests;

/// <summary>
/// Integration tests for client download functionality
/// </summary>
public class ClientDownloadTests : GatewayIntegrationTestBase
{
    [Fact]
    public async Task GetClientInfo_ReturnsMetadata()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/api/client/info");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var info = JsonDocument.Parse(content);

        info.RootElement.GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        info.RootElement.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        info.RootElement.GetProperty("framework").GetString().Should().Contain("NET");
        info.RootElement.GetProperty("type").GetString().Should().Contain("Blazor");
        info.RootElement.GetProperty("license").GetString().Should().Be("MIT");
    }

    [Fact]
    public async Task DownloadClient_ReturnsZipFile()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/api/client/download");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");
        response.Content.Headers.ContentDisposition?.FileName.Should().Contain("sorcha-client");
        response.Content.Headers.ContentDisposition?.FileName.Should().EndWith(".zip");
    }

    [Fact]
    public async Task DownloadClient_ZipContainsValidFiles()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/api/client/download");
        var zipBytes = await response.Content.ReadAsByteArrayAsync();

        // Assert
        using var memoryStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);

        archive.Entries.Should().NotBeEmpty("ZIP should contain files");

        // Verify essential files are present
        archive.Entries.Should().Contain(e => e.FullName.EndsWith(".csproj"), "should contain project file");
        archive.Entries.Should().Contain(e => e.FullName.Contains("Program.cs"), "should contain Program.cs");

        // Verify bin/obj folders are excluded
        archive.Entries.Should().NotContain(e => e.FullName.Contains("/bin/"), "bin folder should be excluded");
        archive.Entries.Should().NotContain(e => e.FullName.Contains("/obj/"), "obj folder should be excluded");
    }

    [Fact]
    public async Task GetInstallationInstructions_ReturnsMarkdown()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/api/client/instructions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/markdown");

        var instructions = await response.Content.ReadAsStringAsync();
        instructions.Should().Contain("Installation Instructions");
        instructions.Should().Contain("dotnet");
        instructions.Should().Contain("Prerequisites");
    }
}
