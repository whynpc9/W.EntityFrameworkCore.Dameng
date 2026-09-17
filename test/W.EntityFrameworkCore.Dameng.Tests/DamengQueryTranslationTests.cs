#pragma warning disable EF1001

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Query.Internal;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengQueryTranslationTests
{
    [Fact]
    public void PaginationUsesQuotedIdentifiersColonParametersAndOffsetFetch()
    {
        using var context = CreateContext();
        var skip = 2;
        var take = 5;
        var name = "达梦";

        var sql = context.Entities
            .Where(entity => entity.Name == name)
            .OrderBy(entity => entity.Id)
            .Skip(skip)
            .Take(take)
            .ToQueryString();

        Assert.Contains("\"Entities\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"Name\"", sql, StringComparison.Ordinal);
        Assert.Contains("-- :name='达梦'", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET :p ROWS FETCH NEXT :p2 ROWS ONLY", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratorKeepsFromlessSelectFromless()
    {
        using var context = CreateContext();
        var typeMapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int));
        var select = new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SelectExpression(
            new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlConstantExpression(1, typeof(int), typeMapping),
            new SqlAliasManager());
        select.ApplyProjection();

        var command = context.GetService<IQuerySqlGeneratorFactory>()
            .Create()
            .GetCommand(select);

        Assert.Equal("SELECT 1", command.CommandText);
        Assert.DoesNotContain("FROM", command.CommandText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StringMembersAndMethodsUseDamengFunctions()
    {
        using var context = CreateContext();
        var contains = "梦";

        var sql = context.Entities
            .Where(
                entity => entity.Name.Contains(contains)
                    && entity.Name.StartsWith("达梦")
                    && entity.Name.EndsWith("数据库"))
            .Select(
                entity => new
                {
                    entity.Name.Length,
                    Segment = entity.Name.Substring(1, 2),
                    Replaced = entity.Name.Replace("达", "大")
                })
            .ToQueryString();

        Assert.Contains("INSTR(", sql, StringComparison.Ordinal);
        Assert.Contains("RIGHT(", sql, StringComparison.Ordinal);
        Assert.Contains("LENGTH(", sql, StringComparison.Ordinal);
        Assert.Contains("SUBSTR(", sql, StringComparison.Ordinal);
        Assert.Contains("REPLACE(", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DateTimeMembersAndIntegralAddsUseDamengFunctions()
    {
        using var context = CreateContext();

        var sql = context.Entities
            .Where(
                entity => entity.CreatedAt.Year == 2026
                    && entity.CreatedAt < DateTime.UtcNow.AddSeconds(30))
            .ToQueryString();

        Assert.Contains("DATEPART(year", sql, StringComparison.Ordinal);
        Assert.Contains("DATEADD(second", sql, StringComparison.Ordinal);
        Assert.Contains("SYS_EXTRACT_UTC(CURRENT_TIMESTAMP())", sql, StringComparison.Ordinal);
        Assert.Contains("CAST(30.0 AS INT)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void GuidNewGuidUsesVerifiedDamengNewIdFunction()
    {
        using var context = CreateContext();

        var sql = context.Entities
            .Where(entity => entity.Token != Guid.NewGuid())
            .ToQueryString();

        Assert.Contains("NEWID()", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void KnownFractionalDateAddIsNotTranslated()
    {
        using var context = CreateContext();

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Entities
                .Where(entity => entity.CreatedAt < DateTime.UtcNow.AddSeconds(1.5))
                .ToQueryString());

        Assert.Contains(nameof(DateTime.AddSeconds), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParameterizedDoubleDateAddIsNotSilentlyTruncated()
    {
        using var context = CreateContext();
        var seconds = 1.5;

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Entities
                .Where(entity => entity.CreatedAt < DateTime.UtcNow.AddSeconds(seconds))
                .ToQueryString());

        Assert.Contains(nameof(DateTime.AddSeconds), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BooleanValuesAreConvertedWhenUsedAsSearchConditions()
    {
        using var context = CreateContext();

        var sql = context.Entities
            .Where(entity => entity.IsActive && !entity.IsDeleted)
            .ToQueryString();

        Assert.Contains("\"IsActive\" = 1", sql, StringComparison.Ordinal);
        Assert.Contains("\"IsDeleted\" = 1", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchConditionsAreConvertedWhenProjectedAsValues()
    {
        using var context = CreateContext();

        var sql = context.Entities
            .Select(
                entity => context.Entities
                    .Any(other => other.Id == entity.Id))
            .ToQueryString();

        Assert.Contains("CASE", sql, StringComparison.Ordinal);
        Assert.Contains("WHEN EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("THEN 1", sql, StringComparison.Ordinal);
        Assert.Contains("ELSE 0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertedBooleanValuesUseTheirProviderLiteralsInPredicates()
    {
        using var context = CreateContext();

        var sql = context.Entities
            .Where(
                entity => entity.ConvertedIntFlag
                    && !entity.ConvertedTextFlag)
            .ToQueryString();

        Assert.Contains("\"ConvertedIntFlag\" = 1", sql, StringComparison.Ordinal);
        Assert.Contains("TEXT_EQUAL(", sql, StringComparison.Ordinal);
        Assert.Contains("\"ConvertedTextFlag\"", sql, StringComparison.Ordinal);
        Assert.Contains("'N'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void LobEqualityUsesDamengNullSafeComparisonFunctions()
    {
        using var context = CreateContext();
        var text = "达梦大字段";
        var payload = new byte[] { 0x01, 0xA5, 0xFF };

        var sql = context.Entities
            .Where(
                entity => entity.LargeText == text
                    && entity.Payload == payload)
            .ToQueryString();

        Assert.Contains("TEXT_EQUAL(", sql, StringComparison.Ordinal);
        Assert.Contains("BLOB_EQUAL(", sql, StringComparison.Ordinal);
        Assert.Contains("CASE", sql, StringComparison.Ordinal);
        Assert.Contains("END = 1", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"LargeText\" = ", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Payload\" = ", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void LobNullableColumnEqualityKeepsEfNullGuardsAroundNullPropagatingFunction()
    {
        using var context = CreateContext();

        var sql = context.Entities
            .Where(entity => entity.LargeText == entity.OtherLargeText)
            .Select(entity => entity.Id)
            .ToQueryString();

        Assert.Contains("CASE", sql, StringComparison.Ordinal);
        Assert.Contains("\"LargeText\" IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"OtherLargeText\" IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("TEXT_EQUAL(", sql, StringComparison.Ordinal);
        Assert.Contains("END = 1", sql, StringComparison.Ordinal);
        Assert.Contains(
            "\"e\".\"LargeText\" IS NULL AND \"e\".\"OtherLargeText\" IS NULL",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LobInequalityUsesDamengNullSafeComparisonFunctions()
    {
        using var context = CreateContext();

        var sql = context.Entities
            .Where(
                entity => entity.LargeText != entity.OtherLargeText
                    && entity.Payload != entity.OtherPayload)
            .ToQueryString();

        Assert.Contains("TEXT_EQUAL(", sql, StringComparison.Ordinal);
        Assert.Contains("BLOB_EQUAL(", sql, StringComparison.Ordinal);
        Assert.Contains("END = 0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"LargeText\" <> ", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Payload\" <> ", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void LobEqualityPreservesRelationalNullSemantics()
    {
        using var context = CreateContext(useRelationalNulls: true);

        var sql = context.Entities
            .Where(entity => entity.LargeText == entity.OtherLargeText)
            .Select(entity => entity.Id)
            .ToQueryString();

        Assert.Contains("CASE", sql, StringComparison.Ordinal);
        Assert.Contains("\"LargeText\" IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"OtherLargeText\" IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("TEXT_EQUAL(", sql, StringComparison.Ordinal);
        Assert.Contains("END = 1", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void LobStringSearchesUseFunctionsSupportedForClobInputs()
    {
        using var context = CreateContext();
        var pattern = "梦";

        var sql = context.Entities
            .Where(
                entity => entity.LargeText!.Contains(pattern)
                    && entity.LargeText.StartsWith("达梦")
                    && entity.LargeText.EndsWith("据库")
                    && EF.Functions.Like(entity.LargeText, "%数据库%"))
            .ToQueryString();

        Assert.Contains("INSTR(", sql, StringComparison.Ordinal);
        Assert.Contains("RIGHT(", sql, StringComparison.Ordinal);
        Assert.Contains("TEXT_EQUAL(", sql, StringComparison.Ordinal);
        Assert.Contains(" LIKE ", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderByOnLobFailsBeforeSendingInvalidSql()
    {
        using var context = CreateContext();

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Entities
                .OrderBy(entity => entity.LargeText)
                .ToQueryString());

        Assert.Contains("ORDER BY", exception.Message, StringComparison.Ordinal);
        Assert.Contains("NCLOB", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HasMaxLength", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupByOnLobFailsBeforeSendingInvalidSql()
    {
        using var context = CreateContext();

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Entities
                .GroupBy(entity => entity.LargeText)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToQueryString());

        Assert.Contains("GROUP BY", exception.Message, StringComparison.Ordinal);
        Assert.Contains("NCLOB", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DistinctOnLobFailsBeforeSendingInvalidSql()
    {
        using var context = CreateContext();

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Entities
                .Select(entity => entity.Payload)
                .Distinct()
                .ToQueryString());

        Assert.Contains("SELECT DISTINCT", exception.Message, StringComparison.Ordinal);
        Assert.Contains("BLOB", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DistinctSetOperationOnLobFailsBeforeSendingInvalidSql()
    {
        using var context = CreateContext();

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Entities
                .Select(entity => entity.LargeText)
                .Union(context.Entities.Select(entity => entity.OtherLargeText))
                .ToQueryString());

        Assert.Contains("UNION", exception.Message, StringComparison.Ordinal);
        Assert.Contains("NCLOB", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DateTimeOffsetColumnComparisonGeneratesParameterizedSql()
    {
        using var context = CreateContext();
        var cutoff = new DateTimeOffset(2026, 7, 23, 11, 0, 0, TimeSpan.FromHours(8));

        var sql = context.Entities
            .Where(entity => entity.OccurredAtOffset >= cutoff)
            .Select(entity => entity.Id)
            .ToQueryString();

        Assert.Contains("\"OccurredAtOffset\"", sql, StringComparison.Ordinal);
        Assert.Contains(">=", sql, StringComparison.Ordinal);
        Assert.Contains("-- :", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Year")]
    [InlineData("Month")]
    [InlineData("Offset")]
    [InlineData("UtcDateTime")]
    [InlineData("DateTime")]
    [InlineData("Now")]
    [InlineData("UtcNow")]
    [InlineData("AddDays")]
    [InlineData("AddHours")]
    public void DateTimeOffsetQueryMembersAndMethodsAreNotTranslated(string member)
    {
        using var context = CreateContext();

        AssertQueryCannotBeTranslated(
            () => member switch
            {
                "Year" => context.Entities
                    .Where(entity => entity.OccurredAtOffset.Year == 2026)
                    .ToQueryString(),
                "Month" => context.Entities
                    .Where(entity => entity.OccurredAtOffset.Month == 7)
                    .ToQueryString(),
                "Offset" => context.Entities
                    .Where(entity => entity.OccurredAtOffset.Offset == TimeSpan.FromHours(8))
                    .ToQueryString(),
                "UtcDateTime" => context.Entities
                    .Where(entity => entity.OccurredAtOffset.UtcDateTime.Year == 2026)
                    .ToQueryString(),
                "DateTime" => context.Entities
                    .Where(entity => entity.OccurredAtOffset.DateTime.Year == 2026)
                    .ToQueryString(),
                "Now" => context.Entities
                    .Where(entity => entity.OccurredAtOffset <= DateTimeOffset.Now)
                    .ToQueryString(),
                "UtcNow" => context.Entities
                    .Where(entity => entity.OccurredAtOffset <= DateTimeOffset.UtcNow)
                    .ToQueryString(),
                "AddDays" => context.Entities
                    .Where(entity => entity.OccurredAtOffset.AddDays(1) > entity.OccurredAtOffset)
                    .ToQueryString(),
                "AddHours" => context.Entities
                    .Where(entity => entity.OccurredAtOffset.AddHours(2) > entity.OccurredAtOffset)
                    .ToQueryString(),
                _ => throw new ArgumentOutOfRangeException(nameof(member), member, message: null)
            },
            member);
    }

    [Fact]
    public void StringCompareAndCompareToTranslateToSqlComparisonOperators()
    {
        using var context = CreateContext();

#pragma warning disable CA1309 // The default culture Compare overload is the construct under test.
        var compare = context.Entities
            .Where(entity => string.Compare(entity.Name, "达梦") > 0)
            .ToQueryString();
#pragma warning restore CA1309
        var compareTo = context.Entities
            .Where(entity => entity.Name.CompareTo("达梦") > 0)
            .ToQueryString();

        Assert.Contains("\"Name\" > ", compare, StringComparison.Ordinal);
        Assert.Contains("'达梦'", compare, StringComparison.Ordinal);
        Assert.Contains("\"Name\" > ", compareTo, StringComparison.Ordinal);
        Assert.Contains("'达梦'", compareTo, StringComparison.Ordinal);
        Assert.DoesNotContain("COMPARE", compare, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("IsNullOrWhiteSpace")]
    [InlineData("PadLeft")]
    [InlineData("PadRight")]
    [InlineData("TrimChars")]
    [InlineData("Split")]
    [InlineData("CompareOrdinal")]
    [InlineData("ContainsOrdinalIgnoreCase")]
    [InlineData("StartsWithOrdinal")]
    [InlineData("EndsWithOrdinalIgnoreCase")]
    [InlineData("IndexOfOrdinalIgnoreCase")]
#pragma warning disable CA1309, CA1865, CA2249 // Intentionally probe the untranslated overloads.
    public void CommonUntranslatedStringMethodsAreNotTranslated(string member)
    {
        using var context = CreateContext();

        AssertQueryCannotBeTranslated(
            () => member switch
            {
                "IsNullOrWhiteSpace" => context.Entities
                    .Where(entity => string.IsNullOrWhiteSpace(entity.Name))
                    .ToQueryString(),
                "PadLeft" => context.Entities
                    .Where(entity => entity.Name.PadLeft(10) == "       达梦")
                    .ToQueryString(),
                "PadRight" => context.Entities
                    .Where(entity => entity.Name.PadRight(10, '*') == "达梦******")
                    .ToQueryString(),
                "TrimChars" => context.Entities
                    .Where(entity => entity.Name.Trim(' ', '\t') == "达梦")
                    .ToQueryString(),
                "Split" => context.Entities
                    .Where(entity => entity.Name.Split(',').Length > 1)
                    .ToQueryString(),
                "CompareOrdinal" => context.Entities
                    .Where(entity => string.Compare(entity.Name, "达梦", StringComparison.Ordinal) > 0)
                    .ToQueryString(),
                "ContainsOrdinalIgnoreCase" => context.Entities
                    .Where(entity => entity.Name.Contains("DREAM", StringComparison.OrdinalIgnoreCase))
                    .ToQueryString(),
                "StartsWithOrdinal" => context.Entities
                    .Where(entity => entity.Name.StartsWith("达", StringComparison.Ordinal))
                    .ToQueryString(),
                "EndsWithOrdinalIgnoreCase" => context.Entities
                    .Where(entity => entity.Name.EndsWith("MENG", StringComparison.OrdinalIgnoreCase))
                    .ToQueryString(),
                "IndexOfOrdinalIgnoreCase" => context.Entities
                    .Where(entity => entity.Name.IndexOf("X", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToQueryString(),
                _ => throw new ArgumentOutOfRangeException(nameof(member), member, message: null)
            },
            member switch
            {
                "TrimChars" => "Trim",
                "CompareOrdinal" => "Compare",
                "ContainsOrdinalIgnoreCase" => "Contains",
                "StartsWithOrdinal" => "StartsWith",
                "EndsWithOrdinalIgnoreCase" => "EndsWith",
                "IndexOfOrdinalIgnoreCase" => "IndexOf",
                _ => member
            });
    }
#pragma warning restore CA1309, CA1865, CA2249

    [Fact]
    public void JsonElementPropertyAccessIsNotTranslated()
    {
        using var context = CreateContext();

        AssertQueryCannotBeTranslated(
            () => context.Entities
                .Where(entity => entity.Document.GetProperty("database").GetString() == "达梦")
                .ToQueryString(),
            nameof(JsonElement.GetProperty));
    }

    [Fact]
    public void JsonElementWholeValueEqualityGeneratesParameterizedSql()
    {
        using var context = CreateContext();
        var document = JsonSerializer.Deserialize<JsonElement>(
            """{"database":"达梦","efCore":10}""");

        var sql = context.Entities
            .Where(entity => entity.Document.Equals(document))
            .Select(entity => entity.Id)
            .ToQueryString();

        Assert.Contains("\"Document\"", sql, StringComparison.Ordinal);
        Assert.Contains("-- :", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_VALUE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JSON_QUERY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JSON_EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertQueryCannotBeTranslated(Func<string> toSql, string expectedFragment)
    {
        var exception = Assert.Throws<InvalidOperationException>(toSql);

        Assert.Contains("could not be translated", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedFragment, exception.Message, StringComparison.Ordinal);
    }

    private static TestContext CreateContext(bool useRelationalNulls = false)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestContext>();
        optionsBuilder
            .UseDameng(
                "Server=localhost;Port=5236;User=test;Password=test",
                damengOptions =>
                {
                    if (useRelationalNulls)
                    {
                        damengOptions.UseRelationalNulls();
                    }
                })
            .ReplaceService<IQuerySqlGeneratorFactory, DamengQuerySqlGeneratorFactory>()
            .ReplaceService<IMemberTranslatorProvider, DamengMemberTranslatorProvider>()
            .ReplaceService<IMethodCallTranslatorProvider, DamengMethodCallTranslatorProvider>();

        return new TestContext(optionsBuilder.Options);
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<TestEntity> Entities
            => Set<TestEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<TestEntity>(
                entity =>
                {
                    entity.ToTable("Entities", "App");
                    entity.HasKey(item => item.Id);
                    entity.Property(item => item.Name).HasMaxLength(200);
                    entity.Property(item => item.CreatedAt);
                    entity.Property(item => item.OccurredAtOffset);
                    entity.Property(item => item.Document);
                    entity.Property(item => item.Token);
                    entity.Property(item => item.IsActive);
                    entity.Property(item => item.IsDeleted);
                    entity.Property(item => item.ConvertedIntFlag)
                        .HasConversion<int>();
                    entity.Property(item => item.ConvertedTextFlag)
                        .HasConversion(
                            value => value ? "Y" : "N",
                            value => value == "Y");
                });
    }

    private sealed class TestEntity
    {
        public long Id { get; set; }

        public required string Name { get; set; }

        public string? LargeText { get; set; }

        public string? OtherLargeText { get; set; }

        public byte[]? Payload { get; set; }

        public byte[]? OtherPayload { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTimeOffset OccurredAtOffset { get; set; }

        public JsonElement Document { get; set; }

        public Guid Token { get; set; }

        public bool IsActive { get; set; }

        public bool IsDeleted { get; set; }

        public bool ConvertedIntFlag { get; set; }

        public bool ConvertedTextFlag { get; set; }
    }
}
