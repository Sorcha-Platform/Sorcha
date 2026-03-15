// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.SystemWallet;

/// <summary>
/// Configuration options for the system wallet signing service
/// </summary>
public class SystemWalletSigningOptions
{
    /// <summary>
    /// Unique identifier for the validator/service instance, used for wallet creation and retrieval
    /// </summary>
    public required string ValidatorId { get; set; }

    /// <summary>
    /// Permitted derivation paths for system signing operations.
    /// Requests with paths not in this list are rejected.
    /// </summary>
    public string[] AllowedDerivationPaths { get; set; } =
        ["sorcha:register-control", "sorcha:docket-signing", "sorcha:blueprint-publish"];

    /// <summary>
    /// Maximum number of signing operations per register per minute (sliding window)
    /// </summary>
    public int MaxSignsPerRegisterPerMinute { get; set; } = 10;
}
