// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Sorcha.Validator.Service.Diagnostics;

/// <summary>
/// Zero-allocation timing scope. Returned by <see cref="RuleTelemetry.TimeRule"/>
/// and <see cref="RuleTelemetry.TimeSection"/>. When telemetry is disabled the
/// scope is <c>default</c> (Code is null) and Dispose is a no-op the JIT can
/// elide entirely.
/// </summary>
public readonly struct RuleScope : IDisposable
{
    private readonly string? _code;
    private readonly long _startTicks;
    private readonly bool _isSection;

    internal RuleScope(string code, long startTicks, bool isSection)
    {
        _code = code;
        _startTicks = startTicks;
        _isSection = isSection;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_code is null) return;
        var elapsed = Stopwatch.GetTimestamp() - _startTicks;
        RuleTelemetry.Record(_code, elapsed, _isSection);
    }
}
