# 计划 006：查明并关闭时间字面量精度缺口

> 执行者须从第一步开始，逐步执行验证命令。此项是调查先行：先保存可重复的最小复现，确认存在缺口后才修改产品代码。不要把历史服务器记录当成本轮结果。由主审维护 `plans/README.md`；执行者只回填本计划和专用证据文件，不改索引。

## 状态

- 状态：DONE（本地，按维护者接受官方驱动风险后的修订边界）
- 批次：M2
- 审查 ID：I01
- 优先级：P1
- 工作量：M
- 修复风险：MED
- 置信度：MED；本计划编写时未重新执行真实库字面量精度回归。
- 类别：调查、正确性、测试
- 依赖：`plans/004-offline-ci.md`。该计划负责 PR 离线门禁；本计划仍使用现有 `unit` / `functional` 通道，不假定新增命令已经可用。
- 编写基线：`c80c788fb3cd34d97626d578db580264a28a11f3`，2026-10-28。

## 维护者验收修订（2026-10-28）

维护者确认官方驱动七位时态参数问题是其预先已知的实现缺陷，也是实现 W.DamengProvider 的原因，并明确要求“如实记录即可，作为使用本项目的预先已知的风险，不需要block”。此指令取代此前对此缺陷保留 BLOCKED 的决定。

本项继续修复并严格验证提供程序的 SQL 字面量，不修复或宣称修复官方参数实现。官方参数/常量一致性正对照采用整微秒输入；W 在支持的 profile 上继续采用相邻 tick。新增 `DriverTemporalParameterPrecisionRiskIsCharacterized`，在两驱动实际执行7/8/9位目标列：精确字面量写入并由服务器文本核验，常量命中ID=1；锁定官方驱动参数未命中作为明确的缺陷表征保留，W必须命中。该测试不接受任意异常、不跳过，也不把错误结果声明为正确能力；驱动将来修复时须同步更新表征与风险文档。

此处及原步骤中“参数必须与常量一致/不支持组合须拒绝”的要求，对这个已接受的官方缺陷采用上述风险表征；其余字面量保真、W参数、普通用户/清理和非零Completed/零失败跳过要求不变。历史红色TRX仍是实际缺陷证据，不能改写成过去已通过。

## 目的与边界

当前参数读写与 SQL 字面量使用不同路径。现有测试断言字面量文本包含七位小数，但仓库已有服务器记录指出裸 `TIMESTAMP` 字面量可能按六位解析。若此记录适用于当前实例，目标列即使是七位或九位也无法恢复表达式求值时已经丢失的精度，受影响路径包括种子、默认值和查询常量。

本计划交付经真实服务器验证的结果：对已支持路径保留完整 tick 与原偏移，无法无损表示的新增边界明确拒绝；或以可重复测试证明当前实现/上游已阻断问题并关闭调查。连接不可用、测试跳过、仅一个未覆盖目标形状的成功样本都不能作为否证。不得扩大官方驱动精度保证，不把 W 的读写能力移植成官方驱动的能力声明。

## 当前实现与既有模式

`src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDateTimeTypeMapping.cs:9-10,33-40`：

```csharp
/// fractional-second precision to the driver; SQL literals and reading stay EF Core's defaults.
protected override void ConfigureParameter(DbParameter parameter)
{
    base.ConfigureParameter(parameter);
    this.RequireDriver().ConfigureTemporalParameter(
        parameter,
        DbType == System.Data.DbType.Date ? 0 : Precision ?? 6);
}
```

`DamengDateTimeOffsetTypeMapping.cs:59-63` 同样只配置参数，无字面量重写：

```csharp
protected override void ConfigureParameter(DbParameter parameter)
{
    base.ConfigureParameter(parameter);
    this.RequireDriver().ConfigureTemporalParameter(parameter, Precision ?? 6);
}
```

`test/W.EntityFrameworkCore.Dameng.Tests/DamengTypeMappingTests.cs:746-770` 的 `TemporalLiteralsPreserveTheSeventhFractionalSecondDigit` 只比较生成的 `TIMESTAMP '...0000001'` 文本。相反，`test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverTemporalAndMigrationFunctionalTests.cs:258-260` 为测试数据构造使用：

```csharp
// A bare TIMESTAMP literal is six-digit on the server; TO_TIMESTAMP keeps every digit.
private static string Nine(string value)
    => $"TO_TIMESTAMP('{value}', 'YYYY-MM-DD HH24:MI:SS.FF9')";
```

此辅助函数只是一个既有服务器样本，不能直接推定 `TO_TIMESTAMP` 能同时解决默认值、DateTimeOffset、不同精度与所有查询上下文。`DamengMigrationsSqlGenerator.cs:506-508,522` 将 `InsertDataOperation` 交给基类；805-806 行调用基类 `DefaultValue`，无独立时间保真保护。

测试模式：单元从 `DbContext.GetService<IRelationalTypeMappingSource>()` 取得实际映射，参考 `DamengTypeMappingTests.TemporalLiteralsPreserveTheSeventhFractionalSecondDigit` 和 W 的 `WDriverTypeMappingTests.TemporalParametersUseScaleAndNeverNonZeroPrecision`。真实库参考 `DamengExtendedTypeMappingFunctionalTests.DateTimeRoundtripsTheSeventhFractionalSecondDigit`、`WDriverTemporalAndMigrationFunctionalTests.NineDigitColumnsAcceptNullAndWholeTickWrites`。共享功能测试通过 `UseDamengUnderTest` 选择驱动，新增共享 `.cs` 会被 W 功能项目自动链接，勿复制两个实现。

## 必须遵守的仓库契约

执行前读取 `skills/dameng-sql/SKILL.md`、`skills/dameng-ef-migrations/SKILL.md`。保留 EF Core 10 / 两驱动的锁定依赖，不复制或反编译官方 EF 提供程序。默认 `DateTime` / `DateTimeOffset` 列为七位；裸 TIMESTAMP 默认六位；TIME/INTERVAL 六位限制不属于本计划。保持 DateTime 无参 ToString 的服务器格式语义，不改日期分桶或客户端文化语义。

两驱动共享测试模式，所有数据库命令和通道必须串行；普通测试不能提升为管理员。测试对象使用唯一 `EF10_TLF_` 前缀，`finally` 中按确切模式/对象名清理并核对不存在。DDL 隐式提交，不用回滚代替清理，生成 DDL 保持 `TransactionSuppressed`。不新增自动重试、不变更服务器参数、不打印连接串、主机、用户或口令。W 明文仅沿用已配置本地 profile 的显式 `PlaintextAllowed`。

## 修改范围

仅允许修改以下文件；不存在的新文件按标注创建：

- `src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDateTimeTypeMapping.cs`
- `src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDateTimeOffsetTypeMapping.cs`
- `src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengTemporalLiteralHelper.cs`（仅在两种映射需要共享逻辑时创建）
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengTypeMappingTests.cs`
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengTemporalLiteralTests.cs`（新建）
- `test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverTemporalLiteralTests.cs`（新建）
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengTemporalLiteralFunctionalTests.cs`（新建并自动链接到 W）
- `docs/compatibility.md`
- `plans/evidence/006.md`（新建脱敏证据）
- 本计划文件，仅更新状态、结论及实际验证记录。

范围外：迁移 SQL 生成器、查询翻译器、其他类型映射、驱动桥接与参数序列化、锁文件、项目文件、启动器、发布流程、服务器配置、已有历史验收记录。引用这些文件只为理解调用链，不授权修改。若只能通过改动范围外文件修复，停止并上报扩展范围建议。不得顺便加入 JSON、流式 LOB 或其他产品方向。

## Git 与命令

使用隔离的 `codex/006-temporal-literal-fidelity` 工作分支，先检查已有工作树与未提交改动，不覆盖他人内容。未经新的明确指令不自动 commit、push、开 PR、合并或发布。

从仓库根目录执行。`scripts/local-test/run.sh` 已负责可写临时 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`、锁定还原、`-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`。不要绕过入口或清全局 NuGet 缓存。若需要额外直接运行 dotnet，必须先设置同样环境与适用标志。M1 的 002 计划可能加入 `--configuration`，本计划命令无需该选项；不能把默认 Debug 结果写成 Release 证据。

| 用途 | 命令 | 成功条件 |
| --- | --- | --- |
| 官方聚焦单元 | `scripts/local-test/run.sh test unit --filter 'Plan=006'` | Completed，非零测试数，全通过，无跳过 |
| W 聚焦单元 | `scripts/local-test/run.sh test unit --driver w --filter 'Plan=006'` | 同上 |
| 官方聚焦实库 | `scripts/local-test/run.sh test functional --filter 'Plan=006'` | 同上，实际执行服务器断言 |
| W 聚焦实库 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --filter 'Plan=006'` | 同上；在官方通道结束后运行 |
| 全部官方单元 | `scripts/local-test/run.sh test unit` | Completed，无失败/跳过 |
| 全部 W 单元 | `scripts/local-test/run.sh test unit --driver w` | Completed，无失败/跳过 |

新增测试统一标注 `[Trait("Plan", "006")]`，不得将上述正式回归标成 CapabilityProbe。

## 执行步骤

### 1. 检查漂移并固定调查矩阵

执行：

```bash
git status --short
git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDateTimeTypeMapping.cs src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDateTimeOffsetTypeMapping.cs src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengTemporalLiteralHelper.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengTypeMappingTests.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengTemporalLiteralTests.cs test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverTemporalLiteralTests.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengTemporalLiteralFunctionalTests.cs docs/compatibility.md plans/evidence/006.md plans/006-temporal-literal-fidelity.md
```

`c80c788fb3cd34d97626d578db580264a28a11f3` 是原始审查锚点，不是要求执行者恢复的版本。先读取已验收前置计划的实际差异，更新本计划摘录定位、证据中的实施 SHA，再继续；可归因且符合前置验收契约的修改不触发停工。只有非预期差异、未解释的调用链变化或契约冲突才暂停核对，不恢复旧版本。证据文件记录实际 HEAD、两个驱动、非敏感服务器版本和精度 capability、测试配置及以下矩阵，不记录连接信息。

矩阵必须覆盖：DateTime / DateTimeOffset；显式精度 6/7/8/9；裸 TIMESTAMP / DATETIME WITH TIME ZONE；NULL、整微秒、`.1234567`、相邻 tick、Min/Max 可表示边界。DateTimeOffset 至少含 `+00:00`、`+08:00`、`-05:30`，同时比较 `Ticks`、`Offset`、`UtcTicks`，不能只用默认相等比较掩盖偏移变化。六位目标的非整微秒属于拒绝边界，七到九位目标的整 tick 应保真；参数通道对不支持的驱动/profile 按已有合同明确拒绝并点名错误，不能把任意 SQL/网络异常视为预期。

验证：`rg -n 'DateTimeOffset|6/7/8/9|裸|参数|字面量|HasData|HasDefaultValue|查询常量' plans/evidence/006.md` → 每个调查维度都有明确条目。

### 2. 产品代码不变，建立最小复现和对照

新增三份测试文件，先取得实际映射的 `GenerateSqlLiteral` 结果，而不是手写一个假定等价表达式。功能测试分别执行该表达式、带参数表达式，并以服务器的高精度格式文本作为独立判据，再检查 EF 物化的 tick/偏移。查询常量用表达式树 `Expression.Constant` 等确定方式强制内联；捕获命令并断言常量用例确实没有对应参数、参数用例确实有参数，避免两条路径被 EF 参数化成同一条。

测试名称至少包含：

- `GeneratedTemporalLiteralMatchesExactServerValue`：6/7/8/9 及裸类型、两种 CLR 类型、边界值；六位不整齐值使用独立拒绝用例。
- `ParameterizedAndConstantTemporalPredicatesAgree`：相邻 tick 的等于/不等于/大小比较，匹配相同 ID 集合。
- `TemporalHasDataAndInsertDataKeepTicksAndOffset`：分别从 `HasData` 模型差异和显式 `InsertDataOperation` 生成并执行命令，不能只直接拼 INSERT。
- `TemporalDefaultValuesKeepTicksAndOffset`：`HasDefaultValue` 生成的建表/加列默认值实际插入后读回。
- `TemporalLiteralRejectsUnrepresentableTargetPrecision`：六位目标的非整微秒不被默默缩短，记录首次失败阶段；不把既有官方驱动参数限制当成本轮字面量修复成果。
- 单元 `TemporalLiteralGenerationRetainsMappingDriverAndFacets`、`TemporalLiteralUsesInvariantFormatting`，在 `zh-CN` / `en-US` / `fr-FR` 下验证文本稳定、Clone 保留驱动与精度、NULL 正常。

按命令表顺序运行四个聚焦通道。最小复现预期可能出现精度断言失败，这是本步骤允许的红灯；只接受明确的 tick/偏移/命中集合差异作为缺陷证据。保存脱敏 TRX 路径、测试名称、目标类型、差异和清理结果。测试宿主失败、连接错误、跳过或没有覆盖目标路径时停止，不能推进为“问题不存在”。

### 3. 根据证据选择修复或关闭分支

若最小复现确认缺口，先在功能测试中比较候选无损表达式在直接值、查询、默认值和种子上下文的行为，分别覆盖 DateTimeOffset 偏移。不得仅依据既有 `Nine` helper 就全面替换为 `TO_TIMESTAMP`；必要时参考公开达梦文档并用真实断言确认。确定可用表达式后，只在两种映射/可选内部 helper 中实现，使用不受文化影响的格式化和现有驱动绑定 Clone 模式。目标精度不足时明确拒绝，不先四舍五入再声称保真。

若全部目标形状在当前基线已经正确，或锁定 EF 明确在到达产品缺口前拒绝了某形状，则保留具有辨别力的回归，记录阻断 API/异常/版本和仍可达的其他形状；不改正确的产品代码。只有覆盖了完整调查矩阵才可将 I01 关闭为“不成立（已验证范围）”。若仅部分形状被阻断，继续处理其余部分，不能整体关闭。

验证：重跑两个 `Plan=006` 单元和两个功能命令 → 四通道 Completed、非零数、全通过、无跳过；修复分支需要记录先前失败用例现在通过，关闭分支需要记录实际阻断断言。旧 `TemporalLiteralsPreserveTheSeventhFractionalSecondDigit` 的文本期望应与已验证最终表达式一致，不能直接删除该覆盖。

### 4. 回归、证据与验收

运行两套完整单元，再串行运行现有相关功能切片：

```bash
scripts/local-test/run.sh test functional --filter 'FullyQualifiedName~DamengExtendedTypeMappingFunctionalTests|FullyQualifiedName~DamengMigrationsFunctionalTests'
scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --filter 'FullyQualifiedName~DamengExtendedTypeMappingFunctionalTests|FullyQualifiedName~DamengMigrationsFunctionalTests|FullyQualifiedName~WDriverTemporalAndMigrationFunctionalTests'
git diff --check
git status --short
```

预期：通道均 Completed、无失败/跳过；`git diff --check` 退出 0；工作区新增改动仅限白名单，保留他人既有改动。本计划证据记录每个 TRX、当前 HEAD、配置、已验证的驱动/服务器范围、未覆盖实例和清理核对。兼容性文档只更新本轮实际证明的字面量/参数差异，不改写历史验收数字。

## 完成标准

- [x] 完整矩阵和最小复现结果已经写入专用证据文件，缺陷确认/否证有明确结果与阶段。
- [x] 每个新增测试拥有 `Plan=006` Trait；四个聚焦通道实际执行且符合验收计数。
- [x] HasData、显式 InsertData、HasDefaultValue、强制常量及参数查询都执行并断言，DateTimeOffset 原偏移有独立断言。
- [x] 6/7/8/9、裸类型、NULL、整微秒/相邻 tick/边界值均有结果；未支持组合有精确拒绝合同。
- [x] 两套完整单元和指定功能回归通过；DDL 测试对象精确清理，无新服务器参数/权限要求。
- [x] `git diff --check` 通过，范围未扩张；主审收到状态与证据，本计划标记 DONE 或 BLOCKED 的具体原因。

## STOP 条件与维护

缺少真实库、没有可用精度 profile、驱动返回不可区分的错误，或不能在允许文件内提供无损表达式时停止并报告，不降低断言、不放宽锁定版本。初始预期红灯以外，同一验证经过一次合理修正仍失败时停止分析并回报。若官方参数通道暴露一个与字面量无关的新精度问题，单独记录，不扩大本计划实现范围。

后续 EF/驱动升级、字面量格式重写、迁移默认值或查询参数化改变时必须重跑此矩阵。代码审查重点是服务器实际值、偏移、内联与参数形状及拒绝边界；SQL 文本看起来有七位不是最终证据。不自动提交、推送、合并或发布。

## 批次交付与统一证据

本计划属于 M2，启动实施前须确认 M1 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/006.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为调查：确认后修复并验证，或用充分可重复证据确证无缺陷；缺环境、跳过和单次未复现不能闭合。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M2.md` 的输入。

## 本轮执行摘要（2026-10-28）

实施基线 `14acdae6753c7e7a796f0e32cf5787de889684ba`；详见 [唯一执行记录](evidence/006.md)。
官方七位参数查询与精确常量不一致已获维护者确认为可接受的预先已知风险；不扩展参数/驱动修复，按上方修订边界验收并保留原失败证据。
