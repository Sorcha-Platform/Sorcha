// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography.X509Certificates;

namespace Sorcha.WorkloadIdentity;

/// <summary>
/// Loads workload certificate material configured as either a PFX file path or a base64
/// PKCS#12 blob (the Haip <c>VerifierCertificate</c> loading pattern). Any failure throws
/// <see cref="WorkloadCertificateLoadException"/> — configured material that cannot load is a
/// startup failure, never a silent fallback to the shared secret (F191 FR-009).
/// </summary>
public static class WorkloadCertificateLoader
{
    /// <summary>Loads a PKCS#12 certificate + private key from a file path or base64 blob.</summary>
    /// <exception cref="WorkloadCertificateLoadException">Missing file / undecodable blob / wrong password / malformed content.</exception>
    public static X509Certificate2 Load(string pathOrBase64, string? password)
    {
        if (string.IsNullOrWhiteSpace(pathOrBase64))
            throw new WorkloadCertificateLoadException("Workload certificate source is empty.");

        byte[] pkcs12Bytes;
        string sourceDescription;

        if (File.Exists(pathOrBase64))
        {
            sourceDescription = $"file '{pathOrBase64}'";
            try
            {
                pkcs12Bytes = File.ReadAllBytes(pathOrBase64);
            }
            catch (Exception ex)
            {
                throw new WorkloadCertificateLoadException($"Cannot read workload certificate {sourceDescription}.", ex);
            }
        }
        else
        {
            sourceDescription = $"configured value (not a file on disk: '{pathOrBase64}')";
            try
            {
                pkcs12Bytes = Convert.FromBase64String(pathOrBase64);
            }
            catch (FormatException ex)
            {
                throw new WorkloadCertificateLoadException(
                    $"Workload certificate source is neither an existing file nor valid base64: '{pathOrBase64}'.", ex);
            }
        }

        try
        {
            return X509CertificateLoader.LoadPkcs12(
                pkcs12Bytes,
                password,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (Exception ex)
        {
            throw new WorkloadCertificateLoadException(
                $"Workload certificate {sourceDescription} could not be loaded as PKCS#12 (wrong password or corrupt content).", ex);
        }
    }
}
