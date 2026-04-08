// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Tenant.Models.Persona;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

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
    private readonly ILogger<PersonaService> _logger;

    public PersonaService(
        TenantDbContext db,
        IPersonaCryptoClient crypto,
        ILogger<PersonaService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PersonaReadModelV1> GetAsync(
        Guid platformUserId,
        PersonaReadOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new PersonaReadOptions();

        if (!string.Equals(options.ActingAs, "self", StringComparison.Ordinal))
        {
            // v1 only supports self — delegation is a tracked follow-up.
            throw new NotSupportedException($"actingAs='{options.ActingAs}' is not supported in this release.");
        }

        var row = await _db.PlatformUserPersonas
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PlatformUserId == platformUserId, ct);

        if (row is null)
        {
            // New users get an empty persona — never a 404.
            return new PersonaReadModelV1();
        }

        // Fetch the user's default wallet address so we can decrypt.
        var walletAddress = await ResolvePrimaryWalletAsync(platformUserId, ct);
        if (walletAddress is null)
        {
            _logger.LogWarning(
                "Persona row exists for user {PlatformUserId} but no primary wallet is set — returning empty.",
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

        var attributes = JsonSerializer.Deserialize<PersonaAttributesV1>(plaintext, JsonOptions)
            ?? new PersonaAttributesV1();

        return Project(attributes, row.UpdatedAt);
    }

    /// <inheritdoc />
    public async Task<PersonaReadModelV1> ReplaceAsync(
        Guid platformUserId,
        PersonaAttributesV1 plaintext,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        // 1. Validate invariants.
        var normalised = ValidateAndNormalise(plaintext);

        // 2. Resolve the user's primary wallet — required for encryption.
        var walletAddress = await ResolvePrimaryWalletAsync(platformUserId, ct)
            ?? throw new PersonaWalletNotProvisionedException();

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

        // 4. Upsert the row.
        var now = DateTime.UtcNow;
        var row = await _db.PlatformUserPersonas
            .FirstOrDefaultAsync(p => p.PlatformUserId == platformUserId, ct);

        if (row is null)
        {
            row = new PlatformUserPersona
            {
                PlatformUserId = platformUserId,
                CiphertextBlob = encryptResult.Ciphertext,
                Nonce = encryptResult.Nonce,
                WrappedKeyRef = encryptResult.WrappedKeyRef,
                SchemaVersion = CurrentSchemaVersion,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.PlatformUserPersonas.Add(row);
        }
        else
        {
            row.CiphertextBlob = encryptResult.Ciphertext;
            row.Nonce = encryptResult.Nonce;
            row.WrappedKeyRef = encryptResult.WrappedKeyRef;
            row.SchemaVersion = CurrentSchemaVersion;
            row.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Persona saved for user {PlatformUserId}", platformUserId);

        return Project(normalised, row.UpdatedAt);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid platformUserId, CancellationToken ct = default)
    {
        var row = await _db.PlatformUserPersonas
            .FirstOrDefaultAsync(p => p.PlatformUserId == platformUserId, ct);

        if (row is null)
        {
            return; // Idempotent.
        }

        _db.PlatformUserPersonas.Remove(row);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Persona deleted for user {PlatformUserId}", platformUserId);
    }

    // -------- private helpers --------

    /// <summary>
    /// Resolves the user's primary wallet address from their
    /// <see cref="UserPreferences.DefaultWalletAddress"/>. Returns null if
    /// no default wallet is set. In v1 this is the sole mechanism —
    /// richer resolution (latest active wallet, etc.) is a follow-up.
    /// </summary>
    private async Task<string?> ResolvePrimaryWalletAsync(Guid platformUserId, CancellationToken ct)
    {
        return await _db.UserPreferences
            .Where(p => p.UserId == platformUserId)
            .Select(p => p.DefaultWalletAddress)
            .FirstOrDefaultAsync(ct);
    }

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
            if (string.IsNullOrWhiteSpace(email.Value) || !email.Value.Contains('@') || email.Value.Length > 320)
                errors[$"emails[{i}].value"] = ["invalid_email"];
        }
        for (var i = 0; i < input.Phones.Count; i++)
        {
            var phone = input.Phones[i];
            if (string.IsNullOrWhiteSpace(phone.Value) || !E164Regex().IsMatch(phone.Value))
                errors[$"phones[{i}].value"] = ["invalid_phone"];
        }
        for (var i = 0; i < input.Addresses.Count; i++)
        {
            var addr = input.Addresses[i];
            if (string.IsNullOrWhiteSpace(addr.Line1))
                errors[$"addresses[{i}].line1"] = ["required"];
            if (string.IsNullOrWhiteSpace(addr.City))
                errors[$"addresses[{i}].city"] = ["required"];
            if (string.IsNullOrWhiteSpace(addr.PostalCode))
                errors[$"addresses[{i}].postalCode"] = ["required"];
            if (string.IsNullOrWhiteSpace(addr.Country) || !CountryCodeRegex().IsMatch(addr.Country))
                errors[$"addresses[{i}].country"] = ["invalid_country_code"];
        }
        for (var i = 0; i < input.Nationalities.Count; i++)
        {
            if (!CountryCodeRegex().IsMatch(input.Nationalities[i]))
                errors[$"nationalities[{i}]"] = ["invalid_country_code"];
        }

        // Multi-default detection (I-1 — reject)
        if (input.Emails.Count(e => e.IsDefault) > 1) errors["emails"] = ["multiple_defaults"];
        if (input.Phones.Count(p => p.IsDefault) > 1) errors["phones"] = ["multiple_defaults"];
        if (input.Addresses.Count(a => a.IsDefault) > 1) errors["addresses"] = ["multiple_defaults"];

        if (errors.Count > 0)
            throw new PersonaValidationException(errors);

        // I-1 normalisation — promote first entry when none marked default.
        var emails = PromoteDefault(input.Emails, e => e.IsDefault, e => e with { IsDefault = true });
        var phones = PromoteDefault(input.Phones, p => p.IsDefault, p => p with { IsDefault = true });
        var addresses = PromoteDefault(input.Addresses, a => a.IsDefault, a => a with { IsDefault = true });

        return input with
        {
            Emails = emails,
            Phones = phones,
            Addresses = addresses
        };
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
    private static PersonaReadModelV1 Project(PersonaAttributesV1 attrs, DateTime lastUpdated)
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
