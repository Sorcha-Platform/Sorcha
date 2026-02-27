# Sorcha.TransactionHandler

A comprehensive transaction management library for the Sorcha distributed ledger platform, providing transaction creation, signing, verification, multi-recipient payload encryption, and serialization.

## Features

### Transaction Management
- **Fluent API** for transaction creation via `TransactionBuilder`
- **Digital Signatures** using ED25519 with double SHA-256 hashing
- **Transaction Verification** with signature and payload validation
- **Multi-recipient Support** with per-recipient access control
- **Transaction Chaining** via previous transaction hash references

### Payload Management
- **Multi-recipient Encryption** using XChaCha20-Poly1305
- **Per-recipient Access Control** with encrypted symmetric keys
- **Payload Compression** with configurable compression levels
- **Payload Verification** with hash validation
- **Dynamic Access Management** (grant/revoke access)

### Serialization
- **Binary Serialization** with VarInt encoding for efficient network transmission
- **JSON Serialization** for human-readable APIs and debugging
- **Transport Packets** optimized for network protocols
- **Version Detection** for backward compatibility

### Versioning
- **Read Support** for transaction versions v1-v4
- **Write Support** for v4 (current version)
- **Automatic Version Detection** from binary and JSON data
- **Version-specific Factories** for creating transactions

## Installation

```bash
dotnet add package Sorcha.TransactionHandler
```

## Quick Start

### Creating and Signing a Transaction

```csharp
using Sorcha.TransactionHandler.Core;
using Sorcha.TransactionHandler.Enums;
using Sorcha.Cryptography.Core;

// Initialize dependencies
var cryptoModule = new CryptoModule();
var hashProvider = new HashProvider();
var builder = new TransactionBuilder(cryptoModule, hashProvider);

// Create and sign a transaction
var result = await builder
    .Create(TransactionVersion.V1)
    .WithRecipients("ws1recipient1", "ws1recipient2")
    .WithMetadata("{\"type\": \"transfer\", \"amount\": 100}")
    .SignAsync(privateKeyWif);

var transaction = result.Build().Value;
```

### Adding Encrypted Payloads

```csharp
// Add payload accessible to specific recipients
var payloadData = System.Text.Encoding.UTF8.GetBytes("Sensitive data");

var result = await builder
    .Create()
    .WithRecipients("ws1recipient1", "ws1recipient2")
    .AddPayload(payloadData, new[] { "ws1recipient1" })
    .AddPayload(anotherPayload, new[] { "ws1recipient1", "ws1recipient2" })
    .SignAsync(privateKeyWif);
```

### Verifying Transactions

```csharp
// Verify transaction signature and payloads
var status = await transaction.VerifyAsync();

if (status == TransactionStatus.Success)
{
    Console.WriteLine("Transaction is valid");
}
```

### Serialization

```csharp
// Binary serialization (compact)
var serializer = new BinaryTransactionSerializer(cryptoModule, hashProvider);
var binaryData = serializer.SerializeToBinary(transaction);
var deserialized = serializer.DeserializeFromBinary(binaryData);

// JSON serialization (human-readable)
var jsonSerializer = new JsonTransactionSerializer(cryptoModule, hashProvider);
var json = jsonSerializer.SerializeToJson(transaction);
var fromJson = jsonSerializer.DeserializeFromJson(json);

// Transport packet (network transmission)
var transportPacket = serializer.CreateTransportPacket(transaction);
```

### Version Detection

```csharp
// Detect version from binary or JSON data
var versionDetector = new VersionDetector();
var version = versionDetector.DetectVersion(binaryData);

// Use factory to create version-specific transactions
var factory = new TransactionFactory(cryptoModule, hashProvider, versionDetector);
var transaction = factory.Deserialize(binaryData);
```

## Architecture

The library is organized into the following namespaces:

- **Core**: `Transaction`, `TransactionBuilder`
- **Interfaces**: `ITransaction`, `ITransactionBuilder`, `IPayloadManager`, `ITransactionSerializer`
- **Enums**: `TransactionVersion`, `TransactionStatus`, `PayloadType`
- **Models**: `TransactionResult<T>`, `PayloadResult<T>`, `PayloadInfo`, `TransportPacket`
- **Payload**: `PayloadManager` (multi-recipient encryption and access control)
- **Serialization**: `BinaryTransactionSerializer`, `JsonTransactionSerializer`
- **Versioning**: `VersionDetector`, `TransactionFactory`

## Design Principles

1. **Immutability**: Signed transactions cannot be modified
2. **Fluent API**: Builder pattern for intuitive transaction creation
3. **Separation of Concerns**: Clean interfaces and dependency injection
4. **Backward Compatibility**: Support for legacy transaction versions
5. **Security First**: Cryptographic operations delegated to `Sorcha.Cryptography`
6. **Performance**: Binary serialization with VarInt encoding

## Dependencies

- **Sorcha.Cryptography** (>= 2.0.0): Cryptographic operations
- **.NET 10.0**: Target framework

## Testing

The library includes comprehensive test coverage:

- **47 Unit Tests**: Core transaction and builder functionality
- **28 Integration Tests**: End-to-end workflows and multi-recipient scenarios
- **34 Backward Compatibility Tests**: Version detection and factory tests
- **Total: 109 tests passing**

Run tests:

```bash
dotnet test tests/Sorcha.TransactionHandler.Tests/Sorcha.TransactionHandler.Tests.csproj
```

## Performance Benchmarks

Performance benchmarks are available using BenchmarkDotNet:

```bash
dotnet run -c Release --project tests/Sorcha.TransactionHandler.Benchmarks/Sorcha.TransactionHandler.Benchmarks.csproj
```

Benchmarks include:
- Transaction creation (with/without metadata/payloads)
- Transaction signing and verification
- Binary and JSON serialization/deserialization
- Complete workflow (create → sign → serialize → deserialize)

## API Documentation

Complete XML documentation is generated with the library. View IntelliSense in your IDE or generate documentation using tools like DocFX.

## License

MIT License - See LICENSE file for details

## Contributing

Contributions are welcome! Please ensure:
- All tests pass
- Code coverage remains >90%
- XML documentation for public APIs
- Follow existing code style

## CI/CD

The library uses GitHub Actions for automated:
- Building and testing
- NuGet package creation
- Deployment to nuget.org

See `.github/workflows/nuget-publish.yml` for pipeline configuration.

## Roadmap

- [ ] TX-016: Migration guide from embedded transactions
- [ ] TX-017: Additional code examples and tutorials
- [ ] TX-018: Integration with Sorcha services
- [ ] TX-019: Comprehensive regression testing

## Support

For issues, questions, or contributions, please visit the [GitHub repository](https://github.com/sorcha-platform/sorcha).
