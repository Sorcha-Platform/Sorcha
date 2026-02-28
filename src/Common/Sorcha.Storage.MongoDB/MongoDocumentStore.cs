// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq.Expressions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Sorcha.Storage.Abstractions;

namespace Sorcha.Storage.MongoDB;

/// <summary>
/// MongoDB implementation of IDocumentStore for warm-tier document storage.
/// </summary>
/// <typeparam name="TDocument">Document type.</typeparam>
/// <typeparam name="TId">Document identifier type.</typeparam>
public class MongoDocumentStore<TDocument, TId> : IDocumentStore<TDocument, TId>
    where TDocument : class
    where TId : notnull
{
    private readonly IMongoCollection<TDocument> _collection;
    private readonly Func<TDocument, TId> _idSelector;
    private readonly Expression<Func<TDocument, TId>> _idExpression;

    /// <summary>
    /// Initializes a new instance of the MongoDocumentStore.
    /// </summary>
    /// <param name="database">MongoDB database.</param>
    /// <param name="collectionName">Collection name.</param>
    /// <param name="idSelector">Function to extract ID from document.</param>
    /// <param name="idExpression">Expression to extract ID (for MongoDB filters).</param>
    public MongoDocumentStore(
        IMongoDatabase database,
        string collectionName,
        Func<TDocument, TId> idSelector,
        Expression<Func<TDocument, TId>> idExpression)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrEmpty(collectionName);

        _collection = database.GetCollection<TDocument>(collectionName);
        _idSelector = idSelector ?? throw new ArgumentNullException(nameof(idSelector));
        _idExpression = idExpression ?? throw new ArgumentNullException(nameof(idExpression));
    }

    /// <summary>
    /// Initializes a new instance of the MongoDocumentStore with configuration.
    /// </summary>
    /// <param name="options">Document configuration options.</param>
    /// <param name="collectionName">Collection name.</param>
    /// <param name="idSelector">Function to extract ID from document.</param>
    /// <param name="idExpression">Expression to extract ID.</param>
    public MongoDocumentStore(
        IOptions<DocumentConfiguration> options,
        string collectionName,
        Func<TDocument, TId> idSelector,
        Expression<Func<TDocument, TId>> idExpression)
    {
        ArgumentNullException.ThrowIfNull(options?.Value);
        var config = options.Value;

        var client = new MongoClient(config.ConnectionString);
        var database = client.GetDatabase(config.DatabaseName);
        _collection = database.GetCollection<TDocument>(collectionName);
        _idSelector = idSelector ?? throw new ArgumentNullException(nameof(idSelector));
        _idExpression = idExpression ?? throw new ArgumentNullException(nameof(idExpression));
    }

    /// <inheritdoc/>
    public async Task<TDocument?> GetAsync(TId id, CancellationToken cancellationToken = default)
    {
        var filter = CreateIdFilter(id);
        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TDocument>> GetManyAsync(
        IEnumerable<TId> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        var filter = Builders<TDocument>.Filter.In(_idExpression, idList);
        return await _collection.Find(filter).ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TDocument>> QueryAsync(
        Expression<Func<TDocument, bool>> filter,
        int? limit = null,
        int? skip = null,
        CancellationToken cancellationToken = default)
    {
        var query = _collection.Find(filter);

        if (skip.HasValue)
        {
            query = query.Skip(skip.Value);
        }

        if (limit.HasValue)
        {
            query = query.Limit(limit.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TDocument> InsertAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        try
        {
            await _collection.InsertOneAsync(document, new InsertOneOptions(), cancellationToken);
            return document;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var id = _idSelector(document);
            throw new InvalidOperationException($"Document with ID '{id}' already exists.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task InsertManyAsync(IEnumerable<TDocument> documents, CancellationToken cancellationToken = default)
    {
        var docList = documents.ToList();
        if (docList.Count > 0)
        {
            await _collection.InsertManyAsync(docList, new InsertManyOptions(), cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<TDocument> ReplaceAsync(TId id, TDocument document, CancellationToken cancellationToken = default)
    {
        var filter = CreateIdFilter(id);
        await _collection.ReplaceOneAsync(filter, document, new ReplaceOptions { IsUpsert = false }, cancellationToken);
        return document;
    }

    /// <inheritdoc/>
    public async Task<TDocument> UpsertAsync(TId id, TDocument document, CancellationToken cancellationToken = default)
    {
        var filter = CreateIdFilter(id);
        await _collection.ReplaceOneAsync(filter, document, new ReplaceOptions { IsUpsert = true }, cancellationToken);
        return document;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(TId id, CancellationToken cancellationToken = default)
    {
        var filter = CreateIdFilter(id);
        var result = await _collection.DeleteOneAsync(filter, cancellationToken);
        return result.DeletedCount > 0;
    }

    /// <inheritdoc/>
    public async Task<long> DeleteManyAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        var result = await _collection.DeleteManyAsync(filter, cancellationToken);
        return result.DeletedCount;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default)
    {
        var filter = CreateIdFilter(id);
        var count = await _collection.CountDocumentsAsync(filter, new CountOptions { Limit = 1 }, cancellationToken);
        return count > 0;
    }

    /// <inheritdoc/>
    public async Task<long> CountAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (filter is null)
        {
            return await _collection.EstimatedDocumentCountAsync(cancellationToken: cancellationToken);
        }

        return await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    private FilterDefinition<TDocument> CreateIdFilter(TId id)
    {
        return Builders<TDocument>.Filter.Eq(_idExpression, id);
    }
}
