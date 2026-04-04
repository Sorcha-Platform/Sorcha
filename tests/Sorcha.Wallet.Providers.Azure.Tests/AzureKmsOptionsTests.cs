// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Xunit;

namespace Sorcha.Wallet.Providers.Azure.Tests;

public class AzureKmsOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new AzureKmsOptions();

        options.VaultUri.Should().BeEmpty();
        options.UseManagedIdentity.Should().BeTrue();
        options.ManagedIdentityClientId.Should().BeNull();
        options.DefaultKeyName.Should().Be("wallet-encryption-key");
    }
}
