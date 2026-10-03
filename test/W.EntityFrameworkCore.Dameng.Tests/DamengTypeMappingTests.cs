#pragma warning disable EF1001

using System.Data;
using System.Text.Json;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Storage.Internal;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengTypeMappingTests
{
    public static TheoryData<Type, string, DbType?> DefaultMappings
        => new()
        {
            { typeof(bool), "BIT", DbType.Boolean },
            { typeof(byte), "SMALLINT", DbType.Int16 },
            { typeof(sbyte), "TINYINT", DbType.SByte },
            { typeof(short), "SMALLINT", DbType.Int16 },
            { typeof(ushort), "INT", DbType.Int32 },
            { typeof(int), "INT", DbType.Int32 },
            { typeof(uint), "BIGINT", DbType.Int64 },
            { typeof(long), "BIGINT", DbType.Int64 },
            { typeof(ulong), "DECIMAL(20,0)", DbType.Decimal },
            { typeof(float), "REAL", DbType.Single },
            { typeof(double), "FLOAT", DbType.Double },
            { typeof(decimal), "DECIMAL", DbType.Decimal },
            { typeof(char), "NVARCHAR2(1)", DbType.String },
            { typeof(string), "NCLOB", DbType.String },
            { typeof(byte[]), "BLOB", DbType.Binary },
            { typeof(Guid), "CHAR(36)", DbType.Guid },
            { typeof(DateOnly), "DATE", DbType.Date },
            { typeof(TimeOnly), "TIME(6)", DbType.Time },
            { typeof(DateTime), "TIMESTAMP(7)", DbType.DateTime2 },
            { typeof(DateTimeOffset), "DATETIME(7) WITH TIME ZONE", DbType.DateTimeOffset },
            { typeof(TimeSpan), "INTERVAL DAY(9) TO SECOND(6)", null },
            { typeof(JsonElement), "JSON", DbType.String }
        };

    [Theory]
    [InlineData("FLOAT(7)")]
    [InlineData("FLOAT(24)")]
    [InlineData("FLOAT(53)")]
    public void ExplicitFloatPrecisionRemainsInStoreType(string storeType)
    {
        using var context = CreateContext();
        var mapping = GetMappingSource(context).FindMapping(storeType);
        Assert.NotNull(mapping);
        Assert.Equal(storeType, mapping.StoreType);
    }

    [Theory]
    [MemberData(nameof(DefaultMappings))]
    public void ClrTypesHaveRangeSafeDefaultMappings(Type clrType, string storeType, DbType? dbType)
    {
        using var context = CreateContext();
        var mapping = GetMappingSource(context).FindMapping(clrType);

        Assert.NotNull(mapping);
        Assert.Equal(storeType, mapping.StoreType);
        Assert.Equal(dbType, mapping.DbType);
    }

    [Fact]
    public void UnsignedMappingsConvertToProviderTypesThatCoverTheFullRange()
    {
        using var context = CreateContext();
        using var command = new DmCommand();
        var source = GetMappingSource(context);

        AssertProviderValue(source, command, byte.MaxValue, (short)byte.MaxValue);
        AssertProviderValue(source, command, ushort.MaxValue, (int)ushort.MaxValue);
        AssertProviderValue(source, command, uint.MaxValue, (long)uint.MaxValue);
        AssertProviderValue(source, command, ulong.MaxValue, (decimal)ulong.MaxValue);
    }

    [Fact]
    public void StringFacetsSelectUnicodeAnsiFixedAndLobStoreTypesAtTheBoundary()
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);

        AssertStringMapping(source, unicode: true, fixedLength: false, 32767, "NVARCHAR2(32767)", DbType.String);
        AssertStringMapping(source, unicode: true, fixedLength: false, 32768, "NCLOB", DbType.String);
        // Non-Unicode character semantics budget four bytes per character against the
        // smallest supported page's conservative 1900-byte limit: 475 chars fit, 476 use CLOB.
        AssertStringMapping(source, unicode: false, fixedLength: false, 475, "VARCHAR2(475 CHAR)", DbType.AnsiString);
        AssertStringMapping(source, unicode: false, fixedLength: true, 475, "CHAR(475 CHAR)", DbType.AnsiStringFixedLength);
        AssertStringMapping(source, unicode: false, fixedLength: false, 476, "CLOB", DbType.AnsiString);
        AssertStringMapping(source, unicode: false, fixedLength: false, 1000, "CLOB", DbType.AnsiString);
        AssertStringMapping(source, unicode: false, fixedLength: false, 2047, "CLOB", DbType.AnsiString);
        AssertStringMapping(source, unicode: true, fixedLength: true, 12, "NCHAR(12)", DbType.StringFixedLength);
        AssertStringMapping(source, unicode: false, fixedLength: true, 12, "CHAR(12 CHAR)", DbType.AnsiStringFixedLength);
        Assert.Null(source.FindMapping(
            typeof(string), storeTypeName: null, unicode: false, size: 476, fixedLength: true));
        Assert.Throws<NotSupportedException>(
            () => source.FindMapping(
                typeof(string),
                storeTypeName: null,
                unicode: true,
                size: 32768,
                fixedLength: true));
    }

    [Fact]
    public void InferredOversizedFixedAnsiPropertyIsRejectedInsteadOfConvertedToBinary()
    {
        var options = new DbContextOptionsBuilder<OversizedFixedAnsiContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test").Options;
        using var context = new OversizedFixedAnsiContext(options);
        Assert.Throws<NotSupportedException>(() => _ = context.Model);
    }

    private sealed class OversizedFixedAnsiContext(DbContextOptions<OversizedFixedAnsiContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity("TooWide").HasNoKey().Property<string>("Value")
                .IsUnicode(false).IsFixedLength().HasMaxLength(476);
    }

    [Theory]
    [InlineData("VARCHAR2(2047 CHAR)", 2047)]
    [InlineData("CHAR(476 CHAR)", 476)]
    public void ExplicitStoreTypesRetainTheirInstanceSpecificCapacity(string storeType, int size)
    {
        using var context = CreateContext();
        var mapping = GetMappingSource(context).FindMapping(storeType);
        Assert.NotNull(mapping);
        Assert.Equal(storeType, mapping.StoreType);
        Assert.Equal(size, mapping.Size);
    }

    [Fact]
    public void UnboundedStringsAndBinaryUseLobMappings()
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);

        var unicode = source.FindMapping(typeof(string));
        var ansi = source.FindMapping(
            typeof(string),
            storeTypeName: null,
            unicode: false);
        var binary = source.FindMapping(typeof(byte[]));

        Assert.NotNull(unicode);
        Assert.Equal("NCLOB", unicode.StoreType);
        Assert.Null(unicode.Size);
        Assert.NotNull(ansi);
        Assert.Equal("CLOB", ansi.StoreType);
        Assert.Null(ansi.Size);
        Assert.NotNull(binary);
        Assert.Equal("BLOB", binary.StoreType);
        Assert.Null(binary.Size);
    }

    [Fact]
    public void KeyStringsUseBoundedIndexSafeDefaults()
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);

        var unicode = source.FindMapping(typeof(string), null, keyOrIndex: true, unicode: true);
        var ansi = source.FindMapping(typeof(string), null, keyOrIndex: true, unicode: false);

        Assert.NotNull(unicode);
        Assert.Equal("NVARCHAR2(450)", unicode.StoreType);
        Assert.NotNull(ansi);
        Assert.Equal("VARCHAR2(450 CHAR)", ansi.StoreType);
    }

    [Fact]
    public void CharQualifiedAnsiStoreTypeParsesAndRoundTrips()
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);

        var mapping = source.FindMapping("VARCHAR2(20 CHAR)");
        Assert.NotNull(mapping);
        Assert.Equal("VARCHAR2(20 CHAR)", mapping.StoreType);
        Assert.Equal(20, mapping.Size);
        Assert.False(mapping.IsUnicode);

        var fixedMapping = source.FindMapping("CHAR(5 CHAR)");
        Assert.NotNull(fixedMapping);
        Assert.Equal("CHAR(5 CHAR)", fixedMapping.StoreType);
        Assert.Equal(5, fixedMapping.Size);

        // Unicode declarations are already character-based and stay unchanged.
        var unicode = source.FindMapping("NVARCHAR2(20)");
        Assert.NotNull(unicode);
        Assert.Equal("NVARCHAR2(20)", unicode.StoreType);
    }

    [Theory]
    [InlineData("VARCHAR(9 BYTE)")]
    [InlineData("VARCHAR2(9 BYTE)")]
    [InlineData("CHAR(9 BYTE)")]
    [InlineData("varchar( 9 byte )")]
    [InlineData("VARCHAR2 (9 BYTE)")]
    [InlineData("CHAR  (9 BYTE)")]
    [InlineData("varchar\t(9 byte)")]
    public void ByteQualifiedStoreTypesKeepTheirUnitAndLength(string storeType)
    {
        using var context = CreateContext();
        var mapping = GetMappingSource(context).FindMapping(storeType);
        Assert.NotNull(mapping);
        Assert.Equal(storeType, mapping.StoreType);
        Assert.Equal(9, mapping.Size);
        Assert.False(mapping.IsUnicode);
        Assert.True(DamengTypeMappingSource.RequiresByteLengthSemantics(storeType));
        Assert.False(DamengTypeMappingSource.RequiresByteLengthSemantics("VARCHAR(9 CHAR)"));
    }

    [Theory]
    [InlineData("VARCHAR2 (9 CHAR)")]
    [InlineData("CHAR  (9 CHAR)")]
    [InlineData("varchar\t(9 char)")]
    public void CharacterLengthSemanticsAllowWhitespaceBeforeParenthesis(string storeType)
    {
        using var context = CreateContext();
        var mapping = GetMappingSource(context).FindMapping(storeType);
        Assert.NotNull(mapping);
        Assert.Equal(storeType, mapping.StoreType);
        Assert.Equal(9, mapping.Size);
        Assert.False(mapping.IsUnicode);
    }

    [Theory]
    [InlineData("NVARCHAR2(9 BYTE)")]
    [InlineData("NVARCHAR2 (9 BYTE)")]
    [InlineData("INT (9 BYTE)")]
    [InlineData("NCHAR(9 BYTE)")]
    [InlineData("INT(9 BYTE)")]
    public void ByteQualifierSupportDoesNotExpandToOtherStoreTypes(string storeType)
    {
        using var context = CreateContext();
        Assert.Null(GetMappingSource(context).FindMapping(storeType));
    }

    [Fact]
    public void BinaryFacetsSwitchToBlobOnlyAboveTheInlineLimit()
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);

        var inline = source.FindMapping(typeof(byte[]), null, size: 32767);
        var lob = source.FindMapping(typeof(byte[]), null, size: 32768);
        var fixedLength = source.FindMapping(typeof(byte[]), null, size: 16, fixedLength: true);
        var key = source.FindMapping(typeof(byte[]), null, keyOrIndex: true);
        var rowVersion = source.FindMapping(typeof(byte[]), null, rowVersion: true);

        Assert.NotNull(inline);
        Assert.Equal("VARBINARY(32767)", inline.StoreType);
        Assert.NotNull(lob);
        Assert.Equal("BLOB", lob.StoreType);
        Assert.NotNull(fixedLength);
        Assert.Equal("BINARY(16)", fixedLength.StoreType);
        Assert.NotNull(key);
        Assert.Equal("VARBINARY(900)", key.StoreType);
        Assert.NotNull(rowVersion);
        Assert.Equal("BINARY(8)", rowVersion.StoreType);
        Assert.Throws<NotSupportedException>(
            () => source.FindMapping(
                typeof(byte[]),
                storeTypeName: null,
                size: 32768,
                fixedLength: true));
    }

    [Fact]
    public void DecimalFacetsArePreservedInMappingAndParameter()
    {
        using var context = CreateContext();
        using var command = new DmCommand();
        var source = GetMappingSource(context);
        var precisionOnlyMapping = source.FindMapping(
            typeof(decimal),
            storeTypeName: null,
            precision: 38);
        var mapping = source.FindMapping(typeof(decimal), null, precision: 38, scale: 20);

        Assert.NotNull(precisionOnlyMapping);
        Assert.Equal("DECIMAL(38)", precisionOnlyMapping.StoreType);
        Assert.Equal(38, precisionOnlyMapping.Precision);
        Assert.Equal(0, precisionOnlyMapping.Scale);
        Assert.NotNull(mapping);
        Assert.Equal("DECIMAL(38,20)", mapping.StoreType);
        Assert.Equal(38, mapping.Precision);
        Assert.Equal(20, mapping.Scale);

        var parameter = mapping.CreateParameter(command, "p", 123.456m);
        Assert.Equal((byte)38, parameter.Precision);
        Assert.Equal((byte)20, parameter.Scale);
    }

    [Fact]
    public void DefaultDecimalMappingPreservesHighScaleValues()
    {
        using var context = CreateContext();
        var mapping = GetMappingSource(context).FindMapping(typeof(decimal));
        const decimal value = 0.1234567890123456789012345678m;

        Assert.NotNull(mapping);
        Assert.Equal("DECIMAL", mapping.StoreType);
        Assert.Null(mapping.Precision);
        Assert.Null(mapping.Scale);
        Assert.Equal(
            "0.1234567890123456789012345678",
            mapping.GenerateSqlLiteral(value));
    }

    [Fact]
    public void DecimalScaffoldingCanProbeScaleWithoutPrecision()
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);
        var probe = source.FindMapping(typeof(decimal), storeTypeName: null, precision: null, scale: 2);
        Assert.NotNull(probe);
        Assert.Equal("DECIMAL", probe.StoreType);
        Assert.Null(probe.Precision);
        Assert.Null(probe.Scale);
        var explicitMapping = source.FindMapping(typeof(decimal), storeTypeName: null, precision: 18, scale: 2);
        Assert.NotNull(explicitMapping);
        Assert.Equal("DECIMAL(18,2)", explicitMapping.StoreType);
    }

    [Theory]
    [InlineData(39, null)]
    [InlineData(10, 11)]
    public void InvalidDecimalFacetsAreRejected(int precision, int? scale)
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);

        var exception = Assert.Throws<InvalidOperationException>(
            () => source.FindMapping(
                typeof(decimal),
                storeTypeName: null,
                precision: precision,
                scale: scale));

        Assert.Contains("Dameng DECIMAL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitStoreTypesRetainSpellingAndFacets()
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);

        var number = source.FindMapping(typeof(decimal), "NUMBER(38,20)");
        var time = source.FindMapping(typeof(TimeOnly), "TIME(3)");
        var timestamp = source.FindMapping(typeof(DateTimeOffset), "TIMESTAMP(6) WITH TIME ZONE");
        var interval = source.FindMapping(typeof(TimeSpan), "INTERVAL DAY(4) TO SECOND(3)");
        var intervalWithDefaultDayPrecision =
            source.FindMapping(typeof(TimeSpan), "INTERVAL DAY TO SECOND(3)");

        Assert.NotNull(number);
        Assert.Equal("NUMBER(38,20)", number.StoreType);
        Assert.Equal(38, number.Precision);
        Assert.Equal(20, number.Scale);
        Assert.NotNull(time);
        Assert.Equal("TIME(3)", time.StoreType);
        Assert.Equal(3, time.Precision);
        Assert.NotNull(timestamp);
        Assert.Equal("TIMESTAMP(6) WITH TIME ZONE", timestamp.StoreType);
        Assert.Equal(6, timestamp.Precision);
        Assert.NotNull(interval);
        Assert.Equal("INTERVAL DAY(4) TO SECOND(3)", interval.StoreType);
        Assert.Equal(4, interval.Precision);
        Assert.Equal(3, interval.Scale);
        Assert.NotNull(intervalWithDefaultDayPrecision);
        Assert.Equal(
            "INTERVAL DAY TO SECOND(3)",
            intervalWithDefaultDayPrecision.StoreType);
        Assert.Null(intervalWithDefaultDayPrecision.Precision);
        Assert.Equal(3, intervalWithDefaultDayPrecision.Scale);
    }

    [Fact]
    public void TemporalPrecisionFacetsGenerateDamengStoreTypeSyntax()
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);

        var timeWithoutFraction = source.FindMapping(
            typeof(TimeOnly),
            storeTypeName: null,
            precision: 0);
        var timeWithMicroseconds = source.FindMapping(
            typeof(TimeOnly),
            storeTypeName: null,
            precision: 6);
        var timestamp = source.FindMapping(
            typeof(DateTime),
            storeTypeName: null,
            precision: 6);
        var timestampWithTimeZone = source.FindMapping(
            typeof(DateTimeOffset),
            storeTypeName: null,
            precision: 6);
        var interval = source.FindMapping(
            typeof(TimeSpan),
            storeTypeName: null,
            precision: 9,
            scale: 6);

        Assert.NotNull(timeWithoutFraction);
        Assert.Equal("TIME(0)", timeWithoutFraction.StoreType);
        Assert.Equal(0, timeWithoutFraction.Precision);
        Assert.NotNull(timeWithMicroseconds);
        Assert.Equal("TIME(6)", timeWithMicroseconds.StoreType);
        Assert.Equal(6, timeWithMicroseconds.Precision);
        Assert.NotNull(timestamp);
        Assert.Equal("TIMESTAMP(6)", timestamp.StoreType);
        Assert.Equal(6, timestamp.Precision);
        Assert.NotNull(timestampWithTimeZone);
        Assert.Equal("DATETIME(6) WITH TIME ZONE", timestampWithTimeZone.StoreType);
        Assert.Equal(6, timestampWithTimeZone.Precision);
        Assert.NotNull(interval);
        Assert.Equal("INTERVAL DAY(9) TO SECOND(6)", interval.StoreType);
        Assert.Equal(9, interval.Precision);
        Assert.Equal(6, interval.Scale);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void TimestampPrecisionSupportsDamengBoundaryValues(int precision)
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);

        var timestamp = source.FindMapping(
            typeof(DateTime),
            storeTypeName: null,
            precision: precision);
        var timestampWithTimeZone = source.FindMapping(
            typeof(DateTimeOffset),
            storeTypeName: null,
            precision: precision);

        Assert.NotNull(timestamp);
        Assert.Equal($"TIMESTAMP({precision})", timestamp.StoreType);
        Assert.Equal(precision, timestamp.Precision);
        Assert.NotNull(timestampWithTimeZone);
        Assert.Equal(
            $"DATETIME({precision}) WITH TIME ZONE",
            timestampWithTimeZone.StoreType);
        Assert.Equal(precision, timestampWithTimeZone.Precision);
    }

    [Fact]
    public void ModelTemporalPrecisionFacetsGenerateDamengStoreTypeSyntax()
    {
        using var context = CreateTemporalFacetContext();
        var entityType = context.Model.FindEntityType(typeof(TemporalFacetEntity))!;

        Assert.Equal(
            "TIME(6)",
            entityType.FindProperty(nameof(TemporalFacetEntity.DefaultTime))!
                .GetRelationalTypeMapping()
                .StoreType);
        Assert.Equal(
            "TIME(0)",
            entityType.FindProperty(nameof(TemporalFacetEntity.TimeWithoutFraction))!
                .GetRelationalTypeMapping()
                .StoreType);
        Assert.Equal(
            "TIME(3)",
            entityType.FindProperty(nameof(TemporalFacetEntity.ExplicitTime))!
                .GetRelationalTypeMapping()
                .StoreType);
        Assert.Equal(
            "TIMESTAMP(7)",
            entityType.FindProperty(nameof(TemporalFacetEntity.DefaultTimestamp))!
                .GetRelationalTypeMapping()
                .StoreType);
        Assert.Equal(
            "DATETIME(7) WITH TIME ZONE",
            entityType.FindProperty(nameof(TemporalFacetEntity.DefaultTimestampWithTimeZone))!
                .GetRelationalTypeMapping()
                .StoreType);
        Assert.Equal(
            "TIMESTAMP(6)",
            entityType.FindProperty(nameof(TemporalFacetEntity.Timestamp))!
                .GetRelationalTypeMapping()
                .StoreType);
        Assert.Equal(
            "DATETIME(6) WITH TIME ZONE",
            entityType.FindProperty(nameof(TemporalFacetEntity.TimestampWithTimeZone))!
                .GetRelationalTypeMapping()
                .StoreType);
        Assert.Equal(
            "INTERVAL DAY(9) TO SECOND(6)",
            entityType.FindProperty(nameof(TemporalFacetEntity.Interval))!
                .GetRelationalTypeMapping()
                .StoreType);
        Assert.Equal(
            "TIMESTAMP(3)",
            entityType.FindProperty(nameof(TemporalFacetEntity.ExplicitTimestamp))!
                .GetRelationalTypeMapping()
                .StoreType);
        Assert.Equal(
            "DATETIME(3) WITH TIME ZONE",
            entityType.FindProperty(nameof(TemporalFacetEntity.ExplicitTimestampWithTimeZone))!
                .GetRelationalTypeMapping()
                .StoreType);
        Assert.Equal(
            "INTERVAL DAY(4) TO SECOND(3)",
            entityType.FindProperty(nameof(TemporalFacetEntity.ExplicitInterval))!
                .GetRelationalTypeMapping()
                .StoreType);
        Assert.Equal(
            "DECIMAL",
            entityType.FindProperty(nameof(TemporalFacetEntity.DefaultDecimal))!
                .GetRelationalTypeMapping()
                .StoreType);
        Assert.Equal(
            "DECIMAL(38)",
            entityType.FindProperty(nameof(TemporalFacetEntity.DecimalWithPrecision))!
                .GetRelationalTypeMapping()
                .StoreType);
        Assert.Equal(
            "DECIMAL(38,20)",
            entityType.FindProperty(nameof(TemporalFacetEntity.DecimalWithPrecisionAndScale))!
                .GetRelationalTypeMapping()
                .StoreType);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void TimeOnlyPrecisionOutsideDamengRangeIsRejected(int precision)
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);

        var exception = Assert.Throws<InvalidOperationException>(
            () => source.FindMapping(
                typeof(TimeOnly),
                storeTypeName: null,
                precision: precision));
        var explicitStoreTypeException = Assert.Throws<InvalidOperationException>(
            () => source.FindMapping(
                typeof(TimeOnly),
                $"TIME({precision})"));

        Assert.Contains("between 0 and 6", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "between 0 and 6",
            explicitStoreTypeException.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(DateTime), -1)]
    [InlineData(typeof(DateTime), 10)]
    [InlineData(typeof(DateTimeOffset), -1)]
    [InlineData(typeof(DateTimeOffset), 10)]
    public void TimestampPrecisionOutsideDamengRangeIsRejected(
        Type clrType,
        int precision)
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);

        var exception = Assert.Throws<InvalidOperationException>(
            () => source.FindMapping(
                clrType,
                storeTypeName: null,
                precision: precision));

        Assert.Contains("between 0 and 9", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidTimeOnlyPrecisionIsRejectedWhileBuildingTheEfModel()
    {
        var options = new DbContextOptionsBuilder<InvalidTemporalFacetContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .Options;
        using var context = new InvalidTemporalFacetContext(options);

        var exception = Assert.Throws<InvalidOperationException>(
            () => _ = context.Model);

        Assert.Contains("between 0 and 6", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidIntervalFractionalSecondPrecisionIsRejected()
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);

        var exception = Assert.Throws<InvalidOperationException>(
            () => source.FindMapping(
                typeof(TimeSpan),
                storeTypeName: null,
                precision: 9,
                scale: 7));

        Assert.Contains("between 0 and 6", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LossyOrReadOnlyStoreTypesAreNotMappedToWritableClrTypes()
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);

        Assert.Null(
            source.FindMapping(
                typeof(DateTimeOffset),
                "TIME WITH TIME ZONE"));
        Assert.Null(source.FindMapping(typeof(byte[]), "BFILE"));
        Assert.Null(
            source.FindMapping(
                typeof(TimeSpan),
                "INTERVAL HOUR TO MINUTE"));

        var timeOptions =
            new DbContextOptionsBuilder<UnsupportedTimeWithTimeZoneContext>()
                .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
                .Options;
        var bfileOptions = new DbContextOptionsBuilder<UnsupportedBfileContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .Options;
        var intervalOptions =
            new DbContextOptionsBuilder<UnsupportedIntervalContext>()
                .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
                .Options;
        using var timeContext = new UnsupportedTimeWithTimeZoneContext(timeOptions);
        using var bfileContext = new UnsupportedBfileContext(bfileOptions);
        using var intervalContext = new UnsupportedIntervalContext(intervalOptions);

        Assert.Throws<InvalidOperationException>(() => _ = timeContext.Model);
        Assert.Throws<InvalidOperationException>(() => _ = bfileContext.Model);
        Assert.Throws<InvalidOperationException>(() => _ = intervalContext.Model);
    }

    [Fact]
    public void DamengParametersUseLobBinaryIntervalAndJsonTypes()
    {
        using var context = CreateContext();
        using var command = new DmCommand();
        var source = GetMappingSource(context);

        var clobMapping = source.FindMapping(typeof(string), null, unicode: true, size: 32768)!;
        var clobParameter = Assert.IsType<DmParameter>(
            clobMapping.CreateParameter(command, "clob", "达梦"));
        Assert.Equal(DmDbType.Clob, clobParameter.DmSqlType);

        var varbinaryMapping = source.FindMapping(typeof(byte[]), null, size: 128)!;
        var varbinaryParameter = Assert.IsType<DmParameter>(
            varbinaryMapping.CreateParameter(command, "binary", new byte[] { 1, 2, 3 }));
        Assert.Equal(DmDbType.VarBinary, varbinaryParameter.DmSqlType);
        Assert.Equal(128, varbinaryParameter.Size);

        var blobMapping = source.FindMapping(typeof(byte[]), null, size: 32768)!;
        var blobParameter = Assert.IsType<DmParameter>(
            blobMapping.CreateParameter(command, "blob", new byte[] { 1, 2, 3 }));
        Assert.Equal(DmDbType.Blob, blobParameter.DmSqlType);

        var intervalMapping = source.FindMapping(typeof(TimeSpan))!;
        var intervalParameter = Assert.IsType<DmParameter>(
            intervalMapping.CreateParameter(command, "interval", TimeSpan.FromMinutes(90)));
        Assert.Equal(DmDbType.IntervalDayToSecond, intervalParameter.DmSqlType);

        using var document = JsonDocument.Parse("""{"name":"达梦","enabled":true}""");
        var jsonMapping = source.FindMapping(typeof(JsonElement))!;
        var jsonParameter = Assert.IsType<DmParameter>(
            jsonMapping.CreateParameter(command, "json", document.RootElement));
        Assert.Equal(DmDbType.VarChar, jsonParameter.DmSqlType);
        Assert.Equal("""{"name":"达梦","enabled":true}""", jsonParameter.Value);
    }

    [Fact]
    public void LiteralsUseDamengCompatibleJsonBinaryAndIntervalSyntax()
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);
        using var document = JsonDocument.Parse("""{"text":"O'Reilly"}""");

        Assert.Equal(
            """'{"text":"O''Reilly"}'""",
            source.FindMapping(typeof(JsonElement))!.GenerateSqlLiteral(document.RootElement));
        Assert.Equal(
            "0x00A5FF",
            source.FindMapping(typeof(byte[]))!.GenerateSqlLiteral(new byte[] { 0, 0xA5, 0xFF }));
        Assert.Equal(
            "INTERVAL '-2 03:04:05.6' DAY(9) TO SECOND(6)",
            source.FindMapping(typeof(TimeSpan))!.GenerateSqlLiteral(
                -new TimeSpan(days: 2, hours: 3, minutes: 4, seconds: 5, milliseconds: 600)));
        Assert.Equal(
            "INTERVAL '123 04:05:06.789' DAY(9) TO SECOND(6)",
            source.FindMapping(typeof(TimeSpan))!.GenerateSqlLiteral(
                new TimeSpan(days: 123, hours: 4, minutes: 5, seconds: 6, milliseconds: 789)));
        Assert.Equal(
            "INTERVAL '2 03:04:05.6' DAY TO SECOND(3)",
            source.FindMapping(
                    typeof(TimeSpan),
                    "INTERVAL DAY TO SECOND(3)")!
                .GenerateSqlLiteral(
                    new TimeSpan(days: 2, hours: 3, minutes: 4, seconds: 5, milliseconds: 600)));
    }

    [Fact]
    public void TemporalLiteralsPreserveTheSeventhFractionalSecondDigit()
    {
        using var context = CreateContext();
        var source = GetMappingSource(context);
        var timestamp = new DateTime(2026, 7, 24, 13, 14, 15).AddTicks(1);
        var timestampWithTimeZone =
            new DateTimeOffset(
                    2026,
                    7,
                    24,
                    13,
                    14,
                    15,
                    TimeSpan.FromHours(8))
                .AddTicks(1);

        Assert.Equal(
            "TIMESTAMP '2026-07-24 13:14:15.0000001'",
            source.FindMapping(typeof(DateTime))!.GenerateSqlLiteral(timestamp));
        Assert.Equal(
            "TIMESTAMP '2026-07-24 13:14:15.0000001+08:00'",
            source.FindMapping(typeof(DateTimeOffset))!
                .GenerateSqlLiteral(timestampWithTimeZone));
    }

    [Fact]
    public void TimeOnlyMappingRejectsValuesThatWouldLoseFractionalSeconds()
    {
        using var context = CreateContext();
        using var command = new DmCommand();
        var source = GetMappingSource(context);
        var microsecondMapping = source.FindMapping(typeof(TimeOnly))!;
        var millisecondMapping = source.FindMapping(
            typeof(TimeOnly),
            storeTypeName: null,
            precision: 3)!;
        var oneMicrosecond = new TimeOnly(13, 14, 15).Add(TimeSpan.FromTicks(10));
        var oneTick = new TimeOnly(13, 14, 15).Add(TimeSpan.FromTicks(1));
        var oneMillisecond = new TimeOnly(13, 14, 15, 1);

        Assert.Equal(
            "TIME '13:14:15.000001'",
            microsecondMapping.GenerateSqlLiteral(oneMicrosecond));
        Assert.Equal(
            "TIME '13:14:15.001'",
            millisecondMapping.GenerateSqlLiteral(oneMillisecond));
        Assert.Equal(
            (byte)6,
            microsecondMapping.CreateParameter(command, "aligned", oneMicrosecond).Scale);
        Assert.Throws<ArgumentException>(
            () => microsecondMapping.GenerateSqlLiteral(oneTick));
        Assert.Throws<ArgumentException>(
            () => microsecondMapping.CreateParameter(command, "literalLoss", oneTick));
        Assert.Throws<ArgumentException>(
            () => millisecondMapping.GenerateSqlLiteral(oneMicrosecond));
    }

    [Fact]
    public void TimeSpanMappingRejectsValuesThatWouldLoseFractionalSeconds()
    {
        using var context = CreateContext();
        using var command = new DmCommand();
        var source = GetMappingSource(context);
        var microsecondMapping = source.FindMapping(typeof(TimeSpan))!;
        var millisecondMapping = source.FindMapping(
            typeof(TimeSpan),
            storeTypeName: null,
            precision: 9,
            scale: 3)!;
        var oneMicrosecond = TimeSpan.FromTicks(10);
        var oneTick = TimeSpan.FromTicks(1);
        var oneMillisecond = TimeSpan.FromMilliseconds(1);

        Assert.Equal(
            "INTERVAL '0 00:00:00.000001' DAY(9) TO SECOND(6)",
            microsecondMapping.GenerateSqlLiteral(oneMicrosecond));
        Assert.Equal(
            "INTERVAL '0 00:00:00.001' DAY(9) TO SECOND(3)",
            millisecondMapping.GenerateSqlLiteral(oneMillisecond));
        Assert.Equal(
            (byte)6,
            microsecondMapping.CreateParameter(command, "aligned", oneMicrosecond).Scale);
        Assert.Throws<ArgumentException>(
            () => microsecondMapping.GenerateSqlLiteral(oneTick));
        Assert.Throws<ArgumentException>(
            () => microsecondMapping.CreateParameter(command, "parameterLoss", oneTick));
        Assert.Throws<ArgumentException>(
            () => millisecondMapping.GenerateSqlLiteral(oneMicrosecond));
    }

    [Theory]
    [InlineData(
        "INTERVAL '000000123 04:05:06.789000' DAY(9) TO SECOND(6)",
        123,
        4,
        5,
        6,
        789,
        false)]
    [InlineData(
        "INTERVAL '-000000123 04:05:06.789000' DAY(9) TO SECOND(6)",
        123,
        4,
        5,
        6,
        789,
        true)]
    public void IntervalReaderTextParsesWithoutDriverSpecificTypes(
        string value,
        int days,
        int hours,
        int minutes,
        int seconds,
        int milliseconds,
        bool negative)
    {
        var expected = new TimeSpan(
            days,
            hours,
            minutes,
            seconds,
            milliseconds);

        Assert.Equal(
            negative ? -expected : expected,
            DamengTimeSpanTypeMapping.Parse(value));
    }

    [Fact]
    public void DateTimeOffsetReaderTextPreservesTheOriginalOffset()
        => Assert.Equal(
            new DateTimeOffset(
                2026,
                7,
                23,
                14,
                15,
                16,
                TimeSpan.FromHours(8)),
            DamengDateTimeOffsetTypeMapping.Parse(
                "2026-07-23 14:15:16.000000 +08:00"));

    private static MappingContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MappingContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .Options;
        return new MappingContext(options);
    }

    private static TemporalFacetContext CreateTemporalFacetContext()
    {
        var options = new DbContextOptionsBuilder<TemporalFacetContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .Options;
        return new TemporalFacetContext(options);
    }

    private static IRelationalTypeMappingSource GetMappingSource(DbContext context)
        => context.GetService<IRelationalTypeMappingSource>();

    private static void AssertProviderValue<TModel, TProvider>(
        IRelationalTypeMappingSource source,
        DmCommand command,
        TModel modelValue,
        TProvider expectedProviderValue)
        where TModel : struct
        where TProvider : struct
    {
        var mapping = source.FindMapping(typeof(TModel));

        Assert.NotNull(mapping);
        Assert.Equal(typeof(TProvider), mapping.Converter?.ProviderClrType);
        var parameter = mapping.CreateParameter(command, "p", modelValue);
        Assert.IsType<TProvider>(parameter.Value);
        Assert.Equal(expectedProviderValue, parameter.Value);
    }

    private static void AssertStringMapping(
        IRelationalTypeMappingSource source,
        bool unicode,
        bool fixedLength,
        int size,
        string expectedStoreType,
        DbType expectedDbType)
    {
        var mapping = source.FindMapping(
            typeof(string),
            null,
            unicode: unicode,
            size: size,
            fixedLength: fixedLength);

        Assert.NotNull(mapping);
        Assert.Equal(expectedStoreType, mapping.StoreType);
        Assert.Equal(expectedDbType, mapping.DbType);
        Assert.Equal(unicode, mapping.IsUnicode);
        Assert.Equal(expectedStoreType is not "CLOB" and not "NCLOB", mapping.Size.HasValue);
    }

    private sealed class MappingContext(DbContextOptions<MappingContext> options) : DbContext(options);

    private sealed class TemporalFacetContext(
        DbContextOptions<TemporalFacetContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TemporalFacetEntity>(
                entity =>
                {
                    entity.HasKey(item => item.Id);
                    entity.Property(item => item.TimeWithoutFraction).HasPrecision(0);
                    entity.Property(item => item.Timestamp).HasPrecision(6);
                    entity.Property(item => item.TimestampWithTimeZone).HasPrecision(6);
                    entity.Property(item => item.Interval).HasPrecision(9, 6);
                    entity.Property(item => item.ExplicitTime)
                        .HasColumnType("TIME(3)");
                    entity.Property(item => item.ExplicitTimestamp)
                        .HasColumnType("TIMESTAMP(3)");
                    entity.Property(item => item.ExplicitTimestampWithTimeZone)
                        .HasColumnType("DATETIME(3) WITH TIME ZONE");
                    entity.Property(item => item.ExplicitInterval)
                        .HasColumnType("INTERVAL DAY(4) TO SECOND(3)");
                    entity.Property(item => item.DecimalWithPrecision)
                        .HasPrecision(38);
                    entity.Property(item => item.DecimalWithPrecisionAndScale)
                        .HasPrecision(38, 20);
                });
        }
    }

    private sealed class InvalidTemporalFacetContext(
        DbContextOptions<InvalidTemporalFacetContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<TemporalFacetEntity>()
                .Property(item => item.TimeWithoutFraction)
                .HasPrecision(7);
    }

    private sealed class UnsupportedTimeWithTimeZoneContext(
        DbContextOptions<UnsupportedTimeWithTimeZoneContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<UnsupportedTimeWithTimeZoneEntity>(
                entity =>
                {
                    entity.HasKey(item => item.Id);
                    entity.Property(item => item.Value)
                        .HasColumnType("TIME WITH TIME ZONE");
                });
    }

    private sealed class UnsupportedBfileContext(
        DbContextOptions<UnsupportedBfileContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<UnsupportedBfileEntity>(
                entity =>
                {
                    entity.HasKey(item => item.Id);
                    entity.Property(item => item.Value)
                        .HasColumnType("BFILE");
                });
    }

    private sealed class UnsupportedIntervalContext(
        DbContextOptions<UnsupportedIntervalContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<UnsupportedIntervalEntity>(
                entity =>
                {
                    entity.HasKey(item => item.Id);
                    entity.Property(item => item.Value)
                        .HasColumnType("INTERVAL HOUR TO MINUTE");
                });
    }

    private sealed class UnsupportedTimeWithTimeZoneEntity
    {
        public int Id { get; set; }

        public DateTimeOffset Value { get; set; }
    }

    private sealed class UnsupportedBfileEntity
    {
        public int Id { get; set; }

        public required byte[] Value { get; set; }
    }

    private sealed class UnsupportedIntervalEntity
    {
        public int Id { get; set; }

        public TimeSpan Value { get; set; }
    }

    private sealed class TemporalFacetEntity
    {
        public int Id { get; set; }

        public TimeOnly DefaultTime { get; set; }

        public TimeOnly TimeWithoutFraction { get; set; }

        public TimeOnly ExplicitTime { get; set; }

        public DateTime DefaultTimestamp { get; set; }

        public DateTimeOffset DefaultTimestampWithTimeZone { get; set; }

        public DateTime Timestamp { get; set; }

        public DateTimeOffset TimestampWithTimeZone { get; set; }

        public TimeSpan Interval { get; set; }

        public DateTime ExplicitTimestamp { get; set; }

        public DateTimeOffset ExplicitTimestampWithTimeZone { get; set; }

        public TimeSpan ExplicitInterval { get; set; }

        public decimal DefaultDecimal { get; set; }

        public decimal DecimalWithPrecision { get; set; }

        public decimal DecimalWithPrecisionAndScale { get; set; }
    }
}
