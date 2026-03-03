// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Wallet.Service.Tests.Helpers;

/// <summary>
/// Test implementation of IMeterFactory that creates real meters without export.
/// Used to satisfy NotificationMetrics constructor in unit tests.
/// </summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options) => new(options);
    public void Dispose() { }
}
