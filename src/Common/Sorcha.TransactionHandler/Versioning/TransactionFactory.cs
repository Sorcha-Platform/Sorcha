// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System;
using Sorcha.TransactionHandler.Core;
using Sorcha.TransactionHandler.Enums;
using Sorcha.TransactionHandler.Interfaces;
using Sorcha.TransactionHandler.Payload;
using Sorcha.TransactionHandler.Serialization;
using Sorcha.Cryptography.Interfaces;

namespace Sorcha.TransactionHandler.Versioning;

/// <summary>
/// Factory for creating and deserializing transactions with version support.
/// </summary>
public class TransactionFactory : ITransactionFactory
{
    private readonly ICryptoModule _cryptoModule;
    private readonly IHashProvider _hashProvider;
    private readonly ISymmetricCrypto _symmetricCrypto;
    private readonly IVersionDetector _versionDetector;

    /// <summary>
    /// Initializes a new instance of the TransactionFactory class.
    /// </summary>
    public TransactionFactory(
        ICryptoModule cryptoModule,
        IHashProvider hashProvider,
        ISymmetricCrypto symmetricCrypto,
        IVersionDetector versionDetector)
    {
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        _symmetricCrypto = symmetricCrypto ?? throw new ArgumentNullException(nameof(symmetricCrypto));
        _versionDetector = versionDetector ?? throw new ArgumentNullException(nameof(versionDetector));
    }

    /// <inheritdoc/>
    public ITransaction Create(TransactionVersion version)
    {
        return version switch
        {
            TransactionVersion.V1 => CreateV1Transaction(),
            _ => throw new NotSupportedException($"Transaction version {version} is not supported")
        };
    }

    /// <inheritdoc/>
    public ITransaction Deserialize(byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentNullException(nameof(data));

        // Detect version from binary data
        var version = _versionDetector.DetectVersion(data);

        // Get appropriate serializer for the version
        var serializer = GetSerializer(version);

        // Deserialize using version-specific serializer
        return serializer.DeserializeFromBinary(data);
    }

    /// <inheritdoc/>
    public ITransaction Deserialize(string json)
    {
        if (string.IsNullOrEmpty(json))
            throw new ArgumentNullException(nameof(json));

        // Detect version from JSON
        var version = _versionDetector.DetectVersion(json);

        // Get appropriate serializer for the version
        var serializer = GetSerializer(version);

        // Deserialize using version-specific serializer
        return serializer.DeserializeFromJson(json);
    }

    private ITransaction CreateV1Transaction()
    {
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        return new Transaction(_cryptoModule, _hashProvider, payloadManager, TransactionVersion.V1);
    }

    private ITransactionSerializer GetSerializer(TransactionVersion version)
    {
        // TODO: Implement version-based serializer routing when V2 format is defined
        // Currently only V1 serializers are available — version parameter intentionally unused
        _ = version;
        return new BinaryTransactionSerializer(_cryptoModule, _hashProvider, _symmetricCrypto);
    }

}
