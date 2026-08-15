// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography.X509Certificates;
using Sorcha.WorkloadIdentity;

namespace Sorcha.Cli.Commands.WorkloadCa;

/// <summary>
/// Filesystem layout + IO for the per-installation workload-certificate directory (F191).
/// Layout is the operator contract (specs/191 data-model.md):
/// <code>
/// {dir}/ca/ca.pfx            — Workload CA (private key; CLI-only, never mounted into services)
/// {dir}/ca/ca.previous.pfx   — previous CA during rotate-ca overlap
/// {dir}/ca/bundle.pem        — PUBLIC trust bundle (1 root; 2 during overlap)
/// {dir}/services/{id}.pfx    — per-service leaf (mounted ONLY into its own service)
/// {dir}/server/tenant-service.pfx — mTLS listener server certificate
/// </code>
/// All writes are temp+rename so a failed run never leaves a torn artifact.
/// </summary>
internal sealed class WorkloadCaStore
{
    private readonly string _dir;
    private readonly string? _password;

    public WorkloadCaStore(string dir, string? password)
    {
        _dir = dir;
        _password = password;
    }

    public string Root => _dir;
    public string CaPfxPath => Path.Combine(_dir, "ca", "ca.pfx");
    public string PreviousCaPfxPath => Path.Combine(_dir, "ca", "ca.previous.pfx");
    public string BundlePath => Path.Combine(_dir, "ca", "bundle.pem");
    public string ServicesDir => Path.Combine(_dir, "services");
    public string ServerPfxPath => Path.Combine(_dir, "server", "tenant-service.pfx");

    public string LeafPath(string clientId) => Path.Combine(ServicesDir, $"{clientId}.pfx");

    public bool Exists => Directory.Exists(_dir);

    public void EnsureLayout()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "ca"));
        Directory.CreateDirectory(ServicesDir);
        Directory.CreateDirectory(Path.Combine(_dir, "server"));
    }

    /// <summary>Loads a PFX or returns null when the file is absent. Wrong password/corrupt content throws.</summary>
    public X509Certificate2? TryLoadPfx(string path) =>
        File.Exists(path) ? WorkloadCertificateLoader.Load(path, _password) : null;

    /// <summary>Like <see cref="TryLoadPfx"/> but classifies unloadable content as null (for status/init repair).</summary>
    public X509Certificate2? TryLoadPfxLenient(string path)
    {
        try
        {
            return TryLoadPfx(path);
        }
        catch (WorkloadCertificateLoadException)
        {
            return null;
        }
    }

    public WorkloadTrustBundle? TryLoadBundle() =>
        File.Exists(BundlePath) ? WorkloadTrustBundle.LoadFromFile(BundlePath) : null;

    public IReadOnlyList<(string ClientId, string Path)> EnumerateLeaves() =>
        Directory.Exists(ServicesDir)
            ? Directory.GetFiles(ServicesDir, "*.pfx")
                .Select(p => (Path.GetFileNameWithoutExtension(p), p))
                .OrderBy(t => t.Item1, StringComparer.Ordinal)
                .ToList()
            : [];

    public void SavePfx(string path, X509Certificate2 certificate) =>
        WriteAtomically(path, certificate.Export(X509ContentType.Pfx, _password));

    public void SaveBundle(IEnumerable<X509Certificate2> roots) =>
        WriteAtomically(BundlePath, System.Text.Encoding.UTF8.GetBytes(
            WorkloadTrustBundle.ExportBundlePem(roots)));

    private static void WriteAtomically(string path, byte[] content)
    {
        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, content);
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>Parses a `--services id=dnshost,id=dnshost` override; null/empty → the default 8-principal map.</summary>
    public static IReadOnlyDictionary<string, string> ResolveServiceMap(string? servicesOverride)
    {
        if (string.IsNullOrWhiteSpace(servicesOverride))
            return WorkloadIdentityConfig.DefaultServiceDnsMap;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in servicesOverride.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts.Any(string.IsNullOrEmpty))
                throw new ArgumentException($"Invalid --services entry '{pair}' — expected client_id=dns-host.");
            map[parts[0]] = parts[1];
        }

        return map;
    }

    /// <summary>DNS host for a leaf: the service map when known, else the client id itself.</summary>
    public static string DnsHostFor(string clientId, IReadOnlyDictionary<string, string> serviceMap) =>
        serviceMap.TryGetValue(clientId, out var dns) ? dns : clientId;
}
