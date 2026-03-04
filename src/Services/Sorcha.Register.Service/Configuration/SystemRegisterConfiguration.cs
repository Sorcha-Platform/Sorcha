// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Service.Configuration;

/// <summary>
/// Configuration options for system register bootstrap behavior.
/// Bound from environment variables <c>SORCHA_SEED_SYSTEM_REGISTER</c> and
/// <c>SORCHA_SYSTEM_REGISTER_BLUEPRINT</c>.
/// </summary>
public class SystemRegisterConfiguration
{
    /// <summary>
    /// Whether the system register should be seeded on startup.
    /// Defaults to <c>false</c>. Set to <c>true</c> via environment variable
    /// <c>SORCHA_SEED_SYSTEM_REGISTER</c> or <c>.env</c> file to enable bootstrap.
    /// </summary>
    public bool SeedSystemRegister { get; set; } = false;

    /// <summary>
    /// Optional override for the system register governance blueprint identifier.
    /// When null, the default blueprint from <see cref="Sorcha.Register.Models.Constants.SystemRegisterConstants.DefaultBlueprintVersion"/>
    /// is used.
    /// </summary>
    public string? SystemRegisterBlueprint { get; set; }
}
