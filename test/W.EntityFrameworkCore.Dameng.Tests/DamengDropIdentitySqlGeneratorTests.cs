using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengDropIdentitySqlGeneratorTests
{
    [Theory]
    [InlineData(null, "Orders", "ALTER TABLE \"Orders\" DROP IDENTITY;\n")]
    [InlineData("app", "Orders", "ALTER TABLE \"app\".\"Orders\" DROP IDENTITY;\n")]
    [InlineData("a\"b", "o\"r", "ALTER TABLE \"a\"\"b\".\"o\"\"r\" DROP IDENTITY;\n")]
    public void PureRemovalIsOneTransactionSuppressedTableCommand(string? schema, string table, string expected)
    {
        using var context = CreateContext();
        var operation = Removal();
        operation.Schema = schema;
        operation.Table = table;
        operation.OldColumn["Dameng:IdentitySeed"] = 17L;
        operation.OldColumn["Dameng:IdentityIncrement"] = 3;

        var command = Assert.Single(context.GetService<IMigrationsSqlGenerator>().Generate([operation]));

        Assert.Equal(expected, command.CommandText, ignoreLineEndingDifferences: true);
        Assert.True(command.TransactionSuppressed);
    }

    [Fact]
    public void RemovalUsesExistingIdempotentDynamicSqlWrapper()
    {
        using var context = CreateContext();
        var command = Assert.Single(context.GetService<IMigrationsSqlGenerator>().Generate(
            [Removal()], options: MigrationsSqlGenerationOptions.Idempotent));

        Assert.Equal("EXECUTE IMMEDIATE 'ALTER TABLE \"Orders\" DROP IDENTITY;';\n", command.CommandText,
            ignoreLineEndingDifferences: true);
        Assert.True(command.TransactionSuppressed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealEfModelDifferenceRemovesIdentityAndDownIsRejected(bool explicitIdentity)
    {
        using var source = explicitIdentity
            ? (DbContext)new ExplicitIdentityContext(CreateOptions<ExplicitIdentityContext>())
            : new IdentityContext(CreateOptions<IdentityContext>());
        using var target = CreateContext();
        var sourceModel = source.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var targetModel = target.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var differ = target.GetService<IMigrationsModelDiffer>();
        var operation = Assert.IsType<AlterColumnOperation>(Assert.Single(differ.GetDifferences(sourceModel, targetModel)));
        Assert.Equal(DamengValueGenerationStrategy.IdentityColumn, operation.OldColumn["Dameng:ValueGenerationStrategy"]);
        Assert.Null(operation["Dameng:IdentitySeed"]);
        Assert.Null(operation["Dameng:IdentityIncrement"]);

        var command = Assert.Single(target.GetService<IMigrationsSqlGenerator>().Generate([operation]));
        Assert.Equal("ALTER TABLE \"app\".\"Orders\" DROP IDENTITY;\n", command.CommandText,
            ignoreLineEndingDifferences: true);
        Assert.True(command.TransactionSuppressed);

        var down = Assert.IsType<AlterColumnOperation>(Assert.Single(differ.GetDifferences(targetModel, sourceModel)));
        var exception = Assert.Throws<NotSupportedException>(() => target.GetService<IMigrationsSqlGenerator>().Generate([down]));
        Assert.Contains("adding or restoring IDENTITY", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("clr")]
    [InlineData("nullable")]
    [InlineData("unicode")]
    [InlineData("fixed")]
    [InlineData("length")]
    [InlineData("precision")]
    [InlineData("scale")]
    [InlineData("rowversion")]
    [InlineData("default")]
    [InlineData("defaultSql")]
    [InlineData("computed")]
    [InlineData("stored")]
    [InlineData("collation")]
    [InlineData("comment")]
    [InlineData("sequence")]
    [InlineData("sequenceName")]
    [InlineData("sequenceSchema")]
    [InlineData("annotation")]
    [InlineData("removedAnnotation")]
    public void RemovalRejectsEveryAdditionalColumnDefinitionChange(string change)
    {
        using var context = CreateContext();
        var operation = Removal();
        switch (change)
        {
            case "type": operation.ColumnType = "INT"; break;
            case "clr": operation.ClrType = typeof(int); break;
            case "nullable": operation.IsNullable = true; break;
            case "unicode": operation.IsUnicode = true; break;
            case "fixed": operation.IsFixedLength = true; break;
            case "length": operation.MaxLength = 20; break;
            case "precision": operation.Precision = 10; break;
            case "scale": operation.Scale = 2; break;
            case "rowversion": operation.IsRowVersion = true; break;
            case "default": operation.DefaultValue = 42L; break;
            case "defaultSql": operation.DefaultValueSql = "42"; break;
            case "computed": operation.ComputedColumnSql = "1 + 1"; break;
            case "stored": operation.IsStored = true; break;
            case "collation": operation.Collation = "BINARY"; break;
            case "comment": operation.Comment = "变更"; break;
            case "sequence": operation["Dameng:ValueGenerationStrategy"] = DamengValueGenerationStrategy.Sequence; break;
            case "sequenceName": operation["Dameng:SequenceName"] = "NextId"; break;
            case "sequenceSchema": operation["Dameng:SequenceSchema"] = "app"; break;
            case "annotation": operation["Custom:ColumnMeaning"] = "new"; break;
            case "removedAnnotation": operation.OldColumn["Custom:ColumnMeaning"] = "old"; break;
        }

        var exception = Assert.Throws<NotSupportedException>(() => context.GetService<IMigrationsSqlGenerator>().Generate([operation]));
        Assert.Contains("all other column", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoneStrategyAndUnchangedAnnotationsAreAccepted(bool stringAnnotations)
    {
        using var context = CreateContext();
        var operation = Removal();
        operation["Dameng:ValueGenerationStrategy"] = stringAnnotations ? "None" : DamengValueGenerationStrategy.None;
        operation.OldColumn["Dameng:ValueGenerationStrategy"] = stringAnnotations ? "IdentityColumn" : DamengValueGenerationStrategy.IdentityColumn;
        operation["Custom:ColumnMeaning"] = "retained";
        operation.OldColumn["Custom:ColumnMeaning"] = "retained";
        Assert.Single(context.GetService<IMigrationsSqlGenerator>().Generate([operation]));
    }

    internal static AlterColumnOperation Removal()
    {
        var operation = new AlterColumnOperation
        {
            Table = "Orders",
            Name = "Id",
            ClrType = typeof(long),
            ColumnType = "BIGINT",
            OldColumn = new AddColumnOperation { ClrType = typeof(long), ColumnType = "BIGINT" }
        };
        operation.OldColumn["Dameng:ValueGenerationStrategy"] = DamengValueGenerationStrategy.IdentityColumn;
        return operation;
    }

    private static ManualContext CreateContext()
        => new(CreateOptions<ManualContext>());

    private static DbContextOptions<TContext> CreateOptions<TContext>() where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test").Options;

    private class IdentityContext(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<IdentityEntity>();
            entity.ToTable("Orders", "app");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnType("BIGINT").HasComment("保留的列注释");
        }
    }

    private sealed class ExplicitIdentityContext(DbContextOptions<ExplicitIdentityContext> options) : IdentityContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<IdentityEntity>().Property(item => item.Id).UseDamengIdentityColumn(17, 3);
        }
    }

    private sealed class ManualContext(DbContextOptions<ManualContext> options) : IdentityContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<IdentityEntity>().Property(item => item.Id).ValueGeneratedNever();
        }
    }

    private sealed class IdentityEntity
    {
        public long Id { get; set; }
    }
}
