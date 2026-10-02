using System.Data.Common;
using System.Globalization;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengDropIdentityFunctionalTests
{
    [DamengFact]
    public async Task ModelDifferenceDropsIdentityWithoutChangingRowsOrPrimaryKey()
    {
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var table = $"EF10_DI_{suffix}";
        var primaryKey = $"PK_DI_{suffix}";
        var created = false;
        await using var connection = new DmConnection(connectionString);
        await connection.OpenAsync();
        try
        {
            await ExecuteAsync(connection, $"CREATE TABLE \"{table}\" (\"ID\" BIGINT IDENTITY(17,3) NOT NULL, \"NAME\" NVARCHAR2(40) NOT NULL, CONSTRAINT \"{primaryKey}\" NOT CLUSTER PRIMARY KEY (\"ID\"))");
            created = true;
            await ExecuteAsync(connection, $"INSERT INTO \"{table}\" (\"NAME\") VALUES ('原始数据')");
            Assert.Contains("IDENTITY", await GetDefinitionAsync(connection, table), StringComparison.OrdinalIgnoreCase);

            await using var source = CreateContext(connectionString, table, identity: true);
            await using var target = CreateContext(connectionString, table, identity: false);
            var sourceModel = source.GetService<IDesignTimeModel>().Model.GetRelationalModel();
            var targetModel = target.GetService<IDesignTimeModel>().Model.GetRelationalModel();
            var differ = target.GetService<IMigrationsModelDiffer>();
            var removal = Assert.IsType<AlterColumnOperation>(Assert.Single(differ.GetDifferences(sourceModel, targetModel)));
            var command = Assert.Single(target.GetService<IMigrationsSqlGenerator>().Generate([removal]));
            Assert.True(command.TransactionSuppressed);
            await ExecuteAsync(connection, command.CommandText);

            // DBMS_METADATA reads the server's definition, independent of EF's target model.
            var definition = await GetDefinitionAsync(connection, table);
            Assert.DoesNotContain("IDENTITY", definition, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(primaryKey, definition, StringComparison.Ordinal);
            Assert.Contains("PRIMARY KEY", definition, StringComparison.OrdinalIgnoreCase);
            var original = await target.Entities.AsNoTracking().SingleAsync();
            Assert.Equal(17L, original.Id);
            Assert.Equal("原始数据", original.Name);

            target.Entities.Add(new DropIdentityEntity { Id = 99, Name = "显式主键" });
            await target.SaveChangesAsync();
            await using (var independent = CreateContext(connectionString, table, identity: false))
            {
                var ids = await independent.Entities.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync();
                Assert.Equal([17L, 99L], ids);
            }

            await Assert.ThrowsAnyAsync<DbException>(() => ExecuteAsync(connection, $"INSERT INTO \"{table}\" (\"ID\", \"NAME\") VALUES (17, '重复主键')"));
            await Assert.ThrowsAnyAsync<DbException>(() => ExecuteAsync(connection, $"INSERT INTO \"{table}\" (\"NAME\") VALUES ('缺少必填主键')"));
            Assert.Equal(2, await target.Entities.CountAsync());
            var down = differ.GetDifferences(targetModel, sourceModel);
            Assert.Throws<NotSupportedException>(() => target.GetService<IMigrationsSqlGenerator>().Generate(down));
        }
        finally
        {
            if (created)
            {
                await using var cleanup = new DmConnection(connectionString);
                await cleanup.OpenAsync();
                await ExecuteAsync(cleanup, $"DROP TABLE \"{table}\"");
            }
        }
    }

    private static async Task<string> GetDefinitionAsync(DmConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DBMS_METADATA.GET_DDL('TABLE', :tableName, SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())) FROM DUAL";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tableName";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture)!;
    }

    private static async Task ExecuteAsync(DmConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static DropIdentityContext CreateContext(string connectionString, string table, bool identity)
        => new(new DbContextOptionsBuilder<DropIdentityContext>()
            .UseDameng(connectionString)
            .ReplaceService<IModelCacheKeyFactory, DropIdentityModelCacheKeyFactory>()
            .Options, table, identity);

    private sealed class DropIdentityContext(DbContextOptions<DropIdentityContext> options, string tableName, bool identity) : DbContext(options)
    {
        public string TableName { get; } = tableName;
        public bool Identity { get; } = identity;
        public DbSet<DropIdentityEntity> Entities => Set<DropIdentityEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<DropIdentityEntity>();
            entity.ToTable(TableName);
            entity.HasKey(item => item.Id);
            var id = entity.Property(item => item.Id).HasColumnName("ID").HasColumnType("BIGINT");
            if (Identity)
            {
                id.UseDamengIdentityColumn(17, 3);
            }
            else
            {
                id.ValueGeneratedNever();
            }
            entity.Property(item => item.Name).HasColumnName("NAME").HasMaxLength(40);
        }
    }

    private sealed class DropIdentityModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is DropIdentityContext dropContext
                ? (context.GetType(), dropContext.TableName, dropContext.Identity, designTime)
                : (object)(context.GetType(), designTime);
    }

    private sealed class DropIdentityEntity
    {
        public long Id { get; set; }
        public required string Name { get; set; }
    }
}
