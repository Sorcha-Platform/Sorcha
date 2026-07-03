// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Minimal-API request-DTO validation (VAL-001, #327). Runs <c>System.ComponentModel.DataAnnotations</c>
/// attributes (<c>[Required]</c>, <c>[StringLength]</c>, <c>[RegularExpression]</c>, <c>[Range]</c>, …) on
/// the bound request object and returns RFC 7807 <c>400 ValidationProblem</c> before the handler runs, so
/// endpoints no longer fall back to ad-hoc null/empty checks (or none at all) deep in the handler.
/// <para>
/// The <c>AddInputValidation</c> middleware guards the attack surface (oversized bodies, malformed JSON);
/// this covers business-logic validation. Apply per endpoint or group with <see cref="WithRequestValidation"/>.
/// </para>
/// </summary>
public static class RequestValidationExtensions
{
    /// <summary>Validates bound request DTOs against their DataAnnotations before the handler runs.</summary>
    public static RouteHandlerBuilder WithRequestValidation(this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter<RequestValidationFilter>().ProducesValidationProblem();

    /// <summary>Applies request-DTO validation to every endpoint in the group.</summary>
    public static RouteGroupBuilder WithRequestValidation(this RouteGroupBuilder builder)
        => builder.AddEndpointFilter<RequestValidationFilter>();
}

/// <summary>
/// Endpoint filter that validates each bound request DTO argument (and its nested request DTOs) with
/// DataAnnotations, short-circuiting to <c>400 ValidationProblem</c> on the first invalid argument.
/// Only types that actually declare a DataAnnotations attribute on a property are inspected — framework
/// types, primitives, and injected services are skipped.
/// </summary>
public sealed class RequestValidationFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is null || !ShouldValidate(argument.GetType())) continue;

            var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Validate(argument, prefix: string.Empty, errors, new HashSet<object>(ReferenceEqualityComparer.Instance));

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray()));
            }
        }

        return await next(context);
    }

    private static void Validate(object instance, string prefix, Dictionary<string, List<string>> errors, HashSet<object> visited)
    {
        if (!visited.Add(instance)) return; // cycle guard

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);

        foreach (var result in results)
        {
            var members = result.MemberNames.Any() ? result.MemberNames : new[] { string.Empty };
            foreach (var member in members)
            {
                var key = Combine(prefix, member);
                if (!errors.TryGetValue(key, out var list)) errors[key] = list = new List<string>();
                list.Add(result.ErrorMessage ?? "Invalid value.");
            }
        }

        // Recurse into nested request DTOs so their attributes are enforced too (e.g. Branding on
        // CreateOrganizationRequest). Collections of DTOs are validated element-by-element.
        foreach (var property in instance.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length > 0) continue;

            object? value;
            try { value = property.GetValue(instance); }
            catch { continue; }
            if (value is null) continue;

            if (ShouldValidate(value.GetType()))
            {
                Validate(value, Combine(prefix, property.Name), errors, visited);
            }
            else if (value is IEnumerable enumerable and not string)
            {
                foreach (var item in enumerable)
                {
                    if (item is not null && ShouldValidate(item.GetType()))
                    {
                        Validate(item, Combine(prefix, property.Name), errors, visited);
                    }
                }
            }
        }
    }

    private static string Combine(string prefix, string member)
        => string.IsNullOrEmpty(prefix) ? member : string.IsNullOrEmpty(member) ? prefix : $"{prefix}.{member}";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> Validatable = new();

    // A type is validated iff it declares at least one DataAnnotations attribute on a property. Precise
    // (injected services, framework types, and primitives are skipped) and needs no namespace convention
    // or marker interface. Cached per type.
    private static bool ShouldValidate(Type type)
    {
        if (!type.IsClass || type == typeof(string)) return false;
        if (type.Namespace is { } ns &&
            (ns.StartsWith("System", StringComparison.Ordinal) || ns.StartsWith("Microsoft", StringComparison.Ordinal)))
        {
            return false;
        }
        return Validatable.GetOrAdd(type, static t =>
            t.GetProperties().Any(p => p.GetCustomAttributes(typeof(ValidationAttribute), inherit: true).Length > 0));
    }
}
