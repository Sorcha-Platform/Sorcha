// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Service.Endpoints;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.CitizenWallet;

/// <summary>
/// Feature 114 / US5 PR3 — handler-level coverage for the citizen presentation
/// history endpoints (<c>GET /api/v1/wallet/presentations</c> + <c>DELETE …/{id}</c>).
/// Uses the established reflection-based static-handler invocation pattern
/// (<see cref="CitizenWalletEnrolEndpointTests"/>); the store is a real
/// <see cref="InMemoryCitizenPresentationStore"/> so semantics are exercised end-to-end.
/// </summary>
public sealed class PresentationHistoryEndpointTests
{
    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();

    private static HttpContext BuildHttpContext(Guid? platformUserId)
    {
        var ctx = new DefaultHttpContext();
        var claims = new List<Claim>();
        if (platformUserId is not null)
            claims.Add(new Claim("platform_user_id", platformUserId.Value.ToString()));
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        return ctx;
    }

    private static PresentationLogEntry Entry(DateTimeOffset presentedAt) => new()
    {
        Id = Guid.NewGuid(),
        CredentialId = Guid.NewGuid(),
        VerifierLabel = "Strathcarron Council",
        DisclosedClaims = ["givenName"],
        PresentedAt = presentedAt,
        Outcome = PresentationLogOutcome.Presented
    };

    private static async Task<IResult> InvokeListAsync(HttpContext context, ICitizenPresentationStore store)
    {
        var method = typeof(CitizenWalletEndpoints).GetMethod(
            "ListPresentations", BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Should().NotBeNull("ListPresentations handler should exist");
        var result = method.Invoke(null, [context, store, CancellationToken.None]);
        return await (Task<IResult>)result!;
    }

    private static async Task<IResult> InvokeDeleteAsync(Guid id, HttpContext context, ICitizenPresentationStore store)
    {
        var method = typeof(CitizenWalletEndpoints).GetMethod(
            "DeletePresentation", BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Should().NotBeNull("DeletePresentation handler should exist");
        var result = method.Invoke(null, [id, context, store, CancellationToken.None]);
        return await (Task<IResult>)result!;
    }

    // ---- List ----

    [Fact]
    public async Task List_ReturnsCallersRowsNewestFirst()
    {
        var store = new InMemoryCitizenPresentationStore();
        var now = DateTimeOffset.UtcNow;
        var older = Entry(now.AddMinutes(-10));
        var newer = Entry(now);
        await store.UpsertAsync(UserA, older);
        await store.UpsertAsync(UserA, newer);
        await store.UpsertAsync(UserB, Entry(now)); // different user — must not leak

        var result = await InvokeListAsync(BuildHttpContext(UserA), store);

        var ok = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<PresentationHistoryResponse>>().Subject;
        ok.Value!.Entries.Select(e => e.Id).Should().ContainInOrder(newer.Id, older.Id);
        ok.Value.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_EmptyHistory_ReturnsEmptyListNot404()
    {
        var store = new InMemoryCitizenPresentationStore();

        var result = await InvokeListAsync(BuildHttpContext(UserA), store);

        var ok = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<PresentationHistoryResponse>>().Subject;
        ok.Value!.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task List_MissingPlatformUserClaim_ReturnsUnauthorized()
    {
        var store = new InMemoryCitizenPresentationStore();

        var result = await InvokeListAsync(BuildHttpContext(platformUserId: null), store);

        result.GetType().Name.Should().Contain("Unauthorized");
    }

    // ---- Delete ----

    [Fact]
    public async Task Delete_OwnEntry_Returns204AndRemoves()
    {
        var store = new InMemoryCitizenPresentationStore();
        var entry = Entry(DateTimeOffset.UtcNow);
        await store.UpsertAsync(UserA, entry);

        var result = await InvokeDeleteAsync(entry.Id, BuildHttpContext(UserA), store);

        result.GetType().Name.Should().Contain("NoContent");
        (await store.ListAsync(UserA)).Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_CrossUserEntry_Returns204AndLeavesRowIntact()
    {
        var store = new InMemoryCitizenPresentationStore();
        var entry = Entry(DateTimeOffset.UtcNow);
        await store.UpsertAsync(UserA, entry);

        // UserB tries to delete UserA's entry — must be a 204 no-op (indistinguishable).
        var result = await InvokeDeleteAsync(entry.Id, BuildHttpContext(UserB), store);

        result.GetType().Name.Should().Contain("NoContent");
        (await store.ListAsync(UserA)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Delete_NonExistentEntry_Returns204()
    {
        var store = new InMemoryCitizenPresentationStore();

        var result = await InvokeDeleteAsync(Guid.NewGuid(), BuildHttpContext(UserA), store);

        result.GetType().Name.Should().Contain("NoContent");
    }

    [Fact]
    public async Task Delete_MissingPlatformUserClaim_ReturnsUnauthorized()
    {
        var store = new InMemoryCitizenPresentationStore();

        var result = await InvokeDeleteAsync(Guid.NewGuid(), BuildHttpContext(platformUserId: null), store);

        result.GetType().Name.Should().Contain("Unauthorized");
    }
}
