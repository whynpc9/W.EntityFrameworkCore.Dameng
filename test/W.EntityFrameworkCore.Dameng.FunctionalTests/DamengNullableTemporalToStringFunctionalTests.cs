using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengNullableTemporalToStringFunctionalTests
{
#pragma warning disable CA1305 // Exercise the intended no-culture overloads.
    [DamengFact]
    public Task NullableIdentityDefaultTextMatchesNativeCast() => WithTableAsync(async (context, commands) =>
    {
        var nullable = await ReadNativeTextAsync(context, """CAST("Native" AS VARCHAR(100))""");
        var fallback = nullable.Select(text => text ?? string.Empty).ToArray();
        var nonNullable = await ReadNativeTextAsync(context, """CAST("Identity" AS VARCHAR(100))""");
        Assert.Null(nullable[0]);
        Assert.NotNull(nullable[1]);

        await AssertServerTextAsync(context, commands, r => r.Native.ToString(), fallback, "COALESCE(CAST(");
        await AssertServerTextAsync(context, commands, r => r.Native!.Value.ToString(), nullable, "CAST(");
        await AssertServerTextAsync(context, commands, r => r.NullableIdentity.ToString(), fallback, "COALESCE(CAST(");
        await AssertServerTextAsync(context, commands, r => r.NullableIdentity!.Value.ToString(), nullable, "CAST(");
        await AssertServerTextAsync(context, commands, r => r.Identity.ToString(), nonNullable, "CAST(");

        commands.Clear();
        Assert.Equal([1], await context.Rows.Where(r => r.NullableIdentity.ToString() == "")
            .Select(r => r.Id).ToListAsync());
        AssertExecuted(commands, "COALESCE(CAST(", "WHERE");
    });

    [DamengTheory]
    [InlineData("yyyy", "YYYY")]
    [InlineData("yyyy-MM", "YYYY-MM")]
    [InlineData("yyyy-MM-dd", "YYYY-MM-DD")]
    [InlineData("yyyy-MM-dd HH:mm:ss", "YYYY-MM-DD HH24:MI:SS")]
    [InlineData("yyyy-MM-dd HH:mm:ss.fffffff", "YYYY-MM-DD HH24:MI:SS.FF7")]
    [InlineData("s", "YYYY-MM-DD\"T\"HH24:MI:SS")]
    [InlineData("yyyy-MM-dd'T'HH:mm:ss", "YYYY-MM-DD\"T\"HH24:MI:SS")]
    public Task NullableIdentityConstantFormatsMatchNativeTemporalControls(string format, string serverFormat) =>
        WithTableAsync(async (context, commands) =>
        {
            // The server format comes from the fixed inline cases above.
            var expected = await ReadNativeTextAsync(context, $"""TO_CHAR("Native", '{serverFormat}')""");
            Assert.Null(expected[0]);
            Assert.Equal(Seed().Select(r => r.Native?.ToString(format, CultureInfo.InvariantCulture)), expected);

            await AssertServerTextAsync(context, commands, FormatSelector(nameof(Row.Native), format), expected, "TO_CHAR(");
            await AssertServerTextAsync(context, commands, FormatSelector(nameof(Row.NullableIdentity), format), expected, "TO_CHAR(");
        });

    [DamengFact]
    public Task NonNativeTemporalTextCallsFailBeforeSendingSql() => WithTableAsync(async (context, commands) =>
    {
        foreach (var (_, direct, value, formatted) in NonNativeSelectors())
        {
            foreach (var selector in new[] { direct, value, formatted })
            {
                var text = context.Rows.Select(selector);
                commands.Clear();
                await AssertTranslationFailureAsync(() => text.Where(s => s == "2024").ToListAsync(), commands);
                await AssertTranslationFailureAsync(() => text.GroupBy(s => s).Select(g => g.Count()).ToListAsync(), commands);
            }
        }
    });

    [DamengFact]
    public Task NonNativeTopLevelTextProjectionKeepsClientEvaluation() => WithTableAsync(async (context, commands) =>
    {
        var savedCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var seed = Seed();
            foreach (var (column, direct, value, formatted) in NonNativeSelectors())
            {
                commands.Clear();
                Assert.Equal(seed.Select(direct.Compile()),
                    await context.Rows.OrderBy(r => r.Id).Select(direct).ToListAsync());
                AssertClientProjection(commands, column);

                var nonNullRows = context.Rows.Where(r => r.Id != 1).OrderBy(r => r.Id);
                foreach (var selector in new[] { value, formatted })
                {
                    commands.Clear();
                    Assert.Equal(seed.Where(r => r.Id != 1).Select(selector.Compile()),
                        await nonNullRows.Select(selector).ToListAsync());
                    AssertClientProjection(commands, column);
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }
    });

    private static (string Column, Expression<Func<Row, string?>> Direct, Expression<Func<Row, string?>> Value,
        Expression<Func<Row, string?>> Formatted)[] NonNativeSelectors() =>
    [
        (nameof(Row.Ticks), r => r.Ticks.ToString(), r => r.Ticks!.Value.ToString(), FormatSelector(nameof(Row.Ticks), "yyyy")),
        (nameof(Row.Text), r => r.Text.ToString(), r => r.Text!.Value.ToString(), FormatSelector(nameof(Row.Text), "yyyy"))
    ];
#pragma warning restore CA1305

    private static async Task AssertServerTextAsync(Context context, List<string> commands,
        Expression<Func<Row, string?>> selector, string?[] expected, string conversion)
    {
        commands.Clear();
        var projected = await context.Rows.OrderBy(r => r.Id).Select(selector).ToListAsync();
        Assert.Equal(expected, projected);
        AssertExecuted(commands, conversion);

        var key = expected[1];
        var text = context.Rows.Select(selector);
        var expectedIds = Seed().Where((_, index) => expected[index] == key).Select(r => r.Id);
        commands.Clear();
        var parameter = selector.Parameters[0];
        var predicate = Expression.Lambda<Func<Row, bool>>(
            Expression.Equal(selector.Body, Expression.Constant(key, typeof(string))), parameter);
        var actualIds = await context.Rows.Where(predicate).OrderBy(r => r.Id).Select(r => r.Id).ToListAsync();
        Assert.Equal(expectedIds, actualIds);
        AssertExecuted(commands, conversion, "WHERE");

        commands.Clear();
        var actualGroups = await text.GroupBy(s => s)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var expectedGroups = expected.GroupBy(s => s, StringComparer.Ordinal)
            .Select(g => new { g.Key, Count = g.Count() });
        Assert.Equal(expectedGroups.OrderBy(g => g.Key, StringComparer.Ordinal),
            actualGroups.OrderBy(g => g.Key, StringComparer.Ordinal));
        AssertExecuted(commands, conversion, "GROUP BY");
    }

    private static async Task AssertTranslationFailureAsync(Func<Task> query, List<string> commands)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(query);
        Assert.Contains("could not be translated", error.Message, StringComparison.Ordinal);
        Assert.Contains("ToString", error.Message, StringComparison.Ordinal);
        Assert.Empty(commands);
    }

    private static void AssertClientProjection(List<string> commands, string column)
    {
        var sql = Assert.Single(commands);
        Assert.Contains($"\"{column}\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CAST(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("TO_CHAR(", sql, StringComparison.Ordinal);
    }

    private static void AssertExecuted(List<string> commands, string conversion, string? clause = null)
    {
        var sql = Assert.Single(commands);
        if (clause is not null)
        {
            var start = sql.IndexOf(clause, StringComparison.Ordinal);
            Assert.True(start >= 0, sql);
            // EF may compute the grouping key in a subquery and group by its alias.
            if (clause == "WHERE")
            {
                sql = sql[start..];
            }
        }
        Assert.Contains(conversion, sql, StringComparison.Ordinal);
        if (conversion.Contains("CAST(", StringComparison.Ordinal))
        {
            Assert.Contains("AS VARCHAR(100)", sql, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("TRUNC(", sql, StringComparison.Ordinal);
    }

    private static Expression<Func<Row, string?>> FormatSelector(string property, string format)
    {
        var row = Expression.Parameter(typeof(Row), "row");
        var value = Expression.Property(Expression.Property(row, property), nameof(Nullable<DateTime>.Value));
        var call = Expression.Call(value, nameof(DateTime.ToString), Type.EmptyTypes, Expression.Constant(format));
        return Expression.Lambda<Func<Row, string?>>(call, row);
    }

    private static async Task<string?[]> ReadNativeTextAsync(Context context, string sqlExpression)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"""SELECT {sqlExpression} FROM "{context.TableName}" ORDER BY "Id" """;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string?>();
        while (await reader.ReadAsync())
        {
            result.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        }
        return result.ToArray();
    }

    private static async Task WithTableAsync(Func<Context, List<string>, Task> test)
    {
        var table = "EF10_NTS_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        var createAttempted = false;
        try
        {
            await using var connection = new DmConnection(connectionString);
            await connection.OpenAsync();
            Assert.Equal(0, await CountTablesAsync(connection, table));
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TABLE "{table}" (
                    "Id" INT NOT NULL,
                    "Native" TIMESTAMP(7),
                    "NullableIdentity" TIMESTAMP(7),
                    "Identity" TIMESTAMP(7) NOT NULL,
                    "Ticks" BIGINT,
                    "Text" NVARCHAR2(64),
                    CONSTRAINT "PK_{table}" NOT CLUSTER PRIMARY KEY ("Id")
                )
                """;
            createAttempted = true;
            await command.ExecuteNonQueryAsync();
            var commands = new List<string>();
            await using var context = new Context(new DbContextOptionsBuilder<Context>()
                .UseDamengUnderTest(connection)
                .ReplaceService<IModelCacheKeyFactory, ModelCacheKeyFactory>()
                .AddInterceptors(new CommandCapture(commands)).Options, table);
            await SeedAndVerifyAsync(context);
            await test(context, commands);
        }
        finally
        {
            if (createAttempted)
            {
                // CREATE may have committed even if its response did not reach this connection.
                await using var cleanup = new DmConnection(connectionString);
                await cleanup.OpenAsync();
                if (await CountTablesAsync(cleanup, table) != 0)
                {
                    await using var command = cleanup.CreateCommand();
                    command.CommandText = $"""DROP TABLE "{table}" """;
                    await command.ExecuteNonQueryAsync();
                    Assert.Equal(0, await CountTablesAsync(cleanup, table));
                }
            }
        }
    }

    private static async Task<int> CountTablesAsync(DbConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "name";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return DamengTestStore.AsInt32(await command.ExecuteScalarAsync());
    }

    private static async Task SeedAndVerifyAsync(Context context)
    {
        var entity = context.Model.FindEntityType(typeof(Row))!;
        foreach (var (property, storeType, providerType) in new (string, string, Type?)[]
        {
            (nameof(Row.Native), "TIMESTAMP(7)", null),
            (nameof(Row.NullableIdentity), "TIMESTAMP(7)", typeof(DateTime?)),
            (nameof(Row.Identity), "TIMESTAMP(7)", typeof(DateTime)),
            (nameof(Row.Ticks), "BIGINT", typeof(long)),
            (nameof(Row.Text), "NVARCHAR2(64)", typeof(string))
        })
        {
            var mapping = entity.FindProperty(property)!.GetRelationalTypeMapping();
            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal(providerType, mapping.Converter?.ProviderClrType);
        }

        var seed = Seed();
        context.Rows.AddRange(seed);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var actual = await context.Rows.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(seed.Select(Values), actual.Select(Values));

        static (int, DateTime?, DateTime?, DateTime, DateTime?, DateTime?) Values(Row r) =>
            (r.Id, r.Native, r.NullableIdentity, r.Identity, r.Ticks, r.Text);
    }

    private static Row[] Seed()
    {
        var leapDay = new DateTime(2024, 2, 29, 13, 14, 15).AddTicks(1234560);
        var nextYear = new DateTime(2025, 1, 1, 0, 0, 1).AddTicks(6543210);
        DateTime?[] values = [null, leapDay, leapDay, nextYear];
        return values.Select((value, index) => new Row
        {
            Id = index + 1,
            Native = value,
            NullableIdentity = value,
            Identity = value ?? nextYear,
            Ticks = value,
            Text = value
        }).ToArray();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public DateTime? Native { get; set; }
        public DateTime? NullableIdentity { get; set; }
        public DateTime Identity { get; set; }
        public DateTime? Ticks { get; set; }
        public DateTime? Text { get; set; }
    }

    private sealed class Context(DbContextOptions<Context> options, string tableName) : DbContext(options)
    {
        public string TableName => tableName;
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<Row>().ToTable(TableName);
            row.Property(r => r.Id).ValueGeneratedNever();
            row.Property(r => r.Native).HasColumnType("TIMESTAMP(7)");
            row.Property(r => r.NullableIdentity).HasConversion(v => v, v => v).HasColumnType("TIMESTAMP(7)");
            row.Property(r => r.Identity).HasConversion(v => v, v => v).HasColumnType("TIMESTAMP(7)");
            row.Property(r => r.Ticks).HasConversion<long>().HasColumnType("BIGINT");
            row.Property(r => r.Text).HasConversion(v => Encode(v!.Value), v => Decode(v)).HasColumnType("NVARCHAR2(64)");
        }

        private static string Encode(DateTime value) => "D:" + value.ToString("O", CultureInfo.InvariantCulture);

        private static DateTime Decode(string value) =>
            DateTime.Parse(value[2..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private sealed class ModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => context is Context dates
            ? (context.GetType(), dates.TableName, designTime)
            : (context.GetType(), designTime);
    }

    private sealed class CommandCapture(List<string> commands) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
