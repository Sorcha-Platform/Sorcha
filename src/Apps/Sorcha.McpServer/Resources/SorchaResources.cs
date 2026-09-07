// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Sorcha.McpServer.Resources;

/// <summary>
/// Static, embedded reference resources: the blueprint JSON Schema, worked example blueprints,
/// and a glossary of Sorcha's core terms. These are the "nouns" side of the MCP surface — the
/// server was tools-only (zero resources) until this class, and a cold-start authoring A/B
/// measured the blueprint schema as the single intervention that closed the authoring gap.
/// </summary>
[McpServerResourceType]
public static class SorchaResources
{
    private const string BlueprintSchemaLogicalName = "Sorcha.McpServer.Resources.blueprint.schema.json";

    private static readonly IReadOnlyDictionary<string, string> ExampleLogicalNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["assured-identity"] = "Sorcha.McpServer.Resources.examples.assured-identity.json",
            ["encryption-at-rest"] = "Sorcha.McpServer.Resources.examples.encryption-at-rest.json",
            ["ping-pong"] = "Sorcha.McpServer.Resources.examples.ping-pong.json",
        };

    // Ruling 3, corrected (2026-09-07): the embedded schema (src/Common/blueprint.schema.json,
    // last touched by 5acb82db5 / F111 / PR #382) is NOT wrong or legacy — every property it
    // documents (action-level `participants`, `condition`, etc.) is a live, current-model property;
    // `register-governance-v1` routes on `condition` today, and F195 deliberately added action-level
    // `Participants` routing to the executable-definition hash precisely because it is still
    // executed. The defect is pure OMISSION, not error: it simply does not yet describe several
    // constructs an agent needs to author a current blueprint — `routes` has no definition at all,
    // nor do `isStartingAction`, `credentialRequirements`, `credentialIssuanceConfig`,
    // `rejectionConfig`, `requiredPriorActions`, or `instanceReference`. The three worked examples
    // below use `routes` + `isStartingAction` (assured-identity also `credentialIssuanceConfig`)
    // and are what the walkthrough suite actually executes, so they are the reference for those
    // constructs — complementary to the schema, not a correction of it. Regenerating the platform's
    // published schema to cover the omitted constructs is separate work and out of scope here.
    [McpServerResource(UriTemplate = "sorcha://schema/blueprint", Name = "Blueprint JSON Schema", MimeType = "application/schema+json")]
    [Description("A JSON Schema for a Sorcha blueprint: everything it documents (participants, actions, data schemas, disclosure groups, action-level condition routing) is accurate and current. IMPORTANT: it is INCOMPLETE, not wrong — it does not yet define `routes`, `isStartingAction`, `credentialRequirements`, `credentialIssuanceConfig`, `rejectionConfig`, `requiredPriorActions`, or `instanceReference`. For those constructs, read sorcha://examples/{name} instead, which the walkthrough suite actually executes. Read this before writing any blueprint JSON — for the parts it covers, it is the difference between real JSON and a plausible-looking guess.")]
    public static string BlueprintSchema() => GetBlueprintSchema();

    [McpServerResource(UriTemplate = "sorcha://examples/{name}", Name = "Example blueprint", MimeType = "application/json")]
    [Description("A complete, working blueprint that the Sorcha walkthrough suite actually executes, using `routes` + `isStartingAction` to connect actions — constructs the embedded blueprint schema does not yet define, so read this alongside sorcha://schema/blueprint rather than in place of it. Available names: assured-identity (credential issuance with selective disclosure and credentialIssuanceConfig), encryption-at-rest (encrypted payloads and disclosure groups), ping-pong (the minimal two-party exchange).")]
    public static string? Example(string name) => GetExample(name);

    [McpServerResource(UriTemplate = "sorcha://glossary", Name = "Sorcha glossary", MimeType = "text/markdown")]
    [Description("What Sorcha's core terms mean: register, blueprint, action, participant, disclosure group, docket, publication id versus executable-definition hash.")]
    public static string Glossary() => GetGlossary();

    /// <summary>
    /// Reads the embedded blueprint JSON Schema.
    /// </summary>
    internal static string GetBlueprintSchema() => ReadEmbeddedResource(BlueprintSchemaLogicalName)
        ?? throw new InvalidOperationException($"Embedded resource '{BlueprintSchemaLogicalName}' was not found.");

    /// <summary>
    /// Reads the embedded example blueprint identified by <paramref name="name"/>, or null when
    /// <paramref name="name"/> does not match a known example.
    /// </summary>
    internal static string? GetExample(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !ExampleLogicalNames.TryGetValue(name, out var logicalName))
        {
            return null;
        }

        var raw = ReadEmbeddedResource(logicalName);
        return raw is null ? null : UnwrapTemplate(raw);
    }

    // walkthroughs/*/blueprints/*.json come in two shapes: wrapped
    // ({"template": {...}, "category": ..., "tags": [...], ...} — walkthrough-catalog metadata
    // around the blueprint) or flat (the blueprint object at the top level). Both assured-identity
    // and ping-pong are wrapped; encryption-at-rest is flat. Publish-SorchaBlueprint
    // (walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1) tolerates both the same way —
    // unwrap here so the resource returns the actual blueprint object the suite publishes, not the
    // walkthrough-catalog envelope (title/category/tags/author) around it.
    private static string UnwrapTemplate(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("template", out var template) &&
            template.ValueKind == JsonValueKind.Object)
        {
            return JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
        }

        return raw;
    }

    /// <summary>
    /// Returns the inline glossary of Sorcha's core terms.
    /// </summary>
    internal static string GetGlossary() => GlossaryMarkdown;

    private static string? ReadEmbeddedResource(string logicalName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private const string GlossaryMarkdown = """
        # Sorcha glossary

        - **Register** — a distributed, cryptographically secured ledger that records every
          transaction for a set of participants. Registers are how the DAD (Disclosure,
          Alteration, Destruction) security model is enforced: disclosure is scoped by schema,
          alteration is recorded immutably, and destruction risk is eliminated through peer
          replication.

        - **Blueprint** — a workflow definition: an ordered set of actions, the participants
          who may perform them, the data schemas each action's payload must satisfy, and (in the
          current model) `routes` connecting actions together, with exactly one action marked
          `isStartingAction`. Published to a register, a blueprint becomes executable.

        - **Action** — one step in a blueprint: who may perform it (`sender`, `participants`),
          what data it requires (`dataSchemas`), and where execution goes next. A running
          instance advances along the `routes` its definition declares, with exactly one action
          marked `isStartingAction` — see sorcha://examples/{name} for `routes` in a complete,
          working blueprint.

        - **Participant** — an identity (a wallet address, ultimately) named in a blueprint as
          eligible to send or receive a given action.

        - **Disclosure group** — a named subset of an action's data schema that is selectively
          revealed to a specific participant, independent of what is sealed on the register.
          This is the "Disclosure" half of DAD: the ledger can hold more than any one party sees.

        - **Docket** — a batch of confirmed transactions, sealed with validator consensus
          signatures and written to the register. Dockets are the unit of "Alteration": once
          sealed, a docket's contents are immutable.

        - **Wallet address** — the public identifier derived from a participant's key pair
          (ED25519 / P-256 / RSA-4096 via HD wallets). Sorcha identities — participants,
          organisations, validators — are wallet addresses, not usernames.

        - **`publicationTxId`** — the transaction id that published a specific blueprint
          definition to a specific register. It is the SHA-256 digest of the definition's own
          canonical bytes (domain-tagged, register-scoped), so it both identifies the
          definition and anchors an instance to exactly the definition it started against.

        - **`execDefHash`** — the *behavioural signature* of a blueprint definition: a narrower
          projection that deliberately excludes presentational fields (`title`, `description`,
          `x-*`). It answers "would a rehearsal (F142 `RehearsalPass`) still be valid after a
          republish?" — nothing else.

        **`publicationTxId` and `execDefHash` answer different questions and live in different
        value spaces. Several publications may legitimately share one `execDefHash`; the same
        definition published to two different registers has the same `execDefHash` and two
        different `publicationTxId` values. Never compare one against the other — that
        comparison yields a plausible-but-wrong answer rather than an error, which is exactly
        how `isPinnedToLatest` shipped hard-wired to `false`.**
        """;
}
