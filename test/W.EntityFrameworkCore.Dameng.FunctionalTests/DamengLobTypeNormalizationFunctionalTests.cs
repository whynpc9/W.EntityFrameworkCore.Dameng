using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Trait("AuditPlan", "024")]
public sealed class DamengLobTypeNormalizationFunctionalTests
{
    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitLobsRoundTripAndKeepQuerySemantics(bool padded)
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture).ToUpperInvariant();
        var table = $"EF10_LN_{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        await using var connection = new DmConnection(connectionString);
        await connection.OpenAsync();
        var capture = new CommandCapture();
        var options = new DbContextOptionsBuilder<Context>()
            .UseDamengUnderTest(connectionString)
            .ReplaceService<IModelCacheKeyFactory, CacheKey>()
            .AddInterceptors(capture).Options;

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                CREATE TABLE "{table}" (
                    "Id" INT NOT NULL,
                    "NationalText" NCLOB,
                    "AnsiText" CLOB,
                    "Payload" BLOB,
                    "ShortText" NVARCHAR2(16) NOT NULL,
                    "ShortPayload" VARBINARY(16) NOT NULL,
                    NOT CLUSTER PRIMARY KEY ("Id")
                )
                """;
            await command.ExecuteNonQueryAsync();

            const string smallText = "达梦 ' 小值";
            byte[] smallPayload = [0, 1, 128, 255];
            var inserted = LargeRow(42_000, 24);
            var updated = LargeRow(49_000, 25);
            Row[] expected =
            [
                new() { Id = 1 },
                new() { Id = 2, NationalText = "", AnsiText = "", Payload = [] },
                new() { Id = 3, NationalText = smallText, AnsiText = smallText, Payload = smallPayload },
                inserted
            ];

            await using (var writer = new Context(options, table, padded))
            {
                writer.Rows.AddRange(expected.Take(3));
                Assert.Equal(3, await writer.SaveChangesAsync());
                Assert.Equal(4, capture.Parameters.Count(parameter => parameter == (DmDbType.Clob, 0)));
                Assert.Equal(2, capture.Parameters.Count(parameter => parameter == (DmDbType.Blob, 0)));
                AssertWriteParameters(capture, 3, expected[2]);
                writer.ChangeTracker.Clear();

                // TEXT_EQUAL only sees the small controls, regardless of the server's evaluation order.
                capture.Reset();
                Assert.Equal(3, Assert.Single(await writer.Rows
                    .Where(row => row.NationalText == smallText).Select(row => row.Id).ToArrayAsync()));
                Assert.Equal(3, Assert.Single(await writer.Rows
                    .Where(row => row.AnsiText == smallText).Select(row => row.Id).ToArrayAsync()));
                Assert.Equal(3, Assert.Single(await writer.Rows
                    .Where(row => row.Payload == smallPayload).Select(row => row.Id).ToArrayAsync()));
                Assert.Equal(3, capture.CommandCount);

                capture.Reset();
                writer.Rows.Add(inserted);
                Assert.Equal(1, await writer.SaveChangesAsync());
                AssertWriteParameters(capture, 1, inserted);
                writer.ChangeTracker.Clear();
            }

            await using (var reader = new Context(options, table, padded))
            {
                await AssertReadback(reader, expected);
            }

            capture.Reset();
            await using (var writer = new Context(options, table, padded))
            {
                writer.Attach(updated);
                writer.Entry(updated).Property(row => row.NationalText).IsModified = true;
                writer.Entry(updated).Property(row => row.AnsiText).IsModified = true;
                writer.Entry(updated).Property(row => row.Payload).IsModified = true;
                Assert.Equal(1, await writer.SaveChangesAsync());
                AssertWriteParameters(capture, 1, updated);
                writer.ChangeTracker.Clear();
            }

            await using var context = new Context(options, table, padded);
            expected[^1] = updated;
            await AssertReadback(context, expected);

            await AssertGuards(context.Rows.Select(row => row.NationalText), capture);
            await AssertGuards(context.Rows.Select(row => row.AnsiText), capture);
            await AssertGuards(context.Rows.Select(row => row.Payload), capture);
            await AssertBoundedOperations(context.Rows.Select(row => row.ShortText), capture);
            await AssertBoundedOperations(context.Rows.Select(row => row.ShortPayload), capture);
        }
        finally
        {
            if (await CountTable(connection, table) != 0)
            {
                await using var drop = connection.CreateCommand();
                drop.CommandText = $"DROP TABLE \"{table}\"";
                await drop.ExecuteNonQueryAsync();
            }

            Assert.Equal(0, await CountTable(connection, table));
        }
    }

    private static Row LargeRow(int length, int seed)
    {
        var payload = new byte[length];
        new Random(seed).NextBytes(payload);
        return new Row
        {
            Id = 4,
            NationalText = new string('达', length) + $"'𠮷{seed}",
            AnsiText = new string('梦', length) + $"'𠮷{seed}",
            Payload = payload
        };
    }

    private static async Task AssertReadback(Context context, Row[] expected)
    {
        var actual = await context.Rows.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < actual.Length; i++)
        {
            Assert.Equal(expected[i].Id, actual[i].Id);
            Assert.Equal(expected[i].NationalText?.Length, actual[i].NationalText?.Length);
            Assert.Equal(expected[i].AnsiText?.Length, actual[i].AnsiText?.Length);
            Assert.Equal(expected[i].Payload?.Length, actual[i].Payload?.Length);
            Assert.Equal(expected[i].NationalText, actual[i].NationalText);
            Assert.Equal(expected[i].AnsiText, actual[i].AnsiText);
            Assert.Equal(expected[i].Payload, actual[i].Payload);
        }
    }

    private static void AssertWriteParameters(CommandCapture capture, int commandCount, Row row)
    {
        Assert.Equal(commandCount, capture.CommandCount);
        Assert.Equal(2, capture.Parameters.Count(parameter => parameter == (DmDbType.Clob, row.NationalText!.Length)));
        Assert.Single(capture.Parameters, parameter => parameter == (DmDbType.Blob, row.Payload!.Length));
    }

    private static async Task AssertGuards<T>(IQueryable<T> values, CommandCapture capture)
    {
        capture.Reset();
        await AssertRejected(values.OrderBy(value => value), "ORDER BY");
        await AssertRejected(values.GroupBy(value => value).Select(group => group.Count()), "GROUP BY");
        await AssertRejected(values.Distinct(), "SELECT DISTINCT");
        await AssertRejected(values.Union(values), "UNION");
        await AssertRejected(values.Intersect(values), "INTERSECT");
        await AssertRejected(values.Except(values), "EXCEPT");
        Assert.Equal(0, capture.CommandCount);
    }

    private static async Task AssertRejected<T>(IQueryable<T> query, string operation)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToArrayAsync());
        Assert.Contains(operation, error.Message, StringComparison.Ordinal);
        Assert.Contains("LOB store type", error.Message, StringComparison.Ordinal);
    }

    private static async Task AssertBoundedOperations<T>(IQueryable<T> values, CommandCapture capture)
    {
        capture.Reset();
        Assert.Equal(4, (await values.OrderBy(value => value).ToArrayAsync()).Length);
        Assert.Equal(4, Assert.Single(await values.GroupBy(value => value).Select(group => group.Count()).ToArrayAsync()));
        Assert.Single(await values.Distinct().ToArrayAsync());
        Assert.Single(await values.Union(values).ToArrayAsync());
        Assert.Single(await values.Intersect(values).ToArrayAsync());
        Assert.Empty(await values.Except(values).ToArrayAsync());
        Assert.Equal(6, capture.CommandCount);
    }

    private static async Task<int> CountTable(DbConnection connection, string table)
    {
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :name";
        var parameter = count.CreateParameter();
        parameter.ParameterName = "name";
        parameter.Value = table;
        count.Parameters.Add(parameter);
        return Convert.ToInt32(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (((Context)context).Table, ((Context)context).Padded, designTime);
    }

    private sealed class Context(DbContextOptions<Context> options, string table, bool padded) : DbContext(options)
    {
        public string Table => table;
        public bool Padded => padded;
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<Row>();
            row.ToTable(Table);
            row.Property(value => value.Id).ValueGeneratedNever();
            row.Property(value => value.NationalText).HasColumnType(Padded ? " \tNcLoB\n" : "NCLOB");
            row.Property(value => value.AnsiText).HasColumnType(Padded ? " cLoB " : "CLOB");
            row.Property(value => value.Payload).HasColumnType(Padded ? "\t BlOb \n" : "BLOB");
            row.Property(value => value.ShortText).HasColumnType("NVARCHAR2(16)");
            row.Property(value => value.ShortPayload).HasColumnType("VARBINARY(16)");
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public string? NationalText { get; set; }
        public string? AnsiText { get; set; }
        public byte[]? Payload { get; set; }
        public string ShortText { get; set; } = "a";
        public byte[] ShortPayload { get; set; } = [1];
    }

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public int CommandCount { get; private set; }
        public List<(DmDbType Type, int Length)> Parameters { get; } = [];

        public void Reset()
        {
            CommandCount = 0;
            Parameters.Clear();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return ValueTask.FromResult(result);
        }

        private void Capture(DbCommand command)
        {
            CommandCount++;
            foreach (DmParameter parameter in command.Parameters)
            {
                var length = parameter.Value switch
                {
                    string text => text.Length,
                    byte[] bytes => bytes.Length,
                    _ => 0
                };
                Parameters.Add((parameter.DmSqlType, length));
            }
        }
    }
}
