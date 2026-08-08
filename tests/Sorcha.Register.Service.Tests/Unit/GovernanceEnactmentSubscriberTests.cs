// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Services;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// The sealed-docket trigger for enactment (T044b).
/// </summary>
public sealed class GovernanceEnactmentSubscriberTests
{
    private const string RegisterId = "cbb1fa4c1bc942b7a1f86eabcfb96ea6";

    private readonly Mock<IGovernanceProposalReader> _reader = new();
    private readonly Mock<IGovernanceEnactmentService> _enactment = new();
    private readonly GovernanceEnactmentSubscriber _sut;

    private readonly List<string> _attempted = [];

    public GovernanceEnactmentSubscriberTests()
    {
        _reader.Setup(r => r.ListOpenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _enactment.Setup(e => e.TryEnactAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string proposalId, CancellationToken _) => _attempted.Add(proposalId))
            .ReturnsAsync(new GovernanceEnactmentResult(
                EnactmentOutcome.QuorumNotMet, null, "not yet"));

        var services = new ServiceCollection();
        services.AddScoped(_ => _reader.Object);
        services.AddScoped(_ => _enactment.Object);

        _sut = new GovernanceEnactmentSubscriber(
            Mock.Of<IEventSubscriber>(),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<GovernanceEnactmentSubscriber>>());
    }

    private static DocketConfirmedEvent Docket(string registerId = RegisterId) => new()
    {
        RegisterId = registerId,
        DocketId = 7,
        Hash = "abc",
        TimeStamp = DateTime.UtcNow,
    };

    private void OpenProposals(params string[] proposalIds) =>
        _reader.Setup(r => r.ListOpenAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposalIds.Select(id => new GovernanceProposalRead(
                ProposalReadOutcome.Found,
                new GovernanceOperation { Status = ProposalStatus.Pending },
                new TransactionModel { TxId = id, RegisterId = RegisterId })).ToList());

    [Fact]
    public async Task EverySealedDocket_ReEvaluatesEveryOpenProposalOnThatRegister()
    {
        // Self-healing by design: a dropped event costs a delay until the register's next docket,
        // not a proposal stuck at quorum forever.
        OpenProposals("proposal-a", "proposal-b");

        await _sut.OnDocketConfirmedAsync(Docket(), CancellationToken.None);

        _attempted.Should().BeEquivalentTo(["proposal-a", "proposal-b"]);
    }

    [Fact]
    public async Task WithNoOpenProposals_NothingIsAttempted()
    {
        await _sut.OnDocketConfirmedAsync(Docket(), CancellationToken.None);

        _attempted.Should().BeEmpty();
        _enactment.Verify(e => e.TryEnactAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OneProposalThrowing_DoesNotStopTheOthers()
    {
        // Otherwise a single malformed or unenactable proposal blocks every later one on the register.
        OpenProposals("proposal-a", "proposal-b", "proposal-c");
        _enactment.Setup(e => e.TryEnactAsync(RegisterId, "proposal-b", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await _sut.OnDocketConfirmedAsync(Docket(), CancellationToken.None);

        _attempted.Should().Contain("proposal-c");
    }

    [Fact]
    public async Task AFailureReadingTheRegister_DoesNotEscapeTheHandler()
    {
        // An exception out of an event handler kills the subscription, and a dead subscription is
        // silent: governance would simply stop enacting with nothing to show for it.
        _reader.Setup(r => r.ListOpenAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("register unreachable"));

        var act = async () => await _sut.OnDocketConfirmedAsync(Docket(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ADocketNamingNoRegister_IsIgnored()
    {
        await _sut.OnDocketConfirmedAsync(
            new DocketConfirmedEvent { RegisterId = "", DocketId = 1, Hash = "x" },
            CancellationToken.None);

        _reader.Verify(r => r.ListOpenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ItEvaluatesOnlyTheRegisterTheDocketBelongsTo()
    {
        OpenProposals("proposal-a");

        await _sut.OnDocketConfirmedAsync(Docket("another-register"), CancellationToken.None);

        _attempted.Should().BeEmpty();
        _reader.Verify(r => r.ListOpenAsync("another-register", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ItDoesNotDecideAuthority_ItOnlyAsks()
    {
        // The subscriber must not filter on its own view of quorum, expiry or roster state — those
        // belong to the enactment service and, finally, to the Validator.
        OpenProposals("proposal-a");
        _enactment.Setup(e => e.TryEnactAsync(RegisterId, "proposal-a", It.IsAny<CancellationToken>()))
            .Callback((string _, string id, CancellationToken _) => _attempted.Add(id))
            .ReturnsAsync(new GovernanceEnactmentResult(
                EnactmentOutcome.NotEnactable, null, "invalidated by a roster change"));

        await _sut.OnDocketConfirmedAsync(Docket(), CancellationToken.None);

        _attempted.Should().ContainSingle("the decision is made downstream, not here");
    }
}
