using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace W.EntityFrameworkCore.Dameng.TestUtilities;

// 来自消费方查询形状的最小模型；不依赖消费方框架、实体或服务。
internal static class NumericAggregateQueryCases
{
    public static readonly string[] Names =
    [
        "Basic", "Conditional", "FilteredDistinct", "Constant", "Composite", "DateParts",
        "DateOnly", "NullableDistinct", "Computed", "HavingPaging", "ProjectedFlags", "TwoStage",
        "Concat", "InnerJoin", "LeftJoin", "GroupedJoin", "GroupKeys", "FilteredStatusDistinct", "CorrelatedExists"
    ];

    public static NumericAggregateRow[] Rows() =>
    [
        Row(1, "A", "alpha", 10.25m, 1, true, AggregateStatus.Complete, 1),
        Row(2, "A", "beta", 20.75m, 2, false, AggregateStatus.Pending, 1),
        Row(3, "A", null, null, null, true, AggregateStatus.Pending, 2),
        Row(4, "B", "alpha", -5.5m, 2, false, AggregateStatus.Pending, 2),
        Row(5, "B", "gamma", 5.5m, 4, true, AggregateStatus.Complete, 2),
        Row(6, "C", null, null, null, false, AggregateStatus.Pending, 1),
        Row(7, "C", null, null, null, true, AggregateStatus.Pending, 1),
        Row(8, null, "delta", 100m, 10, false, AggregateStatus.Complete, null)
    ];

    private static readonly AggregateStatus[] IncludedStatuses = [AggregateStatus.Complete];

    private static NumericAggregateRow Row(int id, string? department, string? name, decimal? fee,
        int? period, bool active, AggregateStatus status, int? month)
        => new()
        {
            Id = id,
            Department = department,
            Name = name,
            Fee = fee,
            Period = period,
            Active = active,
            Status = status,
            At = month is null ? null : new DateTime(2024, month.Value, 1, 12, 30, 0)
        };

    // 保留消费方的无格式日期转换重载，测试其数据库分组行为。
#pragma warning disable CA1305
    public static IQueryable<NumericAggregateResult> Query(IQueryable<NumericAggregateRow> rows, string name)
        => name switch
        {
            "Basic" => rows.GroupBy(r => r.Department).Select(g => new NumericAggregateResult
            {
                Key = g.Key,
                Count = g.Count(),
                LongCount = g.LongCount(),
                Sum = g.Sum(r => r.Fee),
                Average = g.Average(r => r.Fee),
                Minimum = g.Min(r => r.Fee),
                Maximum = g.Max(r => r.Fee),
                Extra = g.Average(r => (decimal?)r.Period!.Value),
                Other = g.Average(r => r.Period * 1.0m),
                Key2 = g.Max(r => r.Name),
                MinimumAt = g.Min(r => r.At),
                MaximumAt = g.Max(r => r.At)
            }),
            "Conditional" => rows.GroupBy(r => r.Department).Select(g => new NumericAggregateResult
            {
                Key = g.Key,
                Count = g.Count(r => r.Active),
                ConditionalCount = g.Sum(r => r.Active && r.Status == AggregateStatus.Complete ? 1 : 0),
                Sum = g.Sum(r => r.Fee < 0 ? -r.Fee : 0),
                Average = g.Average(r => r.Active ? r.Fee : null),
                Other = g.Average(r => r.Active ? r.Fee : 0),
                Extra = g.Max(r => r.Active ? 1 : 0),
                Minimum = g.Sum(r => r.Fee) - g.Sum(r => r.Active ? r.Fee : 0),
                Maximum = g.Average(r => r.Fee * r.Fee)
            }),
            "FilteredDistinct" => rows.GroupBy(r => r.Department).Select(g => new NumericAggregateResult
            {
                Key = g.Key,
                Count = g.Where(r => r.Active).Count(),
                // 消费方非空标识符的分组 DISTINCT COUNT 形状。
                DistinctCount = g.Where(r => r.Name != null).Select(r => r.Name).Distinct().Count(),
                Sum = g.Where(r => r.Active).Sum(r => r.Fee),
                Average = g.Where(r => r.Active).Average(r => r.Fee)
            }),
            "NullableDistinct" => rows.GroupBy(r => r.Department).Select(g => new NumericAggregateResult
            {
                Key = g.Key,
                DistinctCount = g.Where(r => r.Name != null).Select(r => r.Name).Distinct().Count()
                    + (g.Count(r => r.Name == null) > 0 ? 1 : 0)
            }),
            "FilteredStatusDistinct" => rows.GroupBy(r => r.Department).Select(g => new NumericAggregateResult
            {
                Key = g.Key,
                DistinctCount = g.Where(r => IncludedStatuses.Contains(r.Status) && r.Name != null)
                    .Select(r => r.Name).Distinct().Count(),
                Count = g.Count(r => IncludedStatuses.Contains(r.Status))
            }),
            "CorrelatedExists" => rows.Where(r => !rows.Any(other =>
                    other.Department == r.Department && other.Id > r.Id && other.Active))
                .GroupBy(r => r.Department).Select(g => new NumericAggregateResult
                { Key = g.Key, Count = g.Count(), Sum = g.Sum(r => r.Fee) }),
            "Constant" => rows.GroupBy(r => 1).Select(g => new NumericAggregateResult
            {
                Count = g.Count(),
                Sum = g.Sum(r => r.Fee),
                Average = g.Average(r => (r.Period ?? 0) * 1.0m),
                MinimumAt = g.Min(r => r.At),
                MaximumAt = g.Max(r => r.At)
            }),
            "Composite" => rows.GroupBy(r => new { r.Department, r.Status }).Select(g => new NumericAggregateResult
            {
                Key = g.Key.Department,
                ConditionalCount = (int)g.Key.Status,
                Count = g.Count(),
                Sum = g.Sum(r => r.Fee)
            }),
            "DateParts" => rows.Where(r => r.At != null).GroupBy(r => new { r.At!.Value.Year, r.At.Value.Month })
                .Select(g => new NumericAggregateResult
                {
                    Count = g.Count(),
                    ConditionalCount = g.Key.Year,
                    DistinctCount = g.Key.Month,
                    MinimumAt = new DateTime(g.Key.Year, g.Key.Month, 1),
                    Sum = g.Sum(r => r.Fee)
                }),
            "DateString" => rows.Where(r => r.At != null).GroupBy(r => r.At!.Value.ToString())
                .Select(g => new NumericAggregateResult
                {
                    MinimumAt = DateTime.Parse(g.Key),
                    Count = g.Count(),
                    Sum = g.Sum(r => r.Fee)
                }),
            "DateOnly" => rows.Where(r => r.At != null).GroupBy(r => r.At!.Value.Date)
                .Select(g => new NumericAggregateResult { MinimumAt = g.Key, Count = g.Count(), Sum = g.Sum(r => r.Fee) }),
            "Computed" => rows.GroupBy(r => new { Prefix = (r.Department ?? "?").Substring(0, 1), Flag = r.Active ? 1 : 0 })
                .Select(g => new NumericAggregateResult
                {
                    Key = g.Key.Prefix,
                    ConditionalCount = g.Key.Flag,
                    Count = g.Count(),
                    Sum = g.Sum(r => r.Fee)
                }),
            "HavingPaging" => rows.GroupBy(r => r.Department).Where(g => g.Count() > 1)
                .Select(g => new NumericAggregateResult { Key = g.Key, Count = g.Count(), Sum = g.Sum(r => r.Fee) })
                .OrderByDescending(r => r.Count).ThenBy(r => r.Key).Skip(1).Take(1),
            "ProjectedFlags" => rows.Select(r => new { r.Department, Flag = r.Active ? 1 : 0, Amount = r.Fee ?? 0 })
                .GroupBy(r => r.Department).Select(g => new NumericAggregateResult
                {
                    Key = g.Key,
                    ConditionalCount = g.Sum(r => r.Flag),
                    Sum = g.Sum(r => r.Amount)
                }),
            "TwoStage" => rows.GroupBy(r => new { r.Department, r.Name }).Select(g => new
            { Total = 1, Active = g.Max(r => r.Active ? 1 : 0), Amount = g.Sum(r => r.Fee) })
                .GroupBy(r => 1).Select(g => new NumericAggregateResult
                {
                    Count = g.Sum(r => r.Total),
                    ConditionalCount = g.Sum(r => r.Active),
                    Sum = g.Sum(r => r.Amount)
                }),
            "Concat" => rows.Where(r => r.Active).Concat(rows.Where(r => !r.Active))
                .GroupBy(r => r.Department).Select(g => new NumericAggregateResult
                { Key = g.Key, Count = g.Count(), Sum = g.Sum(r => r.Fee) }),
            "InnerJoin" => (from r in rows
                            join other in rows.Where(x => x.Active) on r.Department equals other.Department
                            group new { r, other } by r.Department into g
                            select new NumericAggregateResult
                            { Key = g.Key, Count = g.Count(), Sum = g.Sum(x => x.r.Fee), ConditionalCount = g.Sum(x => x.other.Active ? 1 : 0) }),
            "LeftJoin" => (from r in rows
                           join other in rows.Where(x => x.Active) on r.Id equals other.Id into matches
                           from other in matches.DefaultIfEmpty()
                           group new { r, other } by r.Department into g
                           select new NumericAggregateResult
                           { Key = g.Key, Count = g.Count(), Sum = g.Sum(x => x.other == null ? null : x.other.Fee) }),
            "GroupedJoin" => (from r in rows
                              join summary in rows.GroupBy(x => x.Department).Select(g => new
                              { Department = g.Key, Amount = g.Sum(x => x.Fee) })
                                  on r.Department equals summary.Department into matches
                              from summary in matches.DefaultIfEmpty()
                              group new { r, summary } by r.Department into g
                              select new NumericAggregateResult
                              { Key = g.Key, Count = g.Count(), Sum = g.Max(x => x.summary == null ? null : x.summary.Amount) }),
            "GroupKeys" => rows.GroupBy(r => new { r.Department, r.Status })
                .Select(g => new NumericAggregateResult { Key = g.Key.Department, ConditionalCount = (int)g.Key.Status }),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
#pragma warning restore CA1305
}

internal enum AggregateStatus { Pending, Complete }

internal sealed class NumericAggregateRow
{
    public int Id { get; set; }
    public string? Department { get; set; }
    public string? Name { get; set; }
    public decimal? Fee { get; set; }
    public int? Period { get; set; }
    public bool Active { get; set; }
    public AggregateStatus Status { get; set; }
    public DateTime? At { get; set; }
}

internal sealed record NumericAggregateResult
{
    public string? Key { get; set; }
    public string? Key2 { get; set; }
    public int Count { get; set; }
    public long LongCount { get; set; }
    public int ConditionalCount { get; set; }
    public int DistinctCount { get; set; }
    public decimal? Sum { get; set; }
    public decimal? Average { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public decimal? Extra { get; set; }
    public decimal? Other { get; set; }
    public int? MinimumPeriod { get; set; }
    public int? MaximumPeriod { get; set; }
    public int? PeriodSum { get; set; }
    public DateTime? MinimumAt { get; set; }
    public DateTime? MaximumAt { get; set; }
}

internal sealed class NumericAggregateContext(DbContextOptions<NumericAggregateContext> options,
    string tableName = "NUMERIC_AGGREGATE_ROWS") : DbContext(options)
{
    public string TableName { get; } = tableName;
    public DbSet<NumericAggregateRow> Rows => Set<NumericAggregateRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<NumericAggregateRow>(entity =>
        {
            entity.ToTable(TableName);
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedNever();
            entity.Property(r => r.Department).HasMaxLength(32);
            entity.Property(r => r.Name).HasMaxLength(32);
            entity.Property(r => r.Fee).HasPrecision(18, 4);
            entity.Property(r => r.At).HasColumnType("TIMESTAMP(7)");
        });
}

internal sealed class NumericAggregateModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
        => context is NumericAggregateContext aggregate
            ? (context.GetType(), aggregate.TableName, designTime)
            : (context.GetType(), designTime);
}
