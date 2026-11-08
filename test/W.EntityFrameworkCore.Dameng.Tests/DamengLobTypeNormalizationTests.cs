#pragma warning disable EF1001 // Exercise the mapping and driver binding contracts.

using Dm;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using W.EntityFrameworkCore.Dameng.Storage.Internal;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

[Trait("AuditPlan", "024")]
public sealed class DamengLobTypeNormalizationTests
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
        var driver = Assert.IsAssignableFrom<Infrastructure.DamengDriver>(driverBinding.Driver);
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

    private static DbContext CreateContext() => new(new DbContextOptionsBuilder().UseDameng("Server=localhost;Port=5236;User=test;Password=test").Options);

    [Theory]
    [InlineData(" \tNcLoB\n", false, "primary key")]
    [InlineData(" \tNcLoB\n", false, "alternate key")]
    [InlineData(" \tNcLoB\n", false, "index")]
    [InlineData(" \tBlOb\n", true, "primary key")]
    [InlineData(" \tBlOb\n", true, "alternate key")]
    [InlineData(" \tBlOb\n", true, "index")]
    [InlineData("NVARCHAR2(16)", false, "primary key")]
    [InlineData("VARCHAR2(16)", false, "alternate key")]
    [InlineData("VARBINARY(16)", true, "index")]
    public void ModelGuardIdentifiesTheKeyOrIndexProperty(string storeType, bool binary, string role)
    {
        using var context = CreateGuardContext(storeType, binary, role);
        if (storeType.Trim() == storeType)
        {
            Assert.NotNull(context.Model.FindEntityType(typeof(Row)));
            return;
        }

        var error = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains(binary ? "Row.Bytes" : "Row.Text", error.Message, StringComparison.Ordinal);
        Assert.Contains("requires a LOB", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitLobEqualityAndQueryGuardsUseTheSameClassification()
    {
        using var textContext = CreateGuardContext(" \tNcLoB\n", false);
        using var binaryContext = CreateGuardContext(" \tBlOb\n", true);
        var textValue = "达梦";
        byte[] binaryValue = [1, 2, 3];
        Assert.Contains("TEXT_EQUAL", textContext.Set<Row>().Where(row => row.Text == textValue).ToQueryString(),
            StringComparison.Ordinal);
        Assert.Contains("BLOB_EQUAL", binaryContext.Set<Row>().Where(row => row.Bytes == binaryValue).ToQueryString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" \tNcLoB\n", false, true)]
    [InlineData(" \tBlOb\n", true, true)]
    [InlineData("NVARCHAR2(16)", false, false)]
    [InlineData("VARBINARY(16)", true, false)]
    public void OrderingGroupingAndDeduplicationRespectLobSemantics(string storeType, bool binary, bool lob)
    {
        using var context = CreateGuardContext(storeType, binary);
        var rows = context.Set<Row>();
        if (binary)
        {
            AssertQueryShapes(rows.Select(row => row.Bytes), lob);
        }
        else
        {
            AssertQueryShapes(rows.Select(row => row.Text), lob);
        }
    }

    [Fact]
    public void JsonPropertyAccessRemainsUntranslatable()
    {
        using var context = CreateGuardContext("JSON", false);
        Assert.Throws<InvalidOperationException>(() => context.Set<Row>()
            .Where(row => row.Json.GetProperty("value").GetString() == "x").ToQueryString());
    }

    private static void AssertQueryShapes<T>(IQueryable<T> values, bool lob)
    {
        IQueryable[] shapes =
        [
            values.OrderBy(value => value),
            values.GroupBy(value => value).Select(group => group.Count()),
            values.Distinct(),
            values.Union(values),
            values.Intersect(values),
            values.Except(values)
        ];
        foreach (var shape in shapes)
        {
            if (lob)
            {
                Assert.Throws<InvalidOperationException>(() => shape.ToQueryString());
            }
            else
            {
                Assert.NotEmpty(shape.ToQueryString());
            }
        }
    }

    private static GuardContext CreateGuardContext(string storeType, bool binary, string? role = null)
        => new(new DbContextOptionsBuilder<GuardContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .ReplaceService<IModelCacheKeyFactory, GuardCacheKey>()
            .Options, storeType, binary, role);

    private sealed class GuardCacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (((GuardContext)context).Shape, designTime);
    }

    private sealed class GuardContext(DbContextOptions options, string storeType, bool binary, string? role)
        : DbContext(options)
    {
        public (string, bool, string?) Shape => (storeType, binary, role);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<Row>();
            var property = binary ? nameof(Row.Bytes) : nameof(Row.Text);
            row.Property(property).HasColumnType(storeType);
            row.Property(entity => entity.Id).ValueGeneratedNever();
            switch (role)
            {
                case "primary key":
                    row.HasKey(property);
                    break;
                case "alternate key":
                    row.HasAlternateKey(property);
                    break;
                case "index":
                    row.HasIndex(property);
                    break;
            }
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
        public byte[] Bytes { get; set; } = [];
        public JsonElement Json { get; set; }
    }
}
