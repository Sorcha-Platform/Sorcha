// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.JSInterop;
using Moq;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Mobile login-crash regression (branch fix/wallet-pwa-mobile-backend-url).
/// On a non-secure origin (the Capacitor <c>capacitor://localhost</c> custom
/// scheme on iOS, or a non-localhost device origin) <c>crypto.subtle</c> is
/// absent, so <c>js/indexeddb-bridge.js</c> returns early and never defines
/// <c>globalThis.SorchaIndexedDb</c>. The first interop call then throws
/// <see cref="JSException"/> ("Could not find 'SorchaIndexedDb.get'
/// ('SorchaIndexedDb' was undefined)."). Because <c>IsSignedInAsync</c> →
/// <c>GetAsync</c> sits unguarded on the sign-in load path, that uncaught
/// throw white-screened the app via the ErrorBoundary instead of showing a
/// usable sign-in form. The read paths must degrade to "signed out" (null)
/// rather than propagate.
/// </summary>
public sealed class IndexedDbAccessTokenStoreResilienceTests
{
    private static readonly JSException IndexedDbMissing =
        new("Could not find 'SorchaIndexedDb.get' ('SorchaIndexedDb' was undefined).");

    private static Mock<IJSRuntime> JsThatThrowsOnGet()
    {
        var js = new Mock<IJSRuntime>();
        js.Setup(j => j.InvokeAsync<AccessTokenRecord?>(
                It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
          .Returns(ValueTask.FromException<AccessTokenRecord?>(IndexedDbMissing));
        return js;
    }

    [Fact]
    public async Task GetAsync_WhenIndexedDbBridgeUndefined_ReturnsNull_DoesNotThrow()
    {
        var store = new IndexedDbAccessTokenStore(JsThatThrowsOnGet().Object);

        var act = async () => await store.GetAsync();

        (await act.Should().NotThrowAsync()).Subject.Should().BeNull(
            "a dead IndexedDB bridge must read as signed-out, not crash the sign-in screen");
    }

    [Fact]
    public async Task GetHomeAsync_WhenIndexedDbBridgeUndefined_ReturnsNull_DoesNotThrow()
    {
        var store = new IndexedDbAccessTokenStore(JsThatThrowsOnGet().Object);

        var act = async () => await store.GetHomeAsync();

        (await act.Should().NotThrowAsync()).Subject.Should().BeNull();
    }
}
