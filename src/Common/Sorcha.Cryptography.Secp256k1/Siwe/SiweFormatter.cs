// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using System.Text;

namespace Sorcha.Cryptography.Secp256k1.Siwe;

/// <summary>
/// Formats and parses the EIP-4361 (SIWE) message text (Feature 180). Emits/consumes the exact spec ABNF
/// (LF line separators; EIP-55 address; no trailing newline). Parsing is fail-closed — a missing required
/// field or malformed structure returns false.
/// </summary>
public static class SiweFormatter
{
    private const string Suffix = " wants you to sign in with your Ethereum account:";

    /// <summary>Render <paramref name="message"/> as EIP-4361 message text.</summary>
    public static string Format(SiweMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var domainLine = message.Scheme is { Length: > 0 } scheme
            ? $"{scheme}://{message.Domain}"
            : message.Domain;

        var sb = new StringBuilder();
        sb.Append(domainLine).Append(Suffix).Append('\n');
        sb.Append(message.Address).Append('\n');
        sb.Append('\n');
        if (message.Statement is { Length: > 0 })
        {
            sb.Append(message.Statement).Append('\n');
        }
        sb.Append('\n');
        sb.Append("URI: ").Append(message.Uri).Append('\n');
        sb.Append("Version: ").Append(message.Version).Append('\n');
        sb.Append("Chain ID: ").Append(message.ChainId.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("Nonce: ").Append(message.Nonce).Append('\n');
        sb.Append("Issued At: ").Append(message.IssuedAt);
        if (message.ExpirationTime is { Length: > 0 })
        {
            sb.Append('\n').Append("Expiration Time: ").Append(message.ExpirationTime);
        }
        if (message.NotBefore is { Length: > 0 })
        {
            sb.Append('\n').Append("Not Before: ").Append(message.NotBefore);
        }
        if (message.RequestId is { Length: > 0 })
        {
            sb.Append('\n').Append("Request ID: ").Append(message.RequestId);
        }
        if (message.Resources is { Count: > 0 })
        {
            sb.Append('\n').Append("Resources:");
            foreach (var resource in message.Resources)
            {
                sb.Append('\n').Append("- ").Append(resource);
            }
        }

        return sb.ToString();
    }

    /// <summary>Parse EIP-4361 message text; returns false (fail-closed) on any structural error.</summary>
    public static bool TryParse(string message, out SiweMessage parsed)
    {
        parsed = null!;
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        try
        {
            var lines = message.Split('\n');
            if (lines.Length < 6 || !lines[0].EndsWith(Suffix, StringComparison.Ordinal))
            {
                return false;
            }

            var domainPart = lines[0][..^Suffix.Length];
            string? scheme = null;
            var domain = domainPart;
            var schemeIdx = domainPart.IndexOf("://", StringComparison.Ordinal);
            if (schemeIdx >= 0)
            {
                scheme = domainPart[..schemeIdx];
                domain = domainPart[(schemeIdx + 3)..];
            }

            var address = lines[1];

            // address LF / LF / [statement LF] / LF / fields…
            var idx = 2;
            if (lines[idx] != string.Empty)
            {
                return false;
            }
            idx++;

            string? statement = null;
            if (lines[idx] != string.Empty)
            {
                statement = lines[idx];
                idx++;
            }
            if (lines[idx] != string.Empty)
            {
                return false;
            }
            idx++;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            List<string>? resources = null;
            for (; idx < lines.Length; idx++)
            {
                var line = lines[idx];
                if (line == "Resources:")
                {
                    resources = [];
                    for (idx++; idx < lines.Length; idx++)
                    {
                        if (lines[idx].StartsWith("- ", StringComparison.Ordinal))
                        {
                            resources.Add(lines[idx][2..]);
                        }
                        else
                        {
                            break;
                        }
                    }
                    break;
                }

                var colon = line.IndexOf(": ", StringComparison.Ordinal);
                if (colon < 0)
                {
                    return false;
                }
                fields[line[..colon]] = line[(colon + 2)..];
            }

            if (!fields.TryGetValue("URI", out var uri)
                || !fields.TryGetValue("Version", out var version)
                || !fields.TryGetValue("Chain ID", out var chainStr)
                || !fields.TryGetValue("Nonce", out var nonce)
                || !fields.TryGetValue("Issued At", out var issuedAt)
                || !long.TryParse(chainStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chainId))
            {
                return false;
            }

            parsed = new SiweMessage
            {
                Scheme = scheme,
                Domain = domain,
                Address = address,
                Statement = statement,
                Uri = uri,
                Version = version,
                ChainId = chainId,
                Nonce = nonce,
                IssuedAt = issuedAt,
                ExpirationTime = fields.GetValueOrDefault("Expiration Time"),
                NotBefore = fields.GetValueOrDefault("Not Before"),
                RequestId = fields.GetValueOrDefault("Request ID"),
                Resources = resources
            };
            return true;
        }
        catch
        {
            return false;
        }
    }
}
