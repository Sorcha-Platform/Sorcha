// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Forms;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Blueprint.Engine.Implementation;

/// <summary>
/// Computes a stable, deterministic hash over a Blueprint's <b>executable definition</b>
/// (Feature 142 / T004). The hash is the join key for the rehearsal-pass soft gate (D4/FR-032):
/// a recorded <c>RehearsalPass</c> is valid for a publish iff its <c>ExecDefHash</c> equals the
/// publishing version's <c>ExecDefHash</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is included</b> (the executable definition): participants (identity / wallet-binding
/// structural fields), actions (<c>id</c>, <c>sender</c>, <c>isStartingAction</c>,
/// <c>requiredPriorActions</c>, <c>calculations</c>, <c>disclosures</c>,
/// <c>credentialRequirements</c>, <c>credentialIssuanceConfig</c>, <c>routes</c>
/// [<c>id</c>, <c>nextActionIds</c>, <c>isDefault</c>, <c>condition</c>, <c>outputMapping</c>]),
/// and each action's <c>dataSchemas</c> with presentational <c>x-*</c> keywords stripped but
/// behavioural <c>x-*</c> kept (see <see cref="FormKeywordClassifier"/>).
/// </para>
/// <para>
/// <b>What is excluded</b> (presentational / display): blueprint and action <c>title</c> /
/// <c>description</c>, <c>x-introduction</c>, and every keyword in the presentational set
/// wherever it appears in a data schema.
/// </para>
/// <para>
/// <b>Canonicalisation.</b> JSON object keys are sorted (order-insensitive) so semantically-equal
/// blueprints hash identically; arrays keep their order because action and route order is
/// semantic. The result is a SHA-256 over UTF-8 canonical JSON, returned as lowercase hex.
/// </para>
/// <para>
/// <b>Portability.</b> This type lives in the portable <c>Sorcha.Blueprint.Engine</c> and uses
/// only <see cref="SHA256"/> and <see cref="System.Text.Json"/> — no HttpClient, no platform
/// APIs — so it is WASM-safe and produces identical results on the client and the server.
/// </para>
/// </remarks>
/// <summary>
/// The BEHAVIOURAL SIGNATURE of a blueprint — "did this republish change how the workflow runs?"
/// </summary>
/// <remarks>
/// <para>
/// <b>This value does NOT identify a definition (Feature 195).</b> That is the publication
/// transaction id, which addresses the whole published definition and names a ledger fact any node
/// can resolve. This hash addresses a deliberately NARROWER projection — the parts that affect
/// execution — and several publications may legitimately share one.
/// </para>
/// <para>
/// It has exactly one job: deciding whether a recorded Feature 142 <c>RehearsalPass</c> survives a
/// republish. A relabelled field must not cost a designer a fresh rehearsal; a changed
/// <c>required</c> entry must.
/// </para>
/// <para>
/// <b>Its meaning was widened once before, and that was the bug.</b> Feature 194 promoted this hash
/// to the instance pin without widening its coverage, so nine execution-affecting fields sat outside
/// a value the validator then enforced. Coverage is now guarded by
/// <c>ExecutableDefinitionCoverageTests</c>, which fails on any property of the blueprint graph that
/// nobody has classified — there is no default, because both defaults are wrong in different
/// directions.
/// </para>
/// </remarks>
public class ExecutableDefinitionHasher
{
    private static readonly JsonSerializerOptions ModelSerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Computes the executable-definition hash for the supplied Blueprint.
    /// </summary>
    /// <param name="blueprint">The Blueprint to hash. Must not be null.</param>
    /// <returns>A lowercase hex SHA-256 string over the canonical executable definition.</returns>
    public string ComputeHash(BlueprintModel blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);

        var execDef = BuildExecutableDefinition(blueprint);
        var canonicalJson = canonicalSerialise(execDef);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Projects the Blueprint down to the executable definition as a <see cref="JsonObject"/>:
    /// only the structural fields, with presentational layout removed.
    /// </summary>
    private static JsonObject BuildExecutableDefinition(BlueprintModel blueprint)
    {
        var root = new JsonObject
        {
            // Structural identity. Title/Description deliberately excluded (presentational).
            //
            // Feature 194 removed `["version"] = blueprint.Version` from this projection. The
            // ordinal is a DISPLAY LABEL — assigned from in-memory insert order and re-derived on
            // recovery — so putting it inside a content address is a contradiction: two blueprints
            // that execute identically would hash differently purely because someone renumbered
            // one. `Blueprint.Version` is a plain settable int, so an author editing it would have
            // stranded every in-flight instance on the previous definition for no behavioural
            // reason, and re-locked the F142 rehearsal gate at the same time.
            //
            // It never bit only because republishing does not bump it: the store assigns
            // PublishedBlueprint.Version, a different property on a different type.
            ["id"] = blueprint.Id,
            ["participants"] = BuildParticipants(blueprint.Participants),
            ["actions"] = BuildActions(blueprint.Actions),

            // Feature 195 (#1566) — three blueprint-level omissions, each execution-affecting.
            // A probe found them together with six more on Action/Route; every one produced an
            // IDENTICAL signature for a behaviourally different definition.
            ["dataSchemas"] = BuildDataSchemas(blueprint.DataSchemas),
            // PresentationLifecycleService.ResolveConfig reads this: validity window, abandonment
            // recording, outcome detail level.
            ["presentationConfig"] = SerialiseModel(blueprint.PresentationConfig),
            // Generates the instance's PUBLIC metadata — its human-readable reference.
            ["instanceReference"] = SerialiseModel(blueprint.InstanceReference),
            // Carries hasCycles, written by the publish path and read at execution.
            ["metadata"] = SerialiseModel(blueprint.Metadata),
        };

        return root;
    }

    private static JsonArray BuildParticipants(IEnumerable<Participant>? participants)
    {
        var arr = new JsonArray();
        if (participants is null)
        {
            return arr;
        }

        foreach (var p in participants)
        {
            // Identity + wallet-binding structural fields only; instructions/display omitted.
            var obj = new JsonObject
            {
                ["id"] = p.Id,
                ["walletAddress"] = p.WalletAddress,
                ["useStealthAddress"] = p.UseStealthAddress,
            };
            arr.Add(obj);
        }

        return arr;
    }

    private static JsonArray BuildActions(IEnumerable<ActionModel>? actions)
    {
        var arr = new JsonArray();
        if (actions is null)
        {
            return arr;
        }

        // Action order is semantic — preserve it (no sorting of the array).
        foreach (var a in actions)
        {
            var obj = new JsonObject
            {
                ["id"] = a.Id,
                ["sender"] = a.Sender,
                ["isStartingAction"] = a.IsStartingAction,
                ["requiredPriorActions"] = ToIntArray(a.RequiredPriorActions),
                ["calculations"] = SerialiseModel(a.Calculations),
                ["disclosures"] = BuildDisclosures(a.Disclosures),
                ["credentialRequirements"] = SerialiseModel(a.CredentialRequirements),
                ["credentialIssuanceConfig"] = SerialiseModel(a.CredentialIssuanceConfig),
                ["routes"] = BuildRoutes(a.Routes),

                // Feature 195 (#1566) — omissions on Action.
                //
                // RejectionConfig is a real ROUTING EDGE, not decoration: the validator reads
                // TargetActionId as a structural successor in VAL_ROUTING_001 and in VAL_BP_003
                // reachability, and IsTerminal decides whether rejection ends the workflow.
                ["rejectionConfig"] = SerialiseModel(a.RejectionConfig),
                // The LEGACY condition-based routing model, still live in RoutingEngine. A blueprint
                // routed this way previously had ZERO routing coverage in its signature.
                ["participants"] = SerialiseModel(a.Participants),
                // The validation fallback used when an action declares no dataSchemas.
                ["requiredActionData"] = SerialiseModel(a.RequiredActionData),
                ["target"] = a.Target,
                ["condition"] = SerialiseModel(a.Condition),
                ["notification"] = SerialiseModel(a.Notification),
                ["dataSchemas"] = BuildDataSchemas(a.DataSchemas),
            };
            arr.Add(obj);
        }

        return arr;
    }

    private static JsonArray BuildDisclosures(IEnumerable<Disclosure>? disclosures)
    {
        var arr = new JsonArray();
        if (disclosures is null)
        {
            return arr;
        }

        foreach (var d in disclosures)
        {
            var obj = new JsonObject
            {
                ["participantAddress"] = d.ParticipantAddress,
                ["dataPointers"] = new JsonArray(d.DataPointers.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
            };
            arr.Add(obj);
        }

        return arr;
    }

    private static JsonArray BuildRoutes(IEnumerable<Route>? routes)
    {
        var arr = new JsonArray();
        if (routes is null)
        {
            return arr;
        }

        // Route order is semantic (first matching condition wins) — preserve order.
        foreach (var r in routes)
        {
            var obj = new JsonObject
            {
                ["id"] = r.Id,
                ["nextActionIds"] = new JsonArray(r.NextActionIds.Select(i => (JsonNode)JsonValue.Create(i)).ToArray()),
                ["isDefault"] = r.IsDefault,
                ["condition"] = r.Condition?.DeepClone(),
                ["outputMapping"] = SerialiseModel(r.OutputMapping),

                // Feature 195 (#1566) — omissions on Route.
                ["branchDeadline"] = r.BranchDeadline,
                // F184/F186: the citizen-facing outcome catalogue. F186 resolves the wording FROM
                // THE PINNED DEFINITION, so two definitions differing only here give a refused
                // applicant different reasons.
                ["x-decision-notice"] = SerialiseModel(r.DecisionNotice),
            };
            arr.Add(obj);
        }

        return arr;
    }

    /// <summary>
    /// Builds the data-schemas array with presentational <c>x-*</c> keywords stripped and
    /// behavioural <c>x-*</c> kept, walking each schema recursively.
    /// </summary>
    private static JsonArray BuildDataSchemas(IEnumerable<JsonDocument>? schemas)
    {
        var arr = new JsonArray();
        if (schemas is null)
        {
            return arr;
        }

        foreach (var doc in schemas)
        {
            var node = JsonNode.Parse(doc.RootElement.GetRawText());
            var stripped = StripPresentational(node);
            arr.Add(stripped);
        }

        return arr;
    }

    /// <summary>
    /// Recursively removes presentational <c>x-*</c> keywords from a schema node. Behavioural
    /// <c>x-*</c> keywords and standard JSON Schema vocabulary are retained. Returns a new node.
    /// </summary>
    private static JsonNode? StripPresentational(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var result = new JsonObject();
                foreach (var kvp in obj)
                {
                    // Drop presentational extension keywords wherever they appear.
                    if (FormKeywordClassifier.IsPresentational(kvp.Key))
                    {
                        continue;
                    }

                    result[kvp.Key] = StripPresentational(kvp.Value);
                }

                return result;
            }

            case JsonArray array:
            {
                var result = new JsonArray();
                foreach (var item in array)
                {
                    result.Add(StripPresentational(item));
                }

                return result;
            }

            default:
                return node?.DeepClone();
        }
    }

    /// <summary>
    /// Serialises an arbitrary model object to a canonical-ready <see cref="JsonNode"/> tree,
    /// or <see langword="null"/> when the source is null. Used for the rich credential/config
    /// objects whose every structural field participates in the hash.
    /// </summary>
    private static JsonNode? SerialiseModel(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(value, ModelSerializerOptions);
        return JsonNode.Parse(json);
    }

    private static JsonNode? ToIntArray(IEnumerable<int>? values)
    {
        if (values is null)
        {
            return null;
        }

        return new JsonArray(values.Select(i => (JsonNode)JsonValue.Create(i)).ToArray());
    }

    /// <summary>
    /// Serialises a node to canonical JSON: object keys sorted recursively (order-insensitive),
    /// arrays left in source order (order-sensitive).
    /// </summary>
    private static string canonicalSerialise(JsonNode? node)
    {
        var canonical = Canonicalise(node);
        return canonical?.ToJsonString() ?? "null";
    }

    private static JsonNode? Canonicalise(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var sorted = new JsonObject();
                foreach (var kvp in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    sorted[kvp.Key] = Canonicalise(kvp.Value);
                }

                return sorted;
            }

            case JsonArray array:
            {
                var result = new JsonArray();
                foreach (var item in array)
                {
                    result.Add(Canonicalise(item));
                }

                return result;
            }

            default:
                return node?.DeepClone();
        }
    }
}
