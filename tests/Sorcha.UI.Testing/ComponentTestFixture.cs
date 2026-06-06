// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;

namespace Sorcha.UI.Testing;

/// <summary>
/// Base fixture for bUnit component tests across the Sorcha web app and the
/// Citizen Wallet PWA. Wires the boilerplate every component test needs —
/// MudBlazor services and a loose JSInterop — so a new test only has to
/// register the mocks it actually exercises and call <see cref="BunitContext.Render"/>.
/// </summary>
/// <remarks>
/// JSInterop is set to <see cref="JSRuntimeMode.Loose"/> so MudBlazor's
/// internal interop (popovers, scroll listeners, etc.) and any component
/// JS calls resolve to no-ops by default. Tests that assert specific interop
/// can still <c>JSInterop.Setup(...)</c> / <c>VerifyInvoke(...)</c> on top.
/// </remarks>
public abstract class ComponentTestFixture : BunitContext
{
    protected ComponentTestFixture()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    /// <summary>
    /// Registers a fresh <see cref="Mock{T}"/> as the singleton implementation
    /// of <typeparamref name="T"/> and returns the mock so the test can set up
    /// expectations and verify calls.
    /// </summary>
    protected Mock<T> ProvideMock<T>() where T : class
    {
        var mock = new Mock<T>();
        Services.AddSingleton(mock.Object);
        return mock;
    }

    /// <summary>Registers a concrete instance as the singleton for <typeparamref name="T"/>.</summary>
    protected T Provide<T>(T instance) where T : class
    {
        Services.AddSingleton(instance);
        return instance;
    }
}
