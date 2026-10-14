using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengProviderFunctionalTests
{
    [DamengFact]
    public async Task AdoAndEfConnectionsOpenOnNet10()
    {
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        await using (var connection = new DmConnection(connectionString))
        {
            await connection.OpenAsync();

            Assert.Equal(ConnectionState.Open, connection.State);
            Assert.False(string.IsNullOrWhiteSpace(connection.ServerVersion));
        }

        var options = new DbContextOptionsBuilder<ConnectionContext>()
            .UseDamengUnderTest(connectionString)
            .Options;

        await using var context = new ConnectionContext(options);
        await context.Database.OpenConnectionAsync();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT 1";

        Assert.Equal(1, DamengTestStore.AsInt32(await command.ExecuteScalarAsync()));
    }

    [DamengFact]
    public Task UnicodeCrudAndIdentityGeneratedKeyRoundtrip()
        => DamengTestStore.WithEntityTableAsync(
            async store =>
            {
                long id;

                await using (var context = CreateContext(store))
                {
                    var entity = new FunctionalEntity
                    {
                        Name = "达梦数据库-你好世界",
                        Note = "EF Core 10 中文往返",
                        Version = 1
                    };

                    context.Entities.Add(entity);
                    await context.SaveChangesAsync();

                    Assert.True(entity.Id > 0);
                    id = entity.Id;
                }

                await using (var context = CreateContext(store))
                {
                    var entity = await context.Entities.SingleAsync(item => item.Id == id);

                    Assert.Equal("达梦数据库-你好世界", entity.Name);
                    Assert.Equal("EF Core 10 中文往返", entity.Note);
                    Assert.Equal(1, entity.Version);
                }
            });

    [DamengFact]
    public Task OrderedSkipTakeQueryExecutes()
        => DamengTestStore.WithEntityTableAsync(
            async store =>
            {
                await using (var context = CreateContext(store))
                {
                    context.Entities.AddRange(
                        new FunctionalEntity { Name = "第一个", Version = 1 },
                        new FunctionalEntity { Name = "第二个", Version = 1 },
                        new FunctionalEntity { Name = "第三个", Version = 1 },
                        new FunctionalEntity { Name = "第四个", Version = 1 });

                    await context.SaveChangesAsync();
                }

                await using (var context = CreateContext(store))
                {
                    var names = await context.Entities
                        .OrderBy(item => item.Id)
                        .Skip(1)
                        .Take(2)
                        .Select(item => item.Name)
                        .ToListAsync();

                    Assert.Equal(["第二个", "第三个"], names);
                }
            });

    [DamengFact]
    public Task UpdateAndDeleteRoundtrip()
        => DamengTestStore.WithEntityTableAsync(
            async store =>
            {
                long id;

                await using (var context = CreateContext(store))
                {
                    var entity = new FunctionalEntity { Name = "更新前", Version = 1 };
                    context.Entities.Add(entity);
                    await context.SaveChangesAsync();
                    id = entity.Id;
                }

                await using (var context = CreateContext(store))
                {
                    var entity = await context.Entities.SingleAsync(item => item.Id == id);
                    entity.Name = "更新后";
                    await context.SaveChangesAsync();
                }

                await using (var context = CreateContext(store))
                {
                    Assert.Equal(
                        "更新后",
                        await context.Entities
                            .Where(item => item.Id == id)
                            .Select(item => item.Name)
                            .SingleAsync());

                    var entity = await context.Entities.SingleAsync(item => item.Id == id);
                    context.Entities.Remove(entity);
                    await context.SaveChangesAsync();
                }

                await using (var context = CreateContext(store))
                {
                    Assert.False(await context.Entities.AnyAsync(item => item.Id == id));
                }
            });

    [DamengFact]
    public Task StaleConcurrencyTokenThrows()
        => DamengTestStore.WithEntityTableAsync(
            async store =>
            {
                await using (var seedContext = CreateContext(store))
                {
                    seedContext.Entities.Add(
                        new FunctionalEntity { Name = "原始值", Version = 1 });
                    await seedContext.SaveChangesAsync();
                }

                await using var firstContext = CreateContext(store);
                await using var staleContext = CreateContext(store);

                var first = await firstContext.Entities.SingleAsync();
                var stale = await staleContext.Entities.SingleAsync();

                first.Name = "第一个提交";
                first.Version = 2;
                await firstContext.SaveChangesAsync();

                stale.Name = "过期提交";
                stale.Version = 3;

                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                    () => staleContext.SaveChangesAsync());
            });

    [DamengFact]
    public Task TransactionRollbackDiscardsChanges()
        => DamengTestStore.WithEntityTableAsync(
            async store =>
            {
                await using (var context = CreateContext(store))
                {
                    await using var transaction = await context.Database.BeginTransactionAsync();

                    context.Entities.Add(
                        new FunctionalEntity { Name = "必须回滚", Version = 1 });
                    await context.SaveChangesAsync();
                    await transaction.RollbackAsync();
                }

                await using (var context = CreateContext(store))
                {
                    Assert.Empty(await context.Entities.ToListAsync());
                }
            });

    [DamengFact]
    public Task SavepointRollbackPreservesEarlierWork()
        => DamengTestStore.WithEntityTableAsync(
            async store =>
            {
                await using (var context = CreateContext(store))
                {
                    await using var transaction = await context.Database.BeginTransactionAsync();

                    context.Entities.Add(
                        new FunctionalEntity { Name = "保存点之前", Version = 1 });
                    await context.SaveChangesAsync();

                    await transaction.CreateSavepointAsync("EF10_SAVEPOINT");

                    context.Entities.Add(
                        new FunctionalEntity { Name = "保存点之后", Version = 1 });
                    await context.SaveChangesAsync();

                    await transaction.RollbackToSavepointAsync("EF10_SAVEPOINT");
                    await transaction.CommitAsync();
                }

                await using (var context = CreateContext(store))
                {
                    var names = await context.Entities
                        .OrderBy(item => item.Id)
                        .Select(item => item.Name)
                        .ToListAsync();

                    Assert.Equal(["保存点之前"], names);
                }
            });

    private static FunctionalContext CreateContext(DamengTestStore store)
    {
        var options = new DbContextOptionsBuilder<FunctionalContext>()
            .UseDamengUnderTest(store.ConnectionString)
            .ReplaceService<IModelCacheKeyFactory, FunctionalModelCacheKeyFactory>()
            .EnableDetailedErrors()
            .Options;

        return new FunctionalContext(options, store.TableName);
    }

    private sealed class ConnectionContext(DbContextOptions<ConnectionContext> options)
        : DbContext(options);

    private sealed class FunctionalContext(
        DbContextOptions<FunctionalContext> options,
        string tableName)
        : DbContext(options)
    {
        public string TableName { get; } = tableName;

        public DbSet<FunctionalEntity> Entities => Set<FunctionalEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<FunctionalEntity>(
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

    private sealed class FunctionalModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is FunctionalContext functionalContext
                ? (context.GetType(), functionalContext.TableName, designTime)
                : (context.GetType(), designTime);
    }

    private sealed class FunctionalEntity
    {
        public long Id { get; set; }

        public required string Name { get; set; }

        public string? Note { get; set; }

        public int Version { get; set; }
    }
}
