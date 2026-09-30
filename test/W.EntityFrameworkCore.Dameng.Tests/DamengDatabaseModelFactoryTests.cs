using Microsoft.EntityFrameworkCore.Scaffolding;
using W.EntityFrameworkCore.Dameng.Scaffolding.Internal;
using Xunit;

#pragma warning disable EF1001 // Tests intentionally exercise EF/provider infrastructure contracts.

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengDatabaseModelFactoryTests
{
    [Theory]
    // Character types keep their declared length semantics.
    [InlineData("VARCHAR", 30, null, null, 30, "B", "VARCHAR(30)")]
    [InlineData("VARCHAR", 80, null, null, 20, "C", "VARCHAR(20 CHAR)")]
    [InlineData("VARCHAR2", 80, null, null, 20, "C", "VARCHAR2(20 CHAR)")]
    [InlineData("CHAR", 12, null, null, 3, "C", "CHAR(3 CHAR)")]
    [InlineData("NVARCHAR2", 200, null, null, 50, "C", "NVARCHAR2(50)")]
    [InlineData("NCHAR", 8, null, null, 2, "C", "NCHAR(2)")]
    // Decimal facets.
    [InlineData("DECIMAL", 22, 18, 3, 0, null, "DECIMAL(18,3)")]
    [InlineData("DECIMAL", 22, null, null, 0, null, "DECIMAL")]
    [InlineData("NUMERIC", 22, 9, 0, 0, null, "NUMERIC(9,0)")]
    // Temporal scale comes from DATA_SCALE; a declared (0) is preserved because the
    // unqualified type would fall back to the server's default precision.
    [InlineData("DATETIME", 8, null, 6, 0, null, "DATETIME(6)")]
    [InlineData("DATETIME", 8, null, 7, 0, null, "DATETIME(7)")]
    [InlineData("DATETIME", 8, null, 0, 0, null, "DATETIME(0)")]
    [InlineData("TIME", 5, null, 3, 0, null, "TIME(3)")]
    [InlineData("TIME", 5, null, 0, 0, null, "TIME(0)")]
    [InlineData("TIME", 5, null, null, 0, null, "TIME")]
    [InlineData("TIMESTAMP", 8, null, 0, 0, null, "TIMESTAMP(0)")]
    [InlineData("TIMESTAMP", 8, null, 6, 0, null, "TIMESTAMP(6)")]
    [InlineData("DATETIME WITH TIME ZONE", 11, null, 7, 0, null, "DATETIME(7) WITH TIME ZONE")]
    [InlineData("DATETIME WITH TIME ZONE", 11, null, 0, 0, null, "DATETIME(0) WITH TIME ZONE")]
    [InlineData("TIMESTAMP WITH TIME ZONE", 10, null, 6, 0, null, "TIMESTAMP(6) WITH TIME ZONE")]
    [InlineData("TIMESTAMP WITH TIME ZONE", 10, null, 0, 0, null, "TIMESTAMP(0) WITH TIME ZONE")]
    // Interval precision and scale restore the declared facets.
    [InlineData("INTERVAL DAY TO SECOND", 24, 9, 6, 0, null, "INTERVAL DAY(9) TO SECOND(6)")]
    [InlineData("INTERVAL DAY TO SECOND", 24, 4, 3, 0, null, "INTERVAL DAY(4) TO SECOND(3)")]
    [InlineData("INTERVAL YEAR TO MONTH", 12, 3, 6, 0, null, "INTERVAL YEAR(3) TO MONTH")]
    // Binary lengths.
    [InlineData("VARBINARY", 16, null, null, 0, null, "VARBINARY(16)")]
    [InlineData("BINARY", 4, null, null, 0, null, "BINARY(4)")]
    // Facetless and large-object types pass through.
    [InlineData("INT", 4, null, 0, 0, null, "INT")]
    [InlineData("BIGINT", 8, null, 0, 0, null, "BIGINT")]
    [InlineData("BIT", 1, null, 0, 0, null, "BIT")]
    [InlineData("TEXT", 2147483647, null, 0, 0, null, "TEXT")]
    [InlineData("BLOB", 2147483647, null, 0, null, null, "BLOB")]
    public void BuildStoreTypeRestoresFacets(
        string dataType,
        int? dataLength,
        int? dataPrecision,
        int? dataScale,
        int? charLength,
        string? charUsed,
        string expected)
        => Assert.Equal(
            expected,
            DamengDatabaseModelFactory.BuildStoreType(
                dataType,
                dataLength,
                dataPrecision,
                dataScale,
                charLength,
                charUsed));

    [Theory]
    [InlineData("\"OrderSeq\".NEXTVAL", "OrderSeq", null)]
    [InlineData("OrderSeq.NEXTVAL", "OrderSeq", null)]
    [InlineData("\"sales\".\"Order Seq\".NEXTVAL", "Order Seq", "sales")]
    [InlineData("sales.OrderSeq.nextval", "OrderSeq", "sales")]
    [InlineData("  \"OrderSeq\".NEXTVAL  ", "OrderSeq", null)]
    [InlineData("\"Quoted\"\"Seq\".NEXTVAL", "Quoted\"Seq", null)]
    public void TryParseSequenceDefaultMatchesNextvalDefaults(
        string defaultValueSql,
        string expectedName,
        string? expectedSchema)
    {
        var matched = DamengDatabaseModelFactory.TryParseSequenceDefault(
            defaultValueSql,
            out var sequenceName,
            out var sequenceSchema);

        Assert.True(matched);
        Assert.Equal(expectedName, sequenceName);
        Assert.Equal(expectedSchema, sequenceSchema);
    }

    [Theory]
    [InlineData("'NEW'")]
    [InlineData("CURRENT_TIMESTAMP")]
    [InlineData("OrderSeq.CURRVAL")]
    [InlineData("OrderSeq.NEXTVAL + 1")]
    [InlineData("NEXTVAL")]
    [InlineData("")]
    public void TryParseSequenceDefaultRejectsOtherDefaults(string defaultValueSql)
    {
        var matched = DamengDatabaseModelFactory.TryParseSequenceDefault(
            defaultValueSql,
            out _,
            out _);

        Assert.False(matched);
    }

    [Theory]
    [InlineData("USERS", null, "USERS")]
    [InlineData("APP.USERS", "APP", "USERS")]
    [InlineData("\"A.B\"", null, "A.B")]
    [InlineData("APP.\"A.B\"", "APP", "A.B")]
    [InlineData("\"MY.SCHEMA\".T", "MY.SCHEMA", "T")]
    [InlineData("\"MY.SCHEMA\".\"T.U\"", "MY.SCHEMA", "T.U")]
    [InlineData("\"WEIRD\"\"NAME\".T", "WEIRD\"NAME", "T")]
    [InlineData("app.\"Quoted\"", "app", "Quoted")]
    public void SplitQualifiedNameParsesIdentifierComponents(
        string entry,
        string? expectedSchema,
        string expectedName)
    {
        var (schema, name) = DamengDatabaseModelFactory.SplitQualifiedName(entry);

        Assert.Equal(expectedSchema, schema);
        Assert.Equal(expectedName, name);
    }
}
