using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengNullableTemporalToStringTranslationTests
{
#pragma warning disable CA1305 // Exercise the server-side overloads without a culture argument.
    [Fact]
    public void NullableIdentityToStringTranslatesInRequiredServerPositions()
    {
        using var context = CreateContext();
        AssertNullableIdentityMapping(context);
        AssertServerPositions(context, r => r.NullableIdentity.ToString(), "COALESCE(CAST(", "AS VARCHAR(100)");
        AssertServerPositions(context, r => r.NullableIdentity!.Value.ToString(), "CAST(", "AS VARCHAR(100)");

        foreach (var (format, serverFormat) in new[]
        {
            ("yyyy", "YYYY"),
            ("yyyy-MM", "YYYY-MM"),
            ("yyyy-MM-dd", "YYYY-MM-DD"),
            ("yyyy-MM-dd HH:mm:ss", "YYYY-MM-DD HH24:MI:SS"),
            ("yyyy-MM-dd HH:mm:ss.fffffff", "YYYY-MM-DD HH24:MI:SS.FF7"),
            ("s", "YYYY-MM-DD\"T\"HH24:MI:SS"),
            ("yyyy-MM-dd'T'HH:mm:ss", "YYYY-MM-DD\"T\"HH24:MI:SS")
        })
        {
            AssertServerPositions(context, FormatSelector(nameof(Row.NullableIdentity), format), "TO_CHAR(", $"'{serverFormat}'");
        }
    }

    [Fact]
    public void NullableIdentityRetainsDistinctNullContracts()
    {
        using var context = CreateContext();
        AssertNullableIdentityMapping(context);
        var direct = context.Rows.Select(r => r.NullableIdentity.ToString()).ToQueryString();
        var value = context.Rows.Select(r => r.NullableIdentity!.Value.ToString()).ToQueryString();
        Assert.Contains("COALESCE(CAST(", direct, StringComparison.Ordinal);
        Assert.Contains("AS VARCHAR(100)), '')", direct, StringComparison.Ordinal);
        Assert.Contains("CAST(", value, StringComparison.Ordinal);
        Assert.Contains("AS VARCHAR(100)", value, StringComparison.Ordinal);
        Assert.DoesNotContain("COALESCE(", value, StringComparison.Ordinal);
    }

    [Fact]
    public void NonNativeToStringRemainsUntranslatedInServerPositions()
    {
        using var context = CreateContext();
        AssertServerPositions(context, r => r.Native.ToString(), "COALESCE(CAST(", "AS VARCHAR(100)");
        AssertServerPositions(context, r => r.Identity.ToString(), "CAST(", "AS VARCHAR(100)");
        foreach (var selector in NonNativeSelectors())
        {
            var text = context.Rows.Select(selector);
            AssertTranslationFailure(() => text.Where(value => value == "2024").ToQueryString());
            AssertTranslationFailure(() => text.GroupBy(value => value).Select(g => g.Count()).ToQueryString());
        }
    }

    [Fact]
    public void NonNativeTopLevelProjectionKeepsClientEvaluationBoundary()
    {
        using var context = CreateContext();
        foreach (var selector in NonNativeSelectors())
        {
            var sql = context.Rows.Select(selector).ToQueryString();
            var call = (MethodCallExpression)selector.Body;
            var property = (MemberExpression)call.Object!;
            var column = property.Member.Name == nameof(Nullable<DateTime>.Value)
                ? ((MemberExpression)property.Expression!).Member.Name
                : property.Member.Name;
            Assert.Contains($"\"{column}\"", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("CAST(", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("TO_CHAR(", sql, StringComparison.Ordinal);
        }
    }

    private static Expression<Func<Row, string?>>[] NonNativeSelectors() =>
    [
        r => r.Ticks.ToString(), r => r.Ticks!.Value.ToString(), FormatSelector(nameof(Row.Ticks), "yyyy"),
        r => r.Text.ToString(), r => r.Text!.Value.ToString(), FormatSelector(nameof(Row.Text), "yyyy")
    ];
#pragma warning restore CA1305

    private static void AssertNullableIdentityMapping(Context context)
    {
        var mapping = context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.NullableIdentity))!
            .GetRelationalTypeMapping();
        Assert.Equal("TIMESTAMP(7)", mapping.StoreType);
        Assert.Equal(typeof(DateTime?), mapping.Converter!.ProviderClrType);
    }

    private static void AssertServerPositions(Context context, Expression<Func<Row, string?>> selector,
        string conversion, string resultTypeOrFormat)
    {
        var text = context.Rows.Select(selector);
        var queries = new (IQueryable Query, string Clause)[]
        {
            (text.Where(value => value == "2024"), "WHERE"),
            (text, "SELECT"),
            (text.OrderBy(value => value), "ORDER BY"),
            (text.GroupBy(value => value).Select(g => new { g.Key, Count = g.Count() }), "GROUP BY")
        };
        foreach (var (query, clause) in queries)
        {
            var sql = query.ToQueryString();
            var start = sql.IndexOf(clause, StringComparison.Ordinal);
            Assert.True(start >= 0, sql);
            // EF may compute the grouping key in a subquery and group by its alias.
            var expressionSql = clause == "GROUP BY" ? sql : sql[start..];
            Assert.Contains(conversion, expressionSql, StringComparison.Ordinal);
            Assert.Contains(resultTypeOrFormat, expressionSql, StringComparison.Ordinal);
            Assert.DoesNotContain("TRUNC(", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("CLOB", sql, StringComparison.Ordinal);
        }
    }

    private static void AssertTranslationFailure(Func<string> query)
    {
        var error = Assert.Throws<InvalidOperationException>(query);
        Assert.Contains("could not be translated", error.Message, StringComparison.Ordinal);
        Assert.Contains("ToString", error.Message, StringComparison.Ordinal);
    }

    private static Expression<Func<Row, string?>> FormatSelector(string property, string format)
    {
        var row = Expression.Parameter(typeof(Row), "row");
        var value = Expression.Property(Expression.Property(row, property), nameof(Nullable<DateTime>.Value));
        var call = Expression.Call(value, nameof(DateTime.ToString), Type.EmptyTypes, Expression.Constant(format));
        return Expression.Lambda<Func<Row, string?>>(call, row);
    }

    private static Context CreateContext() => new(new DbContextOptionsBuilder<Context>()
        .UseDameng((string?)null).Options);

    private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<Row>();
            row.Property(r => r.Native).HasColumnType("TIMESTAMP(7)");
            row.Property(r => r.NullableIdentity).HasConversion(v => v, v => v).HasColumnType("TIMESTAMP(7)");
            row.Property(r => r.Identity).HasConversion(v => v, v => v).HasColumnType("TIMESTAMP(7)");
            row.Property(r => r.Ticks).HasConversion<long>().HasColumnType("BIGINT");
            row.Property(r => r.Text).HasConversion(v => Encode(v!.Value), v => Decode(v)).HasColumnType("NVARCHAR2(64)");
        }
    }

    private static string Encode(DateTime value) => "D:" + value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime Decode(string value) =>
        DateTime.Parse(value[2..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed class Row
    {
        public int Id { get; set; }
        public DateTime? Native { get; set; }
        public DateTime? NullableIdentity { get; set; }
        public DateTime Identity { get; set; }
        public DateTime? Ticks { get; set; }
        public DateTime? Text { get; set; }
    }
}
