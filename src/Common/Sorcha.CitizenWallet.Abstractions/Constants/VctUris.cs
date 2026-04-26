// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Constants;

/// <summary>
/// Verifiable Credential Type URIs for citizen-wallet-issued credentials.
/// </summary>
public static class VctUris
{
    /// <summary>
    /// Device delegation credential v1. Issued by the citizen's holder key
    /// to a specific enrolled device authorising it to make presentations.
    /// </summary>
    public const string CitizenDeviceDelegationV1 =
        "https://sorcha.dev/vc/citizen-device-delegation/v1";
}
