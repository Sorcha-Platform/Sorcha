// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using Sorcha.Validator.Service.Models;
using Sorcha.ServiceClients.Register;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Serializes and deserializes dockets for network transmission and service integration.
/// </summary>
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
    /// Converts a Validator Docket to Register Service DocketModel.
    /// </summary>
    /// <param name="docket">Validator docket</param>
    /// <returns>Register service docket model</returns>
    public static DocketModel ToRegisterModel(Docket docket)
    {
        ArgumentNullException.ThrowIfNull(docket);

        return new DocketModel
        {
            DocketId = docket.DocketId,
            RegisterId = docket.RegisterId,
            DocketNumber = docket.DocketNumber,
            PreviousHash = docket.PreviousHash,
            DocketHash = docket.DocketHash,
            CreatedAt = docket.CreatedAt,
            MerkleRoot = docket.MerkleRoot,
            ProposerValidatorId = docket.ProposerValidatorId,
            Transactions = docket.Transactions.Select(tx =>
            {
                // Determine TransactionType from metadata
                var transactionType = Sorcha.Register.Models.Enums.TransactionType.Action;
                if (tx.Metadata.TryGetValue("Type", out var typeStr))
                {
                    transactionType = typeStr.ToLowerInvariant() switch
                    {
                        "participant" => Sorcha.Register.Models.Enums.TransactionType.Participant,
                        "control" => Sorcha.Register.Models.Enums.TransactionType.Control,
                        _ => Sorcha.Register.Models.Enums.TransactionType.Action
                    };
                }

                // Build payload model from transaction data
                var payloadData = tx.PayloadJson != null
                    ? Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(tx.PayloadJson))
                    : string.Empty;

                return new Sorcha.Register.Models.TransactionModel
                {
                    TxId = tx.TransactionId,
                    RegisterId = tx.RegisterId,
                    TimeStamp = tx.CreatedAt.UtcDateTime,
                    SenderWallet = GetSenderWallet(tx),
                    Signature = tx.Signatures.FirstOrDefault() != null
                        ? Base64Url.EncodeToString(tx.Signatures.First().SignatureValue)
                        : string.Empty,
                    PayloadCount = string.IsNullOrEmpty(payloadData) ? 0UL : 1UL,
                    Payloads = string.IsNullOrEmpty(payloadData)
                        ? Array.Empty<Sorcha.Register.Models.PayloadModel>()
                        : [new Sorcha.Register.Models.PayloadModel
                        {
                            Data = payloadData,
                            Hash = tx.PayloadHash,
                            PayloadSize = (ulong)(tx.PayloadJson?.Length ?? 0),
                            ContentEncoding = "base64url"
                        }],
                    MetaData = new Sorcha.Register.Models.TransactionMetaData
                    {
                        RegisterId = tx.RegisterId,
                        TransactionType = transactionType,
                        BlueprintId = tx.BlueprintId,
                        ActionId = uint.TryParse(tx.ActionId, out var actionId) ? actionId : null,
                        TrackingData = tx.Metadata.Count > 0
                            ? new Dictionary<string, string>(tx.Metadata)
                            : null
                    }
                };
            }).ToList()
        };
    }

    /// <summary>
    /// Extracts sender wallet from transaction signatures or metadata.
    /// </summary>
    private static string GetSenderWallet(Transaction tx)
    {
        // Try to get sender from first signature's public key
        if (tx.Signatures.Count > 0)
        {
            return Base64Url.EncodeToString(tx.Signatures[0].PublicKey);
        }
        return string.Empty;
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
        BlueprintId = tx.BlueprintId,
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
