// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.WorkloadIdentity;

/// <summary>
/// Thrown when configured workload-certificate material cannot be loaded (missing file, wrong
/// password, malformed content). Deliberately a distinct type so callers fail FAST and LOUD at
/// startup — a configured certificate must never silently fall back to the shared-secret path
/// (F191 FR-009): a fallback good enough to hide a dead primary is how seam bugs ship.
/// </summary>
public sealed class WorkloadCertificateLoadException : InvalidOperationException
{
    public WorkloadCertificateLoadException(string message) : base(message)
    {
    }

    public WorkloadCertificateLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
