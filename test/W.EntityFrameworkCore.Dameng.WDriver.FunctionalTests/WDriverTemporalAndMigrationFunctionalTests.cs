#pragma warning disable EF1002 // SQL text only interpolates generated test object names.

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

/// <summary>
/// W.DamengProvider 100ns temporal precision, the guarded seven-to-six digit migration fixture,
/// and switching a database between the official and W drivers across migrations.
/// The verified seven-digit profile is the server's self-reported 8.1.5.60; W publishes its
/// temporal write limit as <c>ServerCapabilities.MaxTemporalWriteScale</c>.
/// </summary>
public sealed class WDriverTemporalAndMigrationFunctionalTests
{
    private static readonly string[] GuardedColumns = ["A", "B"];

    public static TheoryData<DateTime> SevenDigitDateTimes
        => new()
        {
            DateTime.MaxValue,
            DateTime.MaxValue.AddTicks(-1),
            new DateTime(2026, 10, 14, 1, 2, 3).AddTicks(1234567),
            new DateTime(2026, 10, 14, 1, 2, 3).AddTicks(1234560),
            new DateTime(2026, 10, 14, 1, 2, 3).AddTicks(1234561),
            DateTime.MinValue.AddTicks(1)
        };

    [DamengTheory]
    [MemberData(nameof(SevenDigitDateTimes))]
    public Task DateTimeRoundtripsEveryTickAndTheServerStoresSevenDigits(DateTime value)
        => TemporalStore.WithTableAsync(
            async store =>
            {
                long id;
                await using (var context = store.CreateContext())
                {
                    var row = new TemporalRow { Value = value };
                    context.Rows.Add(row);
                    await context.SaveChangesAsync();
                    id = row.Id;
                }

                await using (var context = store.CreateContext())
                {
                    var actual = await context.Rows.SingleAsync(row => row.Id == id);
                    Assert.Equal(value.Ticks, actual.Value.Ticks);
                    Assert.Equal(1, await context.Rows.CountAsync(row => row.Value == value));
                    if (value < DateTime.MaxValue)
                    {
                        Assert.Equal(0, await context.Rows.CountAsync(row => row.Value == value.AddTicks(1)));
                        Assert.Equal(1, await context.Rows.CountAsync(row => row.Value < value.AddTicks(1)));
                    }

                    Assert.Equal(0, await context.Rows.CountAsync(row => row.Value > value));
                }

                Assert.Equal(
                    value.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
                    await store.ScalarStringAsync(
                        $"SELECT TO_CHAR(\"VALUE\", 'YYYY-MM-DD HH24:MI:SS.FF7') FROM \"{store.TableName}\" WHERE \"ID\" = {id}"));
            });

    public static TheoryData<DateTimeOffset> SevenDigitOffsets
        => new()
        {
            new DateTimeOffset(2026, 10, 14, 1, 2, 3, TimeSpan.FromHours(8)).AddTicks(1234567),
            new DateTimeOffset(2026, 10, 14, 1, 2, 3, new TimeSpan(-5, -30, 0)).AddTicks(1),
            new DateTimeOffset(2026, 10, 14, 23, 59, 59, new TimeSpan(13, 59, 0)).AddTicks(9999999),
            new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero)
        };

    [DamengTheory]
    [MemberData(nameof(SevenDigitOffsets))]
    public Task DateTimeOffsetKeepsTicksAndTheOriginalOffset(DateTimeOffset value)
        => TemporalStore.WithTableAsync(
            async store =>
            {
                long id;
                await using (var context = store.CreateContext())
                {
                    var row = new TemporalRow { Value = new DateTime(2026, 1, 1), Offset = value };
                    context.Rows.Add(row);
                    await context.SaveChangesAsync();
                    id = row.Id;
                }

                await using var readContext = store.CreateContext();
                var actual = (await readContext.Rows.SingleAsync(row => row.Id == id)).Offset;
                Assert.Equal(value.Ticks, actual.Ticks);
                Assert.Equal(value.Offset, actual.Offset);
                Assert.Equal(value.UtcTicks, actual.UtcTicks);
                Assert.Equal(1, await readContext.Rows.CountAsync(row => row.Offset == value));
            });

    [DamengFact]
    public Task SixDigitColumnsRejectValuesTheyCannotRepresent()
        => TemporalStore.WithTableAsync(
            async store =>
            {
                await using (var context = store.CreateContext())
                {
                    context.Rows.Add(new TemporalRow
                    {
                        Value = new DateTime(2026, 1, 1),
                        Micro = new DateTime(2026, 1, 1).AddTicks(1234567)
                    });

                    // The value is rejected before it is written; nothing is rounded or truncated.
                    await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
                }

                Assert.Equal("0", await store.ScalarStringAsync($"SELECT COUNT(*) FROM \"{store.TableName}\""));

                await using (var context = store.CreateContext())
                {
                    var exact = new DateTime(2026, 1, 1).AddTicks(1234560);
                    context.Rows.Add(new TemporalRow { Value = new DateTime(2026, 1, 1), Micro = exact });
                    await context.SaveChangesAsync();
                    Assert.Equal(exact, (await store.CreateContext().Rows.SingleAsync()).Micro);
                }
            });

    [DamengFact]
    public Task NineDigitValuesThatAreNotWholeTicksAreRejectedOnRead()
        => TemporalStore.WithTableAsync(
            async store =>
            {
                await store.ExecuteAsync(
                    $"INSERT INTO \"{store.TableName}\" (\"VALUE\", \"OFFSET\", \"NANO\") VALUES (TIMESTAMP '2026-01-01 00:00:00', TIMESTAMP '2026-01-01 00:00:00 +08:00', {Nine("2026-01-01 00:00:00.123456700")})");
                Assert.Equal("00:00:00.123456700", await store.ScalarStringAsync($"SELECT TO_CHAR(\"NANO\", 'HH24:MI:SS.FF9') FROM \"{store.TableName}\""));
                await using (var context = store.CreateContext())
                {
                    Assert.Equal(
                        new DateTime(2026, 1, 1).AddTicks(1234567),
                        (await context.Rows.SingleAsync()).Nano);
                }

                await store.ExecuteAsync(
                    $"UPDATE \"{store.TableName}\" SET \"NANO\" = {Nine("2026-01-01 00:00:00.123456789")}");
                await using (var context = store.CreateContext())
                {
                    var error = await Assert.ThrowsAnyAsync<Exception>(() => context.Rows.SingleAsync());
                    Assert.Contains(Flatten(error), current => current is OverflowException);
                }
            });

    [DamengFact]
    public Task WideningKeepsValuesAndTheGuardBlocksNarrowingThatWouldLoseDigits()
        => TemporalStore.WithRawTableAsync(
            "\"A\" TIMESTAMP(6), \"B\" TIMESTAMP(6)",
            async store =>
            {
                await store.ExecuteAsync(
                    $"INSERT INTO \"{store.TableName}\" (\"A\", \"B\") VALUES (TIMESTAMP '2026-01-01 01:02:03.123456', TIMESTAMP '2026-01-01 01:02:03.654321')");
                await using var context = store.CreateContext();
                var generator = context.GetService<IMigrationsSqlGenerator>();

                foreach (var column in GuardedColumns)
                {
                    await ExecuteMigrationAsync(context, generator, AlterPrecision(store.TableName, column, from: 6, to: 7));
                }

                Assert.Equal("01:02:03.1234560", await store.FractionTextAsync("A"));
                Assert.Equal("01:02:03.6543210", await store.FractionTextAsync("B"));

                await store.ExecuteAsync(
                    $"UPDATE \"{store.TableName}\" SET \"B\" = {Nine("2026-01-01 01:02:03.654321700")}");

                // Every column to be narrowed is checked before any ALTER runs.
                var guard = new SqlOperation { Sql = NarrowingGuard(store.TableName, GuardedColumns), SuppressTransaction = true };
                var error = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteMigrationAsync(context, generator, guard));
                Assert.Contains(Flatten(error), current => current is W.Dm.DmException { Number: -20071 });
                Assert.Equal("01:02:03.1234560", await store.FractionTextAsync("A"));
                Assert.Equal("01:02:03.6543217", await store.FractionTextAsync("B"));
                Assert.Equal(1, await context.Database.ExecuteSqlRawAsync(
                    $"UPDATE \"{store.TableName}\" SET \"B\" = {Nine("2026-01-01 01:02:03.654321000")}"));

                await ExecuteMigrationAsync(context, generator, guard);
                foreach (var column in GuardedColumns)
                {
                    await ExecuteMigrationAsync(context, generator, AlterPrecision(store.TableName, column, from: 7, to: 6));
                }

                Assert.Equal("01:02:03.1234560", await store.FractionTextAsync("A"));
                Assert.Equal("01:02:03.6543210", await store.FractionTextAsync("B"));
            });

    [DamengFact]
    public async Task MigrationsCanSwitchBetweenTheOfficialAndWDrivers()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        SwitchNames.Table = $"EF10_SWT_{suffix}";
        SwitchNames.History = $"EF10_SWH_{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        var officialConnectionString = OfficialConnectionString(connectionString);
        try
        {
            await using (var official = SwitchContext.Create(officialConnectionString, w: false))
            {
                Assert.Equal("W.EntityFrameworkCore.Dameng", official.Database.ProviderName);
                await official.GetService<IMigrator>().MigrateAsync(SwitchOne.Id);
            }

            await using (var w = SwitchContext.Create(connectionString, w: true))
            {
                Assert.Equal([SwitchOne.Id], await w.Database.GetAppliedMigrationsAsync());
                Assert.Equal([SwitchTwo.Id, SwitchThree.Id], await w.Database.GetPendingMigrationsAsync());
                await w.GetService<IMigrator>().MigrateAsync(SwitchThree.Id);
            }

            await using (var official = SwitchContext.Create(officialConnectionString, w: false))
            {
                Assert.Equal([SwitchOne.Id, SwitchTwo.Id, SwitchThree.Id], await official.Database.GetAppliedMigrationsAsync());
                Assert.Equal(
                    ["w"],
                    await official.Database.SqlQueryRaw<string>(
                        $"SELECT \"NOTE\" AS \"Value\" FROM \"{SwitchNames.Table}\"").ToListAsync());

                // Roll the last migration back with the official driver and re-apply it.
                await official.GetService<IMigrator>().MigrateAsync(SwitchTwo.Id);
                await official.GetService<IMigrator>().MigrateAsync(SwitchThree.Id);
                Assert.Equal(
                    ["official"],
                    await official.Database.SqlQueryRaw<string>(
                        $"SELECT \"NOTE\" AS \"Value\" FROM \"{SwitchNames.Table}\"").ToListAsync());
            }

            await using (var w = SwitchContext.Create(connectionString, w: true))
            {
                Assert.Empty(await w.Database.GetPendingMigrationsAsync());
                await w.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);
                Assert.Empty(await w.Database.GetAppliedMigrationsAsync());
            }
        }
        finally
        {
            await using var connection = new DmConnection(connectionString);
            await connection.OpenAsync();
            foreach (var table in new[] { SwitchNames.Table, SwitchNames.History })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = '{table}'";
                if (Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0)
                {
                    command.CommandText = $"DROP TABLE \"{table}\"";
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
    }

    // A bare TIMESTAMP literal is six-digit on the server; TO_TIMESTAMP keeps every digit.
    private static string Nine(string value)
        => $"TO_TIMESTAMP('{value}', 'YYYY-MM-DD HH24:MI:SS.FF9')";

    public static TheoryData<DateTime?> NineDigitWrites
        => new()
        {
            null,
            new DateTime(2026, 10, 15, 1, 2, 3).AddTicks(1234567),
            DateTime.MaxValue,
            DateTime.MinValue.AddTicks(1)
        };

    // W 0.2.0 binds Scale 9 on the verified profile: NULL and whole-tick values are written
    // without loss, so EF can write TIMESTAMP(9) columns instead of excluding them.
    [DamengTheory]
    [MemberData(nameof(NineDigitWrites))]
    public Task NineDigitColumnsAcceptNullAndWholeTickWrites(DateTime? value)
        => TemporalStore.WithTableAsync(
            async store =>
            {
                await using (var connection = new DmConnection(store.ConnectionString))
                {
                    await connection.OpenAsync();
                    Assert.Equal(9, connection.ServerCapabilities.MaxTemporalWriteScale);
                }

                var changed = new DateTime(2026, 10, 15, 4, 5, 6).AddTicks(7654321);
                var offset = value is null ? (DateTimeOffset?)null : new DateTimeOffset(value.Value.Ticks, TimeSpan.Zero);
                long id;
                await using (var context = store.CreateContext())
                {
                    var row = new TemporalRow { Value = new DateTime(2026, 1, 1), Nano = value, NanoOffset = offset };
                    context.Rows.Add(row);
                    await context.SaveChangesAsync();
                    id = row.Id;
                }

                Assert.Equal(NineDigitText(value), await store.ScalarStringAsync(
                    $"SELECT TO_CHAR(\"NANO\", 'YYYY-MM-DD HH24:MI:SS.FF9') FROM \"{store.TableName}\" WHERE \"ID\" = {id}"));
                Assert.Equal(NineDigitText(value), await store.ScalarStringAsync(
                    $"SELECT TO_CHAR(\"NANO_OFFSET\", 'YYYY-MM-DD HH24:MI:SS.FF9') FROM \"{store.TableName}\" WHERE \"ID\" = {id}"));

                await using (var context = store.CreateContext())
                {
                    var row = await context.Rows.SingleAsync(current => current.Id == id);
                    Assert.Equal(value, row.Nano);
                    Assert.Equal(offset, row.NanoOffset);

                    // The synchronous update path swaps NULL and a value in both columns.
                    row.Nano = value is null ? changed : null;
                    row.NanoOffset = value is null ? new DateTimeOffset(changed, TimeSpan.FromHours(8)) : null;
                    Assert.Equal(1, context.SaveChanges());
                }

                await using (var context = store.CreateContext())
                {
                    var row = await context.Rows.SingleAsync(current => current.Id == id);
                    Assert.Equal(value is null ? changed : null, row.Nano);
                    Assert.Equal(value is null ? new DateTimeOffset(changed, TimeSpan.FromHours(8)) : null, row.NanoOffset);
                }
            });

    [DamengFact]
    public Task EightDigitColumnsAcceptTheProviderParameterShape()
        => TemporalStore.WithRawTableAsync(
            "\"EIGHT\" TIMESTAMP(8)",
            async store =>
            {
                await using var connection = new DmConnection(store.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"INSERT INTO \"{store.TableName}\" (\"EIGHT\") VALUES (:v)";

                // The provider configures temporal parameters with Precision 0 and Scale only.
                var parameter = new DmParameter("v", new DateTime(2026, 1, 1).AddTicks(1234567)) { Precision = 0, Scale = 8 };
                command.Parameters.Add(parameter);
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
                parameter.Value = DBNull.Value;
                Assert.Equal(1, await command.ExecuteNonQueryAsync());

                Assert.Equal("00:00:00.12345670", await store.ScalarStringAsync(
                    $"SELECT TO_CHAR(\"EIGHT\", 'HH24:MI:SS.FF8') FROM \"{store.TableName}\" WHERE \"EIGHT\" IS NOT NULL"));
                Assert.Equal("1", await store.ScalarStringAsync(
                    $"SELECT COUNT(*) FROM \"{store.TableName}\" WHERE \"EIGHT\" IS NULL"));
            });

    private static string NineDigitText(DateTime? value)
        => value is null
            ? ""
            : value.Value.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "00";

    private static AlterColumnOperation AlterPrecision(string table, string column, int from, int to)
        => new()
        {
            Table = table,
            Name = column,
            ClrType = typeof(DateTime),
            ColumnType = $"TIMESTAMP({to})",
            IsNullable = true,
            OldColumn = new AddColumnOperation
            {
                Table = table,
                Name = column,
                ClrType = typeof(DateTime),
                ColumnType = $"TIMESTAMP({from})",
                IsNullable = true
            }
        };

    // Migration guidance fixture: reject narrowing while any column holds a non-whole-microsecond value.
    private static string NarrowingGuard(string table, IEnumerable<string> columns)
        => $"""
            DECLARE v_bad BIGINT;
            BEGIN
                SELECT COUNT(*) INTO v_bad FROM "{table}"
                 WHERE {string.Join(" OR ", columns.Select(column => $"MOD(TO_NUMBER(TO_CHAR(\"{column}\", 'FF9')), 1000) <> 0"))};
                IF v_bad > 0 THEN
                    RAISE_APPLICATION_ERROR(-20071, 'Seven-digit fractions would be lost by narrowing to six digits.');
                END IF;
            END;
            """;

    private static async Task ExecuteMigrationAsync(DbContext context, IMigrationsSqlGenerator generator, MigrationOperation operation)
    {
        foreach (var command in generator.Generate([operation], context.Model))
        {
            Assert.True(command.TransactionSuppressed, "Dameng DDL and guard blocks must run outside a transaction.");
            await context.Database.ExecuteSqlRawAsync(command.CommandText);
        }
    }

    private static IEnumerable<Exception> Flatten(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static string OfficialConnectionString(string wConnectionString)
    {
        var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = wConnectionString };
        builder.Remove("transport_security");
        return builder.ConnectionString;
    }

    private sealed class TemporalStore
    {
        private TemporalStore()
        {
            ConnectionString = DamengTestEnvironment.GetRequiredConnectionString();
            TableName = "EF10_WTP_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        }

        public string ConnectionString { get; }

        public string TableName { get; }

        public static async Task WithTableAsync(Func<TemporalStore, Task> test)
        {
            var store = new TemporalStore();
            await store.WithCleanupAsync(
                async () =>
                {
                    await using (var context = store.CreateContext())
                    {
                        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
                    }

                    await test(store);
                });
        }

        public static async Task WithRawTableAsync(string columns, Func<TemporalStore, Task> test)
        {
            var store = new TemporalStore();
            await store.WithCleanupAsync(
                async () =>
                {
                    await store.ExecuteAsync($"CREATE TABLE \"{store.TableName}\" ({columns})");
                    await test(store);
                });
        }

        public TemporalContext CreateContext()
            => new(
                new DbContextOptionsBuilder<TemporalContext>()
                    .UseDamengUnderTest(ConnectionString)
                    .ReplaceService<IModelCacheKeyFactory, TemporalModelCacheKeyFactory>()

                    // Per-table model keys add internal service providers across the suite.
                    .ConfigureWarnings(warnings => warnings.Log(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
                    .Options,
                TableName);

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<string?> ScalarStringAsync(string sql)
        {
            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        public Task<string?> FractionTextAsync(string column)
            => ScalarStringAsync($"SELECT TO_CHAR(\"{column}\", 'HH24:MI:SS.FF7') FROM \"{TableName}\"");

        private async Task WithCleanupAsync(Func<Task> test)
        {
            try
            {
                await test();
            }
            finally
            {
                if (await ScalarStringAsync($"SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = '{TableName}'") != "0")
                {
                    await ExecuteAsync($"DROP TABLE \"{TableName}\"");
                }
            }
        }
    }

    private sealed class TemporalContext(DbContextOptions<TemporalContext> options, string tableName) : DbContext(options)
    {
        public string TableName { get; } = tableName;

        public DbSet<TemporalRow> Rows
            => Set<TemporalRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<TemporalRow>(
                entity =>
                {
                    entity.ToTable(TableName);
                    entity.Property(row => row.Id).HasColumnName("ID");
                    entity.Property(row => row.Value).HasColumnName("VALUE");
                    entity.Property(row => row.Offset).HasColumnName("OFFSET");
                    entity.Property(row => row.Micro).HasColumnName("MICRO").HasPrecision(6);
                    entity.Property(row => row.Nano).HasColumnName("NANO").HasColumnType("TIMESTAMP(9)");
                    entity.Property(row => row.NanoOffset).HasColumnName("NANO_OFFSET").HasColumnType("TIMESTAMP(9) WITH TIME ZONE");
                });
    }

    private sealed class TemporalModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), ((TemporalContext)context).TableName, designTime);
    }

    private sealed class TemporalRow
    {
        public long Id { get; set; }

        public DateTime Value { get; set; }

        public DateTimeOffset Offset { get; set; }

        public DateTime? Micro { get; set; }

        public DateTime? Nano { get; set; }

        public DateTimeOffset? NanoOffset { get; set; }
    }
}

internal static class SwitchNames
{
    public static string Table { get; set; } = "";

    public static string History { get; set; } = "";
}

/// <summary>A context whose migrations are applied alternately by the official and W drivers.</summary>
public sealed class SwitchContext(DbContextOptions<SwitchContext> options) : DbContext(options)
{
    internal static SwitchContext Create(string connectionString, bool w)
    {
        var builder = new DbContextOptionsBuilder<SwitchContext>()
            .ConfigureWarnings(warnings => warnings.Log(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning));
        Action<DamengDbContextOptionsBuilder> configure = dameng => dameng
            .MigrationsHistoryTable(SwitchNames.History)
            .MigrationsAssembly(typeof(SwitchContext).Assembly.GetName().Name);
        if (w)
        {
            builder.UseDamengWithWDriver(connectionString, configure);
        }
        else
        {
            builder.UseDameng(connectionString, configure);
        }

        return new SwitchContext(builder.Options);
    }
}

[DbContext(typeof(SwitchContext))]
[Migration(Id)]
public sealed class SwitchOne : Migration
{
    public const string Id = "20261014000001_SwitchOne";

    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.CreateTable(
            name: SwitchNames.Table,
            columns: table => new
            {
                ID = table.Column<long>(type: "BIGINT", nullable: false),
                NAME = table.Column<string>(type: "NVARCHAR2(50)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_" + SwitchNames.Table, item => item.ID));

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(SwitchNames.Table);
}

[DbContext(typeof(SwitchContext))]
[Migration(Id)]
public sealed class SwitchTwo : Migration
{
    public const string Id = "20261014000002_SwitchTwo";

    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(name: "NOTE", table: SwitchNames.Table, type: "NVARCHAR2(20)", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "NOTE", table: SwitchNames.Table);
}

/// <summary>
/// Branches on <see cref="MigrationBuilder.ActiveProvider" />: migrations that compare against
/// one provider name take the other branch after switching drivers, so such migrations must
/// accept both names explicitly.
/// </summary>
[DbContext(typeof(SwitchContext))]
[Migration(Id)]
public sealed class SwitchThree : Migration
{
    public const string Id = "20261014000003_SwitchThree";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var note = migrationBuilder.ActiveProvider switch
        {
            "W.EntityFrameworkCore.Dameng" => "official",
            "W.EntityFrameworkCore.Dameng.WDriver" => "w",
            _ => throw new NotSupportedException(migrationBuilder.ActiveProvider)
        };
        migrationBuilder.Sql($"INSERT INTO \"{SwitchNames.Table}\" (\"ID\", \"NOTE\") VALUES (1, '{note}')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql($"DELETE FROM \"{SwitchNames.Table}\" WHERE \"ID\" = 1");
}
