// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Components.Wallet;

/// <summary>
/// Selects the wallet Home hero copy: an empty wallet welcome, or an active
/// wallet summary reflecting the credential count.
/// </summary>
public enum WalletHeroMode
{
    /// <summary>No credentials yet — welcome + enrol prompt.</summary>
    Empty,

    /// <summary>One or more credentials — active wallet + count.</summary>
    Active
}
