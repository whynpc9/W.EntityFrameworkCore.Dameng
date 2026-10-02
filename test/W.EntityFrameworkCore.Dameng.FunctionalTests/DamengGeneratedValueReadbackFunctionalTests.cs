using System.Data.Common;
using System.Globalization;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengGeneratedValueReadbackFunctionalTests
{
    [DamengTheory]
    [InlineData(KeyMode.Client, false)]
    [InlineData(KeyMode.Client, true)]
    [InlineData(KeyMode.Composite, false)]
    [InlineData(KeyMode.Composite, true)]
    [InlineData(KeyMode.Identity, false)]
    [InlineData(KeyMode.Identity, true)]
    [InlineData(KeyMode.Sequence, false)]
    [InlineData(KeyMode.Sequence, true)]
    public Task InsertImmediatelyReadsDefaultsNullAndComputedValues(KeyMode mode, bool async)
        => GeneratedStore.WithObjectsAsync(mode, trigger: false, async store =>
        {
            await using var context = CreateContext(store);
            var clientKey = mode is KeyMode.Client or KeyMode.Composite;
            var first = NewEntity("alpha达梦", id: clientKey ? 7 : 0, tenant: 10);
            var second = NewEntity("beta达梦", id: clientKey ? (mode == KeyMode.Composite ? 7 : 8) : 0, tenant: 11);
            context.Entities.AddRange(first, second);

            Assert.Equal(2, await SaveAsync(context, async));
            AssertGenerated(context, first, version: 1);
            AssertGenerated(context, second, version: 1);
            if (mode == KeyMode.Identity)
            {
                Assert.Equal(17L, first.Id);
                Assert.Equal(20L, second.Id);
            }
            else if (mode == KeyMode.Sequence)
            {
                Assert.Equal(1000L, first.Id);
                Assert.Equal(1001L, second.Id);
            }
            else
            {
                Assert.Equal(7L, first.Id);
                Assert.Equal(mode == KeyMode.Composite ? 7L : 8L, second.Id);
            }

            await using var independent = CreateContext(store);
            var rows = await independent.Entities.AsNoTracking().OrderBy(item => item.Name).ToArrayAsync();
            Assert.Equal(2, rows.Length);
            AssertSameValues(first, rows[0], mode);
            AssertSameValues(second, rows[1], mode);
        });

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TriggerValuesBecomeCurrentAndOriginalAcrossRepeatedUpdatesAndStaleFailure(bool async)
        => GeneratedStore.WithObjectsAsync(KeyMode.Identity, trigger: true, async store =>
        {
            await using (var seed = CreateContext(store))
            {
                seed.Entities.Add(NewEntity("first"));
                await SaveAsync(seed, async);
            }
            await using var writer = CreateContext(store);
            await using var staleContext = CreateContext(store);
            var current = await writer.Entities.SingleAsync();
            var stale = await staleContext.Entities.SingleAsync();
            Assert.Equal(1L, current.Version);
            var oldComputed = stale.Normalized;

            current.Name = "second";
            Assert.Equal(1, await SaveAsync(writer, async));
            AssertGenerated(writer, current, version: 2);
            Assert.Equal("SECOND", current.Normalized);
            current.Name = "third";
            Assert.Equal(1, await SaveAsync(writer, async));
            AssertGenerated(writer, current, version: 3);
            Assert.Equal("THIRD", current.Normalized);

            stale.Name = "stale";
            await AssertConcurrencyFailureAsync(staleContext, stale, async);
            Assert.Equal(1L, stale.Version);
            Assert.Equal(1L, staleContext.Entry(stale).Property(item => item.Version).OriginalValue);
            Assert.Equal(oldComputed, stale.Normalized);
            Assert.Equal(EntityState.Modified, staleContext.Entry(stale).State);
            await using var independent = CreateContext(store);
            AssertSameValues(current, await independent.Entities.AsNoTracking().SingleAsync(), store.Mode);
        });

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DeletedRowUpdateThrowsWithoutPropagatingAnotherRowsGeneratedValues(bool async)
        => GeneratedStore.WithObjectsAsync(KeyMode.Identity, trigger: true, async store =>
        {
            await using (var seed = CreateContext(store))
            {
                seed.Entities.AddRange(NewEntity("deleted"), NewEntity("survivor"));
                await SaveAsync(seed, async);
            }
            await using var missingContext = CreateContext(store);
            var missing = await missingContext.Entities.SingleAsync(item => item.Name == "deleted");
            var oldComputed = missing.Normalized;
            await using (var deleting = CreateContext(store))
            {
                Assert.Equal(1, await deleting.Entities.Where(item => item.Id == missing.Id).ExecuteDeleteAsync());
                var survivor = await deleting.Entities.SingleAsync();
                survivor.Name = "survivor-new";
                await SaveAsync(deleting, async);
                AssertGenerated(deleting, survivor, version: 2);
            }

            missing.Name = "must-fail";
            await AssertConcurrencyFailureAsync(missingContext, missing, async);
            Assert.Equal(1L, missing.Version);
            Assert.Equal(1L, missingContext.Entry(missing).Property(item => item.Version).OriginalValue);
            Assert.Equal(oldComputed, missing.Normalized);
            await using var independent = CreateContext(store);
            var remaining = await independent.Entities.AsNoTracking().SingleAsync();
            Assert.Equal("survivor-new", remaining.Name);
            Assert.Equal("SURVIVOR-NEW", remaining.Normalized);
            Assert.Equal(2L, remaining.Version);
        });

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CallerOwnedTransactionReadsGeneratedValuesAndRollbackRestoresDatabase(bool async)
        => GeneratedStore.WithObjectsAsync(KeyMode.Identity, trigger: true, async store =>
        {
            long originalId;
            await using (var seed = CreateContext(store))
            {
                var original = NewEntity("before");
                seed.Entities.Add(original);
                await SaveAsync(seed, async);
                originalId = original.Id;
            }

            await using var connection = new DmConnection(store.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = async ? await connection.BeginTransactionAsync() : connection.BeginTransaction();
            await using (var context = CreateContext(store, connection))
            {
                if (async)
                {
                    await context.Database.UseTransactionAsync(transaction);
                }
                else
                {
                    context.Database.UseTransaction(transaction);
                }
                var original = await context.Entities.SingleAsync();
                original.Name = "inside";
                Assert.Equal(1, await SaveAsync(context, async));
                AssertGenerated(context, original, version: 2);
                Assert.Equal("INSIDE", original.Normalized);

                var inserted = NewEntity("rolled-back");
                context.Entities.Add(inserted);
                Assert.Equal(1, await SaveAsync(context, async));
                AssertGenerated(context, inserted, version: 1);
                Assert.True(inserted.Id > originalId);
                Assert.Equal(2, await context.Entities.AsNoTracking().CountAsync());
                if (async)
                {
                    await transaction.RollbackAsync();
                }
                else
                {
                    transaction.Rollback();
                }
            }

            await using var independent = CreateContext(store);
            var restored = await independent.Entities.AsNoTracking().SingleAsync();
            Assert.Equal(originalId, restored.Id);
            Assert.Equal("before", restored.Name);
            Assert.Equal("BEFORE", restored.Normalized);
            Assert.Equal(1L, restored.Version);
            Assert.Equal("默认中文", restored.DefaultText);
            Assert.Null(restored.NullableDefault);
        });

    private static GeneratedEntity NewEntity(string name, long id = 0, int tenant = 0)
        => new() { Id = id, TenantId = tenant, Name = name };

    private static async Task<int> SaveAsync(GeneratedContext context, bool async)
        => async ? await context.SaveChangesAsync() : context.SaveChanges();

    private static async Task AssertConcurrencyFailureAsync(GeneratedContext context, GeneratedEntity entity, bool async)
    {
        var exception = async
            ? await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync())
            : Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
        Assert.Same(entity, Assert.Single(exception.Entries).Entity);
    }

    private static void AssertGenerated(GeneratedContext context, GeneratedEntity entity, long version)
    {
        Assert.Equal("默认中文", entity.DefaultText);
        Assert.Equal(37, entity.DefaultNumber);
        Assert.Null(entity.NullableDefault);
        Assert.Equal(entity.Name.ToUpperInvariant(), entity.Normalized);
        Assert.Equal(version, entity.Version);
        var entry = context.Entry(entity);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(entry.Property(item => item.Id).IsTemporary);
        Assert.Equal(entity.Normalized, entry.Property(item => item.Normalized).OriginalValue);
        Assert.Equal(entity.Version, entry.Property(item => item.Version).OriginalValue);
        Assert.Equal(entity.DefaultText, entry.Property(item => item.DefaultText).OriginalValue);
        Assert.Null(entry.Property(item => item.NullableDefault).OriginalValue);
    }

    private static void AssertSameValues(GeneratedEntity expected, GeneratedEntity actual, KeyMode mode)
    {
        Assert.Equal(expected.Id, actual.Id);
        if (mode == KeyMode.Composite)
        {
            Assert.Equal(expected.TenantId, actual.TenantId);
        }
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.DefaultText, actual.DefaultText);
        Assert.Equal(expected.DefaultNumber, actual.DefaultNumber);
        Assert.Equal(expected.NullableDefault, actual.NullableDefault);
        Assert.Equal(expected.Normalized, actual.Normalized);
        Assert.Equal(expected.Version, actual.Version);
    }

    private static GeneratedContext CreateContext(GeneratedStore store, DbConnection? connection = null)
    {
        var builder = new DbContextOptionsBuilder<GeneratedContext>();
        if (connection is null)
        {
            builder.UseDameng(store.ConnectionString);
        }
        else
        {
            builder.UseDameng(connection);
        }
        builder.ReplaceService<IModelCacheKeyFactory, GeneratedModelCacheKeyFactory>().EnableDetailedErrors();
        return new GeneratedContext(builder.Options, store);
    }

    public enum KeyMode { Client, Composite, Identity, Sequence }

    private sealed class GeneratedContext(DbContextOptions<GeneratedContext> options, GeneratedStore store) : DbContext(options)
    {
        public GeneratedStore Store { get; } = store;
        public DbSet<GeneratedEntity> Entities => Set<GeneratedEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<GeneratedEntity>();
            entity.ToTable(Store.TableName, table =>
            {
                if (Store.Trigger)
                {
                    table.HasTrigger(Store.TriggerName);
                }
            });
            if (Store.Mode == KeyMode.Composite)
            {
                entity.HasKey(item => new { item.Id, item.TenantId });
                entity.Property(item => item.TenantId).HasColumnName("TENANT_ID").ValueGeneratedNever();
            }
            else
            {
                entity.HasKey(item => item.Id);
                entity.Ignore(item => item.TenantId);
            }
            var id = entity.Property(item => item.Id).HasColumnName("ID");
            if (Store.Mode == KeyMode.Identity)
            {
                id.UseDamengIdentityColumn(17, 3);
            }
            else if (Store.Mode == KeyMode.Sequence)
            {
                id.UseDamengSequence(Store.SequenceName);
            }
            else
            {
                id.ValueGeneratedNever();
            }
            entity.Property(item => item.Name).HasColumnName("NAME").HasMaxLength(80).IsRequired();
            entity.Property(item => item.DefaultText).HasColumnName("DEFAULT_TEXT").HasMaxLength(40).HasDefaultValue("默认中文");
            entity.Property(item => item.DefaultNumber).HasColumnName("DEFAULT_NUMBER").HasDefaultValue(37);
            entity.Property(item => item.NullableDefault).HasColumnName("NULLABLE_DEFAULT").HasMaxLength(40)
                .HasDefaultValueSql("NULL").HasSentinel("客户端占位");
            entity.Property(item => item.Normalized).HasColumnName("NORMALIZED").HasMaxLength(80)
                .HasComputedColumnSql("UPPER(\"NAME\")", stored: false);
            var version = entity.Property(item => item.Version).HasColumnName("VERSION").HasDefaultValue(1L);
            if (Store.Trigger)
            {
                version.ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            }
        }
    }

    private sealed class GeneratedModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is GeneratedContext generated
                ? (context.GetType(), generated.Store.TableName, generated.Store.Mode, generated.Store.Trigger, designTime)
                : (object)(context.GetType(), designTime);
    }

    private sealed class GeneratedEntity
    {
        public long Id { get; set; }
        public int TenantId { get; set; }
        public required string Name { get; set; }
        public string? DefaultText { get; set; }
        public int DefaultNumber { get; set; }
        public string? NullableDefault { get; set; } = "客户端占位";
        public string? Normalized { get; set; }
        public long Version { get; set; }
    }

    private sealed class GeneratedStore
    {
        private bool _tableCreated;
        private bool _sequenceCreated;
        private bool _triggerCreated;

        private GeneratedStore(KeyMode mode, bool trigger)
        {
            Mode = mode;
            Trigger = trigger;
            ConnectionString = DamengTestEnvironment.GetRequiredConnectionString();
            var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
            TableName = $"EF10_GV_{suffix}";
            SequenceName = $"EF10_GS_{suffix}";
            TriggerName = $"EF10_GT_{suffix}";
            PrimaryKeyName = $"PK_GV_{suffix}";
        }

        public KeyMode Mode { get; }
        public bool Trigger { get; }
        public string ConnectionString { get; }
        public string TableName { get; }
        public string SequenceName { get; }
        public string TriggerName { get; }
        public string PrimaryKeyName { get; }

        public static async Task WithObjectsAsync(KeyMode mode, bool trigger, Func<GeneratedStore, Task> test)
        {
            var store = new GeneratedStore(mode, trigger);
            try
            {
                await store.CreateAsync();
                await test(store);
            }
            finally
            {
                await store.DropAsync();
            }
        }

        private async Task CreateAsync()
        {
            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            if (Mode == KeyMode.Sequence)
            {
                await ExecuteAsync(connection, $"CREATE SEQUENCE \"{SequenceName}\" START WITH 1000 INCREMENT BY 1");
                _sequenceCreated = true;
            }
            var idDefinition = Mode switch
            {
                KeyMode.Identity => "BIGINT IDENTITY(17,3) NOT NULL",
                KeyMode.Sequence => $"BIGINT DEFAULT (\"{SequenceName}\".NEXTVAL) NOT NULL",
                _ => "BIGINT NOT NULL"
            };
            var tenantColumn = Mode == KeyMode.Composite ? "\"TENANT_ID\" INT NOT NULL," : "";
            var keys = Mode == KeyMode.Composite ? "\"ID\", \"TENANT_ID\"" : "\"ID\"";
            await ExecuteAsync(connection, $"""
                CREATE TABLE "{TableName}" (
                    "ID" {idDefinition},
                    {tenantColumn}
                    "NAME" NVARCHAR2(80) NOT NULL,
                    "DEFAULT_TEXT" NVARCHAR2(40) DEFAULT '默认中文' NULL,
                    "DEFAULT_NUMBER" INT DEFAULT 37 NOT NULL,
                    "NULLABLE_DEFAULT" NVARCHAR2(40) DEFAULT NULL,
                    "NORMALIZED" AS (UPPER("NAME")),
                    "VERSION" BIGINT DEFAULT 1 NOT NULL,
                    CONSTRAINT "{PrimaryKeyName}" NOT CLUSTER PRIMARY KEY ({keys})
                )
                """);
            _tableCreated = true;
            if (Trigger)
            {
                // DM's public trigger contract: BEFORE ROW may assign :NEW, using :OLD as the prior row.
                await ExecuteAsync(connection, $"""
                    CREATE TRIGGER "{TriggerName}" BEFORE UPDATE ON "{TableName}"
                    FOR EACH ROW
                    BEGIN
                        :NEW."VERSION" := :OLD."VERSION" + 1;
                    END;
                    """);
                _triggerCreated = true;
            }
        }

        private async Task DropAsync()
        {
            if (!_tableCreated && !_sequenceCreated && !_triggerCreated)
            {
                return;
            }
            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            try
            {
                if (_triggerCreated)
                {
                    await ExecuteAsync(connection, $"DROP TRIGGER \"{TriggerName}\"");
                }
            }
            finally
            {
                try
                {
                    if (_tableCreated)
                    {
                        await ExecuteAsync(connection, $"DROP TABLE \"{TableName}\"");
                    }
                }
                finally
                {
                    if (_sequenceCreated)
                    {
                        await ExecuteAsync(connection, $"DROP SEQUENCE \"{SequenceName}\"");
                    }
                }
            }
        }

        private static async Task ExecuteAsync(DmConnection connection, string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}
