// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Sorcha.UI.Core.Models;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

public class LocalizationServiceTests
{
    private readonly Mock<HttpClient> _httpMock = new();
    private readonly Mock<IUserPreferencesService> _prefsMock = new();
    private readonly Mock<IJSRuntime> _jsMock = new();

    [Fact]
    public void T_MissingKey_ReturnsKeyAsIs()
    {
        var sut = CreateService();
        var result = sut.T("nonexistent.key");
        result.Should().Be("nonexistent.key");
    }

    [Fact]
    public void T_WithArgs_FormatsString()
    {
        var sut = CreateService();
        // Without loaded translations, returns key — testing the fallback
        var result = sut.T("some.key", "arg1");
        result.Should().Be("some.key");
    }

    [Fact]
    public void CurrentLanguage_DefaultsToEnglish()
    {
        var sut = CreateService();
        sut.CurrentLanguage.Should().Be("en");
    }

    [Fact]
    public async Task SetLanguageAsync_UpdatesCurrentLanguage()
    {
        var sut = CreateService();
        _prefsMock.Setup(p => p.UpdateUserPreferencesAsync(It.IsAny<UpdateUserPreferencesRequest>()))
            .ReturnsAsync(new UserPreferencesDto { Language = "fr" });

        var changed = false;
        sut.OnLanguageChanged += () => changed = true;

        await sut.SetLanguageAsync("fr");

        sut.CurrentLanguage.Should().Be("fr");
        changed.Should().BeTrue();
    }

    private LocalizationService CreateService()
    {
        return new LocalizationService(
            new HttpClient(), // Won't make real calls in unit tests
            _prefsMock.Object,
            _jsMock.Object,
            NullLogger<LocalizationService>.Instance);
    }

    /// <summary>
    /// Bug fix (Wallet PWA Settings → Security rendering raw keys, e.g. "settings.accounts.title"):
    /// the translation JSON moved from a plain wwwroot asset of the web host to a shared static web
    /// asset of THIS razor class library (Sorcha.UI.Components.User/wwwroot/i18n), served under the
    /// RCL's conventional "_content/{PackageId}/..." path. This proves the fetch actually hits that
    /// path (a leading-slash or host-relative typo would silently 404 and fall back to raw keys —
    /// exactly what a citizen saw) and that a resolved key returns real text, not the key itself.
    /// </summary>
    [Fact]
    public async Task LoadDefaultTranslationsAsync_FetchesFromComponentsUserContentPath_AndResolvesRealText()
    {
        string? requestedUri = null;
        var handler = new StubHandler((req, ct) =>
        {
            requestedUri = req.RequestUri!.ToString();
            var json = "{\"settings\":{\"accounts\":{\"title\":\"Sign-in methods\"}}}";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        });
        // BaseAddress mirrors the Wallet PWA's ambient HttpClient (base href "/wallet/").
        var http = new HttpClient(handler) { BaseAddress = new System.Uri("https://wallet.test/wallet/") };
        var sut = new LocalizationService(http, _prefsMock.Object, _jsMock.Object, NullLogger<LocalizationService>.Instance);

        await sut.LoadDefaultTranslationsAsync();

        requestedUri.Should().Be("https://wallet.test/wallet/_content/Sorcha.UI.Components.User/i18n/en.json");
        sut.T("settings.accounts.title").Should().Be("Sign-in methods",
            because: "once the resource is reachable, a real key must resolve to real text, not the raw key");
    }

    private sealed class StubHandler(
        System.Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request, cancellationToken);
    }
}
