// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

public class PasskeyInteropContractTests
{
    [Fact]
    public async Task Fake_ReturnsAssertion()
    {
        IPasskeyInterop interop = new FakePasskeyInterop();
        (await interop.IsSupportedAsync()).Should().BeTrue();
        var resp = await interop.GetAssertionAsync(default);
        resp.GetProperty("id").GetString().Should().Be("abc");
    }
}

/// <summary>
/// Shared in-memory passkey interop fake — top-level + internal so the AuthService
/// tests (a later task) reuse it. Stands in for navigator.credentials.get() so no
/// test touches IJSRuntime (brittle to mock — F114 lesson).
/// </summary>
internal sealed class FakePasskeyInterop : IPasskeyInterop
{
    public bool Supported = true;
    public JsonElement Assertion = JsonDocument.Parse("{\"id\":\"abc\"}").RootElement.Clone();
    public Task<bool> IsSupportedAsync() => Task.FromResult(Supported);
    public Task<JsonElement> GetAssertionAsync(JsonElement options) => Task.FromResult(Assertion);
}
