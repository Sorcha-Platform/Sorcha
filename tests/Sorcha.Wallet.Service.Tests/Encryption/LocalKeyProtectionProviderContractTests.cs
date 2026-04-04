// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Wallet.Core.Encryption.Interfaces;
using Sorcha.Wallet.Core.Encryption.Providers;

namespace Sorcha.Wallet.Service.Tests.Encryption;

/// <summary>
/// Runs the IKeyProtectionProvider contract tests against LocalEncryptionProvider.
/// </summary>
public class LocalKeyProtectionProviderContractTests : KeyProtectionProviderContractTests
{
    private readonly LocalEncryptionProvider _provider = new(NullLogger<LocalEncryptionProvider>.Instance);

    protected override IKeyProtectionProvider CreateProvider() => _provider;
}
