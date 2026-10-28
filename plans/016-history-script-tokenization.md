# 计划 016：按词法位置转换历史表 CREATE TABLE 语句

> 执行者按本计划完成代码、单元和真实重复执行验收；不要把生成字符串测试当成幂等脚本实库证据。协调者维护总索引；不自动提交、推送或发布。

## 状态

- 状态：TODO；里程碑：M5（迁移与资源恢复）；发现：F07。
- 优先级：P2；工作量：S；修改风险：LOW；类别：正确性/迁移。
- 原始审查锚点：`c80c788fb3cd34d97626d578db580264a28a11f3`，2026-10-28。
- 依赖：`014-admin-ddl-recovery.md`、`015-comment-only-migrations.md`。014 规定失败恢复/精确清理；015 先完成同一迁移生成链路的变更，避免并行改动造成无法归因的证据。
- 分支：`codex/m5-016-history-script-tokenization`，使用已验收依赖所在的隔离工作树基线；不自动 commit/push/release。

## 要解决的问题

历史表建表脚本包含模式存在性匿名块和建表命令。当前实现对整段文本寻找第一个 `CREATE TABLE ` 子串；若合法带引号的模式名本身含此文本，命中的是匿名块内的名称字面量，改坏模式守卫，真正的建表语句仍无 `IF NOT EXISTS`。目标是仅在真正建表命令的 TABLE token 后插入子句，保持全部原始名称、引号、注释、空白与其他命令不变。

这不是扩大 DMSQL 语法支持，也不是把原始 SQL 扩展点当成注入入口。必须沿用达梦标识符逐组成部分引用和字面量转义，拒绝无法唯一识别的命令形态，不能退回 Replace/IndexOf 猜测。

## 当前实现

仓库 `/Users/wanghongyi/Projects/dameng-entityframework-core`。实施前读 `AGENTS.md`、`skills/dameng-sql/SKILL.md`、`skills/dameng-ef-migrations/SKILL.md`。

`src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengHistoryRepository.cs:47-59`：

```csharp
const string createTable = "CREATE TABLE ";
var script = GetCreateScript();
var createTableIndex = script.IndexOf(createTable, StringComparison.Ordinal);
if (createTableIndex < 0) { throw new InvalidOperationException(/* ... */); }
return script.Insert(createTableIndex + createTable.Length, "IF NOT EXISTS ");
```

`DamengMigrationsSqlGenerator.cs:385-396` 中 EnsureSchema 先输出：

```csharp
.AppendLine("BEGIN")
.AppendLine("    IF NOT EXISTS (")
// ...
.Append("        WHERE TYPE$ = 'SCH' AND NAME = ")
.AppendLine(stringTypeMapping.GenerateSqlLiteral(operation.Name))
// ...
.Append(stringTypeMapping.GenerateSqlLiteral(
    "CREATE SCHEMA " + Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name)))
```

可复用 `src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengSqlLexer.cs:3-9`：

```csharp
// Quoted text is one opaque token; comments and whitespace are skipped
// while source offsets are preserved.
internal readonly record struct Token(string Text, int Start, int End);
internal static IEnumerable<Token> Read(string sql)
```

Lexer 已跳过注释、保持引号内容为整体并提供原文 offset。`DamengHistoryRepository.GetCreateIfNotExistsCommands` 当前把完成的脚本作为 SuppressTransaction=true 的 SqlOperation；不要丢掉这一契约。EF 10.0.12 的 [HistoryRepository.GetCreateScript/GetCreateCommands](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Migrations/HistoryRepository.cs) 提供受保护的命令列表入口，可利用现有命令边界避免解析整个匿名块语法。

## 白名单与漂移

只允许修改/新增：

- `src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengHistoryRepository.cs`。
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengHistoryScriptTests.cs`（新增）。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengHistoryScriptFunctionalTests.cs`（新增，W 自动链接）。
- `docs/migrations.md`、`docs/compatibility.md`、本计划文件。

只读复用 lexer、迁移生成器和脚本执行器；不改 SQL lexer 语法、默认历史表名、锁 ID、公共 API、依赖版本/锁或数据库全局参数。必须变更白名单外文件时先交回协调者。

```bash
git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengHistoryRepository.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengHistoryScriptTests.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengHistoryScriptFunctionalTests.cs docs/migrations.md docs/compatibility.md
git status --short
```

`c80c788` 只是原始审查锚点。先读取 014/015 的已验收差异，更新本计划执行记录中的实施 SHA 和摘录定位；可归因的前置修改允许继续，只有非预期差异或契约冲突才暂停复核。保留用户未提交修改，不覆盖它们。

## 验证入口

依赖 M1 的 `--configuration Debug|Release` 和 `offline-fixtures` 已落地。本计划的纯脚本测试位于正式 unit 项目；真实测试在 functional 通道。入口自动设置临时可写 DOTNET_CLI_HOME、首次体验/遥测关闭，锁定还原和单节点/禁构建服务器配置；不裸跑 dotnet、不清包缓存。

| 用途 | 命令 | 成功条件 |
| --- | --- | --- |
| 历史脚本单元 | `scripts/local-test/run.sh test unit --configuration Debug --filter 'FullyQualifiedName~DamengHistoryScriptTests'` | 新增至少 6 种名称/模式场景全部通过 |
| 现有拆批回归 | `scripts/local-test/run.sh test offline-fixtures --configuration Debug --filter 'FullyQualifiedName~DamengScriptExecutorTests'` | 既有块/引用/注释边界全部通过，无数据库连接 |
| 官方真实聚焦 | `scripts/local-test/run.sh test functional --configuration Debug --filter 'Plan=016'` | 重复执行与名称核对全部通过 |
| W 真实聚焦 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'Plan=016'` | 官方结束后运行，相同范围全部通过 |
| 官方迁移交叉 | `scripts/local-test/run.sh test functional --configuration Debug --filter 'FullyQualifiedName~DamengMigrationsFunctionalTests|FullyQualifiedName~DamengHistoryScriptFunctionalTests'` | 全部通过 |
| W 迁移交叉 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'FullyQualifiedName~DamengMigrationsFunctionalTests|FullyQualifiedName~DamengHistoryScriptFunctionalTests'` | 前一命令结束后运行，全部通过 |
| 完整官方单元 | `scripts/local-test/run.sh test unit --configuration Debug` | 全部通过 |
| 完整 W 单元 | `scripts/local-test/run.sh test unit --driver w --configuration Debug` | 全部通过 |

最终门禁要求退出码 0、Completed、total>0、total=executed=passed、failed=skipped=0。两驱动及所有共享数据库通道严格串行；不把跳过/零测试当成功。

## 实施步骤

### 1. 通过实际历史仓储构造失败回归

新增 `DamengHistoryScriptTests`。通过正常 options 的 `MigrationsHistoryTable(historyName, schema)` 创建 DbContext，取实际 `IHistoryRepository`，对比 `GetCreateScript` 和 `GetCreateIfNotExistsScript`，不手工拼一个不可能由 EF 生成的脚本。

固定方法：

- `CreateIfMissingPreservesSchemaLiteralAndIdentifier`：Theory，默认模式、普通显式模式、含 `CREATE TABLE ` 的模式、含双引号/单引号的模式、含点号/中文的模式、含大小写相近关键字的模式至少 6 项。断言 EnsureSchema 的字面量及动态 CREATE SCHEMA 完全保持，唯一真正 CREATE TABLE 带 IF NOT EXISTS。
- `CreateIfMissingPreservesHistoryTableIdentifier`：历史表名自身带关键字、双引号、点号；断言完整引用路径一致，不修改名称中的文本。
- `CreateIfMissingContainsExactlyOneRealHistoryCreate`：通过原始脚本与结果断言只有一个真正建表位置增加子句，其他命令的字符序列完全保留；不调用待测转换函数产生 expected。

预期文本取自真实生成路径，但断言必须点名正确字面量/标识符与实际建表语句；不能用同一个待测 token 转换函数计算 expected。

**验证**：运行历史脚本单元命令。关键字模式用例应在修复前失败，其余基线应通过；若关键字模式未触发 EnsureSchema 或被 EF 预先拒绝，停止确认真实输入边界，不强造结果。

### 2. 在命令边界内定位词法 token

优先读取 `GetCreateCommands()` 的命令序列，对每条命令用现有 Lexer 查首批有效 token。只转换首两个有效 token 为 CREATE、TABLE 的独立建表命令；BEGIN/DECLARE 开头的模式守卫原样保留，不扫描其中的字符串去寻找建表子串。

以 TABLE token 的原文 End offset 插入 ` IF NOT EXISTS`，其余文本按原文拼接；大小写比较使用 OrdinalIgnoreCase。识别不到恰好一条历史表建表命令时抛清晰 InvalidOperationException。不能全局 Replace，不能重建全部 token 文本导致丢空白/注释，不能用 lexer 把引号内文本误当关键词。检查已存在 IF NOT EXISTS 的情况并明确避免重复插入；当前链路若不可能出现，也须有安全失败分支。

不要顺手重构 `GetCreateIfNotExistsCommands`、改变事务抑制或匿名块拆批方式。若当前已验收 EF 链路把多个顶层语句合在一个命令中而无法靠命令边界安全识别，停止并重新审查方案，不能以启发式字符串判断兜底。

**验证**：历史脚本单元及现有拆批回归命令均通过；`git diff --check` 退出 0；`rg -n 'IndexOf\(createTable|Replace\("CREATE TABLE' src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengHistoryRepository.cs` 应无匹配（rg 退出 1 为此项预期）。

### 3. 验证模式名与历史表的重复创建

新增 `DamengHistoryScriptFunctionalTests`，类级 `[Trait("Plan", "016")]`。固定方法 `HistoryCreationWithKeywordLikeSchemaIsRepeatable` 使用 DamengTheory，至少包含普通显式模式、`CREATE TABLE ` 前缀模式、带转义引号/中文模式 3 个场景；每项名称附唯一随机后缀，长度符合服务器限制。

从实际 IHistoryRepository 取得建表脚本，经既有 DamengScriptExecutor 执行两遍；独立目录查询确认只创建预期模式和预期历史表，原始名称未被篡改。随后使用 GetInsertScript 插入一条合成 MigrationId，再次运行建表脚本，断言原行保留且仅一行。额外测试 `IdempotentHistoryGuardKeepsMigrationRowSingle` 生成含该历史表模式的真正幂等迁移脚本并执行两遍，确认同一 MigrationId 一行。

固定 Theory `CreateIfNotExistsRunsDdlWithoutTransaction` 覆盖同步/异步的实际 `IHistoryRepository.CreateIfNotExists[Async]` 服务入口。对唯一表第一次返回 true、再次返回 false；用 EF 命令/事务拦截器捕获真实执行的建表及模式守卫命令，断言其 DbTransaction 为 null、创建过程中未开启迁移事务。不要反射 private 方法，也不要手工构造 SuppressTransaction=true 的 operation 来冒充被测历史仓储证据。

默认模式场景只创建唯一历史表；显式模式场景只清理该次唯一模式内明确创建的历史表及精确模式。外层 finally 必须记录创建尝试而非只记录成功返回，遵循 014 的未知结果核对原则；不使用 `DROP USER` 或前缀批量 DROP，不修改持久测试账户。

**验证**：官方真实聚焦后再运行 W 真实聚焦，至少 6 个场景全部通过。每次 finally 后独立核对自有对象不存在；缺 CREATE SCHEMA 权限时报 BLOCKED，不改用管理员连接绕过普通功能通道。

### 4. 回归并记录证据

按表运行两条迁移交叉及两组完整单元。更新迁移文档对合法引用名称的说明，并在兼容矩阵记录本轮具体模式名类别与两驱动实库范围，不泛称完整 DMSQL 解析器。

**验证**：上述命令全部通过，`git diff --check` 退出 0，`git diff --name-only` 与白名单一致。把实施 SHA、各驱动 TRX 和清理核对结果交协调者，不提交日志正文或连接信息。

## 完成标准

- [ ] 至少 6 类真实仓储生成名称用例通过，模式守卫字面量与引用标识符原样保留。
- [ ] 只修改唯一顶层 CREATE TABLE 的词法位置，无整段 Replace/IndexOf 兜底。
- [ ] 事务抑制、默认历史表、既有匿名块拆批与迁移功能没有回归。
- [ ] 两驱动至少 6 个真实场景及交叉回归通过；重复建表不丢历史行，幂等脚本每项一行，真实创建入口没有事务包裹 DDL。
- [ ] 所有自有对象精确清理、没有失败/跳过/零测试；文档和范围检查通过。

## 停止条件与维护

- 已验收前置计划产生可归因变化时更新摘录继续；出现未解释的历史仓储/EF 命令形态变化时停止。
- 无法在引号/注释外唯一确认建表命令、需改 lexer 语法或白名单外实现时，交回协调者。
- 服务器不接受某种合法性假设、缺普通 CREATE SCHEMA 权限或无法确认清理结果时，保存脱敏证据并 BLOCKED，不扩大权限。
- 合理修正后同一门禁连续两次失败则报告，不靠放宽断言消除失败。
- 后续 EF 更改历史表建表命令数量或引入额外守卫时，要重跑本计划名称矩阵；不能将安全失败改为静默原样返回。

## 批次交付与统一证据

本计划属于 M5，启动实施前须确认 M4 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/016.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为确认问题或验证缺口，须以“已修复并验证”闭合；补足有辨别力的测试证据也属于验证缺口的修复。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M5.md` 的输入。
