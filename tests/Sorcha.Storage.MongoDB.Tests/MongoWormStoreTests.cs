// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq.Expressions;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using Sorcha.Storage.MongoDB;
using Xunit;

namespace Sorcha.Storage.MongoDB.Tests;

public class MongoWormStoreTests
{
    private readonly Mock<IMongoDatabase> _mockDatabase;
    private readonly Mock<IMongoCollection<TestDocument>> _mockCollection;
    private readonly MongoWormStore<TestDocument, string> _sut;

    public MongoWormStoreTests()
    {
        _mockDatabase = new Mock<IMongoDatabase>();
        _mockCollection = new Mock<IMongoCollection<TestDocument>>();

        _mockDatabase
            .Setup(db => db.GetCollection<TestDocument>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(_mockCollection.Object);

        _sut = new MongoWormStore<TestDocument, string>(
            _mockDatabase.Object,
            "test-collection",
            d => d.Id,
            d => d.Id);
    }

    // --- Constructor validation ---

    [Fact]
    public void Constructor_NullDatabase_ThrowsArgumentNullException()
    {
        var act = () => new MongoWormStore<TestDocument, string>(
            (IMongoDatabase)null!, "collection", d => d.Id, d => d.Id);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullCollectionName_ThrowsArgumentException()
    {
        var act = () => new MongoWormStore<TestDocument, string>(
            _mockDatabase.Object, null!, d => d.Id, d => d.Id);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EmptyCollectionName_ThrowsArgumentException()
    {
        var act = () => new MongoWormStore<TestDocument, string>(
            _mockDatabase.Object, "", d => d.Id, d => d.Id);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullIdSelector_ThrowsArgumentNullException()
    {
        var act = () => new MongoWormStore<TestDocument, string>(
            _mockDatabase.Object, "collection", null!, d => d.Id);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullIdExpression_ThrowsArgumentNullException()
    {
        var act = () => new MongoWormStore<TestDocument, string>(
            _mockDatabase.Object, "collection", d => d.Id, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // --- AppendAsync ---

    [Fact]
    public async Task AppendAsync_NewDocument_InsertsAndReturnsDocument()
    {
        // Arrange
        var document = new TestDocument { Id = "doc-1", Name = "Test" };
        SetupFindAsync(Array.Empty<TestDocument>());

        _mockCollection
            .Setup(c => c.InsertOneAsync(document, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.AppendAsync(document);

        // Assert
        result.Should().BeSameAs(document);
        _mockCollection.Verify(
            c => c.InsertOneAsync(document, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AppendAsync_DuplicateId_ThrowsInvalidOperationException()
    {
        // Arrange
        var existing = new TestDocument { Id = "doc-1", Name = "Existing" };
        SetupFindAsync(new[] { existing });

        // Act
        var act = () => _sut.AppendAsync(new TestDocument { Id = "doc-1", Name = "Duplicate" });

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*WORM storage does not allow updates*");
    }

    // --- AppendBatchAsync ---

    [Fact]
    public async Task AppendBatchAsync_NewDocuments_InsertsAll()
    {
        // Arrange
        SetupCountDocuments(0);

        _mockCollection
            .Setup(c => c.InsertManyAsync(
                It.IsAny<IEnumerable<TestDocument>>(),
                It.IsAny<InsertManyOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var documents = new[]
        {
            new TestDocument { Id = "doc-1", Name = "First" },
            new TestDocument { Id = "doc-2", Name = "Second" }
        };

        // Act
        await _sut.AppendBatchAsync(documents);

        // Assert
        _mockCollection.Verify(
            c => c.InsertManyAsync(
                It.IsAny<IEnumerable<TestDocument>>(),
                It.IsAny<InsertManyOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AppendBatchAsync_EmptyBatch_DoesNotInsert()
    {
        // Act
        await _sut.AppendBatchAsync(Array.Empty<TestDocument>());

        // Assert
        _mockCollection.Verify(
            c => c.InsertManyAsync(
                It.IsAny<IEnumerable<TestDocument>>(),
                It.IsAny<InsertManyOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AppendBatchAsync_DuplicateInBatch_ThrowsInvalidOperationException()
    {
        // Arrange — first ExistsAsync returns false, second returns true
        _mockCollection
            .SetupSequence(c => c.CountDocumentsAsync(
                It.IsAny<FilterDefinition<TestDocument>>(),
                It.IsAny<CountOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0)
            .ReturnsAsync(1);

        var documents = new[]
        {
            new TestDocument { Id = "doc-1", Name = "First" },
            new TestDocument { Id = "doc-2", Name = "Second" }
        };

        // Act
        var act = () => _sut.AppendBatchAsync(documents);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*WORM storage does not allow updates*");
    }

    // --- GetAsync ---

    [Fact]
    public async Task GetAsync_DocumentExists_ReturnsDocument()
    {
        // Arrange
        var document = new TestDocument { Id = "doc-1", Name = "Found" };
        SetupFindAsync(new[] { document });

        // Act
        var result = await _sut.GetAsync("doc-1");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("doc-1");
        result.Name.Should().Be("Found");
    }

    [Fact]
    public async Task GetAsync_DocumentNotFound_ReturnsNull()
    {
        // Arrange
        SetupFindAsync(Array.Empty<TestDocument>());

        // Act
        var result = await _sut.GetAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    // --- GetRangeAsync ---

    [Fact]
    public async Task GetRangeAsync_ReturnsMatchingDocuments()
    {
        // Arrange
        var documents = new[]
        {
            new TestDocument { Id = "a", Name = "First" },
            new TestDocument { Id = "b", Name = "Second" }
        };
        SetupFindAsync(documents);

        // Act
        var result = await _sut.GetRangeAsync("a", "c");

        // Assert
        result.Should().HaveCount(2);
    }

    // --- ExistsAsync ---

    [Fact]
    public async Task ExistsAsync_DocumentExists_ReturnsTrue()
    {
        // Arrange
        SetupCountDocuments(1);

        // Act
        var result = await _sut.ExistsAsync("doc-1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_DocumentNotFound_ReturnsFalse()
    {
        // Arrange
        SetupCountDocuments(0);

        // Act
        var result = await _sut.ExistsAsync("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    // --- CountAsync ---

    [Fact]
    public async Task CountAsync_WithoutFilter_ReturnsEstimatedCount()
    {
        // Arrange
        _mockCollection
            .Setup(c => c.EstimatedDocumentCountAsync(It.IsAny<EstimatedDocumentCountOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        // Act
        var result = await _sut.CountAsync();

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public async Task CountAsync_WithFilter_ReturnsFilteredCount()
    {
        // Arrange
        _mockCollection
            .Setup(c => c.CountDocumentsAsync(
                It.IsAny<FilterDefinition<TestDocument>>(),
                It.IsAny<CountOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        // Act
        var result = await _sut.CountAsync(d => d.Name == "Test");

        // Assert
        result.Should().Be(5);
    }

    // --- GetCurrentSequenceAsync ---

    [Fact]
    public async Task GetCurrentSequenceAsync_EmptyStore_ReturnsZero()
    {
        // Arrange
        SetupFindAsync(Array.Empty<TestDocument>());

        // Act
        var result = await _sut.GetCurrentSequenceAsync();

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentSequenceAsync_StringId_ReturnsZero()
    {
        // Arrange — string IDs cannot convert to ulong
        SetupFindAsync(new[] { new TestDocument { Id = "doc-1", Name = "Test" } });

        // Act
        var result = await _sut.GetCurrentSequenceAsync();

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentSequenceAsync_UlongId_ReturnsId()
    {
        // Arrange
        var mockDb = new Mock<IMongoDatabase>();
        var mockCol = new Mock<IMongoCollection<NumericDocument>>();
        mockDb
            .Setup(db => db.GetCollection<NumericDocument>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(mockCol.Object);

        var store = new MongoWormStore<NumericDocument, ulong>(
            mockDb.Object, "test-collection", d => d.Height, d => d.Height);

        SetupFindAsyncFor(mockCol, new[] { new NumericDocument { Height = 99 } });

        // Act
        var result = await store.GetCurrentSequenceAsync();

        // Assert
        result.Should().Be(99);
    }

    [Fact]
    public async Task GetCurrentSequenceAsync_LongId_ReturnsConvertedId()
    {
        // Arrange
        var mockDb = new Mock<IMongoDatabase>();
        var mockCol = new Mock<IMongoCollection<LongIdDocument>>();
        mockDb
            .Setup(db => db.GetCollection<LongIdDocument>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(mockCol.Object);

        var store = new MongoWormStore<LongIdDocument, long>(
            mockDb.Object, "test-collection", d => d.Sequence, d => d.Sequence);

        SetupFindAsyncFor(mockCol, new[] { new LongIdDocument { Sequence = 50L } });

        // Act
        var result = await store.GetCurrentSequenceAsync();

        // Assert
        result.Should().Be(50);
    }

    [Fact]
    public async Task GetCurrentSequenceAsync_IntId_ReturnsConvertedId()
    {
        // Arrange
        var mockDb = new Mock<IMongoDatabase>();
        var mockCol = new Mock<IMongoCollection<IntIdDocument>>();
        mockDb
            .Setup(db => db.GetCollection<IntIdDocument>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(mockCol.Object);

        var store = new MongoWormStore<IntIdDocument, int>(
            mockDb.Object, "test-collection", d => d.Index, d => d.Index);

        SetupFindAsyncFor(mockCol, new[] { new IntIdDocument { Index = 25 } });

        // Act
        var result = await store.GetCurrentSequenceAsync();

        // Assert
        result.Should().Be(25);
    }

    // --- Helpers ---

    private void SetupFindAsync(TestDocument[] documents)
    {
        SetupFindAsyncFor(_mockCollection, documents);
    }

    /// <summary>
    /// Mocks FindAsync on the collection to return a cursor over the given documents.
    /// The MongoDB driver extension methods (Find, FirstOrDefaultAsync, ToListAsync)
    /// ultimately call FindAsync on the collection interface.
    /// </summary>
    private static void SetupFindAsyncFor<T>(Mock<IMongoCollection<T>> mockCollection, T[] documents)
    {
        mockCollection
            .Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<T>>(),
                It.IsAny<FindOptions<T, T>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var mockCursor = new Mock<IAsyncCursor<T>>();
                mockCursor
                    .SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(documents.Length > 0)
                    .ReturnsAsync(false);
                mockCursor
                    .Setup(c => c.Current)
                    .Returns(documents);
                return mockCursor.Object;
            });
    }

    private void SetupCountDocuments(long count)
    {
        _mockCollection
            .Setup(c => c.CountDocumentsAsync(
                It.IsAny<FilterDefinition<TestDocument>>(),
                It.IsAny<CountOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(count);
    }

    // --- Test models ---

    public class TestDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class NumericDocument
    {
        public ulong Height { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class LongIdDocument
    {
        public long Sequence { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class IntIdDocument
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
