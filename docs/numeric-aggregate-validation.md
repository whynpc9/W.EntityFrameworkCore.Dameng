# 数值聚合查询覆盖与验证

## 验证范围

本记录面向本仓库的 EF Core 10 达梦提供程序，使用独立最小模型验证常见聚合查询形状。
验证包含 SQL 翻译断言和真实达梦执行，不代表完整应用验收，也不声明其他 EF Core 主版本的兼容性。

## 现有覆盖与缺口

原有 `DamengDateBucket*` / `DamengMathTranslation*` 用例覆盖特定计算键的分组计数；
`ApplicationCompatibilityFunctionalTests` 覆盖基础 Count/Any；
`DamengStringAggregate*` 覆盖 LISTAGG。LOB 分组拒绝也有单元断言。
数值聚合、条件平均值、分组 DISTINCT、嵌套聚合及 JOIN 组合需要专门的回归证据。

新增共享最小模型 `test/Shared/NumericAggregateQueryCases.cs`，由单元和功能项目共用查询表达式；
数据含正负小数、重复值、混合 NULL、全 NULL 组、NULL 分组键、枚举、BIT 布尔和两个月份。
每种正向形状同时测试有数据和空表，记录实际执行命令，并与 CLR 结果比较；
Nullable.Value 聚合的独立预期按服务器跳过 NULL 的语义计算，不在 CLR 上执行会抛异常的 Value。
AVG 结果按六位小数比较，不声称任意精度、溢出或超大计数已验证。

| 查询形状 | 新增用例 / 结论 |
| --- | --- |
| 分组 Count、数值 Sum/Average/Min/Max、字符串 Max | `Basic`；真实执行，含小数和 NULL；同时扩展验证 LongCount、日期边界 |
| Nullable.Value 转 decimal、乘 1.0m 求平均 | `Basic`、`Constant`；小数平均没有被整数截断 |
| 条件计数、复合布尔/枚举、条件金额与平方平均 | `Conditional`；区分 AVG 的 ELSE NULL 与 ELSE 0 |
| 组内 Where、非空 Distinct Count、状态集合筛选 | `FilteredDistinct`、`FilteredStatusDistinct`；真实执行 |
| 常量键全局汇总、条件 Count、日期 Min/Max | `Constant`；空表返回零个组，与标量 Sum 的 0 区别明确 |
| 复合/可空键、日期 Year/Month、Date 分组 | `Composite`、`DateParts`、`DateOnly`；真实执行；月份 DateTime 在顶层投影构造 |
| 表达式/动态层级键 | `Computed`、`DynamicHierarchyKeyExecutesOnServer`；COALESCE、CASE、Substring、EF.Property |
| HAVING、分组重复检查、聚合排序分页 | 标量用例验证 GroupBy.Any(Count > 1)；`HavingPaging` 扩展验证分页组合 |
| 先投影条件标记再求和 | `ProjectedFlags`；真实执行 |
| 先按业务键去重 Max，再常量分组 Sum | `TwoStage`；两个 GROUP BY，真实执行 |
| 内连接 / 左连接 / 聚合子查询连接 | `InnerJoin`、`LeftJoin`、`GroupedJoin`；真实执行，保留连接基数与未匹配行 |
| 相关 EXISTS 筛选后分组 | `CorrelatedExists`；真实执行 |
| 分组键投影 / 分组数 Count、Distinct 后标量 Count | `GroupKeys`、标量用例；真实执行 |
| 标量条件 Count、负数 Sum、Average/Min/Max、Any/All | 标量用例；空集、全 NULL、非可空空集异常、整数平均均验证 |
| 集合合并后分组 | `Concat` 验证服务端 UNION ALL 组合；物化后的内存 Union 不属于 EF SQL 翻译范围 |

`ToList` / `ToListAsync` / `AsEnumerable` 之后的集合聚合属于内存 LINQ，
不应将其列为提供程序 SQL 翻译缺口。测试仅对数据库端的查询形状作能力声明。

## 日期字符串分组与剩余边界

1. **DateTime.ToString 分组使用服务器文本转换**。
   无参数重载翻译为 `CAST(value AS VARCHAR(100))`，分组键就是转换后的字符串。
   提供程序不推断业务时间粒度，也不额外添加月份/日期截断。
   已存储为月份代表值的字段可以直接分组；对完整时间戳分组也允许，业务粒度由调用方决定。
   完整时间戳、月份代表值、日期列及 nullable 键已有真实库回归。
   服务器文本格式和支持的格式重载见[日期字符串转换](datetime-string-translation.md)。

2. **直接在未投影的 IGrouping 上做外层 Sum 无法翻译**。
   `GroupBy(...).Sum(g => g.Count())` 与 `SumAsync(g => g.Sum(...))` 均无法翻译。
   同步/异步、空表/有数据均已验证翻译失败且没有发出 SQL。
   保留分组语义的改写为：

   ```csharp
   await query.GroupBy(key).Select(g => g.Count()).SumAsync();
   await query.GroupBy(key).Select(g => g.Sum(x => x.Fee)).SumAsync();
   ```

   两种改写均在真实库通过。不存在 HAVING、分页等额外组处理时，计数也可以对原筛选查询
   直接 Count。此项是当前 EF 查询翻译路径的边界，不是达梦服务器拒绝聚合 SQL。

3. **组内 nullable 列的 Distinct().Count() 不计 NULL**。
   SQL 为 COUNT(DISTINCT ...)，本夹具 A 组 CLR=3、数据库=2；全 NULL 的 C 组 CLR=1、数据库=0。
   `NullableDistinctCountDocumentsSqlNullDifference` 明确保留该差异，未冒充 CLR 语义支持。
   如果业务要求 NULL 算一项，需要补计 NULL；如果要求排除 NULL，
   则显式 Where 非空后去重即可（已验证）。
   需要保留 CLR 的 NULL 单项时，已验证下列改写：

   ```csharp
   g.Where(x => x.Name != null).Select(x => x.Name).Distinct().Count()
       + (g.Count(x => x.Name == null) > 0 ? 1 : 0)
   ```

   普通 `query.Select(...).Distinct().CountAsync()` 经子查询保留 NULL，标量用例已验证；
   不应把组内 COUNT(DISTINCT) 的差异扩大到全部 DISTINCT 查询。

数值聚合用例本身未修改聚合翻译实现；日期字符串转换现已新增专用方法翻译器。
负向/差异用例通过只说明已确认这些边界，不表示原查询可行。

## 真实环境与复现

2026-10-07 使用现有持久测试用户执行；没有重新创建测试用户/表空间，也没有使用管理员
连接执行普通测试。只读 `info` 返回：`DM Database Server 64 V8` /
`DM Database Server x64 V8`，PAGE=32768、COMPATIBLE_MODE=0、LENGTH_IN_CHAR=0、
GLOBAL_CHARSET=1、CALC_AS_DECIMAL=0、JSON_MODE=0、CLOB_MAX_CALC_LEN=20480。
未取得新的精确 build 编号。驱动保持锁定的 DM.DmProvider 8.3.1.47463。
夹具使用唯一表名、非聚集主键，并在 DisposeAsync（含创建失败路径）精确 DROP。

```bash
scripts/local-test/run.sh test unit --filter FullyQualifiedName~DamengNumericAggregate
scripts/local-test/run.sh test functional --filter FullyQualifiedName~DamengNumericAggregate
```

日期字符串翻译实现前的首轮功能结果为 33/39，发现日期字符串分组失败、未投影外层聚合失败与
NULL 去重计数差异；另外一项失败来自 CLR 预期直接执行 Nullable.Value，现已使用独立预期。
原始失败 TRX 已从隔离工作树复制到当前项目的
`artifacts/query-translation/local-test/20261007T144718759Z-3df3d7d5/functional.trx`，
没有删除历史失败或把改写后的结果当成原查询通过。

## 2026-10-07 数值聚合增量验收记录（历史）

- 新增 **22 个单元用例**（19 种正向查询形状及 3 项专门断言），当前工作树聚焦运行
  **22/22**，失败 0、跳过 0，宿主 Completed。
  TRX：`artifacts/query-translation/local-test/20261007T145725760Z-9e24048a/unit.trx`。
- 当前工作树的真实达梦聚焦回归 **46/46**，失败 0、跳过 0，宿主 Completed。
  其中包括 38 项正向查询形状（有数据/空表）、标量/动态键、失败边界、NULL 差异及改写验证；
  不能把负向/差异用例通过解读成原查询支持。
  TRX：`artifacts/query-translation/local-test/20261007T145756981Z-74918e06/functional.trx`。
- 在干净提交 `5863d3755dbd2d333ac359bf1c0a789762adbaed` 上叠加本任务改动的隔离全单元回归
  **710/710**，失败 0、跳过 0；其 TRX 已复制到当前项目
  `artifacts/query-translation/local-test/20261007T145756981Z-5af747a6/unit.trx`。
  不把当前工作树的其他未提交修改计入这次隔离全单元验收。
- 新增三个 C# 文件的 `dotnet format --verify-no-changes --include ...` 及
  `git diff --check` 均通过。
- 未执行全功能、规范或管理员套件。本次是查询覆盖增量，不是发布验收或全应用迁移验收。


2026-10-08 的日期转换实现及当前回归结果见[日期字符串转换](datetime-string-translation.md)。
