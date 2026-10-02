using Microsoft.EntityFrameworkCore.Scaffolding;
using W.EntityFrameworkCore.Dameng.Scaffolding.Internal;
using Xunit;

#pragma warning disable EF1001 // Tests intentionally exercise EF/provider infrastructure contracts.

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengDatabaseModelFactoryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(32)]
    public void OrdinaryColumnFlagsDoNotBlockScaffolding(long flags)
        => DamengDatabaseModelFactory.ValidateColumnGenerationFlags("T", "C", flags);

    [Theory]
    [InlineData(1, "virtual computed column")]
    [InlineData(16, "DEFAULT ON NULL")]
    [InlineData(48, "DEFAULT ON NULL")]
    [InlineData(64, "ON UPDATE")]
    [InlineData(96, "ON UPDATE")]
    public void UnsupportedColumnGenerationIsRejected(long flags, string expected)
    {
        var error = Assert.Throws<NotSupportedException>(
            () => DamengDatabaseModelFactory.ValidateColumnGenerationFlags("T", "C", flags));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(32, false)]
    [InlineData(33, true)]
    public void VirtualColumnMarkerUsesOnlyTheDocumentedBit(long flags, bool expected)
        => Assert.Equal(expected, DamengDatabaseModelFactory.IsVirtualColumnFlags(flags));

    [Theory]
    [InlineData("NORMAL", false)]
    [InlineData("CLUSTER", true)]
    public void PrimaryKeyClusteringIsReadFromTheBackingIndex(string indexType, bool expected)
        => Assert.Equal(expected, DamengDatabaseModelFactory.ReadPrimaryKeyClustering(indexType));

    [Theory]
    [InlineData(null)]
    [InlineData("UNKNOWN")]
    public void UnknownPrimaryKeyClusteringIsRejected(string? indexType)
        => Assert.Throws<NotSupportedException>(() => DamengDatabaseModelFactory.ReadPrimaryKeyClustering(indexType));

    [Fact]
    public void EnabledConstraintStateCanBeScaffolded()
        => DamengDatabaseModelFactory.ValidateConstraintState("T", "PK_T", "ENABLED");

    [Theory]
    [InlineData("DISABLED")]
    [InlineData("UNKNOWN")]
    [InlineData(null)]
    public void UnsupportedConstraintStateFailsExplicitly(string? status)
    {
        var error = Assert.Throws<NotSupportedException>(
            () => DamengDatabaseModelFactory.ValidateConstraintState("T", "PK_T", status));
        Assert.Contains("PK_T", error.Message, StringComparison.Ordinal);
        Assert.Contains("Exclude this table", error.Message, StringComparison.Ordinal);
    }

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
    [InlineData("FLOAT", 8, 53, null, 0, null, "FLOAT(53)")]
    [InlineData("FLOAT", 4, 24, null, 0, null, "FLOAT(24)")]
    [InlineData("FLOAT", 8, 7, null, 0, null, "FLOAT(7)")]
    [InlineData("FLOAT", 8, null, null, 0, null, "FLOAT")]
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
    // The catalog offsets TIMESTAMP WITH LOCAL TIME ZONE scale by 4096.
    [InlineData("TIMESTAMP WITH LOCAL TIME ZONE", 8, null, 4102, 0, null, "TIMESTAMP(6) WITH LOCAL TIME ZONE")]
    [InlineData("TIMESTAMP WITH LOCAL TIME ZONE", 8, null, 4096, 0, null, "TIMESTAMP(0) WITH LOCAL TIME ZONE")]
    [InlineData("TIMESTAMP WITH LOCAL TIME ZONE", 8, null, 4099, 0, null, "TIMESTAMP(3) WITH LOCAL TIME ZONE")]
    [InlineData("TIMESTAMP WITH LOCAL TIME ZONE", 8, null, null, 0, null, "TIMESTAMP WITH LOCAL TIME ZONE")]
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
    [InlineData("OrderSeq.NEXTVAL", "ORDERSEQ", null)]
    [InlineData("\"sales\".\"Order Seq\".NEXTVAL", "Order Seq", "sales")]
    [InlineData("sales.OrderSeq.nextval", "ORDERSEQ", "SALES")]
    [InlineData("  \"OrderSeq\".NEXTVAL  ", "OrderSeq", null)]
    [InlineData("\"Quoted\"\"Seq\".NEXTVAL", "Quoted\"Seq", null)]
    [InlineData("Order$Seq.NEXTVAL", "ORDER$SEQ", null)]
    [InlineData("sales$.Order#Seq.nextval", "ORDER#SEQ", "SALES$")]
    [InlineData("\"sales$\".order#seq.NEXTVAL", "ORDER#SEQ", "sales$")]
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
    [InlineData("app", "APP")]
    [InlineData(" \"app\" ", "app")]
    [InlineData("\"a\"\"b\"", "a\"b")]
    public void NormalizeSchemaIdentifierPreservesDelimitedNames(string input, string expected)
        => Assert.Equal(expected, DamengDatabaseModelFactory.NormalizeIdentifier(input));

    [Theory]
    [InlineData("USERS", null, "USERS")]
    [InlineData("APP.USERS", "APP", "USERS")]
    [InlineData("\"A.B\"", null, "A.B")]
    [InlineData("APP.\"A.B\"", "APP", "A.B")]
    [InlineData("\"MY.SCHEMA\".T", "MY.SCHEMA", "T")]
    [InlineData("\"MY.SCHEMA\".\"T.U\"", "MY.SCHEMA", "T.U")]
    [InlineData("\"WEIRD\"\"NAME\".T", "WEIRD\"NAME", "T")]
    [InlineData("app.\"Quoted\"", "APP", "Quoted")]
    [InlineData("app.users", "APP", "USERS")]
    [InlineData("users", null, "USERS")]
    [InlineData(" \"app\" . users ", "app", "USERS")]
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
