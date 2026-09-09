// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Sorcha.Serialization;

namespace Sorcha.Register.Service.Tests.Helpers;

/// <summary>
/// Reads a response body the way every real Sorcha client reads it — with
/// <see cref="SorchaJson.Options"/>.
/// </summary>
/// <remarks>
/// <para>
/// These tests used the parameterless <c>ReadFromJsonAsync&lt;T&gt;()</c>, which binds with
/// <c>JsonSerializerOptions</c> defaults. Those parse an enum from a NUMBER only, so once the
/// Register Service adopted the shared wire format — enums as kebab-case strings, so that a client
/// property typed <c>string</c> can no longer silently break the whole payload (#1613) — every DTO
/// here carrying an enum threw on deserialization.
/// </para>
/// <para>
/// The tests were right about the shape and wrong about the reader: no Sorcha client uses bare
/// defaults. <see cref="SorchaJson.Options"/> reads the kebab form AND the PascalCase form AND the
/// integer form, so a test using it is testing the contract rather than a snapshot of one encoding.
/// </para>
/// <para>
/// Prefer this over <c>ReadFromJsonAsync&lt;T&gt;()</c> in this project. A test that binds with raw
/// defaults is asserting something no consumer does.
/// </para>
/// </remarks>
public static class SorchaHttpJson
{
    /// <summary>Deserializes the response body using the shared Sorcha wire format.</summary>
    public static Task<T?> ReadSorchaAsync<T>(
        this HttpContent content,
        CancellationToken cancellationToken = default) =>
        content.ReadFromJsonAsync<T>(SorchaJson.Options, cancellationToken);
}
