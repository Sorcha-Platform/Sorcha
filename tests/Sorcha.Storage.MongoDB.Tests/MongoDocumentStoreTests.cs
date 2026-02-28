// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Runtime.Serialization;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using Sorcha.Storage.MongoDB;
using Xunit;

namespace Sorcha.Storage.MongoDB.Tests;

public class MongoDocumentStoreTests
{
    private readonly Mock<IMongoDatabase> _mockDatabase;
    private readonly Mock<IMongoCollection<TestDocument>> _mockCollection;
    private readonly MongoDocumentStore<TestDocument, string> _sut;

    public MongoDocumentStoreTests()
    {
        _mockDatabase = new Mock<IMongoDatabase>();
        _mockCollection = new Mock<IMongoCollection<TestDocument>>();

        _mockDatabase
            .Setup(db => db.GetCollection<TestDocument>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(_mockCollection.Object);

        _sut = new MongoDocumentStore<TestDocument, string>(
            _mockDatabase.Object,
            "test-collection",
            d => d.Id,
            d => d.Id);
    }

    // --- Constructor validation ---

    [Fact]
    public void Constructor_NullDatabase_ThrowsArgumentNullException()
    {
        var act = () => new MongoDocumentStore<TestDocument, string>(
            (IMongoDatabase)null!, "collection", d => d.Id, d => d.Id);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_EmptyCollectionName_ThrowsArgumentException()
    {
        var act = () => new MongoDocumentStore<TestDocument, string>(
            _mockDatabase.Object, "", d => d.Id, d => d.Id);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullIdSelector_ThrowsArgumentNullException()
    {
        var act = () => new MongoDocumentStore<TestDocument, string>(
            _mockDatabase.Object, "collection", null!, d => d.Id);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullIdExpression_ThrowsArgumentNullException()
    {
        var act = () => new MongoDocumentStore<TestDocument, string>(
            _mockDatabase.Object, "collection", d => d.Id, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // --- InsertAsync ---

    [Fact]
    public async Task InsertAsync_NewDocument_InsertsAndReturnsDocument()
    {
        var document = new TestDocument { Id = "doc-1", Name = "Test" };
        _mockCollection
            .Setup(c => c.InsertOneAsync(document, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.InsertAsync(document);

        result.Should().BeSameAs(document);
        _mockCollection.Verify(
            c => c.InsertOneAsync(document, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InsertAsync_DuplicateKey_ThrowsInvalidOperationException()
    {
        var document = new TestDocument { Id = "doc-1", Name = "Test" };
        var writeException = CreateMongoWriteException(ServerErrorCategory.DuplicateKey);

        _mockCollection
            .Setup(c => c.InsertOneAsync(document, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(writeException);

        var act = () => _sut.InsertAsync(document);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    // --- GetAsync ---

    [Fact]
    public async Task GetAsync_DocumentExists_ReturnsDocument()
    {
        var document = new TestDocument { Id = "doc-1", Name = "Found" };
        SetupFindAsync(new[] { document });

        var result = await _sut.GetAsync("doc-1");

        result.Should().NotBeNull();
        result!.Id.Should().Be("doc-1");
        result.Name.Should().Be("Found");
    }

    [Fact]
    public async Task GetAsync_DocumentNotFound_ReturnsNull()
    {
        SetupFindAsync(Array.Empty<TestDocument>());

        var result = await _sut.GetAsync("nonexistent");

        result.Should().BeNull();
    }

    // --- GetManyAsync ---

    [Fact]
    public async Task GetManyAsync_ReturnsMatchingDocuments()
    {
        var documents = new[]
        {
            new TestDocument { Id = "doc-1", Name = "First" },
            new TestDocument { Id = "doc-2", Name = "Second" }
        };
        SetupFindAsync(documents);

        var result = await _sut.GetManyAsync(new[] { "doc-1", "doc-2" });

        result.Should().HaveCount(2);
    }

    // --- ReplaceAsync ---

    [Fact]
    public async Task ReplaceAsync_ReplacesAndReturnsDocument()
    {
        var document = new TestDocument { Id = "doc-1", Name = "Updated" };
        var replaceResult = new Mock<ReplaceOneResult>();
        replaceResult.Setup(r => r.ModifiedCount).Returns(1);

        _mockCollection
            .Setup(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<TestDocument>>(), document,
                It.IsAny<ReplaceOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(replaceResult.Object);

        var result = await _sut.ReplaceAsync("doc-1", document);

        result.Should().BeSameAs(document);
        _mockCollection.Verify(
            c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<TestDocument>>(), document,
                It.Is<ReplaceOptions>(o => o.IsUpsert == false),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- UpsertAsync ---

    [Fact]
    public async Task UpsertAsync_UpsertsAndReturnsDocument()
    {
        var document = new TestDocument { Id = "doc-1", Name = "Upserted" };
        var replaceResult = new Mock<ReplaceOneResult>();
        replaceResult.Setup(r => r.ModifiedCount).Returns(1);

        _mockCollection
            .Setup(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<TestDocument>>(), document,
                It.IsAny<ReplaceOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(replaceResult.Object);

        var result = await _sut.UpsertAsync("doc-1", document);

        result.Should().BeSameAs(document);
        _mockCollection.Verify(
            c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<TestDocument>>(), document,
                It.Is<ReplaceOptions>(o => o.IsUpsert == true),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsync_DocumentExists_ReturnsTrue()
    {
        var deleteResult = new Mock<DeleteResult>();
        deleteResult.Setup(r => r.DeletedCount).Returns(1);
        _mockCollection
            .Setup(c => c.DeleteOneAsync(It.IsAny<FilterDefinition<TestDocument>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleteResult.Object);

        var result = await _sut.DeleteAsync("doc-1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_DocumentNotFound_ReturnsFalse()
    {
        var deleteResult = new Mock<DeleteResult>();
        deleteResult.Setup(r => r.DeletedCount).Returns(0);
        _mockCollection
            .Setup(c => c.DeleteOneAsync(It.IsAny<FilterDefinition<TestDocument>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleteResult.Object);

        var result = await _sut.DeleteAsync("nonexistent");

        result.Should().BeFalse();
    }

    // --- DeleteManyAsync ---

    [Fact]
    public async Task DeleteManyAsync_ReturnsDeletedCount()
    {
        var deleteResult = new Mock<DeleteResult>();
        deleteResult.Setup(r => r.DeletedCount).Returns(3);
        _mockCollection
            .Setup(c => c.DeleteManyAsync(It.IsAny<FilterDefinition<TestDocument>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleteResult.Object);

        var result = await _sut.DeleteManyAsync(d => d.Name == "Test");

        result.Should().Be(3);
    }

    // --- QueryAsync ---

    [Fact]
    public async Task QueryAsync_ReturnsMatchingDocuments()
    {
        var documents = new[]
        {
            new TestDocument { Id = "doc-1", Name = "Match" },
            new TestDocument { Id = "doc-2", Name = "Match" }
        };
        SetupFindAsync(documents);

        var result = await _sut.QueryAsync(d => d.Name == "Match");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task QueryAsync_WithLimitAndSkip_ReturnsSubset()
    {
        var documents = new[] { new TestDocument { Id = "doc-3", Name = "Third" } };
        SetupFindAsync(documents);

        var result = await _sut.QueryAsync(d => true, limit: 1, skip: 2);

        result.Should().HaveCount(1);
    }

    // --- CountAsync ---

    [Fact]
    public async Task CountAsync_WithoutFilter_ReturnsEstimatedCount()
    {
        _mockCollection
            .Setup(c => c.EstimatedDocumentCountAsync(It.IsAny<EstimatedDocumentCountOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        var result = await _sut.CountAsync();

        result.Should().Be(100);
    }

    [Fact]
    public async Task CountAsync_WithFilter_ReturnsFilteredCount()
    {
        _mockCollection
            .Setup(c => c.CountDocumentsAsync(
                It.IsAny<FilterDefinition<TestDocument>>(),
                It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        var result = await _sut.CountAsync(d => d.Name == "Test");

        result.Should().Be(7);
    }

    // --- ExistsAsync ---

    [Fact]
    public async Task ExistsAsync_DocumentExists_ReturnsTrue()
    {
        _mockCollection
            .Setup(c => c.CountDocumentsAsync(
                It.IsAny<FilterDefinition<TestDocument>>(),
                It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _sut.ExistsAsync("doc-1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_DocumentNotFound_ReturnsFalse()
    {
        _mockCollection
            .Setup(c => c.CountDocumentsAsync(
                It.IsAny<FilterDefinition<TestDocument>>(),
                It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await _sut.ExistsAsync("nonexistent");

        result.Should().BeFalse();
    }

    // --- InsertManyAsync ---

    [Fact]
    public async Task InsertManyAsync_NonEmptyList_InsertsAll()
    {
        _mockCollection
            .Setup(c => c.InsertManyAsync(
                It.IsAny<IEnumerable<TestDocument>>(),
                It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.InsertManyAsync(new[]
        {
            new TestDocument { Id = "doc-1", Name = "First" },
            new TestDocument { Id = "doc-2", Name = "Second" }
        });

        _mockCollection.Verify(
            c => c.InsertManyAsync(
                It.IsAny<IEnumerable<TestDocument>>(),
                It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InsertManyAsync_EmptyList_DoesNotInsert()
    {
        await _sut.InsertManyAsync(Array.Empty<TestDocument>());

        _mockCollection.Verify(
            c => c.InsertManyAsync(
                It.IsAny<IEnumerable<TestDocument>>(),
                It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // --- Helpers ---

#pragma warning disable SYSLIB0050
    private static MongoWriteException CreateMongoWriteException(ServerErrorCategory category)
    {
        var writeError = (WriteError)FormatterServices.GetUninitializedObject(typeof(WriteError));
        var categoryField = typeof(WriteError).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(f => f.FieldType == typeof(ServerErrorCategory));
        categoryField.SetValue(writeError, category);

        var exception = (MongoWriteException)FormatterServices.GetUninitializedObject(typeof(MongoWriteException));
        var backingField = typeof(MongoWriteException).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(f => f.FieldType == typeof(WriteError));
        backingField.SetValue(exception, writeError);

        return exception;
    }
#pragma warning restore SYSLIB0050

    private void SetupFindAsync(TestDocument[] documents)
    {
        _mockCollection
            .Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<TestDocument>>(),
                It.IsAny<FindOptions<TestDocument, TestDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var mockCursor = new Mock<IAsyncCursor<TestDocument>>();
                mockCursor
                    .SetupSequence(cur => cur.MoveNextAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(documents.Length > 0)
                    .ReturnsAsync(false);
                mockCursor.Setup(cur => cur.Current).Returns(documents);
                return mockCursor.Object;
            });
    }

    public class TestDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
