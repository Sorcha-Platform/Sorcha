// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using Sorcha.Cli.Infrastructure;

namespace Sorcha.Cli.Tests.Utilities;

/// <summary>
/// A simple encryption provider for testing that uses Base64 encoding.
/// Not secure — only for unit/integration test use where real encryption is unnecessary.
/// </summary>
public class TestEncryptionProvider : IEncryptionProvider
{
    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task<byte[]> EncryptAsync(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        return Task.FromResult(bytes);
    }

    /// <inheritdoc />
    public Task<string> DecryptAsync(byte[] ciphertext)
    {
        var plaintext = Encoding.UTF8.GetString(ciphertext);
        return Task.FromResult(plaintext);
    }
}
