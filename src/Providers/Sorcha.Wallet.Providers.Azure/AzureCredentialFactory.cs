// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Azure.Core;
using Azure.Identity;

namespace Sorcha.Wallet.Providers.Azure;

/// <summary>
/// Creates Azure credentials based on <see cref="AzureKmsOptions"/>.
/// </summary>
internal static class AzureCredentialFactory
{
    /// <summary>
    /// Creates a <see cref="TokenCredential"/> from the provided options.
    /// Uses <see cref="ManagedIdentityCredential"/> when configured, otherwise <see cref="DefaultAzureCredential"/>.
    /// </summary>
    public static TokenCredential Create(AzureKmsOptions options)
    {
        if (!options.UseManagedIdentity)
            return new DefaultAzureCredential();

        if (string.IsNullOrWhiteSpace(options.ManagedIdentityClientId))
            return new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);

        return new ManagedIdentityCredential(
            ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId));
    }
}
