using System.Data.Common;
using System.Globalization;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using W.EntityFrameworkCore.Dameng.TestData;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

#pragma warning disable CA1861 // Inline arrays must remain SQL constants in expression trees.
public sealed class DamengTrimCharacterFunctionalTests
{
    private static readonly string[] Operations =
        ["TrimCharacter", "TrimStartCharacter", "TrimEndCharacter",
            "TrimCharacters", "TrimStartCharacters", "TrimEndCharacters"];
    private static readonly char[] ExpectedCharacterSet = ['*', '.'];

    [DamengFact]
    public Task NclobTrimPreservesLongUnicodeText()
        => TrimStore.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = CreateContext(store, commands);
            var body = new string('中', 2_000) + "雪🌙";
            context.Entities.Add(new TrimEntity
            {
                Id = 1,
                LargeText = "***" + body + "**"
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            commands.Commands.Clear();

            var query = context.Entities
                .Where(entity => entity.Id == 1)
                .Select(entity => entity.LargeText!.Trim('*'));
            AssertServerTrim(query.ToQueryString(), "TrimCharacter");

            var value = await query.SingleAsync();
            Assert.Equal(body, value);
            AssertServerTrim(Assert.Single(commands.Commands), "TrimCharacter");
        });

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ConstantCharacterTrimRunsInFiltersAndServerProjections(bool lob)
        => TrimStore.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = CreateContext(store, commands);
            context.Entities.AddRange(QueryTranslationCases.Trim.Select(value => new TrimEntity
            {
                Id = value.Id,
                ShortText = value.Text,
                LargeText = value.Text
            }));
            context.Entities.Add(new TrimEntity { Id = 100 });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            foreach (var operation in Operations)
            {
                var query = Project(context, lob, operation)
                    .Where(value => value.Id < 100)
                    .OrderBy(value => value.Id);
                var sql = query.ToQueryString();
                AssertServerTrim(sql, operation);

                commands.Commands.Clear();
                var actual = await query.ToListAsync();
                AssertServerTrim(Assert.Single(commands.Commands), operation);
                Assert.Equal(
                    QueryTranslationCases.Trim
                        .OrderBy(value => value.Id)
                        .Select(value => Expected(value.Text, operation)),
                    actual.Select(value => value.Value));

                var filterValue = Expected(
                    QueryTranslationCases.Trim.Single(value => value.Id == 1).Text,
                    operation);
                commands.Commands.Clear();
                var matchingIds = await Project(context, lob, operation)
                    .Where(value => value.Value == filterValue && value.Id < 100)
                    .OrderBy(value => value.Id)
                    .Select(value => value.Id)
                    .ToListAsync();
                AssertServerTrim(Assert.Single(commands.Commands), operation);
                Assert.Equal(
                    QueryTranslationCases.Trim
                        .Where(value => Expected(value.Text, operation) == filterValue)
                        .OrderBy(value => value.Id)
                        .Select(value => (long)value.Id),
                    matchingIds);
            }

            commands.Commands.Clear();
            var nullValue = await Project(context, lob, "TrimCharacter")
                .Where(value => value.Id == 100)
                .Select(value => value.Value)
                .SingleAsync();
            Assert.Null(nullValue);
            AssertServerTrim(Assert.Single(commands.Commands), "TrimCharacter");

            await AssertSpecialCasesAsync(context, commands, lob);
        });

    private static async Task AssertSpecialCasesAsync(
        TrimContext context,
        CommandCaptureInterceptor commands,
        bool lob)
    {
        commands.Commands.Clear();
        var chinese = lob
            ? await context.Entities.Where(entity => entity.Id == 3)
                .Select(entity => entity.LargeText!.Trim(new[] { '雪', '雨' }))
                .SingleAsync()
            : await context.Entities.Where(entity => entity.Id == 3)
                .Select(entity => entity.ShortText!.Trim(new[] { '雪', '雨' }))
                .SingleAsync();
        Assert.Equal(QueryTranslationCases.Trim.Single(value => value.Id == 3).ExpectedTrimCharacters,
            chinese);
        AssertServerTrim(Assert.Single(commands.Commands), "TrimCharacters");

        commands.Commands.Clear();
        var quote = lob
            ? await context.Entities.Where(entity => entity.Id == 4)
                .Select(entity => entity.LargeText!.Trim('\''))
                .SingleAsync()
            : await context.Entities.Where(entity => entity.Id == 4)
                .Select(entity => entity.ShortText!.Trim('\''))
                .SingleAsync();
        Assert.Equal(QueryTranslationCases.Trim.Single(value => value.Id == 4).ExpectedTrimCharacter,
            quote);
        AssertServerTrim(Assert.Single(commands.Commands), "TrimCharacter");
        Assert.Contains("''", Assert.Single(commands.Commands), StringComparison.Ordinal);

        commands.Commands.Clear();
        var tab = lob
            ? await context.Entities.Where(entity => entity.Id == 5)
                .Select(entity => entity.LargeText!.Trim('\t'))
                .SingleAsync()
            : await context.Entities.Where(entity => entity.Id == 5)
                .Select(entity => entity.ShortText!.Trim('\t'))
                .SingleAsync();
        Assert.Equal(QueryTranslationCases.Trim.Single(value => value.Id == 5).ExpectedTrimCharacter,
            tab);
        AssertServerTrim(Assert.Single(commands.Commands), "TrimCharacter");

        commands.Commands.Clear();
        var allRemoved = lob
            ? await context.Entities.Where(entity => entity.Id == 7)
                .Select(entity => entity.LargeText!.Trim('!'))
                .SingleAsync()
            : await context.Entities.Where(entity => entity.Id == 7)
                .Select(entity => entity.ShortText!.Trim('!'))
                .SingleAsync();
        Assert.Equal(QueryTranslationCases.Trim.Single(value => value.Id == 7).ExpectedTrimCharacter,
            allRemoved);
        AssertServerTrim(Assert.Single(commands.Commands), "TrimCharacter");
    }

    private static string Expected(string value, string operation)
        => operation switch
        {
            "TrimCharacter" => value.Trim('*'),
            "TrimStartCharacter" => value.TrimStart('*'),
            "TrimEndCharacter" => value.TrimEnd('*'),
            "TrimCharacters" => value.Trim(ExpectedCharacterSet),
            "TrimStartCharacters" => value.TrimStart(ExpectedCharacterSet),
            "TrimEndCharacters" => value.TrimEnd(ExpectedCharacterSet),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

    private static void AssertServerTrim(string sql, string operation)
    {
        if (operation is "TrimCharacter" or "TrimCharacters")
        {
            Assert.Contains("RTRIM(LTRIM(", sql, StringComparison.Ordinal);
        }
        else if (operation is "TrimStartCharacter" or "TrimStartCharacters")
        {
            Assert.Contains("LTRIM(", sql, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("RTRIM(", sql, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(" AS VARCHAR", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" AS NVARCHAR", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static IQueryable<TrimResult> Project(
        TrimContext context,
        bool lob,
        string operation)
        => (lob, operation) switch
        {
            (false, "TrimCharacter") => context.Entities.Select(entity => new TrimResult
            {
                Id = entity.Id,
                Value = entity.ShortText!.Trim('*')
            }),
            (false, "TrimStartCharacter") => context.Entities.Select(entity => new TrimResult
            {
                Id = entity.Id,
                Value = entity.ShortText!.TrimStart('*')
            }),
            (false, "TrimEndCharacter") => context.Entities.Select(entity => new TrimResult
            {
                Id = entity.Id,
                Value = entity.ShortText!.TrimEnd('*')
            }),
            (false, "TrimCharacters") => context.Entities.Select(entity => new TrimResult
            {
                Id = entity.Id,
                Value = entity.ShortText!.Trim(new[] { '*', '.' })
            }),
            (false, "TrimStartCharacters") => context.Entities.Select(entity => new TrimResult
            {
                Id = entity.Id,
                Value = entity.ShortText!.TrimStart(new[] { '*', '.' })
            }),
            (false, "TrimEndCharacters") => context.Entities.Select(entity => new TrimResult
            {
                Id = entity.Id,
                Value = entity.ShortText!.TrimEnd(new[] { '*', '.' })
            }),
            (true, "TrimCharacter") => context.Entities.Select(entity => new TrimResult
            {
                Id = entity.Id,
                Value = entity.LargeText!.Trim('*')
            }),
            (true, "TrimStartCharacter") => context.Entities.Select(entity => new TrimResult
            {
                Id = entity.Id,
                Value = entity.LargeText!.TrimStart('*')
            }),
            (true, "TrimEndCharacter") => context.Entities.Select(entity => new TrimResult
            {
                Id = entity.Id,
                Value = entity.LargeText!.TrimEnd('*')
            }),
            (true, "TrimCharacters") => context.Entities.Select(entity => new TrimResult
            {
                Id = entity.Id,
                Value = entity.LargeText!.Trim(new[] { '*', '.' })
            }),
            (true, "TrimStartCharacters") => context.Entities.Select(entity => new TrimResult
            {
                Id = entity.Id,
                Value = entity.LargeText!.TrimStart(new[] { '*', '.' })
            }),
            (true, "TrimEndCharacters") => context.Entities.Select(entity => new TrimResult
            {
                Id = entity.Id,
                Value = entity.LargeText!.TrimEnd(new[] { '*', '.' })
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

    private static TrimContext CreateContext(TrimStore store, CommandCaptureInterceptor commands)
    {
        var options = new DbContextOptionsBuilder<TrimContext>()
            .UseDameng(store.ConnectionString)
            .ReplaceService<IModelCacheKeyFactory, TrimModelCacheKeyFactory>()
            .AddInterceptors(commands)
            .EnableDetailedErrors()
            .Options;
        return new TrimContext(options, store.TableName);
    }

    private sealed class TrimResult
    {
        public long Id { get; set; }
        public string? Value { get; set; }
    }

    private sealed class TrimContext(DbContextOptions<TrimContext> options, string tableName)
        : DbContext(options)
    {
        public string TableName { get; } = tableName;
        public DbSet<TrimEntity> Entities => Set<TrimEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<TrimEntity>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("ID").ValueGeneratedNever();
                entity.Property(item => item.ShortText).HasColumnName("SHORT_TEXT").HasMaxLength(200);
                entity.Property(item => item.LargeText).HasColumnName("LARGE_TEXT");
            });
    }

    private sealed class TrimEntity
    {
        public long Id { get; set; }
        public string? ShortText { get; set; }
        public string? LargeText { get; set; }
    }

    private sealed class TrimModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is TrimContext trimContext
                ? (context.GetType(), trimContext.TableName, designTime)
                : (context.GetType(), designTime);
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

    private sealed class TrimStore
    {
        private bool _tableCreated;

        private TrimStore()
        {
            ConnectionString = DamengTestEnvironment.GetRequiredConnectionString();
            var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
            TableName = $"EF10_TC_{suffix}";
            PrimaryKeyName = $"PK_TC_{suffix}";
        }

        public string ConnectionString { get; }
        public string TableName { get; }
        public string PrimaryKeyName { get; }

        public static async Task WithTableAsync(Func<TrimStore, Task> test)
        {
            var store = new TrimStore();
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
                    "ID" BIGINT NOT NULL,
                    "SHORT_TEXT" NVARCHAR2(200) NULL,
                    "LARGE_TEXT" NCLOB NULL,
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
#pragma warning restore CA1861
