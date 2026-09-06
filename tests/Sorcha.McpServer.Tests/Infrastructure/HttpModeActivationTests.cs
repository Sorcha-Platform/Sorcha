// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;

namespace Sorcha.McpServer.Tests.Infrastructure;

/// <summary>
/// Every advertised tool must be constructible from the container the HTTP transport builds.
/// Eleven tools once injected a stdio-only <c>IMcpSessionService</c> the HTTP branch never
/// registered, so every one of them failed to activate and the whole public surface returned
/// "An error occurred invoking 'X'." for six days. `initialize` and `tools/list` both still
/// succeeded, so no smoke check noticed.
/// </summary>
public class HttpModeActivationTests
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
        return services.BuildServiceProvider();
    }

    public static TheoryData<Type> ToolTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(McpServerHttpRegistration).Assembly.GetTypes()
                     .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ToolTypes))]
    public void EveryAdvertisedTool_CanBeActivated_FromTheHttpModeContainer(Type toolType)
    {
        using var provider = BuildHttpModeProvider();

        var act = () => ActivatorUtilities.CreateInstance(provider, toolType);

        act.Should().NotThrow(
            $"{toolType.Name} is advertised over the HTTP transport and must be constructible there");
    }

    [Fact]
    public void ToolTypes_AreDiscovered_SoTheTheoryIsNotVacuous()
    {
        ToolTypes().Should().HaveCountGreaterThan(20,
            "a discovery bug that found no tools would make every activation assertion pass trivially");
    }
}
