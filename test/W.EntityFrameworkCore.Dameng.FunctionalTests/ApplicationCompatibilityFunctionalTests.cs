using System.Globalization;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class ApplicationCompatibilityFunctionalTests
{
    [DamengFact]
    public Task CommonApplicationScalarTypesAndValueConverterRoundtrip()
        => ApplicationCompatibilityStore.WithTableAsync(
            async store =>
            {
                var externalId = Guid.NewGuid();
                var occurredAt = new DateTime(
                        2026,
                        7,
                        23,
                        10,
                        11,
                        12,
                        DateTimeKind.Unspecified)
                    .AddTicks(1_234_560);
                const decimal amount = 12_345_678.12345678901234567890m;
                long id;

                await using (var context = CreateContext(store))
                {
                    var record = CreateRecord(
                        tenantId: 101,
                        displayName: "达梦数据库-多租户中文",
                        externalId: externalId,
                        amount: amount,
                        occurredAt: occurredAt,
                        status: ApplicationCompatibilityStatus.Suspended,
                        optionalNote: null,
                        isActive: true);

                    context.Records.Add(record);
                    await context.SaveChangesAsync();

                    Assert.True(record.Id > 0);
                    id = record.Id;
                }

                await using (var context = CreateContext(store))
                {
                    var record = await context.Records
                        .AsNoTracking()
                        .SingleAsync(item => item.Id == id);

                    Assert.Equal(externalId, record.ExternalId);
                    Assert.True(record.IsActive);
                    Assert.Null(record.OptionalNote);
                    Assert.Equal("达梦数据库-多租户中文", record.DisplayName);
                    Assert.Equal(amount, record.Amount);
                    Assert.Equal(occurredAt, record.OccurredAt);
                    Assert.Equal(ApplicationCompatibilityStatus.Suspended, record.Status);
                    Assert.Equal(101, record.TenantId);
                    Assert.False(record.IsDeleted);
                }
            });

    [DamengFact]
    public Task GlobalTenantAndSoftDeleteFiltersUseTheCurrentContextState()
        => ApplicationCompatibilityStore.WithTableAsync(
            async store =>
            {
                await using (var seedContext = CreateContext(store))
                {
                    seedContext.Records.AddRange(
                        CreateRecord(101, "租户一-有效"),
                        CreateRecord(101, "租户一-已删除", isDeleted: true),
                        CreateRecord(202, "租户二-有效"));

                    await seedContext.SaveChangesAsync();
                }

                await using (var tenantOneContext = CreateContext(
                                 store,
                                 currentTenantId: 101,
                                 applyTenantAndSoftDeleteFilter: true))
                {
                    var names = await tenantOneContext.Records
                        .OrderBy(item => item.Id)
                        .Select(item => item.DisplayName)
                        .ToListAsync();

                    Assert.Equal(["租户一-有效"], names);
                    Assert.Equal(
                        3,
                        await tenantOneContext.Records
                            .IgnoreQueryFilters()
                            .CountAsync());
                }

                await using (var tenantTwoContext = CreateContext(
                                 store,
                                 currentTenantId: 202,
                                 applyTenantAndSoftDeleteFilter: true))
                {
                    Assert.Equal(
                        ["租户二-有效"],
                        await tenantTwoContext.Records
                            .Select(item => item.DisplayName)
                            .ToListAsync());
                }
            });

    [DamengFact]
    public Task KeylessFromSqlQueryMaterializesUnicodeProjection()
        => ApplicationCompatibilityStore.WithTableAsync(
            async store =>
            {
                await using (var seedContext = CreateContext(store))
                {
                    seedContext.Records.Add(
                        CreateRecord(303, "无键查询-达梦"));
                    await seedContext.SaveChangesAsync();
                }

                await using (var context = CreateContext(store))
                {
                    var sql =
                        $"""
                        SELECT "DISPLAY_NAME", "TENANT_ID"
                        FROM "{store.TableName}"
                        WHERE "IS_DELETED" = 0
                        """;
                    var rows = await context.Projections
                        .FromSqlRaw(sql)
                        .ToListAsync();

                    var row = Assert.Single(rows);
                    Assert.Equal("无键查询-达梦", row.DisplayName);
                    Assert.Equal(303, row.TenantId);
                }
            });

    [DamengFact]
    public Task AddDbContextPoolReusesContextWithoutLeakingTenantState()
        => ApplicationCompatibilityStore.WithTableAsync(
            async store =>
            {
                PooledApplicationCompatibilityContext.ConfigureTable(store.TableName);

                var services = new ServiceCollection();
                services.AddDbContextPool<PooledApplicationCompatibilityContext>(
                    options => options
                        .UseDameng(store.ConnectionString)
                        .EnableDetailedErrors(),
                    poolSize: 1);

                await using var serviceProvider = services.BuildServiceProvider();
                Guid firstInstanceId;

                await using (var scope = serviceProvider.CreateAsyncScope())
                {
                    var context = scope.ServiceProvider
                        .GetRequiredService<PooledApplicationCompatibilityContext>();
                    firstInstanceId = context.ContextId.InstanceId;
                    context.CurrentTenantId = 401;
                    context.Records.AddRange(
                        CreateRecord(401, "池化-租户一"),
                        CreateRecord(402, "池化-租户二"));
                    await context.SaveChangesAsync();

                    Assert.Equal(
                        ["池化-租户一"],
                        await context.Records
                            .Select(item => item.DisplayName)
                            .ToListAsync());
                }

                await using (var scope = serviceProvider.CreateAsyncScope())
                {
                    var context = scope.ServiceProvider
                        .GetRequiredService<PooledApplicationCompatibilityContext>();

                    Assert.Equal(firstInstanceId, context.ContextId.InstanceId);
                    Assert.Null(context.CurrentTenantId);

                    context.CurrentTenantId = 402;

                    Assert.Equal(
                        ["池化-租户二"],
                        await context.Records
                            .Select(item => item.DisplayName)
                            .ToListAsync());
                }
            });

    [DamengFact]
    public Task ExecuteUpdateAndExecuteDeleteReportAffectedRowsAndPersistChanges()
        => ApplicationCompatibilityStore.WithTableAsync(
            async store =>
            {
                await using (var seedContext = CreateContext(store))
                {
                    seedContext.Records.AddRange(
                        CreateRecord(501, "批量一"),
                        CreateRecord(501, "批量二"),
                        CreateRecord(502, "待删除"));
                    await seedContext.SaveChangesAsync();
                }

                await using (var context = CreateContext(store))
                {
                    var updated = await context.Records
                        .Where(item => item.TenantId == 501)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(item => item.IsActive, false)
                                .SetProperty(
                                    item => item.Status,
                                    ApplicationCompatibilityStatus.Suspended));

                    Assert.Equal(2, updated);

                    var deleted = await context.Records
                        .Where(item => item.TenantId == 502)
                        .ExecuteDeleteAsync();

                    Assert.Equal(1, deleted);
                }

                await using (var verificationContext = CreateContext(store))
                {
                    var updatedRows = await verificationContext.Records
                        .Where(item => item.TenantId == 501)
                        .OrderBy(item => item.Id)
                        .ToListAsync();

                    Assert.Equal(2, updatedRows.Count);
                    Assert.All(
                        updatedRows,
                        item =>
                        {
                            Assert.False(item.IsActive);
                            Assert.Equal(
                                ApplicationCompatibilityStatus.Suspended,
                                item.Status);
                        });
                    Assert.False(
                        await verificationContext.Records
                            .AnyAsync(item => item.TenantId == 502));
                }
            });

    private static ApplicationCompatibilityContext CreateContext(
        ApplicationCompatibilityStore store,
        int currentTenantId = 0,
        bool applyTenantAndSoftDeleteFilter = false)
    {
        var options = new DbContextOptionsBuilder<ApplicationCompatibilityContext>()
            .UseDameng(store.ConnectionString)
            .ReplaceService<
                IModelCacheKeyFactory,
                ApplicationCompatibilityModelCacheKeyFactory>()
            .EnableDetailedErrors()
            .Options;

        return new ApplicationCompatibilityContext(
            options,
            store.TableName,
            currentTenantId,
            applyTenantAndSoftDeleteFilter);
    }

    private static ApplicationCompatibilityRecord CreateRecord(
        int tenantId,
        string displayName,
        Guid? externalId = null,
        decimal amount = 1.00000000000000000000m,
        DateTime? occurredAt = null,
        ApplicationCompatibilityStatus status = ApplicationCompatibilityStatus.Active,
        string? optionalNote = "可空字段",
        bool isActive = true,
        bool isDeleted = false)
        => new()
        {
            ExternalId = externalId ?? Guid.NewGuid(),
            IsActive = isActive,
            OptionalNote = optionalNote,
            DisplayName = displayName,
            Amount = amount,
            OccurredAt = occurredAt
                ?? new DateTime(
                    2026,
                    7,
                    23,
                    8,
                    0,
                    0,
                    DateTimeKind.Unspecified),
            Status = status,
            TenantId = tenantId,
            IsDeleted = isDeleted
        };
}

internal sealed class ApplicationCompatibilityContext(
    DbContextOptions<ApplicationCompatibilityContext> options,
    string tableName,
    int currentTenantId,
    bool applyTenantAndSoftDeleteFilter)
    : DbContext(options)
{
    public string TableName { get; } = tableName;

    public int CurrentTenantId { get; } = currentTenantId;

    public bool ApplyTenantAndSoftDeleteFilter { get; }
        = applyTenantAndSoftDeleteFilter;

    public DbSet<ApplicationCompatibilityRecord> Records
        => Set<ApplicationCompatibilityRecord>();

    public DbSet<ApplicationCompatibilityProjection> Projections
        => Set<ApplicationCompatibilityProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var record = modelBuilder.Entity<ApplicationCompatibilityRecord>();
        ApplicationCompatibilityModel.ConfigureRecord(record, TableName);
        record.HasQueryFilter(
            item => !ApplyTenantAndSoftDeleteFilter
                || (item.TenantId == CurrentTenantId && !item.IsDeleted));

        ApplicationCompatibilityModel.ConfigureProjection(
            modelBuilder.Entity<ApplicationCompatibilityProjection>());
    }
}

internal sealed class PooledApplicationCompatibilityContext
    : DbContext
{
    private static string? _tableName;

    public PooledApplicationCompatibilityContext(
        DbContextOptions<PooledApplicationCompatibilityContext> options)
        : base(options)
    {
    }

    public int? CurrentTenantId { get; set; }

    public DbSet<ApplicationCompatibilityRecord> Records
        => Set<ApplicationCompatibilityRecord>();

    public static void ConfigureTable(string tableName)
        => _tableName = tableName;

    public override void Dispose()
    {
        ResetTenantState();
        base.Dispose();
    }

    public override ValueTask DisposeAsync()
    {
        ResetTenantState();
        return base.DisposeAsync();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tableName = _tableName
            ?? throw new InvalidOperationException(
                "Configure the uniquely named test table before creating the context pool.");
        var record = modelBuilder.Entity<ApplicationCompatibilityRecord>();
        ApplicationCompatibilityModel.ConfigureRecord(record, tableName);
        record.HasQueryFilter(
            item => CurrentTenantId.HasValue
                && item.TenantId == CurrentTenantId.Value
                && !item.IsDeleted);
    }

    private void ResetTenantState()
        => CurrentTenantId = null;
}

internal static class ApplicationCompatibilityModel
{
    public static void ConfigureRecord(
        EntityTypeBuilder<ApplicationCompatibilityRecord> entity,
        string tableName)
    {
        entity.ToTable(tableName);
        entity.HasKey(item => item.Id);

        entity.Property(item => item.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();
        entity.Property(item => item.ExternalId)
            .HasColumnName("EXTERNAL_ID");
        entity.Property(item => item.IsActive)
            .HasColumnName("IS_ACTIVE");
        entity.Property(item => item.OptionalNote)
            .HasColumnName("OPTIONAL_NOTE")
            .HasMaxLength(200);
        entity.Property(item => item.DisplayName)
            .HasColumnName("DISPLAY_NAME")
            .HasMaxLength(200)
            .IsRequired();
        entity.Property(item => item.Amount)
            .HasColumnName("AMOUNT")
            .HasPrecision(38, 20);
        entity.Property(item => item.OccurredAt)
            .HasColumnName("OCCURRED_AT")
            .HasPrecision(6);
        entity.Property(item => item.Status)
            .HasColumnName("STATUS_TEXT")
            .HasConversion<string>()
            .HasMaxLength(32);
        entity.Property(item => item.TenantId)
            .HasColumnName("TENANT_ID");
        entity.Property(item => item.IsDeleted)
            .HasColumnName("IS_DELETED");
    }

    public static void ConfigureProjection(
        EntityTypeBuilder<ApplicationCompatibilityProjection> entity)
    {
        entity.HasNoKey();
        entity.Property(item => item.DisplayName)
            .HasColumnName("DISPLAY_NAME");
        entity.Property(item => item.TenantId)
            .HasColumnName("TENANT_ID");
    }
}

internal sealed class ApplicationCompatibilityModelCacheKeyFactory
    : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
        => context is ApplicationCompatibilityContext compatibilityContext
            ? (context.GetType(), compatibilityContext.TableName, designTime)
            : (context.GetType(), designTime);
}

internal sealed class ApplicationCompatibilityRecord
{
    public long Id { get; set; }

    public Guid ExternalId { get; set; }

    public bool IsActive { get; set; }

    public string? OptionalNote { get; set; }

    public required string DisplayName { get; set; }

    public decimal Amount { get; set; }

    public DateTime OccurredAt { get; set; }

    public ApplicationCompatibilityStatus Status { get; set; }

    public int TenantId { get; set; }

    public bool IsDeleted { get; set; }
}

internal sealed class ApplicationCompatibilityProjection
{
    public required string DisplayName { get; set; }

    public int TenantId { get; set; }
}

internal enum ApplicationCompatibilityStatus
{
    Active,
    Suspended
}

internal sealed class ApplicationCompatibilityStore
{
    private readonly string _connectionString;
    private bool _tableCreated;

    private ApplicationCompatibilityStore()
    {
        _connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        var suffix = Guid.NewGuid()
            .ToString("N", CultureInfo.InvariantCulture)[..16]
            .ToUpperInvariant();
        TableName = $"EF10_APP_{suffix}";
        PrimaryKeyName = $"PK_APP_{suffix}";
    }

    public string ConnectionString => _connectionString;

    public string TableName { get; }

    public string PrimaryKeyName { get; }

    public static async Task WithTableAsync(
        Func<ApplicationCompatibilityStore, Task> test)
    {
        var store = new ApplicationCompatibilityStore();

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
        await using var connection = new DmConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            CREATE TABLE "{TableName}" (
                "ID" BIGINT IDENTITY(1,1) NOT NULL,
                "EXTERNAL_ID" CHAR(36) NOT NULL,
                "IS_ACTIVE" BIT NOT NULL,
                "OPTIONAL_NOTE" NVARCHAR2(200) NULL,
                "DISPLAY_NAME" NVARCHAR2(200) NOT NULL,
                "AMOUNT" DECIMAL(38,20) NOT NULL,
                "OCCURRED_AT" TIMESTAMP(6) NOT NULL,
                "STATUS_TEXT" NVARCHAR2(32) NOT NULL,
                "TENANT_ID" INT NOT NULL,
                "IS_DELETED" BIT NOT NULL,
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

        await using var connection = new DmConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE \"{TableName}\"";
        await command.ExecuteNonQueryAsync();
        _tableCreated = false;
    }
}
