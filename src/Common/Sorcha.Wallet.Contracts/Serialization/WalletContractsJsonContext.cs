// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;
using Sorcha.Wallet.Contracts.Models;

namespace Sorcha.Wallet.Contracts.Serialization;

/// <summary>
/// System.Text.Json source-generation context for the Wallet contracts. Hosts register this into their
/// <see cref="System.Text.Json.JsonSerializerOptions.TypeInfoResolverChain"/> to get reflection-free,
/// trim-safe (de)serialization with the platform's camelCase wire names — important for the WASM PWA where
/// reflection metadata is trimmed on Release publish.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WalletDto))]
[JsonSerializable(typeof(WalletAddressDto))]
[JsonSerializable(typeof(AddressListResponse))]
[JsonSerializable(typeof(CreateWalletRequest))]
[JsonSerializable(typeof(CreateWalletResponse))]
[JsonSerializable(typeof(SignTransactionRequest))]
[JsonSerializable(typeof(SignTransactionResponse))]
public partial class WalletContractsJsonContext : JsonSerializerContext;
