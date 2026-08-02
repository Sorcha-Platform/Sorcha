// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Sorcha.Serialization;

/// <summary>
/// The single source of truth for Sorcha's JSON wire format. Both sides derive from it so they can
/// never drift:
/// <list type="bullet">
///   <item>Services apply it to their serializer via <c>AddServiceDefaults</c> (server side).</item>
///   <item>UI clients deserialize responses with <see cref="Options"/> (client side).</item>
/// </list>
/// The format is <c>System.Text.Json</c> Web defaults (camelCase properties, case-insensitive
/// matching) plus enums as <b>kebab-case strings</b> (e.g. <c>PersonaAttributeSource.SelfAsserted</c>
/// → <c>"self-asserted"</c>; also required for the WebAuthn <c>"public-key"</c> credential type). A
/// client that deserialises such a response WITHOUT this converter throws on the enum and the caller
/// silently falls back to empty — the "My Profile is blank though it saved" / "couldn't load your
/// security settings" class of bug.
/// </summary>
public static class SorchaJson
{
    /// <summary>
    /// A ready-made, read-only options instance for (de)serialisation. Prefer this over
    /// <c>JsonSerializerOptions.Default</c>/<c>.Web</c> for any Sorcha API payload.
    /// </summary>
    public static readonly JsonSerializerOptions Options = CreateReadOnly();

    /// <summary>
    /// Applies the Sorcha wire-format conventions to an existing <see cref="JsonSerializerOptions"/>
    /// (e.g. the server's <c>ConfigureHttpJsonOptions</c> instance). Idempotent — safe to call more
    /// than once and on top of Web defaults.
    /// </summary>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;

        if (!options.Converters.OfType<JsonStringEnumConverter>().Any())
        {
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        }

        // Issue #1159 — source-generated metadata for the Wallet contracts. Chained (Insert at 0)
        // rather than assigned, so it takes precedence for those types while every other type still
        // falls through to the default reflection resolver. The naming policy set above governs the
        // wire names either way, so this changes performance and trim-safety, not the wire format.
        //
        // Why here rather than per host, despite the awkwardness of a generic serialisation helper
        // naming a domain contract: `Options` below is the instance UI clients deserialise with, and
        // it is MakeReadOnly() — nothing can chain a resolver into it afterwards. Per-host
        // registration therefore could NOT have covered the WASM client, which is the main
        // beneficiary (Release publish trims the reflection metadata the fallback needs). Doing it
        // in Configure reaches that static and every host that calls it, and no host can be missed.
        // Sorcha.Wallet.Contracts is a dependency-free leaf, so this cannot cycle.
        if (!options.TypeInfoResolverChain.Any(r => r is Wallet.Contracts.Serialization.WalletContractsJsonContext))
        {
            options.TypeInfoResolverChain.Insert(
                0, Wallet.Contracts.Serialization.WalletContractsJsonContext.Default);

            // MANDATORY, and the reason this is three lines rather than one. A JsonSerializerOptions
            // carries an IMPLICIT default reflection resolver only while its chain is untouched. The
            // moment anything is inserted, that implicit default is gone — so adding the Wallet
            // context alone leaves the chain able to resolve seven types and NOTHING else, and every
            // other payload in the platform dies with "JsonTypeInfo metadata for type X was not
            // provided by TypeInfoResolver". MakeReadOnly(populateMissingResolver: true) does not
            // save you either: a resolver is no longer "missing", so it adds nothing.
            //
            // Appending the default restores the previous behaviour exactly — Wallet contracts hit
            // the source-generated metadata first, everything else falls through to reflection as
            // before. (Caught by Sorcha.UI.ContractTests, which went 18-red on the one-line version.)
            if (!options.TypeInfoResolverChain.Any(r => r is DefaultJsonTypeInfoResolver))
            {
                options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
            }
        }
    }

    private static JsonSerializerOptions CreateReadOnly()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
