// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;

namespace Sorcha.McpServer.Tests.Prompts;

/// <summary>
/// Task 7 shipped resources on both transports' builder chains and pinned that with
/// <c>HttpModeResourceRegistrationTests</c>, after nearly leaving them dead on the HTTP
/// transport. Ruling 1 (task-8) calls for <c>.WithPromptsFromAssembly()</c> to follow the same
/// precedent exactly — a registration present on stdio and absent from the public HTTP endpoint
/// is undetectable by any test that only builds the stdio container, so this class proves
/// prompts are registered on the SAME container the HTTP transport builds.
/// </summary>
public class HttpModePromptRegistrationTests
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

        // WithPromptsFromAssembly() defaults to Assembly.GetCallingAssembly() when no assembly is
        // passed — which, called directly from a test, would be the TEST assembly, not
        // Sorcha.McpServer. Program.cs gets the right assembly "for free" because Program.cs
        // itself lives in Sorcha.McpServer; pass it explicitly here so this container matches
        // production (same reasoning as HttpModeResourceRegistrationTests).
        var mcpServerAssembly = typeof(McpServerHttpRegistration).Assembly;
        services
            .AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .WithToolsFromAssembly(mcpServerAssembly)
            .WithResourcesFromAssembly(mcpServerAssembly)
            .WithPromptsFromAssembly(mcpServerAssembly);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void PromptsFromAssembly_AreRegistered_InTheHttpModeContainer()
    {
        using var provider = BuildHttpModeProvider();

        var prompts = provider.GetServices<McpServerPrompt>().ToList();

        prompts.Should().NotBeEmpty(
            "WithPromptsFromAssembly() must be called on the HTTP builder chain, not only stdio's " +
            "— a prompt registered on one transport and absent from the public one is exactly the " +
            "defect class this workstream exists to close (see PR #1608 / MCP-P0, and Task 7's " +
            "identical trap with resources).");
    }

    [Theory]
    [InlineData("sorcha_two_party_exchange")]
    [InlineData("sorcha_issue_credential")]
    [InlineData("sorcha_prove_to_regulator")]
    public void EveryAdvertisedPrompt_IsPresent_InTheHttpModeContainer(string expectedName)
    {
        using var provider = BuildHttpModeProvider();

        var names = provider.GetServices<McpServerPrompt>().Select(p => p.ProtocolPrompt.Name).ToList();

        names.Should().Contain(expectedName);
    }
}
