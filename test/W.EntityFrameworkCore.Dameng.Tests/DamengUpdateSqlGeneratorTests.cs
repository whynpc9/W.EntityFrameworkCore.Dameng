using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengUpdateSqlGeneratorTests
{
    [Fact]
    public void ModificationCommandsAreKeptInSingleCommandBatches()
    {
        using var context = CreateContext();
        var factory = context.GetService<IModificationCommandBatchFactory>();

        Assert.IsType<SingularModificationCommandBatch>(factory.Create());
    }

    [Fact]
    public void InsertReadsAllGeneratedValuesFromTheInsertedIdentityRow()
    {
        using var context = CreateContext();
        var generator = context.GetService<IUpdateSqlGenerator>();
        var command = CreateCommand(
            EntityState.Added,
            Column("Id", read: true, key: true),
            Column("Name", value: "达梦", write: true),
            Column("Stamp", read: true));
        var sql = new StringBuilder();

        var mapping = generator.AppendInsertOperation(
            sql,
            command,
            commandPosition: 0,
            out var requiresTransaction);

        Assert.Equal(ResultSetMapping.LastInResultSet, mapping);
        Assert.True(requiresTransaction);
        AssertSql(
            """
            INSERT INTO "APP"."Widgets" ("Name")
            VALUES (:p0);
            SELECT "Id", "Stamp"
            FROM "APP"."Widgets"
            WHERE SQL%ROWCOUNT = 1 AND "Id" = SCOPE_IDENTITY();
            """,
            sql);
    }

    [Fact]
    public void UpdateReturnsOneOnlyWhenExactlyOneRowWasAffected()
    {
        using var context = CreateContext();
        var generator = context.GetService<IUpdateSqlGenerator>();
        var command = CreateCommand(
            EntityState.Modified,
            Column("Name", value: "new", write: true),
            Column("Id", originalValue: 7L, key: true, condition: true),
            Column("Version", originalValue: 3, condition: true));
        var sql = new StringBuilder();

        var mapping = generator.AppendUpdateOperation(
            sql,
            command,
            commandPosition: 0,
            out var requiresTransaction);

        Assert.Equal(
            ResultSetMapping.LastInResultSet | ResultSetMapping.ResultSetWithRowsAffectedOnly,
            mapping);
        Assert.False(requiresTransaction);
        AssertSql(
            """
            UPDATE "APP"."Widgets" SET "Name" = :p0
            WHERE "Id" = :p1 AND "Version" = :p2;
            /*EFCOREROWCOUNT*/SELECT SQL%ROWCOUNT;
            """,
            sql);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClientKeysLocateMultipleGeneratedNonKeyValues(bool compositeKey)
    {
        using var context = CreateContext();
        var generator = context.GetService<IUpdateSqlGenerator>();
        var columns = new List<ColumnModificationParameters>
        {
            Column("Id", value: 7L, write: true, key: true)
        };
        if (compositeKey)
        {
            columns.Add(Column("TenantId", value: 11, write: true, key: true));
        }
        columns.Add(Column("Name", value: "生成值", write: true));
        columns.Add(Column("DefaultText", read: true));
        columns.Add(Column("NullableDefault", read: true));
        columns.Add(Column("Computed", read: true));
        var sql = new StringBuilder();

        var mapping = generator.AppendInsertOperation(sql, CreateCommand(EntityState.Added, [.. columns]), 0, out var transaction);

        Assert.Equal(ResultSetMapping.LastInResultSet, mapping);
        Assert.True(transaction);
        Assert.Contains("SELECT \"DefaultText\", \"NullableDefault\", \"Computed\"", sql.ToString(), StringComparison.Ordinal);
        Assert.Contains(compositeKey
            ? "WHERE SQL%ROWCOUNT = 1 AND \"Id\" = :p0 AND \"TenantId\" = :p1;"
            : "WHERE SQL%ROWCOUNT = 1 AND \"Id\" = :p0;", sql.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SCOPE_IDENTITY", sql.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("CURRVAL", sql.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedUpdateUsesOriginalConcurrencyAndReadsByKeyWithZeroRowGuard(bool nullOriginalToken)
    {
        using var context = CreateContext();
        var generator = context.GetService<IUpdateSqlGenerator>();
        var token = Column("Version", originalValue: nullOriginalToken ? null : 3, value: 4, read: true, condition: true);
        var command = CreateCommand(EntityState.Modified,
            Column("Name", value: "new", write: true),
            Column("Id", originalValue: 7L, value: 7L, key: true, condition: true),
            token,
            Column("Computed", read: true));
        var sql = new StringBuilder();

        var mapping = generator.AppendUpdateOperation(sql, command, 0, out var transaction);

        Assert.Equal(ResultSetMapping.LastInResultSet, mapping);
        Assert.True(transaction);
        var text = sql.ToString();
        Assert.StartsWith("UPDATE \"APP\".\"Widgets\" SET \"Name\" = :p0", text, StringComparison.Ordinal);
        Assert.Contains(nullOriginalToken
            ? "WHERE \"Id\" = :p1 AND \"Version\" IS NULL;"
            : "WHERE \"Id\" = :p1 AND \"Version\" = :p2;", text, StringComparison.Ordinal);
        Assert.Contains("SELECT \"Version\", \"Computed\"", text, StringComparison.Ordinal);
        Assert.Contains("WHERE SQL%ROWCOUNT = 1 AND \"Id\" = :p1;", text, StringComparison.Ordinal);
        var version = command.ColumnModifications.Single(column => column.ColumnName == "Version");
        Assert.True(version.UseOriginalValueParameter);
        Assert.False(version.UseCurrentValueParameter);
        Assert.Equal(nullOriginalToken ? null : 3, version.OriginalValue);
    }

    [Fact]
    public void SequenceGeneratedKeyUsesCurrvalForReadback()
    {
        using var context = new SequenceContext(
            new DbContextOptionsBuilder<SequenceContext>()
                .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
                .Options);
        var generator = context.GetService<IUpdateSqlGenerator>();
        var id = context.Model
            .FindEntityType(typeof(SequenceEntity))!
            .FindProperty(nameof(SequenceEntity.Id))!;
        var command = CreateCommand(
            EntityState.Added,
            Column("Id", property: id, read: true, key: true),
            Column("Name", value: "序列", write: true),
            Column("DefaultText", read: true),
            Column("Computed", read: true));
        var sql = new StringBuilder();

        var mapping = generator.AppendInsertOperation(
            sql,
            command,
            commandPosition: 0,
            out var requiresTransaction);

        Assert.Equal(ResultSetMapping.LastInResultSet, mapping);
        Assert.True(requiresTransaction);
        AssertSql(
            """
            INSERT INTO "APP"."Widgets" ("Name")
            VALUES (:p0);
            SELECT "Id", "DefaultText", "Computed"
            FROM "APP"."Widgets"
            WHERE SQL%ROWCOUNT = 1 AND "Id" = "APP"."WidgetSequence".CURRVAL;
            """,
            sql);
    }

    [Fact]
    public void DeleteReturnsOneOnlyWhenExactlyOneRowWasAffected()
    {
        using var context = CreateContext();
        var generator = context.GetService<IUpdateSqlGenerator>();
        var command = CreateCommand(
            EntityState.Deleted,
            Column("Id", originalValue: 7L, key: true, condition: true),
            Column("Version", originalValue: 3, condition: true));
        var sql = new StringBuilder();

        var mapping = generator.AppendDeleteOperation(
            sql,
            command,
            commandPosition: 0,
            out var requiresTransaction);

        Assert.Equal(
            ResultSetMapping.LastInResultSet | ResultSetMapping.ResultSetWithRowsAffectedOnly,
            mapping);
        Assert.False(requiresTransaction);
        AssertSql(
            """
            DELETE FROM "APP"."Widgets"
            WHERE "Id" = :p0 AND "Version" = :p1;
            /*EFCOREROWCOUNT*/SELECT SQL%ROWCOUNT;
            """,
            sql);
    }

    private static TestContext CreateContext()
        => new(
            new DbContextOptionsBuilder<TestContext>()
                .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
                .Options);

    private static ModificationCommand CreateCommand(
        EntityState state,
        params ColumnModificationParameters[] columns)
    {
        var command = new ModificationCommand(
            new NonTrackedModificationCommandParameters(
                tableName: "Widgets",
                schemaName: "APP",
                sensitiveLoggingEnabled: false))
        {
            EntityState = state,
        };

        var parameterIndex = 0;
        foreach (var column in columns)
        {
            command.AddColumnModification(
                column with
                {
                    GenerateParameterName = () => $"p{parameterIndex++}",
                });
        }

        return command;
    }

    private static ColumnModificationParameters Column(
        string name,
        IProperty? property = null,
        object? originalValue = null,
        object? value = null,
        bool read = false,
        bool write = false,
        bool key = false,
        bool condition = false)
        => new()
        {
            ColumnName = name,
            Property = property,
            OriginalValue = originalValue,
            Value = value,
            IsRead = read,
            IsWrite = write,
            IsKey = key,
            IsCondition = condition,
        };

    private static void AssertSql(string expected, StringBuilder actual)
        => Assert.Equal(
            expected.TrimEnd(),
            actual.ToString().TrimEnd(),
            ignoreLineEndingDifferences: true);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);

    private sealed class SequenceContext(DbContextOptions<SequenceContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SequenceEntity>().ToTable("Widgets", "APP");
            modelBuilder.Entity<SequenceEntity>().HasKey(entity => entity.Id);
            modelBuilder.Entity<SequenceEntity>()
                .Property(entity => entity.Id)
                .UseDamengSequence("WidgetSequence", "APP");
        }
    }

    private sealed class SequenceEntity
    {
        public long Id { get; set; }

        public required string Name { get; set; }
    }
}
