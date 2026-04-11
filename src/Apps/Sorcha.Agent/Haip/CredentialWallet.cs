// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Agent.Haip;

/// <summary>
/// File-based storage for SD-JWT VC credentials. One file per credential type,
/// stored as raw SD-JWT compact serialisation in the wallet directory.
/// </summary>
public class CredentialWallet
{
    private readonly string _credentialDir;

    public CredentialWallet(string walletDir)
    {
        _credentialDir = Path.Combine(walletDir, "credentials");
        Directory.CreateDirectory(_credentialDir);
    }

    /// <summary>
    /// Stores a credential by type. Overwrites if the type already exists.
    /// </summary>
    public async Task StoreAsync(string credentialType, string rawSdJwt)
    {
        var path = GetPath(credentialType);
        await File.WriteAllTextAsync(path, rawSdJwt);
    }

    /// <summary>
    /// Loads a credential by type. Returns null if not found.
    /// </summary>
    public async Task<string?> LoadAsync(string credentialType)
    {
        var path = GetPath(credentialType);
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
    }

    /// <summary>
    /// Lists all stored credential types.
    /// </summary>
    public IEnumerable<string> ListTypes()
    {
        if (!Directory.Exists(_credentialDir))
            return [];

        return Directory.GetFiles(_credentialDir, "*.sdjwt")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null)
            .Cast<string>();
    }

    /// <summary>
    /// Checks whether a credential of the given type exists.
    /// </summary>
    public bool HasCredential(string credentialType) =>
        File.Exists(GetPath(credentialType));

    private string GetPath(string credentialType) =>
        Path.Combine(_credentialDir, $"{credentialType}.sdjwt");
}
