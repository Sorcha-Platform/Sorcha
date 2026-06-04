// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// Runs the deprecated-IEncryptionProvider contract suite; intentional until the interface is
// removed (new code uses IKeyProtectionProvider). Suppress the obsolete-usage warning file-wide.
#pragma warning disable CS0618
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Wallet.Core.Encryption.Interfaces;
using Sorcha.Wallet.Core.Encryption.Providers;

namespace Sorcha.Wallet.Service.Tests.Encryption;

/// <summary>
/// Runs the IEncryptionProvider contract tests against LocalEncryptionProvider.
/// </summary>
public class LocalEncryptionProviderContractTests : EncryptionProviderContractTests
{
    private readonly LocalEncryptionProvider _provider = new(NullLogger<LocalEncryptionProvider>.Instance);

    protected override IEncryptionProvider CreateProvider() => _provider;
}
