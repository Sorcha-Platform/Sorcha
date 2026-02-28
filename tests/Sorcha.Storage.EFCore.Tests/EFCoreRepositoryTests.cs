// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sorcha.Storage.EFCore.Tests.Fixtures;
using Xunit;

namespace Sorcha.Storage.EFCore.Tests;

public class EFCoreRepositoryTests
{
    private static TestDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new TestDbContext(options);
    }

    private static EFCoreRepository<TestEntity, Guid, TestDbContext> CreateRepository(TestDbContext context)
    {
        return new EFCoreRepository<TestEntity, Guid, TestDbContext>(context, e => e.Id);
    }

    private static TestEntity CreateTestEntity(string name = "Test", bool isActive = true)
    {
        return new TestEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };
    }

    // -- Constructor --------------------------------------------------------

    [Fact]
    public void Constructor_NullContext_ThrowsArgumentNullException()
    {
        var act = () => new EFCoreRepository<TestEntity, Guid, TestDbContext>(null!, e => e.Id);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("context");
    }

    [Fact]
    public void Constructor_NullIdSelector_ThrowsArgumentNullException()
    {
        using var context = CreateContext(nameof(Constructor_NullIdSelector_ThrowsArgumentNullException));

        var act = () => new EFCoreRepository<TestEntity, Guid, TestDbContext>(context, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("idSelector");
    }

    // -- AddAsync -----------------------------------------------------------

    [Fact]
    public async Task AddAsync_ValidEntity_ReturnsAddedEntity()
    {
        using var context = CreateContext(nameof(AddAsync_ValidEntity_ReturnsAddedEntity));
        var repository = CreateRepository(context);
        var entity = CreateTestEntity("AddTest");

        var result = await repository.AddAsync(entity);

        result.Should().BeSameAs(entity);
        result.Name.Should().Be("AddTest");
    }

    [Fact]
    public async Task AddAsync_ValidEntity_PersistsAfterSaveChanges()
    {
        var dbName = nameof(AddAsync_ValidEntity_PersistsAfterSaveChanges);
        var entityId = Guid.NewGuid();

        using (var context = CreateContext(dbName))
        {
            var repository = CreateRepository(context);
            var entity = new TestEntity
            {
                Id = entityId,
                Name = "Persisted",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await repository.AddAsync(entity);
            await repository.SaveChangesAsync();
        }

        // Verify with a fresh context to ensure actual persistence
        using (var context = CreateContext(dbName))
        {
            var persisted = await context.TestEntities.FindAsync(entityId);
            persisted.Should().NotBeNull();
            persisted!.Name.Should().Be("Persisted");
        }
    }

    // -- AddRangeAsync ------------------------------------------------------

    [Fact]
    public async Task AddRangeAsync_MultipleEntities_AllPersisted()
    {
        var dbName = nameof(AddRangeAsync_MultipleEntities_AllPersisted);
        var entities = new[]
        {
            CreateTestEntity("First"),
            CreateTestEntity("Second"),
            CreateTestEntity("Third")
        };

        using (var context = CreateContext(dbName))
        {
            var repository = CreateRepository(context);
            await repository.AddRangeAsync(entities);
            await repository.SaveChangesAsync();
        }

        using (var context = CreateContext(dbName))
        {
            var count = await context.TestEntities.CountAsync();
            count.Should().Be(3);
        }
    }

    // -- GetByIdAsync -------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ExistingEntity_ReturnsEntity()
    {
        var dbName = nameof(GetByIdAsync_ExistingEntity_ReturnsEntity);
        var entity = CreateTestEntity("FindMe");

        using var context = CreateContext(dbName);
        var repository = CreateRepository(context);

        await repository.AddAsync(entity);
        await repository.SaveChangesAsync();

        var result = await repository.GetByIdAsync(entity.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("FindMe");
    }

    [Fact]
    public async Task GetByIdAsync_NonexistentId_ReturnsNull()
    {
        using var context = CreateContext(nameof(GetByIdAsync_NonexistentId_ReturnsNull));
        var repository = CreateRepository(context);

        var result = await repository.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    // -- GetAllAsync --------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_EmptyDatabase_ReturnsEmptyCollection()
    {
        using var context = CreateContext(nameof(GetAllAsync_EmptyDatabase_ReturnsEmptyCollection));
        var repository = CreateRepository(context);

        var result = await repository.GetAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_MultipleEntities_ReturnsAll()
    {
        using var context = CreateContext(nameof(GetAllAsync_MultipleEntities_ReturnsAll));
        var repository = CreateRepository(context);

        await repository.AddRangeAsync(new[]
        {
            CreateTestEntity("One"),
            CreateTestEntity("Two"),
            CreateTestEntity("Three")
        });
        await repository.SaveChangesAsync();

        var result = await repository.GetAllAsync();

        result.Should().HaveCount(3);
    }

    // -- QueryAsync ---------------------------------------------------------

    [Fact]
    public async Task QueryAsync_MatchingPredicate_ReturnsFilteredEntities()
    {
        using var context = CreateContext(nameof(QueryAsync_MatchingPredicate_ReturnsFilteredEntities));
        var repository = CreateRepository(context);

        await repository.AddRangeAsync(new[]
        {
            CreateTestEntity("Active1", isActive: true),
            CreateTestEntity("Active2", isActive: true),
            CreateTestEntity("Inactive", isActive: false)
        });
        await repository.SaveChangesAsync();

        var result = await repository.QueryAsync(e => e.IsActive);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(e => e.IsActive);
    }

    [Fact]
    public async Task QueryAsync_NoMatches_ReturnsEmptyCollection()
    {
        using var context = CreateContext(nameof(QueryAsync_NoMatches_ReturnsEmptyCollection));
        var repository = CreateRepository(context);

        await repository.AddAsync(CreateTestEntity("Active", isActive: true));
        await repository.SaveChangesAsync();

        var result = await repository.QueryAsync(e => !e.IsActive);

        result.Should().BeEmpty();
    }

    // -- GetPagedAsync ------------------------------------------------------

    [Fact]
    public async Task GetPagedAsync_FirstPage_ReturnsCorrectPageData()
    {
        using var context = CreateContext(nameof(GetPagedAsync_FirstPage_ReturnsCorrectPageData));
        var repository = CreateRepository(context);

        var entities = Enumerable.Range(1, 10)
            .Select(i => CreateTestEntity($"Entity{i}"))
            .ToList();
        await repository.AddRangeAsync(entities);
        await repository.SaveChangesAsync();

        var result = await repository.GetPagedAsync(page: 1, pageSize: 3);

        result.Items.Should().HaveCount(3);
        result.TotalCount.Should().Be(10);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(3);
        result.TotalPages.Should().Be(4); // ceil(10/3) = 4
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task GetPagedAsync_LastPartialPage_ReturnsRemainingItems()
    {
        using var context = CreateContext(nameof(GetPagedAsync_LastPartialPage_ReturnsRemainingItems));
        var repository = CreateRepository(context);

        var entities = Enumerable.Range(1, 10)
            .Select(i => CreateTestEntity($"Entity{i}"))
            .ToList();
        await repository.AddRangeAsync(entities);
        await repository.SaveChangesAsync();

        var result = await repository.GetPagedAsync(page: 4, pageSize: 3);

        result.Items.Should().HaveCount(1); // 10 - (3 * 3) = 1 remaining
        result.TotalCount.Should().Be(10);
        result.Page.Should().Be(4);
        result.TotalPages.Should().Be(4);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task GetPagedAsync_EmptyDatabase_ReturnsEmptyPage()
    {
        using var context = CreateContext(nameof(GetPagedAsync_EmptyDatabase_ReturnsEmptyPage));
        var repository = CreateRepository(context);

        var result = await repository.GetPagedAsync(page: 1, pageSize: 10);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.TotalPages.Should().Be(0);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task GetPagedAsync_WithPredicate_FiltersAndPages()
    {
        using var context = CreateContext(nameof(GetPagedAsync_WithPredicate_FiltersAndPages));
        var repository = CreateRepository(context);

        var entities = new List<TestEntity>();
        for (int i = 0; i < 8; i++)
            entities.Add(CreateTestEntity($"Active{i}", isActive: true));
        for (int i = 0; i < 4; i++)
            entities.Add(CreateTestEntity($"Inactive{i}", isActive: false));

        await repository.AddRangeAsync(entities);
        await repository.SaveChangesAsync();

        var result = await repository.GetPagedAsync(
            page: 1,
            pageSize: 5,
            predicate: e => e.IsActive);

        result.Items.Should().HaveCount(5);
        result.TotalCount.Should().Be(8);
        result.TotalPages.Should().Be(2); // ceil(8/5) = 2
        result.Items.Should().OnlyContain(e => e.IsActive);
    }

    // -- UpdateAsync ---------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_ExistingEntity_ModifiesAndPersists()
    {
        var dbName = nameof(UpdateAsync_ExistingEntity_ModifiesAndPersists);
        var entityId = Guid.NewGuid();

        using (var context = CreateContext(dbName))
        {
            var repository = CreateRepository(context);
            var entity = new TestEntity
            {
                Id = entityId,
                Name = "Original",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await repository.AddAsync(entity);
            await repository.SaveChangesAsync();
        }

        using (var context = CreateContext(dbName))
        {
            var repository = CreateRepository(context);
            var entity = await repository.GetByIdAsync(entityId);
            entity.Should().NotBeNull();

            entity!.Name = "Updated";
            entity.IsActive = false;

            var result = await repository.UpdateAsync(entity);
            await repository.SaveChangesAsync();

            result.Name.Should().Be("Updated");
        }

        using (var context = CreateContext(dbName))
        {
            var persisted = await context.TestEntities.FindAsync(entityId);
            persisted.Should().NotBeNull();
            persisted!.Name.Should().Be("Updated");
            persisted.IsActive.Should().BeFalse();
        }
    }

    [Fact]
    public async Task UpdateAsync_ReturnsUpdatedEntity()
    {
        using var context = CreateContext(nameof(UpdateAsync_ReturnsUpdatedEntity));
        var repository = CreateRepository(context);
        var entity = CreateTestEntity("Original");

        await repository.AddAsync(entity);
        await repository.SaveChangesAsync();

        entity.Name = "Changed";
        var result = await repository.UpdateAsync(entity);

        result.Should().BeSameAs(entity);
        result.Name.Should().Be("Changed");
    }

    // -- DeleteAsync ---------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_ExistingEntity_ReturnsTrueAndRemoves()
    {
        var dbName = nameof(DeleteAsync_ExistingEntity_ReturnsTrueAndRemoves);
        var entityId = Guid.NewGuid();

        using var context = CreateContext(dbName);
        var repository = CreateRepository(context);
        var entity = new TestEntity
        {
            Id = entityId,
            Name = "ToDelete",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        await repository.AddAsync(entity);
        await repository.SaveChangesAsync();

        var result = await repository.DeleteAsync(entityId);
        await repository.SaveChangesAsync();

        result.Should().BeTrue();

        var remaining = await repository.GetByIdAsync(entityId);
        remaining.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonexistentId_ReturnsFalse()
    {
        using var context = CreateContext(nameof(DeleteAsync_NonexistentId_ReturnsFalse));
        var repository = CreateRepository(context);

        var result = await repository.DeleteAsync(Guid.NewGuid());

        result.Should().BeFalse();
    }

    // -- ExistsAsync ---------------------------------------------------------

    [Fact]
    public async Task ExistsAsync_ExistingEntity_ReturnsTrue()
    {
        using var context = CreateContext(nameof(ExistsAsync_ExistingEntity_ReturnsTrue));
        var repository = CreateRepository(context);
        var entity = CreateTestEntity("Exists");

        await repository.AddAsync(entity);
        await repository.SaveChangesAsync();

        var result = await repository.ExistsAsync(entity.Id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonexistentEntity_ReturnsFalse()
    {
        using var context = CreateContext(nameof(ExistsAsync_NonexistentEntity_ReturnsFalse));
        var repository = CreateRepository(context);

        var result = await repository.ExistsAsync(Guid.NewGuid());

        result.Should().BeFalse();
    }

    // -- CountAsync ----------------------------------------------------------

    [Fact]
    public async Task CountAsync_NoPredicate_ReturnsTotalCount()
    {
        using var context = CreateContext(nameof(CountAsync_NoPredicate_ReturnsTotalCount));
        var repository = CreateRepository(context);

        await repository.AddRangeAsync(new[]
        {
            CreateTestEntity("One"),
            CreateTestEntity("Two"),
            CreateTestEntity("Three")
        });
        await repository.SaveChangesAsync();

        var count = await repository.CountAsync();

        count.Should().Be(3);
    }

    [Fact]
    public async Task CountAsync_WithPredicate_ReturnsFilteredCount()
    {
        using var context = CreateContext(nameof(CountAsync_WithPredicate_ReturnsFilteredCount));
        var repository = CreateRepository(context);

        await repository.AddRangeAsync(new[]
        {
            CreateTestEntity("Active1", isActive: true),
            CreateTestEntity("Active2", isActive: true),
            CreateTestEntity("Inactive", isActive: false)
        });
        await repository.SaveChangesAsync();

        var count = await repository.CountAsync(e => e.IsActive);

        count.Should().Be(2);
    }

    [Fact]
    public async Task CountAsync_EmptyDatabase_ReturnsZero()
    {
        using var context = CreateContext(nameof(CountAsync_EmptyDatabase_ReturnsZero));
        var repository = CreateRepository(context);

        var count = await repository.CountAsync();

        count.Should().Be(0);
    }

    // -- SaveChangesAsync ----------------------------------------------------

    [Fact]
    public async Task SaveChangesAsync_WithPendingAdd_ReturnsAffectedCount()
    {
        using var context = CreateContext(nameof(SaveChangesAsync_WithPendingAdd_ReturnsAffectedCount));
        var repository = CreateRepository(context);

        await repository.AddAsync(CreateTestEntity("One"));
        await repository.AddAsync(CreateTestEntity("Two"));

        var affected = await repository.SaveChangesAsync();

        affected.Should().Be(2);
    }

    [Fact]
    public async Task SaveChangesAsync_NoPendingChanges_ReturnsZero()
    {
        using var context = CreateContext(nameof(SaveChangesAsync_NoPendingChanges_ReturnsZero));
        var repository = CreateRepository(context);

        var affected = await repository.SaveChangesAsync();

        affected.Should().Be(0);
    }

    [Fact]
    public async Task SaveChangesAsync_ChangesNotSaved_AreNotPersisted()
    {
        var dbName = nameof(SaveChangesAsync_ChangesNotSaved_AreNotPersisted);

        using (var context = CreateContext(dbName))
        {
            var repository = CreateRepository(context);
            await repository.AddAsync(CreateTestEntity("Unsaved"));
            // Intentionally NOT calling SaveChangesAsync
        }

        using (var context = CreateContext(dbName))
        {
            var count = await context.TestEntities.CountAsync();
            count.Should().Be(0);
        }
    }
}
