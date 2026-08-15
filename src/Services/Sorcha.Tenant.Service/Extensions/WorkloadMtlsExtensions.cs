// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Sorcha.WorkloadIdentity;

namespace Sorcha.Tenant.Service.Extensions;

/// <summary>
/// F191 (#1420): additive Kestrel mTLS listener for workload-certificate service auth.
/// Activated only when BOTH <c>ServiceAuth:Mtls:ServerCertificate</c> and
/// <c>ServiceAuth:Mtls:TrustBundle</c> are configured; otherwise a no-op, leaving the
/// deployment byte-for-byte on its existing listeners. Client certificates are REQUIRED on
/// this listener and chain-validated against the workload trust bundle at handshake, so any
/// certificate that reaches a handler on this port is already trusted material — handlers only
/// match its SPIFFE identity to the requested client id.
/// </summary>
public static class WorkloadMtlsExtensions
{
    /// <summary>
    /// Adds the workload mTLS listener when configured. Fail-fast: partially-configured or
    /// unreadable material throws at startup (never a silent fallback — FR-009).
    /// </summary>
    public static WebApplicationBuilder AddWorkloadMtlsListener(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var serverCertSource = configuration[WorkloadIdentityConfig.MtlsServerCertificate];
        var bundleSource = configuration[WorkloadIdentityConfig.MtlsTrustBundle];

        var serverCertConfigured = !string.IsNullOrWhiteSpace(serverCertSource);
        var bundleConfigured = !string.IsNullOrWhiteSpace(bundleSource);

        if (!serverCertConfigured && !bundleConfigured)
        {
            return builder; // workload mTLS not configured — deployment unchanged
        }

        if (!serverCertConfigured || !bundleConfigured)
        {
            throw new WorkloadCertificateLoadException(
                $"Workload mTLS listener is partially configured: both '{WorkloadIdentityConfig.MtlsServerCertificate}' " +
                $"and '{WorkloadIdentityConfig.MtlsTrustBundle}' must be set together.");
        }

        // Load eagerly so a broken deployment fails at startup, not at first mint.
        var serverCertificate = WorkloadCertificateLoader.Load(
            serverCertSource!,
            configuration[WorkloadIdentityConfig.MtlsServerCertificatePassword]);
        var trustBundle = WorkloadTrustBundle.LoadFromFile(bundleSource!);

        var mtlsPort = int.TryParse(configuration[WorkloadIdentityConfig.MtlsPort], out var configuredPort)
            ? configuredPort
            : WorkloadIdentityConfig.DefaultMtlsPort;

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // Code-level Listen* calls make Kestrel IGNORE the address-based configuration
            // (ASPNETCORE_URLS) with only a startup warning — which would silently kill the
            // plaintext :8080 listener every other caller depends on. Re-bind the configured
            // URLs explicitly so the mTLS listener is genuinely ADDITIVE.
            RebindConfiguredUrls(kestrel, configuration["urls"]);

            kestrel.ListenAnyIP(mtlsPort, listenOptions =>
            {
                listenOptions.UseHttps(https =>
                {
                    https.ServerCertificate = serverCertificate;
                    https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    // Replaces Kestrel's default client-cert validation entirely: chain to the
                    // workload bundle (CustomRootTrust, no revocation) or the handshake fails.
                    https.ClientCertificateValidation = (certificate, _, _) =>
                        trustBundle.Validate(certificate, out _);
                });
            });
        });

        return builder;
    }

    private static void RebindConfiguredUrls(KestrelServerOptions kestrel, string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
        {
            return; // no address-based config to preserve
        }

        foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var address = BindingAddress.Parse(url);
            if (!string.Equals(address.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                // An https URL would need its own certificate wiring; combining one with the
                // workload mTLS listener is not a supported topology — fail loud, not half-bound.
                throw new WorkloadCertificateLoadException(
                    $"Workload mTLS listener cannot preserve non-HTTP configured URL '{url}'. " +
                    "Run the service's existing listeners on HTTP and let the mTLS listener own TLS.");
            }

            if (address.Host is "+" or "*" or "0.0.0.0" or "[::]")
            {
                kestrel.ListenAnyIP(address.Port);
            }
            else if (string.Equals(address.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                kestrel.ListenLocalhost(address.Port);
            }
            else if (System.Net.IPAddress.TryParse(address.Host, out var ip))
            {
                kestrel.Listen(ip, address.Port);
            }
            else
            {
                kestrel.ListenAnyIP(address.Port);
            }
        }
    }
}
