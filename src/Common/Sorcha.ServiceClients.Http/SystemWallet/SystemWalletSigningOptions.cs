// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Contracts.Constants;

namespace Sorcha.ServiceClients.SystemWallet;

/// <summary>
/// Configuration options for the system wallet signing service
/// </summary>
public class SystemWalletSigningOptions
{
    /// <summary>
    /// Identifier for this node's local-validator system wallet. Passed verbatim to
    /// <c>IWalletServiceClient.CreateOrRetrieveSystemWalletAsync</c> — wallets are
    /// keyed by this string, so every service that signs on this validator's behalf
    /// MUST use the same value.
    /// </summary>
    /// <remarks>
    /// Feature 108 roster-extraction contract: Register.Service populates a newly-created
    /// register's validator roster with the <c>sorcha:docket-signing</c> pubkey derived
    /// under this wallet. Validator.Service later signs dockets with the key derived
    /// under its own <c>Validator:ValidatorId</c> wallet. If the two IDs diverge, the
    /// roster pubkey won't match the signing key, validator roster matching fails,
    /// <c>RegisterMonitoringBootstrap</c> never enrols the register, and dockets are
    /// never sealed. Align <c>SystemWalletSigning:ValidatorId</c> on Register.Service
    /// with <c>Validator:ValidatorId</c> on Validator.Service (same node → same value).
    /// </remarks>
    public required string ValidatorId { get; set; }

    /// <summary>
    /// Permitted derivation paths for system signing operations.
    /// Requests with paths not in this list are rejected.
    /// </summary>
    public string[] AllowedDerivationPaths { get; set; } =
        [SorchaDerivationPaths.RegisterControl, SorchaDerivationPaths.DocketSigning, SorchaDerivationPaths.BlueprintPublish];

    /// <summary>
    /// Maximum number of signing operations per register per minute (sliding window)
    /// </summary>
    public int MaxSignsPerRegisterPerMinute { get; set; } = 10;
}
