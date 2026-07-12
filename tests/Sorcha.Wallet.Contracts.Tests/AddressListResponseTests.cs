// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Wallet.Contracts.Models;

namespace Sorcha.Wallet.Contracts.Tests;

public class AddressListResponseTests
{
    [Theory]
    [InlineData(1, 50, 100, true)]   // page 1 of 2
    [InlineData(2, 50, 100, false)]  // last page
    [InlineData(1, 50, 40, false)]   // single short page
    [InlineData(1, 50, 50, false)]   // exactly full
    public void HasMore_ReflectsPagination(int page, int pageSize, int totalCount, bool expected)
    {
        var response = new AddressListResponse
        {
            WalletAddress = "ws1qexample",
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };

        response.HasMore.Should().Be(expected);
    }
}
