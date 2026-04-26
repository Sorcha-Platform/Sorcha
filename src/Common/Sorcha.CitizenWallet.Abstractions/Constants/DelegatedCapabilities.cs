// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Constants;

/// <summary>
/// Capability identifiers used in the device delegation credential's
/// <c>delegated_capabilities</c> claim. v1 supports exactly one capability.
/// </summary>
public static class DelegatedCapabilities
{
    /// <summary>
    /// The device may sign Key Binding JWTs on behalf of the holder for
    /// OID4VP presentations of credentials bound to the holder's key.
    /// </summary>
    public const string PresentationHolderKeyBinding = "presentation.holder-key-binding";
}
