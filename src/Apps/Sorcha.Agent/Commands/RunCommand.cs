// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Auth;
using Sorcha.Agent.Configuration;
using Sorcha.Agent.Decision;
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
            IDecisionEngine decisionEngine = definition.Mode switch
            {
                "rules" => new RulesDecisionEngine(
                    definition.Rules!,
                    loggerFactory.CreateLogger<RulesDecisionEngine>()),
                "ai" => new AiDecisionEngine(
                    definition.Ai!,
                    aiHttpClient,
                    loggerFactory.CreateLogger<AiDecisionEngine>()),
                _ => throw new NotSupportedException($"Mode '{definition.Mode}' not supported")
            };

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
                var hubUrl = $"{definition.Connection.GatewayUrl.TrimEnd('/')}/actionshub";
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
            await foreach (var action in compositeListener.ListenAsync(cancellationToken))
            {
                if (!quiet)
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Action \"{action.ActionName}\" discovered (id: {action.ActionId[..Math.Min(8, action.ActionId.Length)]})");

                var decision = await decisionEngine.DecideAsync(action, cancellationToken);

                if (!quiet) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Decision: {decision.Decision}");

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
}
