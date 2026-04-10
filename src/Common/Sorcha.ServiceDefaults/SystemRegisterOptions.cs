// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceDefaults;

/// <summary>
/// Configuration for system register genesis trust anchor.
/// Bound from the "SystemRegister" configuration section.
/// </summary>
public class SystemRegisterOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "SystemRegister";

    /// <summary>
    /// Absolute path to the genesis JSON file.
    /// When null, the embedded assembly resource is used as fallback.
    /// </summary>
    public string? GenesisFile { get; set; }
}
