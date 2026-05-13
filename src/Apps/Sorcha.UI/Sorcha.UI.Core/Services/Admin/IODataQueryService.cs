// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Common;
using Sorcha.UI.Core.Models.Explorer;
using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for executing OData queries against the Register Service.
/// </summary>
public interface IODataQueryService
{
    Task<PaginatedList<TransactionViewModel>> ExecuteTransactionQueryAsync(ODataQueryModel query, CancellationToken cancellationToken = default);
    Task<PaginatedList<RegisterViewModel>> ExecuteRegisterQueryAsync(ODataQueryModel query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pure function: converts ODataQueryModel to OData $filter string.
    /// </summary>
    string BuildFilterString(ODataQueryModel model);
}
