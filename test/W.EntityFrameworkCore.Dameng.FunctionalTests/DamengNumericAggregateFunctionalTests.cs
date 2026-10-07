using System.Data.Common;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using W.EntityFrameworkCore.Dameng.TestUtilities;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengNumericAggregateFunctionalTests
{
    public static IEnumerable<object[]> QueryCases => NumericAggregateQueryCases.Names
        .SelectMany(name => new[] { new object[] { name, false }, new object[] { name, true } });

    [DamengTheory]
    [MemberData(nameof(QueryCases))]
    public async Task ServerAggregatesMatchClrResults(string name, bool empty)
    {
        await using var store = await AggregateStore.CreateAsync(empty);
        var query = NumericAggregateQueryCases.Query(store.Context.Rows, name);
        var actual = await query.ToListAsync();
        var expected = Expected(store.Seed, name);
        Assert.Equal(Normalize(expected), Normalize(actual));
        var sql = Assert.Single(store.Commands);
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
        if (name != "GroupKeys") Assert.Matches(@"(COUNT|SUM|AVG|MIN|MAX)\(", sql);
    }

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScalarAggregatesPreserveEmptyAndNullableSemantics(bool empty)
    {
        await using var store = await AggregateStore.CreateAsync(empty);
        var rows = store.Context.Rows;
        Assert.Equal(store.Seed.Length, await rows.CountAsync());
        Assert.Equal(store.Seed.LongLength, await rows.LongCountAsync());
        Assert.Equal(store.Seed.Count(r => r.Status == AggregateStatus.Complete),
            await rows.CountAsync(r => r.Status == AggregateStatus.Complete));
        Assert.Equal(store.Seed.Sum(r => -r.Fee), await rows.SumAsync(r => -r.Fee));
        Assert.Equal(store.Seed.Select(r => r.Department).Distinct().Count(),
            await rows.Select(r => r.Department).Distinct().CountAsync());
        Assert.Equal(store.Seed.Select(r => new { r.Department, r.Status }).Distinct().Count(),
            await rows.GroupBy(r => new { r.Department, r.Status }).Select(g => g.Key).CountAsync());
        Assert.Equal(store.Seed.Select(r => r.Fee).DefaultIfEmpty(null).Average(), await rows.AverageAsync(r => r.Fee));
        Assert.Equal(store.Seed.Select(r => r.Fee).DefaultIfEmpty(null).Min(), await rows.MinAsync(r => r.Fee));
        Assert.Equal(store.Seed.Select(r => r.Fee).DefaultIfEmpty(null).Max(), await rows.MaxAsync(r => r.Fee));
        Assert.Equal(0m, await rows.Where(r => r.Department == "C").SumAsync(r => r.Fee));
        Assert.Null(await rows.Where(r => r.Department == "C").AverageAsync(r => r.Fee));
        Assert.Null(await rows.Where(r => r.Department == "C").MinAsync(r => r.Fee));
        Assert.Null(await rows.Where(r => r.Department == "C").MaxAsync(r => r.Fee));
        Assert.Equal(store.Seed.GroupBy(r => r.Department).Any(g => g.Count() > 1),
            await rows.GroupBy(r => r.Department).AnyAsync(g => g.Count() > 1));
        Assert.Equal(store.Seed.Any(r => r.Active), await rows.AnyAsync(r => r.Active));
        Assert.Equal(store.Seed.All(r => r.Id > 0 && r.Status != AggregateStatus.Pending),
            await rows.AllAsync(r => r.Id > 0 && r.Status != AggregateStatus.Pending));
        Assert.All(store.Commands, sql => Assert.DoesNotContain("@", sql, StringComparison.Ordinal));
        Assert.Contains(store.Commands, sql => sql.Contains("HAVING", StringComparison.Ordinal));
    }

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedTerminalAggregatesRequireProjectionAndRewritesExecute(bool empty)
    {
        await using var store = await AggregateStore.CreateAsync(empty);
        var groups = store.Context.Rows.GroupBy(r => r.Department);
        var countError = await Assert.ThrowsAsync<InvalidOperationException>(() => groups.SumAsync(g => g.Count()));
        var sumError = await Assert.ThrowsAsync<InvalidOperationException>(() => groups.SumAsync(g => g.Sum(r => r.Fee)));
        var syncError = Assert.Throws<InvalidOperationException>(() => groups.Sum(g => g.Count()));
        Assert.Contains("could not be translated", countError.Message, StringComparison.Ordinal);
        Assert.Contains("could not be translated", sumError.Message, StringComparison.Ordinal);
        Assert.Contains("could not be translated", syncError.Message, StringComparison.Ordinal);
        Assert.Empty(store.Commands);

        Assert.Equal(store.Seed.Length, await groups.Select(g => g.Count()).SumAsync());
        Assert.Equal(store.Seed.Sum(r => r.Fee), await groups.Select(g => g.Sum(r => r.Fee)).SumAsync());
        Assert.Equal(store.Seed.Length, groups.Select(g => g.Count()).Sum());
        Assert.All(store.Commands, sql =>
        {
            Assert.Contains("SUM(", sql, StringComparison.Ordinal);
            Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        });
    }

    [DamengFact]
    public async Task DateStringGroupingFailsBeforeCommandExecution()
    {
        await using var store = await AggregateStore.CreateAsync(false);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NumericAggregateQueryCases.Query(store.Context.Rows, "DateString").ToListAsync());
        Assert.Contains("DateTime.ToString", error.Message, StringComparison.Ordinal);
        Assert.Empty(store.Commands);
    }

    [DamengFact]
    public async Task NullableDistinctCountDocumentsSqlNullDifference()
    {
        await using var store = await AggregateStore.CreateAsync(false);
        var counts = await store.Context.Rows.GroupBy(r => r.Department)
            .Select(g => new { g.Key, Count = g.Select(r => r.Name).Distinct().Count() }).ToListAsync();
        Assert.Equal(2, Assert.Single(counts, r => r.Key == "A").Count);
        Assert.Equal(0, Assert.Single(counts, r => r.Key == "C").Count);
        Assert.Equal(3, store.Seed.Where(r => r.Department == "A").Select(r => r.Name).Distinct().Count());
        Assert.Single(store.Seed.Where(r => r.Department == "C").Select(r => r.Name).Distinct());
        Assert.Contains("COUNT(DISTINCT", Assert.Single(store.Commands), StringComparison.Ordinal);
    }

    [DamengFact]
    public async Task NonNullableScalarAggregatesThrowOnEmptySetAndAverageIntegersAsFraction()
    {
        await using var store = await AggregateStore.CreateAsync(false);
        Assert.Equal(4.5d, await store.Context.Rows.AverageAsync(r => r.Id));
        Assert.Equal(1, await store.Context.Rows.MinAsync(r => r.Id));
        Assert.Equal(8, await store.Context.Rows.MaxAsync(r => r.Id));
        Assert.Equal(3.8d, await store.Context.Rows.AverageAsync(r => r.Period));
        var empty = store.Context.Rows.Where(r => r.Id < 0);
        Assert.Equal(0, await empty.SumAsync(r => r.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => empty.AverageAsync(r => r.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => empty.MinAsync(r => r.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => empty.MaxAsync(r => r.Id));
        Assert.Equal(8, store.Commands.Count);
    }

    [DamengFact]
    public async Task DynamicHierarchyKeyExecutesOnServer()
    {
        await using var store = await AggregateStore.CreateAsync(false);
        var property = nameof(NumericAggregateRow.Department);
        var actual = await store.Context.Rows.GroupBy(r => EF.Property<string>(r, property))
            .Select(g => new NumericAggregateResult { Key = g.Key, Count = g.Count(), Sum = g.Sum(r => r.Fee) })
            .ToListAsync();
        var expected = store.Seed.GroupBy(r => r.Department)
            .Select(g => new NumericAggregateResult { Key = g.Key, Count = g.Count(), Sum = g.Sum(r => r.Fee) });
        Assert.Equal(Normalize(expected), Normalize(actual));
        Assert.Contains("GROUP BY", Assert.Single(store.Commands), StringComparison.Ordinal);
    }

    private static IEnumerable<NumericAggregateResult> Expected(NumericAggregateRow[] rows, string name)
    {
        if (name != "Basic") return NumericAggregateQueryCases.Query(rows.AsQueryable(), name).ToList();
        // EF 的 Nullable.Value 在 SQL 聚合中是列引用；CLR 的 Value 会抛异常。
        // 独立预期按 SQL 跳过 NULL 的定义计算，不能执行消费方的 Value 选择器。
        return rows.GroupBy(r => r.Department).Select(g => new NumericAggregateResult
        {
            Key = g.Key,
            Count = g.Count(),
            LongCount = g.LongCount(),
            Sum = g.Sum(r => r.Fee),
            Average = g.Average(r => r.Fee),
            Minimum = g.Min(r => r.Fee),
            Maximum = g.Max(r => r.Fee),
            Extra = g.Average(r => (decimal?)r.Period),
            Other = g.Average(r => r.Period * 1.0m),
            Key2 = g.Max(r => r.Name),
            MinimumAt = g.Min(r => r.At),
            MaximumAt = g.Max(r => r.At)
        });
    }

    private static NumericAggregateResult[] Normalize(IEnumerable<NumericAggregateResult> rows)
        => rows.Select(r => r with
        {
            Average = Round(r.Average),
            Extra = Round(r.Extra),
            Other = Round(r.Other),
            Maximum = Round(r.Maximum)
        }).OrderBy(r => r.Key, StringComparer.Ordinal).ThenBy(r => r.Key2, StringComparer.Ordinal)
            .ThenBy(r => r.ConditionalCount).ThenBy(r => r.DistinctCount).ThenBy(r => r.MinimumAt).ToArray();

    private static decimal? Round(decimal? value) => value is null ? null : decimal.Round(value.Value, 6);

    private sealed class AggregateStore : IAsyncDisposable
    {
        private readonly string _connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        private readonly string _tableName = "EF10_NA_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        private bool _created;
        public NumericAggregateContext Context { get; private set; } = null!;
        public NumericAggregateRow[] Seed { get; private set; } = [];
        public List<string> Commands { get; } = [];

        public static async Task<AggregateStore> CreateAsync(bool empty)
        {
            var store = new AggregateStore();
            try
            {
                await using var connection = new DmConnection(store._connectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    CREATE TABLE "{store._tableName}" (
                        "Id" INT NOT NULL, "Department" NVARCHAR2(32), "Name" NVARCHAR2(32),
                        "Fee" DECIMAL(18,4), "Period" INT, "Active" BIT NOT NULL,
                        "Status" INT NOT NULL, "At" TIMESTAMP(7),
                        CONSTRAINT "PK_{store._tableName}" NOT CLUSTER PRIMARY KEY ("Id")
                    )
                    """;
                await command.ExecuteNonQueryAsync();
                store._created = true;
                store.Context = new NumericAggregateContext(new DbContextOptionsBuilder<NumericAggregateContext>()
                    .UseDameng(store._connectionString)
                    .ReplaceService<IModelCacheKeyFactory, NumericAggregateModelCacheKeyFactory>()
                    .AddInterceptors(new CommandCapture(store.Commands)).Options, store._tableName);
                store.Seed = empty ? [] : NumericAggregateQueryCases.Rows();
                store.Context.Rows.AddRange(store.Seed);
                await store.Context.SaveChangesAsync();
                store.Context.ChangeTracker.Clear();
                store.Commands.Clear();
                return store;
            }
            catch
            {
                await store.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Context is not null) await Context.DisposeAsync();
            if (!_created) return;
            await using var connection = new DmConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP TABLE \"{_tableName}\"";
            await command.ExecuteNonQueryAsync();
            _created = false;
        }
    }

    private sealed class CommandCapture(List<string> commands) : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
