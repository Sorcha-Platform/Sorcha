// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Moq;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Register.Service.Tests;

/// <summary>
/// Shared Moq helpers for register repository tests.
/// </summary>
internal static class RegisterMockHelpers
{
    /// <summary>
    /// Stubs <c>GetTransactionsByTypeAsync</c> to derive its result at call time from whatever
    /// <c>GetTransactionsAsync</c> the test sets up on the same mock — filtering to the requested
    /// <see cref="TransactionType"/> and honouring skip/take. This lets tests that predate the
    /// pushed-down read methods (T1) keep stubbing only <c>GetTransactionsAsync</c>.
    /// </summary>
    public static void StubTransactionsByTypeReadThrough(Mock<IRegisterRepository> mock)
    {
        mock.Setup(r => r.GetTransactionsByTypeAsync(
                It.IsAny<string>(),
                It.IsAny<TransactionType>(),
                It.IsAny<TransactionSort>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string rid, TransactionType type, TransactionSort _, int skip, int take, CancellationToken ct) =>
            {
                var all = await mock.Object.GetTransactionsAsync(rid, ct);
                var q = all.Where(t => t.MetaData != null && t.MetaData.TransactionType == type).Skip(skip);
                if (take > 0) q = q.Take(take);
                return (IReadOnlyList<TransactionModel>)q.ToList();
            });
    }
}
