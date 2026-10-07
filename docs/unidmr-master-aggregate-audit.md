# UniDmrCore master 聚合查询覆盖审计

## 范围与扫描基线

2026-10-07 更新消费方 `origin/master` 后，固定扫描提交
`d7ed9b6efd0cd0d7d507d5d72bd8d4b1eff92846`。本地 `master` 为
`c38c0d2cf6f1e60f7c44fb6c80f59fc6e41a2aa8`，因此没有拿本地分支代替远端。
消费方 `UniWeb.Dal.csproj:11` 锁定 EF Core **3.1.32**；本仓库是 EF Core **10**
提供程序。本次重建最小查询形状并在当前提供程序上执行，不是消费方全应用运行验收，
也不表示可以直接替换 EF Core 3.1 的提供程序。

扫描包含所有受 Git 管理的 C# 文件，检索方法语法和 `group ... by` 查询语法，
再追踪代表性查询的 `DbSet` / `IQueryable` 来源、继承的分组方法及物化边界。
主检索（GroupBy、Sum、Average、Min、Max、CountAsync 等）得到 **337 文件、2412 匹配行**；
加上同步 Count、Any/All 及查询语法后的初始逐行检索为 **714 文件、5043 候选行**。
PR 审查后，扫描工具改为关键词预筛加全文方法匹配和独立词法分组候选检查，修复
`group` / `by` 分行及聚合方法名与左括号分行的遗漏；同一固定提交的清单现为
**714 文件、5043 候选定位行**，原有 17 个查询语法定位全部保留。
查询语法定位记录 `group` 所在行，方法语法记录方法名所在行，同一运算符/行去重。
这些数字是静态检索候选，包含注释、Math、内存 LINQ 等，**不是 EF 查询数量**。
没有对消费方进行完整语义编译，也没有把每个业务方法逐一接到达梦上执行。
全部候选定位见[固定提交清单](unidmr-master-aggregate-inventory.tsv)，其中只记录路径、运算符和行号。

可重建清单（先自行更新消费方远端，再固定提交扫描）：

```bash
python3 scripts/audit/scan-aggregate-queries.py /path/to/UniDmrCore \
  --ref d7ed9b6efd0cd0d7d507d5d72bd8d4b1eff92846
python3 -B -m unittest discover -s scripts/audit -p 'test_*.py' -v
```

## 现有覆盖与缺口

原有 `DamengDateBucket*` / `DamengMathTranslation*` 用例覆盖特定计算键的分组计数；
`ApplicationCompatibilityFunctionalTests` 覆盖基础 Count/Any；
`DamengStringAggregate*` 覆盖 LISTAGG。LOB 分组拒绝也有单元断言。
它们不能证明消费方报表的数值聚合、条件平均值、分组 DISTINCT、嵌套聚合及 JOIN 组合正确。

新增共享最小模型 `test/Shared/NumericAggregateQueryCases.cs`，由单元和功能项目共用查询表达式；
数据含正负小数、重复值、混合 NULL、全 NULL 组、NULL 分组键、枚举、BIT 布尔和两个月份。
每种正向形状同时测试有数据和空表，记录实际执行命令，并与 CLR 结果比较；
Nullable.Value 聚合的独立预期按服务器跳过 NULL 的语义计算，不在 CLR 上执行会抛异常的 Value。
AVG 结果按六位小数比较，不声称任意精度、溢出或超大计数已验证。

下表源码定位均相对消费方根目录，行号属于上述固定提交。

| 查询形状 | 消费方代表性来源 | 新增用例 / 结论 |
| --- | --- | --- |
| 分组 Count、数值 Sum/Average/Min/Max、字符串 Max | `UniWeb.Business.Chs/Jobs/ChsBmCalculateHandler.cs:75-92` | `Basic`；真实执行，含小数和 NULL；同时扩展验证 LongCount、日期边界 |
| Nullable.Value 转 decimal、乘 1.0m 求平均 | 上述 Chs 源码 `:90`；`UniWeb.Business.Drgs.Stats/Resolvers/Cmi/CmiTrendResolver.cs:63` | `Basic`、`Constant`；小数平均没有被整数截断 |
| 条件计数、复合布尔/枚举、条件金额与平方平均 | `UniWeb.Business.Drgs.Stats/Resolvers/Death/DeathTrendResolver.cs:26-41`；`UniWeb.Business.LatestSettle.Stats/Resolvers/Drg/DrgSettleCompGroupResolver.cs:107` | `Conditional`；区分 AVG 的 ELSE NULL 与 ELSE 0 |
| 组内 Where、非空 Distinct Count、状态集合筛选 | `UniWeb.Business.Grade.Stats/Resolvers/GradeServiceability/GradeServiceabilityTrendResolver.cs:40-41`；`UniWeb.Business.Dmr/Modules/DmrPreCardQualityExamineReportModule.cs:331` | `FilteredDistinct`、`FilteredStatusDistinct`；真实执行 |
| 常量键全局汇总、条件 Count、日期 Min/Max | `UniWeb.Business.CenterFb/Operations/CenterFbAppealOperation.cs:84-99`；`UniWeb.Business.Dyn/Modules/DeptDynamicRegistration/DailyDataQueryHelper.cs:15` | `Constant`；空表返回零个组，与标量 Sum 的 0 区别明确 |
| 复合/可空键、日期 Year/Month、Date 分组 | `UniWeb.Business.CenterSettle/Modules/CenterSettleAppealReportModule.cs:45`；`UniWeb.Business.Dmr/Modules/Llm/LlmOper/LlmOperManagementModule.cs:65-98`；`UniWeb.Business.Dyn/Modules/DeptDynamicRegistration/DmrCardQueryModule.cs:141` | `Composite`、`DateParts`、`DateOnly`；真实执行；月份 DateTime 在顶层投影构造 |
| 表达式/动态层级键 | `UniWeb.Business.Emr/Modules/EmrQcStatsModule.cs:207-218`；`UniWeb.Business.Drgs.Stats/Resolvers/DrgComposition/DrgCompositionHierarchyResolver.cs:74` | `Computed`、`DynamicHierarchyKeyExecutesOnServer`；COALESCE、CASE、Substring、EF.Property |
| HAVING、分组重复检查、聚合排序分页 | `UniWeb.Business.Insur/HealthChecks/InsurCardCheck.cs:196,223` | 标量用例验证 GroupBy.Any(Count > 1)；`HavingPaging` 扩展验证分页组合 |
| 先投影条件标记再求和 | `UniWeb.Business.Emr/Modules/EmrQcStatsModule.cs:207-230`；`UniWeb.Business.Dyn/Modules/DeptDynamicRegistration/DmrCardQueryModule.cs:153-175` | `ProjectedFlags`；真实执行 |
| 先按患者去重 Max，再常量分组 Sum | `UniWeb.Business.Grade.Stats/Resolvers/GradeOperComplicationComposition/GradeOperComplicationCompositionHierarchyResolver.cs:69-93` | `TwoStage`；两个 GROUP BY，真实执行 |
| 内连接 / 左连接 / 聚合子查询连接 | `UniWeb.Business.Chs/Jobs/ChsBmCalculateHandler.cs:30-92`；`UniWeb.Business.Dyn/Modules/DeptDynamicRegistration/InpatientAmtModule.cs:334-385,595-615` | `InnerJoin`、`LeftJoin`、`GroupedJoin`；真实执行，保留连接基数与未匹配行 |
| 相关 EXISTS 筛选后分组 | `UniWeb.Business.Dmr/Modules/Llm/LlmOper/LlmOperManagementModule.cs:107-110` | `CorrelatedExists`；真实执行 |
| 分组键投影 / 分组数 Count、Distinct 后标量 Count | `UniWeb.Business.FundSupervise/Modules/IntelliCheckReportModule.cs:97-105,109-118` | `GroupKeys`、标量用例；真实执行 |
| 标量条件 Count、负数 Sum、Average/Min/Max、Any/All | `UniWeb.Business.CenterSettle/Modules/CenterSettleAppealMasterModule.cs:431-444`；`UniWeb.Business.Emr/Modules/EmrQcStatsModule.cs:110`；`UniWeb.Business.Dmr/Modules/DmrCardQualityExamineModule.cs:1924` | 标量用例；空集、全 NULL、非可空空集异常、整数平均均验证 |
| 集合合并后分组 | 多个 Dyn 流程在物化后 Union，例如 `DmrCardQueryModule.cs:178` | 该消费方操作发生在内存；`Concat` 额外验证服务端 UNION ALL 组合，不把内存 Union 当 EF 缺口 |

以下代表性命中明确属于内存操作：

- `DeptOutpatientAmtMgmt.cs:54` 的 `EntityToModel(List<OutpatientAmt>)` 汇总；
- `CenterSettleAppealMasterModule.cs:125,157,829,848` 的物化后分组；
- `IntelliCheckReportModule.cs:120` 的 `groups` 已于 `:118` ToListAsync；
- `EmrLLmOperExternalCalcModule.cs:81,103` 的 GroupBy.First 在已物化的 `aiCodes`/`missingAiCodes` 上；
- `BaseStatsQueryResolver.cs:46` 之前的 `_ApplyAggregations` 是服务器查询，之后
  `_ApplyPostAggregations(IEnumerable<...>)` 的比率/补充汇总是内存逻辑。

## 已确认的三个边界及可行改写

1. **无格式 DateTime.ToString 分组无法翻译**。`MedQualityTrendQueryResolver.cs:25`、
   `GradeTrendQueryResolver.cs:26`、`HqmsTrendQueryResolver.cs:26`、`SettleTrendQueryResolver.cs:22,25`
   等公共入口使用该形状。单元和真实连接用例均断言在发送命令前失败。
   月趋势建议按 `{ Year, Month }` 分组，顶层投影构造月份 DateTime；若业务需要日粒度则按 Date。
   两种形状已在真实库验证。不能直接把任意 ToString 分组替换为 Date 而假设业务语义等价。

2. **直接在未投影的 IGrouping 上做外层 Sum 无法翻译**。
   静态查到 19 行精确的 `.Sum(g => g.Count())`（其中部分是分支中的链式调用），例如
   `DrgCompositionTrendResolver.cs:31` 和
   `GradeOperComplicationCompositionHierarchyResolver.cs:125`。
   `IntelliCheckReportModule.cs:106-107` 的 `SumAsync(g => g.Sum(...))` 也失败。
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
   例如 `CmiTrendResolver.cs:46` 的 DrgCode 如果可空且业务要求 NULL 算一项，会受影响；
   如果本来要求排除 NULL，则显式 Where 非空后去重即可（已验证）。
   需要保留 CLR 的 NULL 单项时，已验证下列改写：

   ```csharp
   g.Where(x => x.Name != null).Select(x => x.Name).Distinct().Count()
       + (g.Count(x => x.Name == null) > 0 ? 1 : 0)
   ```

   普通 `query.Select(...).Distinct().CountAsync()` 经子查询保留 NULL，标量用例已验证；
   不应把组内 COUNT(DISTINCT) 的差异扩大到全部 DISTINCT 查询。

本次仅补测试、扫描工具和证据文档；未修改消费方业务代码或提供程序翻译实现。
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

首轮功能结果为 33/39，通过命令执行发现日期字符串分组失败、未投影外层聚合失败与
NULL 去重计数差异；另外一项失败来自 CLR 预期直接执行 Nullable.Value，现已使用独立预期。
原始失败 TRX 已从隔离工作树复制到当前项目的
`artifacts/query-translation/local-test/20261007T144718759Z-3df3d7d5/functional.trx`，
没有删除历史失败或把改写后的结果当成原查询通过。

## 最终验收记录

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
- 新增三个 C# 文件的 `dotnet format --verify-no-changes --include ...`、
  `git diff --check` 及固定提交扫描清单重建均通过。
- 未执行全功能、规范或管理员套件。本次是查询覆盖增量，不是发布验收或全应用迁移验收。


## PR 多行扫描回归

针对 Codex 的多行 `group ... by` 意见，预筛不再要求同一行包含完整子句；全文扫描同时保留
嵌套方法候选和方法名/左括号分行的定位。同一行重复调用仍只记录一次定位。扫描保持启发式
候选语义，不能把字符串、注释或内存 LINQ 命中当成已确认的服务端查询。
首轮四项 Python 回归通过，包括只含多行查询表达式的 Git 文件、匿名对象分组中的嵌套聚合、
多个分组子句、方法换行、固定提交与未提交工作区内容分离、确定性清单和零候选清单。
已使用文首固定提交重建 TSV；不增加 EF/驱动依赖，也不修改消费方代码。

PR 独立检出另完成完整单元 725/725（`20261007T200422282Z-b0fb8c0f/unit.trx`）。
同一反向工程生产文件的独立真实库复验 130/130（`20261007T195132558Z-f7e14259/functional.trx`），
本轮 Python 扫描修复没有修改 .NET 实现；聚合真实库 46/46 的既有记录见上文。

Cursor 进一步指出首版跨行正则以前方的 group 变量/注释起始，会吞掉后面的真实查询子句。
初版跨行清单的 716 文件/5051 定位包含误报及错位，不能作为最终清单。现改为独立词法候选检查，
跳过注释/字符串中的关键字与常见变量声明/使用上下文，并按表达式括号深度查找 by；
不让较早候选消费后面的 group 子句，group/by 本身作为元素标识符和成员名时仍可定位。
新增变量、foreach、注释、嵌套查询及关键字标识符回归，现为 7/7；修复前四个前缀子例均复现失败。
重新生成的清单恢复 InpatientAmtOfWardModule.cs:527，并逐项核对初始清单的 17 个
query_group 定位无丢失、无额外项；方法候选仍是启发式匹配，不声明完整 C# 语义解析。
