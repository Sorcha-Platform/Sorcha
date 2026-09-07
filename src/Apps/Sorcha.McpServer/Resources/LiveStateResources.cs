// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.Serialization;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Resources;

/// <summary>
/// Caller-scoped live-state resources: what is actually running or present for the calling
/// identity right now, as JSON, without spending a tool-call round trip. Reads via the same
/// typed <see cref="IBlueprintServiceClient"/> / <see cref="IRegisterServiceClient"/> tools use
/// (spec 139 US4), so the caller's bearer is forwarded and the routes are contract-pinned, not
/// hand-rolled.
/// </summary>
[McpServerResourceType]
public sealed class LiveStateResources
{
    // GetRecentRegistersAsync truncates client-side with no total count available from the
    // server (GET /api/registers/ returns the caller's whole visible set; the client does
    // .OrderByDescending(CreatedAt).Take(limit) locally) — so silently capping at N would let an
    // agent conclude "register X isn't in this list, so it doesn't exist", confidently wrong for
    // any org with more than N registers. Requesting one MORE than we display turns that into an
    // exact signal: if the extra item comes back, there are more than RegistersDisplayLimit
    // registers, without a second endpoint call for a total the API doesn't otherwise expose.
    private const int RegistersDisplayLimit = 50;

    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ICallerContext _callerContext;
    private readonly ILogger<LiveStateResources> _logger;

    /// <summary>
    /// Creates the live-state resources for the current caller.
    /// </summary>
    public LiveStateResources(
        IBlueprintServiceClient blueprintClient,
        IRegisterServiceClient registerClient,
        ICallerContext callerContext,
        ILogger<LiveStateResources> logger)
    {
        _blueprintClient = blueprintClient;
        _registerClient = registerClient;
        _callerContext = callerContext;
        _logger = logger;
    }

    /// <summary>
    /// The workflow instances visible to the calling identity.
    /// </summary>
    [McpServerResource(UriTemplate = "sorcha://instances", Name = "Your workflow instances", MimeType = "application/json")]
    [Description("The workflow instances visible to you right now, as JSON. Read this instead of spending a tool call when you only need to see what is running.")]
    public async Task<string> InstancesAsync(CancellationToken cancellationToken)
    {
        if (!_callerContext.IsAuthenticated)
        {
            return NoteJson("instances", "Not authenticated — sign in to see your workflow instances.");
        }

        try
        {
            // Raw pass-through body — this is already the shape sorcha://instances needs
            // (GET /api/instances/), so there is no separately-declared response DTO to drift
            // from the server's actual shape.
            var body = await _blueprintClient.GetWorkflowInstancesAsync(cancellationToken: cancellationToken);

            return string.IsNullOrWhiteSpace(body)
                ? NoteJson("instances", "The Blueprint service is currently unavailable.")
                : body;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to read sorcha://instances");
            return NoteJson("instances", "The Blueprint service is currently unavailable.");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Timed out reading sorcha://instances");
            return NoteJson("instances", "The Blueprint service is currently unavailable.");
        }
    }

    /// <summary>
    /// The registers visible to the calling identity's organisation, plus system registers.
    /// </summary>
    [McpServerResource(UriTemplate = "sorcha://registers", Name = "Your registers", MimeType = "application/json")]
    [Description("The registers visible to you right now (your organisation's registers, plus system registers), most-recently-created first, as JSON. Capped at 50 entries — the response carries `count` (how many are in this body) and `truncated` (true when more than 50 exist), so a register's absence from this list is NOT evidence it doesn't exist when `truncated` is true. Read this instead of spending a tool call when you only need to see what registers exist.")]
    public async Task<string> RegistersAsync(CancellationToken cancellationToken)
    {
        if (!_callerContext.IsAuthenticated)
        {
            return NoteJson("registers", "Not authenticated — sign in to see your registers.");
        }

        try
        {
            // GET /api/registers/ (Sorcha.ServiceClients.Register.RegisterServiceClient) —
            // org-scoped by the caller's forwarded bearer, plus system registers. Today this
            // client itself swallows failures and returns an empty list rather than throwing, so
            // an empty result here is indistinguishable from "no registers"; the catch clauses
            // below are defensive against that contract changing, not evidence it currently fires.
            //
            // Request one more than we display (see RegistersDisplayLimit) so truncation is an
            // exact fact, not a guess.
            var fetched = await _registerClient.GetRecentRegistersAsync(
                limit: RegistersDisplayLimit + 1, cancellationToken: cancellationToken);
            var truncated = fetched.Count > RegistersDisplayLimit;
            var visible = truncated ? fetched.Take(RegistersDisplayLimit).ToList() : fetched;

            // SorchaJson.Options — camelCase, matching the wire format every other Sorcha JSON
            // payload uses (including the raw sorcha://instances body above), rather than the
            // PascalCase JsonSerializer.Serialize's own defaults would produce from RegisterSummaryInfo.
            return JsonSerializer.Serialize(
                new { registers = visible, count = visible.Count, truncated },
                SorchaJson.Options);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to read sorcha://registers");
            return NoteJson("registers", "The Register service is currently unavailable.");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Timed out reading sorcha://registers");
            return NoteJson("registers", "The Register service is currently unavailable.");
        }
    }

    private static string NoteJson(string arrayPropertyName, string note) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                [arrayPropertyName] = Array.Empty<object>(),
                ["note"] = note,
            },
            SorchaJson.Options);
}
