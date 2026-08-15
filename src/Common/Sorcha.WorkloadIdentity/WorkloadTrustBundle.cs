// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sorcha.WorkloadIdentity;

/// <summary>
/// The installation's Workload CA trust bundle: one root normally, two during
/// <c>rotate-ca</c> overlap. Chain validation is pinned to EXACTLY these roots
/// (<see cref="X509ChainTrustMode.CustomRootTrust"/> — system/public roots are never
/// consulted) with revocation checking disabled by design: this is a private CA whose
/// revocation story is operational re-issue + restart (F191 research D5).
/// </summary>
public sealed class WorkloadTrustBundle
{
    /// <summary>The trusted roots (public certificates only).</summary>
    public IReadOnlyList<X509Certificate2> Roots { get; }

    private WorkloadTrustBundle(IReadOnlyList<X509Certificate2> roots)
    {
        Roots = roots;
    }

    /// <summary>Parses a PEM bundle (one or more CERTIFICATE blocks).</summary>
    /// <exception cref="WorkloadCertificateLoadException">No parseable certificate in the content.</exception>
    public static WorkloadTrustBundle FromPem(string pemContent)
    {
        if (string.IsNullOrWhiteSpace(pemContent))
            throw new WorkloadCertificateLoadException("Workload trust bundle content is empty.");

        X509Certificate2Collection collection = [];
        try
        {
            collection.ImportFromPem(pemContent);
        }
        catch (Exception ex)
        {
            throw new WorkloadCertificateLoadException("Workload trust bundle is not valid PEM certificate content.", ex);
        }

        if (collection.Count == 0)
            throw new WorkloadCertificateLoadException("Workload trust bundle contains no certificates.");

        return new WorkloadTrustBundle(collection.Cast<X509Certificate2>().ToList());
    }

    /// <summary>Loads a PEM bundle from disk, failing fast with the path in the message.</summary>
    /// <exception cref="WorkloadCertificateLoadException">Missing or unreadable file, or unparseable content.</exception>
    public static WorkloadTrustBundle LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new WorkloadCertificateLoadException("Workload trust bundle path is empty.");

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new WorkloadCertificateLoadException($"Cannot read workload trust bundle at '{path}'.", ex);
        }

        try
        {
            return FromPem(content);
        }
        catch (WorkloadCertificateLoadException ex)
        {
            throw new WorkloadCertificateLoadException($"Workload trust bundle at '{path}' is invalid: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Resolves a bundle from any supported configuration shape: an existing file path, inline
    /// PEM content, or base64-encoded PEM (the env-var delivery model — certificates travel in
    /// `.env` like every other per-deploy credential, avoiding bind-mount traps).
    /// </summary>
    /// <exception cref="WorkloadCertificateLoadException">The source matches no supported shape.</exception>
    public static WorkloadTrustBundle Resolve(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new WorkloadCertificateLoadException("Workload trust bundle source is empty.");

        if (File.Exists(source))
            return LoadFromFile(source);

        if (source.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal))
            return FromPem(source);

        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(source));
            if (decoded.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal))
                return FromPem(decoded);
        }
        catch (FormatException)
        {
            // fall through to the diagnostic below
        }

        throw new WorkloadCertificateLoadException(
            "Workload trust bundle source is neither an existing file, inline PEM, nor base64-encoded PEM.");
    }

    /// <summary>
    /// Validates that a presented certificate chains to one of the bundled roots and is within
    /// its validity period. Suitable for both Kestrel's client-certificate validation callback
    /// and outbound server-certificate pinning.
    /// </summary>
    public bool Validate(X509Certificate2 certificate, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        foreach (var root in Roots)
            chain.ChainPolicy.CustomTrustStore.Add(root);

        if (chain.Build(certificate))
        {
            failureReason = null;
            return true;
        }

        failureReason = string.Join("; ",
            chain.ChainStatus.Select(s => $"{s.Status}: {s.StatusInformation.Trim()}"));
        if (string.IsNullOrEmpty(failureReason))
            failureReason = "certificate does not chain to the workload trust bundle";
        return false;
    }

    /// <summary>
    /// Exports the PUBLIC certificates as a PEM bundle (never private keys — the bundle is
    /// mounted into every service).
    /// </summary>
    public static string ExportBundlePem(IEnumerable<X509Certificate2> certificates)
    {
        ArgumentNullException.ThrowIfNull(certificates);

        var writer = new System.Text.StringBuilder();
        foreach (var certificate in certificates)
        {
            writer.AppendLine(new string(PemEncoding.Write("CERTIFICATE", certificate.RawData)));
        }

        return writer.ToString();
    }

    /// <summary>This bundle's PEM representation (public roots only).</summary>
    public string ToPem() => ExportBundlePem(Roots);
}
