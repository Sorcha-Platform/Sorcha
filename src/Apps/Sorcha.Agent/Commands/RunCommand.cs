// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Auth;
using Sorcha.Agent.Configuration;
using Sorcha.Agent.Decision;
using Sorcha.Agent.Decision.Checks;
using Sorcha.Agent.Execution;
using Sorcha.Agent.Inbox;
using Sorcha.Agent.Persona;

namespace Sorcha.Agent.Commands;

/// <summary>
/// The "run" command — starts a long-running autonomous actor process.
/// </summary>
public class RunCommand : Command
{
    public RunCommand() : base("run", "Start an autonomous actor")
    {
        var configOption = new Option<string>("--config")
        {
            Description = "Path to actor definition JSON file",
            Required = true
        };
        var stateOption = new Option<string?>("--state")
        {
            Description = "Path to state.json for placeholder resolution"
        };
        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Enable debug-level logging"
        };
        var quietOption = new Option<bool>("--quiet")
        {
            Description = "Errors only"
        };

        Options.Add(configOption);
        Options.Add(stateOption);
        Options.Add(verboseOption);
        Options.Add(quietOption);

        this.SetAction(async (parseResult, cancellationToken) =>
        {
            var configPath = parseResult.GetValue(configOption)!;
            var statePath = parseResult.GetValue(stateOption);
            var verbose = parseResult.GetValue(verboseOption);
            var quiet = parseResult.GetValue(quietOption);

            return await ExecuteAsync(configPath, statePath, verbose, quiet, cancellationToken);
        });
    }

    private static async Task<int> ExecuteAsync(
        string configPath,
        string? statePath,
        bool verbose,
        bool quiet,
        CancellationToken cancellationToken)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : quiet ? LogLevel.Error : LogLevel.Information);
        });

        var sw = Stopwatch.StartNew();
        var actionsProcessed = 0;
        var errors = 0;
        Task? personaTask = null;

        try
        {
            // Load config
            var loadResult = ActorDefinitionLoader.Load(configPath, statePath);
            if (!loadResult.IsSuccess)
            {
                foreach (var error in loadResult.Errors)
                    Console.Error.WriteLine($"  Config error: {error}");
                return ExitCodes.ConfigurationError;
            }

            var definition = loadResult.Definition!;
            var actorName = definition.Actor.Name;

            if (!quiet) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Actor \"{actorName}\" starting...");

            // Create HTTP client (disposed at end of scope)
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(definition.Connection.GatewayUrl.TrimEnd('/')),
                Timeout = TimeSpan.FromSeconds(30)
            };

            // Authenticate
            var authService = new AgentAuthService(
                httpClient, definition.Connection, loggerFactory.CreateLogger<AgentAuthService>());

            try
            {
                await authService.AuthenticateAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Authentication failed: {ex.Message}");
                return ExitCodes.AuthenticationError;
            }

            if (!quiet) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Authenticated");

            // Create decision engine
            // AI HttpClient is separate (calls Anthropic API, not Sorcha gateway)
            using var aiHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            // External-check runner — only built when a checks config is declared (rules mode).
            // Dedicated HttpClient: external read-only lookups (postcodes.io), not the Sorcha gateway.
            using var checksHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var checkRunner = BuildCheckRunner(definition, configPath, checksHttpClient, loggerFactory, quiet);

            IDecisionEngine decisionEngine = definition.Mode switch
            {
                "rules" => new RulesDecisionEngine(
                    definition.Rules!,
                    checkRunner,
                    loggerFactory.CreateLogger<RulesDecisionEngine>()),
                "ai" => new AiDecisionEngine(
                    definition.Ai!,
                    aiHttpClient,
                    loggerFactory.CreateLogger<AiDecisionEngine>()),
                _ => throw new NotSupportedException($"Mode '{definition.Mode}' not supported")
            };

            // Feature 176 — per-action disclosed-data fetch. Only engines that depend on the disclosed
            // prior-action payload (rules with external checks) use it; others opt out via
            // RequiresDisclosedPayload and pay nothing. Uses the agent's own HttpClient/bearer so the
            // endpoint resolves the agent's wallet as the disclosure recipient.
            var disclosedDataClient = new HttpDisclosedDataClient(
                httpClient, authService, loggerFactory.CreateLogger<HttpDisclosedDataClient>());
            var disclosedPayloadEnricher = new DisclosedPayloadEnricher(
                disclosedDataClient, loggerFactory.CreateLogger<DisclosedPayloadEnricher>());

            // Create audit logger
            using var auditLogger = new AuditLogger(definition.Logging?.ActionLog);

            // Create action executor
            var actionExecutor = new ActionExecutor(
                httpClient, authService,
                definition.Connection.WalletAddress,
                definition.Connection.RegisterId,
                loggerFactory.CreateLogger<ActionExecutor>(), auditLogger);

            // Create inbox listeners
            var listeners = new List<IInboxListener>();

            if (definition.Inbox.SignalR?.Enabled ?? false)
            {
                var hubUrl = $"{definition.Connection.GatewayUrl.TrimEnd('/')}/hubs/blueprint";
                var signalR = new SignalRInboxListener(
                    hubUrl,
                    definition.Connection.WalletAddress,
                    authService,
                    loggerFactory.CreateLogger<SignalRInboxListener>());
                listeners.Add(signalR);
                if (!quiet) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] SignalR enabled");
            }

            if (definition.Inbox.Polling?.Enabled ?? false)
            {
                var interval = definition.Inbox.Polling.IntervalSeconds;
                var polling = new PollingInboxListener(
                    httpClient, authService, interval,
                    loggerFactory.CreateLogger<PollingInboxListener>());
                listeners.Add(polling);
                if (!quiet) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Polling enabled ({interval}s interval)");
            }

            // Issue #1446 — opt-in: also watch for Feature 103 OPEN starting actions of a named
            // blueprint, so this agent can START a workflow. These never appear in /pending: until
            // somebody submits, the open participant is bound to no wallet, so the action is in
            // nobody's assigned work. Off unless the actor asks for it, and scoped to one blueprint.
            if (definition.Inbox.OpenStarting?.Enabled ?? false)
            {
                var openStarting = definition.Inbox.OpenStarting!;
                if (string.IsNullOrWhiteSpace(openStarting.BlueprintId))
                {
                    Console.Error.WriteLine($"  Config error: {OpenStartingConfig.BlueprintIdRequiredError}");
                    return ExitCodes.ConfigurationError;
                }

                var openInterval = openStarting.IntervalSeconds
                    ?? definition.Inbox.Polling?.IntervalSeconds
                    ?? 60;
                var openListener = new PollingInboxListener(
                    httpClient, authService, openInterval,
                    loggerFactory.CreateLogger<PollingInboxListener>(),
                    PollingInboxListener.OpenStartingPath(openStarting.BlueprintId!, definition.Connection.RegisterId));
                listeners.Add(openListener);
                if (!quiet)
                    Console.WriteLine(
                        $"[{DateTime.Now:HH:mm:ss}] Open-starting watch enabled for blueprint "
                        + $"\"{openStarting.BlueprintId}\" ({openInterval}s interval)");
            }

            var compositeListener = new CompositeInboxListener(
                loggerFactory.CreateLogger<CompositeInboxListener>(),
                listeners.ToArray());

            // Persona loop — optional; runs alongside reactive inbox loop when a persona file is declared.
            // Launched BEFORE entering the inbox loop so its delaySeconds countdown starts immediately,
            // and so the two loops share the same cancellationToken — Ctrl+C unwinds both within the
            // ≤ 1 s budget validated by PersonaShutdownTests (Feature 110 T039, SC-004).
            if (!string.IsNullOrWhiteSpace(definition.PersonaFile))
            {
                var personaPath = Path.IsPathRooted(definition.PersonaFile)
                    ? definition.PersonaFile
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configPath) ?? ".", definition.PersonaFile));

                var personaLoad = PersonaDefinitionLoader.Load(personaPath, statePath);
                if (!personaLoad.IsSuccess)
                {
                    foreach (var error in personaLoad.Errors)
                        Console.Error.WriteLine($"  Persona error: {error}");
                    return ExitCodes.ConfigurationError;
                }

                var personaSubmitter = new PersonaSubmitter(
                    httpClient, authService,
                    definition.Connection.WalletAddress,
                    definition.Connection.RegisterId,
                    loggerFactory.CreateLogger<PersonaSubmitter>());

                var personaHost = new PersonaHost(
                    personaLoad.Definition!,
                    personaSubmitter,
                    new PayloadTokenResolver(),
                    new RandomSource(),
                    TimeProvider.System,
                    loggerFactory,
                    auditLogger);

                // Don't wrap in Task.Run — personaHost.RunAsync is already async and the wrapper
                // would box OperationCanceledException as AggregateException on the shutdown join.
                personaTask = personaHost.RunAsync(cancellationToken);
                if (!quiet) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Persona \"{personaLoad.Definition!.Name}\" loaded");
            }

            if (!quiet) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Actor \"{actorName}\" started");

            // Main loop
            await foreach (var discovered in compositeListener.ListenAsync(cancellationToken))
            {
                var action = discovered;

                if (!quiet)
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Action \"{action.ActionName}\" discovered (id: {action.ActionId[..Math.Min(8, action.ActionId.Length)]})");

                // Feature 176: fetch the disclosed prior-action data the decision depends on, and hold
                // (fail-closed) if it cannot be obtained — never decide on a blank view. Retries naturally
                // on the next poll. Skipped for engines that don't need the payload.
                if (decisionEngine.RequiresDisclosedPayload)
                {
                    var enriched = await disclosedPayloadEnricher.EnrichAsync(action, cancellationToken);
                    if (enriched.ShouldHold)
                    {
                        if (!quiet)
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Action \"{action.ActionName}\" HELD (no data): {enriched.HoldReason}");
                        continue;
                    }

                    action = enriched.Action;
                }

                var decision = await decisionEngine.DecideAsync(action, cancellationToken);

                if (!quiet) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Decision: {decision.Decision}");

                // A "hold" is a deliberate no-op: the agent submits nothing (no approve/reject) and the
                // action is left pending for manual review / re-evaluation on a later poll (Feature 176 /
                // #1077). "skip" is likewise non-submitting.
                if (decision.Decision == "hold")
                {
                    if (!quiet)
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Action \"{action.ActionName}\" HELD: {decision.Reasoning}");
                    continue;
                }

                if (decision.Decision != "skip")
                {
                    // Execute preActions (e.g., file uploads) and merge results into payload
                    if (decision.PreActions is { Length: > 0 })
                    {
                        var fileUploadHandler = new FileUploadHandler(
                            httpClient, authService,
                            definition.Connection.WalletAddress,
                            definition.Connection.RegisterId,
                            loggerFactory.CreateLogger<FileUploadHandler>());

                        var mergedPayload = decision.Payload ?? new Dictionary<string, object>();
                        foreach (var preAction in decision.PreActions)
                        {
                            if (preAction.Type == "file-upload")
                            {
                                if (!quiet)
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Uploading file for field \"{preAction.Config.FieldName}\"...");
                                var fileRef = await fileUploadHandler.ExecuteAsync(preAction.Config, cancellationToken);
                                mergedPayload[preAction.Config.FieldName] = fileRef;
                            }
                        }
                        decision = decision with { Payload = mergedPayload };
                    }

                    var success = await actionExecutor.ExecuteAsync(action, decision, cancellationToken);
                    if (success)
                    {
                        actionsProcessed++;
                        if (!quiet)
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Action \"{action.ActionName}\" submitted successfully");
                    }
                    else
                    {
                        errors++;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return ExitCodes.GeneralError;
        }
        finally
        {
            if (personaTask is not null)
            {
                try
                {
                    await personaTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
                catch (OperationCanceledException) { /* persona cancelled — expected on shutdown */ }
                catch (TimeoutException) { /* persona didn't finish in grace window — non-fatal */ }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Persona task terminated unexpectedly: {ex.Message}");
                }
            }
        }

        if (!quiet)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Shutting down");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Summary: {actionsProcessed} actions processed, {errors} errors, uptime {sw.Elapsed:hh\\:mm\\:ss}");
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// Builds the external-check runner from the actor's optional <c>ChecksFile</c> (resolved
    /// relative to the config file), or <c>null</c> when none is declared. Throws on any load
    /// failure — a declared checks file that cannot be loaded is a configuration error that must
    /// abort startup rather than silently allow the agent to approve all actions.
    /// </summary>
    private static ExternalCheckRunner? BuildCheckRunner(
        ActorDefinition definition,
        string configPath,
        HttpClient checksHttpClient,
        ILoggerFactory loggerFactory,
        bool quiet)
    {
        if (string.IsNullOrWhiteSpace(definition.ChecksFile))
            return null;

        var checksPath = Path.IsPathRooted(definition.ChecksFile)
            ? definition.ChecksFile
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configPath) ?? ".", definition.ChecksFile));

        var runner = ExternalCheckFactory.BuildRunner(checksPath, checksHttpClient, loggerFactory);
        if (!quiet)
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] External checks loaded from {Path.GetFileName(checksPath)}");
        return runner;
    }
}
