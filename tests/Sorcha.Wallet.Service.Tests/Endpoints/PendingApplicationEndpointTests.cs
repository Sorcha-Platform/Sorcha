// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Wallet.Service.Endpoints;
using Sorcha.Wallet.Service.Models;
using Sorcha.Wallet.Service.Services.Interfaces;
using Sorcha.Wallet.Service.Validators;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Endpoint handler tests for <see cref="PendingApplicationEndpoints"/>
/// (Feature 124, T026). Uses the reflection-based static-handler invocation
/// pattern shared by the other citizen-wallet endpoint tests.
/// </summary>
public sealed class PendingApplicationEndpointTests
{
    private static readonly Guid PlatformUserId = Guid.NewGuid();

    private sealed class FakeStore : IPendingApplicationStore
    {
        private readonly Dictionary<Guid, PendingApplicationNotice> _store = [];
        public int SetCalls { get; private set; }
        public int ClearCalls { get; private set; }
        public int GetCalls { get; private set; }

        public Task<PendingApplicationNotice?> GetAsync(Guid platformUserId, CancellationToken ct = default)
        {
            GetCalls++;
            return Task.FromResult(_store.TryGetValue(platformUserId, out var n) ? n : null);
        }

        public Task<PendingApplicationNotice> SetAsync(Guid platformUserId, string label, CancellationToken ct = default)
        {
            SetCalls++;
            var notice = new PendingApplicationNotice(label, DateTimeOffset.UtcNow);
            _store[platformUserId] = notice;
            return Task.FromResult(notice);
        }

        public Task ClearAsync(Guid platformUserId, CancellationToken ct = default)
        {
            ClearCalls++;
            _store.Remove(platformUserId);
            return Task.CompletedTask;
        }
    }

    private static HttpContext BuildHttpContext(Guid? platformUserId)
    {
        var ctx = new DefaultHttpContext();
        var claims = new List<Claim>();
        if (platformUserId is not null)
            claims.Add(new Claim("platform_user_id", platformUserId.Value.ToString()));
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        return ctx;
    }

    private static async Task<IResult> InvokeHandlerAsync(string name, object[] args)
    {
        var method = typeof(PendingApplicationEndpoints).GetMethod(name,
            BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Should().NotBeNull($"Handler {name} should exist on PendingApplicationEndpoints");
        return await (Task<IResult>)method.Invoke(null, args)!;
    }

    [Fact]
    public async Task Get_NoPlatformUserClaim_ReturnsUnauthorized()
    {
        var store = new FakeStore();
        var ctx = BuildHttpContext(null);
        var result = await InvokeHandlerAsync("GetPendingApplication",
            [ctx, store, CancellationToken.None]);

        result.GetType().Name.Should().Contain("Unauthorized");
        store.GetCalls.Should().Be(0);
    }

    [Fact]
    public async Task Get_NoNoticeSet_ReturnsOkWithNullNotice()
    {
        var store = new FakeStore();
        var ctx = BuildHttpContext(PlatformUserId);
        var result = await InvokeHandlerAsync("GetPendingApplication",
            [ctx, store, CancellationToken.None]);

        var ok = result.Should().BeOfType<Ok<PendingApplicationEnvelope>>().Subject;
        ok.Value!.Notice.Should().BeNull();
    }

    [Fact]
    public async Task Get_NoticeSet_ReturnsOkWithNotice()
    {
        var store = new FakeStore();
        await store.SetAsync(PlatformUserId, "Assured Identity");
        var ctx = BuildHttpContext(PlatformUserId);
        var result = await InvokeHandlerAsync("GetPendingApplication",
            [ctx, store, CancellationToken.None]);

        var ok = result.Should().BeOfType<Ok<PendingApplicationEnvelope>>().Subject;
        ok.Value!.Notice.Should().NotBeNull();
        ok.Value.Notice!.Label.Should().Be("Assured Identity");
    }

    [Fact]
    public async Task Set_ValidLabel_PersistsAndReturnsEnvelope()
    {
        var store = new FakeStore();
        var validator = new SetPendingApplicationRequestValidator();
        var ctx = BuildHttpContext(PlatformUserId);
        var request = new SetPendingApplicationRequest { Label = "Assured Identity" };

        var result = await InvokeHandlerAsync("SetPendingApplication",
            [request, ctx, (IValidator<SetPendingApplicationRequest>)validator, store,
                NullLogger<Program>.Instance, CancellationToken.None]);

        var ok = result.Should().BeOfType<Ok<PendingApplicationEnvelope>>().Subject;
        ok.Value!.Notice!.Label.Should().Be("Assured Identity");
        store.SetCalls.Should().Be(1);
    }

    [Fact]
    public async Task Set_TrimsLabelBeforePersisting()
    {
        var store = new FakeStore();
        var validator = new SetPendingApplicationRequestValidator();
        var ctx = BuildHttpContext(PlatformUserId);
        var request = new SetPendingApplicationRequest { Label = "  Assured Identity  " };

        var result = await InvokeHandlerAsync("SetPendingApplication",
            [request, ctx, (IValidator<SetPendingApplicationRequest>)validator, store,
                NullLogger<Program>.Instance, CancellationToken.None]);

        result.Should().BeOfType<Ok<PendingApplicationEnvelope>>();
        (await store.GetAsync(PlatformUserId))!.Label.Should().Be("Assured Identity");
    }

    [Fact]
    public async Task Set_ReplacesPriorNotice()
    {
        var store = new FakeStore();
        await store.SetAsync(PlatformUserId, "Driving Licence");
        var validator = new SetPendingApplicationRequestValidator();
        var ctx = BuildHttpContext(PlatformUserId);
        var request = new SetPendingApplicationRequest { Label = "Assured Identity" };

        await InvokeHandlerAsync("SetPendingApplication",
            [request, ctx, (IValidator<SetPendingApplicationRequest>)validator, store,
                NullLogger<Program>.Instance, CancellationToken.None]);

        (await store.GetAsync(PlatformUserId))!.Label.Should().Be("Assured Identity");
    }

    [Fact]
    public async Task Set_InvalidLabel_ReturnsValidationProblem()
    {
        var store = new FakeStore();
        var validator = new SetPendingApplicationRequestValidator();
        var ctx = BuildHttpContext(PlatformUserId);
        var request = new SetPendingApplicationRequest { Label = "   " };

        var result = await InvokeHandlerAsync("SetPendingApplication",
            [request, ctx, (IValidator<SetPendingApplicationRequest>)validator, store,
                NullLogger<Program>.Instance, CancellationToken.None]);

        result.GetType().Name.Should().Contain("ProblemHttpResult", "validation problem flows through Results.ValidationProblem");
        store.SetCalls.Should().Be(0);
    }

    [Fact]
    public async Task Set_NoPlatformUserClaim_ReturnsUnauthorized()
    {
        var store = new FakeStore();
        var validator = new SetPendingApplicationRequestValidator();
        var ctx = BuildHttpContext(null);
        var request = new SetPendingApplicationRequest { Label = "Assured Identity" };

        var result = await InvokeHandlerAsync("SetPendingApplication",
            [request, ctx, (IValidator<SetPendingApplicationRequest>)validator, store,
                NullLogger<Program>.Instance, CancellationToken.None]);

        result.GetType().Name.Should().Contain("Unauthorized");
        store.SetCalls.Should().Be(0);
    }

    [Fact]
    public async Task Clear_PresentNotice_RemovesAndReturnsNoContent()
    {
        var store = new FakeStore();
        await store.SetAsync(PlatformUserId, "Assured Identity");
        var ctx = BuildHttpContext(PlatformUserId);

        var result = await InvokeHandlerAsync("ClearPendingApplication",
            [ctx, store, NullLogger<Program>.Instance, CancellationToken.None]);

        result.GetType().Name.Should().Contain("NoContent");
        (await store.GetAsync(PlatformUserId)).Should().BeNull();
        store.ClearCalls.Should().Be(1);
    }

    [Fact]
    public async Task Clear_AbsentNotice_IsIdempotent()
    {
        var store = new FakeStore();
        var ctx = BuildHttpContext(PlatformUserId);

        var result = await InvokeHandlerAsync("ClearPendingApplication",
            [ctx, store, NullLogger<Program>.Instance, CancellationToken.None]);

        result.GetType().Name.Should().Contain("NoContent");
        store.ClearCalls.Should().Be(1);
    }

    [Fact]
    public async Task Clear_NoPlatformUserClaim_ReturnsUnauthorized()
    {
        var store = new FakeStore();
        var ctx = BuildHttpContext(null);

        var result = await InvokeHandlerAsync("ClearPendingApplication",
            [ctx, store, NullLogger<Program>.Instance, CancellationToken.None]);

        result.GetType().Name.Should().Contain("Unauthorized");
        store.ClearCalls.Should().Be(0);
    }
}
