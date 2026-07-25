// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Core.Components.Verify;
using Sorcha.UI.Components.User.Models.Verification;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.UI.Testing;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Verify;

/// <summary>
/// Guards issue #1282, reported from the iOS TestFlight app: tapping Verify → "Confirm identity"
/// produced a QR, the raw <c>openid4vp://authorize?client_id=…</c> request URI as visible body text
/// clipped at the right edge, and no way off the screen.
/// <para>
/// The protocol side was correct and current (F181 US6 — signed request object, x509 SAN-DNS client
/// id, served <c>request_uri</c>), so this was purely a UI failure: a protocol string presented to a
/// citizen as if it were content.
/// </para>
/// </summary>
public sealed class VerificationSessionQrTests : ComponentTestFixture
{
    /// <summary>The exact deep link from the reported screenshot.</summary>
    private const string RealDeepLink =
        "openid4vp://authorize?client_id=x509_san_dns%3An1.sorcha.dev&request_uri=https%3A%2F%2Fn1."
        + "sorcha.dev%2Fhaip%2Fpresentations%2F1c9f8e77-43f7-8820-b50df99dbb0f%2Frequest-object";

    private readonly Mock<IVerificationTransport> _transport = new();

    private static VerificationPreset Preset() => new(
        Key: "confirm-identity",
        Label: "Confirm identity",
        Purpose: "Check who they are",
        RequiredVct: "https://sorcha.dev/vc/assured-identity/v1",
        RequiredClaims: ["fullName", "portrait"],
        OptionalClaims: [],
        KnownCredentialClaims: ["fullName", "portrait", "dateOfBirth"]);

    public VerificationSessionQrTests()
    {
        _transport
            .Setup(t => t.StartSessionAsync(It.IsAny<VerificationPreset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationSessionStarted("session-1", RealDeepLink, "Check who they are", "https://sorcha.dev/vc/assured-identity/v1"));
        // Never completes — hold the component in its QR-displayed state, which is the state under test.
        _transport
            .Setup(t => t.PollSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new VerificationSessionPoll(false, null, null);
            });
        Services.AddSingleton(_transport.Object);
    }

    private IRenderedComponent<VerificationSessionQr> RenderQr(EventCallback? onCancel = null)
        => Render<VerificationSessionQr>(ps =>
        {
            ps.Add(p => p.Question, Preset());
            if (onCancel.HasValue) ps.Add(p => p.OnCancel, onCancel.Value);
        });

    /// <summary>THE reported defect: a protocol URI must never be shown to a citizen as text.</summary>
    [Fact]
    public void QrScreen_NeverRendersTheRawProtocolUriAsVisibleText()
    {
        var cut = RenderQr();

        var link = cut.Find("[data-testid=deep-link]");
        link.TextContent.Should().NotContain(
            "openid4vp://", "a protocol string is not content — it looked like debug output left in (#1282)");
        link.TextContent.Should().NotContain("client_id");
        link.TextContent.Trim().Should().Be("Open in a wallet on this device");
    }

    /// <summary>
    /// The URI still has to reach the href — removing it from view must not break the same-device
    /// hand-off it exists for.
    /// </summary>
    [Fact]
    public void QrScreen_KeepsTheDeepLinkInTheHref()
        => cutHref().Should().Be(RealDeepLink, "the URI belongs in the href, only in the href");

    private string cutHref() => RenderQr().Find("[data-testid=deep-link]").GetAttribute("href")!;

    /// <summary>
    /// The screen carried no explanation of what to do with the QR — an operator holding a phone at a
    /// doorstep needs telling.
    /// </summary>
    [Fact]
    public void QrScreen_ExplainsWhatToDoWithTheCode()
    {
        var cut = RenderQr();

        cut.Find("[data-testid=qr-instruction]").TextContent.Should().Contain("scan");
        cut.Markup.Should().Contain("Confirm identity", "the operator must be able to see which check this is");
    }

    /// <summary>A visible waiting state, so a silent screen doesn't read as a hang.</summary>
    [Fact]
    public void QrScreen_ShowsThatItIsWaiting()
        => RenderQr().FindAll("[data-testid=qr-waiting]").Should().ContainSingle();

    /// <summary>
    /// #1282: there was no exit at all. Cancel renders only when the host supplies a handler — a dead
    /// control would be worse than none.
    /// </summary>
    [Fact]
    public void QrScreen_WithCancelHandler_OffersAWayOut()
    {
        var cancelled = false;
        var cut = RenderQr(EventCallback.Factory.Create(this, () => cancelled = true));

        var cancel = cut.Find("[data-testid=qr-cancel]");
        cancel.Click();

        cancelled.Should().BeTrue("the operator must be able to abandon a session they started");
    }

    [Fact]
    public void QrScreen_WithoutCancelHandler_RendersNoDeadControl()
        => RenderQr().FindAll("[data-testid=qr-cancel]").Should().BeEmpty();

    /// <summary>
    /// A terminal poll must surface the error state rather than crash the page. Before the fix this
    /// path called StateHasChanged() directly from the background poll task — off the renderer's
    /// dispatcher, which is the classic source of Blazor's "An unhandled error has occurred" bar and
    /// suppressed the verdict panel that should have rendered.
    /// </summary>
    [Fact]
    public void TerminalPoll_SurfacesTheErrorState_WithoutThrowing()
    {
        _transport
            .Setup(t => t.PollSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationSessionPoll(false, null, null, IsTerminal: true));

        var cut = RenderQr();

        cut.WaitForAssertion(
            () => cut.FindAll("[data-testid=transport-error]").Should().ContainSingle(),
            TimeSpan.FromSeconds(10));
    }
}
