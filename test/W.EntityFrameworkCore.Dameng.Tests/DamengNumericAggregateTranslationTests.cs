using Microsoft.EntityFrameworkCore;
using W.EntityFrameworkCore.Dameng.TestUtilities;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengNumericAggregateTranslationTests
{
    public static IEnumerable<object[]> QueryNames => NumericAggregateQueryCases.Names.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(QueryNames))]
    public void AggregateShapesRemainServerQueries(string name)
    {
        using var context = CreateContext();
        var sql = NumericAggregateQueryCases.Query(context.Rows, name).ToQueryString();
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
        if (name != "GroupKeys")
        {
            Assert.Matches(@"(COUNT|SUM|AVG|MIN|MAX)\(", sql);
        }
        if (name == "Conditional")
        {
            Assert.Contains("CASE", sql, StringComparison.Ordinal);
            Assert.Contains("AVG(", sql, StringComparison.Ordinal);
        }
        if (name == "HavingPaging")
        {
            Assert.Contains("HAVING", sql, StringComparison.Ordinal);
            Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
            Assert.Contains("FETCH NEXT", sql, StringComparison.Ordinal);
        }
        if (name == "TwoStage")
        {
            Assert.Equal(2, sql.Split("GROUP BY", StringSplitOptions.None).Length - 1);
        }
    }

    [Fact]
    public void DateStringGroupingRejectsUntranslatedDateConversion()
    {
        using var context = CreateContext();
        var error = Assert.Throws<InvalidOperationException>(() =>
            NumericAggregateQueryCases.Query(context.Rows, "DateString").ToQueryString());
        Assert.Contains("DateTime.ToString", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullableDistinctCountRemedyCountsNullPresenceSeparately()
    {
        using var context = CreateContext();
        var sql = NumericAggregateQueryCases.Query(context.Rows, "NullableDistinct").ToQueryString();
        Assert.Contains("COUNT(DISTINCT", sql, StringComparison.Ordinal);
        Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CASE", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DynamicHierarchyKeyUsesMappedColumn()
    {
        using var context = CreateContext();
        var property = nameof(NumericAggregateRow.Department);
        var sql = context.Rows.GroupBy(r => EF.Property<string>(r, property))
            .Select(g => new { g.Key, Count = g.Count(), Sum = g.Sum(r => r.Fee) }).ToQueryString();
        Assert.Contains("\"Department\"", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(", sql, StringComparison.Ordinal);
    }

    private static NumericAggregateContext CreateContext()
        => new(new DbContextOptionsBuilder<NumericAggregateContext>()
            .UseDameng("Server=localhost;User=TEST;Password=unused").Options);
}
