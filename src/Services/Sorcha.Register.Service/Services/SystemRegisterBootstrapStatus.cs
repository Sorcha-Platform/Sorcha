// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceDefaults;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Readiness signal for the system register bootstrap (Feature 197, #1466). Set by
/// <see cref="SystemRegisterBootstrapper"/> when it completes; read by the drift monitor and the
/// system blueprint health check, which must not judge the register before it exists.
/// </summary>
public interface ISystemRegisterBootstrapStatus
{
    /// <summary>True once the bootstrapper has logged completion.</summary>
    bool IsCompleted { get; }

    /// <summary>The mode the bootstrap completed under; meaningful only when <see cref="IsCompleted"/>.</summary>
    BootstrapMode Mode { get; }

    /// <summary>Marks the bootstrap complete under <paramref name="mode"/>.</summary>
    void MarkCompleted(BootstrapMode mode);
}

/// <summary>Thread-safe default <see cref="ISystemRegisterBootstrapStatus"/>; register as a singleton.</summary>
public sealed class SystemRegisterBootstrapStatus : ISystemRegisterBootstrapStatus
{
    private volatile bool _completed;
    private volatile int _mode;

    /// <inheritdoc />
    public bool IsCompleted => _completed;

    /// <inheritdoc />
    public BootstrapMode Mode => (BootstrapMode)_mode;

    /// <inheritdoc />
    public void MarkCompleted(BootstrapMode mode)
    {
        _mode = (int)mode;
        _completed = true;
    }
}
