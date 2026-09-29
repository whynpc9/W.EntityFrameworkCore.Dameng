namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// 在 EF Core 查询中使用的达梦日期分桶函数。
/// </summary>
public static class DamengDateTimeDbFunctionsExtensions
{
    /// <summary>返回时间所在小时的起点。</summary>
    public static DateTime DamengTruncateHour(this DbFunctions _, DateTime value)
        => throw ServerOnly();

    /// <summary>返回可空时间所在小时的起点；SQL 输入为 NULL 时返回 NULL。</summary>
    public static DateTime? DamengTruncateHour(this DbFunctions _, DateTime? value)
        => throw ServerOnly();

    /// <summary>返回时间所在分钟的起点。</summary>
    public static DateTime DamengTruncateMinute(this DbFunctions _, DateTime value)
        => throw ServerOnly();

    /// <summary>返回可空时间所在分钟的起点；SQL 输入为 NULL 时返回 NULL。</summary>
    public static DateTime? DamengTruncateMinute(this DbFunctions _, DateTime? value)
        => throw ServerOnly();

    /// <summary>返回时间所属 ISO 周的周一零点；跨年时可能位于上一公历年。</summary>
    public static DateTime DamengStartOfIsoWeek(this DbFunctions _, DateTime value)
        => throw ServerOnly();

    /// <summary>返回可空时间所属 ISO 周的周一零点；跨年时可能位于上一公历年，SQL 输入为 NULL 时返回 NULL。</summary>
    public static DateTime? DamengStartOfIsoWeek(this DbFunctions _, DateTime? value)
        => throw ServerOnly();

    /// <summary>返回时间所属公历季度，取值为 1 到 4。</summary>
    public static int DamengQuarter(this DbFunctions _, DateTime value)
        => throw ServerOnly();

    /// <summary>返回可空时间所属公历季度，取值为 1 到 4；SQL 输入为 NULL 时返回 NULL。</summary>
    public static int? DamengQuarter(this DbFunctions _, DateTime? value)
        => throw ServerOnly();

    private static InvalidOperationException ServerOnly()
        => new("Dameng date bucket functions can only be used in an EF Core query translated to SQL.");
}
