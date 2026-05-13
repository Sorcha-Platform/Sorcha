// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.UI.Core.Models.Common;

/// <summary>
/// Generic paginated list response
/// </summary>
/// <typeparam name="T">Item type</typeparam>
public sealed record PaginatedList<T>
{
    /// <summary>
    /// Items for the current page
    /// </summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>
    /// Total number of items across all pages
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Current page number (1-based)
    /// </summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>
    /// Page size (items per page)
    /// </summary>
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>
    /// Indicates if there is a previous page
    /// </summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>
    /// Indicates if there is a next page
    /// </summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>
    /// Creates an empty paginated list
    /// </summary>
    public static PaginatedList<T> Empty(int pageSize = 20)
    {
        return new PaginatedList<T>
        {
            Items = Array.Empty<T>(),
            TotalCount = 0,
            PageNumber = 1,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Creates a paginated list from items and total count
    /// </summary>
    public static PaginatedList<T> Create(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
    {
        return new PaginatedList<T>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }
}
