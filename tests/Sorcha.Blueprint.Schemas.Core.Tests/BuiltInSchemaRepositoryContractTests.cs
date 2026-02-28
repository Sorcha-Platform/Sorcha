// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Blueprint.Schemas;

namespace Sorcha.Blueprint.Schemas.Core.Tests;

/// <summary>
/// Runs the ISchemaRepository contract tests against BuiltInSchemaRepository.
/// Built-in schemas are loaded from embedded resources in the assembly.
/// </summary>
public class BuiltInSchemaRepositoryContractTests : SchemaRepositoryContractTests
{
    private readonly BuiltInSchemaRepository _repository = new(NullLogger<BuiltInSchemaRepository>.Instance);

    protected override ISchemaRepository CreateRepository() => _repository;

    // Built-in schemas include installation, organisation, participant, register schemas.
    // The IDs follow the pattern "https://sorcha.io/schemas/..."
    protected override string GetKnownSchemaId()
    {
        // Get the first available schema ID dynamically
        var schemas = _repository.GetAllSchemasAsync().GetAwaiter().GetResult();
        return schemas.First().Metadata.Id;
    }

    protected override string GetKnownCategory()
    {
        var schemas = _repository.GetAllSchemasAsync().GetAwaiter().GetResult();
        return schemas.First().Metadata.Category;
    }

    protected override string GetKnownSearchTerm()
    {
        var schemas = _repository.GetAllSchemasAsync().GetAwaiter().GetResult();
        // Use a word from the first schema's title
        return schemas.First().Metadata.Title.Split(' ')[0];
    }
}
