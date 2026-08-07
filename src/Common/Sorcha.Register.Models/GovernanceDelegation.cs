// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// A signed grant empowering an autonomous approver to act for an organisation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists (FR-033 / R-017).</b> An approval must resolve to a named individual. It was
/// first proposed that autonomous approvers be exempt, on the grounds that a machine has no person
/// behind it — which is wrong. A machine external to the platform was <i>empowered by someone</i> to
/// act, so accountability is deferred, never missing.
/// </para>
/// <para>
/// <b>Why a JWT claim is not enough.</b> <c>RequireDelegatedAuthority</c> already carries a
/// <c>delegated_user_id</c> claim meaning "service token acting on behalf of a user". But the
/// <b>server mints the token</b>. A delegation the server can assert is one it can forge — which
/// defeats the entire point of moving signing outside the server. So this record is signed by the
/// empowering individual's own key and travels as evidence.
/// </para>
/// <para>
/// Structurally this is the same shape as a citizen's paired device (Feature 114): its own key, acting
/// for a principal, with authority from a scoped, revocable, expiring grant.
/// </para>
/// </remarks>
public sealed class GovernanceDelegation
{
    /// <summary>Ledger identity of this grant. Revocation refers to it.</summary>
    public string DelegationId { get; set; } = string.Empty;

    /// <summary>Organisation whose authority is being exercised.</summary>
    public string OrganisationDid { get; set; } = string.Empty;

    /// <summary>The individual who empowered the approver, and who remains accountable.</summary>
    public string IndividualDid { get; set; } = string.Empty;

    /// <summary>Public key of the machine this empowers. Base64.</summary>
    public string ApproverPublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Operations this grant permits. A bot can be empowered for routine crypto-policy changes while
    /// <see cref="GovernanceOperationType.Transfer"/> still requires a person — which is the point of
    /// scoping rather than granting wholesale.
    /// </summary>
    public List<GovernanceOperationType> Scope { get; set; } = new();

    /// <summary>When the grant lapses. An unbounded delegation is a standing key with no owner.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When it was granted.</summary>
    public DateTimeOffset GrantedAt { get; set; }
}

/// <summary>
/// Canonical bytes the empowering individual signs when granting a <see cref="GovernanceDelegation"/>.
/// </summary>
/// <remarks>
/// Binds the grant's whole serialisation for the same reason
/// <see cref="GovernanceApprovalStatement"/> does: a hand-picked field list silently stops covering a
/// property added later. Here the stakes are if anything higher — an unbound <c>Scope</c> would let a
/// grant reviewed as "may approve crypto-policy changes" be widened to include
/// <see cref="GovernanceOperationType.Transfer"/> after signing.
/// </remarks>
public static class GovernanceDelegationStatement
{
    /// <summary>Field separator — a control character that cannot occur in a DID or enum name.</summary>
    private const char UnitSeparator = '';

    /// <summary>Domain tag. Distinct from the approval statement so neither can be replayed as the other.</summary>
    public const string StatementVersion = "sorcha:governance-delegation:v1";

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>SHA-256 digest the empowering individual signs.</summary>
    public static byte[] ComputeDigest(GovernanceDelegation delegation)
        => SHA256.HashData(Encoding.UTF8.GetBytes(BuildStatement(delegation)));

    /// <summary>The canonical statement, exposed so a grantor can be shown exactly what they authorise.</summary>
    public static string BuildStatement(GovernanceDelegation delegation)
    {
        ArgumentNullException.ThrowIfNull(delegation);

        var node = JsonSerializer.SerializeToNode(delegation, CanonicalOptions)!.AsObject();

        return string.Join(UnitSeparator, StatementVersion, Canonicalise(node)!.ToJsonString());
    }

    /// <summary>Recursively rewrites an object graph with keys in ordinal order.</summary>
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
