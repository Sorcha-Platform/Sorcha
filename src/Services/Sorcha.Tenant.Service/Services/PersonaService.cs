// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Tenant.Models.Persona;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services.Interfaces;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Default implementation of <see cref="IPersonaService"/>. Validates the
/// plaintext shape, resolves the owner's primary wallet, calls the Wallet
/// Service to encrypt or decrypt, and persists the ciphertext alongside
/// the owning <see cref="PlatformUser"/>.
/// </summary>
public sealed partial class PersonaService : IPersonaService
{
    private const int ListCap = 5;
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$", RegexOptions.Compiled)]
    private static partial Regex E164Regex();

    [GeneratedRegex(@"^[A-Z]{2}$", RegexOptions.Compiled)]
    private static partial Regex CountryCodeRegex();

    private readonly TenantDbContext _db;
    private readonly IPersonaCryptoClient _crypto;
    private readonly IEventService _events;
    private readonly ILogger<PersonaService> _logger;
    private readonly IPersonaInboxWriter _personaInboxWriter;

    public PersonaService(
        TenantDbContext db,
        IPersonaCryptoClient crypto,
        IEventService events,
        ILogger<PersonaService> logger,
        IPersonaInboxWriter personaInboxWriter)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _personaInboxWriter = personaInboxWriter ?? throw new ArgumentNullException(nameof(personaInboxWriter));
    }

    /// <inheritdoc />
    public async Task<PersonaReadModelV1> GetAsync(
        Guid platformUserId,
        Guid? contextOrgId = null,
        PersonaReadOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new PersonaReadOptions();

        if (!string.Equals(options.ActingAs, "self", StringComparison.Ordinal))
        {
            // v1 only supports self — delegation is a tracked follow-up.
            // Modelled as a validation failure so the endpoint maps it to
            // HTTP 400 via the existing PersonaValidationException pipeline
            // instead of relying on NotSupportedException semantics.
            throw new PersonaValidationException(new Dictionary<string, string[]>
            {
                ["actingAs"] = ["actingAs_not_supported"]
            });
        }

        // Feature 125 — per-context scoping. Personal context (null on the
        // wire) is represented as Guid.Empty in the database, which keeps
        // the composite primary key (PlatformUserId, ContextOrgId)
        // non-nullable.
        var scope = contextOrgId ?? Guid.Empty;
        var row = await _db.PlatformUserPersonas
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PlatformUserId == platformUserId && p.ContextOrgId == scope, ct);

        if (row is null)
        {
            // New users get an empty persona — never a 404.
            return new PersonaReadModelV1();
        }

        // WrappedKeyRef stores the wallet address that was used to derive the
        // content key on encrypt (see data-model.md §1). Using it directly
        // means reads never depend on the caller's currently active wallet or
        // on any org-scoped preferences row — the persona is cross-org, and
        // so is its key material reference.
        var walletAddress = row.WrappedKeyRef;
        if (string.IsNullOrEmpty(walletAddress))
        {
            _logger.LogWarning(
                "Persona row for user {PlatformUserId} has no WrappedKeyRef — returning empty.",
                platformUserId);
            return new PersonaReadModelV1();
        }

        byte[] plaintext;
        try
        {
            plaintext = await _crypto.DecryptAsync(
                walletAddress,
                row.CiphertextBlob,
                row.Nonce,
                row.WrappedKeyRef,
                ct);
        }
        catch (PersonaWalletNotFoundException)
        {
            _logger.LogWarning(
                "Wallet {WalletAddress} not found when decrypting persona for user {PlatformUserId}.",
                walletAddress, platformUserId);
            return new PersonaReadModelV1();
        }
        catch (CryptographicException ex)
        {
            // Decrypt failed — most commonly because the wallet signing
            // key was rotated after the persona was last encrypted, so the
            // deterministic content key derivation no longer produces the
            // original key. Surface as an empty persona + high-severity log
            // rather than a 500 so the UI can re-prompt. A recovery path
            // (re-encrypt on key rotation) is tracked as a follow-up.
            _logger.LogError(
                ex,
                "Persona ciphertext could not be decrypted for user {PlatformUserId} — possible wallet key rotation",
                platformUserId);
            return new PersonaReadModelV1();
        }
        catch (PersonaCryptoException ex)
        {
            // Wallet Service round-trip failure (non-404, non-key-related).
            // Same fallback as above — return empty and surface the error
            // as a high-severity log so operators can diagnose.
            _logger.LogError(
                ex,
                "Persona crypto call failed for user {PlatformUserId}",
                platformUserId);
            return new PersonaReadModelV1();
        }

        PersonaAttributesV1? attributes;
        try
        {
            attributes = JsonSerializer.Deserialize<PersonaAttributesV1>(plaintext, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Persona JSON deserialisation threw for user {PlatformUserId} — possible data corruption",
                platformUserId);
            return new PersonaReadModelV1();
        }

        if (attributes is null)
        {
            _logger.LogError(
                "Persona JSON deserialisation returned null for user {PlatformUserId} — possible data corruption",
                platformUserId);
            return new PersonaReadModelV1();
        }

        return Project(attributes, row.UpdatedAt);
    }

    /// <inheritdoc />
    public async Task<PersonaReadModelV1> ReplaceAsync(
        Guid platformUserId,
        string? walletAddress,
        PersonaAttributesV1 plaintext,
        Guid? contextOrgId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        // Feature 125 — Personal context = Guid.Empty.
        var scope = contextOrgId ?? Guid.Empty;

        // 1. Validate invariants.
        var normalised = ValidateAndNormalise(plaintext);

        // 2. The caller-supplied wallet address is the content-key derivation
        // input and is stored with the row as WrappedKeyRef so future reads
        // can derive the same key. Persona is a cross-org attribute on the
        // PlatformUser, so it is keyed by platform_user_id — the previous
        // implementation's UserPreferences lookup was org-scoped and
        // produced a foreign-key violation against PlatformUserPersonas.
        if (string.IsNullOrEmpty(walletAddress))
        {
            throw new PersonaWalletNotProvisionedException();
        }

        // 3. Encrypt via Wallet Service.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(normalised, JsonOptions);
        PersonaCryptoRemoteResult encryptResult;
        try
        {
            encryptResult = await _crypto.EncryptAsync(walletAddress, bytes, ct);
        }
        catch (PersonaWalletNotFoundException)
        {
            // User's preferred wallet address is stale — surface as not
            // provisioned so the UI prompts recovery.
            throw new PersonaWalletNotProvisionedException();
        }

        // 4. Upsert the row — with a race-safe retry on the insert path.
        // Two concurrent PUTs can both observe `row is null`; the loser of
        // the insert race catches the unique-key DbUpdateException and
        // retries as an update.
        var now = DateTimeOffset.UtcNow;
        PlatformUserPersona row;
        var attempt = 0;
        while (true)
        {
            row = await _db.PlatformUserPersonas
                .FirstOrDefaultAsync(p => p.PlatformUserId == platformUserId && p.ContextOrgId == scope, ct)
                ?? new PlatformUserPersona
                {
                    PlatformUserId = platformUserId,
                    ContextOrgId = scope,
                    CreatedAt = now
                };

            var isInsert = _db.Entry(row).State == EntityState.Detached;
            row.CiphertextBlob = encryptResult.Ciphertext;
            row.Nonce = encryptResult.Nonce;
            row.WrappedKeyRef = encryptResult.WrappedKeyRef;
            row.SchemaVersion = CurrentSchemaVersion;
            row.UpdatedAt = now;

            if (isInsert)
            {
                _db.PlatformUserPersonas.Add(row);
            }

            try
            {
                await _db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException) when (isInsert && attempt++ == 0)
            {
                // Another request won the insert race. Detach the new row
                // we just added so the next iteration can load the existing
                // row and perform an update instead. A second collision on
                // the retry (attempt > 1) is let through — concurrent PUTs
                // from the same user on the same millisecond are unusual
                // enough that surfacing the 500 is acceptable.
                _logger.LogWarning(
                    "Persona upsert race detected for user {PlatformUserId}; retrying as update",
                    platformUserId);
                _db.Entry(row).State = EntityState.Detached;
                continue;
            }
        }

        await _events.CreateEventAsync(new ActivityEvent
        {
            UserId = platformUserId,
            EventType = "persona.replaced",
            Severity = EventSeverity.Info,
            Title = "Profile saved",
            Message = "Your personal profile was updated.",
            SourceService = "TenantService",
            EntityType = "PlatformUserPersona",
            EntityId = platformUserId.ToString(),
            CreatedAt = now.UtcDateTime,
            ExpiresAt = now.UtcDateTime.AddYears(1)
        }, ct);

        try
        {
            await _personaInboxWriter.WritePersonaSavedAsync(platformUserId, "Personal Profile", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PersonaInboxWriter — failed to emit persona-saved inbox entry for {PlatformUserId}", platformUserId);
        }

        _logger.LogInformation("Persona saved for user {PlatformUserId}", platformUserId);

        return Project(normalised, row.UpdatedAt);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        Guid platformUserId,
        Guid? contextOrgId = null,
        CancellationToken ct = default)
    {
        var scope = contextOrgId ?? Guid.Empty;
        var row = await _db.PlatformUserPersonas
            .FirstOrDefaultAsync(p => p.PlatformUserId == platformUserId && p.ContextOrgId == scope, ct);

        if (row is null)
        {
            return; // Idempotent.
        }

        _db.PlatformUserPersonas.Remove(row);
        await _db.SaveChangesAsync(ct);

        var now = DateTimeOffset.UtcNow;
        await _events.CreateEventAsync(new ActivityEvent
        {
            UserId = platformUserId,
            EventType = "persona.deleted",
            Severity = EventSeverity.Warning,
            Title = "Profile deleted",
            Message = "Your personal profile was deleted.",
            SourceService = "TenantService",
            EntityType = "PlatformUserPersona",
            EntityId = platformUserId.ToString(),
            CreatedAt = now.UtcDateTime,
            ExpiresAt = now.UtcDateTime.AddYears(1)
        }, ct);

        try
        {
            await _personaInboxWriter.WritePersonaDeletedAsync(platformUserId, "Personal Profile", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PersonaInboxWriter — failed to emit persona-deleted inbox entry for {PlatformUserId}", platformUserId);
        }

        _logger.LogInformation("Persona deleted for user {PlatformUserId}", platformUserId);
    }

    // -------- private helpers --------

    /// <summary>
    /// Validates invariants I-1 through I-5 from data-model.md §2 and
    /// returns a normalised copy of the input (auto-promoting the first
    /// entry of any list that has zero explicit defaults).
    /// </summary>
    private static PersonaAttributesV1 ValidateAndNormalise(PersonaAttributesV1 input)
    {
        var errors = new Dictionary<string, string[]>();

        // List caps (I-2)
        EnforceCap(errors, "emails", input.Emails.Count);
        EnforceCap(errors, "phones", input.Phones.Count);
        EnforceCap(errors, "addresses", input.Addresses.Count);
        EnforceCap(errors, "nationalities", input.Nationalities.Count);

        // Field validation (I-3, I-4, I-5)
        for (var i = 0; i < input.Emails.Count; i++)
        {
            var email = input.Emails[i];
            if (!IsValidEmail(email.Value))
                errors[$"emails[{i}].value"] = ["invalid_email"];
        }
        for (var i = 0; i < input.Phones.Count; i++)
        {
            var phone = input.Phones[i];
            if (string.IsNullOrWhiteSpace(phone.Value) || !E164Regex().IsMatch(phone.Value))
                errors[$"phones[{i}].value"] = ["invalid_phone"];
        }
        // Normalise country codes to upper-case before regex matching so
        // browser locale values like "gb" round-trip correctly. The
        // normalised list is applied to the output below so callers never
        // have to worry about casing.
        var normalisedAddresses = new PersonaAddress[input.Addresses.Count];
        for (var i = 0; i < input.Addresses.Count; i++)
        {
            var addr = input.Addresses[i];
            if (string.IsNullOrWhiteSpace(addr.Line1))
                errors[$"addresses[{i}].line1"] = ["required"];
            if (string.IsNullOrWhiteSpace(addr.City))
                errors[$"addresses[{i}].city"] = ["required"];
            if (string.IsNullOrWhiteSpace(addr.PostalCode))
                errors[$"addresses[{i}].postalCode"] = ["required"];

            var country = (addr.Country ?? string.Empty).Trim().ToUpperInvariant();
            if (!CountryCodeRegex().IsMatch(country))
                errors[$"addresses[{i}].country"] = ["invalid_country_code"];
            normalisedAddresses[i] = addr with { Country = country };
        }
        var normalisedNationalities = new string[input.Nationalities.Count];
        for (var i = 0; i < input.Nationalities.Count; i++)
        {
            var nat = (input.Nationalities[i] ?? string.Empty).Trim().ToUpperInvariant();
            if (!CountryCodeRegex().IsMatch(nat))
                errors[$"nationalities[{i}]"] = ["invalid_country_code"];
            normalisedNationalities[i] = nat;
        }

        // Multi-default detection (I-1 — reject). Nationalities is a plain
        // string list with no IsDefault field — the first entry is treated
        // as the default by `Project`, so no multi-default check is needed.
        if (input.Emails.Count(e => e.IsDefault) > 1) errors["emails"] = ["multiple_defaults"];
        if (input.Phones.Count(p => p.IsDefault) > 1) errors["phones"] = ["multiple_defaults"];
        if (input.Addresses.Count(a => a.IsDefault) > 1) errors["addresses"] = ["multiple_defaults"];

        if (errors.Count > 0)
            throw new PersonaValidationException(errors);

        // I-1 normalisation — promote first entry when none marked default.
        var emails = PromoteDefault(input.Emails, e => e.IsDefault, e => e with { IsDefault = true });
        var phones = PromoteDefault(input.Phones, p => p.IsDefault, p => p with { IsDefault = true });
        var addresses = PromoteDefault(normalisedAddresses, a => a.IsDefault, a => a with { IsDefault = true });

        return input with
        {
            Emails = emails,
            Phones = phones,
            Addresses = addresses,
            Nationalities = normalisedNationalities,
        };
    }

    /// <summary>
    /// Validates an email address against RFC 5322 basic shape using
    /// <see cref="MailAddress.TryCreate(string?, out MailAddress?)"/>. Also
    /// requires a non-empty local part, a dot-bearing domain, and ≤ 320
    /// characters total so obviously-malformed values like <c>@example.com</c>
    /// or <c>a@</c> are rejected.
    /// </summary>
    private static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Length > 320) return false;
        if (!MailAddress.TryCreate(value, out var parsed) || parsed is null) return false;
        if (string.IsNullOrEmpty(parsed.User)) return false;
        if (string.IsNullOrEmpty(parsed.Host)) return false;
        if (!parsed.Host.Contains('.')) return false;
        return true;
    }

    private static void EnforceCap(Dictionary<string, string[]> errors, string field, int count)
    {
        if (count > ListCap)
            errors[field] = ["list_cap_exceeded"];
    }

    private static IReadOnlyList<T> PromoteDefault<T>(
        IReadOnlyList<T> list,
        Func<T, bool> isDefault,
        Func<T, T> withDefault)
    {
        if (list.Count == 0) return list;
        if (list.Any(isDefault)) return list;

        var result = new List<T>(list.Count) { withDefault(list[0]) };
        for (var i = 1; i < list.Count; i++) result.Add(list[i]);
        return result;
    }

    /// <summary>
    /// Projects a validated <see cref="PersonaAttributesV1"/> into the wire
    /// read shape with provenance wrappers.
    /// </summary>
    private static PersonaReadModelV1 Project(PersonaAttributesV1 attrs, DateTimeOffset lastUpdated)
    {
        PersonaAttribute<T>? Wrap<T>(T? value) where T : class =>
            value is null ? null : new PersonaAttribute<T>(value, PersonaAttributeSource.SelfAsserted, null, lastUpdated);

        PersonaAttribute<DateOnly>? WrapDate(DateOnly? value) =>
            value is null ? null : new PersonaAttribute<DateOnly>(value.Value, PersonaAttributeSource.SelfAsserted, null, lastUpdated);

        var defaultEmail = attrs.Emails.FirstOrDefault(e => e.IsDefault);
        var defaultPhone = attrs.Phones.FirstOrDefault(p => p.IsDefault);
        var defaultAddress = attrs.Addresses.FirstOrDefault(a => a.IsDefault);
        var defaultNationality = attrs.Nationalities.FirstOrDefault();

        return new PersonaReadModelV1
        {
            GivenName = Wrap(attrs.GivenName),
            MiddleName = Wrap(attrs.MiddleName),
            FamilyName = Wrap(attrs.FamilyName),
            FullName = Wrap(attrs.FullName),
            DateOfBirth = WrapDate(attrs.DateOfBirth),
            DefaultEmail = defaultEmail is null
                ? null
                : new PersonaAttribute<PersonaEmail>(defaultEmail, PersonaAttributeSource.SelfAsserted, null, lastUpdated),
            AllEmails = attrs.Emails,
            DefaultPhone = defaultPhone is null
                ? null
                : new PersonaAttribute<PersonaPhone>(defaultPhone, PersonaAttributeSource.SelfAsserted, null, lastUpdated),
            AllPhones = attrs.Phones,
            DefaultAddress = defaultAddress is null
                ? null
                : new PersonaAttribute<PersonaAddress>(defaultAddress, PersonaAttributeSource.SelfAsserted, null, lastUpdated),
            AllAddresses = attrs.Addresses,
            DefaultNationality = defaultNationality is null
                ? null
                : new PersonaAttribute<string>(defaultNationality, PersonaAttributeSource.SelfAsserted, null, lastUpdated),
            AllNationalities = attrs.Nationalities
        };
    }
}
