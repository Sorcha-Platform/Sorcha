// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Interface for generating transaction receipts during docket sealing.
/// </summary>
public interface IReceiptGenerator
{
    /// <summary>
    /// Generates signed receipts for all transactions in a confirmed docket.
    /// </summary>
    /// <param name="docket">The confirmed docket.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Array of signed receipts, one per transaction.</returns>
    Task<TransactionReceipt[]> GenerateReceiptsForDocketAsync(
        Sorcha.Validator.Service.Models.Docket docket,
        CancellationToken ct = default);
}
