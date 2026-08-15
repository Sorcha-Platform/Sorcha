// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.WorkloadIdentity;

/// <summary>
/// Canonical configuration keys and defaults for workload-identity service auth (F191).
/// Call sites bind through these constants — never string literals — so the client, server,
/// CLI, and health-check surfaces cannot drift apart (the repo's "exactly one home" discipline).
/// </summary>
public static class WorkloadIdentityConfig
{
    // ── Client side (every service) ────────────────────────────────────────────────
    /// <summary>PFX file path or base64 PKCS#12 blob; presence activates certificate mode.</summary>
    public const string ClientCertificate = "ServiceAuth:ClientCertificate";
    public const string ClientCertificatePassword = "ServiceAuth:ClientCertificatePassword";
    /// <summary>PEM bundle path used to pin the mint endpoint's server certificate.</summary>
    public const string TrustBundle = "ServiceAuth:TrustBundle";
    /// <summary>Token mint base address in certificate mode.</summary>
    public const string MtlsTokenAddress = "ServiceAuth:MtlsTokenAddress";
    public const string DefaultMtlsTokenAddress = "https://tenant-service:8443";

    // ── Server side (Tenant) ───────────────────────────────────────────────────────
    /// <summary>Server PFX for the mTLS listener; presence (with the bundle) activates it.</summary>
    public const string MtlsServerCertificate = "ServiceAuth:Mtls:ServerCertificate";
    public const string MtlsServerCertificatePassword = "ServiceAuth:Mtls:ServerCertificatePassword";
    /// <summary>PEM bundle used to chain-validate client certificates at the TLS layer.</summary>
    public const string MtlsTrustBundle = "ServiceAuth:Mtls:TrustBundle";
    public const string MtlsPort = "ServiceAuth:Mtls:Port";
    public const int DefaultMtlsPort = 8443;
    /// <summary>When true, secret-based service authentication is refused platform-wide.</summary>
    public const string DisableSharedSecrets = "ServiceAuth:DisableSharedSecrets";

    // ── Observability ──────────────────────────────────────────────────────────────
    /// <summary>Degraded window (days) for the health check; CLI status/renew threshold.</summary>
    public const string ExpiryWarningDays = "WorkloadIdentity:ExpiryWarningDays";
    public const int DefaultExpiryWarningDays = 30;

    /// <summary>Health-check registration name.</summary>
    public const string HealthCheckName = "workload-certificate";

    /// <summary>OpenTelemetry meter name for workload-identity instruments.</summary>
    public const string MeterName = "Sorcha.WorkloadIdentity";

    /// <summary>
    /// The v1 issuance universe: seeded service-principal client ids mapped to their
    /// docker-compose DNS hostnames (data-model.md; verifier hostname confirmed against
    /// docker-compose.yml service `sorcha-verifier`).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultServiceDnsMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["service-blueprint"] = "blueprint-service",
            ["service-wallet"] = "wallet-service",
            ["register-service"] = "register-service",
            ["service-peer"] = "peer-service",
            ["validator-service"] = "validator-service",
            ["tenant-service"] = "tenant-service",
            ["service-haip"] = "haip-service",
            ["service-verifier"] = "sorcha-verifier",
        };
}
