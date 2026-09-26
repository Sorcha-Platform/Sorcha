// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.ServiceClients.Shared;

/// <summary>
/// Reads the explanation out of a service's refusal body, so a client can hand the caller the
/// service's own reason instead of a bare status.
/// </summary>
/// <remarks>
/// One home for a rule that three clients needed (#1641 publish, #1691 rehearsal, #1673 tenant
/// reads). Understands problem+json (<c>detail</c> / <c>title</c>), the <c>{ "error": … }</c> and
/// <c>{ "message": … }</c> shapes Sorcha endpoints return, and a plain-text body.
/// </remarks>
public static class RefusalBody
{
    /// <summary>
    /// Returns the body's machine code (<c>code</c>) and human reason, either of which may be null.
    /// A JSON body with no recognised field yields no reason; a plain-text body is the reason.
    /// </summary>
    public static (string? Code, string? Reason) Read(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        var trimmed = body.Trim();
        if (trimmed[0] is not ('{' or '['))
        {
            return (null, trimmed);
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            string? Field(string name) =>
                document.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString())
                    ? value.GetString()
                    : null;

            return (Field("code"), Field("message") ?? Field("detail") ?? Field("title") ?? Field("error"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
