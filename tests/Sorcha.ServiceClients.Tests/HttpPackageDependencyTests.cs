// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Sorcha.ServiceClients.Tests;

public class HttpPackageDependencyTests
{
    [Fact]
    public void ServiceClientsHttp_HasNoGrpcDependencies()
    {
        // Get the ServiceClients.Http assembly via a type that lives in it
        var assembly = typeof(Sorcha.ServiceClients.Auth.IServiceAuthClient).Assembly;
        assembly.GetName().Name.Should().Be("Sorcha.ServiceClients.Http",
            "auth interfaces should have been moved to the Http assembly");

        var refs = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        refs.Should().NotContain(name => name!.Contains("Grpc"),
            "HTTP client package must not reference gRPC");
        refs.Should().NotContain(name => name!.Contains("Google.Protobuf"),
            "HTTP client package must not reference Google.Protobuf");
    }
}
