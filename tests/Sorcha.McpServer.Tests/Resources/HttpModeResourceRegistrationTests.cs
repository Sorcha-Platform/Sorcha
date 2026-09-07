// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;

namespace Sorcha.McpServer.Tests.Resources;

/// <summary>
/// The MCP server was tools-only for its whole history — <c>.WithResourcesFromAssembly()</c> was
/// never called on either transport's builder chain. This class proves resources are registered
/// on the SAME container the HTTP transport builds (spec 139 US3 / MCP-P0's precedent, see
/// <see cref="McpServerHttpRegistration"/>), rather than only on the stdio one — a registration
/// present on one transport and absent on the public one is exactly the defect class this whole
/// workstream exists to close (see PR #1608 / MCP-P0).
/// </summary>
public class HttpModeResourceRegistrationTests
{
    private static ServiceProvider BuildHttpModeProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:InstallationName"] = "test",
                ["ServiceClients:BlueprintService:Address"] = "http://localhost",
                ["ServiceClients:RegisterService:Address"] = "http://localhost",
                ["ServiceClients:WalletService:Address"] = "http://localhost",
                ["ServiceClients:TenantService:Address"] = "http://localhost",
                ["ServiceClients:ValidatorService:Address"] = "http://localhost",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        McpServerHttpRegistration.ConfigureServices(services, configuration);

        // WithResourcesFromAssembly() defaults to Assembly.GetCallingAssembly() when no assembly is
        // passed — which, called directly from a test, would be the TEST assembly, not
        // Sorcha.McpServer. Program.cs gets the right assembly "for free" because Program.cs itself
        // lives in Sorcha.McpServer; pass it explicitly here so this container matches production.
        var mcpServerAssembly = typeof(McpServerHttpRegistration).Assembly;
        services
            .AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .WithToolsFromAssembly(mcpServerAssembly)
            .WithResourcesFromAssembly(mcpServerAssembly);

        return services.BuildServiceProvider();
    }

    private static string ResourceUri(McpServerResource resource) =>
        (resource.IsTemplated ? resource.ProtocolResourceTemplate.UriTemplate : resource.ProtocolResource.Uri) ?? string.Empty;

    [Fact]
    public void ResourcesFromAssembly_AreRegistered_InTheHttpModeContainer()
    {
        using var provider = BuildHttpModeProvider();

        var resources = provider.GetServices<McpServerResource>().ToList();

        resources.Should().NotBeEmpty(
            "WithResourcesFromAssembly() must be called on the HTTP builder chain, not only stdio's — " +
            "a resource registered on one transport and absent from the public one is undetectable by " +
            "any test that only builds the stdio container.");
    }

    [Theory]
    [InlineData("sorcha://schema/blueprint")]
    [InlineData("sorcha://examples/{name}")]
    [InlineData("sorcha://glossary")]
    [InlineData("sorcha://instances")]
    [InlineData("sorcha://registers")]
    public void EveryAdvertisedResource_IsPresent_InTheHttpModeContainer(string expectedUri)
    {
        using var provider = BuildHttpModeProvider();

        var uris = provider.GetServices<McpServerResource>().Select(ResourceUri).ToList();

        uris.Should().Contain(expectedUri);
    }

    [Fact]
    public void LiveStateResources_CanBeActivated_FromTheHttpModeContainer()
    {
        using var provider = BuildHttpModeProvider();

        var act = () => ActivatorUtilities.CreateInstance(provider, typeof(Sorcha.McpServer.Resources.LiveStateResources));

        act.Should().NotThrow("LiveStateResources is advertised over the HTTP transport and must be constructible there");
    }
}
