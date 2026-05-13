// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models.Enums;

namespace Sorcha.UI.Core.Models.Registers;

/// <summary>
/// State model for tracking register creation wizard progress.
/// </summary>
public record RegisterCreationState
{
    /// <summary>
    /// Current step in the wizard (1-3)
    /// </summary>
    public int CurrentStep { get; init; } = 1;

    /// <summary>
    /// Register name entered by user
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The purpose classification for this register
    /// </summary>
    public RegisterPurpose Purpose { get; init; } = RegisterPurpose.General;

    /// <summary>
    /// Whether to advertise the register publicly
    /// </summary>
    public bool Advertise { get; init; }

    /// <summary>
    /// Whether to maintain full transaction history
    /// </summary>
    public bool IsFullReplica { get; init; } = true;

    /// <summary>
    /// Tenant ID for the register
    /// </summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>
    /// Selected wallet address for signing
    /// </summary>
    public string? SelectedWalletAddress { get; init; }

    /// <summary>
    /// Selected wallet display name
    /// </summary>
    public string? SelectedWalletName { get; init; }

    /// <summary>
    /// Available wallets for selection
    /// </summary>
    public IReadOnlyList<WalletViewModel> AvailableWallets { get; init; } = [];

    /// <summary>
    /// Whether wallets are loading
    /// </summary>
    public bool IsLoadingWallets { get; init; }

    /// <summary>
    /// Whether a wallet is selected and can sign
    /// </summary>
    public bool HasValidWallet => !string.IsNullOrEmpty(SelectedWalletAddress);

    /// <summary>
    /// Register ID returned from initiate step
    /// </summary>
    public string? RegisterId { get; init; }

    /// <summary>
    /// Unsigned control record from initiate step
    /// </summary>
    public string? UnsignedControlRecord { get; init; }

    /// <summary>
    /// Signed control record for finalization
    /// </summary>
    public string? SignedControlRecord { get; init; }

    /// <summary>
    /// Whether the wizard is currently processing
    /// </summary>
    public bool IsProcessing { get; init; }

    /// <summary>
    /// Error message if creation failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Whether name validation passed
    /// </summary>
    public bool IsNameValid => !string.IsNullOrWhiteSpace(Name)
        && Name.Length >= 1
        && Name.Length <= 38;

    /// <summary>
    /// Whether the wizard can proceed to the next step
    /// </summary>
    public bool CanProceed => CurrentStep switch
    {
        1 => IsNameValid,
        2 => HasValidWallet, // Wallet selection step requires valid wallet
        3 => true, // Options step always valid
        4 => !string.IsNullOrEmpty(SignedControlRecord),
        _ => false
    };

    /// <summary>
    /// Whether the wizard can go back to the previous step
    /// </summary>
    public bool CanGoBack => CurrentStep > 1 && !IsProcessing;

    /// <summary>
    /// Creates a new state with the next step
    /// </summary>
    public RegisterCreationState NextStep() => this with { CurrentStep = CurrentStep + 1 };

    /// <summary>
    /// Creates a new state with the previous step
    /// </summary>
    public RegisterCreationState PreviousStep() => this with { CurrentStep = CurrentStep - 1 };
}
