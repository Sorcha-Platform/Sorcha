// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// PR #881 regression. Transactions relayed between peers MUST be serialised with
/// <see cref="RegisterSerializationOptions.Canonical"/> (camelCase) because the receiver
/// deserialises cached transactions with the same options (case-sensitive). Serialising with
/// default options (PascalCase) made <see cref="TransactionMetaData"/> — which has no
/// [JsonPropertyName] — silently deserialise to null, so the genesis Control transaction could
/// not be identified, the validator roster could not be reconstructed, and post-genesis dockets
/// were rejected ("validator key not available").
/// </summary>
public class TransactionCanonicalSerializationTests
{
    private static TransactionModel MakeTx() => new()
    {
        TxId = "tx-1",
        RegisterId = "reg-1",
        MetaData = new TransactionMetaData
        {
            RegisterId = "reg-1",
            TransactionType = TransactionType.BlueprintPublish,
            BlueprintId = "assured-identity-v1",
            TrackingData = new Dictionary<string, string>
            {
                ["Type"] = "BlueprintPublish",
                ["contentHash"] = "212b4f05497106dc11cc3db7366edad0110f0432afdb53a7d462da8060e95aea",
            }
        }
    };

    [Fact]
    public void Canonical_RoundTrip_PreservesMetaDataAndTrackingData()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(MakeTx(), RegisterSerializationOptions.Canonical);

        var restored = JsonSerializer.Deserialize<TransactionModel>(bytes, RegisterSerializationOptions.Canonical);

        restored.Should().NotBeNull();
        restored!.MetaData.Should().NotBeNull();
        restored.MetaData!.TransactionType.Should().Be(TransactionType.BlueprintPublish);
        restored.MetaData.BlueprintId.Should().Be("assured-identity-v1");
        restored.MetaData.TrackingData.Should().NotBeNull();
        restored.MetaData.TrackingData!.Should().ContainKey("contentHash")
            .WhoseValue.Should().Be("212b4f05497106dc11cc3db7366edad0110f0432afdb53a7d462da8060e95aea");
    }

    [Fact]
    public void DefaultSerialize_CanonicalDeserialize_DropsMetaData_DocumentsTheBug()
    {
        // The #881 bug: serve side used default (PascalCase) options, receive side used Canonical
        // (camelCase, case-sensitive). MetaData has no [JsonPropertyName], so the case mismatch
        // wiped it on deserialise. This asserts that exact failure mode so the fix can't silently
        // regress to default serialisation.
        var defaultPascalBytes = JsonSerializer.SerializeToUtf8Bytes(MakeTx());

        var restored = JsonSerializer.Deserialize<TransactionModel>(defaultPascalBytes, RegisterSerializationOptions.Canonical);

        restored.Should().NotBeNull();
        restored!.MetaData.Should().BeNull("PascalCase wire vs camelCase case-sensitive deserialise drops MetaData");
    }
}
