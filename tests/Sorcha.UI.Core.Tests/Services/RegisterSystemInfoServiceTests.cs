// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Models.LocalRelationship;
using Sorcha.UI.Core.Models.Blueprints;
using Sorcha.UI.Core.Models.Registers;
using Sorcha.UI.Core.Services;
using Xunit;
using FluentAssertions;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for the Feature 142 <see cref="RegisterSystemInfoService"/> aggregator (T035): the
/// client-side fan-out composes the six sub-reads into one view-model, derives the caller's
/// governance role from the local-relationship flags, and degrades a failed sub-read to a single
/// field rather than crashing the card (FR-026 / D6 / spec edge case).
/// </summary>
public class RegisterSystemInfoServiceTests
{
    private readonly Mock<IRegisterReadService> _read = new();
    private readonly Mock<IRegisterGovernanceService> _governance = new();

    private RegisterSystemInfoService CreateService() =>
        new(_read.Object, _governance.Object, NullLogger<RegisterSystemInfoService>.Instance);

    private static RegisterViewModel Register(string id, bool advertise, bool devMode) => new()
    {
        Id = id,
        Name = "Test Register",
        Advertise = advertise,
        DevMode = devMode,
        Status = RegisterStatus.Online,
    };

    private static RegisterLocalRelationship Relationship(RegisterRoleSet roles) =>
        new("reg-1", roles, ControlRecordVersion: 1, DerivedAt: DateTimeOffset.UtcNow);

    private static GovernanceRosterViewModel Roster(params (string subject, string role)[] members) => new()
    {
        RegisterId = "reg-1",
        Members = members.Select(m => new RosterMemberViewModel { Subject = m.subject, Role = m.role }).ToList(),
        MemberCount = members.Length,
    };

    [Fact]
    public async Task GetSystemInfoAsync_AggregatesAllFields()
    {
        _read.Setup(r => r.GetRegisterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register("reg-1", advertise: true, devMode: false));
        _read.Setup(r => r.GetLocalRelationshipAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Relationship(RegisterRoleSet.Owner));
        _read.Setup(r => r.GetSyncStateAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterSyncStateView("reg-1", RegisterSyncState.CaughtUp, 5, 5, 1, DateTimeOffset.UtcNow, false, null, null));
        _read.Setup(r => r.GetPublishedBlueprintCountAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        _governance.Setup(g => g.GetGovernanceRosterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Roster(("v1", "Validator"), ("v2", "Validator"), ("o1", "Owner")));

        var info = await CreateService().GetSystemInfoAsync("reg-1");

        info.IsLocallyOwned.Should().BeTrue();
        info.CallerRole.Should().Be(RegisterGovernanceRole.Owner);
        info.CallerCanPublish.Should().BeTrue();
        info.ValidatorCount.Should().Be(2);
        info.RequiredSignatures.Should().Be(2);
        info.IsPublic.Should().BeTrue();
        info.DevMode.Should().BeFalse();
        info.PublishedServiceCount.Should().Be(7);
    }

    [Fact]
    public async Task GetSystemInfoAsync_DesignerRole_CanPublish()
    {
        _read.Setup(r => r.GetRegisterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register("reg-1", advertise: false, devMode: true));
        _read.Setup(r => r.GetLocalRelationshipAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Relationship(RegisterRoleSet.Designer));
        _governance.Setup(g => g.GetGovernanceRosterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Roster());

        var info = await CreateService().GetSystemInfoAsync("reg-1");

        info.CallerRole.Should().Be(RegisterGovernanceRole.Designer);
        info.CallerCanPublish.Should().BeTrue();
        info.DevMode.Should().BeTrue();
        info.IsPublic.Should().BeFalse();
    }

    [Fact]
    public async Task GetSystemInfoAsync_NoRelationship_RoleIsNone()
    {
        _read.Setup(r => r.GetRegisterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register("reg-1", advertise: true, devMode: false));
        _read.Setup(r => r.GetLocalRelationshipAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegisterLocalRelationship?)null);
        _governance.Setup(g => g.GetGovernanceRosterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Roster());

        var info = await CreateService().GetSystemInfoAsync("reg-1");

        info.OwnershipAvailable.Should().BeFalse();
        info.CallerRole.Should().Be(RegisterGovernanceRole.None);
        info.CallerCanPublish.Should().BeFalse();
    }

    [Fact]
    public async Task GetSystemInfoAsync_FailedSubRead_DegradesThatFieldOnly()
    {
        _read.Setup(r => r.GetRegisterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register("reg-1", advertise: true, devMode: false));
        // Relationship read throws — must degrade ownership/role, not crash the card.
        _read.Setup(r => r.GetLocalRelationshipAsync("reg-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));
        _read.Setup(r => r.GetSyncStateAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegisterSyncStateView?)null);
        _read.Setup(r => r.GetPublishedBlueprintCountAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _governance.Setup(g => g.GetGovernanceRosterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Roster(("v1", "Validator")));

        var info = await CreateService().GetSystemInfoAsync("reg-1");

        info.OwnershipAvailable.Should().BeFalse("the failed relationship read degrades only that field");
        info.CallerRole.Should().Be(RegisterGovernanceRole.None);
        // Other fields still aggregate normally.
        info.IsPublic.Should().BeTrue();
        info.ValidatorCount.Should().Be(1);
        info.PublishedServiceCount.Should().Be(3);
    }
}
