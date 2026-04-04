// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Sorcha.Wallet.Core.Tests;

public class PortablePackageDependencyTests
{
    [Fact]
    public void WalletPortable_HasNoServerDependencies()
    {
        // Get the Wallet.Portable assembly via a type that lives in it
        var assembly = typeof(Sorcha.Wallet.Core.Domain.Entities.Wallet).Assembly;
        assembly.GetName().Name.Should().Be("Sorcha.Wallet.Portable",
            "entities should have been moved to the Portable assembly");

        var refs = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        refs.Should().NotContain(name => name!.Contains("EntityFrameworkCore"),
            "portable package must not reference EF Core");
        refs.Should().NotContain(name => name!.Contains("Npgsql"),
            "portable package must not reference Npgsql");
        refs.Should().NotContain(name => name!.StartsWith("Microsoft.AspNetCore"),
            "portable package must not reference ASP.NET Core");
    }
}
