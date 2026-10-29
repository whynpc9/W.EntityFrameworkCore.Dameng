using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

// Preliminary characterization only: a clustered-default server is required to
// select CONFIRMED/REFUTED. These tests intentionally retain the existing SQL.
[Trait("Category", "LobPrimaryKey")]
public sealed class DamengLobPrimaryKeyTests
{
    [Theory]
    [InlineData(null, false, "NCLOB")]
    [InlineData(null, true, "BLOB")]
    [InlineData("CLOB", false, "CLOB")]
    [InlineData("NCLOB", false, "NCLOB")]
    [InlineData("BLOB", true, "BLOB")]
    public void HandwrittenLobModelBaselineDefersClusteringToServer(string? storeType, bool binary, string expected)
    {
        using var context = Create(storeType, binary);
        var model = context.GetService<IDesignTimeModel>().Model;
        var table = Assert.Single(model.GetRelationalModel().Tables);
        Assert.Equal(expected, table.FindColumn("Payload")!.StoreType);
        Assert.Null(table.PrimaryKey!["Dameng:IsClustered"]);
        var sql = context.Database.GenerateCreateScript();
        Assert.Contains("PRIMARY KEY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CLUSTER PRIMARY KEY", sql, StringComparison.Ordinal);
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var generated = context.GetService<IMigrationsSqlGenerator>().Generate(operations, model);
        Assert.All(generated, command => Assert.True(command.TransactionSuppressed));
        Assert.DoesNotContain("CLUSTER PRIMARY KEY", string.Concat(generated.Select(c => c.CommandText)), StringComparison.Ordinal);
        Assert.Empty(context.GetService<IMigrationsModelDiffer>().GetDifferences(model.GetRelationalModel(), model.GetRelationalModel()));
    }

    [Fact]
    public void ExplicitNonclusteredAnnotationIsPreserved()
    {
        using var context = Create("CLOB", false, false);
        Assert.Contains("NOT CLUSTER PRIMARY KEY", context.Database.GenerateCreateScript(), StringComparison.Ordinal);
    }

    [Fact]
    public void NonLobUnannotatedPrimaryKeyKeepsExistingBehavior()
    {
        using var context = Create("NVARCHAR2(40)", false);
        Assert.DoesNotContain("CLUSTER PRIMARY KEY", context.Database.GenerateCreateScript(), StringComparison.Ordinal);
    }

    [Fact]
    public void ModelLessAddPrimaryKeyDoesNotInventColumnMetadata()
    {
        using var context = Create("CLOB", false);
        var sql = string.Concat(context.GetService<IMigrationsSqlGenerator>().Generate([
            new AddPrimaryKeyOperation { Name = "PK_T", Table = "T", Columns = ["Id"] }]).Select(c => c.CommandText));
        Assert.Contains("PRIMARY KEY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CLUSTER PRIMARY KEY", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CLOB", false)]
    [InlineData("NCLOB", false)]
    [InlineData("BLOB", true)]
    public void ManualCreateTableBaselineDefersClusteringToServer(string storeType, bool binary)
    {
        using var context = Create(storeType, binary);
        var table = new CreateTableOperation { Name = "T", PrimaryKey = new AddPrimaryKeyOperation { Name = "PK_T", Table = "T", Columns = ["Id"] } };
        table.Columns.Add(new AddColumnOperation { Name = "Id", Table = "T", ClrType = typeof(int), ColumnType = "INT" });
        table.Columns.Add(new AddColumnOperation { Name = "Payload", Table = "T", ClrType = binary ? typeof(byte[]) : typeof(string), ColumnType = storeType, IsNullable = true });
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate([table]);
        Assert.All(commands, command => Assert.True(command.TransactionSuppressed));
        Assert.DoesNotContain("CLUSTER PRIMARY KEY", string.Concat(commands.Select(c => c.CommandText)), StringComparison.Ordinal);
    }

    private static LobContext Create(string? storeType, bool binary, bool? clustered = null)
        => new(new DbContextOptionsBuilder<LobContext>().UseDameng("Server=synthetic.invalid")
            .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, storeType, binary, clustered);

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        {
            var lob = (LobContext)context;
            return (context.GetType(), lob.StoreType, lob.Binary, lob.Clustered, designTime);
        }
    }
    private sealed class LobContext(DbContextOptions<LobContext> options, string? storeType, bool binary, bool? clustered) : DbContext(options)
    {
        public string? StoreType => storeType;
        public bool Binary => binary;
        public bool? Clustered => clustered;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity("Lob", entity =>
            {
                entity.ToTable("T");
                entity.Property<int>("Id").ValueGeneratedNever();
                var key = entity.HasKey("Id");
                if (clustered.HasValue) key.HasAnnotation("Dameng:IsClustered", clustered.Value);
                var property = entity.Property(binary ? typeof(byte[]) : typeof(string), "Payload");
                if (storeType is not null) property.HasColumnType(storeType);
            });
        }
    }
}
