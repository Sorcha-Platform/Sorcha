// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Moq;
using Sorcha.Storage.Abstractions;
using Sorcha.Storage.MongoDB;
using Xunit;

namespace Sorcha.Storage.MongoDB.Tests;

public class MongoServiceExtensionsTests
{
    // --- AddMongoClient (IConfiguration) ---

    [Fact]
    public void AddMongoClient_WithWarmDocumentsConnectionString_RegistersClient()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Warm:Documents:ConnectionString"] = "mongodb://localhost:27017"
            })
            .Build();

        // Act
        services.AddMongoClient(configuration);

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMongoClient));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddMongoClient_WithColdConnectionString_RegistersClient()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Cold:ConnectionString"] = "mongodb://localhost:27017"
            })
            .Build();

        // Act
        services.AddMongoClient(configuration);

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMongoClient));
        descriptor.Should().NotBeNull();
    }

    [Fact]
    public void AddMongoClient_WithNoConnectionString_DoesNotRegister()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // Act
        services.AddMongoClient(configuration);

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMongoClient));
        descriptor.Should().BeNull();
    }

    // --- AddMongoClient (string) ---

    [Fact]
    public void AddMongoClient_WithExplicitConnectionString_RegistersClient()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMongoClient("mongodb://localhost:27017");

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMongoClient));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    // --- AddMongoDatabase ---

    [Fact]
    public void AddMongoDatabase_RegistersDatabaseAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockClient = new Mock<IMongoClient>();
        var mockDatabase = new Mock<IMongoDatabase>();
        mockClient.Setup(c => c.GetDatabase("testdb", null)).Returns(mockDatabase.Object);
        services.AddSingleton(mockClient.Object);

        // Act
        services.AddMongoDatabase("testdb");

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMongoDatabase));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddMongoDatabase_ResolvesCorrectDatabase()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockClient = new Mock<IMongoClient>();
        var mockDatabase = new Mock<IMongoDatabase>();
        mockClient.Setup(c => c.GetDatabase("mydb", null)).Returns(mockDatabase.Object);
        services.AddSingleton(mockClient.Object);
        services.AddMongoDatabase("mydb");

        // Act
        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IMongoDatabase>();

        // Assert
        resolved.Should().BeSameAs(mockDatabase.Object);
    }

    // --- AddMongoDocumentStore ---

    [Fact]
    public void AddMongoDocumentStore_RegistersDocumentStoreAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockDatabase = new Mock<IMongoDatabase>();
        var mockCollection = new Mock<IMongoCollection<TestDoc>>();
        mockDatabase
            .Setup(db => db.GetCollection<TestDoc>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(mockCollection.Object);
        services.AddSingleton(mockDatabase.Object);

        // Act
        services.AddMongoDocumentStore<TestDoc, string>(
            "test-collection",
            d => d.Id,
            d => d.Id);

        // Assert
        var descriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(IDocumentStore<TestDoc, string>));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddMongoDocumentStore_ResolvesInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockDatabase = new Mock<IMongoDatabase>();
        var mockCollection = new Mock<IMongoCollection<TestDoc>>();
        mockDatabase
            .Setup(db => db.GetCollection<TestDoc>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(mockCollection.Object);
        services.AddSingleton(mockDatabase.Object);

        services.AddMongoDocumentStore<TestDoc, string>(
            "test-collection",
            d => d.Id,
            d => d.Id);

        // Act
        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore<TestDoc, string>>();

        // Assert
        store.Should().NotBeNull();
        store.Should().BeOfType<MongoDocumentStore<TestDoc, string>>();
    }

    // --- AddMongoWormStore ---

    [Fact]
    public void AddMongoWormStore_RegistersWormStoreAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockDatabase = new Mock<IMongoDatabase>();
        var mockCollection = new Mock<IMongoCollection<TestDoc>>();
        mockDatabase
            .Setup(db => db.GetCollection<TestDoc>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(mockCollection.Object);
        services.AddSingleton(mockDatabase.Object);

        // Act
        services.AddMongoWormStore<TestDoc, string>(
            "test-collection",
            d => d.Id,
            d => d.Id);

        // Assert
        var descriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(IWormStore<TestDoc, string>));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddMongoWormStore_ResolvesInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockDatabase = new Mock<IMongoDatabase>();
        var mockCollection = new Mock<IMongoCollection<TestDoc>>();
        mockDatabase
            .Setup(db => db.GetCollection<TestDoc>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(mockCollection.Object);
        services.AddSingleton(mockDatabase.Object);

        services.AddMongoWormStore<TestDoc, string>(
            "test-collection",
            d => d.Id,
            d => d.Id);

        // Act
        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IWormStore<TestDoc, string>>();

        // Assert
        store.Should().NotBeNull();
        store.Should().BeOfType<MongoWormStore<TestDoc, string>>();
    }

    public class TestDoc
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
