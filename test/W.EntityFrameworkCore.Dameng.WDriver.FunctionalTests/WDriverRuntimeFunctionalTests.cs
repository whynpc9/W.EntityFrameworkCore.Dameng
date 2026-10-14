#pragma warning disable EF1002 // SQL text only interpolates generated test object names.

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

/// <summary>
/// W.DamengProvider runtime contracts observed through EF Core on the real server: asynchronous
/// network calls, cancellation and deadlines, failure metadata, conditional session recovery,
/// EF savepoints, and credential-free diagnostics. These tests only run in the W lane.
/// </summary>
public sealed class WDriverRuntimeFunctionalTests
{
    private static readonly TimeSpan ServerDelay = TimeSpan.FromSeconds(2);

    [DamengFact]
    public Task QueryAsyncReturnsBeforeTheServerCompletes()
        => RuntimeStore.WithStoreAsync(
            async store =>
            {
                await using var context = store.CreateContext();
                var watch = Stopwatch.StartNew();

                var pending = context.Database
                    .SqlQueryRaw<int>($"SELECT \"{store.SleepFunction}\"(2) AS \"Value\" FROM DUAL")
                    .ToListAsync();
                var returnedAfter = watch.Elapsed;

                Assert.False(pending.IsCompleted, "The query task completed before the server delay ended.");
                Assert.True(returnedAfter < TimeSpan.FromSeconds(1), $"ToListAsync blocked the caller for {returnedAfter}.");
                Assert.Equal([2], await pending);
                Assert.True(watch.Elapsed >= ServerDelay - TimeSpan.FromMilliseconds(200));
            });

    [DamengFact]
    public Task SaveChangesAsyncReturnsBeforeTheServerCompletes()
        => RuntimeStore.WithStoreAsync(
            async store =>
            {
                await store.CreateSlowInsertTriggerAsync(seconds: 2);
                await using var context = store.CreateContext();
                context.Items.Add(new RuntimeItem { Name = "异步保存" });
                var watch = Stopwatch.StartNew();

                var pending = context.SaveChangesAsync();
                var returnedAfter = watch.Elapsed;

                Assert.False(pending.IsCompleted, "SaveChangesAsync completed before the trigger delay ended.");
                Assert.True(returnedAfter < TimeSpan.FromSeconds(1), $"SaveChangesAsync blocked the caller for {returnedAfter}.");
                Assert.Equal(1, await pending);
                Assert.Equal(1, await store.CountAsync("异步保存"));
            });

    [DamengFact]
    public Task PreCanceledTokenFailsWithoutBreakingTheContext()
        => RuntimeStore.WithStoreAsync(
            async store =>
            {
                await using var context = store.CreateContext();
                using var canceled = new CancellationTokenSource();
                await canceled.CancelAsync();

                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => context.Items.ToListAsync(canceled.Token));

                Assert.Equal(canceled.Token, error.CancellationToken);
                Assert.Empty(await context.Items.ToListAsync());
            });

    [DamengFact]
    public Task CancelingARunningQueryKeepsTheTokenAndUsesANewSessionAfterwards()
        => RuntimeStore.WithStoreAsync(
            async store =>
            {
                await using var context = store.CreateContext();
                var sessionBefore = await SessionIdAsync(context);
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                var watch = Stopwatch.StartNew();

                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => context.Database
                        .SqlQueryRaw<int>($"SELECT \"{store.SleepFunction}\"(5) AS \"Value\" FROM DUAL")
                        .ToListAsync(cancellation.Token));

                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), $"Cancellation took {watch.Elapsed}.");
                Assert.Equal(cancellation.Token, error.CancellationToken);
                var canceled = Assert.IsType<W.Dm.DmOperationCanceledException>(error);
                Assert.Equal(W.Dm.DmCancelSource.User, canceled.FailureInfo.CancelSource);
                Assert.False(canceled.FailureInfo.ConnectionReusable);

                // Sent-then-canceled closes the session; EF opens a new one for the next operation.
                Assert.NotEqual(sessionBefore, await SessionIdAsync(context));
            });

    [DamengFact]
    public Task CommandTimeoutReportsAClientDeadline()
        => RuntimeStore.WithStoreAsync(
            async store =>
            {
                await using var context = store.CreateContext(dameng => dameng.CommandTimeout(1));
                var watch = Stopwatch.StartNew();

                var error = await Assert.ThrowsAsync<W.Dm.DmTimeoutException>(
                    () => context.Database
                        .SqlQueryRaw<int>($"SELECT \"{store.SleepFunction}\"(4) AS \"Value\" FROM DUAL")
                        .ToListAsync());

                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3.5), $"The deadline took {watch.Elapsed}.");
                Assert.Equal(W.Dm.DmErrorKind.Timeout, error.FailureInfo.ErrorKind);
                // EF's CommandTimeout becomes the W command's total deadline.
                Assert.Equal(W.Dm.DmCancelSource.TotalDeadline, error.FailureInfo.CancelSource);
                Assert.Equal(0, error.Number);
                Assert.Empty(await context.Items.ToListAsync());
            });

    [DamengFact]
    public Task CanceledSaveChangesInsideATransactionDoesNotCommitAndReportsAnUnknownOutcome()
        => RuntimeStore.WithStoreAsync(
            async store =>
            {
                await store.CreateSlowInsertTriggerAsync(seconds: 4);
                await using (var context = store.CreateContext())
                {
                    await using var transaction = await context.Database.BeginTransactionAsync();
                    context.Items.Add(new RuntimeItem { Name = "取消写入" });
                    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

                    var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                        () => context.SaveChangesAsync(cancellation.Token));

                    var canceled = Assert.IsType<W.Dm.DmOperationCanceledException>(error);
                    Assert.Equal(cancellation.Token, canceled.CancellationToken);
                    Assert.Equal(W.Dm.DmOperationOutcome.Unknown, canceled.FailureInfo.OperationOutcome);
                    Assert.False(canceled.FailureInfo.ConnectionReusable);
                    await Assert.ThrowsAnyAsync<Exception>(() => transaction.CommitAsync());
                }

                // The client exception does not prove a rollback; read the final state independently.
                Assert.Equal(0, await store.CountAsync("取消写入"));
            });

    [DamengFact]
    public Task AnOldTokenDoesNotCancelANewCall()
        => RuntimeStore.WithStoreAsync(
            async store =>
            {
                await using var context = store.CreateContext();
                using var first = new CancellationTokenSource();
                Assert.Empty(await context.Items.ToListAsync(first.Token));
                await first.CancelAsync();

                using var second = new CancellationTokenSource();
                Assert.Equal(
                    [1],
                    await context.Database
                        .SqlQueryRaw<int>($"SELECT \"{store.SleepFunction}\"(1) AS \"Value\" FROM DUAL")
                        .ToListAsync(second.Token));
            });

    public static TheoryData<string, bool, bool> RecoverableErrors
    {
        get
        {
            var data = new TheoryData<string, bool, bool>();
            foreach (var kind in new[] { "unique", "foreign-key", "not-null", "raise", "sequence-exhausted", "unbound-marker" })
            {
                foreach (var async in new[] { false, true })
                {
                    foreach (var transaction in new[] { false, true })
                    {
                        data.Add(kind, async, transaction);
                    }
                }
            }

            return data;
        }
    }

    [DamengTheory]
    [MemberData(nameof(RecoverableErrors))]
    public Task MeasuredStatementErrorsKeepTheSessionAndTheTransaction(string kind, bool async, bool transaction)
        => RuntimeStore.WithStoreAsync(
            async store =>
            {
                await using var context = store.CreateContext();
                await context.Database.OpenConnectionAsync();
                var sessionBefore = await SessionIdAsync(context);
                await using var dbTransaction = transaction
                    ? await context.Database.BeginTransactionAsync()
                    : null;
                context.Items.Add(new RuntimeItem { Name = "恢复前" });
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();

                var error = await CaptureAsync(() => TriggerErrorAsync(context, store, kind, async));

                var driverError = FindDriverException(error);
                Assert.True(driverError is not null, error.ToString());
                Assert.Equal(ExpectedErrorNumber(kind), driverError.Number);
                Assert.Equal(W.Dm.DmErrorKind.Server, driverError.ErrorKind);
                Assert.Equal(W.Dm.DmOperationOutcome.ServerReported, driverError.FailureInfo.OperationOutcome);
                Assert.True(driverError.FailureInfo.ConnectionReusable);
                Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
                Assert.Equal(sessionBefore, await SessionIdAsync(context));
                context.ChangeTracker.Clear();

                context.Items.Add(new RuntimeItem { Name = "恢复后" });
                await context.SaveChangesAsync();
                if (dbTransaction is not null)
                {
                    await dbTransaction.CommitAsync();
                }

                Assert.Equal(1, await store.CountAsync("恢复前"));
                Assert.Equal(1, await store.CountAsync("恢复后"));
                Assert.Equal(2, await store.CountAsync(null));
            });

    [DamengFact]
    public Task AnonymousBlocksAreRejectedInsideATransactionBeforeSending()
        => RuntimeStore.WithStoreAsync(
            async store =>
            {
                await using (var context = store.CreateContext())
                {
                    await context.Database.OpenConnectionAsync();
                    var sessionBefore = await SessionIdAsync(context);
                    await using var transaction = await context.Database.BeginTransactionAsync();
                    context.Items.Add(new RuntimeItem { Name = "块前" });
                    await context.SaveChangesAsync();

                    // W refuses dynamic control SQL in a local transaction; nothing is sent.
                    await Assert.ThrowsAsync<NotSupportedException>(
                        () => context.Database.ExecuteSqlRawAsync(
                            $"BEGIN UPDATE \"{store.TableName}\" SET \"NAME\" = 'block'; END;"));

                    Assert.Equal(sessionBefore, await SessionIdAsync(context));
                    await transaction.CommitAsync();
                }

                Assert.Equal(1, await store.CountAsync("块前"));
                Assert.Equal(0, await store.CountAsync("block"));

                // Outside a transaction the same block runs.
                await using (var context = store.CreateContext())
                {
                    await context.Database.ExecuteSqlRawAsync(
                        $"BEGIN UPDATE \"{store.TableName}\" SET \"NAME\" = 'block'; END;");
                }

                Assert.Equal(1, await store.CountAsync("block"));
            });

    [DamengFact]
    public Task FailedSaveChangesInsideATransactionRollsBackToTheEfSavepoint()
        => RuntimeStore.WithStoreAsync(
            async store =>
            {
                await using (var context = store.CreateContext())
                {
                    await using var transaction = await context.Database.BeginTransactionAsync();
                    Assert.True(transaction.SupportsSavepoints);
                    context.Items.Add(new RuntimeItem { Name = "保存点-保留" });
                    await context.SaveChangesAsync();

                    context.Items.Add(new RuntimeItem { Name = "保存点-批内" });
                    context.Items.Add(new RuntimeItem { Name = "保存点-保留" });
                    await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
                    context.ChangeTracker.Clear();

                    await transaction.CreateSavepointAsync("EXPLICIT_SP");
                    context.Items.Add(new RuntimeItem { Name = "保存点-显式回滚" });
                    await context.SaveChangesAsync();
                    await transaction.RollbackToSavepointAsync("EXPLICIT_SP");
                    context.ChangeTracker.Clear();

                    context.Items.Add(new RuntimeItem { Name = "保存点-提交" });
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }

                Assert.Equal(1, await store.CountAsync("保存点-保留"));
                Assert.Equal(0, await store.CountAsync("保存点-批内"));
                Assert.Equal(0, await store.CountAsync("保存点-显式回滚"));
                Assert.Equal(1, await store.CountAsync("保存点-提交"));
            });

    [DamengFact]
    public Task PrepareFailureBreaksTheSessionAndEfOpensANewOne()
        => RuntimeStore.WithStoreAsync(
            async store =>
            {
                await using var context = store.CreateContext();
                var sessionBefore = await SessionIdAsync(context);

                // A statement the server rejects while preparing is outside the measured
                // execute-time recovery contract: W marks the session Broken.
                var error = await Assert.ThrowsAnyAsync<DbException>(
                    () => context.Database.ExecuteSqlRawAsync("SELECT SLEEP(1) FROM DUAL"));

                Assert.False(FindDriverException(error)?.FailureInfo?.ConnectionReusable ?? false);
                Assert.Empty(await context.Items.ToListAsync());
                Assert.NotEqual(sessionBefore, await SessionIdAsync(context));
            });

    [DamengFact]
    public Task ParameterizedCommandsBindStrictly()
        => RuntimeStore.WithStoreAsync(
            async store =>
            {
                await using var context = store.CreateContext();
                context.Items.Add(new RuntimeItem { Name = "绑定" });
                await context.SaveChangesAsync();

                // Marker names must match the supplied parameters; nothing is rewritten in EF.
                var unused = new W.Dm.DmParameter("unused", 1);
                await Assert.ThrowsAnyAsync<Exception>(
                    () => context.Database.ExecuteSqlRawAsync(
                        $"UPDATE \"{store.TableName}\" SET \"NAME\" = 'changed'",
                        unused));
                Assert.Equal(1, await store.CountAsync("绑定"));

                Assert.Equal(
                    1,
                    await context.Database.ExecuteSqlRawAsync(
                        $"UPDATE \"{store.TableName}\" SET \"NAME\" = {{0}} WHERE \"NAME\" = {{1}}",
                        "已绑定",
                        "绑定"));
                Assert.Equal(1, await store.CountAsync("已绑定"));
            });

    [DamengFact]
    public Task DriverDiagnosticsAreObservableWithoutSqlValuesOrCredentials()
        => RuntimeStore.WithStoreAsync(
            async store =>
            {
                const string valueSentinel = "VALUE_SENTINEL_7F3A";
                var activities = new List<Activity>();
                var instruments = new List<string>();
                using var listener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name == W.Dm.DmDiagnostics.ActivitySourceName,
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStopped = activity =>
                    {
                        lock (activities)
                        {
                            activities.Add(activity);
                        }
                    }
                };
                ActivitySource.AddActivityListener(listener);
                using var meterListener = new MeterListener
                {
                    InstrumentPublished = (instrument, _) =>
                    {
                        if (instrument.Meter.Name == W.Dm.DmDiagnostics.MeterName)
                        {
                            lock (instruments)
                            {
                                instruments.Add(instrument.Name);
                            }
                        }
                    }
                };
                meterListener.Start();

                await using (var context = store.CreateContext())
                {
                    context.Items.Add(new RuntimeItem { Name = valueSentinel });
                    await context.SaveChangesAsync();
                    Assert.Single(await context.Items.Where(item => item.Name == valueSentinel).ToListAsync());
                }

                // Activity export runs on a bounded background dispatcher.
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline)
                {
                    lock (activities)
                    {
                        if (activities.Count > 0)
                        {
                            break;
                        }
                    }

                    await Task.Delay(50);
                }

                List<Activity> captured;
                lock (activities)
                {
                    captured = [.. activities];
                }

                Assert.NotEmpty(captured);
                var secrets = SensitiveValues(store.ConnectionString).Append(valueSentinel).Append(store.TableName).ToList();
                foreach (var activity in captured)
                {
                    var text = string.Join(
                        "\n",
                        new[] { activity.DisplayName, activity.OperationName, activity.StatusDescription ?? "" }
                            .Concat(activity.TagObjects.Select(tag => tag.Key + "=" + tag.Value))
                            .Concat(activity.Events.Select(item => item.Name)));
                    foreach (var secret in secrets)
                    {
                        Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
                    }

                    Assert.DoesNotContain("SELECT", text, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("INSERT", text, StringComparison.OrdinalIgnoreCase);
                }
            });

    private static async Task TriggerErrorAsync(RuntimeContext context, RuntimeStore store, string kind, bool async)
    {
        switch (kind)
        {
            case "unique":
                context.Items.Add(new RuntimeItem { Name = "恢复前" });
                await SaveAsync(context, async);
                break;
            case "foreign-key":
                context.Items.Add(new RuntimeItem { Name = "外键", ParentId = -1 });
                await SaveAsync(context, async);
                break;
            case "not-null":
                await ExecuteAsync(context, $"INSERT INTO \"{store.TableName}\" (\"NAME\") VALUES (NULL)", async);
                break;
            case "raise":
                // A trigger raises the user error so the statement is legal inside a transaction.
                context.Items.Add(new RuntimeItem { Name = RuntimeStore.RaiseName });
                await SaveAsync(context, async);
                break;
            case "sequence-exhausted":
                await ExecuteAsync(context, $"SELECT \"{store.SequenceName}\".NEXTVAL FROM DUAL", async);
                break;
            case "unbound-marker":
                // No parameters: W leaves the marker to the server, which reports -6804.
                await ExecuteAsync(context, $"UPDATE \"{store.TableName}\" SET \"NAME\" = :X WHERE 1 = 0", async);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static int ExpectedErrorNumber(string kind)
        => kind switch
        {
            "unique" => -6602,
            "foreign-key" => -6607,
            "not-null" => -6609,
            "raise" => -20071,
            "sequence-exhausted" => -7069,
            "unbound-marker" => -6804,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static async Task SaveAsync(RuntimeContext context, bool async)
    {
        if (async)
        {
            await context.SaveChangesAsync();
        }
        else
        {
            context.SaveChanges();
        }
    }

    private static async Task ExecuteAsync(RuntimeContext context, string sql, bool async)
    {
        if (async)
        {
            await context.Database.ExecuteSqlRawAsync(sql);
        }
        else
        {
            context.Database.ExecuteSqlRaw(sql);
        }
    }

    private static async Task<Exception> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error)
        {
            return error;
        }

        throw new Xunit.Sdk.XunitException("Expected the statement to fail.");
    }

    private static W.Dm.DmException? FindDriverException(Exception? error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            if (current is W.Dm.DmException driverError)
            {
                return driverError;
            }
        }

        return null;
    }

    private static async Task<long> SessionIdAsync(DbContext context)
        => (await context.Database.SqlQueryRaw<long>("SELECT SESSID() AS \"Value\" FROM DUAL").ToListAsync()).Single();

    private static IEnumerable<string> SensitiveValues(string connectionString)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        foreach (string key in builder.Keys)
        {
            if (key.Contains("pwd", StringComparison.OrdinalIgnoreCase)
                || key.Contains("password", StringComparison.OrdinalIgnoreCase)
                || key.Contains("uid", StringComparison.OrdinalIgnoreCase)
                || key.Contains("user", StringComparison.OrdinalIgnoreCase)
                || key.Contains("server", StringComparison.OrdinalIgnoreCase)
                || key.Contains("host", StringComparison.OrdinalIgnoreCase))
            {
                var value = Convert.ToString(builder[key], CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(value))
                {
                    yield return value;
                }
            }
        }

        yield return connectionString;
    }

    private sealed class RuntimeStore
    {
        private readonly List<string> _cleanup = [];

        private RuntimeStore()
        {
            ConnectionString = DamengTestEnvironment.GetRequiredConnectionString();
            var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
            TableName = $"EF10_WRT_{suffix}";
            ParentName = $"EF10_WRP_{suffix}";
            SequenceName = $"EF10_WRS_{suffix}";
            SleepFunction = $"EF10_WRF_{suffix}";
            TriggerName = $"EF10_WRG_{suffix}";
            Suffix = suffix;
        }

        public string ConnectionString { get; }

        public string Suffix { get; }

        public string TableName { get; }

        public string ParentName { get; }

        public string SequenceName { get; }

        public string SleepFunction { get; }

        public string TriggerName { get; }

        public const string RaiseName = "RAISE-GUARD";

        public static async Task WithStoreAsync(Func<RuntimeStore, Task> test)
        {
            var store = new RuntimeStore();
            try
            {
                await store.CreateAsync();
                await test(store);
            }
            finally
            {
                await store.DropAsync();
            }
        }

        public RuntimeContext CreateContext(Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
            => new(
                new DbContextOptionsBuilder<RuntimeContext>()
                    .UseDamengUnderTest(ConnectionString, damengOptionsAction)
                    .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, RuntimeModelCacheKeyFactory>()

                    // Per-table model keys add internal service providers across the suite.
                    .ConfigureWarnings(warnings => warnings.Log(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
                    .Options,
                TableName);

        public async Task CreateSlowInsertTriggerAsync(int seconds)
        {
            await ExecuteAsync(
                $"""
                CREATE TRIGGER "{TriggerName}" BEFORE INSERT ON "{TableName}" FOR EACH ROW
                BEGIN
                    DBMS_LOCK.SLEEP({seconds.ToString(CultureInfo.InvariantCulture)});
                END;
                """);
            _cleanup.Add($"DROP TRIGGER \"{TriggerName}\"");
        }

        public async Task<int> CountAsync(string? name)
        {
            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = name is null
                ? $"SELECT COUNT(*) FROM \"{TableName}\""
                : $"SELECT COUNT(*) FROM \"{TableName}\" WHERE \"NAME\" = :name";
            if (name is not null)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "name";
                parameter.Value = name;
                command.Parameters.Add(parameter);
            }

            return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        private async Task CreateAsync()
        {
            await ExecuteAsync($"CREATE TABLE \"{ParentName}\" (\"ID\" BIGINT NOT NULL, CONSTRAINT \"PK_{ParentName}\" PRIMARY KEY (\"ID\"))");
            _cleanup.Add($"DROP TABLE \"{ParentName}\"");
            await ExecuteAsync(
                $"""
                CREATE TABLE "{TableName}" (
                    "ID" BIGINT IDENTITY(1,1) NOT NULL,
                    "NAME" NVARCHAR2(100) NOT NULL,
                    "PARENT_ID" BIGINT NULL,
                    CONSTRAINT "PK_{TableName}" PRIMARY KEY ("ID"),
                    CONSTRAINT "UQ_{TableName}" UNIQUE ("NAME"),
                    CONSTRAINT "FK_{TableName}" FOREIGN KEY ("PARENT_ID") REFERENCES "{ParentName}" ("ID")
                )
                """);
            _cleanup.Add($"DROP TABLE \"{TableName}\"");
            await ExecuteAsync($"CREATE SEQUENCE \"{SequenceName}\" START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 2 NOCYCLE NOCACHE");
            _cleanup.Add($"DROP SEQUENCE \"{SequenceName}\"");
            await ExecuteAsync($"SELECT \"{SequenceName}\".NEXTVAL FROM DUAL");
            await ExecuteAsync($"SELECT \"{SequenceName}\".NEXTVAL FROM DUAL");
            await ExecuteAsync(
                $"""
                CREATE TRIGGER "{TriggerName}_R" BEFORE INSERT ON "{TableName}" FOR EACH ROW
                BEGIN
                    IF :NEW."NAME" = '{RaiseName}' THEN
                        RAISE_APPLICATION_ERROR(-20071, 'guard');
                    END IF;
                END;
                """);
            _cleanup.Add($"DROP TRIGGER \"{TriggerName}_R\"");
            await ExecuteAsync(
                $"""
                CREATE OR REPLACE FUNCTION "{SleepFunction}"("SECONDS" INT) RETURN INT AS
                BEGIN
                    DBMS_LOCK.SLEEP("SECONDS");
                    RETURN "SECONDS";
                END;
                """);
            _cleanup.Add($"DROP FUNCTION \"{SleepFunction}\"");
        }

        private async Task DropAsync()
        {
            foreach (var sql in Enumerable.Reverse(_cleanup))
            {
                await ExecuteAsync(sql);
            }
        }

        private async Task ExecuteAsync(string sql)
        {
            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class RuntimeContext(DbContextOptions<RuntimeContext> options, string tableName) : DbContext(options)
    {
        public string TableName { get; } = tableName;

        public DbSet<RuntimeItem> Items
            => Set<RuntimeItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<RuntimeItem>(
                entity =>
                {
                    entity.ToTable(TableName);
                    entity.HasKey(item => item.Id);
                    entity.Property(item => item.Id).HasColumnName("ID");
                    entity.Property(item => item.Name).HasColumnName("NAME").HasMaxLength(100);
                    entity.Property(item => item.ParentId).HasColumnName("PARENT_ID");
                });
    }

    private sealed class RuntimeModelCacheKeyFactory : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), ((RuntimeContext)context).TableName, designTime);
    }

    private sealed class RuntimeItem
    {
        public long Id { get; set; }

        public required string Name { get; set; }

        public long? ParentId { get; set; }
    }
}
