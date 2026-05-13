// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Explorer;

/// <summary>
/// View model for docket chain display.
/// </summary>
public record DocketViewModel
{
    public string DocketId { get; init; } = string.Empty;
    public string RegisterId { get; init; } = string.Empty;
    public int Version { get; init; }
    public string Hash { get; init; } = string.Empty;
    public string? PreviousHash { get; init; }
    public int TransactionCount { get; init; }
    public List<string> TransactionIds { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsIntegrityValid { get; init; }
    public string State { get; init; } = string.Empty;
}

/// <summary>
/// Model for the visual OData query builder.
/// </summary>
public class ODataQueryModel
{
    public List<ODataFilterRow> Filters { get; set; } = [];
    public string? OrderBy { get; set; }
    public string OrderDirection { get; set; } = "asc";
    public int Top { get; set; } = 20;
    public int Skip { get; set; }
}

/// <summary>
/// A single filter row in the OData query builder.
/// </summary>
public class ODataFilterRow
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = "eq";
    public string Value { get; set; } = string.Empty;
    public string LogicalOperator { get; set; } = "and";
}
