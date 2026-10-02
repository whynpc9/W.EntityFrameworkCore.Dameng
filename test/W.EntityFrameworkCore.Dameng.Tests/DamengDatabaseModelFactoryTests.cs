using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Metadata.Internal;
using W.EntityFrameworkCore.Dameng.Scaffolding.Internal;
using Xunit;

#pragma warning disable EF1001 // Tests intentionally exercise EF/provider infrastructure contracts.

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengDatabaseModelFactoryTests
{
    [Theory]
    [InlineData("SMALLINT", false)]
    [InlineData("TINYINT", false)]
    [InlineData("DECIMAL(18,0)", false)]
    [InlineData("INT", true)]
    [InlineData("INTEGER", true)]
    [InlineData("BIGINT", true)]
    public void IdentityAnnotationsRequireTheSameClrTypesAsModelFinalization(string storeType, bool supported)
    {
        using var context = new DbContext(new DbContextOptionsBuilder()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test").Options);
        var factory = new DamengDatabaseModelFactory(context.GetService<IRelationalTypeMappingSource>(), context.GetService<ISqlGenerationHelper>());
        var column = new DatabaseColumn { Table = new DatabaseTable { Name = "T" }, Name = "ID", StoreType = storeType };
        if (supported)
        {
            factory.ValidateIdentityColumn(column);
        }
        else
        {
            var error = Assert.Throws<NotSupportedException>(() => factory.ValidateIdentityColumn(column));
            Assert.Contains("'T'", error.Message, StringComparison.Ordinal);
            Assert.Contains("'ID'", error.Message, StringComparison.Ordinal);
            Assert.Contains(storeType, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReferencedUniqueColumnsHaveTheSameGeneratedKeyRestriction()
    {
        var parent = new DatabaseTable { Name = "Parent" };
        var column = new DatabaseColumn { Table = parent, Name = "Code", StoreType = "NUMBER(18,0)", DefaultValueSql = "\"S\".NEXTVAL" };
        var child = new DatabaseTable { Name = "Child" };
        var foreignKey = new DatabaseForeignKey { Table = child, PrincipalTable = parent, Name = "FK" };
        foreignKey.PrincipalColumns.Add(column);
        child.ForeignKeys.Add(foreignKey);
        var error = Assert.Throws<NotSupportedException>(() => DamengDatabaseModelFactory.ValidateKeyGeneration(child));
        Assert.Contains("Parent", error.Message, StringComparison.Ordinal);
        Assert.Contains("Code", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DECIMAL(18,2)", false)]
    [InlineData("NUMBER(18,0)", false)]
    [InlineData("NUMBER(18,0)", true)]
    public void UnsupportedGeneratedPrimaryKeyIdentifiesItsColumn(string storeType, bool composite)
    {
        var table = new DatabaseTable { Name = "T" };
        var column = new DatabaseColumn { Table = table, Name = "GeneratedId", StoreType = storeType, DefaultValueSql = "\"S\".NEXTVAL" };
        table.PrimaryKey = new DatabasePrimaryKey { Table = table, Name = "PK_T" };
        if (composite) table.PrimaryKey.Columns.Add(new DatabaseColumn { Table = table, Name = "Tenant", StoreType = "INT" });
        table.PrimaryKey.Columns.Add(column);
        var error = Assert.Throws<NotSupportedException>(() => DamengDatabaseModelFactory.ValidateKeyGeneration(table));
        Assert.Contains("'T'", error.Message, StringComparison.Ordinal);
        Assert.Contains("GeneratedId", error.Message, StringComparison.Ordinal);
        Assert.Contains(storeType, error.Message, StringComparison.Ordinal);
        Assert.Contains("exclude this table", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryPrimaryKeysAndNonKeyDefaultsAreAllowed(bool usesSequence)
    {
        var table = new DatabaseTable { Name = "T" };
        var id = new DatabaseColumn { Table = table, Name = "Id", StoreType = "BIGINT" };
        if (usesSequence) id[DamengAnnotationNames.ValueGenerationStrategy] = DamengValueGenerationStrategy.Sequence;
        table.PrimaryKey = new DatabasePrimaryKey { Table = table, Name = "PK_T" };
        table.PrimaryKey.Columns.Add(id);
        table.Columns.Add(id);
        table.Columns.Add(new DatabaseColumn { Table = table, Name = "Amount", StoreType = "DECIMAL(18,2)", DefaultValueSql = "\"S\".NEXTVAL" });
        DamengDatabaseModelFactory.ValidateKeyGeneration(table);
    }

    [Theory]
    [InlineData("INT", true)]
    [InlineData("BIGINT", true)]
    [InlineData("DECIMAL(18,2)", false)]
    [InlineData("NUMBER(18,0)", false)]
    public void LocalSequenceDefaultsUseCompatibleStrategiesAndResolvedSchema(string storeType, bool usesStrategy)
    {
        using var context = new DbContext(new DbContextOptionsBuilder()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test").Options);
        var factory = new DamengDatabaseModelFactory(context.GetService<IRelationalTypeMappingSource>(), context.GetService<ISqlGenerationHelper>());
        var column = new DatabaseColumn { Name = "C", StoreType = storeType };
        factory.ApplyLocalSequenceDefault(column, new DatabaseSequence { Name = "Seq\"X", Schema = "Other.Schema" });
        if (usesStrategy)
        {
            Assert.Equal(DamengValueGenerationStrategy.Sequence, column[DamengAnnotationNames.ValueGenerationStrategy]);
            Assert.Equal("Other.Schema", column[DamengAnnotationNames.SequenceSchema]);
            Assert.Null(column.DefaultValueSql);
        }
        else
        {
            Assert.Null(column[DamengAnnotationNames.ValueGenerationStrategy]);
            Assert.Equal("\"Other.Schema\".\"Seq\"\"X\".NEXTVAL", column.DefaultValueSql);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("UNKNOWN")]
    public void UnknownCharacterUnitIdentifiesTheObjectColumnAndCatalogValue(string? unit)
    {
        var error = Assert.Throws<NotSupportedException>(() => DamengDatabaseModelFactory.BuildStoreType(
            "VARCHAR", 9, null, null, 9, unit, "Target.View", "ProblemColumn"));
        Assert.Contains("Target.View", error.Message, StringComparison.Ordinal);
        Assert.Contains("ProblemColumn", error.Message, StringComparison.Ordinal);
        Assert.Contains(unit ?? "NULL", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BFILE")]
    [InlineData("TIME WITH TIME ZONE")]
    [InlineData("TIME(3) WITH TIME ZONE")]
    [InlineData("INTERVAL HOUR TO MINUTE")]
    [InlineData("INTERVAL YEAR(4) TO MONTH")]
    [InlineData("UNKNOWN_TYPE")]
    public void UnmappedColumnTypesAreExplicitlyRejected(string storeType)
    {
        using var context = new DbContext(new DbContextOptionsBuilder()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test").Options);
        var factory = new DamengDatabaseModelFactory(context.GetService<IRelationalTypeMappingSource>(), context.GetService<ISqlGenerationHelper>());
        var error = Assert.Throws<NotSupportedException>(() => factory.ValidateColumnType("T", "C", storeType));
        Assert.Contains("'T'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'C'", error.Message, StringComparison.Ordinal);
        Assert.Contains(storeType, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("INT")]
    [InlineData("BLOB")]
    [InlineData("VARCHAR(20 CHAR)")]
    [InlineData("DECIMAL(10,2)")]
    [InlineData("TIMESTAMP(0)")]
    [InlineData("TIMESTAMP(3) WITH TIME ZONE")]
    [InlineData("INTERVAL DAY(4) TO SECOND(3)")]
    public void MappedColumnTypesKeepTheProviderMappingContract(string storeType)
    {
        using var context = new DbContext(new DbContextOptionsBuilder()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test").Options);
        var factory = new DamengDatabaseModelFactory(context.GetService<IRelationalTypeMappingSource>(), context.GetService<ISqlGenerationHelper>());
        factory.ValidateColumnType("T", "C", storeType);
    }

    [Fact]
    public void NativeIdentityTypeIsAcceptedWithoutReadingOtherInfo6Facets()
    {
        var info6 = Enumerable.Repeat((byte)255, 32).ToArray();
        info6[24] = 1;
        info6[25] = 0;
        DamengDatabaseModelFactory.ValidateIdentityType("T", "C", info6);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(1, 1)]
    public void AutoIncrementAndUnknownTypesCannotBecomeIdentity(byte low, byte high)
    {
        var info6 = new byte[26];
        info6[24] = low;
        info6[25] = high;
        var error = Assert.Throws<NotSupportedException>(() => DamengDatabaseModelFactory.ValidateIdentityType("T", "C", info6));
        Assert.Contains("AUTO_INCREMENT", error.Message, StringComparison.Ordinal);
        Assert.Contains("'T'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'C'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(25)]
    public void IncompleteIdentityTypeIsRejected(int? length)
        => Assert.Throws<NotSupportedException>(() => DamengDatabaseModelFactory.ValidateIdentityType(
            "T", "C", length is null ? null : new byte[length.Value]));

    [Theory]
    [InlineData(0L)]
    [InlineData(256L)]
    [InlineData(4294967296L)]
    public void OrdinaryNativeTableKindIgnoresUnrelatedHigherBits(long info3)
        => DamengDatabaseModelFactory.ValidateNativeTableKind("T", info3);

    [Theory]
    [InlineData(0x13L)]
    [InlineData(0x21L)]
    [InlineData(0x27L)]
    [InlineData(0x121L)]
    [InlineData(0x3FL)]
    [InlineData(null)]
    public void HugeOrUnknownNativeTableKindIsRejected(long? info3)
    {
        var error = Assert.Throws<NotSupportedException>(
            () => DamengDatabaseModelFactory.ValidateNativeTableKind("T", info3));
        Assert.Contains("HUGE", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PermanentTableKindCanBeScaffolded()
        => DamengDatabaseModelFactory.ValidateTableKind("T", "N", "NO");

    [Theory]
    [InlineData("Y")]
    [InlineData("UNKNOWN")]
    [InlineData(null)]
    public void TemporaryOrUnknownTableKindIsRejected(string? marker)
        => Assert.Throws<NotSupportedException>(() => DamengDatabaseModelFactory.ValidateTableKind("T", marker, "NO"));

    [Theory]
    [InlineData("YES")]
    [InlineData("UNKNOWN")]
    [InlineData(null)]
    public void PartitionedOrUnknownPermanentTableKindIsRejected(string? marker)
    {
        var error = Assert.Throws<NotSupportedException>(
            () => DamengDatabaseModelFactory.ValidateTableKind("T", "N", marker));
        Assert.Contains("partition definitions", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NORMAL", true)]
    [InlineData("CLUSTER", false)]
    public void OnlyNormalStandaloneIndexesAreReadAsColumnIndexes(string type, bool expected)
        => Assert.Equal(expected, DamengDatabaseModelFactory.ShouldReadIndexColumns("T", "IX_T", type));

    [Theory]
    [InlineData("NORMAL", "P", false)]
    [InlineData("NORMAL", "U", false)]
    [InlineData("NORMAL", "F", true)]
    [InlineData("VIRTUAL", "F", false)]
    public void ConstraintIndexesKeepTheirCatalogRole(string type, string constraintType, bool readColumns)
        => Assert.Equal(readColumns, DamengDatabaseModelFactory.ShouldReadIndexColumns("T", "IX_T", type, constraintType));

    [Fact]
    public void ConstraintAssociationDoesNotMakeBitmapIndexesSupported()
        => Assert.Throws<NotSupportedException>(
            () => DamengDatabaseModelFactory.ShouldReadIndexColumns("T", "IX_T", "BITMAP", constraintType: "F"));

    [Theory]
    [InlineData("VIRTUAL")]
    [InlineData("BITMAP")]
    [InlineData("FUNCTION-BASED BITMAP")]
    [InlineData("FUNCTION-BASED NORMAL")]
    [InlineData("UNKNOWN")]
    [InlineData(null)]
    public void SpecializedOrUnknownIndexTypeIsRejected(string? type)
    {
        var error = Assert.Throws<NotSupportedException>(
            () => DamengDatabaseModelFactory.ShouldReadIndexColumns("T", "IX_T", type));
        Assert.Contains("IX_T", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryIndexColumnPositionCanBeScaffolded()
        => DamengDatabaseModelFactory.ValidateIndexColumnPosition("T", "IX_T", 1);

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(null)]
    public void ExpressionOrUnknownIndexColumnPositionIsRejected(long? position)
    {
        var error = Assert.Throws<NotSupportedException>(
            () => DamengDatabaseModelFactory.ValidateIndexColumnPosition("T", "IX_T", position));
        Assert.Contains("'T'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'IX_T'", error.Message, StringComparison.Ordinal);
    }

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
        => DamengDatabaseModelFactory.ValidateConstraintState("T", "PK_T", "ENABLED", "NOT DEFERRABLE", "IMMEDIATE", "VALIDATED");

    [Theory]
    [InlineData("DISABLED")]
    [InlineData("UNKNOWN")]
    [InlineData(null)]
    public void UnsupportedConstraintStateFailsExplicitly(string? status)
    {
        var error = Assert.Throws<NotSupportedException>(
            () => DamengDatabaseModelFactory.ValidateConstraintState("T", "PK_T", status, "NOT DEFERRABLE", "IMMEDIATE", "VALIDATED"));
        Assert.Contains("PK_T", error.Message, StringComparison.Ordinal);
        Assert.Contains("Exclude this table", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    // Character types keep their declared length semantics.
    [InlineData("VARCHAR", 30, null, null, 30, "B", "VARCHAR(30 BYTE)")]
    [InlineData("CHAR", 9, null, null, 9, "B", "CHAR(9 BYTE)")]
    [InlineData("VARCHAR2", 9, null, null, 9, "B", "VARCHAR2(9 BYTE)")]
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
    [InlineData("DEFERRABLE", "IMMEDIATE", "VALIDATED")]
    [InlineData("NOT DEFERRABLE", "DEFERRED", "VALIDATED")]
    [InlineData("NOT DEFERRABLE", "IMMEDIATE", "NOT VALIDATED")]
    [InlineData(null, "IMMEDIATE", "VALIDATED")]
    [InlineData("NOT DEFERRABLE", null, "VALIDATED")]
    [InlineData("NOT DEFERRABLE", "IMMEDIATE", null)]
    public void UnsupportedConstraintFacetsAreRejected(string? deferrable, string? deferred, string? validated)
    {
        var error = Assert.Throws<NotSupportedException>(() => DamengDatabaseModelFactory.ValidateConstraintState(
            "T", "C", "ENABLED", deferrable, deferred, validated));
        Assert.Contains("'T'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'C'", error.Message, StringComparison.Ordinal);
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
