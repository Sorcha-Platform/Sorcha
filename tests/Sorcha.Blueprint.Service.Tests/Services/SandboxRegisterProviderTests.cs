// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.ServiceClients.Register;
using RegisterModel = Sorcha.Register.Models.Register;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 142 (T026 / T022) — tests for <see cref="SandboxRegisterProvider"/>: lazy creation,
/// per-org reuse, and isolation between organisations.
/// </summary>
public class SandboxRegisterProviderTests
{
    private readonly Mock<IRegisterServiceClient> _registerClient = new();

    private SandboxRegisterProvider CreateProvider() =>
        new(_registerClient.Object, NullLogger<SandboxRegisterProvider>.Instance);

    public SandboxRegisterProviderTests()
    {
        // Echo back the requested register id so the cache key is stable.
        _registerClient
            .Setup(c => c.CreateRegisterAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, string _, string _, string _, string _, CancellationToken _) =>
                new RegisterModel { Id = id, Name = "Sandbox" });
    }

    [Fact]
    public async Task GetOrCreate_FirstCall_CreatesRegisterOwnedByOrg()
    {
        var provider = CreateProvider();

        var registerId = await provider.GetOrCreateSandboxRegisterAsync("org-1");

        registerId.Should().NotBeNullOrWhiteSpace();
        _registerClient.Verify(c => c.CreateRegisterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            "org-1", "org-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreate_SameOrgTwice_ReusesAndDoesNotCreateAgain()
    {
        var provider = CreateProvider();

        var first = await provider.GetOrCreateSandboxRegisterAsync("org-1");
        var second = await provider.GetOrCreateSandboxRegisterAsync("org-1");

        second.Should().Be(first);
        _registerClient.Verify(c => c.CreateRegisterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreate_DifferentOrgs_CreatesSeparateRegisters()
    {
        var provider = CreateProvider();

        var orgA = await provider.GetOrCreateSandboxRegisterAsync("org-A");
        var orgB = await provider.GetOrCreateSandboxRegisterAsync("org-B");

        orgA.Should().NotBe(orgB);
        _registerClient.Verify(c => c.CreateRegisterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentSameOrg_CreatesOnlyOnce()
    {
        var provider = CreateProvider();

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => provider.GetOrCreateSandboxRegisterAsync("org-race"))
            .ToArray();
        var ids = await Task.WhenAll(tasks);

        ids.Distinct().Should().HaveCount(1);
        _registerClient.Verify(c => c.CreateRegisterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreate_BlankOrg_Throws()
    {
        var provider = CreateProvider();

        var act = () => provider.GetOrCreateSandboxRegisterAsync("  ");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
