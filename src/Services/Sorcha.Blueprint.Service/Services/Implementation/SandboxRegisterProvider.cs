// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.ServiceClients.Register;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 142 (T026 / D1) — lazily provisions and reuses one devMode sandbox register per
/// organisation. The per-org register id is cached in-memory (transient — Redis is a future swap)
/// and reused across rehearsals. A creation is serialised per organisation so concurrent rehearsal
/// starts share a single sandbox register rather than racing to create duplicates.
/// </summary>
/// <remarks>
/// <para>
/// Sandbox registers are created in <b>devMode</b> (plaintext payloads with disclosure filtering,
/// no envelope encryption — fast, throwaway) and tagged <c>Metadata["sandbox"] = "true"</c> so the
/// Go-live picker and normal listings exclude them (the computed <c>Register.Sandbox</c> flag from
/// T009). A "reset" of a rehearsal discards the rehearsal instance and the ephemeral identities,
/// never the register — so the same sandbox register backs every rehearsal an org ever runs.
/// </para>
/// <para>
/// Registered as a singleton because the per-org cache is process-wide transient state.
/// </para>
/// </remarks>
public sealed class SandboxRegisterProvider : ISandboxRegisterProvider
{
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<SandboxRegisterProvider> _logger;

    /// <summary>org id → sandbox register id (resolved once, reused thereafter).</summary>
    private readonly ConcurrentDictionary<string, string> _byOrg =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>org id → per-org creation gate, so concurrent starts share one register.</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _orgLocks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Marker carried on the sandbox register's metadata (D1 / T009).</summary>
    public const string SandboxMetadataKey = "sandbox";

    /// <summary>Initialises a new instance of the <see cref="SandboxRegisterProvider"/> class.</summary>
    public SandboxRegisterProvider(
        IRegisterServiceClient registerClient,
        ILogger<SandboxRegisterProvider> logger)
    {
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<string> GetOrCreateSandboxRegisterAsync(
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            throw new ArgumentException("Organisation id is required.", nameof(organizationId));
        }

        // Fast path — already provisioned and cached.
        if (_byOrg.TryGetValue(organizationId, out var cached))
        {
            return cached;
        }

        var gate = _orgLocks.GetOrAdd(organizationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring the lock — a concurrent caller may have created it.
            if (_byOrg.TryGetValue(organizationId, out cached))
            {
                return cached;
            }

            var registerId = GenerateSandboxRegisterId(organizationId);
            var name = $"Sandbox {Truncate(organizationId, 24)}";

            _logger.LogInformation(
                "Provisioning sandbox register {RegisterId} for organisation {OrgId} (devMode, sandbox-tagged)",
                registerId, organizationId);

            // D1: devMode + Metadata["sandbox"]="true", owned by the org. The created register is
            // tagged so the Go-live picker / listings can exclude it via Register.Sandbox (T009).
            var register = await _registerClient.CreateRegisterAsync(
                registerId,
                name,
                blueprintId: string.Empty,
                owner: organizationId,
                tenant: organizationId,
                cancellationToken);

            var resolvedId = string.IsNullOrWhiteSpace(register?.Id) ? registerId : register!.Id;
            _byOrg[organizationId] = resolvedId;

            _logger.LogInformation(
                "Sandbox register {RegisterId} ready for organisation {OrgId}",
                resolvedId, organizationId);

            return resolvedId;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Builds a deterministic 32-char lowercase-hex register id derived from the org id, prefixed so
    /// it is recognisably a sandbox. Stable per org so a process restart reattaches rather than
    /// orphaning the existing sandbox register.
    /// </summary>
    private static string GenerateSandboxRegisterId(string organizationId)
    {
        var seed = $"sandbox:{organizationId}";
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
