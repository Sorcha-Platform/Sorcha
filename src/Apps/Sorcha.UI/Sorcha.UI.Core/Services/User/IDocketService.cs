// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Explorer;
using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for docket chain inspection on register detail pages.
/// </summary>
public interface IDocketService
{
    Task<List<DocketViewModel>> GetDocketsAsync(string registerId, CancellationToken cancellationToken = default);
    Task<DocketViewModel?> GetDocketAsync(string registerId, string docketId, CancellationToken cancellationToken = default);
    Task<List<TransactionViewModel>> GetDocketTransactionsAsync(string registerId, string docketId, CancellationToken cancellationToken = default);
    Task<DocketViewModel?> GetLatestDocketAsync(string registerId, CancellationToken cancellationToken = default);
}
