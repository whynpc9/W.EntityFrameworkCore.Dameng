# 计划 007：补齐 distinct 集合与 LISTAGG 的 LOB 拒绝边界

## 状态与执行约定

- 状态：DONE（本地验收）。
- 里程碑：M2，查询与时间正确性；对应发现 F06。
- 优先级 P2；工作量 S；修复风险 LOW；置信度高，确认的是提前拒绝缺口，未复现服务器具体报错。
- 依赖：[004-offline-ci.md](004-offline-ci.md) 完成；其 `unit` 通道不读取 secrets，`offline-fixtures` 仅执行纯脚本白名单。本计划使用 `unit` / `functional`，不修改通道定义。
- 编写基线：`c80c788fb3cd34d97626d578db580264a28a11f3`，2026-10-28。
- 仓库：`/Users/wanghongyi/Projects/dameng-entityframework-core`。逐步实施并保存脱敏证据；不自动 commit/push/创建 PR。计划索引由协调者更新，结束时报告状态、修改范围和证据路径。

## 问题与目标

提供程序明确拒绝 LOB 上的 distinct 集合操作和 LISTAGG 排序，但实现存在两个不对称入口：集合检查只看左侧投影；LISTAGG 的 LOB 分类只包含文本类型。右侧出现 NCLOB，或组内排序使用 BLOB 时，可以绕过拒绝守卫。

目标是在发送 SQL 前一致拒绝这些已声明不支持的形状，并保持 bounded 集合、合法 `UNION ALL` 和 bounded LISTAGG 排序可用。这不是增加服务器 LOB 排序支持，也不应通过截短或强制 CAST 数据来“修复”查询。

## 当前状态与源码证据

`src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengLobQueryRewriter.cs:119–133`：

```csharp
private SetOperationBase VisitSetOperation(SetOperationBase setOperation)
{
    var visited = (SetOperationBase)base.VisitExtension(setOperation);

    if (visited.IsDistinct)
    {
        ThrowIfContainsLob(
            visited.Source1.Projection.Select(projection => projection.Expression),
            visited switch
            {
                UnionExpression => "UNION",
                IntersectExpression => "INTERSECT",
                ExceptExpression => "EXCEPT",
                _ => "set operation"
            });
    }
```

`Source2` 未检查。[EF 10.0.12 SelectExpression](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/SqlExpressions/SelectExpression.cs#L1966-L1977) 会分别保存两侧原始投影，并不保证右侧映射与左侧一致。已有 `DamengQueryTranslationTests.DistinctSetOperationOnLobFailsBeforeSendingInvalidSql` 只测试双侧 NCLOB 的 `Union`。

`src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengStringAggregateMethodTranslator.cs:55–58`：

```csharp
if (source.Orderings.Any(ordering => IsLob(ordering.Expression.TypeMapping)))
{
    throw new NotSupportedException("Dameng string.Join/Concat aggregation cannot order by a LOB expression.");
}
```

同文件 `IsLob` 只认识 CLOB、NCLOB、TEXT、NTEXT、LONG、LONGVARCHAR；而通用重写器 `GetLobKind` 还识别：

```csharp
"BLOB" or "IMAGE" or "LONGVARBINARY" or "BFILE"
    => LobKind.Binary,
```

通用 visitor 的排序检查仅针对 `SelectExpression.Orderings`。自定义 `DamengListAggExpression` 访问子节点本身不会触发排序用途检查，其内部 BLOB 排序仍会由 `DamengQuerySqlGenerator` 输出。

现有 `test/W.EntityFrameworkCore.Dameng.Tests/DamengStringAggregateTranslationTests.cs` 提供可复用的 LINQ 形状：

```csharp
var lobOrdering = Record.Exception(() => context.Rows
    .GroupBy(row => row.GroupId)
    .Select(group => string.Join("|", group
        .OrderBy(row => row.LobText)
        .Select(row => row.Text)))
    .ToQueryString());
Assert.NotNull(lobOrdering);
Assert.Contains("order by a LOB", lobOrdering.ToString(), StringComparison.Ordinal);
```

新测试沿用真实 LINQ 到 `ToQueryString()` 的模式，不能只测试分类 helper。`docs/query-functions.md:78` 已明确“LOB 排序明确拒绝”，`docs/compatibility.md` 同样区分 LOB 普通读写和查询限制。

## 文件白名单与约束

只允许修改：

- `src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengLobQueryRewriter.cs`。
- `src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengStringAggregateMethodTranslator.cs`。
- `src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengLobTypeClassifier.cs`（允许新建，仅共享现有类型分类，不公开 API）。
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengLobGuardTranslationTests.cs`（允许新建）。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengLobGuardFunctionalTests.cs`（允许新建）。
- `docs/query-functions.md`、`docs/compatibility.md`，仅补充本拒绝边界和验证证据。
- 本计划的执行记录；脱敏运行证据保存到忽略目录 `artifacts/query-translation/plan-007/`。

不得改 SQL 生成器、LISTAGG 的 null/空串补偿算法、原有 COALESCE 重写语义、普通排序 NULL 位置、类型映射容量、公共 API、项目文件或依赖锁。RowNumber 等其他查询形状未在本计划审查，不扩大成全查询树重写。005 可能修改其他字符串路径，不覆盖其工作。

先读 `skills/dameng-sql/SKILL.md`。两种驱动共享 SQL 服务；新官方功能测试会通过 W 项目的通配 Compile 自动链接，W 独立单元项目不会自动链接。保留中文文档、EF/驱动版本锁、`:name` 参数和分段引用标识符。真实库夹具使用唯一名称、显式非聚集主键及 `finally` 精确清理，不能修改共享实例或清扫其他对象。

## 漂移与命令

```bash
git status --short
git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengLobQueryRewriter.cs src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengStringAggregateMethodTranslator.cs src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengLobTypeClassifier.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengLobGuardTranslationTests.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengLobGuardFunctionalTests.cs docs/query-functions.md docs/compatibility.md
```

`c80c788` 是原始审查锚点。实施前读取已验收前置计划的实际差异，回读变动文件并确认缺口是否仍存在；可归因的预期修改应合并到本计划摘录定位、记录新实施 SHA 后继续，不触发停工。非预期差异或契约冲突才暂停核对。若缺口已被其他已验收提交修复，保留验证依据并与协调者对齐本计划剩余工作，不能覆盖该修复。

所有 .NET 验证通过 `scripts/local-test/run.sh`：它设置可写临时 CLI_HOME、关闭首次体验和遥测，使用 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`。不直接调用 `dotnet`，不清理 NuGet 缓存。命令顺序如下，任何零命中、跳过或未完成宿主都不算通过：

```bash
scripts/local-test/run.sh test unit --filter FullyQualifiedName~DamengLobGuardTranslationTests
scripts/local-test/run.sh test unit --filter 'FullyQualifiedName~DamengQueryTranslationTests|FullyQualifiedName~DamengStringAggregateTranslationTests|FullyQualifiedName~DamengBasicStringTranslationTests'
scripts/local-test/run.sh test functional --filter FullyQualifiedName~DamengLobGuardFunctionalTests
scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --filter FullyQualifiedName~DamengLobGuardFunctionalTests
scripts/local-test/run.sh test unit
scripts/local-test/run.sh test unit --driver w
git diff --check
```

每个功能通道必须在上一个通道清理完成后运行，两驱动共享测试模式。只使用持久普通测试用户，管理员通道不在范围内。新类至少含下文固定测试方法，保证过滤器有真实目标。

## 实施步骤

### 1. 用双向集合和二进制排序固定失败基线

创建 `DamengLobGuardTranslationTests`，使用独立 DbContext，模型包含 bounded `ShortText`、NCLOB `LongText`、VARBINARY `SmallBinary`、BLOB `LargeBinary`、分组键和普通排序键。为有界属性显式设置长度，避免约定把全部列映射成 LOB。

固定测试方法：

- `DistinctSetOperationsRejectLobOnEitherSide`：Union/Intersect/Except × LOB 左/右 × 文本/二进制，调用 `ToQueryString()` 断言提供程序异常包含操作名和 LOB 类型。两侧 CLR 类型须相同，不能因为模型或 LINQ 类型错误提前失败。
- `OrderedStringAggregatesRejectBinaryLobKeys`：Join/Concat × OrderBy/OrderByDescending × 首个/ThenBy 排序键中的 BLOB，selector 保持 bounded 文本；断言明确的 LOB 排序异常。
- `SupportedSetAndAggregateShapesRemainTranslatable`：bounded 双侧 distinct 集合、LOB Concat/UNION ALL、bounded LISTAGG 排序均成功生成 SQL。

在当前产品代码下先运行新单元类，预期右侧 LOB 或 BLOB 排序中的一个或多个拒绝断言失败。保存这些红色结果，这属于预期表征而非修复失败；不能为了保持测试绿而把未拒绝行为写成最终期望。

**验证**：`scripts/local-test/run.sh test unit --filter FullyQualifiedName~DamengLobGuardTranslationTests`。应发现三个固定方法且失败来自缺失的提供程序异常；如果都通过或先被上游其他检查拒绝，停下报告实际路径，不机械加守卫。

### 2. 对齐分类与拒绝检查

将现有文本/二进制 LOB 分类合并到唯一的 internal helper（如确需新增，使用白名单名称）。保持现有类别、别名和大小写行为；重写器仍需要区分 `TEXT_EQUAL` 与 `BLOB_EQUAL`，不能用单一布尔值破坏现有相等比较分派。

在 `VisitSetOperation` 完成子节点访问后、`IsDistinct` 为 true 时检查双方投影，异常继续使用现有操作名和建议。不能把 `UnionExpression` 一概拒绝；`Concat` 的 UNION ALL 允许保留重复 LOB 值。

LISTAGG 排序守卫复用同一分类，检查所有排序键。不得通过删除排序、改成哈希、截断值或隐式转换为 bounded 来规避。分类中的 BFILE/LONGVARBINARY 仅表示已知不支持比较的存储类别；若映射源不提供相应模型映射，不能把 helper 测试通过写成该类型存储已支持。

**验证**：新单元类与上述三类原回归命令都通过；`git diff --check` 退出 0。特别复核既有 LOB 等值仍使用正确函数、NCLOB COALESCE 不降为有界文本。

### 3. 证明拒绝发生于发送前，并保留真实合法路径

新建 `DamengLobGuardFunctionalTests`，固定方法 `UnsupportedLobShapesFailBeforeCommandExecution` 与 `SupportedLobControlsExecuteOnServer`。参照 `DamengBasicStringTranslationFunctionalTests` 的 `UseDamengUnderTest`、`DbCommandInterceptor`、唯一表名模型缓存键和 finally 清理模式；表只包含上述必要列，数据用两三行即可。

建立数据后清空命令计数。对右侧 LOB 集合和 LISTAGG BLOB 排序实际调用 `ToListAsync()`，断言提供程序异常和命令执行计数为 0，避免“发送后服务器报错”被当成守卫成功。正对照实际执行 bounded Union/Intersect/Except、LOB Concat 和 bounded 排序 Join/Concat，断言结果内容、重复项和显式顺序；不要对无排序集合依赖行顺序。

**验证**：顺序执行两驱动 `DamengLobGuardFunctionalTests`。两个 TRX 分别 Total > 0、Total = Executed = Passed、Failed = Skipped = 0、Completed；断言的零命令数只针对拒绝查询阶段，不能把建表和插入计入。

### 4. 更新精确边界并完成回归

文档写明 distinct 集合任一侧存在 LOB 都拒绝，LISTAGG 文本/二进制 LOB 排序均拒绝；原有合法 UNION ALL 路径继续保留。不写“所有 LOB 查询已验证”，不宣称服务器故障已复现。记录两驱动真实合法路径与发送前拒绝证据。

**验证**：两组全单元和 `git diff --check` 通过；`git diff --name-only`、`git status --short` 与白名单一致。已有无关工作区变化单独记录，不删除。

## 回归矩阵与完成标准

| 维度 | 范围 |
| --- | --- |
| distinct 集合 | Union、Intersect、Except；LOB 左/右；文本/二进制 |
| 聚合排序 | Join、Concat；升/降；首键/次键；文本/BLOB |
| 允许路径 | bounded distinct 集合、LOB UNION ALL、bounded 聚合排序 |
| 守卫层次 | ToQueryString 失败；实库夹具中枚举失败且执行命令数为 0 |
| 既有行为 | TEXT_EQUAL/BLOB_EQUAL 分类、NCLOB COALESCE、null/空串聚合 |

- [x] 三个固定单元方法和两个固定功能方法可被命名过滤器发现。
- [x] 原缺口的至少一个基线红色断言被保留，修复后新单元矩阵全部通过。
- [x] 任一侧 LOB 和任意聚合排序键 LOB 均在发送前拒绝；合法正对照在两驱动执行成功。
- [x] 两组完整单元和两组新功能回归通过，无失败/跳过；临时对象完成清理。
- [x] 没有为了修复拒绝缺口改写用户数据、顺序或长度；没有白名单外产品变更。
- [x] 文档与证据限定到实际验证范围，报告 DONE 或精确 BLOCKED 原因。

## STOP 条件与维护提示

如果混合映射集合已经被当前 EF 上游拒绝，或 BLOB 排序没有走进目标翻译器，先记录真实路径再调整计划；基线红色是有意的，不适用“遇到失败立即放弃”。修复阶段发生无法解释的既有等值/拼接回归，或者需要更改 SQL 生成器、类型映射和其他查询结构时停止扩展范围。缺数据库环境只能完成单元阶段并报告 BLOCKED，不能用零命中或跳过功能测试关闭。

后续新增自定义 SQL 节点时，普通 Select 的用途检查不会自动覆盖节点内部排序，应在新节点入口补用途守卫。新增 LOB 别名时只维护共同分类，并同时检查相等比较的文本/二进制分派以及两个拒绝入口。

## 批次交付与统一证据

本计划属于 M2，启动实施前须确认 M1 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/007.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为确认问题或验证缺口，须以“已修复并验证”闭合；补足有辨别力的测试证据也属于验证缺口的修复。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M2.md` 的输入。

## 本轮执行摘要（2026-10-28）

实施基线 `14acdae6753c7e7a796f0e32cf5787de889684ba`；详见 [唯一执行记录](evidence/007.md)。
