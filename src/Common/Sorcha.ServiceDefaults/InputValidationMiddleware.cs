// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.ServiceDefaults.Helpers;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Middleware for input validation and protection against common attacks (SEC-003).
/// Implements OWASP Top 10 input validation recommendations.
/// </summary>
public partial class InputValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<InputValidationMiddleware> _logger;
    private readonly InputValidationOptions _options;

    // Compiled regex patterns for attack detection
    private static readonly Regex SqlInjectionPattern = CreateSqlInjectionRegex();
    private static readonly Regex XssPattern = CreateXssRegex();
    private static readonly Regex PathTraversalPattern = CreatePathTraversalRegex();
    private static readonly Regex CommandInjectionPattern = CreateCommandInjectionRegex();
    private static readonly Regex LdapInjectionPattern = CreateLdapInjectionRegex();

    public InputValidationMiddleware(
        RequestDelegate next,
        ILogger<InputValidationMiddleware> logger,
        IOptions<InputValidationOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip validation for excluded paths
        if (IsExcludedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Validate request size
        if (!ValidateContentLength(context))
        {
            await WriteErrorResponse(context, StatusCodes.Status413PayloadTooLarge,
                "Request payload too large");
            return;
        }

        // Validate query string
        if (!ValidateQueryString(context))
        {
            await WriteErrorResponse(context, StatusCodes.Status400BadRequest,
                "Invalid characters in query string");
            return;
        }

        // Validate headers
        if (!ValidateHeaders(context))
        {
            await WriteErrorResponse(context, StatusCodes.Status400BadRequest,
                "Invalid characters in headers");
            return;
        }

        // Validate path
        if (!ValidatePath(context))
        {
            await WriteErrorResponse(context, StatusCodes.Status400BadRequest,
                "Invalid path");
            return;
        }

        await _next(context);
    }

    private bool IsExcludedPath(PathString path)
    {
        var pathValue = path.Value ?? "";
        return pathValue.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || pathValue.StartsWith("/alive", StringComparison.OrdinalIgnoreCase)
            || pathValue.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase)
            || pathValue.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase)
            || pathValue.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
            || pathValue.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase);
    }

    private bool ValidateContentLength(HttpContext context)
    {
        var contentLength = context.Request.ContentLength;
        if (contentLength.HasValue && contentLength.Value > _options.MaxRequestBodySize)
        {
            LogSuspiciousRequest(context, "OversizedPayload",
                $"Content-Length: {contentLength.Value} exceeds max {_options.MaxRequestBodySize}");
            return false;
        }
        return true;
    }

    private bool ValidateQueryString(HttpContext context)
    {
        var queryString = context.Request.QueryString.Value;
        if (string.IsNullOrEmpty(queryString))
            return true;

        // Check length
        if (queryString.Length > _options.MaxQueryStringLength)
        {
            LogSuspiciousRequest(context, "OversizedQueryString",
                $"Query string length: {queryString.Length}");
            return false;
        }

        // Check for attack patterns
        if (ContainsSuspiciousPatterns(queryString, context, "QueryString"))
            return false;

        return true;
    }

    private bool ValidateHeaders(HttpContext context)
    {
        foreach (var header in context.Request.Headers)
        {
            // Skip standard headers that may contain special characters
            if (IsStandardHeader(header.Key))
                continue;

            var headerValue = header.Value.ToString();

            // Check header value length
            if (headerValue.Length > _options.MaxHeaderValueLength)
            {
                LogSuspiciousRequest(context, "OversizedHeader",
                    $"Header {header.Key} length: {headerValue.Length}");
                return false;
            }

            // Check for attack patterns in custom headers
            if (ContainsSuspiciousPatterns(headerValue, context, $"Header:{header.Key}"))
                return false;
        }

        return true;
    }

    private bool ValidatePath(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Check for path traversal
        if (PathTraversalPattern.IsMatch(path))
        {
            LogSuspiciousRequest(context, "PathTraversal", $"Path: {path}");
            return false;
        }

        // Check for null bytes
        if (path.Contains('\0'))
        {
            LogSuspiciousRequest(context, "NullByte", $"Path contains null byte");
            return false;
        }

        return true;
    }

    private bool ContainsSuspiciousPatterns(string input, HttpContext context, string location)
    {
        // URL decode to catch encoded attacks
        var decoded = Uri.UnescapeDataString(input);

        if (_options.DetectSqlInjection && SqlInjectionPattern.IsMatch(decoded))
        {
            LogSuspiciousRequest(context, "SqlInjection", $"Location: {location}");
            return true;
        }

        if (_options.DetectXss && XssPattern.IsMatch(decoded))
        {
            LogSuspiciousRequest(context, "XSS", $"Location: {location}");
            return true;
        }

        if (_options.DetectCommandInjection && CommandInjectionPattern.IsMatch(decoded))
        {
            LogSuspiciousRequest(context, "CommandInjection", $"Location: {location}");
            return true;
        }

        if (_options.DetectLdapInjection && LdapInjectionPattern.IsMatch(decoded))
        {
            LogSuspiciousRequest(context, "LdapInjection", $"Location: {location}");
            return true;
        }

        return false;
    }

    private static bool IsStandardHeader(string headerName)
    {
        return headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Accept", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Host", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Referer", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Origin", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Cache-Control", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("If-None-Match", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("If-Modified-Since", StringComparison.OrdinalIgnoreCase)
            || headerName.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
            || headerName.StartsWith("X-Request-", StringComparison.OrdinalIgnoreCase)
            || headerName.StartsWith("X-SignalR-", StringComparison.OrdinalIgnoreCase)
            || headerName.StartsWith("Sec-", StringComparison.OrdinalIgnoreCase);
    }

    private void LogSuspiciousRequest(HttpContext context, string attackType, string details)
    {
        var clientIp = GetClientIp(context);
        _logger.LogWarning(
            "Suspicious request blocked. Type: {AttackType}, IP: {ClientIp}, Path: {Path}, Details: {Details}",
            attackType, clientIp, context.Request.Path, details);
    }

    private static string GetClientIp(HttpContext context) =>
        ClientIpHelper.GetClientIp(context);

    private static async Task WriteErrorResponse(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            $"{{\"error\":\"{message}\",\"code\":\"{statusCode}\"}}");
    }

    // Regex patterns for attack detection
    // Using GeneratedRegex for performance in .NET 10

    [GeneratedRegex(
        @"(?i)(\b(SELECT|INSERT|UPDATE|DELETE|DROP|UNION|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE)\b.*\b(FROM|INTO|TABLE|WHERE|SET|VALUES)\b)|('.*(--))|(\b(OR|AND)\b\s+\d+\s*=\s*\d+)|(\bWAITFOR\b\s+\bDELAY\b)|(\bBENCHMARK\b\s*\()",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CreateSqlInjectionRegex();

    [GeneratedRegex(
        @"<\s*script[^>]*>|<\s*/\s*script\s*>|javascript\s*:|on\w+\s*=|<\s*img[^>]+onerror|<\s*svg[^>]+onload|<\s*iframe|<\s*object|<\s*embed|<\s*link[^>]+href\s*=\s*['""]?javascript|expression\s*\(|vbscript\s*:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CreateXssRegex();

    [GeneratedRegex(
        @"\.{2,}[/\\]|[/\\]\.{2,}|%2e%2e[%/\\]|%252e%252e|\.%00\.|%00",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CreatePathTraversalRegex();

    // Command injection patterns - requires more context than single characters
    // Catches: backtick execution, $() substitution, chained commands with context, dangerous commands
    [GeneratedRegex(
        @"`[^`]+`|\$\([^)]+\)|\|\s*\w+|;\s*\w+|\&\&\s*\w+|>\s*/[a-z]+|<\s*/[a-z]+|\b(nc|netcat)\s+-|\bwget\s+https?://|\bcurl\s+https?://|\bchmod\s+[0-7]+|\brm\s+-[rf]+\s",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CreateCommandInjectionRegex();

    // LDAP injection patterns - requires LDAP filter syntax context
    // Catches: LDAP filter injection attempts, not just special characters
    [GeneratedRegex(
        @"\(\||\(\&|\)\(\!|\)\(|\*\)\(|\(\*|%2528|%252a\)|%257c\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CreateLdapInjectionRegex();
}

/// <summary>
/// Configuration options for input validation middleware
/// </summary>
public class InputValidationOptions
{
    /// <summary>
    /// Maximum allowed request body size in bytes. Default: 10MB
    /// </summary>
    public long MaxRequestBodySize { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Maximum allowed query string length. Default: 2048
    /// </summary>
    public int MaxQueryStringLength { get; set; } = 2048;

    /// <summary>
    /// Maximum allowed header value length. Default: 8192
    /// </summary>
    public int MaxHeaderValueLength { get; set; } = 8192;

    /// <summary>
    /// Enable SQL injection detection. Default: true
    /// </summary>
    public bool DetectSqlInjection { get; set; } = true;

    /// <summary>
    /// Enable XSS detection. Default: true
    /// </summary>
    public bool DetectXss { get; set; } = true;

    /// <summary>
    /// Enable command injection detection. Default: true
    /// </summary>
    public bool DetectCommandInjection { get; set; } = true;

    /// <summary>
    /// Enable LDAP injection detection. Default: false (most web APIs don't use LDAP)
    /// </summary>
    public bool DetectLdapInjection { get; set; } = false;
}
