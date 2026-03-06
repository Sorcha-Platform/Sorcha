// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

/// <summary>
/// View model for displaying a W3C Bitstring Status List in the admin viewer.
/// </summary>
public class StatusListViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string IssuerDid { get; set; } = string.Empty;
    public DateTimeOffset ValidFrom { get; set; }
    public string EncodedList { get; set; } = string.Empty;
    public string[] ContextUrls { get; set; } = [];
}
