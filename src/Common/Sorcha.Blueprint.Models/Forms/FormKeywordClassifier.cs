// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;

namespace Sorcha.Blueprint.Models.Forms;

/// <summary>
/// Single source of truth for classifying Blueprint form/layout <c>x-*</c> keywords as
/// <b>presentational</b> (display-only; excluded from the executable-definition hash and
/// never re-locking the Go-live rehearsal gate) versus <b>behavioural</b> (part of the
/// executable definition; included in the hash and re-locking the gate when changed).
/// </summary>
/// <remarks>
/// <para>
/// This classifier is the shared contract used by both the client-side re-lock logic
/// (Feature 142 / FR-023) and the server-side publish gate (FR-032), as well as by the
/// <c>ExecutableDefinitionHasher</c> in <c>Sorcha.Blueprint.Engine</c>. Both client and
/// server MUST agree on the classification, so the keyword sets live here and nowhere else.
/// </para>
/// <para>
/// <b>Presentational</b> keywords (do not re-lock): <c>x-pages</c>, <c>x-sections</c>,
/// <c>x-width</c>, <c>x-introduction</c>, <c>x-review</c>, <c>x-address-lookup</c>,
/// <c>x-persona</c> (profile-autofill binding). These only change how a form is laid out
/// or pre-filled, not what data is structurally submitted, what transactions are produced,
/// or what credentials are consumed/issued.
/// </para>
/// <para>
/// <b>Behavioural</b> keywords (re-lock): <c>x-file</c> (file-reference → chunked/encrypted
/// transactions), <c>x-credential-offer</c> (credential claim flow), and <c>x-holder-key</c>
/// (whether the carried delivery keys must be captured before submission — see
/// <see cref="HolderKeySchemaExtension"/>). These alter the executed pipeline.
/// </para>
/// <para>
/// <b>Fail-safe default.</b> Any unrecognised <c>x-*</c> keyword is treated as
/// <b>behavioural</b>: if we are uncertain whether a keyword changes execution, we
/// conservatively re-lock the rehearsal gate and include it in the executable-definition
/// hash. Non-<c>x-</c> keys (standard JSON Schema vocabulary, property names, etc.) are not
/// classified by this type — callers decide how to treat them.
/// </para>
/// </remarks>
public static class FormKeywordClassifier
{
    /// <summary>
    /// The set of presentational (display-only) <c>x-*</c> keywords. Mutating any of these
    /// does NOT re-lock the rehearsal gate and they are excluded from the executable-definition hash.
    /// </summary>
    public static IReadOnlySet<string> PresentationalKeywords { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "x-pages",
            "x-sections",
            "x-width",
            "x-introduction",
            "x-review",
            "x-address-lookup",
            "x-persona",
        };

    /// <summary>
    /// The set of behavioural <c>x-*</c> keywords. These are part of the executable definition;
    /// mutating any of them re-locks the rehearsal gate and they are included in the hash.
    /// </summary>
    public static IReadOnlySet<string> BehaviouralKeywords { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "x-file",
            "x-credential-offer",
            // Issue #1302 — x-holder-key gates whether the citizen's carried delivery keys must be
            // captured before submission, which decides whether a credential can be bound and
            // delivered at all. Declared explicitly rather than left to the fail-safe: the
            // classification is the same either way, but a "single source of truth" that omits a
            // keyword the code consumes cannot be read as deliberate.
            "x-holder-key",
        };

    /// <summary>
    /// Returns <see langword="true"/> when the keyword is a known presentational (display-only)
    /// <c>x-*</c> keyword. Unknown <c>x-*</c> keywords are NOT presentational (fail-safe to
    /// behavioural — see <see cref="IsBehavioural"/>).
    /// </summary>
    /// <param name="keyword">The schema keyword (e.g. <c>x-sections</c>).</param>
    /// <returns><see langword="true"/> if the keyword is classified presentational.</returns>
    public static bool IsPresentational(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        return PresentationalKeywords.Contains(keyword);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the keyword is behavioural — i.e. part of the
    /// executable definition. This is <see langword="true"/> for the known behavioural keywords
    /// AND, by the fail-safe rule, for any unknown <c>x-*</c> keyword that is not in the
    /// presentational set. Non-<c>x-</c> keys return <see langword="false"/> (not classified here).
    /// </summary>
    /// <param name="keyword">The schema keyword (e.g. <c>x-file</c>).</param>
    /// <returns><see langword="true"/> if the keyword is classified behavioural.</returns>
    public static bool IsBehavioural(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);

        // Only x-* extension keywords are classified by this type.
        if (!keyword.StartsWith("x-", StringComparison.Ordinal))
        {
            return false;
        }

        // Fail-safe: a known-behavioural keyword OR any unknown x-* keyword re-locks.
        return !PresentationalKeywords.Contains(keyword);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the key is an extension keyword (starts with
    /// <c>x-</c>) that this classifier governs. Convenience for callers that walk schema JSON.
    /// </summary>
    /// <param name="key">The schema property key.</param>
    /// <returns><see langword="true"/> if the key is an <c>x-*</c> extension keyword.</returns>
    public static bool IsExtensionKeyword(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.StartsWith("x-", StringComparison.Ordinal);
    }
}
