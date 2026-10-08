using System.Data.Common;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengEnumToStringFunctionalTests
{
    [DamengFact]
    public async Task DateBoxingGuardPreservesNumericTextAndNullableEnumQueries()
    {
        var table = "EF10_ES_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        var created = false;
        try
        {
            await using (var connection = new DmConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    CREATE TABLE "{table}" (
                        "Id" INT NOT NULL, "Status" INT NOT NULL, "TextStatus" NVARCHAR2(16) NOT NULL,
                        "OptionalStatus" INT, "OptionalTextStatus" NVARCHAR2(16),
                        CONSTRAINT "PK_{table}" NOT CLUSTER PRIMARY KEY ("Id")
                    )
                    """;
                await command.ExecuteNonQueryAsync();
                created = true;
            }
            var commands = new List<string>();
            await using var context = new EnumContext(new DbContextOptionsBuilder<EnumContext>()
                .UseDameng(connectionString).EnableServiceProviderCaching(false)
                .ReplaceService<IModelCacheKeyFactory, EnumModelCacheKeyFactory>()
                .AddInterceptors(new Capture(commands)).Options, table);
            context.Rows.AddRange(
                new EnumRow { Id = 1, Status = Status.Complete, TextStatus = Status.Complete, OptionalStatus = Status.Complete, OptionalTextStatus = Status.Complete },
                new EnumRow { Id = 2, Status = Status.Pending, TextStatus = Status.Pending },
                new EnumRow { Id = 3, Status = Status.Complete, TextStatus = Status.Complete, OptionalStatus = Status.Pending, OptionalTextStatus = Status.Pending });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            commands.Clear();
#pragma warning disable CA1305 // Verify inherited enum translations without a format provider.
            Assert.Equal([1, 3], await context.Rows.Where(r => r.Status.ToString() == "Complete")
                .OrderBy(r => r.Id).Select(r => r.Id).ToListAsync());
            Assert.Contains("CASE", Assert.Single(commands), StringComparison.Ordinal);
            commands.Clear();
            Assert.Equal([1, 3], await context.Rows.Where(r => r.TextStatus.ToString() == "Complete")
                .OrderBy(r => r.Id).Select(r => r.Id).ToListAsync());
            Assert.Contains("TextStatus", Assert.Single(commands), StringComparison.Ordinal);
            commands.Clear();
            Assert.Equal([1], await context.Rows.Where(r => r.OptionalStatus.ToString() == "Complete")
                .Select(r => r.Id).ToListAsync());
            Assert.Contains("CASE", Assert.Single(commands), StringComparison.Ordinal);
            commands.Clear();
            Assert.Equal([1], await context.Rows.Where(r => r.OptionalTextStatus.ToString() == "Complete")
                .Select(r => r.Id).ToListAsync());
            Assert.Single(commands);
            commands.Clear();
            var groupError = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Rows
                .GroupBy(r => r.OptionalTextStatus.ToString()).Select(g => g.Count()).ToListAsync());
#pragma warning restore CA1305
            Assert.Contains("LOB", groupError.Message, StringComparison.Ordinal);
            Assert.Empty(commands);
        }
        finally
        {
            if (created)
            {
                await using var connection = new DmConnection(connectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE \"{table}\"";
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private enum Status { Pending, Complete }
    private sealed class EnumRow
    {
        public int Id { get; set; }
        public Status Status { get; set; }
        public Status TextStatus { get; set; }
        public Status? OptionalStatus { get; set; }
        public Status? OptionalTextStatus { get; set; }
    }
    private sealed class EnumContext(DbContextOptions<EnumContext> options, string tableName) : DbContext(options)
    {
        public string TableName { get; } = tableName;
        public DbSet<EnumRow> Rows => Set<EnumRow>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<EnumRow>(entity =>
            {
                entity.ToTable(TableName);
                entity.Property(r => r.Id).ValueGeneratedNever();
                entity.Property(r => r.TextStatus).HasConversion<string>().HasMaxLength(16);
                entity.Property(r => r.OptionalTextStatus).HasConversion<string>().HasMaxLength(16);
            });
    }
    private sealed class EnumModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => context is EnumContext enums
            ? (context.GetType(), enums.TableName, designTime) : (context.GetType(), designTime);
    }
    private sealed class Capture(List<string> commands) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
