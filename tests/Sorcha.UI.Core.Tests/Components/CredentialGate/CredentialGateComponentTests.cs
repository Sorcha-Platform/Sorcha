// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Core.Components.CredentialGate;
using Sorcha.UI.Core.Models.User.Presentation;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.CredentialGate;

/// <summary>
/// Feature 127 — bunit tests for <c>CredentialGateComponent</c>. Verifies
/// the state machine: NoGate (Init=null) renders ChildContent immediately;
/// Waiting renders the QR; Success fetches claims + renders ChildContent;
/// Decline / Abandoned / ManualRecoveryRequired render the right error UX.
/// </summary>
public sealed class CredentialGateComponentTests : BunitContext
{
    private readonly FakePresentationSignal _signal = new();
    private readonly StubHandler _handler = new();
    private readonly Mock<IQrPresentationService> _qr = new();

    public CredentialGateComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IPresentationSignal>(_signal);
        Services.AddSingleton(new HttpClient(_handler)
        {
            BaseAddress = new Uri("http://test.local")
        });
        // HybridQrAffordance (rendered when waiting) needs IQrPresentationService.
        _qr.Setup(q => q.GenerateSvgFromUri(It.IsAny<string>(), It.IsAny<int>()))
            .Returns("<svg data-testid=\"stub-qr\"/>");
        Services.AddSingleton(_qr.Object);
    }

    [Fact]
    public void NoGate_RendersChildContent_Immediately()
    {
        var cut = Render<CredentialGateComponent>(parameters => parameters
            .Add(p => p.Init, (CredentialGateInit?)null)
            .AddChildContent("<div data-testid=\"form-rendered\">application form</div>"));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=form-rendered]").TextContent.Should().Be("application form");
        });
    }

    [Fact]
    public void Waiting_State_RendersHybridQr_And_PromptCopy()
    {
        var init = NewInit();
        var cut = Render<CredentialGateComponent>(parameters => parameters
            .Add(p => p.Init, init)
            .AddChildContent("<div data-testid=\"form-rendered\">should not be visible yet</div>"));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Prove you're you");
            cut.Markup.Should().Contain("Scan the QR with your phone");
            // ChildContent must NOT render in waiting state.
            cut.FindAll("[data-testid=form-rendered]").Should().BeEmpty();
        });
    }

    [Fact]
    public async Task SuccessOutcome_FetchesClaims_AndRendersChildContent()
    {
        var init = NewInit();
        _handler.NextStatus = HttpStatusCode.OK;
        _handler.NextResponseJson = $"{{\"presentationRequestId\":\"{init.PresentationRequestId:D}\",\"status\":\"success\",\"claims\":{{\"givenName\":\"Sarah\",\"familyName\":\"Example\"}},\"subjectDisplayName\":\"Sarah Example\"}}";

        DisclosedClaimsResponse? captured = null;
        var cut = Render<CredentialGateComponent>(parameters => parameters
            .Add(p => p.Init, init)
            .Add(p => p.OnPresented, EventCallback.Factory.Create<DisclosedClaimsResponse>(this,
                resp => { captured = resp; return Task.CompletedTask; }))
            .AddChildContent("<div data-testid=\"form-rendered\">application form</div>"));

        // Trigger the outcome ready signal.
        await _signal.RaiseOutcomeAsync(new PresentationSignalOutcome(init.PresentationRequestId, "success"));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=form-rendered]").TextContent.Should().Be("application form");
            captured.Should().NotBeNull();
            captured!.Status.Should().Be("success");
            captured.SubjectDisplayName.Should().Be("Sarah Example");
        });
    }

    [Fact]
    public async Task DeclineOutcome_RendersDeclineMessage()
    {
        var init = NewInit();
        var cut = Render<CredentialGateComponent>(parameters => parameters
            .Add(p => p.Init, init)
            .AddChildContent("<div data-testid=\"form-rendered\">should not render</div>"));

        await _signal.RaiseOutcomeAsync(new PresentationSignalOutcome(init.PresentationRequestId, "decline"));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("We couldn't verify your credential");
            cut.FindAll("[data-testid=form-rendered]").Should().BeEmpty();
        });
    }

    [Fact]
    public async Task AbandonedOutcome_RendersAbandonedMessage()
    {
        var init = NewInit();
        var cut = Render<CredentialGateComponent>(parameters => parameters
            .Add(p => p.Init, init)
            .AddChildContent("<div data-testid=\"form-rendered\">should not render</div>"));

        await _signal.RaiseOutcomeAsync(new PresentationSignalOutcome(init.PresentationRequestId, "abandoned"));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Your wallet didn't respond in time");
        });
    }

    [Fact]
    public void ManualRecoveryRequired_RendersManualRecoveryMessage()
    {
        var init = NewInit();
        var cut = Render<CredentialGateComponent>(parameters => parameters
            .Add(p => p.Init, init));

        _signal.RaiseManualRecoveryRequired();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Couldn't reach your wallet");
        });
    }

    [Fact]
    public async Task SuccessOutcome_WithoutClaimsFetchToken_DegradesToDecline()
    {
        // Sanity: if the page supplied no ClaimsFetchToken (HAIP-shape consumer
        // where the gate can't autofill), the gate must NOT try to fetch and
        // must surface a clear error state.
        var init = new CredentialGateInit(
            PresentationRequestId: Guid.NewGuid(),
            AuthorizationRequestUri: "openid4vp://x",
            ClaimsFetchToken: null);
        var cut = Render<CredentialGateComponent>(parameters => parameters
            .Add(p => p.Init, init));

        await _signal.RaiseOutcomeAsync(new PresentationSignalOutcome(init.PresentationRequestId, "success"));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("We couldn't verify your credential");
        });
        _handler.RequestCount.Should().Be(0, "no claims-fetch request should fire without a token");
    }

    [Fact]
    public async Task ClaimsFetch_410Response_TreatsAsAbandoned()
    {
        var init = NewInit();
        _handler.NextStatus = HttpStatusCode.Gone;
        _handler.NextResponseJson = "{\"error\":\"outcome-abandoned\"}";

        var cut = Render<CredentialGateComponent>(parameters => parameters
            .Add(p => p.Init, init));

        await _signal.RaiseOutcomeAsync(new PresentationSignalOutcome(init.PresentationRequestId, "success"));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Your wallet didn't respond in time");
        });
    }

    private static CredentialGateInit NewInit() => new(
        PresentationRequestId: Guid.NewGuid(),
        AuthorizationRequestUri: "openid4vp://test?nonce=abc",
        ClaimsFetchToken: "test-token-value");

    private sealed class FakePresentationSignal : IPresentationSignal
    {
        public event Func<PresentationSignalOutcome, Task>? OnOutcomeReady;
        public event Action? OnFallbackEngaged;
        public event Action? OnManualRecoveryRequired;
        public event Action? OnRequestUnreachable;

        public Task StartAsync(Guid presentationRequestId, CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public async Task RaiseOutcomeAsync(PresentationSignalOutcome outcome)
        {
            if (OnOutcomeReady is not null)
            {
                await OnOutcomeReady.Invoke(outcome);
            }
        }

        public void RaiseFallbackEngaged() => OnFallbackEngaged?.Invoke();
        public void RaiseManualRecoveryRequired() => OnManualRecoveryRequired?.Invoke();
        public void RaiseRequestUnreachable() => OnRequestUnreachable?.Invoke();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpStatusCode NextStatus { get; set; } = HttpStatusCode.OK;
        public string NextResponseJson { get; set; } = "{}";
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(NextStatus)
            {
                Content = new StringContent(NextResponseJson, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
