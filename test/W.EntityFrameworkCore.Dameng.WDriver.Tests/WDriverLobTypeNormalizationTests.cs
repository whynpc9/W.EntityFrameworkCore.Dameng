#pragma warning disable EF1001 // Exercise the mapping and driver binding contracts.

using W.Dm;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using W.EntityFrameworkCore.Dameng.Storage.Internal;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.WDriver.Tests;

[Trait("AuditPlan", "024")]
public sealed class WDriverLobTypeNormalizationTests
{
    public static TheoryData<string, bool, bool> LobSpellings()
    {
        var data = new TheoryData<string, bool, bool>();
        foreach (var (alias, binary, unicode) in new[]
        {
            ("CLOB", false, false), ("NCLOB", false, true), ("TEXT", false, true),
            ("NTEXT", false, true), ("LONG", false, false), ("LONGVARCHAR", false, false),
            ("BLOB", true, false), ("IMAGE", true, false), ("LONGVARBINARY", true, false)
        })
        {
            var lower = alias.ToLowerInvariant();
            var mixed = alias[..1] + lower[1..];
            foreach (var spelling in new[] { alias, lower, mixed, $" {alias} ", $"\t{mixed}\n" })
            {
                data.Add(spelling, binary, unicode);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LobSpellings))]
    public void ExplicitLobKeepsSpellingFacetsAndParameterType(string spelling, bool binary, bool unicode)
    {
        using var context = CreateContext();
        using var command = new DmCommand();
        var source = context.GetService<IRelationalTypeMappingSource>();
        var mapping = Assert.IsAssignableFrom<RelationalTypeMapping>(
            source.FindMapping(binary ? typeof(byte[]) : typeof(string), spelling, size: 123));
        var driverBinding = Assert.IsAssignableFrom<IDamengDriverBoundMapping>(mapping);
        var driver = Assert.IsAssignableFrom<global::W.EntityFrameworkCore.Dameng.Infrastructure.DamengDriver>(driverBinding.Driver);
        Assert.Same(mapping, driverBinding.WithDriver(driver));

        ValueConverter converter = binary
            ? new ValueConverter<byte[], byte[]>(value => value, value => value)
            : new ValueConverter<string, string>(value => value, value => value);
        var converted = Assert.IsAssignableFrom<RelationalTypeMapping>(mapping.WithComposedConverter(converter));
        var facets = new RelationalTypeMappingInfo(spelling, spelling.Trim(), unicode, 234, 7, 3);
        var faceted = converted.WithTypeMappingInfo(facets);
        var resized = faceted.WithStoreTypeAndSize(spelling, 345);

        Assert.Equal(123, mapping.Size);
        Assert.Equal(234, faceted.Size);
        Assert.Equal(345, resized.Size);
        Assert.Same(converter, resized.Converter);
        Assert.Equal(7, resized.Precision);
        Assert.Equal(3, resized.Scale);

        object?[] values = binary
            ? [null, Array.Empty<byte>(), new byte[] { 1, 2 }, new byte[42_000]]
            : [null, "", "达梦", new string('字', 42_000)];
        foreach (var variant in new[] { mapping, converted, faceted, resized })
        {
            Assert.Equal(spelling, variant.StoreType);
            Assert.Equal(unicode, variant.IsUnicode);
            Assert.Same(driver, ((IDamengDriverBoundMapping)variant).Driver);
            foreach (var value in values)
            {
                var parameter = Assert.IsType<DmParameter>(variant.CreateParameter(command, "p", value));
                Assert.Equal(binary ? DmDbType.Blob : DmDbType.Clob, parameter.DmSqlType);
                Assert.Equal(value ?? DBNull.Value, parameter.Value);
                var clone = parameter.Clone();
                Assert.Equal(parameter.DmSqlType, clone.DmSqlType);
                Assert.Equal(parameter.Size, clone.Size);
                Assert.Equal(parameter.Value, clone.Value);
                Assert.Equal(ReferenceEquals(value, values[^1]) ? -1 : DamengTypeMappingSource.MaxInlineLength,
                    parameter.Size);
            }
        }

        var inline = resized.WithStoreTypeAndSize(binary ? "VARBINARY" : "NVARCHAR2", 16);
        var inlineParameter = Assert.IsType<DmParameter>(inline.CreateParameter(command, "p", values[2]));
        Assert.Equal(16, inlineParameter.Size);
        Assert.NotEqual(binary ? DmDbType.Blob : DmDbType.Clob, inlineParameter.DmSqlType);
    }

    [Theory]
    [InlineData("VARCHAR2")]
    [InlineData("NVARCHAR2")]
    [InlineData("CHAR")]
    [InlineData("BINARY")]
    [InlineData("VARBINARY")]
    public void BoundedParametersRetainTheirTypeAndSize(string storeType)
    {
        using var context = CreateContext();
        using var command = new DmCommand();
        var source = context.GetService<IRelationalTypeMappingSource>();
        var binary = storeType is "BINARY" or "VARBINARY";
        var mapping = source.FindMapping(binary ? typeof(byte[]) : typeof(string), storeType, size: 16)!;
        var parameter = Assert.IsType<DmParameter>(
            mapping.CreateParameter(command, "p", binary ? new byte[] { 1 } : "a"));
        Assert.Equal(16, parameter.Size);
        if (binary)
        {
            Assert.Equal(storeType == "BINARY" ? DmDbType.Binary : DmDbType.VarBinary, parameter.DmSqlType);
        }
        else
        {
            Assert.Equal(mapping.DbType, parameter.DbType);
            Assert.NotEqual(DmDbType.Clob, parameter.DmSqlType);
        }
    }

    [Theory]
    [InlineData("JSON")]
    [InlineData(" \tJsOnB\n")]
    public void JsonKeepsVarCharParameterType(string storeType)
    {
        using var context = CreateContext();
        using var command = new DmCommand();
        using var document = JsonDocument.Parse("""{"value":"达梦"}""");
        var source = context.GetService<IRelationalTypeMappingSource>();
        var mapping = source.FindMapping(typeof(JsonElement), storeType)!;
        var parameter = Assert.IsType<DmParameter>(mapping.CreateParameter(command, "p", document.RootElement));
        Assert.Equal(DmDbType.VarChar, parameter.DmSqlType);

        var textMapping = source.FindMapping(typeof(string), storeType)!;
        var textParameter = Assert.IsType<DmParameter>(
            textMapping.CreateParameter(command, "p", document.RootElement.GetRawText()));
        Assert.Equal(DmDbType.VarChar, textParameter.DmSqlType);
    }

    [Fact]
    public void UnsupportedNamesRemainUnmapped()
    {
        using var context = CreateContext();
        var source = context.GetService<IRelationalTypeMappingSource>();
        Assert.Null(source.FindMapping(typeof(byte[]), " BfIlE "));
        Assert.Null(source.FindMapping(typeof(string), "CONTAINS_CLOB"));
    }

    private static DbContext CreateContext() => new(WDriverConfigurationTests.WOptions<DbContext>());
}
