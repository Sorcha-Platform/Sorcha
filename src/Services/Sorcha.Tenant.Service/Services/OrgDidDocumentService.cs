// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.ServiceClients.OrgDidDocument;
using Sorcha.Tenant.Service.Configuration;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Builds, persists, and serves per-organisation DID documents (Feature 120 US2).
/// </summary>
public sealed class OrgDidDocumentService : IOrgDidDocumentService
{
    private readonly TenantDbContext _db;
    private readonly TenantSettings _settings;
    private readonly ILogger<OrgDidDocumentService> _logger;

    /// <summary>DI-friendly constructor.</summary>
    public OrgDidDocumentService(
        TenantDbContext db,
        IOptions<TenantSettings> settings,
        ILogger<OrgDidDocumentService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<OrgDidDocument?> GetAsync(Guid organizationId, CancellationToken ct = default)
        => _db.OrgDidDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.OrganizationId == organizationId, ct);

    /// <summary>
    /// Regenerate from a pushed key snapshot (the wallet-side IssuanceKeyService is the source of truth).
    /// </summary>
    public async Task<OrgDidDocument> RegenerateFromSnapshotAsync(
        OrgDidRegenerateRequest snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ActiveKeys.Count == 0)
            throw new ArgumentException("Snapshot must declare at least one Active key.", nameof(snapshot));

        var primaryDid = $"did:sorcha:org:{snapshot.WalletAddress}";
        var federatedDid = $"did:web:{_settings.PlatformDomain}:orgs:{snapshot.OrganizationId}";

        var verificationMethods = BuildVerificationMethods(primaryDid, snapshot.ActiveKeys);
        var alsoKnownAs = new[] { federatedDid };

        var docJson = SerializeDidDocument(primaryDid, alsoKnownAs, verificationMethods);
        var fingerprint = ComputeFingerprint(primaryDid, verificationMethods, alsoKnownAs);

        var existing = await _db.OrgDidDocuments
            .FirstOrDefaultAsync(d => d.OrganizationId == snapshot.OrganizationId, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var reason = ParseReason(snapshot.KeyEventReason);
            var row = new OrgDidDocument
            {
                Id = Guid.NewGuid(),
                OrganizationId = snapshot.OrganizationId,
                PrimaryDid = primaryDid,
                FederatedDid = federatedDid,
                DocumentJson = docJson,
                KeyVersionFingerprint = fingerprint,
                LastRegeneratedAt = DateTimeOffset.UtcNow,
                LastRegenerationReason = reason,
                Version = 1
            };
            _db.OrgDidDocuments.Add(row);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Bootstrapped OrgDidDocument for org {OrgId} reason={Reason}",
                snapshot.OrganizationId, reason);
            return row;
        }

        if (string.Equals(existing.KeyVersionFingerprint, fingerprint, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "OrgDidDocument regenerate is a no-op for org {OrgId} (fingerprint unchanged)",
                snapshot.OrganizationId);
            return existing;
        }

        existing.PrimaryDid = primaryDid;
        existing.FederatedDid = federatedDid;
        existing.DocumentJson = docJson;
        existing.KeyVersionFingerprint = fingerprint;
        existing.LastRegeneratedAt = DateTimeOffset.UtcNow;
        existing.LastRegenerationReason = ParseReason(snapshot.KeyEventReason);
        existing.Version += 1;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Regenerated OrgDidDocument for org {OrgId} reason={Reason} version={Version}",
            snapshot.OrganizationId, existing.LastRegenerationReason, existing.Version);

        return existing;
    }

    /// <inheritdoc />
    public Task<OrgDidDocument> RegenerateAsync(
        Guid organizationId, KeyEventReason reason, CancellationToken ct = default)
        => throw new NotSupportedException(
            "RegenerateAsync(orgId, reason) requires a key snapshot — use the snapshot overload " +
            "via the regenerate endpoint. Direct in-process regeneration without keys is not " +
            "supported in v1 (no Tenant→Wallet readback path).");

    private static List<DidVerificationMethod> BuildVerificationMethods(
        string primaryDid, IReadOnlyList<OrgDidActiveKey> keys)
    {
        var vms = new List<DidVerificationMethod>(keys.Count * 2);
        foreach (var k in keys)
        {
            using var jwkDoc = JsonDocument.Parse(k.PublicKeyJwk);
            var jwk = jwkDoc.RootElement.Clone();

            // Versioned id — the platform default kid form.
            vms.Add(new DidVerificationMethod(
                Id: $"{primaryDid}#vc-issuance-{k.RotationIndex}",
                Type: "JsonWebKey2020",
                Controller: primaryDid,
                PublicKeyJwk: jwk));

            // Thumbprint id — RFC 7638 fallback for external wallets.
            vms.Add(new DidVerificationMethod(
                Id: $"{primaryDid}#{k.Thumbprint}",
                Type: "JsonWebKey2020",
                Controller: primaryDid,
                PublicKeyJwk: jwk));
        }
        return vms;
    }

    private static string SerializeDidDocument(
        string primaryDid,
        IReadOnlyList<string> alsoKnownAs,
        IReadOnlyList<DidVerificationMethod> vms)
    {
        var assertionMethodIds = vms.Select(v => v.Id).ToArray();
        var vmObjs = vms.Select(v => new Dictionary<string, object>
        {
            ["id"] = v.Id,
            ["type"] = v.Type,
            ["controller"] = v.Controller,
            ["publicKeyJwk"] = v.PublicKeyJwk
        }).ToArray();

        var doc = new Dictionary<string, object>
        {
            ["@context"] = new[] { "https://www.w3.org/ns/did/v1", "https://w3id.org/security/jwk/v1" },
            ["id"] = primaryDid,
            ["alsoKnownAs"] = alsoKnownAs,
            ["verificationMethod"] = vmObjs,
            ["assertionMethod"] = assertionMethodIds,
            ["authentication"] = assertionMethodIds
        };

        return JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static string ComputeFingerprint(
        string primaryDid,
        IReadOnlyList<DidVerificationMethod> vms,
        IReadOnlyList<string> alsoKnownAs)
    {
        var sb = new StringBuilder();
        sb.Append(primaryDid).Append('|');
        foreach (var vm in vms.OrderBy(v => v.Id, StringComparer.Ordinal))
        {
            sb.Append(vm.Id).Append('=').Append(vm.PublicKeyJwk.GetRawText()).Append(';');
        }
        sb.Append('|');
        foreach (var aka in alsoKnownAs.OrderBy(a => a, StringComparer.Ordinal))
        {
            sb.Append(aka).Append(';');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash);
    }

    private static KeyEventReason ParseReason(string reason)
        => Enum.TryParse<KeyEventReason>(reason, ignoreCase: true, out var r)
            ? r
            : KeyEventReason.Bootstrap;

    private sealed record DidVerificationMethod(
        string Id,
        string Type,
        string Controller,
        JsonElement PublicKeyJwk);
}
