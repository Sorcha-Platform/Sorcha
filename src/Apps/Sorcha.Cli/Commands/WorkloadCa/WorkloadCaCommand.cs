// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Security.Cryptography.X509Certificates;
using Spectre.Console;
using Sorcha.WorkloadIdentity;

namespace Sorcha.Cli.Commands.WorkloadCa;

/// <summary>
/// F191 (#1420) — `sorcha workload-ca` owns the full lifecycle of the per-installation
/// workload-identity certificate material used for service-to-service mTLS auth:
/// <c>init</c> (idempotent provisioning), <c>status</c> (expiry report; exit 2 inside the
/// renewal threshold), <c>renew</c> (threshold-based leaf re-issue), and <c>rotate-ca</c>
/// (new CA with trust-bundle overlap; <c>--complete</c> drops the old root).
/// </summary>
public class WorkloadCaCommand : Command
{
    public WorkloadCaCommand()
        : base("workload-ca", "Manage the installation's workload-identity CA and service certificates (F191)")
    {
        Subcommands.Add(new InitCommand());
        Subcommands.Add(new StatusCommand());
        Subcommands.Add(new RenewCommand());
        Subcommands.Add(new RotateCaCommand());
    }

    internal static Option<string> DirOption() => new("--dir")
    {
        Description = "Certificate directory",
        DefaultValueFactory = _ => "./config/workload-certs",
    };

    internal static Option<string> InstallationOption() => new("--installation")
    {
        Description = "Installation name (must match the deployment's JwtSettings:InstallationName)",
        DefaultValueFactory = _ => "sorcha",
    };

    internal static Option<string?> PasswordOption() => new("--password")
    {
        Description = "PFX password (default: WORKLOAD_CERT_PASSWORD environment variable)",
    };

    internal static Option<int> ThresholdOption() => new("--threshold-days")
    {
        Description = "Renewal threshold in days",
        DefaultValueFactory = _ => WorkloadIdentityConfig.DefaultExpiryWarningDays,
    };

    internal static string? ResolvePassword(string? optionValue) =>
        optionValue ?? Environment.GetEnvironmentVariable("WORKLOAD_CERT_PASSWORD");
}

/// <summary>`workload-ca init` — provision CA + per-service leaves + server cert; idempotent.</summary>
internal sealed class InitCommand : Command
{
    public InitCommand() : base("init", "Create the Workload CA and per-service certificates (idempotent)")
    {
        var dirOption = WorkloadCaCommand.DirOption();
        var installationOption = WorkloadCaCommand.InstallationOption();
        var passwordOption = WorkloadCaCommand.PasswordOption();
        var servicesOption = new Option<string?>("--services")
        {
            Description = "Override the issuance universe: client_id=dns-host,client_id=dns-host (default: the 8 seeded principals)",
        };

        Options.Add(dirOption);
        Options.Add(installationOption);
        Options.Add(passwordOption);
        Options.Add(servicesOption);

        this.SetAction((ParseResult parseResult, CancellationToken _) =>
        {
            try
            {
                var store = new WorkloadCaStore(
                    parseResult.GetValue(dirOption)!,
                    WorkloadCaCommand.ResolvePassword(parseResult.GetValue(passwordOption)));
                var installation = parseResult.GetValue(installationOption)!;
                var serviceMap = WorkloadCaStore.ResolveServiceMap(parseResult.GetValue(servicesOption));

                var report = Run(store, installation, serviceMap);
                RenderReport(report);
                return Task.FromResult(ExitCodes.Success);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]workload-ca init failed:[/] {ex.Message}");
                return Task.FromResult(ExitCodes.GeneralError);
            }
        });
    }

    private static List<(string Artifact, string Action)> Run(
        WorkloadCaStore store, string installation, IReadOnlyDictionary<string, string> serviceMap)
    {
        var report = new List<(string, string)>();
        store.EnsureLayout();

        // CA: reuse valid material, never re-issue silently.
        var ca = store.TryLoadPfxLenient(store.CaPfxPath);
        var now = DateTime.UtcNow;
        if (ca is null || ca.NotAfter.ToUniversalTime() <= now || !ca.HasPrivateKey)
        {
            ca?.Dispose();
            ca = WorkloadCertificateAuthority.CreateCertificateAuthority(installation);
            store.SavePfx(store.CaPfxPath, ca);
            report.Add(("ca", "created"));
        }
        else
        {
            report.Add(("ca", "unchanged"));
        }

        // Bundle: keep an existing bundle that already trusts the current CA (it may carry a
        // rotation-overlap second root); otherwise (re)write it as [ca].
        var bundle = TryLoadBundleLenient(store);
        var caThumbprint = ca.Thumbprint;
        if (bundle is null || bundle.Roots.All(r => r.Thumbprint != caThumbprint))
        {
            store.SaveBundle([ca]);
            bundle = store.TryLoadBundle()!;
            report.Add(("bundle", "written"));
        }
        else
        {
            report.Add(("bundle", "unchanged"));
        }

        // Leaves: valid = loadable + expected SPIFFE identity + chains to the bundle.
        foreach (var (clientId, dnsHost) in serviceMap.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var expectedId = SpiffeId.ForService(installation, clientId);
            var leafPath = store.LeafPath(clientId);
            using var existing = store.TryLoadPfxLenient(leafPath);
            var valid = existing is not null
                && WorkloadCertificateAuthority.TryGetSpiffeId(existing, out var actualId)
                && expectedId.Equals(actualId)
                && bundle.Validate(existing, out _);

            if (valid)
            {
                report.Add((clientId, "unchanged"));
                continue;
            }

            using var leaf = WorkloadCertificateAuthority.IssueServiceCertificate(ca, expectedId, dnsHost);
            store.SavePfx(leafPath, leaf);
            report.Add((clientId, "created"));
        }

        // Server certificate for the Tenant mTLS listener.
        using var existingServer = store.TryLoadPfxLenient(store.ServerPfxPath);
        if (existingServer is not null && bundle.Validate(existingServer, out _))
        {
            report.Add(("server (tenant-service)", "unchanged"));
        }
        else
        {
            using var server = WorkloadCertificateAuthority.IssueServerCertificate(ca, "tenant-service");
            store.SavePfx(store.ServerPfxPath, server);
            report.Add(("server (tenant-service)", "created"));
        }

        ca.Dispose();
        return report;
    }

    private static WorkloadTrustBundle? TryLoadBundleLenient(WorkloadCaStore store)
    {
        try
        {
            return store.TryLoadBundle();
        }
        catch (WorkloadCertificateLoadException)
        {
            return null;
        }
    }

    private static void RenderReport(List<(string Artifact, string Action)> report)
    {
        var table = new Table().AddColumn("Artifact").AddColumn("Action");
        foreach (var (artifact, action) in report)
            table.AddRow(artifact.EscapeMarkup(), action);
        AnsiConsole.Write(table);
        if (report.Any(r => r.Action != "unchanged"))
            AnsiConsole.MarkupLine("[yellow]Recreate the service containers to pick up new certificate material.[/]");
    }
}

/// <summary>`workload-ca status` — expiry report; exit 0 healthy, 2 inside threshold/invalid, 1 error.</summary>
internal sealed class StatusCommand : Command
{
    public StatusCommand() : base("status", "Report certificate expiry state (exit 2 when anything is inside the renewal threshold)")
    {
        var dirOption = WorkloadCaCommand.DirOption();
        var passwordOption = WorkloadCaCommand.PasswordOption();
        var thresholdOption = WorkloadCaCommand.ThresholdOption();

        Options.Add(dirOption);
        Options.Add(passwordOption);
        Options.Add(thresholdOption);

        this.SetAction((ParseResult parseResult, CancellationToken _) =>
        {
            try
            {
                var store = new WorkloadCaStore(
                    parseResult.GetValue(dirOption)!,
                    WorkloadCaCommand.ResolvePassword(parseResult.GetValue(passwordOption)));
                var threshold = parseResult.GetValue(thresholdOption);

                var bundle = store.TryLoadBundle();
                if (!store.Exists || bundle is null || !File.Exists(store.CaPfxPath))
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]No workload CA material at '{store.Root}'[/] — run `sorcha workload-ca init` first.");
                    return Task.FromResult(ExitCodes.GeneralError);
                }

                var now = DateTimeOffset.UtcNow;
                var statuses = new List<WorkloadCertificateStatus>();

                Describe(store, store.CaPfxPath, "ca", bundle, now, threshold, statuses, validateChain: false);
                foreach (var (clientId, path) in store.EnumerateLeaves())
                    Describe(store, path, $"service-leaf {clientId}", bundle, now, threshold, statuses, validateChain: true);
                if (File.Exists(store.ServerPfxPath))
                    Describe(store, store.ServerPfxPath, "server tenant-service", bundle, now, threshold, statuses, validateChain: true);

                Render(statuses);

                var allOk = statuses.All(s => s.State == WorkloadCertificateState.Ok);
                // Exit 2 (distinct from hard error 1) is this command's documented
                // "attention needed" code — scriptable from cron/setup.
                return Task.FromResult(allOk ? ExitCodes.Success : 2);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]workload-ca status failed:[/] {ex.Message}");
                return Task.FromResult(ExitCodes.GeneralError);
            }
        });
    }

    private static void Describe(
        WorkloadCaStore store, string path, string kind, WorkloadTrustBundle bundle,
        DateTimeOffset now, int threshold, List<WorkloadCertificateStatus> statuses, bool validateChain)
    {
        using var cert = store.TryLoadPfxLenient(path);
        if (cert is null)
        {
            statuses.Add(new WorkloadCertificateStatus(kind, path, null, now, 0, WorkloadCertificateState.Invalid));
            return;
        }

        var status = WorkloadCertificateInventory.Describe(kind, cert, now, threshold);
        if (validateChain && status.State != WorkloadCertificateState.Expired && !bundle.Validate(cert, out _))
        {
            status = status with { State = WorkloadCertificateState.Invalid };
        }

        statuses.Add(status);
    }

    private static void Render(List<WorkloadCertificateStatus> statuses)
    {
        var table = new Table()
            .AddColumn("Kind").AddColumn("Subject").AddColumn("Identity")
            .AddColumn("Expires (UTC)").AddColumn("Days left").AddColumn("State");
        foreach (var s in statuses)
        {
            var stateMarkup = s.State switch
            {
                WorkloadCertificateState.Ok => "[green]ok[/]",
                WorkloadCertificateState.Expiring => "[yellow]expiring[/]",
                WorkloadCertificateState.Expired => "[red]expired[/]",
                _ => "[red]invalid[/]",
            };
            table.AddRow(
                s.Kind.EscapeMarkup(),
                s.Subject.EscapeMarkup(),
                (s.Identity ?? "-").EscapeMarkup(),
                s.NotAfter.ToString("yyyy-MM-dd"),
                s.DaysRemaining.ToString(),
                stateMarkup);
        }

        AnsiConsole.Write(table);
    }
}

/// <summary>`workload-ca renew` — re-issue leaves/server cert inside the threshold under the current CA.</summary>
internal sealed class RenewCommand : Command
{
    public RenewCommand() : base("renew", "Re-issue certificates inside the renewal threshold under the current CA")
    {
        var dirOption = WorkloadCaCommand.DirOption();
        var passwordOption = WorkloadCaCommand.PasswordOption();
        var thresholdOption = WorkloadCaCommand.ThresholdOption();
        var allOption = new Option<bool>("--all") { Description = "Re-issue every leaf regardless of expiry" };
        var servicesOption = new Option<string?>("--services")
        {
            Description = "Override client_id=dns-host map used for re-issue (default: the 8 seeded principals)",
        };

        Options.Add(dirOption);
        Options.Add(passwordOption);
        Options.Add(thresholdOption);
        Options.Add(allOption);
        Options.Add(servicesOption);

        this.SetAction((ParseResult parseResult, CancellationToken _) =>
        {
            try
            {
                var store = new WorkloadCaStore(
                    parseResult.GetValue(dirOption)!,
                    WorkloadCaCommand.ResolvePassword(parseResult.GetValue(passwordOption)));
                var threshold = parseResult.GetValue(thresholdOption);
                var all = parseResult.GetValue(allOption);
                var serviceMap = WorkloadCaStore.ResolveServiceMap(parseResult.GetValue(servicesOption));

                using var ca = store.TryLoadPfx(store.CaPfxPath)
                    ?? throw new WorkloadCertificateLoadException($"No workload CA at '{store.CaPfxPath}' — run init first.");

                var reissued = new List<string>();
                var now = DateTimeOffset.UtcNow;

                foreach (var (clientId, path) in store.EnumerateLeaves())
                {
                    using var existing = store.TryLoadPfx(path)!;
                    if (!all && WorkloadCertificateInventory.Describe("leaf", existing, now, threshold).State == WorkloadCertificateState.Ok)
                        continue;

                    WorkloadCertificateAuthority.TryGetSpiffeId(existing, out var id);
                    id ??= SpiffeId.ForService("sorcha", clientId); // legacy material without a SAN — reissue with defaults
                    using var leaf = WorkloadCertificateAuthority.IssueServiceCertificate(
                        ca, id, WorkloadCaStore.DnsHostFor(clientId, serviceMap));
                    store.SavePfx(path, leaf);
                    reissued.Add(clientId);
                }

                if (File.Exists(store.ServerPfxPath))
                {
                    using var existingServer = store.TryLoadPfx(store.ServerPfxPath)!;
                    if (all || WorkloadCertificateInventory.Describe("server", existingServer, now, threshold).State != WorkloadCertificateState.Ok)
                    {
                        using var server = WorkloadCertificateAuthority.IssueServerCertificate(ca, "tenant-service");
                        store.SavePfx(store.ServerPfxPath, server);
                        reissued.Add("server (tenant-service)");
                    }
                }

                if (reissued.Count == 0)
                {
                    AnsiConsole.MarkupLine("[green]Nothing inside the renewal threshold — no certificates re-issued.[/]");
                }
                else
                {
                    foreach (var name in reissued)
                        AnsiConsole.MarkupLineInterpolated($"re-issued: {name}");
                    AnsiConsole.MarkupLine(
                        "[yellow]Services load certificates at startup — recreate the affected containers to pick these up.[/]");
                }

                return Task.FromResult(ExitCodes.Success);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]workload-ca renew failed:[/] {ex.Message}");
                return Task.FromResult(ExitCodes.GeneralError);
            }
        });
    }
}

/// <summary>`workload-ca rotate-ca` — new CA with bundle overlap; `--complete` drops the old root.</summary>
internal sealed class RotateCaCommand : Command
{
    public RotateCaCommand() : base("rotate-ca", "Rotate the Workload CA (overlap bundle), or --complete to drop the old root")
    {
        var dirOption = WorkloadCaCommand.DirOption();
        var installationOption = WorkloadCaCommand.InstallationOption();
        var passwordOption = WorkloadCaCommand.PasswordOption();
        var servicesOption = new Option<string?>("--services")
        {
            Description = "Override client_id=dns-host map used for re-issue (default: the 8 seeded principals)",
        };
        var completeOption = new Option<bool>("--complete")
        {
            Description = "Finish a rotation: drop the previous root from the trust bundle",
        };

        Options.Add(dirOption);
        Options.Add(installationOption);
        Options.Add(passwordOption);
        Options.Add(servicesOption);
        Options.Add(completeOption);

        this.SetAction((ParseResult parseResult, CancellationToken _) =>
        {
            try
            {
                var store = new WorkloadCaStore(
                    parseResult.GetValue(dirOption)!,
                    WorkloadCaCommand.ResolvePassword(parseResult.GetValue(passwordOption)));

                return Task.FromResult(parseResult.GetValue(completeOption)
                    ? Complete(store)
                    : Rotate(store,
                        parseResult.GetValue(installationOption)!,
                        WorkloadCaStore.ResolveServiceMap(parseResult.GetValue(servicesOption))));
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]workload-ca rotate-ca failed:[/] {ex.Message}");
                return Task.FromResult(ExitCodes.GeneralError);
            }
        });
    }

    private static int Rotate(WorkloadCaStore store, string installation, IReadOnlyDictionary<string, string> serviceMap)
    {
        using var oldCa = store.TryLoadPfx(store.CaPfxPath)
            ?? throw new WorkloadCertificateLoadException($"No workload CA at '{store.CaPfxPath}' — run init first.");

        using var newCa = WorkloadCertificateAuthority.CreateCertificateAuthority(installation);

        // Order matters for crash-safety: previous-CA copy first, then the overlap bundle
        // (so existing leaves stay valid), then the new CA, then re-issued leaves.
        store.SavePfx(store.PreviousCaPfxPath, oldCa);
        store.SaveBundle([newCa, oldCa]);
        store.SavePfx(store.CaPfxPath, newCa);

        foreach (var (clientId, path) in store.EnumerateLeaves())
        {
            using var existing = store.TryLoadPfx(path)!;
            WorkloadCertificateAuthority.TryGetSpiffeId(existing, out var id);
            id ??= SpiffeId.ForService(installation, clientId);
            using var leaf = WorkloadCertificateAuthority.IssueServiceCertificate(
                newCa, id, WorkloadCaStore.DnsHostFor(clientId, serviceMap));
            store.SavePfx(path, leaf);
        }

        if (File.Exists(store.ServerPfxPath))
        {
            using var server = WorkloadCertificateAuthority.IssueServerCertificate(newCa, "tenant-service");
            store.SavePfx(store.ServerPfxPath, server);
        }

        AnsiConsole.MarkupLine(
            "CA rotated. The trust bundle now carries [bold]both[/] roots (overlap). " +
            "Recreate all service containers, verify minting, then run `workload-ca rotate-ca --complete` to drop the old root.");
        return ExitCodes.Success;
    }

    private static int Complete(WorkloadCaStore store)
    {
        var bundle = store.TryLoadBundle()
            ?? throw new WorkloadCertificateLoadException($"No trust bundle at '{store.BundlePath}'.");
        using var currentCa = store.TryLoadPfx(store.CaPfxPath)
            ?? throw new WorkloadCertificateLoadException($"No workload CA at '{store.CaPfxPath}'.");

        if (bundle.Roots.Count < 2)
        {
            AnsiConsole.MarkupLine(
                "[red]Nothing to complete[/] — the trust bundle carries a single root, so no rotation overlap is pending.");
            return ExitCodes.GeneralError;
        }

        var currentRoot = bundle.Roots.FirstOrDefault(r => r.Thumbprint == currentCa.Thumbprint)
            ?? throw new WorkloadCertificateLoadException(
                "The current CA is not in the trust bundle — the material is inconsistent; re-run rotate-ca.");

        store.SaveBundle([currentRoot]);
        if (File.Exists(store.PreviousCaPfxPath))
            File.Delete(store.PreviousCaPfxPath);

        AnsiConsole.MarkupLine(
            "Rotation complete: old root dropped from the trust bundle. Recreate service containers so validators reload the bundle.");
        return ExitCodes.Success;
    }
}
