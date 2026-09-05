// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Extensions;

namespace Sorcha.McpServer.Tests.Infrastructure;

/// <summary>
/// A host that authorises by forwarding the caller's bearer must be able to resolve the typed
/// service clients without holding service-principal credentials of its own. Giving the MCP
/// server a ServiceAuth client id and secret would grant it ambient authority the design
/// deliberately refuses ("not by anonymous service-to-service trust").
/// </summary>
public class CallerTokenOnlyResolutionTests
{
    [Fact]
    public void ServiceAuthClient_Resolves_WithoutServiceAuthConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ServiceClients:TenantService:Address"] = "http://localhost" }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddServiceClients(configuration);

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IServiceAuthClient>();

        act.Should().NotThrow(
            "a caller-token-forwarding host never acquires a service token, so construction must not demand credentials");
    }

    [Fact]
    public async Task ServiceAuthClient_Throws_OnlyWhenATokenIsActuallyRequested()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ServiceClients:TenantService:Address"] = "http://localhost" }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddServiceClients(configuration);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IServiceAuthClient>();

        var act = async () => await client.GetTokenAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ServiceAuth:ClientId*",
                "the failure must still be loud and specific for hosts that genuinely need a service token");
    }
}
