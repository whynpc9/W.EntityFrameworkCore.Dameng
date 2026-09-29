using System.Data;
using System.Data.Common;
using System.Globalization;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using W.EntityFrameworkCore.Dameng.TestData;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengStringAggregateFunctionalTests
{
    [DamengFact]
    public async Task JoinAndConcatKeepClrElementFilterOrderAndEmptySemantics()
        => await AggregateStore.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = CreateContext(store, commands);

            await AssertJoinAsync(context, commands, "|",
                new Dictionary<int, string>
                {
                    [1] = "甲||乙|",
                    [2] = "|",
                    [3] = "甲|乙|甲",
                    [5] = "| ||x|  "
                });
            await AssertJoinAsync(context, commands, "雪🌙",
                new Dictionary<int, string>
                {
                    [1] = "甲雪🌙雪🌙乙雪🌙",
                    [2] = "雪🌙",
                    [3] = "甲雪🌙乙雪🌙甲",
                    [5] = "雪🌙 雪🌙雪🌙x雪🌙  "
                });
            await AssertJoinAsync(context, commands, null,
                new Dictionary<int, string>
                {
                    [1] = "甲乙", [2] = "", [3] = "甲乙甲", [5] = " x  "
                });
            await AssertJoinAsync(context, commands, string.Empty,
                new Dictionary<int, string>
                {
                    [1] = "甲乙", [2] = "", [3] = "甲乙甲", [5] = " x  "
                });

            var longButValidSeparator = new string('雪', 40);
            await AssertJoinAsync(context, commands, longButValidSeparator,
                ExpectedJoin(longButValidSeparator));

            // Execute the same expression instance twice with different captured values.
            // The second execution must bind the new separator instead of a cached value.
            string? cachedSeparator = "|";
            var cachedQuery = context.Rows
                .Where(row => row.GroupId == 1)
                .GroupBy(row => row.GroupId)
                .Select(group => string.Join(cachedSeparator,
                    group.OrderBy(row => row.Ordinal).Select(row => row.Text)));
            commands.Commands.Clear();
            Assert.Equal("甲||乙|", await cachedQuery.SingleAsync());
            AssertServerAggregate(commands);
            cachedSeparator = "雪🌙";
            commands.Commands.Clear();
            Assert.Equal("甲雪🌙雪🌙乙雪🌙", await cachedQuery.SingleAsync());
            AssertServerAggregate(commands);

            commands.Commands.Clear();
            var concatenated = await ConcatQuery(context).OrderBy(value => value.Key).ToListAsync();
            AssertServerAggregate(commands);
            Assert.Equal("甲乙", concatenated.Single(value => value.Key == 1).Value);
            Assert.Equal(string.Empty, concatenated.Single(value => value.Key == 2).Value);
            Assert.Equal("甲乙甲", concatenated.Single(value => value.Key == 3).Value);
            Assert.Equal(" x  ", concatenated.Single(value => value.Key == 5).Value);

            commands.Commands.Clear();
            var filtered = await context.Rows
                .GroupBy(row => row.GroupId)
                .Select(group => new AggregateValue
                {
                    Key = group.Key,
                    Value = string.Join("|", group
                        .Where(row => row.Ordinal != 1)
                        .OrderBy(row => row.Ordinal)
                        .Select(row => row.Text))
                })
                .Where(value => value.Key == 1)
                .OrderByDescending(value => value.Key)
                .SingleAsync();
            AssertServerAggregate(commands);
            Assert.Equal("甲|乙|", filtered.Value);

            commands.Commands.Clear();
            var descending = await context.Rows
                .Where(row => row.GroupId == 1)
                .GroupBy(row => row.GroupId)
                .Select(group => string.Join("|", group
                    .OrderByDescending(row => row.Ordinal)
                    .Select(row => row.Text)))
                .SingleAsync();
            AssertServerAggregate(commands);
            Assert.Equal("|乙||甲", descending);

            commands.Commands.Clear();
            var emptyAfterFilter = await context.Rows
                .Where(row => row.GroupId == 1)
                .GroupBy(row => row.GroupId)
                .Select(group => string.Join("|", group
                    .Where(row => false)
                    .Select(row => row.Text)))
                .SingleAsync();
            AssertServerAggregate(commands);
            Assert.Equal(string.Empty, emptyAfterFilter);

            commands.Commands.Clear();
            var spaced = await JoinQuery(context, " ")
                .Where(value => value.Key == 5)
                .SingleAsync();
            AssertServerAggregate(commands);
            // Exact CLR comparison matters: Dameng text equality may ignore trailing spaces.
            Assert.Equal("    x   ", spaced.Value);
        });

    [DamengFact]
    public async Task ListAggNullableOrderingMatchesClrInBothDirections()
        => await AggregateStore.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = CreateContext(store, commands);

            var ascendingQuery = context.Rows
                .Where(row => row.GroupId == 97)
                .GroupBy(row => row.GroupId)
                .Select(group => string.Join("|", group
                    .OrderBy(row => row.NullableSortKey)
                    .ThenBy(row => row.SecondarySortKey)
                    .Select(row => row.Text)));
            var ascendingSql = ascendingQuery.ToQueryString();
            Assert.Equal(2, Count(ascendingSql, " NULLS FIRST"));
            commands.Commands.Clear();
            var ascending = await ascendingQuery.SingleAsync();
            AssertServerAggregate(commands);
            Assert.Equal(string.Join("|", QueryTranslationCases.ExpectedAggregateOrderingAscending
                .Select(item => item.Value)), ascending);

            var descendingQuery = context.Rows
                .Where(row => row.GroupId == 97)
                .GroupBy(row => row.GroupId)
                .Select(group => string.Join("|", group
                    .OrderByDescending(row => row.NullableSortKey)
                    .ThenBy(row => row.SecondarySortKey)
                    .Select(row => row.Text)));
            var descendingSql = descendingQuery.ToQueryString();
            Assert.Contains("DESC NULLS LAST", descendingSql, StringComparison.Ordinal);
            Assert.Equal(1, Count(descendingSql, " NULLS FIRST"));
            commands.Commands.Clear();
            var descending = await descendingQuery.SingleAsync();
            AssertServerAggregate(commands);
            Assert.Equal(string.Join("|", QueryTranslationCases.ExpectedAggregateOrderingDescending
                .Select(item => item.Value)), descending);
        });

    [DamengFact]
    public async Task OverflowFailsExplicitlyWithoutTruncating()
        => await AggregateStore.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = CreateContext(store, commands);
            var longSeparator = new string('雪', 200);

            var longSeparatorQuery = context.Rows
                .Where(row => row.GroupId == 98)
                .GroupBy(row => row.GroupId)
                .Select(group => string.Join(longSeparator,
                    group.OrderBy(row => row.Ordinal).Select(row => row.Text)));
            commands.Commands.Clear();
            var longSeparatorError = await Assert.ThrowsAnyAsync<DbException>(
                () => longSeparatorQuery.SingleAsync());
            AssertServerAggregate(commands);
            AssertOverflowMessage(longSeparatorError);

            var longValuesQuery = context.Rows
                .Where(row => row.GroupId == 99)
                .GroupBy(row => row.GroupId)
                .Select(group => string.Concat(
                    group.OrderBy(row => row.Ordinal).Select(row => row.Text)));
            commands.Commands.Clear();
            var longValuesError = await Assert.ThrowsAnyAsync<DbException>(
                () => longValuesQuery.SingleAsync());
            AssertServerAggregate(commands);
            AssertOverflowMessage(longValuesError);
        });

    private static void AssertOverflowMessage(DbException error)
        => Assert.True(
            error.Message.Contains("截断", StringComparison.Ordinal)
                || error.Message.Contains("溢出", StringComparison.Ordinal),
            "Dameng must report an explicit string truncation or overflow error.");

    private static Dictionary<int, string> ExpectedJoin(string separator)
    {
        var expected = QueryTranslationCases.Aggregates
            .Where(fixture => fixture.ValuesInOrder.Length > 0)
            .ToDictionary(
                fixture => fixture.Id,
                fixture => string.Join(separator, fixture.ValuesInOrder));
        expected[5] = string.Join(separator, new string?[] { "", " ", null, "x", "  " });
        return expected;
    }

    private static async Task AssertJoinAsync(
        AggregateContext context,
        CommandCaptureInterceptor commands,
        string? separator,
        Dictionary<int, string> expected)
    {
        var query = JoinQuery(context, separator).OrderBy(value => value.Key);
        Assert.Contains("LISTAGG(", query.ToQueryString(), StringComparison.Ordinal);
        commands.Commands.Clear();
        var actual = await query.ToListAsync();
        AssertServerAggregate(commands);
        Assert.Equal(expected.Count, actual.Count);
        foreach (var item in actual)
        {
            Assert.Equal(expected[item.Key], item.Value);
        }
    }

    private static IQueryable<AggregateValue> JoinQuery(AggregateContext context, string? separator)
        => context.Rows
            .Where(row => row.GroupId <= 5)
            .GroupBy(row => row.GroupId)
            .Select(group => new AggregateValue
            {
                Key = group.Key,
                Value = string.Join(separator, group.OrderBy(row => row.Ordinal).Select(row => row.Text))
            });

    private static IQueryable<AggregateValue> ConcatQuery(AggregateContext context)
        => context.Rows
            .Where(row => row.GroupId <= 5)
            .GroupBy(row => row.GroupId)
            .Select(group => new AggregateValue
            {
                Key = group.Key,
                Value = string.Concat(group.OrderBy(row => row.Ordinal).Select(row => row.Text))
            });

    private static void AssertServerAggregate(CommandCaptureInterceptor commands)
        => Assert.Contains(commands.Commands, command => command.Contains("LISTAGG(", StringComparison.Ordinal));

    private static int Count(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;

    private static AggregateContext CreateContext(AggregateStore store, CommandCaptureInterceptor commands)
        => new(
            new DbContextOptionsBuilder<AggregateContext>()
                .UseDameng(store.ConnectionString)
                .ReplaceService<IModelCacheKeyFactory, AggregateModelCacheKeyFactory>()
                .AddInterceptors(commands)
                .Options,
            store.TableName);

    private sealed class AggregateValue
    {
        public int Key { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    private sealed class AggregateContext(
        DbContextOptions<AggregateContext> options,
        string tableName) : DbContext(options)
    {
        public string TableName { get; } = tableName;

        public DbSet<AggregateRow> Rows => Set<AggregateRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<AggregateRow>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).HasColumnName("ID");
                entity.Property(row => row.GroupId).HasColumnName("GROUP_ID");
                entity.Property(row => row.Ordinal).HasColumnName("ORDINAL");
                entity.Property(row => row.Text).HasColumnName("TEXT_VALUE").HasMaxLength(200);
                entity.Property(row => row.LobText).HasColumnName("LOB_VALUE").HasColumnType("nclob");
                entity.Property(row => row.NullableSortKey).HasColumnName("NULLABLE_SORT_KEY");
                entity.Property(row => row.SecondarySortKey).HasColumnName("SECONDARY_SORT_KEY");
            });
    }

    private sealed class AggregateModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is AggregateContext aggregate
                ? (context.GetType(), aggregate.TableName, designTime)
                : (context.GetType(), designTime);
    }

    private sealed class AggregateRow
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public int Ordinal { get; set; }
        public string? Text { get; set; }
        public string? LobText { get; set; }
        public int? NullableSortKey { get; set; }
        public int? SecondarySortKey { get; set; }
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

    private sealed class AggregateStore
    {
        private bool _created;

        private AggregateStore()
        {
            ConnectionString = DamengTestEnvironment.GetRequiredConnectionString();
            TableName = "EF10_AGF_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        }

        public string ConnectionString { get; }
        public string TableName { get; }

        public static async Task WithTableAsync(Func<AggregateStore, Task> test)
        {
            var store = new AggregateStore();
            try
            {
                await store.CreateAndSeedAsync();
                await test(store);
            }
            finally
            {
                await store.DropAsync();
            }
        }

        private async Task CreateAndSeedAsync()
        {
            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            await using (var create = connection.CreateCommand())
            {
                create.CommandText =
                    $"CREATE TABLE \"{TableName}\" (\"ID\" INT NOT NULL, \"GROUP_ID\" INT NOT NULL, " +
                    "\"ORDINAL\" INT NOT NULL, \"TEXT_VALUE\" NVARCHAR2(200), \"LOB_VALUE\" NCLOB, " +
                    "\"NULLABLE_SORT_KEY\" INT, \"SECONDARY_SORT_KEY\" INT, " +
                    $"CONSTRAINT \"PK_{TableName}\" NOT CLUSTER PRIMARY KEY (\"ID\"))";
                await create.ExecuteNonQueryAsync();
                _created = true;
            }

            var id = 1;
            foreach (var fixture in QueryTranslationCases.Aggregates.Where(fixture => fixture.ValuesInOrder.Length > 0))
            {
                for (var ordinal = 0; ordinal < fixture.ValuesInOrder.Length; ordinal++)
                {
                    await InsertAsync(connection, id++, fixture.Id, ordinal, fixture.ValuesInOrder[ordinal]);
                }
            }

            string?[] spacedValues = ["", " ", null, "x", "  "];
            for (var ordinal = 0; ordinal < spacedValues.Length; ordinal++)
            {
                await InsertAsync(connection, id++, 5, ordinal, spacedValues[ordinal]);
            }

            foreach (var fixture in QueryTranslationCases.AggregateOrderingCases)
            {
                await InsertAsync(connection, id++, 97, fixture.Id, fixture.Value,
                    fixture.NullableSortKey, fixture.SecondarySortKey);
            }

            for (var ordinal = 0; ordinal < 200; ordinal++)
            {
                await InsertAsync(connection, id++, 98, ordinal, "x");
            }

            for (var ordinal = 0; ordinal < 200; ordinal++)
            {
                await InsertAsync(connection, id++, 99, ordinal, new string('x', 200));
            }
        }

        private async Task InsertAsync(
            DbConnection connection, int id, int group, int ordinal, string? value,
            int? nullableSortKey = null, int? secondarySortKey = null)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"INSERT INTO \"{TableName}\" (\"ID\", \"GROUP_ID\", \"ORDINAL\", \"TEXT_VALUE\", " +
                "\"NULLABLE_SORT_KEY\", \"SECONDARY_SORT_KEY\") " +
                "VALUES (:id, :group_id, :ordinal, :value, :nullable_sort_key, :secondary_sort_key)";
            Add(command, "id", DbType.Int32, id);
            Add(command, "group_id", DbType.Int32, group);
            Add(command, "ordinal", DbType.Int32, ordinal);
            Add(command, "value", DbType.String, value ?? (object)DBNull.Value);
            Add(command, "nullable_sort_key", DbType.Int32, nullableSortKey ?? (object)DBNull.Value);
            Add(command, "secondary_sort_key", DbType.Int32, secondarySortKey ?? (object)DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        private static void Add(DbCommand command, string name, DbType type, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = type;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        private async Task DropAsync()
        {
            if (!_created)
            {
                return;
            }

            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP TABLE \"{TableName}\"";
            await drop.ExecuteNonQueryAsync();
            _created = false;
        }
    }
}
