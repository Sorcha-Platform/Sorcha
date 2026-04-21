// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Register.Storage.MongoDB.Serialization;

/// <summary>
/// Feature 108. BSON serializer for the migrated <c>Register.SyncState</c> field.
/// Writes as enum-name string; reads either the new enum-name form or the legacy
/// free-text values that existed before Feature 108:
/// <list type="bullet">
///   <item><c>"Subscribing"</c> → <see cref="RegisterSyncState.Syncing"/></item>
///   <item><c>"Syncing"</c> → <see cref="RegisterSyncState.Syncing"/></item>
///   <item><c>"Synced"</c> → <see cref="RegisterSyncState.CaughtUp"/></item>
///   <item><c>"Error"</c> → <see cref="RegisterSyncState.Error"/></item>
///   <item><c>null</c> → <see cref="RegisterSyncState.Indeterminate"/></item>
///   <item>Unknown strings → log warning, treated as <see cref="RegisterSyncState.Indeterminate"/></item>
/// </list>
/// First write of a register document after Feature 108 persists the new enum-name form,
/// migrating opportunistically without a bulk job.
/// </summary>
public class RegisterSyncStateBsonSerializer : SerializerBase<RegisterSyncState?>
{
    private readonly ILogger<RegisterSyncStateBsonSerializer>? _logger;

    public RegisterSyncStateBsonSerializer() : this(null) { }

    public RegisterSyncStateBsonSerializer(ILogger<RegisterSyncStateBsonSerializer>? logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public override RegisterSyncState? Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonType = context.Reader.GetCurrentBsonType();

        switch (bsonType)
        {
            case BsonType.Null:
                context.Reader.ReadNull();
                return null;

            case BsonType.String:
                var raw = context.Reader.ReadString();
                return MapLegacyOrNewString(raw);

            case BsonType.Int32:
                // Allow numeric persisted form as a defensive fallback.
                var n = context.Reader.ReadInt32();
                return Enum.IsDefined(typeof(RegisterSyncState), n)
                    ? (RegisterSyncState)n
                    : Warn(null, $"int {n}");

            default:
                throw new BsonSerializationException(
                    $"Cannot deserialize BsonType.{bsonType} to RegisterSyncState. Expected String, Int32, or Null.");
        }
    }

    /// <inheritdoc />
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, RegisterSyncState? value)
    {
        if (value is null)
        {
            context.Writer.WriteNull();
            return;
        }

        context.Writer.WriteString(value.Value.ToString());
    }

    private RegisterSyncState? MapLegacyOrNewString(string raw)
    {
        // New enum-name form first
        if (Enum.TryParse<RegisterSyncState>(raw, ignoreCase: false, out var newForm))
            return newForm;

        // Legacy free-text form mapping
        return raw switch
        {
            "Subscribing" => RegisterSyncState.Syncing,
            "Syncing"     => RegisterSyncState.Syncing,
            "Synced"      => RegisterSyncState.CaughtUp,
            "Error"       => RegisterSyncState.Error,
            ""            => null,
            _             => Warn(null, raw)
        };
    }

    private RegisterSyncState? Warn(RegisterSyncState? fallback, string rawValue)
    {
        _logger?.LogWarning(
            "Unknown RegisterSyncState value {RawValue} on read — treating as Indeterminate",
            rawValue);
        return fallback;
    }
}
