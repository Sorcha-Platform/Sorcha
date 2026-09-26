// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Generates and stores the transaction receipts for a docket just written to the Register
/// Service. Every docket-write path calls it (#1704).
/// </summary>
public interface IReceiptPublisher
{
    /// <summary>
    /// Generates the docket's receipts and writes them to the Register Service. Never throws for a
    /// receipt failure — the docket write it follows must not be undone by it.
    /// </summary>
    /// <param name="docket">The docket that was just written.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the receipts were stored (or there were none to store).</returns>
    Task<bool> PublishForDocketAsync(Models.Docket docket, CancellationToken cancellationToken = default);
}
