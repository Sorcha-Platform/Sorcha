// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.UI.Core.Services.Authentication;

/// <summary>
/// Proactive background token refresh service.
/// Schedules refresh before token expiry and handles tab visibility changes.
/// </summary>
public interface ITokenRefreshService : IDisposable
{
    /// <summary>
    /// Starts the background refresh timer and registers tab visibility detection.
    /// </summary>
    /// <param name="jsRuntime">JS runtime for interop (visibility change detection)</param>
    Task StartAsync(IJSRuntime jsRuntime);

    /// <summary>
    /// Stops the background refresh timer and cleans up JS interop.
    /// </summary>
    Task StopAsync();
}
