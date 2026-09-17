using System.Globalization;
using System.Text.Json;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengExtendedTypeMappingFunctionalTests(
    ITestOutputHelper output)
{
    [DamengFact]
    public Task DateOnlyRoundtripsThroughDate()
        => WithValueAsync(
            "DATE",
            new DateOnly(2026, 7, 23),
            static (expected, actual) => Assert.Equal(expected, actual));

    [DamengFact]
    public Task TimeOnlyRoundtripsMicrosecondsThroughTime()
        => WithValueAsync(
            "TIME(6)",
            new TimeOnly(13, 14, 15)
                .Add(TimeSpan.FromTicks(1_234_560)),
            static (expected, actual) => Assert.Equal(expected, actual));

    [DamengFact]
    public Task DateTimeRoundtripsTheSeventhFractionalSecondDigit()
        => WithValueAsync(
            "TIMESTAMP(7)",
            new DateTime(2026, 7, 23, 14, 15, 16)
                .AddTicks(1),
            static (expected, actual) => Assert.Equal(expected, actual));

    [DamengFact]
    public Task DateTimeOffsetRoundtripsThroughDatetimeWithTimeZone()
        => WithValueAsync(
            "DATETIME(7) WITH TIME ZONE",
            new DateTimeOffset(2026, 7, 23, 14, 15, 16, TimeSpan.FromHours(8))
                .AddTicks(1),
            static (expected, actual) => Assert.Equal(expected, actual));

    [DamengFact]
    public Task DateTimeOffsetRawAdoReadbackPreservesServerEvidence()
        => ExtendedTypeStore.WithTableAsync(
            "DATETIME(7) WITH TIME ZONE",
            async store =>
            {
                var options = new DbContextOptionsBuilder<
                        ExtendedTypeContext<DateTimeOffset>>()
                    .UseDameng(store.ConnectionString)
                    .ReplaceService<
                        IModelCacheKeyFactory,
                        ExtendedTypeModelCacheKeyFactory>()
                    .EnableDetailedErrors()
                    .Options;
                var expected = new DateTimeOffset(
                        2026,
                        7,
                        23,
                        14,
                        15,
                        16,
                        TimeSpan.FromHours(8))
                    .AddTicks(1);

                long id;
                await using (var context = new ExtendedTypeContext<DateTimeOffset>(
                    options,
                    store.TableName,
                    configureValue: null))
                {
                    var entity = new ExtendedTypeEntity<DateTimeOffset>
                    {
                        Value = expected
                    };
                    context.Entities.Add(entity);
                    await context.SaveChangesAsync();
                    id = entity.Id;
                }

                await using var connection = new DmConnection(store.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"""
                    SELECT
                        "VALUE",
                        CAST("VALUE" AS VARCHAR2(100))
                    FROM "{store.TableName}"
                    WHERE "ID" = {id.ToString(CultureInfo.InvariantCulture)}
                    """;

                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());

                var raw = reader.GetValue(0);
                var rawDateTime = Assert.IsType<DateTime>(raw);
                var providerSpecificValue = reader.GetProviderSpecificValue(0);
                var directText = reader.GetString(0);
                var text = reader.GetString(1);

                output.WriteLine(
                    "ADO type={0}; fieldType={1}; providerType={2}; providerValueType={3}; dataType={4}; value={5}; kind={6}; ticks={7}; directText={8}; castText={9}",
                    raw.GetType().FullName,
                    reader.GetFieldType(0).FullName,
                    reader.GetProviderSpecificFieldType(0).FullName,
                    providerSpecificValue.GetType().FullName,
                    reader.GetDataTypeName(0),
                    rawDateTime.ToString("O", CultureInfo.InvariantCulture),
                    rawDateTime.Kind,
                    rawDateTime.Ticks,
                    directText,
                    text);

                Assert.Equal(expected.DateTime, rawDateTime);
                Assert.Equal(DateTimeKind.Unspecified, rawDateTime.Kind);
                Assert.EndsWith("+08:00", directText, StringComparison.Ordinal);
                Assert.EndsWith("+08:00", text, StringComparison.Ordinal);
            });

    [DamengFact]
    public Task TimeSpanWithinTwoDigitDaysRoundtripsThroughIntervalDayToSecond()
        => WithValueAsync(
            "INTERVAL DAY(9) TO SECOND(6)",
            new TimeSpan(days: 3, hours: 4, minutes: 5, seconds: 6),
            static (expected, actual) => Assert.Equal(expected, actual));

    [DamengFact]
    public Task PositiveTimeSpanBeyondTwoDigitDaysRoundtripsThroughIntervalDayToSecond()
        => WithValueAsync(
            "INTERVAL DAY(9) TO SECOND(6)",
            new TimeSpan(days: 123, hours: 4, minutes: 5, seconds: 6),
            static (expected, actual) => Assert.Equal(expected, actual));

    [DamengFact]
    public Task NegativeTimeSpanBeyondTwoDigitDaysRoundtripsThroughIntervalDayToSecond()
        => WithValueAsync(
            "INTERVAL DAY(9) TO SECOND(6)",
            -new TimeSpan(days: 123, hours: 4, minutes: 5, seconds: 6),
            static (expected, actual) => Assert.Equal(expected, actual));

    [DamengFact]
    public Task PositiveTimeSpanBeyondTwoDigitDaysRoundtripsWithExplicitDayPrecision()
        => WithValueAsync(
            "INTERVAL DAY(9) TO SECOND",
            new TimeSpan(days: 123, hours: 4, minutes: 5, seconds: 6),
            static property => property.HasColumnType("INTERVAL DAY(9) TO SECOND"),
            static (expected, actual) => Assert.Equal(expected, actual));

    [DamengFact]
    public Task NegativeTimeSpanBeyondTwoDigitDaysRoundtripsWithExplicitDayPrecision()
        => WithValueAsync(
            "INTERVAL DAY(9) TO SECOND",
            -new TimeSpan(days: 123, hours: 4, minutes: 5, seconds: 6),
            static property => property.HasColumnType("INTERVAL DAY(9) TO SECOND"),
            static (expected, actual) => Assert.Equal(expected, actual));

    [DamengFact]
    public Task PositiveTimeSpanRawAdoReadbackPreservesServerEvidence()
        => TimeSpanRawAdoReadbackPreservesServerEvidence(
            new TimeSpan(
                days: 123,
                hours: 4,
                minutes: 5,
                seconds: 6,
                milliseconds: 789));

    [DamengFact]
    public Task NegativeTimeSpanRawAdoReadbackPreservesServerEvidence()
        => TimeSpanRawAdoReadbackPreservesServerEvidence(
            -new TimeSpan(
                days: 123,
                hours: 4,
                minutes: 5,
                seconds: 6,
                milliseconds: 789));

    private Task TimeSpanRawAdoReadbackPreservesServerEvidence(
        TimeSpan expected)
        => ExtendedTypeStore.WithTableAsync(
            "INTERVAL DAY(9) TO SECOND(6)",
            async store =>
            {
                var options = new DbContextOptionsBuilder<ExtendedTypeContext<TimeSpan>>()
                    .UseDameng(store.ConnectionString)
                    .ReplaceService<
                        IModelCacheKeyFactory,
                        ExtendedTypeModelCacheKeyFactory>()
                    .EnableDetailedErrors()
                    .Options;

                long id;
                await using (var context = new ExtendedTypeContext<TimeSpan>(
                    options,
                    store.TableName,
                    configureValue: null))
                {
                    var entity = new ExtendedTypeEntity<TimeSpan>
                    {
                        Value = expected
                    };
                    context.Entities.Add(entity);
                    await context.SaveChangesAsync();
                    id = entity.Id;
                }

                await using var connection = new DmConnection(store.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"""
                    SELECT "VALUE"
                    FROM "{store.TableName}"
                    WHERE "ID" = {id.ToString(CultureInfo.InvariantCulture)}
                    """;

                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());

                var raw = reader.GetValue(0);
                var providerSpecificValue = reader.GetProviderSpecificValue(0);
                var directText = reader.GetString(0);

                output.WriteLine(
                    "Interval ADO type={0}; fieldType={1}; providerType={2}; providerValueType={3}; dataType={4}; directText={5}",
                    raw.GetType().FullName,
                    reader.GetFieldType(0).FullName,
                    reader.GetProviderSpecificFieldType(0).FullName,
                    providerSpecificValue.GetType().FullName,
                    reader.GetDataTypeName(0),
                    directText);

                Assert.Equal(
                    expected < TimeSpan.Zero
                        ? "INTERVAL '-000000123 04:05:06.789000' DAY(9) TO SECOND(6)"
                        : "INTERVAL '000000123 04:05:06.789000' DAY(9) TO SECOND(6)",
                    directText);
            });

    [DamengFact]
    public Task InlineByteArrayRoundtripsThroughVarbinary()
    {
        var value = Enumerable.Range(0, 256)
            .Select(index => checked((byte)index))
            .ToArray();

        return WithValueAsync(
            "VARBINARY(256)",
            value,
            static property => property.HasMaxLength(256),
            static (expected, actual) => Assert.Equal(expected, actual));
    }

    [DamengFact]
    public Task LargeByteArrayRoundtripsThroughBlob()
    {
        var value = Enumerable.Range(0, 40_000)
            .Select(index => (byte)(index % 251))
            .ToArray();

        return WithValueAsync(
            "BLOB",
            value,
            static property => property.HasMaxLength(32_768),
            static (expected, actual) => Assert.Equal(expected, actual));
    }

    [DamengFact]
    public Task LongUnicodeStringRoundtripsThroughNclob()
    {
        var value = $"{new string('数', 40_000)}达梦EF Core 10终点";

        return WithValueAsync(
            "NCLOB",
            value,
            static property => property.HasMaxLength(32_768),
            static (expected, actual) => Assert.Equal(expected, actual));
    }

    [DamengFact]
    public Task JsonElementRoundtripsThroughJson()
    {
        var value = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "database": "达梦",
              "efCore": 10,
              "enabled": true,
              "values": [1, 2, 3],
              "nested": { "message": "你好" }
            }
            """);

        return WithValueAsync(
            "JSON",
            value,
            static (expected, actual) =>
                Assert.True(JsonElement.DeepEquals(expected, actual)));
    }

    [DamengFact]
    public Task DateTimeOffsetColumnComparisonExecutes()
        => ExtendedTypeStore.WithTableAsync(
            "DATETIME(7) WITH TIME ZONE",
            async store =>
            {
                var options = CreateExtendedOptions<DateTimeOffset>(store);
                var earlier = new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.FromHours(8));
                var later = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.FromHours(8));
                var cutoff = new DateTimeOffset(2026, 7, 23, 11, 0, 0, TimeSpan.FromHours(8));

                await using (var context = new ExtendedTypeContext<DateTimeOffset>(
                    options,
                    store.TableName,
                    configureValue: null))
                {
                    context.Entities.AddRange(
                        new ExtendedTypeEntity<DateTimeOffset> { Value = earlier },
                        new ExtendedTypeEntity<DateTimeOffset> { Value = later });
                    await context.SaveChangesAsync();
                }

                await using (var context = new ExtendedTypeContext<DateTimeOffset>(
                    options,
                    store.TableName,
                    configureValue: null))
                {
                    var matched = await context.Entities
                        .AsNoTracking()
                        .Where(entity => entity.Value >= cutoff)
                        .Select(entity => entity.Value)
                        .ToListAsync();

                    var matchedValue = Assert.Single(matched);
                    Assert.Equal(later, matchedValue);
                }
            });

    [DamengFact]
    public Task JsonElementWholeValueEqualityIsRejectedByTheServer()
        => ExtendedTypeStore.WithTableAsync(
            "JSON",
            async store =>
            {
                var options = CreateExtendedOptions<JsonElement>(store);
                var matching = JsonSerializer.Deserialize<JsonElement>(
                    """{"database":"达梦","efCore":10}""");
                var other = JsonSerializer.Deserialize<JsonElement>(
                    """{"database":"其他","efCore":9}""");

                await using (var context = new ExtendedTypeContext<JsonElement>(
                    options,
                    store.TableName,
                    configureValue: null))
                {
                    context.Entities.AddRange(
                        new ExtendedTypeEntity<JsonElement> { Value = matching },
                        new ExtendedTypeEntity<JsonElement> { Value = other });
                    await context.SaveChangesAsync();
                }

                await using (var context = new ExtendedTypeContext<JsonElement>(
                    options,
                    store.TableName,
                    configureValue: null))
                {
                    var query = matching;
                    var exception = await Assert.ThrowsAsync<DmException>(
                        () => context.Entities
                            .AsNoTracking()
                            .Where(entity => entity.Value.Equals(query))
                            .Select(entity => entity.Value)
                            .ToListAsync());

                    Assert.Contains("数据类型不匹配", exception.Message, StringComparison.Ordinal);
                }
            });

    [DamengFact]
    public Task JsonElementPropertyAccessIsRejectedBeforeExecution()
        => ExtendedTypeStore.WithTableAsync(
            "JSON",
            async store =>
            {
                var options = CreateExtendedOptions<JsonElement>(store);
                var document = JsonSerializer.Deserialize<JsonElement>(
                    """{"database":"达梦"}""");

                await using (var context = new ExtendedTypeContext<JsonElement>(
                    options,
                    store.TableName,
                    configureValue: null))
                {
                    context.Entities.Add(new ExtendedTypeEntity<JsonElement> { Value = document });
                    await context.SaveChangesAsync();
                }

                await using (var context = new ExtendedTypeContext<JsonElement>(
                    options,
                    store.TableName,
                    configureValue: null))
                {
                    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                        () => context.Entities
                            .AsNoTracking()
                            .Where(entity => entity.Value.GetProperty("database").GetString() == "达梦")
                            .ToListAsync());

                    Assert.Contains(
                        "could not be translated",
                        exception.Message,
                        StringComparison.OrdinalIgnoreCase);
                    Assert.Contains(
                        nameof(JsonElement.GetProperty),
                        exception.Message,
                        StringComparison.Ordinal);
                }
            });

    private static DbContextOptions<ExtendedTypeContext<T>> CreateExtendedOptions<T>(
        ExtendedTypeStore store)
        => new DbContextOptionsBuilder<ExtendedTypeContext<T>>()
            .UseDameng(store.ConnectionString)
            .ReplaceService<IModelCacheKeyFactory, ExtendedTypeModelCacheKeyFactory>()
            .EnableDetailedErrors()
            .Options;

    private static Task WithValueAsync<T>(
        string storeType,
        T value,
        Action<T, T> assert)
        => WithValueAsync(storeType, value, configureValue: null, assert);

    private static Task WithValueAsync<T>(
        string storeType,
        T value,
        Action<PropertyBuilder<T>>? configureValue,
        Action<T, T> assert)
        => ExtendedTypeStore.WithTableAsync(
            storeType,
            async store =>
            {
                var options = new DbContextOptionsBuilder<ExtendedTypeContext<T>>()
                    .UseDameng(store.ConnectionString)
                    .ReplaceService<IModelCacheKeyFactory, ExtendedTypeModelCacheKeyFactory>()
                    .EnableDetailedErrors()
                    .Options;

                long id;
                await using (var context = new ExtendedTypeContext<T>(
                    options,
                    store.TableName,
                    configureValue))
                {
                    var entity = new ExtendedTypeEntity<T> { Value = value };
                    context.Entities.Add(entity);
                    await context.SaveChangesAsync();

                    Assert.True(entity.Id > 0);
                    id = entity.Id;
                }

                await using (var context = new ExtendedTypeContext<T>(
                    options,
                    store.TableName,
                    configureValue))
                {
                    var actual = await context.Entities
                        .AsNoTracking()
                        .Where(entity => entity.Id == id)
                        .Select(entity => entity.Value)
                        .SingleAsync();

                    assert(value, actual);
                }
            });

    private interface IExtendedTypeContext
    {
        string TableName { get; }
    }

    private sealed class ExtendedTypeContext<T>(
        DbContextOptions<ExtendedTypeContext<T>> options,
        string tableName,
        Action<PropertyBuilder<T>>? configureValue)
        : DbContext(options), IExtendedTypeContext
    {
        public string TableName { get; } = tableName;

        public DbSet<ExtendedTypeEntity<T>> Entities
            => Set<ExtendedTypeEntity<T>>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ExtendedTypeEntity<T>>(
                entity =>
                {
                    entity.ToTable(TableName);
                    entity.HasKey(item => item.Id);

                    entity.Property(item => item.Id)
                        .HasColumnName("ID")
                        .ValueGeneratedOnAdd();

                    var valueProperty = entity.Property(item => item.Value)
                        .HasColumnName("VALUE")
                        .IsRequired();
                    configureValue?.Invoke(valueProperty);
                });
    }

    private sealed class ExtendedTypeModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is IExtendedTypeContext extendedTypeContext
                ? (context.GetType(), extendedTypeContext.TableName, designTime)
                : (context.GetType(), designTime);
    }

    private sealed class ExtendedTypeEntity<T>
    {
        public long Id { get; set; }

        public required T Value { get; set; }
    }

    private sealed class ExtendedTypeStore
    {
        private bool _tableCreated;

        private ExtendedTypeStore(string storeType)
        {
            ConnectionString = DamengTestEnvironment.GetRequiredConnectionString();
            var suffix = Guid.NewGuid()
                .ToString("N", CultureInfo.InvariantCulture)[..12]
                .ToUpperInvariant();
            TableName = $"EF10_XT_{suffix}";
            PrimaryKeyName = $"PK_XT_{suffix}";
            StoreType = storeType;
        }

        public string ConnectionString { get; }

        public string TableName { get; }

        public string PrimaryKeyName { get; }

        public string StoreType { get; }

        public static async Task WithTableAsync(
            string storeType,
            Func<ExtendedTypeStore, Task> test)
        {
            var store = new ExtendedTypeStore(storeType);

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
                    "ID" BIGINT IDENTITY(1,1) NOT NULL,
                    "VALUE" {StoreType} NOT NULL,
                    CONSTRAINT "{PrimaryKeyName}" PRIMARY KEY ("ID")
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
