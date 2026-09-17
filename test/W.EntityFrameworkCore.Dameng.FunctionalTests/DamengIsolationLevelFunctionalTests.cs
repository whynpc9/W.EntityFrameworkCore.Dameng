using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengIsolationLevelFunctionalTests
{
    [DamengFact]
    public Task ReadCommittedIsolationLevelBeginsCommitsAndRollsBack()
        => DamengTestStore.WithEntityTableAsync(
            async store =>
            {
                await using (var context = CreateContext(store))
                {
                    await using var transaction = await context.Database.BeginTransactionAsync(
                        IsolationLevel.ReadCommitted);

                    context.Entities.Add(
                        new IsolationEntity
                        {
                            Name = "隔离级别-回滚",
                            Version = 1
                        });
                    await context.SaveChangesAsync();
                    await transaction.RollbackAsync();
                }

                await using (var context = CreateContext(store))
                {
                    Assert.Empty(await context.Entities.ToListAsync());
                }

                await using (var context = CreateContext(store))
                {
                    await using var transaction = await context.Database.BeginTransactionAsync(
                        IsolationLevel.ReadCommitted);

                    context.Entities.Add(
                        new IsolationEntity
                        {
                            Name = "隔离级别-提交",
                            Version = 1
                        });
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }

                await using (var context = CreateContext(store))
                {
                    var name = Assert.Single(
                        await context.Entities
                            .Select(entity => entity.Name)
                            .ToListAsync());
                    Assert.Equal("隔离级别-提交", name);
                }
            });

    [DamengTheory]
    [InlineData(IsolationLevel.RepeatableRead)]
    [InlineData(IsolationLevel.Snapshot)]
    public Task BeginTransactionRejectsUnsupportedIsolationLevels(IsolationLevel isolationLevel)
        => DamengTestStore.WithEntityTableAsync(
            async store =>
            {
                await using var context = CreateContext(store);

                var exception = await Assert.ThrowsAnyAsync<Exception>(
                    () => context.Database.BeginTransactionAsync(isolationLevel));

                Assert.False(
                    exception is InvalidOperationException queryException
                    && queryException.Message.Contains(
                        "could not be translated",
                        StringComparison.OrdinalIgnoreCase));
            });

    [DamengTheory]
    [InlineData(IsolationLevel.ReadUncommitted)]
    [InlineData(IsolationLevel.Serializable)]
    public Task SaveChangesFailsAfterBeginningReadUncommittedOrSerializable(IsolationLevel isolationLevel)
        => DamengTestStore.WithEntityTableAsync(
            async store =>
            {
                long id;
                await using (var seed = CreateContext(store))
                {
                    var entity = new IsolationEntity
                    {
                        Name = "隔离级别-种子",
                        Version = 1
                    };
                    seed.Entities.Add(entity);
                    await seed.SaveChangesAsync();
                    id = entity.Id;
                }

                await using (var context = CreateContext(store))
                {
                    await using var transaction = await context.Database.BeginTransactionAsync(isolationLevel);
                    var loaded = await context.Entities.SingleAsync(item => item.Id == id);
                    Assert.Equal("隔离级别-种子", loaded.Name);

                    loaded.Name = $"隔离级别更新-{isolationLevel}";
                    loaded.Version = 2;
                    var updateException = await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());
                    AssertCommandTextHasNoValue(updateException);
                    context.ChangeTracker.Clear();
                }

                await using (var context = CreateContext(store))
                {
                    await using var transaction = await context.Database.BeginTransactionAsync(isolationLevel);
                    context.Entities.Add(
                        new IsolationEntity
                        {
                            Name = $"隔离级别插入-{isolationLevel}",
                            Version = 1
                        });
                    var insertException = await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());
                    AssertCommandTextHasNoValue(insertException);
                }
            });

    [DamengFact]
    public Task SerializableTransactionCanQueryExistingRows()
        => DamengTestStore.WithEntityTableAsync(
            async store =>
            {
                long id;
                await using (var context = CreateContext(store))
                {
                    var entity = new IsolationEntity
                    {
                        Name = "可串行化-查询",
                        Version = 1
                    };
                    context.Entities.Add(entity);
                    await context.SaveChangesAsync();
                    id = entity.Id;
                }

                await using (var context = CreateContext(store))
                {
                    await using var transaction = await context.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable);

                    var name = await context.Entities
                        .Where(item => item.Id == id)
                        .Select(item => item.Name)
                        .SingleAsync();
                    Assert.Equal("可串行化-查询", name);
                    await transaction.CommitAsync();
                }
            });

    [DamengFact]
    public Task SerializableExecuteUpdatePersistsChanges()
        => DamengTestStore.WithEntityTableAsync(
            async store =>
            {
                long id;
                await using (var context = CreateContext(store))
                {
                    var entity = new IsolationEntity
                    {
                        Name = "可串行化-批量",
                        Version = 1
                    };
                    context.Entities.Add(entity);
                    await context.SaveChangesAsync();
                    id = entity.Id;
                }

                await using (var context = CreateContext(store))
                {
                    await using var transaction = await context.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable);

                    var updated = await context.Entities
                        .Where(item => item.Id == id)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(item => item.Name, "可串行化-已更新"));
                    Assert.Equal(1, updated);
                    await transaction.CommitAsync();
                }

                await using (var context = CreateContext(store))
                {
                    Assert.Equal(
                        "可串行化-已更新",
                        await context.Entities
                            .Where(item => item.Id == id)
                            .Select(item => item.Name)
                            .SingleAsync());
                }
            });

    private static void AssertCommandTextHasNoValue(DbUpdateException exception)
    {
        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("CommandText has no value", inner.Message, StringComparison.Ordinal);
    }

    private static IsolationContext CreateContext(DamengTestStore store)
    {
        var options = new DbContextOptionsBuilder<IsolationContext>()
            .UseDameng(store.ConnectionString)
            .ReplaceService<IModelCacheKeyFactory, IsolationModelCacheKeyFactory>()
            .EnableDetailedErrors()
            .Options;

        return new IsolationContext(options, store.TableName);
    }

    private sealed class IsolationContext(
        DbContextOptions<IsolationContext> options,
        string tableName)
        : DbContext(options)
    {
        public string TableName { get; } = tableName;

        public DbSet<IsolationEntity> Entities
            => Set<IsolationEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<IsolationEntity>(
                entity =>
                {
                    entity.ToTable(TableName);
                    entity.HasKey(item => item.Id);
                    entity.Property(item => item.Id)
                        .HasColumnName("ID")
                        .ValueGeneratedOnAdd();
                    entity.Property(item => item.Name)
                        .HasColumnName("NAME")
                        .HasMaxLength(200)
                        .IsRequired();
                    entity.Property(item => item.Note)
                        .HasColumnName("NOTE")
                        .HasMaxLength(200);
                    entity.Property(item => item.Version)
                        .HasColumnName("VERSION")
                        .IsConcurrencyToken();
                });
    }

    private sealed class IsolationModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is IsolationContext isolationContext
                ? (context.GetType(), isolationContext.TableName, designTime)
                : (context.GetType(), designTime);
    }

    private sealed class IsolationEntity
    {
        public long Id { get; set; }

        public required string Name { get; set; }

        public string? Note { get; set; }

        public int Version { get; set; }
    }
}
