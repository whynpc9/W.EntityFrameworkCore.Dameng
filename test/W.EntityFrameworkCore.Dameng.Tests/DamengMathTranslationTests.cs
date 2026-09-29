using Microsoft.EntityFrameworkCore;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengMathTranslationTests
{
    [Theory]
    [InlineData("AbsInt", "CAST(ABS(", " AS INT)")]
    [InlineData("AbsLong", "ABS(", "\"LONG_VALUE\"")]
    [InlineData("AbsDecimal", "ABS(", "\"DECIMAL_VALUE\"")]
    [InlineData("AbsDouble", "ABS(", "\"DOUBLE_VALUE\"")]
    [InlineData("SignInt", "SIGN(", "\"INT_VALUE\"")]
    [InlineData("SignLong", "SIGN(", "\"LONG_VALUE\"")]
    [InlineData("SignDecimal", "SIGN(", "\"DECIMAL_VALUE\"")]
    [InlineData("SignDouble", "SIGN(", "\"DOUBLE_VALUE\"")]
    [InlineData("FloorDecimal", "FLOOR(", "\"DECIMAL_VALUE\"")]
    [InlineData("FloorDouble", "FLOOR(", "\"DOUBLE_VALUE\"")]
    [InlineData("CeilingDecimal", "CEIL(", "\"DECIMAL_VALUE\"")]
    [InlineData("CeilingDouble", "CEIL(", "\"DOUBLE_VALUE\"")]
    [InlineData("Exp", "EXP(", "\"DOUBLE_VALUE\"")]
    [InlineData("Log", "LN(", "\"DOUBLE_VALUE\"")]
    [InlineData("LogWithBase", "LOG(", "\"LOG_BASE\"")]
    [InlineData("Log10", "LOG10(", "\"DOUBLE_VALUE\"")]
    [InlineData("Pow", "POWER(", "\"EXPONENT\"")]
    [InlineData("Sqrt", "SQRT(", "\"DOUBLE_VALUE\"")]
    public void ExactMathOverloadsTranslateInServerProjection(
        string operation,
        string function,
        string argumentOrCast)
    {
        using var context = CreateContext();

        var sql = ProjectionSql(context, operation);

        Assert.Contains(function, sql, StringComparison.Ordinal);
        Assert.Contains(argumentOrCast, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
        if (operation.Contains("Decimal", StringComparison.Ordinal))
        {
            Assert.DoesNotContain(" AS FLOAT", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" AS DOUBLE", sql, StringComparison.OrdinalIgnoreCase);
        }

        if (operation == "LogWithBase")
        {
            Assert.True(
                sql.IndexOf("\"LOG_BASE\"", StringComparison.Ordinal)
                    < sql.IndexOf("\"DOUBLE_VALUE\"", StringComparison.Ordinal),
                "Dameng LOG(base, value) must reverse Math.Log(value, base).");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FunctionsTranslateInFilterOrderingGroupingAndNullableProjection(
        bool useRelationalNulls)
    {
        using var context = CreateContext(useRelationalNulls);

        var filtered = context.Entities
            .Where(entity => Math.Abs(entity.IntValue) > 3
                && Math.Floor(entity.DecimalValue) <= 8m)
            .Select(entity => entity.Id)
            .ToQueryString();
        Assert.Contains("CAST(ABS(", filtered, StringComparison.Ordinal);
        Assert.Contains("FLOOR(", filtered, StringComparison.Ordinal);

        var ordered = context.Entities
            .OrderBy(entity => Math.Ceiling(entity.DoubleValue))
            .Select(entity => entity.Id)
            .ToQueryString();
        Assert.Contains("ORDER BY CEIL(", ordered, StringComparison.Ordinal);

        var grouped = context.Entities
            .GroupBy(entity => Math.Sign(entity.LongValue))
            .Select(group => new { group.Key, Count = group.Count() })
            .ToQueryString();
        Assert.Contains("SIGN(", grouped, StringComparison.Ordinal);
        Assert.Contains("GROUP BY", grouped, StringComparison.Ordinal);

        var nullable = context.Entities
            .Select(entity => new
            {
                IntAbs = entity.NullableInt.HasValue
                    ? Math.Abs(entity.NullableInt.Value)
                    : (int?)null,
                DecimalFloor = entity.NullableDecimal.HasValue
                    ? Math.Floor(entity.NullableDecimal.Value)
                    : (decimal?)null
            })
            .ToQueryString();
        Assert.Contains("CAST(ABS(", nullable, StringComparison.Ordinal);
        Assert.Contains("FLOOR(", nullable, StringComparison.Ordinal);
        Assert.Contains("IS NOT NULL", nullable, StringComparison.Ordinal);
    }

    [Fact]
    public void OtherNumericOverloadsRemainUntranslated()
    {
        using var context = CreateContext();

        Assert.Throws<InvalidOperationException>(() => context.Entities
            .Where(entity => Math.Abs((float)entity.DoubleValue) > 1f)
            .ToQueryString());
        Assert.Throws<InvalidOperationException>(() => context.Entities
            .Where(entity => Math.Round(entity.DecimalValue) > 1m)
            .ToQueryString());
        Assert.Throws<InvalidOperationException>(() => context.Entities
            .Where(entity => Math.Sin(entity.DoubleValue) > 0d)
            .ToQueryString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Q2FunctionsTranslateInServerFilterOrderingGroupingAndNullableProjection(
        bool useRelationalNulls)
    {
        using var context = CreateContext(useRelationalNulls);

        var filtered = context.Entities
            .Where(entity => Math.Exp(entity.DoubleValue) > 1d
                && Math.Log(entity.DoubleValue) > 0d
                && Math.Log10(entity.DoubleValue) > 0d)
            .Select(entity => entity.Id)
            .ToQueryString();
        Assert.Contains("EXP(", filtered, StringComparison.Ordinal);
        Assert.Contains("LN(", filtered, StringComparison.Ordinal);
        Assert.Contains("LOG10(", filtered, StringComparison.Ordinal);

        var ordered = context.Entities
            .OrderBy(entity => Math.Pow(entity.DoubleValue, entity.Exponent))
            .Select(entity => entity.Id)
            .ToQueryString();
        Assert.Contains("ORDER BY POWER(", ordered, StringComparison.Ordinal);

        var grouped = context.Entities
            .GroupBy(entity => Math.Sqrt(entity.DoubleValue))
            .Select(group => new { group.Key, Count = group.Count() })
            .ToQueryString();
        Assert.Contains("SQRT(", grouped, StringComparison.Ordinal);
        Assert.Contains("GROUP BY", grouped, StringComparison.Ordinal);

        var nullable = context.Entities
            .Select(entity => entity.NullableDouble.HasValue
                ? Math.Log(entity.NullableDouble.Value, entity.LogBase)
                : (double?)null)
            .ToQueryString();
        Assert.Contains("LOG(", nullable, StringComparison.Ordinal);
        Assert.Contains("IS NOT NULL", nullable, StringComparison.Ordinal);
    }

    private static string ProjectionSql(MathContext context, string operation)
        => operation switch
        {
            "AbsInt" => context.Entities.Select(entity => Math.Abs(entity.IntValue)).ToQueryString(),
            "AbsLong" => context.Entities.Select(entity => Math.Abs(entity.LongValue)).ToQueryString(),
            "AbsDecimal" => context.Entities.Select(entity => Math.Abs(entity.DecimalValue)).ToQueryString(),
            "AbsDouble" => context.Entities.Select(entity => Math.Abs(entity.DoubleValue)).ToQueryString(),
            "SignInt" => context.Entities.Select(entity => Math.Sign(entity.IntValue)).ToQueryString(),
            "SignLong" => context.Entities.Select(entity => Math.Sign(entity.LongValue)).ToQueryString(),
            "SignDecimal" => context.Entities.Select(entity => Math.Sign(entity.DecimalValue)).ToQueryString(),
            "SignDouble" => context.Entities.Select(entity => Math.Sign(entity.DoubleValue)).ToQueryString(),
            "FloorDecimal" => context.Entities.Select(entity => Math.Floor(entity.DecimalValue)).ToQueryString(),
            "FloorDouble" => context.Entities.Select(entity => Math.Floor(entity.DoubleValue)).ToQueryString(),
            "CeilingDecimal" => context.Entities.Select(entity => Math.Ceiling(entity.DecimalValue)).ToQueryString(),
            "CeilingDouble" => context.Entities.Select(entity => Math.Ceiling(entity.DoubleValue)).ToQueryString(),
            "Exp" => context.Entities.Select(entity => Math.Exp(entity.DoubleValue)).ToQueryString(),
            "Log" => context.Entities.Select(entity => Math.Log(entity.DoubleValue)).ToQueryString(),
            "LogWithBase" => context.Entities.Select(entity =>
                Math.Log(entity.DoubleValue, entity.LogBase)).ToQueryString(),
            "Log10" => context.Entities.Select(entity => Math.Log10(entity.DoubleValue)).ToQueryString(),
            "Pow" => context.Entities.Select(entity =>
                Math.Pow(entity.DoubleValue, entity.Exponent)).ToQueryString(),
            "Sqrt" => context.Entities.Select(entity => Math.Sqrt(entity.DoubleValue)).ToQueryString(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

    private static MathContext CreateContext(bool useRelationalNulls = false)
    {
        var options = new DbContextOptionsBuilder<MathContext>()
            .UseDameng(
                "Server=localhost;Port=5236;User=test;Password=test",
                damengOptions =>
                {
                    if (useRelationalNulls)
                    {
                        damengOptions.UseRelationalNulls();
                    }
                })
            .Options;
        return new MathContext(options);
    }

    private sealed class MathContext(DbContextOptions<MathContext> options) : DbContext(options)
    {
        public DbSet<MathEntity> Entities => Set<MathEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<MathEntity>(entity =>
            {
                entity.ToTable("MathEntities", "App");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.IntValue).HasColumnName("INT_VALUE");
                entity.Property(item => item.LongValue).HasColumnName("LONG_VALUE");
                entity.Property(item => item.DecimalValue).HasColumnName("DECIMAL_VALUE")
                    .HasPrecision(38, 9);
                entity.Property(item => item.DoubleValue).HasColumnName("DOUBLE_VALUE");
                entity.Property(item => item.LogBase).HasColumnName("LOG_BASE");
                entity.Property(item => item.Exponent).HasColumnName("EXPONENT");
                entity.Property(item => item.NullableDouble).HasColumnName("NULLABLE_DOUBLE");
                entity.Property(item => item.NullableInt).HasColumnName("NULLABLE_INT");
                entity.Property(item => item.NullableDecimal).HasColumnName("NULLABLE_DECIMAL")
                    .HasPrecision(38, 9);
            });
    }

    private sealed class MathEntity
    {
        public int Id { get; set; }
        public int IntValue { get; set; }
        public long LongValue { get; set; }
        public decimal DecimalValue { get; set; }
        public double DoubleValue { get; set; }
        public double LogBase { get; set; }
        public double Exponent { get; set; }
        public double? NullableDouble { get; set; }
        public int? NullableInt { get; set; }
        public decimal? NullableDecimal { get; set; }
    }
}
