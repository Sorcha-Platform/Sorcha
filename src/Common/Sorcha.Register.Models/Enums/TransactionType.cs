// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models.Enums;

/// <summary>
/// Represents the type of transaction
/// </summary>
public enum TransactionType
{
    /// <summary>
    /// Control transaction (register governance — genesis + admin operations)
    /// </summary>
    Control = 0,

    /// <summary>
    /// Action transaction (blueprint workflow action)
    /// </summary>
    Action = 1,

    /// <summary>
    /// Docket transaction (block sealing)
    /// </summary>
    Docket = 2,

    /// <summary>
    /// Participant transaction (published participant identity record)
    /// </summary>
    Participant = 3,

    /// <summary>
    /// Revocation transaction (supersedes or revokes a previously sealed transaction)
    /// </summary>
    Revocation = 4
}
