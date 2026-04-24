// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Services.Infrastructure;

/// <summary>
/// Test-seam for time. Injected into components whose behaviour depends on
/// absolute time (TTL windows, sweeper ticks, late-outcome detection) so tests
/// can control the clock deterministically.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Default implementation — delegates to <see cref="DateTimeOffset.UtcNow"/>.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
