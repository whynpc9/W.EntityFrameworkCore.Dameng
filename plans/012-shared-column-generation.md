# 计划 012：确认并拒绝共享列的值生成配置冲突

> 本项先构造最小模型再决定是否修复。逐步执行命令并保存结果；若锁定 EF 已可靠拒绝所有候选冲突，保留回归和可重复证据关闭调查，不加入重复校验。环境失败或未触发目标形状不是否证。主审维护索引，执行者只更新本计划和专用证据。

## 状态

- 状态：IN_PROGRESS
- 批次：M4
- 审查 ID：I02
- 优先级：P2
- 工作量：M
- 修复风险：MED
- 置信度：MED；已有静态线索，尚无最小模型运行证据。
- 类别：调查、正确性、元数据
- 依赖：`plans/009-driver-invariants.md`（M3）。该项负责运行时驱动入口和参数一致性；本计划保留它建立的守卫，仅处理共享列元数据，不再次修改驱动桥接。
- 编写基线：`c80c788fb3cd34d97626d578db580264a28a11f3`，2026-10-28。

## 为什么需要调查

EF 表拆分允许多个实体属性指向同一物理列。当前提供程序只从其中一个属性生成 identity/sequence 注解，没有明显的共享列专用分面比较。如果两个属性显式要求不同 seed、increment 或序列，DDL 可能静默采用其中一项。必须先排除锁定 EF 10.0.12 在其他最终化步骤已经阻断此形状，再决定补充校验。

目标是让同一物理列的有效契约一致，冲突在模型/设计时阶段清晰拒绝。不能把共享主键外键的约定性 `None` 当作显式冲突，不能改变 TPT 复制键、TPC 共享序列或相同配置的正常模型。

## 当前源码与测试模式

`src/W.EntityFrameworkCore.Dameng/Metadata/Internal/DamengRelationalAnnotationProvider.cs:76-100`：

```csharp
var storeObject = StoreObjectIdentifier.Table(column.Table.Name, column.Table.Schema);
var property = column.PropertyMappings
    .Select(mapping => mapping.Property)
    .FirstOrDefault(candidate =>
        candidate.GetDamengValueGenerationStrategy(storeObject)
            != DamengValueGenerationStrategy.None);
// 后续输出这个 property 的 strategy、seed、increment 或 sequence name/schema。
```

`src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs:284-304` 按物理列名去重：

```csharp
if (!identityColumnsByTable.TryGetValue(table, out var columns))
{
    columns = new Dictionary<string, IProperty>(StringComparer.Ordinal);
    identityColumnsByTable.Add(table, columns);
}
columns.TryAdd(columnName, property);
```

此处用于一表多自增列拒绝，不比较同列属性的分面。validator 当前只覆盖 `Validate`，未重写共享属性的 `ValidateCompatible`。这些是调查线索，不能代替执行中的模型最终化结果。

`test/W.EntityFrameworkCore.Dameng.Tests/DamengValueGenerationTests.cs:215-241` 的现有模式从真实服务读取设计时关系模型：

```csharp
var model = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
var column = Assert.Single(Assert.Single(model.Tables).Columns);
Assert.Equal(DamengValueGenerationStrategy.IdentityColumn,
    column["Dameng:ValueGenerationStrategy"]);
Assert.Equal(7L, column["Dameng:IdentitySeed"]);
Assert.Equal(3, column["Dameng:IdentityIncrement"]);
```

参照 `DamengModelValidatorTests.MultipleIdentityColumnsInOneTableAreRejected` 的异常断言模式，以及 `DamengValueGenerationTests.SharedPrimaryKeyForeignKeyDoesNotUseIdentityByConvention`、`TptInheritedKeyUsesIdentityOnlyOnTheRootTable`、`TpcInheritedKeyUsesSharedSequenceMetadataOnEveryConcreteTable` 的兼容性形状。新测试必须使用 EF 构建器、正常最终化和实际设计时模型，不能直接伪造一个 IColumn 后宣称公开路径可达。

## 范围与仓库约束

只允许修改：

- `src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs`
- `src/W.EntityFrameworkCore.Dameng/Metadata/Internal/DamengRelationalAnnotationProvider.cs`
- `src/W.EntityFrameworkCore.Dameng/Metadata/Internal/DamengPropertyExtensions.cs`（只有共享列有效策略解析确需修正时）
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengSharedColumnGenerationTests.cs`（新建）
- `test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverSharedColumnGenerationTests.cs`（新建）
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengSharedColumnGenerationFunctionalTests.cs`（必要的新建双驱动正向回归）
- `docs/compatibility.md`
- `plans/evidence/012.md`（新建）
- 本计划文件的状态与结果。

范围外：驱动桥接、迁移 SQL 生成器、值生成策略公共 API/枚举、复杂类型递归、PK 聚集选项、表/索引填充因子、反向工程、依赖与锁文件、项目文件、发布和实例配置。尤其不能为满足负例把所有共享表、所有外键或所有 `None` 策略禁用。

保留 `UseDameng...` / `GetDameng...` 前缀；比较标识符时沿用当前 `Ordinal` 与已解析 schema 语义，不强制大写。生成 SQL/DDL 前读取 `skills/dameng-sql/SKILL.md` 与 `skills/dameng-ef-migrations/SKILL.md`，使用分段引用标识符和 `:name` 参数。若增加实库正例，对象名唯一，在 `finally` 精确清理；DDL 隐式提交且禁用事务，不改管理员权限、实例参数或自动重试。

## Git 与验证命令

工作分支建议 `codex/012-shared-column-generation`，使用隔离工作树并尊重既有未提交改动。没有新的明确指令不得自动 commit、push、开 PR、合并或发布。

使用统一入口；它配置可写 `DOTNET_CLI_HOME`、关闭首次体验/遥测，锁定还原并使用 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`。额外 dotnet 调用也必须先配置同样环境和标志，不清 NuGet 缓存。以下默认配置结果不能称为 Release 验收；计划 002 引入的配置选项不改变本计划的验证目标。

| 验证 | 命令 | 成功条件 |
| --- | --- | --- |
| 官方最小模型 | `scripts/local-test/run.sh test unit --filter 'Plan=012'` | 非零测试数、Completed、全通过 |
| W 最小模型 | `scripts/local-test/run.sh test unit --driver w --filter 'Plan=012'` | 同上 |
| 官方全部单元 | `scripts/local-test/run.sh test unit` | 无失败/跳过 |
| W 全部单元 | `scripts/local-test/run.sh test unit --driver w` | 无失败/跳过 |
| 官方正向实库（触发条件见步骤 4） | `scripts/local-test/run.sh test functional --filter 'Plan=012'` | 非零测试数、Completed、无失败/跳过 |
| W 正向实库（同上） | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --filter 'Plan=012'` | 官方结束后顺序运行，同样验收 |

新测试标 `[Trait("Plan", "012")]`。两驱动数据库通道共享测试模式，绝不并行。使用入口已有机密环境配置，不打印或硬编码真实连接串，不以缺连接导致的跳过计为通过。

## 执行步骤

### 1. 漂移检查及构造有效共享表基线

```bash
git status --short
git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs src/W.EntityFrameworkCore.Dameng/Metadata/Internal/DamengRelationalAnnotationProvider.cs src/W.EntityFrameworkCore.Dameng/Metadata/Internal/DamengPropertyExtensions.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengSharedColumnGenerationTests.cs test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverSharedColumnGenerationTests.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengSharedColumnGenerationFunctionalTests.cs docs/compatibility.md plans/evidence/012.md plans/012-shared-column-generation.md
```

`c80c788fb3cd34d97626d578db580264a28a11f3` 是原始审查锚点。实施先读取已验收前置计划的实际差异，更新本计划摘录定位及实施 SHA 后继续；可归因的预期前置修改不触发停工。只有非预期差异、未解释的调用链变化或契约冲突才暂停核对，不能恢复前置计划之前的实现。证据文件先记录实际 HEAD、EF/驱动锁定版本和依赖验收状态，不靠历史数字确认前置计划完成。

构造两个实体共享同表、同主键列和识别关系的最小模型；先使两属性显式相同 identity 配置正常最终化，检查它们确实是同一 `ITable`/`IColumn` 的两个 PropertyMappings。再构造普通共享主键关系的对照：主体 identity，依赖方约定性 `None`。不同配置使用不同 DbContext 类型或完整模型缓存键，不能让 EF 缓存使多个模型实际复用同一配置。

验证：`scripts/local-test/run.sh test unit --filter 'Plan=012&FullyQualifiedName~SharedColumnBaseline'` → 两个基线测试执行并通过，设计时物理列和映射属性数量有显式断言。

### 2. 仅添加最小复现，逐个确定上游是否已阻断

官方新测试类添加以下名称并使用 Theory 数据逐项测试；W 新测试类至少重复所有冲突类别和两个正常对照，使用 W 实际服务。两项目可以使用各自小型 fixture，不能让 W 测试只调用官方 options。

- `SharedColumnBaselineAcceptsMatchingIdentity`：相同 seed/increment，仅一个物理自增列。
- `SharedColumnBaselineAcceptsConventionForeignKeyCopy`：正常共享主键依赖方不会被误拒绝。
- `SharedColumnRejectsConflictingIdentitySeed`。
- `SharedColumnRejectsConflictingIdentityIncrement`，含正负非零增量。
- `SharedColumnRejectsIdentitySequenceConflict`，两个配置都显式。
- `SharedColumnRejectsDifferentSequenceNameOrSchema`，分别只变名称、只变 schema；相同有效 schema 的默认/显式表示作为正对照。
- `SharedColumnAcceptsMatchingSequence`：一个共享序列、实际 NEXTVAL 引用一致。
- `SharedColumnOutcomeDoesNotDependOnEntityRegistrationOrder`：正反注册顺序结果一致。
- `SharedColumnValidationPreservesTptAndTpcGeneration`：现有 TPT 根表生成、TPC 共享序列形状继续成立。

先在未改产品代码时运行两个 `Plan=012` 命令，记录：模型是否完成、设计时模型是否完成、最终列分面、拒绝阶段和异常类型。候选负例得到有效模型且不同分面被取其一才构成缺口。若 EF 因一个无关无效关系提前抛错，修正 fixture 后重测，不能把无效 fixture 当上游正确拦截。

预期：最小复现可能以“期望拒绝但未拒绝”失败；这是允许的调查红灯。对确实由上游相应一致性规则拒绝的场景，测试应断言具体类型/分面线索，不要用 `ThrowsAny<Exception>` 把任何失败都算通过。证据文件记录逐项结果。

### 3. 只为确认可达的缺口增加提供程序校验

若缺口成立，优先在 `DamengModelValidator` 的共享列兼容性扩展点调用基类校验后比较当前 store object 下的有效策略及 identity seed/increment、sequence name/schema。区分配置来源和有效语义：允许约定性共享 FK 复制，显式冲突点名两个属性、表/列及冲突分面。不要简单比较原始注解字典，不要仅靠排序决定哪个属性获胜。

只有证据显示公共有效策略解析本身错误时才改 `DamengPropertyExtensions`；保持 TPT/TPC 与主表/片段规则。如需要同时调整注解提供者，目的仅是对已经验证一致的映射确定性输出，不能抑制异常或悄悄改调用方配置。

若全部有效候选已被上游相应规则拒绝，保留负例与正例测试、写明上游阻断机制和锁定版本，以“调查关闭：现有实现已满足契约”完成，不为凑实现修改源码。仅部分候选被阻断时仍处理其余候选。

验证：两个 `Plan=012` 单元命令均 Completed、非零测试数、全通过，证据能够将红灯对应到绿灯，或明确列出全部候选的既有阻断。再运行 `scripts/local-test/run.sh test unit --filter 'FullyQualifiedName~DamengValueGenerationTests|FullyQualifiedName~DamengModelValidatorTests'` → 既有兼容性回归全部通过。

### 4. 选择必要的服务器验证并收束

若只新增确定性模型拒绝且所有正常模型生成的 SQL/关系分面与基线相同，本项结论限定为模型/SQL 单元证据，不必为此运行管理员或服务器回归；在证据中列出逐项 SQL/分面对照断言。若改动 `DamengPropertyExtensions` 或正常模型生成结果发生变化，必须新增共享功能测试 `MatchingSharedColumnIdentityRoundtrips` 和 `MatchingSharedColumnSequenceRoundtrips`，验证唯一对象的真实插入、生成值回读及两属性一致，按表中两个实库命令顺序运行。仅比较字符串不够证明改变的服务器路径正确。

运行两套完整单元，然后：

```bash
git diff --check
git status --short
rg -n 'SharedColumn|Plan.*012' test/W.EntityFrameworkCore.Dameng.Tests/DamengSharedColumnGenerationTests.cs test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverSharedColumnGenerationTests.cs
```

预期：单元全部通过；diff 检查退出 0；新增测试名称/Trait 完整；只新增白名单中的本项改动。证据记录实际测试数/TRX、对照 SQL、修复或关闭决定；文档只说明已验证的共享列边界，不声称全面表拆分/EF 关系一致性。

## 完成标准

- [ ] 最小有效模型证明所有负例与正常对照确实进入目标共享列形状，反向注册顺序有覆盖。
- [ ] identity seed/increment、identity/sequence、sequence name/schema 的冲突都有准确拒绝或可重复上游阻断证据。
- [ ] 约定性共享主键 None、相同 identity/sequence、TPT 和 TPC 正常模型继续通过。
- [ ] 两个聚焦和两套完整单元通道 Completed、无失败/跳过；必要时双驱动实库正向回归串行通过。
- [ ] 正常 SQL 未改变的结论有断言；若改变，真实插入/回读及清理有证据。
- [ ] 证据文件、兼容性边界和本计划状态已更新；`git diff --check` 通过，未越界修改。

## STOP 条件与维护

前置计划未验收、最小 fixture 无法获得有效共享表、锁定 EF 行为与假设不同且无法解释、涉及范围外生成策略改变时停止报告。调查预期红灯之外，同一验证合理修正一次仍失败时停止。不能通过禁用共享表、改变 key/FK 语义、放宽类型检查或升级依赖使测试“通过”。

EF 共享列最终化、PropertyMappings 顺序、值生成策略推导和设计时注解生成改变时重跑此组模型。维护者应关注显式配置与约定有效值的区别，以及拒绝是否点名两个冲突来源。复杂 rowversion 递归属于计划 013；不自动提交、推送、合并或发布。

## 批次交付与统一证据

本计划属于 M4，启动实施前须确认 M3 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/012.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为调查：确认后修复并验证，或用充分可重复证据确证无缺陷；缺环境、跳过和单次未复现不能闭合。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M4.md` 的输入。

## 本轮实施结果（2026-10-28）

从已合并 M3 `eb79cc8fb8b91e8cb2d6fb4ede255c61759b4056` 开始，在 `codex/m4-model-consistency` 串行集成；M1–M3 的漂移可归因，未回退。调查确认缺口并已补充精确模型校验，合法对照保持。正式证据见 [执行记录](evidence/012.md)；最终批次与远端门见 [M4](evidence/M4.md)。仅 M4，不启动 M5。
