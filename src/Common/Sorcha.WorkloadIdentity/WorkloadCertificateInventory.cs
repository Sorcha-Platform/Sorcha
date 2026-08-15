// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography.X509Certificates;

namespace Sorcha.WorkloadIdentity;

/// <summary>Lifecycle state of a workload certificate relative to a renewal threshold.</summary>
public enum WorkloadCertificateState
{
    Ok,
    Expiring,
    Expired,
    Invalid,
}

/// <summary>Expiry snapshot for one workload certificate.</summary>
/// <param name="Kind">Artifact kind (e.g. <c>ca</c>, <c>service-leaf</c>, <c>server</c>).</param>
/// <param name="Subject">Certificate subject DN.</param>
/// <param name="Identity">SPIFFE id when the certificate carries one; DNS SAN identity otherwise; null when neither.</param>
/// <param name="NotAfter">Expiry instant (UTC).</param>
/// <param name="DaysRemaining">Whole days until expiry (negative when past).</param>
/// <param name="State">Classification against the threshold.</param>
public sealed record WorkloadCertificateStatus(
    string Kind,
    string Subject,
    string? Identity,
    DateTimeOffset NotAfter,
    int DaysRemaining,
    WorkloadCertificateState State);

/// <summary>
/// Shared expiry inspection used by the CLI (<c>status</c>/<c>renew</c>) and the ServiceDefaults
/// health check — one classification, two surfaces.
/// </summary>
public static class WorkloadCertificateInventory
{
    /// <summary>Describes a certificate's expiry state against a renewal threshold.</summary>
    public static WorkloadCertificateStatus Describe(
        string kind,
        X509Certificate2 certificate,
        DateTimeOffset now,
        int thresholdDays)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        var remaining = notAfter - now;
        var daysRemaining = (int)Math.Floor(remaining.TotalDays);

        var state = daysRemaining < 0
            ? WorkloadCertificateState.Expired
            : daysRemaining <= thresholdDays
                ? WorkloadCertificateState.Expiring
                : WorkloadCertificateState.Ok;

        string? identity = WorkloadCertificateAuthority.TryGetSpiffeId(certificate, out var spiffeId)
            ? spiffeId!.ToString()
            : null;

        return new WorkloadCertificateStatus(
            kind,
            certificate.Subject,
            identity,
            notAfter,
            daysRemaining,
            state);
    }
}
