using System.Data.Common;
using System.Globalization;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using W.EntityFrameworkCore.Dameng.TestData;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengBasicStringTranslationFunctionalTests
{
    private static readonly bool[] LobModes = [false, true];
    private static readonly string[] ConcatenationOperations = ["Concat2", "Concat3", "Concat4", "Add"];

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NullableUnicodeAndNclobStringsHaveServerSideSemantics(bool useRelationalNulls)
        => StringStore.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = CreateContext(store, commands, useRelationalNulls);
            context.Entities.AddRange(QueryTranslationCases.Strings.Select(value => new StringEntity
            {
                Id = value.Id,
                ShortText = value.Text,
                OtherShortText = value.OtherText,
                ThirdShortText = value.ThirdText,
                FourthShortText = value.FourthText,
                LargeText = value.Text,
                OtherLargeText = value.OtherText,
                ThirdLargeText = value.ThirdText,
                FourthLargeText = value.FourthText
            }));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            commands.Commands.Clear();

            foreach (var lob in LobModes)
            {
                var emptyQuery = ProjectIsNullOrEmpty(context, lob).OrderBy(value => value.Id);
                var emptySql = emptyQuery.ToQueryString();
                Assert.Contains("IS NULL", emptySql, StringComparison.Ordinal);
                Assert.Contains("LENGTH(", emptySql, StringComparison.Ordinal);
                Assert.Contains(lob ? "\"LARGE_TEXT\"" : "\"SHORT_TEXT\"",
                    emptySql, StringComparison.Ordinal);

                commands.Commands.Clear();
                var empties = await emptyQuery.ToListAsync();
                AssertExecutedSql(commands, "IS NULL");
                AssertExecutedSql(commands, "LENGTH(");
                Assert.Equal(
                    QueryTranslationCases.Strings
                        .OrderBy(value => value.Id)
                        .Select(value => value.ExpectedIsNullOrEmpty),
                    empties.Select(value => value.IsEmpty));

                commands.Commands.Clear();
                var emptyIds = await ProjectIsNullOrEmpty(context, lob)
                    .Where(value => value.IsEmpty)
                    .OrderBy(value => value.Id)
                    .Select(value => value.Id)
                    .ToListAsync();
                AssertExecutedSql(commands, "IS NULL");
                AssertExecutedSql(commands, "LENGTH(");
                Assert.Equal(
                    QueryTranslationCases.Strings
                        .Where(value => value.ExpectedIsNullOrEmpty)
                        .OrderBy(value => value.Id)
                        .Select(value => (long)value.Id),
                    emptyIds);

                foreach (var operation in ConcatenationOperations)
                {
                    var projected = Project(context, lob, operation)
                        .OrderBy(value => value.Id);
                    var projectionSql = projected.ToQueryString();
                    Assert.Contains(" || ", projectionSql, StringComparison.Ordinal);
                    Assert.DoesNotContain(" AS VARCHAR", projectionSql, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain(" AS NVARCHAR", projectionSql, StringComparison.OrdinalIgnoreCase);
                    if (lob)
                    {
                        Assert.Contains("CAST('' AS NCLOB)", projectionSql, StringComparison.Ordinal);
                    }

                    commands.Commands.Clear();
                    var actual = await projected.ToListAsync();
                    AssertExecutedSql(commands, " || ");
                    if (lob)
                    {
                        AssertExecutedSql(commands, "CAST('' AS NCLOB)");
                    }
                    Assert.Equal(
                        QueryTranslationCases.Strings
                            .OrderBy(value => value.Id)
                            .Select(value => Expected(value, operation)),
                        actual.Select(value => value.Value));

                    var unicodeExpected = Expected(QueryTranslationCases.Strings.Single(value => value.Id == 6), operation);
                    commands.Commands.Clear();
                    var matchingIds = await Project(context, lob, operation)
                        .Where(value => value.Value == unicodeExpected)
                        .OrderBy(value => value.Id)
                        .Select(value => value.Id)
                        .ToListAsync();
                    AssertExecutedSql(commands, " || ");
                    if (lob)
                    {
                        AssertExecutedSql(commands, "CAST('' AS NCLOB)");
                    }
                    Assert.Equal(
                        QueryTranslationCases.Strings
                            .Where(value => Expected(value, operation) == unicodeExpected)
                            .OrderBy(value => value.Id)
                            .Select(value => (long)value.Id),
                        matchingIds);

                    commands.Commands.Clear();
                    var nullOperandIds = await Project(context, lob, operation)
                        .Where(value => value.Value == "右")
                        .OrderBy(value => value.Id)
                        .Select(value => value.Id)
                        .ToListAsync();
                    AssertExecutedSql(commands, " || ");
                    if (lob)
                    {
                        AssertExecutedSql(commands, "CAST('' AS NCLOB)");
                    }
                    Assert.Equal(
                        QueryTranslationCases.Strings
                            .Where(value => Expected(value, operation) == "右")
                            .OrderBy(value => value.Id)
                            .Select(value => (long)value.Id),
                        nullOperandIds);
                }
            }
        });

    [DamengFact]
    public Task NclobConcatenationDoesNotTruncateLongText()
        => StringStore.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = CreateContext(store, commands, useRelationalNulls: false);
            var longText = new string('长', 20_000);
            context.Entities.AddRange(
                new StringEntity
                {
                    Id = 1,
                    LargeText = longText,
                    OtherLargeText = "尾"
                },
                new StringEntity { Id = 2 });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            commands.Commands.Clear();

            var storedValue = await context.Entities
                .Where(entity => entity.Id == 1)
                .Select(entity => entity.LargeText)
                .SingleAsync();
            Assert.Equal(longText, storedValue);
            AssertExecutedSql(commands, "\"LARGE_TEXT\"");
            commands.Commands.Clear();

            var coalesced = await context.Entities
                .Where(entity => entity.Id == 1)
                .Select(entity => entity.LargeText ?? "")
                .SingleAsync();
            Assert.Equal(longText, coalesced);
            AssertExecutedSql(commands, "COALESCE(");
            AssertExecutedSql(commands, "CAST('' AS NCLOB)");
            commands.Commands.Clear();

            var query = context.Entities
                .Where(entity => entity.Id == 1)
                .Select(entity => entity.LargeText + entity.OtherLargeText);
            Assert.Contains(" || ", query.ToQueryString(), StringComparison.Ordinal);
            Assert.Contains("CAST('' AS NCLOB)", query.ToQueryString(), StringComparison.Ordinal);

            var result = await query.SingleAsync();
            Assert.Equal(longText + "尾", result);
            AssertExecutedSql(commands, " || ");
            AssertExecutedSql(commands, "CAST('' AS NCLOB)");
            commands.Commands.Clear();

            var bothNull = await context.Entities
                .Where(entity => entity.Id == 2)
                .Select(entity => entity.LargeText + entity.OtherLargeText)
                .SingleAsync();
            Assert.Equal("", bothNull);
            AssertExecutedSql(commands, " || ");
            AssertExecutedSql(commands, "CAST('' AS NCLOB)");
        });

    private static void AssertExecutedSql(CommandCaptureInterceptor commands, string fragment)
    {
        var command = Assert.Single(commands.Commands);
        Assert.Contains(fragment, command, StringComparison.Ordinal);
        Assert.DoesNotContain(" AS VARCHAR", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" AS NVARCHAR", command, StringComparison.OrdinalIgnoreCase);
    }

    private static string Expected(QueryTranslationCases.StringCase value, string operation)
        => operation switch
        {
            "Concat2" or "Add" => value.ExpectedConcat2,
            "Concat3" => value.ExpectedConcat3,
            "Concat4" => value.ExpectedConcat4,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

    private static IQueryable<FlagResult> ProjectIsNullOrEmpty(StringContext context, bool lob)
        => lob
            ? context.Entities.Select(entity => new FlagResult
            {
                Id = entity.Id,
                IsEmpty = string.IsNullOrEmpty(entity.LargeText)
            })
            : context.Entities.Select(entity => new FlagResult
            {
                Id = entity.Id,
                IsEmpty = string.IsNullOrEmpty(entity.ShortText)
            });

    private static IQueryable<StringResult> Project(
        StringContext context,
        bool lob,
        string operation)
        => (lob, operation) switch
        {
            (false, "Concat2") => context.Entities.Select(entity => new StringResult
            {
                Id = entity.Id,
                Value = string.Concat(entity.ShortText, entity.OtherShortText)
            }),
            (false, "Concat3") => context.Entities.Select(entity => new StringResult
            {
                Id = entity.Id,
                Value = string.Concat(entity.ShortText, entity.OtherShortText,
                    entity.ThirdShortText)
            }),
            (false, "Concat4") => context.Entities.Select(entity => new StringResult
            {
                Id = entity.Id,
                Value = string.Concat(entity.ShortText, entity.OtherShortText,
                    entity.ThirdShortText, entity.FourthShortText)
            }),
            (false, "Add") => context.Entities.Select(entity => new StringResult
            {
                Id = entity.Id,
                Value = entity.ShortText + entity.OtherShortText
            }),
            (true, "Concat2") => context.Entities.Select(entity => new StringResult
            {
                Id = entity.Id,
                Value = string.Concat(entity.LargeText, entity.OtherLargeText)
            }),
            (true, "Concat3") => context.Entities.Select(entity => new StringResult
            {
                Id = entity.Id,
                Value = string.Concat(entity.LargeText, entity.OtherLargeText,
                    entity.ThirdLargeText)
            }),
            (true, "Concat4") => context.Entities.Select(entity => new StringResult
            {
                Id = entity.Id,
                Value = string.Concat(entity.LargeText, entity.OtherLargeText,
                    entity.ThirdLargeText, entity.FourthLargeText)
            }),
            (true, "Add") => context.Entities.Select(entity => new StringResult
            {
                Id = entity.Id,
                Value = entity.LargeText + entity.OtherLargeText
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

    private static StringContext CreateContext(
        StringStore store,
        CommandCaptureInterceptor commands,
        bool useRelationalNulls)
    {
        var options = new DbContextOptionsBuilder<StringContext>()
            .UseDameng(store.ConnectionString, damengOptions =>
            {
                if (useRelationalNulls)
                {
                    damengOptions.UseRelationalNulls();
                }
            })
            .ReplaceService<IModelCacheKeyFactory, StringModelCacheKeyFactory>()
            .AddInterceptors(commands)
            .EnableDetailedErrors()
            .Options;
        return new StringContext(options, store.TableName);
    }

    private sealed class FlagResult
    {
        public long Id { get; set; }
        public bool IsEmpty { get; set; }
    }

    private sealed class StringResult
    {
        public long Id { get; set; }
        public string? Value { get; set; }
    }

    private sealed class StringContext(
        DbContextOptions<StringContext> options,
        string tableName) : DbContext(options)
    {
        public string TableName { get; } = tableName;

        public DbSet<StringEntity> Entities => Set<StringEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<StringEntity>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("ID").ValueGeneratedNever();
                entity.Property(item => item.ShortText).HasColumnName("SHORT_TEXT").HasMaxLength(200);
                entity.Property(item => item.OtherShortText).HasColumnName("OTHER_SHORT_TEXT").HasMaxLength(200);
                entity.Property(item => item.ThirdShortText).HasColumnName("THIRD_SHORT_TEXT").HasMaxLength(200);
                entity.Property(item => item.FourthShortText).HasColumnName("FOURTH_SHORT_TEXT").HasMaxLength(200);
                entity.Property(item => item.LargeText).HasColumnName("LARGE_TEXT");
                entity.Property(item => item.OtherLargeText).HasColumnName("OTHER_LARGE_TEXT");
                entity.Property(item => item.ThirdLargeText).HasColumnName("THIRD_LARGE_TEXT");
                entity.Property(item => item.FourthLargeText).HasColumnName("FOURTH_LARGE_TEXT");
            });
    }

    private sealed class StringModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is StringContext stringContext
                ? (context.GetType(), stringContext.TableName, designTime)
                : (context.GetType(), designTime);
    }

    private sealed class StringEntity
    {
        public long Id { get; set; }
        public string? ShortText { get; set; }
        public string? OtherShortText { get; set; }
        public string? ThirdShortText { get; set; }
        public string? FourthShortText { get; set; }
        public string? LargeText { get; set; }
        public string? OtherLargeText { get; set; }
        public string? ThirdLargeText { get; set; }
        public string? FourthLargeText { get; set; }
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

    private sealed class StringStore
    {
        private bool _tableCreated;

        private StringStore()
        {
            ConnectionString = DamengTestEnvironment.GetRequiredConnectionString();
            var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
            TableName = $"EF10_BS_{suffix}";
            PrimaryKeyName = $"PK_BS_{suffix}";
        }

        public string ConnectionString { get; }
        public string TableName { get; }
        public string PrimaryKeyName { get; }

        public static async Task WithTableAsync(Func<StringStore, Task> test)
        {
            var store = new StringStore();
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
                    "OTHER_SHORT_TEXT" NVARCHAR2(200) NULL,
                    "THIRD_SHORT_TEXT" NVARCHAR2(200) NULL,
                    "FOURTH_SHORT_TEXT" NVARCHAR2(200) NULL,
                    "LARGE_TEXT" NCLOB NULL,
                    "OTHER_LARGE_TEXT" NCLOB NULL,
                    "THIRD_LARGE_TEXT" NCLOB NULL,
                    "FOURTH_LARGE_TEXT" NCLOB NULL,
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
