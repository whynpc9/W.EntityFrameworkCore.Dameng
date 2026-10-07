# 日期字符串转换

## 契约

本提供程序将无参数 `DateTime.ToString()` 翻译为达梦的
`CAST(value AS VARCHAR(100))`，输出采用**服务器默认日期时间文本**。
不按客户端 `CurrentCulture` 格式化，不承诺与 CLR 上的无参数 ToString 文本相同。
实际格式由服务器设置和原始 SQL 类型决定。

`Nullable<DateTime>.ToString()` 在转换外加 `COALESCE(..., '')`，NULL 返回空串；
`nullable.Value.ToString()` 继续采用 SQL NULL 传播，不人为改为空串。
结果映射为有界 VARCHAR，能够用于分组及文本谓词，避免误映射为不允许分组的 CLOB。

同一个转换在投影、筛选、排序和 `GroupBy` 中均可使用。分组直接使用转换结果，
提供程序不识别业务上的月份代表值，也不替调用方补做年月/日期截断：

```csharp
query.GroupBy(x => x.Timestamp.ToString())
    .Select(g => new { g.Key, Count = g.Count() });
```

字段本身已存储月份代表值时，按该值分组；字段保存完整时间戳时，按完整文本分组。
如果业务需要月粒度，可以显式指定已支持的格式或按 Year/Month 分组。

## 支持的重载和格式

| LINQ 表达式 | SQL 映射 |
| --- | --- |
| `dateTime.ToString()` | `CAST(value AS VARCHAR(100))` |
| `nullableDateTime.ToString()` | `COALESCE(CAST(value AS VARCHAR(100)), '')` |
| `dateTime.ToString("yyyy")` | `TO_CHAR(value, 'YYYY')` |
| `dateTime.ToString("yyyy-MM")` | `TO_CHAR(value, 'YYYY-MM')` |
| `dateTime.ToString("yyyy-MM-dd")` | `TO_CHAR(value, 'YYYY-MM-DD')` |
| `dateTime.ToString("yyyy-MM-dd HH:mm:ss")` | `TO_CHAR(value, 'YYYY-MM-DD HH24:MI:SS')` |
| `dateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff")` | `TO_CHAR(value, 'YYYY-MM-DD HH24:MI:SS.FF7')` |
| `dateTime.ToString("s")` 或 `dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss")` | `TO_CHAR(value, 'YYYY-MM-DD"T"HH24:MI:SS')` |

显式格式仅支持上表中的**常量格式字符串**，按服务器公历和数字字段格式化，不读取客户端文化或日历设置。
格式参数、来自列的动态格式、其他自定义/标准格式、`IFormatProvider` 重载以及
`DateTimeOffset`、`DateOnly`、`TimeOnly` 的 ToString 不在本轮支持范围内。
例如 `O` 包含 DateTimeKind/时区语义，不能据日期列的格式化函数猜测实现。
DateTime 经值转换器存成 ticks 或文本时，不应用原生日期函数或把 ticks 当日期字符串。

未映射的调用在需要 SQL 翻译的谓词/分组中失败；顶层投影是否可以由 EF 客户端计算
仍遵循 EF 的通常规则，不能据客户端投影成功声称服务器支持该重载。

## 服务器证据

使用已有持久测试用户验证原生转换以及 EF 查询。
参考环境为 DM8；单一实例不能建立所有达梦版本或兼容模式的最低支持保证。
SQL 能力依据[达梦官方函数手册](https://eco.dameng.com/document/dm/zh-cn/pm/function.html)
中的 CAST 与 TO_CHAR 语义，支持声明以真实回归为准。

探针区分字符串直接 CAST 与显式精度解析：

- 在参考实例上，直接将带七位小数秒的字符串 CAST 为 TIMESTAMP(7)，输入解析只保留六位；
  这不能用于判断七位时间戳列的显示能力。
- 对通过显式 FF7 解析或 EF 参数写入并确认读回的七位时间戳，
  原生 CAST 为 VARCHAR 保留 `.1234567` / `.1234568`，两种值的分组保持区分。
- 无格式 TO_CHAR 在该实例的默认模式只输出六位小数秒，因此无参数 ToString 选择 CAST。
  显式 `TO_CHAR(..., '...FF7')` 保留七位，已有真实回归。
- DATE 的默认文本为日期；TIMESTAMP/DATETIME 包含相应时间字段。
  公元 1 年的默认 CAST 文本年份可能为 `1`，而显式 YYYY 输出 `0001`。
  不承诺默认文本可按客户端文化往返 Parse，或默认文本排序等价于日期排序。

测试包括 DATE、DATETIME(0)、TIMESTAMP(7)、NULL、月份代表值、完整时间戳、
相差一个 tick 的值、闰日、DateTime.MinValue/MaxValue。
原生 CAST 查询是默认格式的独立预期；显式格式与 CLR 不变文化数字格式结果对照。

```bash
scripts/local-test/run.sh test unit --filter FullyQualifiedName~DamengDateTimeString
scripts/local-test/run.sh test functional --filter FullyQualifiedName~DamengDateTimeStringFunctional
scripts/local-test/run.sh test probes --filter FullyQualifiedName~DamengDateTimeStringCapabilityProbe
```

探针执行通过只表明测量已完成，不代表所有候选都支持。最终验收结果另行记录。
