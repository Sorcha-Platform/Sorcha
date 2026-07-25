// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using Sorcha.UI.Core.Models.Workflows;
using Sorcha.UI.Core.Services.Feedback;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Forms;

/// <summary>
/// Issue #1277 (UT-014) — the server drops an oversized portrait claim and says so, and the citizen
/// never hears about it.
/// <para>
/// The plumbing has existed since #340: <c>BuildClaimsFromMappings</c> appends a client-facing
/// message, <c>ActionSubmissionResponse.Warnings</c> carries it over the wire, and
/// <c>ActionSubmissionResultViewModel.Warnings</c> models it on the client. But #340 wired the
/// display at exactly ONE of the five submission call sites. The other four — including BOTH
/// async-encryption completion paths, which is where a portrait-bearing submission with a large
/// payload actually lands — silently discarded it.
/// </para>
/// <para>
/// The second test derives its subjects from the source tree rather than a hand-kept list, so a
/// sixth call site added later cannot quietly reintroduce the silence.
/// </para>
/// </summary>
public sealed class SubmissionWarningSurfacingTests
{
    [Fact]
    public void EveryWarningIsSurfaced_AndRequiresAcknowledgement()
    {
        var feedback = new Mock<IInlineFeedback>();
        var result = new ActionSubmissionResultViewModel
        {
            InstanceId = "i",
            Warnings = ["The portrait image for 'portrait' exceeded the limit and was omitted."],
        };

        result.SurfaceWarnings(feedback.Object);

        feedback.Verify(f => f.ShowWarning(
                It.Is<string>(m => m.Contains("portrait")),
                It.IsAny<string>(),
                // autoDismissMs: 0 — a dropped claim is not a 4-second nicety. The citizen is
                // holding a credential that is missing something they supplied.
                0),
            Times.Once);
    }

    [Fact]
    public void NoWarnings_SaysNothing()
    {
        var feedback = new Mock<IInlineFeedback>(MockBehavior.Strict);
        var result = new ActionSubmissionResultViewModel { InstanceId = "i" };

        result.SurfaceWarnings(feedback.Object);

        // Strict mock: any call at all would throw.
    }

    /// <summary>
    /// Derived, not hand-kept. Any file that submits an action must also surface the warnings the
    /// submission may come back with — that is the whole defect, and #340 fixing one site while
    /// four others stayed silent is exactly what a hand-kept list would have missed again.
    /// </summary>
    [Fact]
    public void EverySubmissionCallSiteSurfacesItsWarnings()
    {
        var appsRoot = Path.Combine(FindRepoRoot(AppContext.BaseDirectory), "src", "Apps");
        var offenders = new List<string>();
        var callSites = 0;

        foreach (var file in Directory.EnumerateFiles(appsRoot, "*.razor", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(appsRoot, "*.cs", SearchOption.AllDirectories)))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (!text.Contains("SubmitActionExecuteAsync(", StringComparison.Ordinal)) continue;

            // The service that DEFINES the call is not a call site.
            if (text.Contains("public async Task<ActionSubmissionResultViewModel?> SubmitActionExecuteAsync",
                    StringComparison.Ordinal)
                || text.Contains("Task<ActionSubmissionResultViewModel?> SubmitActionExecuteAsync(ActionExecuteRequest request, CancellationToken",
                    StringComparison.Ordinal))
            {
                continue;
            }

            callSites++;
            if (!text.Contains("SurfaceWarnings", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(appsRoot, file));
            }
        }

        callSites.Should().BeGreaterThan(1, "the scan is worthless if it finds nothing to check");
        offenders.Should().BeEmpty(
            "a submission that silently discards the server's warnings is how a citizen ends up "
            + "holding a portrait-less credential without ever being told");
    }

    private static string FindRepoRoot(string startDir)
    {
        var dir = startDir;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Sorcha.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repo root (dir containing Sorcha.sln) walking up from '{startDir}'.");
    }
}
