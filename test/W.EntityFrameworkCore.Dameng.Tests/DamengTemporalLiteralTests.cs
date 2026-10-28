#pragma warning disable EF1001 // Tests intentionally inspect driver-bound type mappings.
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Storage.Internal;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

[Trait("Plan", "006")]
public sealed class DamengTemporalLiteralTests
{
    [Fact]
    public void TemporalLiteralGenerationRetainsMappingDriverAndFacets()
    {
        using var context = Context();
        var source = context.GetService<IRelationalTypeMappingSource>();
        for (var precision = 6; precision <= 9; precision++)
        foreach (var offset in OffsetModes)
        {
            var mapping = source.FindMapping(offset ? typeof(DateTimeOffset) : typeof(DateTime), null, precision: precision)!;
            var clone = mapping.WithStoreTypeAndSize(mapping.StoreType, null);
            Assert.Equal(precision, clone.Precision);
            Assert.Equal(((IDamengDriverBoundMapping)mapping).Driver, ((IDamengDriverBoundMapping)clone).Driver);
            Assert.Equal("NULL", clone.GenerateSqlLiteral(null));
            object value = offset ? (object)new DateTimeOffset(2024, 2, 29, 13, 14, 15, TimeSpan.FromHours(8)).AddTicks(1234560)
                : new DateTime(2024, 2, 29, 13, 14, 15).AddTicks(1234560);
            Assert.Contains(offset ? "TO_TIMESTAMP_TZ(" : "TO_TIMESTAMP(", clone.GenerateSqlLiteral(value), StringComparison.Ordinal);
            Assert.Contains(mapping.StoreType, clone.GenerateSqlLiteral(value), StringComparison.Ordinal);
        }
    }
    [Fact]
    public void TemporalLiteralUsesInvariantFormatting()
    {
        using var context = Context();
        var source = context.GetService<IRelationalTypeMappingSource>();
        var mapping = source.FindMapping(typeof(DateTimeOffset))!;
        var value = new DateTimeOffset(2024, 2, 29, 13, 14, 15, new TimeSpan(-5, -30, 0)).AddTicks(1234567);
        var expected = mapping.GenerateSqlLiteral(value);
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in Cultures)
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Assert.Equal(expected, mapping.GenerateSqlLiteral(value));
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
        Assert.Contains(".1234567-05:30", expected, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("TIMESTAMP")]
    [InlineData("TIMESTAMP(6)")]
    [InlineData("DATE")]
    public void TemporalLiteralRejectsUnrepresentableTargetPrecision(string storeType)
    {
        using var context = Context();
        var source = context.GetService<IRelationalTypeMappingSource>();
        var mapping = source.FindMapping(typeof(DateTime), storeType)!;
        Assert.Throws<ArgumentException>(() => mapping.GenerateSqlLiteral(new DateTime(2024, 1, 1).AddTicks(1)));
        var offset = source.FindMapping(typeof(DateTimeOffset), "DATETIME(6) WITH TIME ZONE")!;
        Assert.Throws<ArgumentException>(() => offset.GenerateSqlLiteral(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1)));
    }
    private static readonly bool[] OffsetModes = [false, true];
    private static readonly string[] Cultures = ["zh-CN", "en-US", "fr-FR"];
    private static DbContext Context() => new(new DbContextOptionsBuilder()
        .UseDameng("Server=localhost;User=test;Password=test").Options);
}
