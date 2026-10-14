#pragma warning disable EF1001 // Tests intentionally exercise EF/provider infrastructure contracts.

using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using W.Dm;
using W.EntityFrameworkCore.Dameng.Infrastructure.Internal;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.WDriver.Tests;

public sealed class WDriverTypeMappingTests
{
    [Fact]
    public void ParametersUseWProviderTypesForLobBinaryAndInterval()
    {
        var source = MappingSource();
        using var command = new DmCommand();

        Assert.Equal(DmDbType.Clob, CreateParameter(source.FindMapping(typeof(string), null, unicode: true, size: 40000)!, command, "达梦").DmSqlType);
        Assert.Equal(DmDbType.Clob, CreateParameter(source.FindMapping(typeof(string), "CLOB")!, command, "text").DmSqlType);
        Assert.Equal(DmDbType.Blob, CreateParameter(source.FindMapping(typeof(byte[]), null, size: 40000)!, command, new byte[] { 1 }).DmSqlType);
        Assert.Equal(DmDbType.Blob, CreateParameter(source.FindMapping(typeof(byte[]), "BLOB")!, command, new byte[] { 1 }).DmSqlType);
        Assert.Equal(DmDbType.VarBinary, CreateParameter(source.FindMapping(typeof(byte[]), null, size: 16)!, command, new byte[] { 1 }).DmSqlType);
        Assert.Equal(DmDbType.Binary, CreateParameter(source.FindMapping(typeof(byte[]), null, fixedLength: true, size: 16)!, command, new byte[] { 1 }).DmSqlType);

        var interval = CreateParameter(source.FindMapping(typeof(TimeSpan))!, command, TimeSpan.FromMinutes(90));
        Assert.Equal(DmDbType.IntervalDayToSecond, interval.DmSqlType);
        Assert.Equal(0, interval.Precision);
        Assert.Equal(6, interval.Scale);
    }

    [Fact]
    public void LobParametersKeepSizesAndNulls()
    {
        var source = MappingSource();
        using var command = new DmCommand();
        var clob = source.FindMapping(typeof(string), null, unicode: true, size: 40000)!;
        var blob = source.FindMapping(typeof(byte[]), null, size: 40000)!;

        var largeText = CreateParameter(clob, command, new string('x', 40000));
        var nullText = CreateParameter(clob, command, null);
        var nullBytes = CreateParameter(blob, command, null);

        Assert.Equal(-1, largeText.Size);
        Assert.Equal(DBNull.Value, nullText.Value);
        Assert.Equal(DmDbType.Clob, nullText.DmSqlType);
        Assert.Equal(DBNull.Value, nullBytes.Value);
        Assert.Equal(DmDbType.Blob, nullBytes.DmSqlType);
    }

    [Fact]
    public void JsonParametersBindAsTheSameStringTypeAsTheOfficialDriver()
    {
        var source = MappingSource();
        using var command = new DmCommand();
        using var document = JsonDocument.Parse("""{"name":"达梦","enabled":true}""");

        var parameter = CreateParameter(source.FindMapping(typeof(JsonElement))!, command, document.RootElement);
        var clone = parameter.Clone();

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal(DmDbType.VarChar, parameter.DmSqlType);
        Assert.Equal("""{"name":"达梦","enabled":true}""", parameter.Value);
        Assert.Equal(DmDbType.VarChar, clone.DmSqlType);
        Assert.Equal(parameter.Value, clone.Value);
    }

    public static TheoryData<Type, string?, int?, object, int> TemporalCases
        => new()
        {
            { typeof(DateTime), null, null, new DateTime(2026, 10, 14, 1, 2, 3).AddTicks(1234567), 7 },
            { typeof(DateTime), null, 3, new DateTime(2026, 10, 14, 1, 2, 3, 456), 3 },
            { typeof(DateTime), null, 0, new DateTime(2026, 10, 14, 1, 2, 3), 0 },
            { typeof(DateTime), "TIMESTAMP", null, new DateTime(2026, 10, 14, 1, 2, 3), 6 },
            { typeof(DateTime), "TIMESTAMP(7)", null, new DateTime(2026, 10, 14, 1, 2, 3), 7 },
            { typeof(DateTime), "DATE", null, new DateTime(2026, 10, 14), 0 },
            { typeof(DateTimeOffset), null, null, new DateTimeOffset(2026, 10, 14, 1, 2, 3, TimeSpan.FromHours(8)), 7 },
            { typeof(DateTimeOffset), null, 4, new DateTimeOffset(2026, 10, 14, 1, 2, 3, TimeSpan.FromHours(-5)), 4 },
            { typeof(DateTimeOffset), "DATETIME WITH TIME ZONE", null, new DateTimeOffset(2026, 10, 14, 1, 2, 3, TimeSpan.Zero), 6 },
            { typeof(TimeOnly), null, null, new TimeOnly(1, 2, 3), 6 },
            { typeof(TimeOnly), null, 2, new TimeOnly(1, 2, 3), 2 },
            { typeof(TimeSpan), null, null, TimeSpan.FromSeconds(1), 6 }
        };

    [Theory]
    [MemberData(nameof(TemporalCases))]
    public void TemporalParametersUseScaleAndNeverNonZeroPrecision(
        Type clrType,
        string? storeType,
        int? precision,
        object value,
        int expectedScale)
    {
        var source = MappingSource();
        var mapping = storeType is null
            ? source.FindMapping(clrType, null, precision: precision)!
            : source.FindMapping(clrType, storeType)!;
        using var command = new DmCommand();

        var withValue = CreateParameter(mapping, command, value);
        var withNull = CreateParameter(mapping, command, null);
        var reused = CreateParameter(mapping, command, value);
        var cloned = withValue.Clone();

        foreach (var parameter in new[] { withValue, withNull, reused, cloned })
        {
            Assert.Equal(0, parameter.Precision);
            Assert.Equal(expectedScale, parameter.Scale);
        }

        Assert.Equal(DBNull.Value, withNull.Value);
    }

    [Fact]
    public void ModelPrecisionFacetsReachTheParameterScale()
    {
        using var context = new BlogContext(WDriverConfigurationTests.WOptions<BlogContext>());
        var entity = context.Model.FindEntityType(typeof(Blog))!;
        using var command = new DmCommand();

        var precise = entity.FindProperty(nameof(Blog.Precise))!.GetRelationalTypeMapping();
        var created = entity.FindProperty(nameof(Blog.Created))!.GetRelationalTypeMapping();
        var published = entity.FindProperty(nameof(Blog.Published))!.GetRelationalTypeMapping();

        Assert.Equal("TIMESTAMP(3)", precise.StoreType);
        Assert.Equal(3, CreateParameter(precise, command, new DateTime(2026, 1, 1)).Scale);
        Assert.Equal("TIMESTAMP(7)", created.StoreType);
        Assert.Equal(7, CreateParameter(created, command, new DateTime(2026, 1, 1)).Scale);
        Assert.Equal("DATETIME(7) WITH TIME ZONE", published.StoreType);
        Assert.Equal(7, CreateParameter(published, command, DateTimeOffset.UnixEpoch).Scale);
    }

    [Fact]
    public void DecimalPrecisionAndScaleAreUnchanged()
    {
        var source = MappingSource();
        using var command = new DmCommand();

        var parameter = CreateParameter(source.FindMapping(typeof(decimal), null, precision: 18, scale: 4)!, command, 12.3456m);

        Assert.Equal(18, parameter.Precision);
        Assert.Equal(4, parameter.Scale);
    }

    [Fact]
    public void TemporalAndIntervalColumnsUseTypedGetters()
    {
        var source = MappingSource();

        foreach (var clrType in new[] { typeof(DateTimeOffset), typeof(TimeSpan) })
        {
            var method = source.FindMapping(clrType)!.GetDataReaderMethod();
            Assert.Equal(nameof(DbDataReader.GetFieldValue), method.Name);
            Assert.Equal(clrType, method.GetGenericArguments().Single());
        }
    }

    [Fact]
    public void WMappingsRejectParametersOfAnotherDriver()
    {
        var source = MappingSource();
        using var officialCommand = new global::Dm.DmCommand();
        using var sqlCommand = new SqlCommand();
        var clob = source.FindMapping(typeof(string), null, unicode: true, size: 40000)!;

        var official = Assert.Throws<InvalidOperationException>(() => clob.CreateParameter(officialCommand, "p", "x"));
        var sql = Assert.Throws<InvalidOperationException>(() => clob.CreateParameter(sqlCommand, "p", "x"));

        Assert.Equal("Dameng:ForeignParameter", official.Data[DamengDriverErrors.ErrorIdKey]);
        Assert.Equal("Dameng:ForeignParameter", sql.Data[DamengDriverErrors.ErrorIdKey]);
    }

    private static IRelationalTypeMappingSource MappingSource()
    {
        using var context = new BlogContext(WDriverConfigurationTests.WOptions<BlogContext>());
        return context.GetService<IRelationalTypeMappingSource>();
    }

    private static DmParameter CreateParameter(RelationalTypeMapping mapping, DbCommand command, object? value)
        => Assert.IsType<DmParameter>(mapping.CreateParameter(command, "p", value));
}
