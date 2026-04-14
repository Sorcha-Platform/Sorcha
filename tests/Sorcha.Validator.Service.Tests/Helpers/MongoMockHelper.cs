// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using MongoDB.Driver;
using Moq;
using Sorcha.Validator.Service.Models;

namespace Sorcha.Validator.Service.Tests.Helpers;

/// <summary>
/// Builds a minimally-wired <see cref="IMongoClient"/> mock whose
/// <c>GetDatabase</c> and <c>GetCollection&lt;T&gt;</c> calls return mock
/// instances sufficient for <c>ValidatorRegistry</c> construction. The
/// returned collections are non-functional — tests that exercise Mongo
/// behaviour should configure additional setups.
/// </summary>
internal static class MongoMockHelper
{
    public static Mock<IMongoClient> CreateValidatorRegistryClient()
    {
        var databaseMock = new Mock<IMongoDatabase>();
        databaseMock
            .Setup(d => d.GetCollection<ValidatorDocument>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(Mock.Of<IMongoCollection<ValidatorDocument>>());
        databaseMock
            .Setup(d => d.GetCollection<ValidatorAuditEntry>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(Mock.Of<IMongoCollection<ValidatorAuditEntry>>());

        var clientMock = new Mock<IMongoClient>();
        clientMock
            .Setup(c => c.GetDatabase(It.IsAny<string>(), It.IsAny<MongoDatabaseSettings>()))
            .Returns(databaseMock.Object);

        return clientMock;
    }
}
