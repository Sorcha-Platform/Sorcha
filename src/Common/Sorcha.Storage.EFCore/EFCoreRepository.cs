// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Sorcha.Storage.Abstractions;

namespace Sorcha.Storage.EFCore;

/// <summary>
/// Entity Framework Core implementation of IRepository for warm-tier relational storage.
/// </summary>
/// <remarks>
/// This repository follows the unit-of-work pattern. Methods like <see cref="AddAsync"/>,
/// <see cref="UpdateAsync"/>, and <see cref="DeleteAsync"/> stage changes in the EF Core
/// change tracker. Call <see cref="SaveChangesAsync"/> to persist all staged changes to
/// the database in a single transaction.
/// </remarks>
/// <typeparam name="TEntity">Entity type.</typeparam>
/// <typeparam name="TId">Primary key type.</typeparam>
/// <typeparam name="TContext">DbContext type.</typeparam>
public class EFCoreRepository<TEntity, TId, TContext> : IRepository<TEntity, TId>
    where TEntity : class
    where TId : notnull
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly DbSet<TEntity> _dbSet;
    private readonly Func<TEntity, TId> _idSelector;

    /// <summary>
    /// Initializes a new instance of the EFCoreRepository.
    /// </summary>
    /// <param name="context">Database context.</param>
    /// <param name="idSelector">Function to extract ID from entity.</param>
    public EFCoreRepository(TContext context, Func<TEntity, TId> idSelector)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dbSet = _context.Set<TEntity>();
        _idSelector = idSelector ?? throw new ArgumentNullException(nameof(idSelector));
    }

    /// <inheritdoc/>
    public async Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync([id], cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TEntity>> QueryAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<PagedResult<TEntity>> GetPagedAsync(
        int page,
        int pageSize,
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.AsNoTracking();

        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(
            Items: items,
            TotalCount: totalCount,
            Page: page,
            PageSize: pageSize);
    }

    /// <inheritdoc/>
    public async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        var entry = await _dbSet.AddAsync(entity, cancellationToken);
        return entry.Entity;
    }

    /// <inheritdoc/>
    public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddRangeAsync(entities, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        _dbSet.Update(entity);
        return Task.FromResult(entity);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(TId id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbSet.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            return false;
        }

        _dbSet.Remove(entity);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default)
    {
        var entityType = _context.Model.FindEntityType(typeof(TEntity));
        var primaryKey = entityType?.FindPrimaryKey();
        if (primaryKey is not null && primaryKey.Properties.Count == 1)
        {
            var keyProperty = primaryKey.Properties[0].Name;
            return await _dbSet.AnyAsync(
                e => EF.Property<object>(e, keyProperty).Equals(id),
                cancellationToken);
        }

        // Fallback for composite keys or unresolvable metadata
        var entity = await _dbSet.FindAsync([id], cancellationToken);
        return entity is not null;
    }

    /// <inheritdoc/>
    public async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        if (predicate is null)
        {
            return await _dbSet.CountAsync(cancellationToken);
        }

        return await _dbSet.CountAsync(predicate, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
