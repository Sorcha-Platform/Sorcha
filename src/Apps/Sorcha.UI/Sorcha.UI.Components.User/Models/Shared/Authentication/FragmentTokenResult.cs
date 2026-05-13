// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.UI.Core.Models.Authentication;

/// <summary>
/// Token data extracted from the URL fragment during login redirect handoff.
/// Used by both CustomAuthenticationStateProvider and FragmentTokenHandler.
/// </summary>
public sealed class FragmentTokenResult
{
    /// <summary>
    /// JWT access token
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// JWT refresh token
    /// </summary>
    public string? Refresh { get; set; }

    /// <summary>
    /// Return URL to navigate to after authentication
    /// </summary>
    public string? ReturnUrl { get; set; }
}
