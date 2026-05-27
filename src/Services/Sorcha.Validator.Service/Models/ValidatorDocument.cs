// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using MongoDB.Bson.Serialization.Attributes;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Models;

/// <summary>
/// MongoDB document for durable validator storage.
/// Maps between <see cref="ValidatorInfo"/> and the MongoDB collection.
/// </summary>
/// <remarks>
/// Public so that test fixtures can create <c>Mock&lt;IMongoCollection&lt;ValidatorDocument&gt;&gt;</c>
/// via Castle DynamicProxy. Castle generates proxies in a separate strong-named assembly
/// (<c>DynamicProxyGenAssembly2</c>) that cannot see types marked <c>internal</c> even when
/// <c>InternalsVisibleTo</c> is set for the direct test assembly.
/// </remarks>
public class ValidatorDocument
{
    /// <summary>Unique identifier for the resource.</summary>
    [BsonId]
    public string Id { get; set; } = string.Empty;

    /// <summary>Identifier of the register.</summary>
    public string RegisterId { get; set; } = string.Empty;
    /// <summary>Identifier of the validator.</summary>
    public string ValidatorId { get; set; } = string.Empty;
    /// <summary>Public key material.</summary>
    public string PublicKey { get; set; } = string.Empty;
    /// <summary>Cryptographic algorithm identifier.</summary>
    public string? Algorithm { get; set; }
    /// <summary>The grpc endpoint.</summary>
    public string GrpcEndpoint { get; set; } = string.Empty;
    /// <summary>Current status of the resource.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Timestamp at which registered occurred (UTC).</summary>
    public DateTimeOffset RegisteredAt { get; set; }
    /// <summary>Numeric value for order index.</summary>
    public int? OrderIndex { get; set; }
    /// <summary>Identifier of the registration tx.</summary>
    public string? RegistrationTxId { get; set; }
    /// <summary>Timestamp at which approved occurred (UTC).</summary>
    public DateTimeOffset? ApprovedAt { get; set; }
    /// <summary>The approved by.</summary>
    public string? ApprovedBy { get; set; }
    /// <summary>Timestamp at which suspended occurred (UTC).</summary>
    public DateTimeOffset? SuspendedAt { get; set; }
    /// <summary>The suspended by.</summary>
    public string? SuspendedBy { get; set; }
    /// <summary>Timestamp at which revoked occurred (UTC).</summary>
    public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>The revoked by.</summary>
    public string? RevokedBy { get; set; }
    /// <summary>Timestamp at which last state change occurred (UTC).</summary>
    public DateTimeOffset LastStateChangeAt { get; set; }
    /// <summary>Free-form metadata associated with the resource.</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    public static ValidatorDocument FromValidatorInfo(string registerId, ValidatorInfo info) => new()
    {
        Id = $"{registerId}:{info.ValidatorId}",
        RegisterId = registerId,
        ValidatorId = info.ValidatorId,
        PublicKey = info.PublicKey,
        Algorithm = info.Algorithm,
        GrpcEndpoint = info.GrpcEndpoint,
        Status = info.Status.ToString(),
        RegisteredAt = info.RegisteredAt,
        OrderIndex = info.OrderIndex,
        RegistrationTxId = info.RegistrationTxId,
        ApprovedAt = info.ApprovedAt,
        ApprovedBy = info.ApprovedBy,
        SuspendedAt = info.SuspendedAt,
        SuspendedBy = info.SuspendedBy,
        RevokedAt = info.RevokedAt,
        RevokedBy = info.RevokedBy,
        LastStateChangeAt = info.LastStateChangeAt,
        Metadata = info.Metadata
    };

    public ValidatorInfo ToValidatorInfo() => new()
    {
        ValidatorId = ValidatorId,
        PublicKey = PublicKey,
        Algorithm = Algorithm,
        GrpcEndpoint = GrpcEndpoint,
        Status = Enum.TryParse<ValidatorStatus>(Status, ignoreCase: true, out var status)
            ? status
            : ValidatorStatus.Suspended, // Safe fallback for unknown status values
        RegisteredAt = RegisteredAt,
        OrderIndex = OrderIndex,
        RegistrationTxId = RegistrationTxId,
        ApprovedAt = ApprovedAt,
        ApprovedBy = ApprovedBy,
        SuspendedAt = SuspendedAt,
        SuspendedBy = SuspendedBy,
        RevokedAt = RevokedAt,
        RevokedBy = RevokedBy,
        LastStateChangeAt = LastStateChangeAt,
        Metadata = Metadata
    };
}
