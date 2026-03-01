// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.TransactionHandler.Encryption.Models;

namespace Sorcha.TransactionHandler.Encryption;

/// <summary>
/// Groups recipients by identical disclosure field sets to minimize encryption operations.
/// FR-003: Produces one DisclosureGroup per unique field set.
/// </summary>
public interface IDisclosureGroupBuilder
{
    /// <summary>
    /// Groups recipients by their disclosed field sets.
    /// Recipients sharing the exact same fields are grouped together.
    /// </summary>
    /// <param name="disclosedPayloads">
    /// Map of wallet address → filtered payload data (from DisclosureProcessor).
    /// </param>
    /// <param name="recipients">
    /// Resolved recipient information with public keys.
    /// </param>
    /// <returns>
    /// Array of disclosure groups, one per unique field set.
    /// Each group contains the filtered payload and all recipients who share that disclosure.
    /// </returns>
    DisclosureGroup[] BuildGroups(
        Dictionary<string, Dictionary<string, object>> disclosedPayloads,
        RecipientInfo[] recipients);
}
