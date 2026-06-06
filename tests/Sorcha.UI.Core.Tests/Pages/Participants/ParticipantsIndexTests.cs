// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Moq;
using Sorcha.UI.Core.Models.Participants;
using Sorcha.UI.Core.Services.Participants;
using Sorcha.UI.Core.Services.Feedback;
using Sorcha.UI.Testing;
using Xunit;
using ParticipantsPage = Sorcha.UI.Web.Client.Pages.Participants.Index;

namespace Sorcha.UI.Core.Tests.Pages.Participants;

/// <summary>
/// bUnit tests for the Participants admin page's organisation-context
/// resolution. The page used to hard-code OrganizationId = Guid.Empty (always
/// "No organization selected"); it now resolves the active org from the user's
/// org_id claim, falling back to the first participant profile.
/// </summary>
public sealed class ParticipantsIndexTests : ComponentTestFixture
{
    private readonly Mock<IParticipantApiService> _participants = new();

    public ParticipantsIndexTests()
    {
        _participants
            .Setup(s => s.GetMyProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantDetailViewModel>());
        // The list tab fetches a page of participants once an org is resolved.
        _participants
            .Setup(s => s.ListParticipantsAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantListViewModel());
        Services.AddSingleton(_participants.Object);
        ProvideMock<IInlineFeedback>();
    }

    // Render the page alongside a MudPopoverProvider so the MudTable inside
    // ParticipantList (which the resolved-org branch renders) has the popover
    // host it asserts on. In the app the provider lives in MainLayout.
    private static readonly RenderFragment PageWithProvider = builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<ParticipantsPage>(1);
        builder.CloseComponent();
    };

    [Fact]
    public void NoOrgContext_ShowsNoOrganizationWarning()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("admin@test.local");
        // No org_id claim and no profiles → org stays empty → warning shows.

        var cut = Render(PageWithProvider);

        cut.Markup.Should().Contain("No organization selected");
    }

    [Fact]
    public void OrgIdClaim_ResolvesOrg_AndHidesWarning()
    {
        var orgId = Guid.NewGuid();
        var auth = AddAuthorization();
        auth.SetAuthorized("admin@test.local");
        auth.SetClaims(new Claim("org_id", orgId.ToString()));

        var cut = Render(PageWithProvider);

        cut.Markup.Should().NotContain("No organization selected");
    }
}
