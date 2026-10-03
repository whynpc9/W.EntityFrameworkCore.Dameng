using System.Data.Common;
using System.Globalization;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using W.EntityFrameworkCore.Dameng.TestData;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengMathTranslationFunctionalTests
{
    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MathOverloadsExecuteInServerQueries(bool useRelationalNulls)
        => MathStore.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = CreateContext(store, commands, useRelationalNulls);
            context.Entities.AddRange(Enumerable.Range(0, QueryTranslationCases.MathDecimal.Count)
                .Select(index => new MathEntity
                {
                    Id = index + 1,
                    IntValue = QueryTranslationCases.MathInt32[Math.Min(index,
                        QueryTranslationCases.MathInt32.Count - 1)].Value,
                    LongValue = QueryTranslationCases.MathInt64[index].Value,
                    DecimalValue = QueryTranslationCases.MathDecimal[index].Value,
                    DoubleValue = QueryTranslationCases.MathDouble[Math.Min(index,
                        QueryTranslationCases.MathDouble.Count - 1)].Value,
                    NullableInt = index == 1 ? null : QueryTranslationCases.MathInt32[Math.Min(index,
                        QueryTranslationCases.MathInt32.Count - 1)].Value,
                    NullableDecimal = index == 1 ? null : QueryTranslationCases.MathDecimal[index].Value
                }));
            context.Entities.Add(new MathEntity { Id = 99 });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            commands.Commands.Clear();

            var intQuery = context.Entities
                .Where(entity => entity.Id > 1 && entity.Id <= QueryTranslationCases.MathInt32.Count)
                .OrderBy(entity => entity.Id)
                .Select(entity => new
                {
                    Absolute = Math.Abs(entity.IntValue),
                    Sign = Math.Sign(entity.IntValue)
                });
            Assert.Contains("CAST(ABS(", intQuery.ToQueryString(), StringComparison.Ordinal);
            var ints = await intQuery.ToListAsync();
            Assert.Equal(QueryTranslationCases.MathInt32.Skip(1).Select(value => value.ExpectedAbs),
                ints.Select(value => (int?)value.Absolute));
            Assert.Equal(QueryTranslationCases.MathInt32.Skip(1).Select(value => value.ExpectedSign),
                ints.Select(value => value.Sign));

            var longQuery = context.Entities
                .Where(entity => entity.Id > 1 && entity.Id <= QueryTranslationCases.MathInt64.Count)
                .OrderBy(entity => entity.Id)
                .Select(entity => new
                {
                    Absolute = Math.Abs(entity.LongValue),
                    Sign = Math.Sign(entity.LongValue)
                });
            Assert.Contains("ABS(", longQuery.ToQueryString(), StringComparison.Ordinal);
            Assert.Contains("SIGN(", longQuery.ToQueryString(), StringComparison.Ordinal);
            var longs = await longQuery.ToListAsync();
            Assert.Equal(QueryTranslationCases.MathInt64.Skip(1).Select(value => value.ExpectedAbs),
                longs.Select(value => (long?)value.Absolute));
            Assert.Equal(QueryTranslationCases.MathInt64.Skip(1).Select(value => value.ExpectedSign),
                longs.Select(value => value.Sign));

            var decimalQuery = context.Entities
                .Where(entity => entity.Id <= QueryTranslationCases.MathDecimal.Count)
                .OrderBy(entity => entity.Id)
                .Select(entity => new
                {
                    Absolute = Math.Abs(entity.DecimalValue),
                    Sign = Math.Sign(entity.DecimalValue),
                    Floor = Math.Floor(entity.DecimalValue),
                    Ceiling = Math.Ceiling(entity.DecimalValue)
                });
            AssertMathProjectionSql(decimalQuery.ToQueryString());
            var decimals = await decimalQuery.ToListAsync();
            Assert.Equal(QueryTranslationCases.MathDecimal.Select(value => value.ExpectedAbs),
                decimals.Select(value => value.Absolute));
            Assert.Equal(QueryTranslationCases.MathDecimal.Select(value => value.ExpectedSign),
                decimals.Select(value => value.Sign));
            Assert.Equal(QueryTranslationCases.MathDecimal.Select(value => value.ExpectedFloor),
                decimals.Select(value => value.Floor));
            Assert.Equal(QueryTranslationCases.MathDecimal.Select(value => value.ExpectedCeiling),
                decimals.Select(value => value.Ceiling));

            var doubleQuery = context.Entities
                .Where(entity => entity.Id <= QueryTranslationCases.MathDouble.Count)
                .OrderBy(entity => entity.Id)
                .Select(entity => new
                {
                    Absolute = Math.Abs(entity.DoubleValue),
                    Sign = Math.Sign(entity.DoubleValue),
                    Floor = Math.Floor(entity.DoubleValue),
                    Ceiling = Math.Ceiling(entity.DoubleValue)
                });
            AssertMathProjectionSql(doubleQuery.ToQueryString());
            var doubles = await doubleQuery.ToListAsync();
            Assert.Equal(QueryTranslationCases.MathDouble.Select(value => value.ExpectedAbs),
                doubles.Select(value => value.Absolute));
            Assert.Equal(QueryTranslationCases.MathDouble.Select(value => value.ExpectedSign),
                doubles.Select(value => value.Sign));
            Assert.Equal(QueryTranslationCases.MathDouble.Select(value => value.ExpectedFloor),
                doubles.Select(value => value.Floor));
            Assert.Equal(QueryTranslationCases.MathDouble.Select(value => value.ExpectedCeiling),
                doubles.Select(value => value.Ceiling));

            Assert.Contains(commands.Commands, sql => sql.Contains("CAST(ABS(", StringComparison.Ordinal));
            Assert.Contains(commands.Commands, sql => sql.Contains("SIGN(", StringComparison.Ordinal));
            Assert.Contains(commands.Commands, sql => sql.Contains("FLOOR(", StringComparison.Ordinal));
            Assert.Contains(commands.Commands, sql => sql.Contains("CEIL(", StringComparison.Ordinal));

            commands.Commands.Clear();
            var matchingIds = await context.Entities
                .Where(entity => entity.Id > 1 && Math.Abs(entity.IntValue) == 7)
                .OrderBy(entity => Math.Ceiling(entity.DoubleValue))
                .Select(entity => entity.Id)
                .ToListAsync();
            Assert.Equal([2], matchingIds);
            Assert.Contains(commands.Commands, sql => sql.Contains("CAST(ABS(", StringComparison.Ordinal)
                && sql.Contains("ORDER BY CEIL(", StringComparison.Ordinal));

            var groups = await context.Entities
                .Where(entity => entity.Id <= QueryTranslationCases.MathInt64.Count)
                .GroupBy(entity => Math.Sign(entity.LongValue))
                .Select(group => new { Sign = group.Key, Count = group.Count() })
                .OrderBy(group => group.Sign)
                .ToListAsync();
            Assert.Equal([(-1, 3), (0, 1), (1, 2)],
                groups.Select(group => (group.Sign, group.Count)));

            var nullable = await context.Entities
                .Where(entity => entity.Id == 2 || entity.Id == 3 || entity.Id == 99)
                .OrderBy(entity => entity.Id)
                .Select(entity => new
                {
                    IntAbs = entity.NullableInt.HasValue
                        ? Math.Abs(entity.NullableInt.Value)
                        : (int?)null,
                    DecimalFloor = entity.NullableDecimal.HasValue
                        ? Math.Floor(entity.NullableDecimal.Value)
                        : (decimal?)null
                })
                .ToListAsync();
            Assert.Null(nullable[0].IntAbs);
            Assert.Null(nullable[0].DecimalFloor);
            Assert.Equal(0, nullable[1].IntAbs);
            Assert.Equal(0m, nullable[1].DecimalFloor);
            Assert.Null(nullable[2].IntAbs);
            Assert.Null(nullable[2].DecimalFloor);

            var intOverflowQuery = context.Entities.Where(entity => entity.Id == 1)
                .Select(entity => Math.Abs(entity.IntValue));
            Assert.Contains("CAST(ABS(", intOverflowQuery.ToQueryString(), StringComparison.Ordinal);
            AssertOverflow(await Assert.ThrowsAnyAsync<DbException>(
                () => intOverflowQuery.SingleAsync()));

            var intOverflowFilter = context.Entities
                .Where(entity => entity.Id == 1 && Math.Abs(entity.IntValue) > 0)
                .Select(entity => entity.Id);
            Assert.Contains("WHERE", intOverflowFilter.ToQueryString(), StringComparison.Ordinal);
            Assert.Contains("CAST(ABS(", intOverflowFilter.ToQueryString(), StringComparison.Ordinal);
            AssertOverflow(await Assert.ThrowsAnyAsync<DbException>(
                () => intOverflowFilter.SingleAsync()));

            var longOverflowQuery = context.Entities.Where(entity => entity.Id == 1)
                .Select(entity => Math.Abs(entity.LongValue));
            Assert.Contains("ABS(", longOverflowQuery.ToQueryString(), StringComparison.Ordinal);
            AssertOverflow(await Assert.ThrowsAnyAsync<DbException>(
                () => longOverflowQuery.SingleAsync()));
        });

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ExponentialLogarithmicAndRootFunctionsExecuteOnServer(bool useRelationalNulls)
        => MathStore.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = CreateContext(store, commands, useRelationalNulls);
            context.Entities.AddRange(QueryTranslationCases.MathDoubleFunctions
                .Select((value, index) => new MathEntity
                {
                    Id = 201 + index,
                    DoubleValue = value.Value,
                    LogBase = value.LogBase,
                    Exponent = value.PowerExponent,
                    NullableDouble = value.Value
                }));
            context.Entities.Add(new MathEntity
            {
                Id = 204,
                DoubleValue = 1d,
                LogBase = 10d,
                Exponent = 1d,
                NullableDouble = null
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            commands.Commands.Clear();

            var projected = context.Entities
                .Where(entity => entity.Id >= 201 && entity.Id <= 203)
                .OrderBy(entity => entity.Id)
                .Select(entity => new
                {
                    Exponential = Math.Exp(entity.DoubleValue),
                    NaturalLog = Math.Log(entity.DoubleValue),
                    BaseLog = Math.Log(entity.DoubleValue, entity.LogBase),
                    CommonLog = Math.Log10(entity.DoubleValue),
                    Power = Math.Pow(entity.DoubleValue, entity.Exponent),
                    SquareRoot = Math.Sqrt(entity.DoubleValue)
                });
            var sql = projected.ToQueryString();
            Assert.Contains("EXP(", sql, StringComparison.Ordinal);
            Assert.Contains("LN(", sql, StringComparison.Ordinal);
            Assert.Contains("LOG(", sql, StringComparison.Ordinal);
            Assert.Contains("LOG10(", sql, StringComparison.Ordinal);
            Assert.Contains("POWER(", sql, StringComparison.Ordinal);
            Assert.Contains("SQRT(", sql, StringComparison.Ordinal);
            Assert.True(sql.IndexOf("\"LOG_BASE\"", StringComparison.Ordinal)
                < sql.IndexOf("\"DOUBLE_VALUE\"", sql.IndexOf("LOG(", StringComparison.Ordinal),
                    StringComparison.Ordinal));

            var actual = await projected.ToListAsync();
            Assert.Equal(QueryTranslationCases.MathDoubleFunctions.Count, actual.Count);
            for (var index = 0; index < actual.Count; index++)
            {
                var expected = QueryTranslationCases.MathDoubleFunctions[index];
                AssertClose(expected.ExpectedExp, actual[index].Exponential);
                AssertClose(expected.ExpectedLog, actual[index].NaturalLog);
                AssertClose(expected.ExpectedLogWithBase, actual[index].BaseLog);
                AssertClose(expected.ExpectedLog10, actual[index].CommonLog);
                AssertClose(expected.ExpectedPower, actual[index].Power);
                AssertClose(expected.ExpectedSquareRoot, actual[index].SquareRoot);
            }
            Assert.Contains(commands.Commands, executed => executed.Contains("EXP(", StringComparison.Ordinal)
                && executed.Contains("LN(", StringComparison.Ordinal)
                && executed.Contains("LOG(", StringComparison.Ordinal)
                && executed.Contains("LOG10(", StringComparison.Ordinal)
                && executed.Contains("POWER(", StringComparison.Ordinal)
                && executed.Contains("SQRT(", StringComparison.Ordinal));

            var filtered = context.Entities
                .Where(entity => entity.Id >= 201 && entity.Id <= 203
                    && Math.Log(entity.DoubleValue) > 0d)
                .OrderBy(entity => Math.Exp(entity.DoubleValue))
                .Select(entity => entity.Id);
            Assert.Contains("LN(", filtered.ToQueryString(), StringComparison.Ordinal);
            Assert.Contains("ORDER BY EXP(", filtered.ToQueryString(), StringComparison.Ordinal);
            Assert.Equal([202, 203], await filtered.ToListAsync());

            var ordered = context.Entities
                .Where(entity => entity.Id >= 201 && entity.Id <= 203)
                .OrderBy(entity => Math.Pow(entity.DoubleValue, entity.Exponent))
                .Select(entity => entity.Id);
            Assert.Contains("ORDER BY POWER(", ordered.ToQueryString(), StringComparison.Ordinal);
            Assert.Equal([201, 203, 202], await ordered.ToListAsync());

            var groups = context.Entities
                .Where(entity => entity.Id >= 201 && entity.Id <= 203)
                .GroupBy(entity => Math.Sqrt(entity.DoubleValue))
                .Select(group => new { Root = group.Key, Count = group.Count() })
                .OrderBy(group => group.Root);
            Assert.Contains("SQRT(", groups.ToQueryString(), StringComparison.Ordinal);
            Assert.Contains("GROUP BY", groups.ToQueryString(), StringComparison.Ordinal);
            var grouped = await groups.ToListAsync();
            Assert.Equal(3, grouped.Count);
            Assert.All(grouped, group => Assert.Equal(1, group.Count));
            for (var index = 0; index < grouped.Count; index++)
            {
                AssertClose(QueryTranslationCases.MathDoubleFunctions[index].ExpectedSquareRoot,
                    grouped[index].Root);
            }

            var nullable = context.Entities
                .Where(entity => entity.Id == 201 || entity.Id == 204)
                .OrderBy(entity => entity.Id)
                .Select(entity => entity.NullableDouble.HasValue
                    ? Math.Log(entity.NullableDouble.Value, entity.LogBase)
                    : (double?)null);
            Assert.Contains("LOG(", nullable.ToQueryString(), StringComparison.Ordinal);
            var nullableValues = await nullable.ToListAsync();
            AssertClose(0d, Assert.IsType<double>(nullableValues[0]));
            Assert.Null(nullableValues[1]);

            var row = context.Entities.Where(entity => entity.Id == 201);
            await AssertMathDomainErrorAsync(
                row.Select(entity => Math.Log(entity.DoubleValue - entity.DoubleValue)),
                "LN", "参数数据非法");
            await AssertMathDomainErrorAsync(
                row.Select(entity => Math.Log(-entity.DoubleValue)),
                "LN", "参数数据非法");
            await AssertMathDomainErrorAsync(
                row.Select(entity => Math.Log(entity.DoubleValue, 1d)),
                "LOG", "参数数据非法");
            await AssertMathDomainErrorAsync(
                row.Select(entity => Math.Log(entity.DoubleValue, 0d)),
                "LOG", "参数数据非法");
            await AssertMathDomainErrorAsync(
                row.Select(entity => Math.Log(entity.DoubleValue, -2d)),
                "LOG", "参数数据非法");
            await AssertMathDomainErrorAsync(
                row.Select(entity => Math.Log10(-entity.DoubleValue)),
                "LOG10", "参数数据非法");
            await AssertMathDomainErrorAsync(
                row.Select(entity => Math.Pow(-entity.DoubleValue, 0.5d)),
                "POWER", "参数数据非法");
            await AssertMathDomainErrorAsync(
                row.Select(entity => Math.Sqrt(-entity.DoubleValue)),
                "SQRT", "参数数据非法");
            await AssertMathDomainErrorAsync(
                row.Select(entity => Math.Exp(entity.DoubleValue * 1000d)),
                "EXP", "溢出");
        });

    private static void AssertClose(double expected, double actual)
    {
        Assert.True(double.IsFinite(actual), "Expected a finite double from the driver.");
        var tolerance = Math.Max(1e-12d, Math.Abs(expected) * 1e-12d);
        Assert.InRange(Math.Abs(actual - expected), 0d, tolerance);
    }

    private static async Task AssertMathDomainErrorAsync(
        IQueryable<double> query,
        string function,
        string expectedDiagnostic)
    {
        Assert.Contains($"{function}(", query.ToQueryString(), StringComparison.Ordinal);
        var exception = await Assert.ThrowsAnyAsync<DbException>(() => query.SingleAsync());
        Assert.Contains(expectedDiagnostic, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertMathProjectionSql(string sql)
    {
        Assert.Contains("ABS(", sql, StringComparison.Ordinal);
        Assert.Contains("SIGN(", sql, StringComparison.Ordinal);
        Assert.Contains("FLOOR(", sql, StringComparison.Ordinal);
        Assert.Contains("CEIL(", sql, StringComparison.Ordinal);
    }

    private static void AssertOverflow(DbException exception)
        => Assert.True(
            exception.Message.Contains("溢出", StringComparison.Ordinal)
                || exception.Message.Contains("overflow", StringComparison.OrdinalIgnoreCase),
            $"Expected a numeric overflow diagnostic, got {exception.GetType().Name}.");

    private static MathContext CreateContext(
        MathStore store,
        CommandCaptureInterceptor commands,
        bool useRelationalNulls)
    {
        var options = new DbContextOptionsBuilder<MathContext>()
            .UseDameng(store.ConnectionString, damengOptions =>
            {
                if (useRelationalNulls)
                {
                    damengOptions.UseRelationalNulls();
                }
            })
            // The full suite intentionally uses many fixture-specific model-cache factories.
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
            .ReplaceService<IModelCacheKeyFactory, MathModelCacheKeyFactory>()
            .AddInterceptors(commands)
            .EnableDetailedErrors()
            .Options;
        return new MathContext(options, store.TableName);
    }

    private sealed class MathContext(DbContextOptions<MathContext> options, string tableName)
        : DbContext(options)
    {
        public string TableName { get; } = tableName;
        public DbSet<MathEntity> Entities => Set<MathEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<MathEntity>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("ID").ValueGeneratedNever();
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

    private sealed class MathModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is MathContext mathContext
                ? (context.GetType(), mathContext.TableName, designTime)
                : (context.GetType(), designTime);
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

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class MathStore
    {
        private bool _tableCreated;

        private MathStore()
        {
            ConnectionString = DamengTestEnvironment.GetRequiredConnectionString();
            var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
            TableName = $"EF10_MA_{suffix}";
            PrimaryKeyName = $"PK_MA_{suffix}";
        }

        public string ConnectionString { get; }
        public string TableName { get; }
        public string PrimaryKeyName { get; }

        public static async Task WithTableAsync(Func<MathStore, Task> test)
        {
            var store = new MathStore();
            try
            {
                await store.CreateTableAsync();
                await test(store);
            }
            finally
            {
                await store.DropTableAsync();
            }
        }

        private async Task CreateTableAsync()
        {
            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                CREATE TABLE "{TableName}" (
                    "ID" INT NOT NULL,
                    "INT_VALUE" INT NOT NULL,
                    "LONG_VALUE" BIGINT NOT NULL,
                    "DECIMAL_VALUE" DECIMAL(38,9) NOT NULL,
                    "DOUBLE_VALUE" DOUBLE NOT NULL,
                    "LOG_BASE" DOUBLE NOT NULL,
                    "EXPONENT" DOUBLE NOT NULL,
                    "NULLABLE_DOUBLE" DOUBLE NULL,
                    "NULLABLE_INT" INT NULL,
                    "NULLABLE_DECIMAL" DECIMAL(38,9) NULL,
                    CONSTRAINT "{PrimaryKeyName}" NOT CLUSTER PRIMARY KEY ("ID")
                )
                """;
            await command.ExecuteNonQueryAsync();
            _tableCreated = true;
        }

        private async Task DropTableAsync()
        {
            if (!_tableCreated)
            {
                return;
            }

            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP TABLE \"{TableName}\"";
            await command.ExecuteNonQueryAsync();
            _tableCreated = false;
        }
    }
}
