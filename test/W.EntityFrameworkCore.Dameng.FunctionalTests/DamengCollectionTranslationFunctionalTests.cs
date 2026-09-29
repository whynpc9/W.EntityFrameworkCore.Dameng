using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengCollectionTranslationFunctionalTests
{
    private const string SourceSql =
        "SELECT CAST(0 AS INT) AS \"ID\" FROM dual "
        + "UNION ALL SELECT CAST(1 AS INT) AS \"ID\" FROM dual "
        + "UNION ALL SELECT CAST(NULL AS INT) AS \"ID\" FROM dual";

    [DamengTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(499)]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(998)]
    [InlineData(999)]
    [InlineData(1000)]
    [InlineData(2000)]
    public async Task ParameterCollectionReturnsExpectedRowsInOneCommand(int size)
    {
        var commands = new CommandCounter();
        await using var context = CreateContext(commands);
        int?[] values = Enumerable.Range(0, size).Select(index => (int?)index).ToArray();

        var result = await Source(context)
            .Where(row => values.Contains(row.Id))
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync();

        int?[] expected = size switch
        {
            0 => [],
            1 => [0],
            _ => [0, 1]
        };
        Assert.Equal(expected, result);
        Assert.Equal(1, commands.Count);
    }

    [DamengFact]
    public async Task ConstantCollectionReturnsExpectedRowsInOneCommand()
    {
        var commands = new CommandCounter();
        await using var context = CreateContext(commands);
        var result = await Source(context)
            .Where(row => new int?[] { 0, 1 }.Contains(row.Id))
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync();

        Assert.Equal([0, 1], result);
        Assert.Equal(1, commands.Count);
    }

    [DamengFact]
    public async Task NullAndNegatedCollectionsPreserveNullSemantics()
    {
        var commands = new CommandCounter();
        await using var context = CreateContext(commands);
        int?[] values = [null, 1, 1];

        var positive = await Source(context)
            .Where(row => values.Contains(row.Id))
            .Select(row => row.Id)
            .ToArrayAsync();
        var negative = await Source(context)
            .Where(row => !values.Contains(row.Id))
            .Select(row => row.Id)
            .ToArrayAsync();

        Assert.Equal(2, positive.Length);
        Assert.Contains((int?)null, positive);
        Assert.Contains((int?)1, positive);
        Assert.Equal([0], negative);
        Assert.Equal(2, commands.Count);
    }

    [DamengFact]
    public async Task TwoCollectionsFilterAndPaginationExecuteAsSingleCommands()
    {
        var commands = new CommandCounter();
        await using var context = CreateContext(commands);
        int?[] first = Enumerable.Range(0, 499).Select(index => (int?)index).ToArray();
        int?[] second = Enumerable.Range(499, 501).Select(index => (int?)index).ToArray();
        int? filter = 1;

        var filtered = await Source(context)
            .Where(row => (first.Contains(row.Id) || second.Contains(row.Id)) && row.Id == filter)
            .Select(row => row.Id)
            .ToArrayAsync();
        Assert.Equal([1], filtered);
        Assert.Equal(1, commands.Count);

        int?[] pageValues = [0, 1];
        var page = await Source(context)
            .Where(row => pageValues.Contains(row.Id))
            .OrderBy(row => row.Id)
            .Skip(1)
            .Take(1)
            .Select(row => row.Id)
            .ToArrayAsync();
        Assert.Equal([1], page);
        Assert.Equal(2, commands.Count);
    }

    [DamengFact]
    public async Task ReusedQueryReadsCurrentCollectionAfterSizeChanges()
    {
        var commands = new CommandCounter();
        await using var context = CreateContext(commands);
        int?[] values = [0];
        var query = Source(context)
            .Where(row => values.Contains(row.Id))
            .OrderBy(row => row.Id)
            .Select(row => row.Id);

        Assert.Equal([0], await query.ToArrayAsync());
        values = Enumerable.Range(0, 1000).Select(index => (int?)index).ToArray();
        Assert.Equal([0, 1], await query.ToArrayAsync());
        values = [0];
        Assert.Equal([0], await query.ToArrayAsync());
        Assert.Equal(3, commands.Count);
    }

    private static CollectionContext CreateContext(CommandCounter commands)
    {
        var options = new DbContextOptionsBuilder<CollectionContext>()
            .UseDameng(DamengTestEnvironment.GetRequiredConnectionString())
            .AddInterceptors(commands)
            .Options;
        return new CollectionContext(options);
    }

    private static IQueryable<CollectionRow> Source(CollectionContext context)
        => context.Rows.FromSqlRaw(SourceSql);

    private sealed class CollectionContext(DbContextOptions<CollectionContext> options)
        : DbContext(options)
    {
        public DbSet<CollectionRow> Rows => Set<CollectionRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<CollectionRow>(entity =>
            {
                entity.HasNoKey();
                entity.Property(row => row.Id).HasColumnName("ID");
            });
    }

    private sealed class CollectionRow
    {
        public int? Id { get; set; }
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Count++;
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
