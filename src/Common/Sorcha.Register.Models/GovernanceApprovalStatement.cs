// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// Builds the canonical bytes an organisation signs when approving (or rejecting) a governance
/// operation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists (Feature 189 US2 / option A).</b> <see cref="ApprovalSignature"/> has always
/// carried a <c>Signature</c> field, and nothing in the platform ever verified it.
/// <c>GovernanceRosterService.ValidateQuorumAsync</c> counted an approval if its
/// <c>ApproverDid</c> merely appeared in the roster — so quorum was satisfied by <i>asserting</i>
/// approvals, with no cryptography involved at any point. Whoever composed the proposal payload
/// could claim every other member's approval.
/// </para>
/// <para>
/// <b>What the statement binds, and why each part matters.</b> A signature is only as good as the
/// thing it commits to. Every field below is included so an otherwise-valid signature cannot be
/// lifted and reused:
/// </para>
/// <list type="bullet">
/// <item><b>registerId</b> — an approval on one register must not authorise the same operation on
/// another.</item>
/// <item><b>operationType</b> + <b>targetDid</b> + <b>targetRole</b> — approving "add B as Auditor"
/// must not authorise "add B as Owner", which is the difference between a reader and a party who
/// can transfer the register.</item>
/// <item><b>proposerDid</b> and <b>proposedAt</b> — together these pin the specific proposal, so an
/// approval cannot be replayed onto a later, differently-intentioned proposal of the same shape.</item>
/// <item><b>approverDid</b> — so one member's signature cannot be attributed to another.</item>
/// <item><b>isApproval</b> — so a <i>rejection</i> cannot be counted as an approval by flipping a
/// boolean the signature does not cover. This is the field most easily forgotten, and the one whose
/// omission silently inverts a vote.</item>
/// </list>
/// <para>
/// Fields are joined with a unit separator (<c>0x1F</c>) rather than a printable delimiter, so a
/// value containing the delimiter cannot shift the field boundaries and make two different
/// statements hash identically.
/// </para>
/// </remarks>
public static class GovernanceApprovalStatement
{
    /// <summary>Field separator — a control character that cannot occur in a DID or enum name.</summary>
    private const char UnitSeparator = '';

    /// <summary>
    /// Computes the SHA-256 digest an approver signs, pre-hashed.
    /// </summary>
    /// <param name="registerId">Register the operation applies to.</param>
    /// <param name="operation">The proposal being voted on.</param>
    /// <param name="approverDid">DID of the approving organisation.</param>
    /// <param name="isApproval"><c>true</c> to approve, <c>false</c> to reject.</param>
    public static byte[] ComputeDigest(
        string registerId,
        GovernanceOperation operation,
        string approverDid,
        bool isApproval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(approverDid);

        return SHA256.HashData(Encoding.UTF8.GetBytes(
            BuildStatement(registerId, operation, approverDid, isApproval)));
    }

    /// <summary>
    /// The canonical statement string, exposed for diagnostics and for producers that need to show
    /// an operator exactly what is being signed.
    /// </summary>
    public static string BuildStatement(
        string registerId,
        GovernanceOperation operation,
        string approverDid,
        bool isApproval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(approverDid);

        return string.Join(UnitSeparator,
            StatementVersion,
            registerId,
            approverDid,
            isApproval ? "approve" : "reject",
            CanonicaliseOperation(operation));
    }

    /// <summary>Domain tag. v1 signatures MUST NOT verify under v2 (R-011 clean break).</summary>
    public const string StatementVersion = "sorcha:governance-approval:v2";

    /// <summary>
    /// Members deliberately outside the digest: both are state <i>about</i> the proposal rather than
    /// part of what is being authorised. Signatures accumulate as approvals arrive, so binding them
    /// would make the first signature invalidate the second; status is lifecycle, not content.
    /// </summary>
    private static readonly string[] ExcludedMembers =
    [
        nameof(GovernanceOperation.ApprovalSignatures),
        nameof(GovernanceOperation.Status),
    ];

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        // Enums by name: an approver reads "AddValidator", and a reordered enum must not silently
        // change what a stored signature covers.
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>
    /// Renders the operation as canonical JSON with keys sorted at every level.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why serialisation and not a field list.</b> v1 bound a hand-picked list, and
    /// <see cref="GovernanceOperation"/> carried more than that list — <c>ValidatorEntry</c>,
    /// <c>RosterSnapshotId</c>, <c>QuorumFormulaAtRaise</c>, <c>ExpiresAt</c> and
    /// <c>Justification</c> were all unbound. The sharp case was <c>AddValidator</c>: an approval
    /// bound "add a validator" and <b>not which one</b>, the validator's public key and endpoint
    /// sitting outside the digest entirely.
    /// </para>
    /// <para>
    /// Extending the list would close today's gap and reopen it the next time a property is added —
    /// silently, with no compiler error and no failing test. Binding the serialisation means a new
    /// property is covered the moment it exists. <c>GovernanceApprovalStatementBindingTests</c>
    /// enforces this by reflection, and found <c>Justification</c> that a hand-written list had
    /// missed.
    /// </para>
    /// <para>
    /// Keys are sorted rather than left in declaration order: declaration order is stable for a given
    /// build but reordering a property is an invisible edit that would invalidate every stored
    /// signature.
    /// </para>
    /// </remarks>
    private static string CanonicaliseOperation(GovernanceOperation operation)
    {
        var node = JsonSerializer.SerializeToNode(operation, CanonicalOptions)!.AsObject();

        foreach (var excluded in ExcludedMembers)
        {
            node.Remove(excluded);
        }

        return Canonicalise(node)!.ToJsonString();
    }

    /// <summary>Recursively rewrites an object graph with its keys in ordinal order.</summary>
    private static JsonNode? Canonicalise(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var ordered = new JsonObject();
                foreach (var pair in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    ordered[pair.Key] = Canonicalise(pair.Value?.DeepClone());
                }

                return ordered;
            }

            case JsonArray array:
            {
                // Order is preserved: for a governance payload the sequence is meaningful.
                var copy = new JsonArray();
                foreach (var item in array)
                {
                    copy.Add(Canonicalise(item?.DeepClone()));
                }

                return copy;
            }

            default:
                return node;
        }
    }
}
