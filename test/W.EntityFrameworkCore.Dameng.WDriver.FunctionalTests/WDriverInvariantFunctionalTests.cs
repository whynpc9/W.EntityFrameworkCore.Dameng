#pragma warning disable EF1001
#pragma warning disable EF1002 // Only generated object names enter SQL.
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Trait("AuditPlan", "009")]
public sealed class WDriverInvariantFunctionalTests(ITestOutputHelper output)
{
    [DamengFact]
    public async Task BothDriversSameDriverExplicitModelRoundtripsTemporalValues()
    {
        foreach (var w in new[] { false, true })
        {
            var table = "EF10_INV_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
            var counter = new IoCounter();
            try
            {
                await using var source = Context(w, table, counter);
                await source.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
                var model = source.Model;
                var value = new DateTime(2026, 10, 28, 1, 2, 3).AddTicks(1234560);
                var offset = new DateTimeOffset(value, TimeSpan.FromHours(8));
                var time = new TimeOnly(1, 2, 3).Add(TimeSpan.FromTicks(1234560));
                await using (var write = Context(w, table, counter, model))
                {
                    write.Add(new Row { Id = 1, When = value, Offset = offset, Time = time });
                    Assert.Equal(1, await write.SaveChangesAsync());
                }
                await using (var read = Context(w, table, counter, model))
                {
                    // Parameter-free projection exercises the selected driver's reader mapping.
                    Assert.Equal(offset, await read.Set<Row>().Select(r => r.Offset).SingleAsync());
                    var id = 1;
                    var row = await read.Set<Row>().SingleAsync(r => r.Id == id);
                    Assert.Equal(value.Ticks, row.When.Ticks);
                    Assert.Equal(offset.Ticks, row.Offset.Ticks);
                    Assert.Equal(offset.Offset, row.Offset.Offset);
                    Assert.Equal(time, row.Time);
                    row.When = value.AddSeconds(1); row.Offset = offset.AddSeconds(1); row.Time = time.Add(TimeSpan.FromSeconds(1));
                    Assert.Equal(1, read.SaveChanges());
                }
                await using (var verify = Context(w, table, counter, model))
                {
                    var row = await verify.Set<Row>().SingleAsync();
                    Assert.Equal(value.AddSeconds(1), row.When);
                    Assert.Equal(offset.AddSeconds(1).Ticks, row.Offset.Ticks);
                    Assert.Equal(offset.Offset, row.Offset.Offset);
                    Assert.Equal(time.Add(TimeSpan.FromSeconds(1)), row.Time);
                }
                Assert.True(counter.Opens > 0);
                Assert.True(counter.Commands > 0);
                output.WriteLine($"Driver={(w ? "W" : "Official")}: UseModel parameter-free/parameter read, temporal insert/update/read passed; physical Open/Execute observed.");
            }
            finally
            {
                await using var cleanup = Context(w, table, new IoCounter());
                var count = await cleanup.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS \"Value\" FROM USER_TABLES WHERE TABLE_NAME = '{table}'").SingleAsync();
                if (count > 0) await cleanup.Database.ExecuteSqlRawAsync($"DROP TABLE \"{table}\" PURGE");
                Assert.Equal(0, await cleanup.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS \"Value\" FROM USER_TABLES WHERE TABLE_NAME = '{table}'").SingleAsync());
                output.WriteLine($"Driver={(w ? "W" : "Official")}: exact table cleanup verified.");
            }
        }
    }

    [DamengFact]
    public async Task BothDriversForeignExplicitModelFailsBeforeOpeningDatabase()
    {
        foreach (var sourceW in new[] { false, true })
        {
            await using var source = Context(sourceW, "EF10_NO_IO", new IoCounter());
            var model = source.Model;
            foreach (var write in new[] { false, true })
            {
                var counter = new IoCounter();
                await using var target = Context(!sourceW, "EF10_NO_IO", counter, model);
                var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    if (write) { target.Add(new Row { Id = 1 }); await target.SaveChangesAsync(); }
                    else _ = await target.Set<Row>().Select(r => r.Offset).ToListAsync();
                });
                Assert.Equal("Dameng:ForeignModel", error.Data["Dameng:ErrorId"]);
                Assert.Equal(0, counter.Opens); Assert.Equal(0, counter.Commands);
            }
            output.WriteLine($"Source={(sourceW ? "W" : "Official")}: foreign UseModel read/write refused before Open/Execute; no objects created.");
        }
    }

    private static InvariantContext Context(bool w, string table, IoCounter counter, IModel? model = null)
    {
        var connection = DamengTestEnvironment.GetRequiredConnectionString();
        var builder = new DbContextOptionsBuilder<InvariantContext>()
            .ReplaceService<IModelCacheKeyFactory, KeyFactory>()
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
            .AddInterceptors(counter);
        if (w) builder.UseDamengWithWDriver(connection);
        else
        {
            var official = new DbConnectionStringBuilder { ConnectionString = connection };
            official.Remove("transport_security");
            builder.UseDameng(official.ConnectionString);
        }
        if (model is not null) builder.UseModel(model);
        return new InvariantContext(builder.Options, table);
    }
    private sealed class InvariantContext(DbContextOptions<InvariantContext> options, string table) : DbContext(options)
    {
        public string Table { get; } = table;
        protected override void OnModelCreating(ModelBuilder builder)
        {
            var entity = builder.Entity<Row>();
            entity.ToTable(Table);
            entity.Property(r => r.Id).ValueGeneratedNever();
            entity.Property(r => r.When).HasPrecision(6);
            entity.Property(r => r.Offset).HasPrecision(6);
            entity.Property(r => r.Time).HasPrecision(6);
        }
    }
    private sealed class KeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), ((InvariantContext)context).Table, designTime);
    }
    private sealed class Row
    {
        public int Id { get; set; }
        public DateTime When { get; set; }
        public DateTimeOffset Offset { get; set; }
        public TimeOnly Time { get; set; }
    }
    private sealed class IoCounter : DbConnectionInterceptor, IDbCommandInterceptor
    {
        public int Opens { get; private set; }
        public int Commands { get; private set; }
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) { Opens++; return result; }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) { Opens++; return ValueTask.FromResult(result); }
        public InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { Commands++; return result; }
        public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Commands++; return ValueTask.FromResult(result); }
        public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) { Commands++; return result; }
        public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Commands++; return ValueTask.FromResult(result); }
    }
}
