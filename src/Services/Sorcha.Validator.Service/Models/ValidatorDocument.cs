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
    [BsonId]
    public string Id { get; set; } = string.Empty;

    public string RegisterId { get; set; } = string.Empty;
    public string ValidatorId { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string? Algorithm { get; set; }
    public string GrpcEndpoint { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
    public int? OrderIndex { get; set; }
    public string? RegistrationTxId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
    public string? SuspendedBy { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
    public DateTimeOffset LastStateChangeAt { get; set; }
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
