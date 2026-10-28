# 计划 015：仅修改列注释时只生成 COMMENT 语句

> 执行者先核对当前代码与依赖结果，再按步骤实施和验证。未满足全部真实数据库门禁不得标记 DONE。协调者维护总索引；本计划不授权自动提交、推送或发布。

## 状态

- 状态：TODO；里程碑：M5（迁移与资源恢复）；发现：F04。
- 优先级：P1；工作量：M；修改风险：MED；类别：正确性/迁移。
- 计划基线：`c80c788fb3cd34d97626d578db580264a28a11f3`，2026-10-28。
- 依赖：`014-admin-ddl-recovery.md`、`012-shared-column-generation.md`。014 建立真实 DDL 测试的失败清理准则；012 已解决或明确关闭共享列生成分面调查，不能由本计划再次改变其有效注解规则。
- 分支：`codex/m5-015-comment-only-migrations`。在复用/新建的隔离工作树执行；不自动 commit、push、开发布或修改版本。

## 为什么修复

现有 IDENTITY 列仅新增、修改或删除 `HasComment` 时，EF model differ 仍生成 `AlterColumnOperation`。提供程序无条件先发 `ALTER TABLE ... MODIFY`，随后才发 `COMMENT ON COLUMN`，因而无必要地触及达梦的自增列修改禁限。相同问题也可能影响禁止 MODIFY 的其他列；即使普通列允许修改，多余 DDL 仍造成额外锁和隐式提交。

目标是准确识别“列结构、默认值、生成策略和其他语义完全不变，仅注释变化”，只输出一条事务被抑制的 COMMENT。其他结构修改、纯移除 IDENTITY、序列默认值、BYTE 语义守卫和不支持结构拒绝保持原契约。

## 当前代码与依据

仓库根目录 `/Users/wanghongyi/Projects/dameng-entityframework-core`。先读 `AGENTS.md`、`skills/dameng-sql/SKILL.md`、`skills/dameng-ef-migrations/SKILL.md`、`docs/migrations.md`。达梦 DDL 隐式提交；对象使用唯一名称，在 finally 精确清理。

`src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs:289-314`：

```csharp
builder.Append("ALTER TABLE ")
    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
    .Append(" MODIFY ");
ColumnDefinition(/* ... */, includeIdentity: false);
builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
EndStatement(builder);
if (!string.Equals(operation.Comment, operation.OldColumn?.Comment, StringComparison.Ordinal))
{
    GenerateColumnCommentStatement(/* ... */);
}
```

同文件 `IsIdentityRemovalOnly` 在 `:861-899` 已逐项比较存储分面与注解，但它特意忽略移除的 IDENTITY 元数据，并要求 Comment 相等，不能直接拿来充当本计划的比较器。比较 OldColumn 时不要比较 Name/Table/Schema：EF differ 不填这些旧值。

`test/W.EntityFrameworkCore.Dameng.Tests/DamengMigrationsSqlGeneratorTests.cs:766-769` 当前测试固定了错误的额外操作：

```csharp
Assert.Equal(
    "ALTER TABLE \"app\".\"Orders\" MODIFY \"Note\" NVARCHAR2(100) NULL;\n"
    + "COMMENT ON COLUMN \"app\".\"Orders\".\"Note\" IS '';\n",
    alterSql);
```

真实模型差异测试可模仿 `DamengMigrationsFunctionalTests.cs:397-403`：

```csharp
var modelV2 = contextV2.GetService<IDesignTimeModel>().Model;
var alterCommands = contextV2.GetService<IMigrationsSqlGenerator>().Generate(
    contextV2.GetService<IMigrationsModelDiffer>()
        .GetDifferences(modelV1.GetRelationalModel(), modelV2.GetRelationalModel()), modelV2);
```

该文件的 CommentContext 与缓存键目前只改变普通 NOTE 列，未覆盖 IDENTITY 注释。新用例必须来自不同真实模型版本，不可只手造 AlterColumn 来“证明” EF 差异路径。

公开依据：[EF 10.0.12 MigrationsModelDiffer](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Migrations/Internal/MigrationsModelDiffer.cs) 将 Comment 差异转换为 AlterColumn；[达梦 DDL 文档](https://eco.dameng.com/document/dm/zh-cn/pm/definition-statement.html) 的 MODIFY 使用说明限制自增列及某些索引列修改。本计划尚无新增实库复现，实施中必须先表征。

## 修改白名单

- `src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs`。
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengCommentOnlyMigrationsTests.cs`（新增）。
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengMigrationsSqlGeneratorTests.cs`。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengCommentOnlyMigrationsFunctionalTests.cs`（新增，由 W 项目自动链接）。
- `docs/migrations.md`、`docs/compatibility.md`、本计划文件。

不修改模型 API/约定、类型映射、驱动、共享列策略、数据库实例参数、依赖/锁和发布流程。若需要扩大支持 IDENTITY 类型变更，停止并另立任务。

```bash
git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengCommentOnlyMigrationsTests.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengMigrationsSqlGeneratorTests.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengCommentOnlyMigrationsFunctionalTests.cs docs/migrations.md docs/compatibility.md
git status --short
```

`c80c788` 是原始审查锚点。实施先读取 012/014 的已验收差异，更新执行记录中的实施 SHA 与摘录定位；可归因的预期前置修改不触发停工，非预期差异或契约冲突才暂停复核。保留用户和其他计划的修改，不能为适配旧摘录覆盖已有实现。

## 验证命令

002 已为入口增加 `--configuration Debug|Release`。全部测试通过 `scripts/local-test/run.sh` 执行，其负责临时可写 DOTNET_CLI_HOME、首次体验/遥测关闭、锁定还原、`-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`。不得裸跑未配置 dotnet，不清缓存。

| 用途 | 命令 | 预期 |
| --- | --- | --- |
| 新旧迁移单元 | `scripts/local-test/run.sh test unit --configuration Debug --filter 'FullyQualifiedName~DamengCommentOnlyMigrationsTests|FullyQualifiedName~DamengMigrationsSqlGeneratorTests|FullyQualifiedName~DamengDropIdentitySqlGeneratorTests'` | 新模型差异与既有拒绝边界均通过 |
| 官方真实聚焦 | `scripts/local-test/run.sh test functional --configuration Debug --filter 'Plan=015'` | 新增场景全部通过，无跳过 |
| W 真实聚焦 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'Plan=015'` | 官方结束后顺序运行，全部通过 |
| 官方迁移交叉 | `scripts/local-test/run.sh test functional --configuration Debug --filter 'FullyQualifiedName~DamengMigrationsFunctionalTests|FullyQualifiedName~DamengDropIdentityFunctionalTests|FullyQualifiedName~DamengCommentOnlyMigrationsFunctionalTests'` | 全部通过 |
| W 迁移交叉 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'FullyQualifiedName~DamengMigrationsFunctionalTests|FullyQualifiedName~DamengDropIdentityFunctionalTests|FullyQualifiedName~DamengCommentOnlyMigrationsFunctionalTests'` | 上一命令结束后运行，全部通过 |
| 官方完整单元 | `scripts/local-test/run.sh test unit --configuration Debug` | 全部通过 |
| W 完整单元 | `scripts/local-test/run.sh test unit --driver w --configuration Debug` | 全部通过 |

每次最终验收要求退出 0、Completed、total>0、total=executed=passed、failed=skipped=0，记录各自 TRX，不能把两驱动或重跑次数合并成一次结果。

## 实施步骤

### 1. 用真实模型差异锁定回归

新增 `DamengCommentOnlyMigrationsTests`。采用按模型版本区分的 `IModelCacheKeyFactory`，从 `IDesignTimeModel` 取得旧/新关系模型，经 `IMigrationsModelDiffer.GetDifferences` 再交实际生成器。列名、模式和表名覆盖引用转义；模型版本至少包括无注释→含撇号/中文注释→另一注释→无注释。

固定方法：

- `CommentChangesProduceOnlyCommentCommandsFromModelDiff`：Identity/普通列 × add/change/remove × 普通/幂等，至少 12 个基础组合；断言差异确实有 AlterColumn，SQL 最终只有 COMMENT 或其动态包装，没有 MODIFY/DROP DEFAULT/IDENTITY 变更，TransactionSuppressed 为 true。
- `StructuralChangeIsNotClassifiedAsCommentOnly`：精度/长度、可空性、默认值、序列名称、生成策略、其他语义注解分别变化；应保留已有 DDL 或既有明确拒绝，不能静默只写注释。
- `CommentChangeDoesNotEraseSharedColumnGenerationFacets`：使用 012 已确认可行的共享列模型，验证注释修改不改变有效策略；若该模型本应拒绝，断言仍拒绝。

**验证**：运行“新旧迁移单元”命令。新增仅 COMMENT 断言应先暴露当前多余 MODIFY；保存失败测试名称，不把此轮列为通过。若不能得到预期 AlterColumn，停止调查模型缓存或 EF differ，不能改成仅手造 operation。

### 2. 增加窄范围注释分支

在生成任何结构 DDL 前判定结构是否相同。比较 CLR/StoreType、Unicode、fixed/max length、precision/scale、rowversion、nullable、default CLR 值/SQL、computed SQL/IsStored、Collation 及全部语义注解；只排除注释本身和经证明不会改变列结构的 EF 辅助位置元数据。未知注解按保守方式视为结构差异。数组默认值等结构性相等用现有 EF/仓库语义，不依赖引用相等制造遗漏。

纯注释时调用现有 `GenerateColumnCommentStatement` 后返回；它按空字符串清除注释并经 `EndStatement` 抑制 DDL 事务。保持纯移除 IDENTITY 及不支持结构检查，不允许“注释变化”隐藏其他差异。若 BYTE guard 当前因纯注释 AlterColumn 自动插入，要将 guard 条件同步限定为实际重发 BYTE 列定义的操作，不能让无需改列的注释操作依赖 LENGTH_IN_CHAR。

更新旧 Note 注释测试的预期，保留“注释相同、列长度变化”测试及全部旧边界。不要泛化为大型迁移生成器重构。

**验证**：再次运行“新旧迁移单元”，全部通过；运行 `git diff --check` 退出 0。确认非法 IDENTITY 增加/恢复、seed/increment 改动与组合移除仍被拒绝。

### 3. 验证真实数据库注释增删改和生成值

新建 `DamengCommentOnlyMigrationsFunctionalTests`，类级 `[Trait("Plan", "015")]`，数据库测试使用现有 DamengFact/Theory。沿用功能项目已禁并行的程序集配置，所有对象随机唯一，在外层 finally 精确清理。

固定方法：

- `IdentityCommentLifecyclePreservesRowsAndNextGeneratedKey`：普通/幂等两种模式；创建含 IDENTITY 与普通文本列的表，插入基线行，从真实模型差异执行 add/change/remove，逐阶段查询列注释、既有行和下一次 EF SaveChanges 的生成键。断言没有重建/移除自增，没有额外结构命令。幂等阶段使用独立历史表和新 migration ID，将同一脚本执行两遍，历史每项一行。
- `OrdinaryColumnCommentLifecyclePreservesDefinition`：普通/幂等两种模式；核对长度、可空性、默认值和原数据；避免只检查注释字符串而漏掉结构变化。

幂等执行使用现有 `DamengScriptExecutor`，按 / 批次和匿名块执行，不把整个脚本当一条 ADO 命令。测试生成命令必须保留事务抑制语义，不通过人工删掉 MODIFY 来“修正”待测 SQL。每个真实用例至少验证一个带撇号或多行的注释。

**验证**：顺序执行官方真实聚焦、W 真实聚焦，至少上述 4 个场景各自全部通过。然后执行两条迁移交叉命令，不能并行。

### 4. 回归与文档

运行两组完整单元，记录 SDK、提交/工作树状态、驱动、profile、运行 ID 与 TRX，不包含连接信息。更新 `docs/migrations.md` 的注释变更边界及 `docs/compatibility.md`，区分 SQL/模型证据和真实增删改证据。

**验证**：两组完整单元通过；`git diff --check` 退出 0；`git diff --name-only` 仅白名单。协调者若安排 M5 最终 Release 验收，使用同一入口 `--configuration Release`，不能把 Debug 证据重命名为 Release。

## 完成标准

- [ ] 新单元从真实 model differ 产生操作；Identity/普通列注释三种变化与幂等包装通过。
- [ ] 纯注释命令没有 MODIFY，抑制事务；清除仍为 `IS ''`；无需改列时不增加 BYTE 环境守卫。
- [ ] 结构变化及语义注解变化不能被误判成纯注释；现有 IDENTITY/序列拒绝边界与共享列契约通过。
- [ ] 两驱动至少 4 个真实场景及迁移交叉回归通过；数据、列分面、自增和历史行均核对。
- [ ] 真实对象全部精确清理、无跳过；文档和范围检查通过。

## 停止条件

- model differ 未产生预期操作、依赖计划共享列合同尚未确定，或已存在不同的注释专用实现：停止复核，不覆盖。
- 必须改变类型映射、模型 API、驱动版本或支持 IDENTITY 结构变更才能完成：交回协调者。
- 实库/权限不可用、清理失败或门禁连续两次失败：BLOCKED，不能仅凭 SQL 文本标完成。
- 服务器与文档对 IDENTITY MODIFY 的行为不同：保留实测结果，仍评估多余 DDL，但不得继续宣称未经复现的失败范围。

## 维护提示

以后为 ColumnOperation 增加语义分面时，必须同步纯注释判定与反例测试。不要把“某个注解当前未生成 SQL”自动视为可以忽略；注释优化不能成为绕过不支持结构检查的入口。

## 批次交付与统一证据

本计划属于 M5，启动实施前须确认 M4 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/015.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为确认问题或验证缺口，须以“已修复并验证”闭合；补足有辨别力的测试证据也属于验证缺口的修复。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M5.md` 的输入。
