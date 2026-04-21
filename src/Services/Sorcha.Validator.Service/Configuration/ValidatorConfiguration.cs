// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Configuration;

/// <summary>
/// Main configuration for the Validator Service instance
/// </summary>
public class ValidatorConfiguration
{
    /// <summary>
    /// Identifier for this node's local-validator system wallet. Passed verbatim to
    /// <c>IWalletServiceClient.CreateOrRetrieveSystemWalletAsync</c> by both
    /// <see cref="Services.SystemWalletInitializer"/> (at startup) and
    /// <see cref="Services.DocketBuilder"/> (on first docket seal).
    /// </summary>
    /// <remarks>
    /// Feature 108 roster-extraction contract: Register.Service populates a newly-created
    /// register's validator roster with the <c>sorcha:docket-signing</c> pubkey derived
    /// under the wallet identified by <c>SystemWalletSigning:ValidatorId</c> in its own
    /// config. Validator.Service later signs dockets with the key derived under the
    /// wallet identified by this property. If the two IDs diverge, the roster entry
    /// won't match the signing key, validator roster matching fails, and docket sealing
    /// never starts for any locally-created register.
    /// MUST match <c>SystemWalletSigning:ValidatorId</c> on Register.Service (same node
    /// → same value). Defaults to a random GUID for tests; production/docker environments
    /// should set this explicitly.
    /// </remarks>
    public string ValidatorId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// System wallet address for signing control records and dockets
    /// </summary>
    /// <remarks>
    /// The system wallet is managed by the Wallet Service and is used exclusively by the Validator Service
    /// for system-level signing operations:
    /// - Signing complete control records after attestation collection
    /// - Signing finalized dockets after transaction validation
    /// This wallet address must be configured and accessible via the Wallet Service.
    /// </remarks>
    public required string SystemWalletAddress { get; set; }

    /// <summary>
    /// Maximum depth for blockchain reorganization (fork resolution)
    /// </summary>
    public int MaxReorgDepth { get; set; } = 10;

    /// <summary>
    /// gRPC endpoint for this validator (for peer-to-peer communication)
    /// </summary>
    public string? GrpcEndpoint { get; set; }
}
