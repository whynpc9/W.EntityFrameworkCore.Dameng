# 查询函数与边界

本文描述查询扩展工作树中的能力，发布状态和真实库证据分别见版本说明与
[兼容性矩阵](compatibility.md)。[实施台账](query-translation-execution.md)记录本轮验收。

## 字符串

```csharp
var query = context.Items.Where(item =>
    !string.IsNullOrEmpty(item.Name)
    && item.Code.Trim(new[] { '*', '.' }) == "ABC");
```

`IsNullOrEmpty` 区分 null、空串和纯空格，不替代 `IsNullOrWhiteSpace`。
`Trim`、`TrimStart`、`TrimEnd` 接受常量 BMP 字符和非空常量 BMP 字符数组；
动态数组、null/空数组与 surrogate 修剪字符不翻译。输入文本可以包含 emoji，
但不能把半个 surrogate 作为修剪字符。无参数 Trim 保留历史行为，不保证识别全部 Unicode 空白。

二、三、四个 string 参数的 `string.Concat` 与字符串 `+` 将 null 视为空串。
NCLOB 空值回退在 SQL 中保持 LOB 类型，避免回退空串将长表达式降为有界文本。
这不表示所有 LOB 运算或任意长度均可执行；服务器报错不会被静默转成截断值。

## 数值

数值翻译以可表示的有限值为范围；参考实例对 NaN 和正负 Infinity 的参数执行报数据溢出。
支持 `Math.Abs`、`Sign` 的 int/long/decimal/double 重载，以及 `Floor`、`Ceiling`
的 decimal/double 重载。int/long 的最小值取绝对值会报溢出，decimal 不经过 double 中转。

`Exp`、`Log`、二参数 `Log`、`Log10`、`Pow`、`Sqrt` 支持 double 重载。
它们适用于数据库函数的正常定义域；域外和溢出可能抛数据库错误，不能依赖 CLR 的
NaN/Infinity 行为。`Round`、三角函数、MathF 和未列出的重载不在本批支持范围。

## 日期分桶

```csharp
var hourly = context.Events
    .GroupBy(item => EF.Functions.DamengTruncateHour(item.OccurredAt))
    .Select(group => new { Hour = group.Key, Count = group.Count() });
```

以下函数提供 DateTime 和 DateTime? 两种重载；可空输入为 null 时结果为 null：

| 函数 | 结果 |
| --- | --- |
| `DamengTruncateHour` | 所在小时的起始时刻 |
| `DamengTruncateMinute` | 所在分钟的起始时刻 |
| `DamengStartOfIsoWeek` | 所在 ISO 周的周一零点日期，可落在上一年 |
| `DamengQuarter` | 1–4 的季度数 |

这些 EF.Functions 扩展仅在服务器查询中使用，直接调用会抛异常。
本批没有秒截断或 DateTimeOffset 分桶函数。按时间范围筛选时，优先使用
`value >= start && value < end`，避免对索引列施加函数；不自动改写调用方的任意谓词。

DateTimeOffset 列可以读取 `Year`、`Month`、`Day`、`Hour`、`Minute`、`Second`，
部件来自保存的原偏移对应的本地日期。同一瞬间的两个不同偏移值可能得到不同年月日。
`Offset`、`UtcDateTime`、`DateTime`、`Now`、`UtcNow` 和 Add* 仍不翻译；
本批不增加 100ns 精度或时间转换保证。

## 字符串分组聚合

```csharp
var names = context.Items
    .GroupBy(item => item.CategoryId)
    .Select(group => new
    {
        group.Key,
        Names = string.Join("、", group
            .OrderBy(item => item.SortOrder)
            .Select(item => item.Name))
    });
```

支持 IEnumerable<string> 的 `string.Join`（string 分隔符）和 `string.Concat`，
被聚合的表达式与已有类型映射的分隔符都必须是有界可变文本映射，例如 `HasMaxLength`
的 NVARCHAR2；固定 CHAR/NCHAR 列不能作为分隔符。常量和尚未映射的参数使用结果文本映射。
组内 Where 和显式排序保留，排序键升序显式使用 `NULLS FIRST`、降序使用 `NULLS LAST`；
null/空元素不会丢失其分隔符位置，null 分隔符按空串处理。
无显式排序时不承诺字符串顺序。Distinct、LOB、固定 CHAR/NCHAR 和 LOB 排序明确拒绝。

SQL 使用 LISTAGG；过长结果报数据库错误，不会截短。Join 为保持空元素位置会计算
带一个额外分隔符的中间值，因此可能比最终 CLR 字符串更早达到服务器长度边界。

## 集合与未开放能力

集合 `Contains` 沿用 EF 参数展开，真实回归包含空集合、null、重复、否定、
多个集合、分页和 0–2000 元素样本。没有加入未经证实的 500/999 限制，也不会
悄悄拆成多次查询。2,000 是测试规模，不是最大支持容量或性能保证。

本轮 JSON 标量读取试验未通过无损门槛，未发布相应 API。小数 DateTime.Add*
也继续拒绝。具体原因见兼容性矩阵，不能通过切到客户端计算来宣称服务器翻译已支持。
