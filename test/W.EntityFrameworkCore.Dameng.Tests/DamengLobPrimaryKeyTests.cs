using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

// REFUTED on the recorded DM 8.1.5.60 profiles: the server keeps LOB backing
// indexes nonclustered. These tests fix the unchanged client contract; real data
// and catalog protection are verified in the two functional suites.
[Trait("Category", "LobPrimaryKey")]
public sealed class DamengLobPrimaryKeyTests
{
    [Theory]
    [InlineData(null, false, "NCLOB")]
    [InlineData(null, true, "BLOB")]
    [InlineData("CLOB", false, "CLOB")]
    [InlineData("NCLOB", false, "NCLOB")]
    [InlineData("BLOB", true, "BLOB")]
    public void HandwrittenLobModelRetainsVerifiedExistingProtection(string? storeType, bool binary, string expected)
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
    public void CreateTableOperationRetainsVerifiedExistingProtection(string storeType, bool binary)
    {
        using var context = Create(storeType, binary);
        var table = new CreateTableOperation { Name = "T", PrimaryKey = new AddPrimaryKeyOperation { Name = "PK_T", Table = "T", Columns = ["Id"] } };
        table.Columns.Add(new AddColumnOperation { Name = "Id", Table = "T", ClrType = typeof(int), ColumnType = "INT" });
        table.Columns.Add(new AddColumnOperation { Name = "Payload", Table = "T", ClrType = binary ? typeof(byte[]) : typeof(string), ColumnType = storeType, IsNullable = true });
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate([table]);
        Assert.All(commands, command => Assert.True(command.TransactionSuppressed));
        Assert.DoesNotContain("CLUSTER PRIMARY KEY", string.Concat(commands.Select(c => c.CommandText)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CLOB", false)]
    [InlineData("NCLOB", false)]
    [InlineData("BLOB", true)]
    public void ExplicitClusteredLobOperationRetainsVerifiedExistingBehavior(string storeType, bool binary)
    {
        using var context = Create(storeType, binary, true);
        var model = context.GetService<IDesignTimeModel>().Model;
        Assert.Equal(true, Assert.Single(model.GetRelationalModel().Tables).PrimaryKey!["Dameng:IsClustered"]);
        var sql = context.Database.GenerateCreateScript();
        Assert.Contains(" CLUSTER PRIMARY KEY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT CLUSTER", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CLOB", false)]
    [InlineData("NCLOB", false)]
    [InlineData("BLOB", true)]
    public void SharedTableLobColumnRetainsVerifiedExistingProtection(string storeType, bool binary)
    {
        using var context = Create(storeType, binary, shared: true);
        var table = Assert.Single(context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables);
        var payload = table.FindColumn("Payload")!;
        Assert.Equal(storeType, payload.StoreType);
        Assert.Equal(typeof(Part), Assert.Single(payload.PropertyMappings).Property.DeclaringType.ClrType);
        Assert.Equal(2, table.EntityTypeMappings.Count());
        Assert.Null(table.PrimaryKey!["Dameng:IsClustered"]);
        Assert.DoesNotContain("CLUSTER PRIMARY KEY", context.Database.GenerateCreateScript(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CLOB", false)]
    [InlineData("NCLOB", false)]
    [InlineData("BLOB", true)]
    public void AddPrimaryKeyRetainsVerifiedExistingProtection(string storeType, bool binary)
    {
        using var context = Create(storeType, binary);
        var model = context.GetService<IDesignTimeModel>().Model;
        Assert.Equal(storeType, Assert.Single(model.GetRelationalModel().Tables).FindColumn("Payload")!.StoreType);
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate([
            new AddPrimaryKeyOperation { Table = "T", Name = "PK_T", Columns = ["Id"] }], model);
        Assert.All(commands, command => Assert.True(command.TransactionSuppressed));
        Assert.DoesNotContain("CLUSTER PRIMARY KEY", string.Concat(commands.Select(c => c.CommandText)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CLOB", false)]
    [InlineData("NCLOB", false)]
    [InlineData("BLOB", true)]
    public void ExistingLobPrimaryKeyBehaviorProducesStableMigrationDiff(string storeType, bool binary)
    {
        using var first = Create(storeType, binary);
        using var second = Create(storeType, binary);
        var source = first.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var target = second.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        Assert.NotSame(source, target);
        Assert.Empty(second.GetService<IMigrationsModelDiffer>().GetDifferences(source, target));
        Assert.Equal(first.Database.GenerateCreateScript(), second.Database.GenerateCreateScript());
    }

    private static LobContext Create(string? storeType, bool binary, bool? clustered = null, bool shared = false)
        => new(new DbContextOptionsBuilder<LobContext>().UseDameng("Server=synthetic.invalid")
            .ReplaceService<IModelCacheKeyFactory, CacheKey>().EnableServiceProviderCaching(false).Options, storeType, binary, clustered, shared);

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        {
            var lob = (LobContext)context;
            return (context.GetType(), lob.StoreType, lob.Binary, lob.Clustered, lob.Shared, designTime);
        }
    }
    private sealed class LobContext(DbContextOptions<LobContext> options, string? storeType, bool binary, bool? clustered, bool shared) : DbContext(options)
    {
        public string? StoreType => storeType;
        public bool Binary => binary;
        public bool? Clustered => clustered;
        public bool Shared => shared;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (shared)
            {
                var owner = modelBuilder.Entity<Owner>(); owner.ToTable("T");
                owner.Property(e => e.Id).ValueGeneratedNever(); owner.HasKey(e => e.Id);
                var part = modelBuilder.Entity<Part>(); part.ToTable("T");
                part.Property(e => e.Id).ValueGeneratedNever(); part.HasKey(e => e.Id);
                owner.HasOne(e => e.Part).WithOne().HasForeignKey<Part>(e => e.Id);
                owner.Navigation(e => e.Part).IsRequired();
                if (binary) { part.Ignore(e => e.Text); part.Property(e => e.Bytes).HasColumnName("Payload").HasColumnType(storeType!); }
                else { part.Ignore(e => e.Bytes); part.Property(e => e.Text).HasColumnName("Payload").HasColumnType(storeType!); }
                return;
            }
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
    private sealed class Owner { public int Id { get; set; } public Part? Part { get; set; } }
    private sealed class Part { public int Id { get; set; } public string? Text { get; set; } public byte[]? Bytes { get; set; } }
}
