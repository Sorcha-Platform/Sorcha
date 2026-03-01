// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Cli.Tests.Infrastructure;

/// <summary>
/// Shared xUnit collection definition for all test classes that modify
/// the SORCHA_CONFIG_DIR environment variable. Placing every such class
/// in the same collection forces xUnit to run them sequentially,
/// preventing parallel environment-variable collisions.
/// </summary>
[CollectionDefinition("CliEnvironment")]
public class CliEnvironmentCollection;
