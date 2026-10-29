using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengCommentOnlyMigrationsTests
{
    public static IEnumerable<object[]> Changes()
    {
        foreach (var identity in new[] { false, true })
        foreach (var stage in new[] { 0, 1, 2 })
        foreach (var idempotent in new[] { false, true }) yield return [identity, stage, idempotent];
    }
    private static readonly string?[] Comments = [null, "备注 '引号'\n第二行", "更新备注", null];

    [Theory]
    [MemberData(nameof(Changes))]
    public void CommentChangesProduceOnlyCommentCommandsFromModelDiff(bool identity, int stage, bool idempotent)
    {
        using var source = Create(identity, Comments[stage]);
        using var target = Create(identity, Comments[stage + 1]);
        var operation = Difference(source, target);
        Assert.NotEqual(operation.Comment, operation.OldColumn.Comment);
        var command = Assert.Single(target.GetService<IMigrationsSqlGenerator>().Generate([operation],
            target.GetService<IDesignTimeModel>().Model, idempotent ? MigrationsSqlGenerationOptions.Idempotent : MigrationsSqlGenerationOptions.Default));
        Assert.True(command.TransactionSuppressed);
        Assert.Contains("COMMENT ON COLUMN \"a\"\"b\".\"o\"\"r\".", command.CommandText);
        Assert.DoesNotContain("MODIFY", command.CommandText);
        Assert.DoesNotContain("DROP DEFAULT", command.CommandText);
        Assert.DoesNotContain("IDENTITY", command.CommandText);
        Assert.Equal(idempotent, command.CommandText.StartsWith("EXECUTE IMMEDIATE", StringComparison.Ordinal));
        if (Comments[stage + 1] is null) Assert.Contains(idempotent ? "IS ''''" : "IS ''", command.CommandText);
    }

    [Theory]
    [InlineData("type")][InlineData("clr")][InlineData("unicode")][InlineData("fixed")]
    [InlineData("length")][InlineData("precision")][InlineData("scale")][InlineData("rowversion")]
    [InlineData("nullable")][InlineData("default")][InlineData("defaultSql")][InlineData("computed")]
    [InlineData("stored")][InlineData("collation")][InlineData("annotation")][InlineData("sequence")]
    [InlineData("strategy")]
    public void StructuralChangeIsNotClassifiedAsCommentOnly(string facet)
    {
        using var source = Create(false, null); using var target = Create(false, "new");
        var op = Difference(source, target);
        switch (facet)
        {
            case "type": op.ColumnType = "NVARCHAR2(90)"; break;
            case "clr": op.ClrType = typeof(char[]); break;
            case "unicode": op.IsUnicode = false; break;
            case "fixed": op.IsFixedLength = true; break;
            case "length": op.MaxLength = 90; break;
            case "precision": op.Precision = 8; break;
            case "scale": op.Scale = 2; break;
            case "rowversion": op.IsRowVersion = true; break;
            case "nullable": op.IsNullable = !op.OldColumn.IsNullable; break;
            case "default": op.DefaultValue = "new default"; break;
            case "defaultSql": op.DefaultValueSql = "'new default'"; break;
            case "computed": op.ComputedColumnSql = "'computed'"; break;
            case "stored": op.IsStored = true; break;
            case "collation": op.Collation = "BINARY"; break;
            case "annotation": op["Unknown:Semantic"] = "changed"; break;
            case "sequence": op["Dameng:SequenceName"] = "OtherSequence"; break;
            case "strategy": op["Dameng:ValueGenerationStrategy"] = DamengValueGenerationStrategy.IdentityColumn; break;
        }
        try
        {
            var sql = string.Concat(target.GetService<IMigrationsSqlGenerator>().Generate([op]).Select(c => c.CommandText));
            Assert.Contains("MODIFY", sql);
        }
        catch (NotSupportedException) { /* Existing rejection must not be bypassed by a comment. */ }
    }

    [Theory]
    [InlineData("length")][InlineData("nullable")][InlineData("default")][InlineData("defaultSql")]
    [InlineData("unicode")][InlineData("fixed")][InlineData("collation")]
    public void RealStructuralModelDifferencesAreNotCommentOnly(string shape)
    {
        using var source = Create(false, null); using var target = Create(false, "new", shape: shape);
        var operation = Difference(source, target);
        Assert.Contains("MODIFY", string.Concat(target.GetService<IMigrationsSqlGenerator>().Generate([operation],
            target.GetService<IDesignTimeModel>().Model).Select(c => c.CommandText)));
    }

    [Theory]
    [InlineData("stored")][InlineData("identityDefault")][InlineData("missingSequence")]
    public void CommentOnlyDoesNotBypassExistingDefinitionValidation(string invalid)
    {
        using var source = Create(true, null); using var target = Create(true, "new");
        var op = Difference(source, target);
        if (invalid == "stored")
        {
            op.ComputedColumnSql = op.OldColumn.ComputedColumnSql = "1";
            op.IsStored = op.OldColumn.IsStored = true;
            Assert.Throws<NotSupportedException>(() => target.GetService<IMigrationsSqlGenerator>().Generate([op]));
        }
        else
        {
            if (invalid == "identityDefault") op.DefaultValue = op.OldColumn.DefaultValue = 1L;
            else
            {
                op["Dameng:ValueGenerationStrategy"] = op.OldColumn["Dameng:ValueGenerationStrategy"] = DamengValueGenerationStrategy.Sequence;
            }
            Assert.Throws<InvalidOperationException>(() => target.GetService<IMigrationsSqlGenerator>().Generate([op]));
        }
    }

    [Fact]
    public void CommentOnlyByteColumnDoesNotRequireLengthGuard()
    {
        using var source = Create(false, null); using var target = Create(false, "new");
        var operation = Difference(source, target);
        operation.ColumnType = operation.OldColumn.ColumnType = "VARCHAR(40 BYTE)";
        var command = Assert.Single(target.GetService<IMigrationsSqlGenerator>().Generate([operation]));
        Assert.DoesNotContain("LENGTH_IN_CHAR", command.CommandText);
        Assert.StartsWith("COMMENT", command.CommandText);
    }

    [Fact]
    public void StructurallyEqualArrayDefaultsPermitCommentOnly()
    {
        using var source = Create(false, null); using var target = Create(false, "new");
        var op = Difference(source, target);
        op.DefaultValue = new byte[] { 1, 2 }; op.OldColumn.DefaultValue = new byte[] { 1, 2 };
        Assert.StartsWith("COMMENT", Assert.Single(target.GetService<IMigrationsSqlGenerator>().Generate([op])).CommandText);
        op.DefaultValue = new byte[] { 1, 3 };
        Assert.Contains("MODIFY", string.Concat(target.GetService<IMigrationsSqlGenerator>().Generate([op]).Select(c => c.CommandText)));
    }

    [Fact]
    public void CommentChangeDoesNotEraseSharedColumnGenerationFacets()
    {
        using var source = Create(true, null, shared: true); using var target = Create(true, "new", shared: true);
        var op = Difference(source, target);
        Assert.Equal(17L, op["Dameng:IdentitySeed"]); Assert.Equal(3, op["Dameng:IdentityIncrement"]);
        Assert.Equal(op["Dameng:ValueGenerationStrategy"], op.OldColumn["Dameng:ValueGenerationStrategy"]);
        Assert.StartsWith("COMMENT", Assert.Single(target.GetService<IMigrationsSqlGenerator>().Generate([op])).CommandText);
    }

    private static AlterColumnOperation Difference(Context source, Context target)
        => Assert.IsType<AlterColumnOperation>(Assert.Single(target.GetService<IMigrationsModelDiffer>().GetDifferences(
            source.GetService<IDesignTimeModel>().Model.GetRelationalModel(), target.GetService<IDesignTimeModel>().Model.GetRelationalModel())));
    private static Context Create(bool identity, string? comment, bool shared = false, string shape = "")
        => new(new DbContextOptionsBuilder<Context>().UseDameng("Server=localhost;User=test;Password=test")
            .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, identity, comment, shared, shape);
    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        { var c = (Context)context; return (c.Identity, c.Comment, c.Shared, c.Shape, designTime); }
    }
    private sealed class Context(DbContextOptions<Context> options, bool identity, string? comment, bool shared, string shape) : DbContext(options)
    {
        public bool Identity => identity; public string? Comment => comment; public bool Shared => shared; public string Shape => shape;
        protected override void OnModelCreating(ModelBuilder model)
        {
            var e = model.Entity<Row>(); e.ToTable("o\"r", "a\"b");
            e.HasKey(x => x.Id); var id = e.Property(x => x.Id).HasColumnName("I\"d").UseDamengIdentityColumn(17, 3);
            var text = e.Property(x => x.Text).HasColumnName("N\"ote").HasMaxLength(40);
            if (identity) id.HasComment(comment); else text.HasComment(comment);
            switch (shape)
            {
                case "length": text.HasMaxLength(90); break;
                case "nullable": text.IsRequired(); break;
                case "default": text.HasDefaultValue("seed"); break;
                case "defaultSql": text.HasDefaultValueSql("'seed'"); break;
                case "unicode": text.IsUnicode(false); break;
                case "fixed": text.IsFixedLength(); break;
                case "collation": text.UseCollation("BINARY"); break;
            }

            if (shared)
            {
                var d = model.Entity<Detail>(); d.ToTable("o\"r", "a\"b"); d.HasKey(x => x.Id);
                d.Property(x => x.Id).HasColumnName("I\"d").UseDamengIdentityColumn(17, 3).HasComment(comment);
                e.HasOne(x => x.Detail).WithOne().HasForeignKey<Detail>(x => x.Id);
            }
            else e.Ignore(x => x.Detail);
        }
    }
    private sealed class Row { public long Id { get; set; } public string? Text { get; set; } public Detail? Detail { get; set; } }
    private sealed class Detail { public long Id { get; set; } }
}
