// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using Sorcha.Validator.Service.Models;
using Sorcha.Register.Models;
// Sorcha.Register.Models and Sorcha.Validator.Service.Models BOTH declare a Docket (#1371).
// The alias is required, not stylistic, in any file importing both namespaces.
using Docket = Sorcha.Validator.Service.Models.Docket;
// The gRPC contract also declares a VoteDecision (Sorcha.Validator.Grpc.V1). Bare
// VoteDecision means the canonical ledger enum; the proto one stays Grpc.V1.VoteDecision.
using VoteDecision = Sorcha.Register.Models.VoteDecision;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Serializes and deserializes dockets for network transmission.
/// </summary>
/// <remarks>
/// This type is <b>only</b> a wire-format serializer. The docket→register projection that used to
/// live here as <c>ToRegisterModel</c> was a second, drifted copy of the one in
/// <see cref="DocketRegisterProjection"/> — it dropped <c>InstanceId</c> and <c>RoutingDecision</c>
/// and collapsed five <c>TransactionType</c> members onto <c>Action</c>, so which write path sealed a
/// docket silently changed what landed on the ledger. It was deleted by Feature 187 (#1370). Project
/// to the ledger model via <see cref="DocketRegisterProjection"/>; do not add a mapping here.
/// </remarks>
public static class DocketSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Serializes a docket to bytes for network transmission.
    /// </summary>
    /// <param name="docket">Docket to serialize</param>
    /// <returns>Serialized bytes</returns>
    public static byte[] SerializeToBytes(Docket docket)
    {
        ArgumentNullException.ThrowIfNull(docket);

        var dto = ToSerializableDto(docket);
        return JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
    }

    /// <summary>
    /// Deserializes a docket from bytes.
    /// </summary>
    /// <param name="data">Serialized bytes</param>
    /// <returns>Deserialized docket</returns>
    public static Docket? DeserializeFromBytes(byte[] data)
    {
        if (data == null || data.Length == 0)
            return null;

        var dto = JsonSerializer.Deserialize<DocketDto>(data, JsonOptions);
        return dto != null ? FromSerializableDto(dto) : null;
    }

    /// <summary>
    /// Converts a docket to a serializable DTO.
    /// </summary>
    private static DocketDto ToSerializableDto(Docket docket)
    {
        return new DocketDto
        {
            DocketId = docket.DocketId,
            RegisterId = docket.RegisterId,
            DocketNumber = docket.DocketNumber,
            DocketHash = docket.DocketHash,
            PreviousHash = docket.PreviousHash,
            MerkleRoot = docket.MerkleRoot,
            CreatedAt = docket.CreatedAt,
            ProposerValidatorId = docket.ProposerValidatorId,
            Status = docket.Status.ToString(),
            ProposerSignature = ToSignatureDto(docket.ProposerSignature),
            Transactions = docket.Transactions.Select(ToTransactionDto).ToList(),
            Votes = docket.Votes.Select(ToVoteDto).ToList()
        };
    }

    /// <summary>
    /// Converts a serializable DTO to a docket.
    /// </summary>
    private static Docket FromSerializableDto(DocketDto dto)
    {
        return new Docket
        {
            DocketId = dto.DocketId,
            RegisterId = dto.RegisterId,
            DocketNumber = dto.DocketNumber,
            DocketHash = dto.DocketHash,
            PreviousHash = dto.PreviousHash,
            MerkleRoot = dto.MerkleRoot,
            CreatedAt = dto.CreatedAt,
            ProposerValidatorId = dto.ProposerValidatorId,
            Status = Enum.TryParse<DocketStatus>(dto.Status, out var status) ? status : DocketStatus.Proposed,
            ProposerSignature = FromSignatureDto(dto.ProposerSignature),
            Transactions = dto.Transactions.Select(FromTransactionDto).ToList(),
            Votes = dto.Votes.Select(FromVoteDto).ToList()
        };
    }

    private static SignatureDto ToSignatureDto(Signature sig) => new()
    {
        PublicKey = Base64Url.EncodeToString(sig.PublicKey),
        SignatureValue = Base64Url.EncodeToString(sig.SignatureValue),
        Algorithm = sig.Algorithm,
        SignedAt = sig.SignedAt
    };

    private static Signature FromSignatureDto(SignatureDto dto) => new()
    {
        PublicKey = Base64Url.DecodeFromChars(dto.PublicKey),
        SignatureValue = Base64Url.DecodeFromChars(dto.SignatureValue),
        Algorithm = dto.Algorithm,
        SignedAt = dto.SignedAt
    };

    private static TransactionDto ToTransactionDto(Transaction tx) => new()
    {
        TransactionId = tx.TransactionId,
        RegisterId = tx.RegisterId,
        BlueprintId = tx.BlueprintId ?? string.Empty,
        ActionId = tx.ActionId,
        PayloadJson = tx.PayloadJson,
        PayloadHash = tx.PayloadHash,
        CreatedAt = tx.CreatedAt,
        ExpiresAt = tx.ExpiresAt,
        Priority = tx.Priority.ToString(),
        Signatures = tx.Signatures.Select(ToSignatureDto).ToList(),
        Metadata = tx.Metadata
    };

    private static Transaction FromTransactionDto(TransactionDto dto) => new()
    {
        TransactionId = dto.TransactionId,
        RegisterId = dto.RegisterId,
        BlueprintId = dto.BlueprintId,
        ActionId = dto.ActionId ?? string.Empty,
        PayloadHash = dto.PayloadHash,
        Payload = string.IsNullOrEmpty(dto.PayloadJson)
            ? JsonSerializer.Deserialize<JsonElement>("{}") // Empty object as default
            : JsonSerializer.Deserialize<JsonElement>(dto.PayloadJson),
        CreatedAt = dto.CreatedAt,
        ExpiresAt = dto.ExpiresAt,
        Priority = Enum.TryParse<TransactionPriority>(dto.Priority, out var pri) ? pri : TransactionPriority.Normal,
        Signatures = dto.Signatures.Select(FromSignatureDto).ToList(),
        Metadata = dto.Metadata ?? new Dictionary<string, string>()
    };

    private static ConsensusVoteDto ToVoteDto(ConsensusVote vote) => new()
    {
        VoteId = vote.VoteId,
        DocketId = vote.DocketId,
        ValidatorId = vote.ValidatorId,
        Decision = vote.Decision.ToString(),
        RejectionReason = vote.RejectionReason,
        VotedAt = vote.VotedAt,
        DocketHash = vote.DocketHash,
        ValidatorSignature = ToSignatureDto(vote.ValidatorSignature)
    };

    private static ConsensusVote FromVoteDto(ConsensusVoteDto dto) => new()
    {
        VoteId = dto.VoteId,
        DocketId = dto.DocketId,
        ValidatorId = dto.ValidatorId,
        Decision = Enum.TryParse<VoteDecision>(dto.Decision, out var dec) ? dec : VoteDecision.Reject,
        RejectionReason = dto.RejectionReason,
        VotedAt = dto.VotedAt,
        DocketHash = dto.DocketHash,
        ValidatorSignature = FromSignatureDto(dto.ValidatorSignature)
    };


    private record DocketDto
    {
        public string DocketId { get; init; } = string.Empty;
        public string RegisterId { get; init; } = string.Empty;
        public long DocketNumber { get; init; }
        public string DocketHash { get; init; } = string.Empty;
        public string? PreviousHash { get; init; }
        public string MerkleRoot { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; }
        public string ProposerValidatorId { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public SignatureDto ProposerSignature { get; init; } = new();
        public List<TransactionDto> Transactions { get; init; } = new();
        public List<ConsensusVoteDto> Votes { get; init; } = new();
    }

    private record SignatureDto
    {
        public string PublicKey { get; init; } = string.Empty;
        public string SignatureValue { get; init; } = string.Empty;
        public string Algorithm { get; init; } = string.Empty;
        public DateTimeOffset SignedAt { get; init; }
    }

    private record TransactionDto
    {
        public string TransactionId { get; init; } = string.Empty;
        public string RegisterId { get; init; } = string.Empty;
        public string BlueprintId { get; init; } = string.Empty;
        public string? ActionId { get; init; }
        public string? PayloadJson { get; init; }
        public string PayloadHash { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? ExpiresAt { get; init; }
        public string Priority { get; init; } = string.Empty;
        public List<SignatureDto> Signatures { get; init; } = new();
        public Dictionary<string, string>? Metadata { get; init; }
    }

    private record ConsensusVoteDto
    {
        public string VoteId { get; init; } = string.Empty;
        public string DocketId { get; init; } = string.Empty;
        public string ValidatorId { get; init; } = string.Empty;
        public string Decision { get; init; } = string.Empty;
        public string? RejectionReason { get; init; }
        public DateTimeOffset VotedAt { get; init; }
        public string DocketHash { get; init; } = string.Empty;
        public SignatureDto ValidatorSignature { get; init; } = new();
    }

}
