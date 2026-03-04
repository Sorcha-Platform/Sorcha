// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models.Constants;

/// <summary>
/// Well-known constants for the Sorcha System Register, used for governance
/// and policy storage. All values are deterministic and stable across deployments.
/// </summary>
public static class SystemRegisterConstants
{
    /// <summary>
    /// Deterministic register ID for the system register.
    /// Derived as the first 32 hex characters of SHA-256("sorcha-system-register").
    /// Full hash: aebf26362e079087571ac0932d4db9738c8a8e2def59c3dd73206064ef6f83a7
    /// </summary>
    public const string SystemRegisterId = "aebf26362e079087571ac0932d4db973";

    /// <summary>
    /// Human-readable display name for the system register.
    /// </summary>
    public const string SystemRegisterName = "Sorcha System Register";

    /// <summary>
    /// Default governance blueprint version applied to the system register.
    /// </summary>
    public const string DefaultBlueprintVersion = "register-governance-v1";

    /// <summary>
    /// Wallet name used during system register bootstrap/setup.
    /// </summary>
    public const string SystemSetupWalletName = "system-setup";

    /// <summary>
    /// Environment variable name that controls whether the system register
    /// is seeded on startup. Expected values: "true" or "false" (default: false).
    /// </summary>
    public const string EnvSeedFlag = "SORCHA_SEED_SYSTEM_REGISTER";

    /// <summary>
    /// Environment variable name for overriding the default governance blueprint
    /// applied to the system register.
    /// </summary>
    public const string EnvBlueprintOverride = "SORCHA_SYSTEM_REGISTER_BLUEPRINT";
}
