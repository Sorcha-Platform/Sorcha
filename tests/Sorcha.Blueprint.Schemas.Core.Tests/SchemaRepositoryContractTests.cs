// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Schemas.Client;
using Xunit;

namespace Sorcha.Blueprint.Schemas.Core.Tests;

/// <summary>
/// Contract tests for ISchemaRepository. All implementations must satisfy these tests.
/// Derive from this class and implement CreateRepository() to test a specific implementation.
/// The repository must contain at least one schema for tests to be meaningful.
/// </summary>
public abstract class SchemaRepositoryContractTests
{
    protected abstract ISchemaRepository CreateRepository();

    /// <summary>
    /// Override to provide a known schema ID that exists in the repository.
    /// </summary>
    protected abstract string GetKnownSchemaId();

    /// <summary>
    /// Override to provide a known category that has at least one schema.
    /// </summary>
    protected abstract string GetKnownCategory();

    /// <summary>
    /// Override to provide a search term that matches at least one schema.
    /// </summary>
    protected abstract string GetKnownSearchTerm();

    private ISchemaRepository Sut => CreateRepository();

    // ===========================
    // SourceType
    // ===========================

    [Fact]
    public void SourceType_ReturnsValidValue()
    {
        var result = Sut.SourceType;

        result.Should().BeDefined();
    }

    // ===========================
    // GetAllSchemasAsync
    // ===========================

    [Fact]
    public async Task GetAllSchemasAsync_ReturnsNonNullCollection()
    {
        var result = await Sut.GetAllSchemasAsync();

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAllSchemasAsync_ReturnsAtLeastOneSchema()
    {
        var result = (await Sut.GetAllSchemasAsync()).ToList();

        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAllSchemasAsync_SchemasHaveMetadata()
    {
        var result = (await Sut.GetAllSchemasAsync()).ToList();

        result.Should().AllSatisfy(s =>
        {
            s.Metadata.Should().NotBeNull();
            s.Metadata.Id.Should().NotBeNullOrWhiteSpace();
            s.Metadata.Title.Should().NotBeNullOrWhiteSpace();
        });
    }

    // ===========================
    // GetSchemaByIdAsync
    // ===========================

    [Fact]
    public async Task GetSchemaByIdAsync_ExistingId_ReturnsSchema()
    {
        var knownId = GetKnownSchemaId();

        var result = await Sut.GetSchemaByIdAsync(knownId);

        result.Should().NotBeNull();
        result!.Metadata.Id.Should().Be(knownId);
    }

    [Fact]
    public async Task GetSchemaByIdAsync_NonexistentId_ReturnsNull()
    {
        var result = await Sut.GetSchemaByIdAsync("nonexistent-schema-id-that-does-not-exist");

        result.Should().BeNull();
    }

    // ===========================
    // SearchSchemasAsync
    // ===========================

    [Fact]
    public async Task SearchSchemasAsync_MatchingQuery_ReturnsResults()
    {
        var searchTerm = GetKnownSearchTerm();

        var result = (await Sut.SearchSchemasAsync(searchTerm)).ToList();

        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SearchSchemasAsync_NonMatchingQuery_ReturnsEmpty()
    {
        var result = (await Sut.SearchSchemasAsync("zzz_absolutely_no_match_xyz_12345")).ToList();

        result.Should().BeEmpty();
    }

    // ===========================
    // GetSchemasByCategoryAsync
    // ===========================

    [Fact]
    public async Task GetSchemasByCategoryAsync_ExistingCategory_ReturnsResults()
    {
        var category = GetKnownCategory();

        var result = (await Sut.GetSchemasByCategoryAsync(category)).ToList();

        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetSchemasByCategoryAsync_NonexistentCategory_ReturnsEmpty()
    {
        var result = (await Sut.GetSchemasByCategoryAsync("nonexistent-category-xyz")).ToList();

        result.Should().BeEmpty();
    }

    // ===========================
    // RefreshAsync
    // ===========================

    [Fact]
    public async Task RefreshAsync_DoesNotThrow()
    {
        var act = () => Sut.RefreshAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RefreshAsync_SchemasStillAvailable()
    {
        var sut = Sut;
        await sut.RefreshAsync();

        var result = (await sut.GetAllSchemasAsync()).ToList();

        result.Should().NotBeEmpty();
    }
}
