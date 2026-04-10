// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json;

namespace Sorcha.Register.Models.Genesis;

/// <summary>
/// Loads the system register genesis file from a configured path or embedded assembly resource.
/// Resolution order: config file path → embedded resource → null.
/// </summary>
public static class GenesisFileLoader
{
    private const string EmbeddedResourceName = "Sorcha.Register.Models.Resources.system-register-genesis.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Loads the genesis file from the configured path or embedded resource.
    /// Returns null if no genesis is available.
    /// </summary>
    /// <param name="genesisFilePath">Optional path to a genesis JSON file (from SystemRegisterOptions.GenesisFile).</param>
    /// <returns>The deserialized genesis, or null if not found or the embedded resource is a placeholder.</returns>
    public static SystemRegisterGenesis? Load(string? genesisFilePath)
    {
        // 1. Try config file path
        if (!string.IsNullOrWhiteSpace(genesisFilePath))
        {
            if (!File.Exists(genesisFilePath))
                throw new FileNotFoundException(
                    $"System register genesis file not found at configured path: {genesisFilePath}",
                    genesisFilePath);

            var json = File.ReadAllText(genesisFilePath);
            return Deserialize(json);
        }

        // 2. Try embedded resource
        return LoadFromEmbeddedResource();
    }

    /// <summary>
    /// Loads the genesis file from the configured path or embedded resource.
    /// Returns null if no genesis is available.
    /// </summary>
    public static async Task<SystemRegisterGenesis?> LoadAsync(string? genesisFilePath, CancellationToken cancellationToken = default)
    {
        // 1. Try config file path
        if (!string.IsNullOrWhiteSpace(genesisFilePath))
        {
            if (!File.Exists(genesisFilePath))
                throw new FileNotFoundException(
                    $"System register genesis file not found at configured path: {genesisFilePath}",
                    genesisFilePath);

            var json = await File.ReadAllTextAsync(genesisFilePath, cancellationToken);
            return Deserialize(json);
        }

        // 2. Try embedded resource
        return LoadFromEmbeddedResource();
    }

    /// <summary>
    /// Extracts the genesis public key fingerprint (SHA-256 of public key, truncated to 32 hex chars).
    /// </summary>
    public static string ComputeFingerprint(byte[] publicKey)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(publicKey);
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static SystemRegisterGenesis? LoadFromEmbeddedResource()
    {
        var assembly = typeof(GenesisFileLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        // Placeholder check: empty JSON object means no real genesis embedded
        var trimmed = json.Trim();
        if (trimmed is "{}" or "")
            return null;

        return Deserialize(json);
    }

    private static SystemRegisterGenesis Deserialize(string json)
    {
        var genesis = JsonSerializer.Deserialize<SystemRegisterGenesis>(json, JsonOptions);
        if (genesis is null)
            throw new InvalidOperationException("Failed to deserialize system register genesis file.");

        if (genesis.Version != SystemRegisterGenesis.CurrentVersion)
            throw new InvalidOperationException(
                $"Unsupported genesis file version {genesis.Version}. Expected version {SystemRegisterGenesis.CurrentVersion}.");

        return genesis;
    }
}
