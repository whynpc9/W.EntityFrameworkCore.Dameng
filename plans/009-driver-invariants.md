# 计划 009：补齐运行时连接、时态参数和显式模型的驱动一致性守卫

> 执行者须逐步执行并保留验证结果；本文件是实施计划，尚未实施。只修改白名单文件。主协调者维护索引和里程碑状态，执行者不要编辑 `plans/README.md`，不要自动提交、推送或创建 PR。

## 状态与前置门

- 状态：DONE（已验收，见 [执行证据](evidence/009.md)）；里程碑：M3「驱动与连接生命周期」。
- 发现：F03；优先级：P1；类别：正确性/架构；工作量：M；修复风险：MED。
- 基线：`c80c788fb3cd34d97626d578db580264a28a11f3`；编写日期：2026-10-28。
- 该 SHA 是原始审查锚点。实施先读取已验收前置计划的差异，更新证据中的摘录定位和实施 SHA 后继续；可归因的预期前置修改不触发停工，非预期差异或契约冲突才暂停核对。
- 依赖：计划 004 的离线验收入口已完成，且整个 M2 已关闭。M1 的计划 002 已为测试入口增加 `--configuration Debug|Release`。
- 后续：计划 010、011 使用本计划稳定后的连接边界。本计划单独通过不代表 M3 已关闭。
- 开始前在仓库根目录执行 `git status --short` 和 `git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalConnection.cs src/W.EntityFrameworkCore.Dameng/Infrastructure src/W.EntityFrameworkCore.Dameng.WDriver/Infrastructure test/W.EntityFrameworkCore.Dameng.Tests/DamengDriverInvariantTests.cs test/W.EntityFrameworkCore.Dameng.WDriver.Tests test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverInvariantFunctionalTests.cs docs/architecture.md docs/compatibility.md plans/evidence/009.md`。对前置计划的预期变更按实际完成提交核对；其他变更若使下列片段不再匹配，停止并报告漂移，不覆盖已有工作。

## 目标与现状

官方和 W 驱动共享 SQL，但时间参数、读取器和重试策略不同。初始 `UseDameng`/`UseDamengWithWDriver` 已检查连接类型，运行时 `Database.SetDbConnection` 和时态参数却存在入口遗漏；显式 `UseModel` 的只读查询还可能完全不创建参数。这些入口均应在发送 SQL 前拒绝不一致的驱动。

`src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalConnection.cs:7-20` 当前只有创建连接的覆写：

```csharp
internal sealed class DamengRelationalConnection : RelationalConnection
{
    private readonly DamengDriver _driver;
    // 构造函数保存注入的 driver。
    protected override DbConnection CreateDbConnection()
        => _driver.CreateConnection(ConnectionString!);
}
```

EF 10.0.12 的 [SetDbConnection](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Storage/RelationalConnection.cs#L164-L178) 会处置原连接并替换引用，不验证达梦驱动身份。因此新检查必须发生在调用基类之前，不能先处置旧连接再拒绝新连接。

`src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDateTimeTypeMapping.cs:33-40`：

```csharp
base.ConfigureParameter(parameter);
this.RequireDriver().ConfigureTemporalParameter(
    parameter,
    DbType == System.Data.DbType.Date ? 0 : Precision ?? 6);
```

`Infrastructure/DamengDriver.cs:114-116` 默认 `ConfigureTemporalParameter` 为空；W 的 `Infrastructure/Internal/DamengWDriver.cs:72-79` 仅检查精度范围后设置 `parameter.Precision = 0; parameter.Scale = (byte)fractionalSecondPrecision;`。DateTimeOffset、TimeOnly 同样经过此入口。对照应遵循的现有模式是两个驱动的 `SetParameterType`：

```csharp
if (parameter is not DmParameter dmParameter)
{
    throw CreateForeignParameterException(parameter);
}
```

`DamengDriverErrors` 用 `Exception.Data["Dameng:ErrorId"]` 保存稳定错误 ID；复用 `Dameng:ForeignConnection`、`Dameng:ForeignParameter`。新增模型错配使用 `Dameng:ForeignModel`，消息仅含驱动和模型类型信息，不含连接串或属性值。

`docs/architecture.md:29-30` 当前说跨驱动复用模型会明确失败，但 `WDriverTypeMappingTests.WMappingsRejectParametersOfAnotherDriver` 只测试 CLOB。`DamengDriverSingletonOptions` 检查服务注册与 options，不检查 `UseModel`。EF 的 [DbContextServices](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Internal/DbContextServices.cs#L86-L91) 会将显式模型交给 `IModelRuntimeInitializer`；基类只在尚未初始化时运行模型验证，所以只修改 `IModelValidator` 不能解决已初始化的模型复用。

## 不变量

- .NET 10、EF Core 10.0.12 和两个驱动的版本范围/锁文件保持不变；不得反编译官方 EF 提供程序。
- 共享代码只用 `DamengDriver` 和 `IDamengDriverBoundMapping` 判断驱动，不直接判断官方/W 的 ADO 类型。
- 官方 `ConfigureTemporalParameter` 只新增身份校验，保持原参数精度行为；W 仍使用 Scale，不增加自动重试。
- 同驱动 `UseModel`、上下文池和不同连接串应继续工作；不创建物理数据库，不扩展 DataSource/包装连接支持。
- 运行时模型标记不得进入迁移快照、迁移差异或脚手架模型注解；不能缓存 DbContext、连接、凭据或服务器能力。

## 文件白名单

产品文件：

- `src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalConnection.cs`
- `src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengOfficialDriver.cs`
- `src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengDriverErrors.cs`
- `src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelRuntimeInitializer.cs`（新增）
- `src/W.EntityFrameworkCore.Dameng/Infrastructure/DamengDriverConfiguration.cs`
- `src/W.EntityFrameworkCore.Dameng.WDriver/Infrastructure/Internal/DamengWDriver.cs`

测试与证据文件：

- `test/W.EntityFrameworkCore.Dameng.Tests/DamengDriverInvariantTests.cs`（新增）
- `test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverInvariantTests.cs`（新增）
- `test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverInvariantFunctionalTests.cs`（新增）
- `docs/architecture.md`、`docs/compatibility.md`
- `plans/evidence/009.md`（新增，只记录脱敏的命令、SHA、计数和证据路径）

其余文件不修改。现有 `DamengDriverSelectionTests`、`WDriverConfigurationTests`、`WDriverTypeMappingTests` 只作范例并回归；如必须修改其他映射或模型服务，停止并说明需要扩展的白名单与原因。

实施使用协调者指定的隔离工作区/分支；若协调者要求新分支，采用 `codex/009-driver-invariants`。本计划不授权自行创建提交、推送或 PR。不要重置、清理或暂存不属于本任务的改动。

## 环境与命令

以下命令均在 `/Users/wanghongyi/Projects/dameng-entityframework-core` 执行。`scripts/local-test/run.sh` 会设置独立可写 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`，执行锁定还原与单节点测试；不要绕过该入口运行本地回归。两种驱动的数据库通道不得并行，普通测试不得使用管理员连接。不得打印或编辑 `.local-test.secrets.json`。

| 用途 | 精确命令 | 通过条件 |
| --- | --- | --- |
| 官方新增单元 | `scripts/local-test/run.sh test unit --configuration Debug --filter 'FullyQualifiedName~DamengDriverInvariantTests'` | Completed，total > 0，无失败/跳过 |
| W 新增单元 | `scripts/local-test/run.sh test unit --driver w --configuration Debug --filter 'FullyQualifiedName~WDriverInvariantTests'` | 同上 |
| 双驱动聚焦实库（由 W 项目承载） | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'FullyQualifiedName~WDriverInvariantFunctionalTests'` | 同上；同一测试内顺序完成官方与 W 两套真实读写，各自断言并记录结果 |
| 官方全单元 | `scripts/local-test/run.sh test unit --configuration Debug` | 全部通过 |
| W 全单元 | `scripts/local-test/run.sh test unit --driver w --configuration Debug` | 全部通过 |
| Release W 单元 | `scripts/local-test/run.sh test unit --driver w --configuration Release --filter 'FullyQualifiedName~WDriverInvariantTests'` | 非零，全部通过 |
| 格式化差异 | `git diff --check` | 退出 0，无输出 |

每次验收同时核对 TRX 的新方法名称，不能把“过滤器不存在导致零测试”或数据库跳过视为成功。新类统一增加 `[Trait("AuditPlan", "009")]`，不依赖 Trait 来代替上述固定类名过滤。

## 实施步骤

### 1. 固定回归矩阵

创建两个离线测试类，借鉴 `DamengDriverSelectionTests` 的合成 driver 和已关闭的真实连接对象，不访问数据库。固定保留以下方法名，参数组合可以用 Theory 展开：

- `DamengDriverInvariantTests.RuntimeConnectionRejectsForeignDriverBeforeMutation`
- `DamengDriverInvariantTests.RuntimeConnectionPreservesOwnershipAndNullSemantics`
- `DamengDriverInvariantTests.OfficialTemporalMappingsRejectForeignParameters`
- `DamengDriverInvariantTests.SameDriverExplicitModelRemainsUsable`
- `WDriverInvariantTests.RuntimeConnectionRejectsOtherDriverInBothDirections`
- `WDriverInvariantTests.TemporalMappingsRejectForeignParametersIncludingNullAndClone`
- `WDriverInvariantTests.ExplicitModelRejectsOtherDriverForReadAndWrite`
- `WDriverInvariantTests.SameDriverExplicitModelAndPooledContextsRemainUsable`

测试连接所有权使用可计数的合成 driver/connection 实现，不为此放宽产品类型检查，也不假定两个具体连接类均可继承。先分别运行两个新增单元命令；预期当前基线在新增守卫断言上红灯，现有同驱动正例通过。将预期失败的方法与原因写入证据文件，编译错误不是有效红灯。

### 2. 守住运行时连接替换与时态参数

覆写 `DamengRelationalConnection.SetDbConnection(DbConnection? value, bool contextOwnsConnection)`：非 null 值必须先通过 `_driver.IsDriverConnection(value)`，否则抛现有错误；校验通过再委托基类。null 必须按 EF 现有契约处理，不能为“方便”保留或恢复基类已经清除的连接串；重新设置连接串后应由当前 driver 创建连接。拒绝不能改旧连接引用、状态、所有权或已配置重试策略。

分别在官方和 W 的 `ConfigureTemporalParameter` 中校验本驱动参数。官方参数检查成功后不新增 Scale/Precision 赋值；W 检查成功后保持既有赋值及 0..9 范围校验。不在共享映射中引入 `DmParameter` 类型判断。

**验证**：运行两份新增单元命令，连接和参数方法全部通过；模型拒绝方法在步骤 3 完成前可保持已记录的失败。额外执行 `git diff --check`，预期退出 0。

### 3. 在模型初始化服务封住只读 UseModel 路径

本计划明确允许增加 `DamengModelRuntimeInitializer : RelationalModelRuntimeInitializer`，构造依赖为 EF 的两个 initializer dependencies 加无状态 `DamengDriver`，在 `DamengDriverConfiguration` 注册 `IModelRuntimeInitializer`，不替换 `IDbContextServices`、查询编译器或模型缓存键。

覆写公开 `Initialize`，确保“已有 ModelDependencies”的显式模型也经过本提供程序检查；不能仅覆写只在首次初始化调用的 `InitializeModel`。推荐实现顺序：

1. 对已经存在的仅运行时驱动标记检查身份；标记值只保存无状态 driver，比较使用 `DamengDriver.Equals`，不用名称字符串替代身份。
2. 委托基类保留 EF 初始化/设计时行为；检查完成模型内所有 `IDamengDriverBoundMapping` 的 driver，包括实体标量、owned/继承属性、complex 嵌套属性以及已支持集合元素映射。不能把未知映射当作另一个驱动，也不能静默重新绑定外来映射。
3. 为本次初始化所暴露的设计时/只读运行时模型记录同一个仅运行时身份，令只有 int 等中立属性的已初始化模型也不会跨 driver 重用。使用模型的原子 runtime-annotation/get-or-add 能力或等效线程安全机制；并发首次初始化若身份不同必须有一方拒绝，不能最后写入者获胜。
4. 错配抛 `Dameng:ForeignModel`，且发生在创建/打开业务连接和执行 SQL 之前。不要借修复重写用户模型或修改其持久注解。

在 `ExplicitModelRejectsOtherDriverForReadAndWrite` 中分别验证：不带参数的 DateTimeOffset 投影、普通常量只读投影、添加实体的写路径、仅 int 的模型；两方向都拒绝。用计数连接/拦截器证明零次 Open/Execute。对同驱动模型覆盖参数/无参数查询生成、owned/complex 属性及上下文池复用。

**决策门**：如果公开 EF 10.0.12 API 不能安全枚举现有受支持属性或不能线程安全记录身份，停止并提交可复现测试和替代服务方案供协调者决策，不能只实现写参数 guard 就关闭 F03。此任务不顺带宣称 `dotnet ef dbcontext optimize`、NativeAOT 或跨驱动编译模型受到支持；只能报告实际验证的 `UseModel` 范围。

**验证**：两份新增单元命令全部通过；运行两驱动全单元，含既有迁移差异/服务缓存/设计时生成测试全部通过，证明运行时标记未产生模型 DDL 差异。

### 4. 以真实读写核对同驱动正例并收口文档

新增 `WDriverInvariantFunctionalTests`，固定方法 `BothDriversSameDriverExplicitModelRoundtripsTemporalValues` 与 `BothDriversForeignExplicitModelFailsBeforeOpeningDatabase`。W 功能项目已通过项目引用同时包含两个 provider；参照 `WDriverTemporalAndMigrationFunctionalTests.MigrationsCanSwitchBetweenTheOfficialAndWDrivers` 的显式 `UseDameng` / `UseDamengWithWDriver` options 分支，在一个测试中先官方、后 W 顺序执行，不使用只会选择 W 的 `UseDamengUnderTest` 代替官方正例。连接来自同一普通测试账户；仅在内存中按该现有测试的 `OfficialConnectionString` 模式移除 W 专属 `transport_security` 选项，不打印或持久化派生连接字符串。

正例对每个 driver 分别建立唯一名称表、取得该 driver 已初始化的模型，再以同 driver 的新上下文 `UseModel`：执行无参数时态投影、带参数读取、DateTime/DateTimeOffset/TimeOnly 的插入及更新后读取，逐项断言值和当前合同允许的精度。官方、W 分支各自证明至少一次真实 Open 和 Execute，分别记录非敏感 driver 名称、断言范围和清理结果；不能把 SQL 生成、单元正例或 W 成功代替官方数据库证据，也不能令一方失败后另一方成为未报告的跳过。外来模型测试两个方向，分别计数连接和命令，确认在任何 Open/Execute 前拒绝。两种驱动使用各自已初始化的模型，不能让正例本身跨驱动共享一个模型。全部对象与上下文生命周期顺序完成，不并行启动数据库通道。

生成 DDL 前读取 `skills/dameng-sql/SKILL.md`；若采用 `GenerateCreateScript` 或迁移，另读 `skills/dameng-ef-migrations/SKILL.md`。对象名唯一，在 finally 精确清理，不靠回滚清理 DDL。

运行双驱动聚焦实库、Release W 单元和 `git diff --check`。更新两份文档，只声明已验证的入口/模型种类，并区分单元和实库证据。证据文件记录各 TRX 路径、通过数和执行 SHA；虽然两驱动由同一 W 项目命令及 TRX 承载，仍须分列官方/W 实际读写与同驱动 `UseModel` 的结果，不打印连接信息。

## 测试矩阵

| 边界 | 必须覆盖 |
| --- | --- |
| SetDbConnection | 官方→W、W→官方、包装/其他驱动拒绝；同驱动接受；拒绝前后旧引用/状态/所有权不变；null；重新设置连接串；同引用；contextOwnsConnection true/false；已开连接仍遵循 EF 原有保护 |
| 时态参数 | DateTime/DateTimeOffset/TimeOnly/TimeSpan→TIME，官方/W 双向；NULL/非NULL；Clone/精度变体；本驱动接受；官方原精度不变、W Precision=0/Scale 保持 |
| UseModel | 双向已初始化模型；无参数只读；带参数读；写；仅中立属性；同驱动接受；owned/complex；并发身份检查；上下文池；持久模型注解/迁移差异不变 |
| 数据库 | 官方、W 分别执行同驱动已初始化模型的 UseModel 无参数读、带参数读及真实时态插入/更新/读取；两方向外来模型零 Open/Execute；逐 driver 独立断言与记录；唯一对象与失败精确清理 |

## Done 与停止条件

- [x] 新类和所有固定方法存在；新增两驱动单元、双驱动实库（W 项目承载）、Release W 单元全部非零 Completed，无失败或跳过；官方与 W 的真实读写分别有断言和证据。
- [x] 两驱动全部单元通过；`git diff --check` 通过；没有更改依赖/锁/公共配置签名。
- [x] 只读 `UseModel` 在零次数据库执行前拒绝外来模型，不能由某个写参数测试代替。
- [x] `git diff --name-only` 与 `git ls-files --others --exclude-standard` 对照白名单，无本任务越界改动；保留原有未跟踪文件。
- [x] 脱敏证据与文档完成，交协调者审核 F03 是否可关闭；不自行标记整个 M3 完成。

前置门未完成、出现非前置计划造成的漂移、同驱动行为回归、需升级 EF/驱动、需要新增公开桥接 API 或修改白名单外服务时停止。数据库不可用/测试跳过应报告验证未完成，不改为只用单元证据关闭。一次合理修正后同一验证再次失败时记录失败并交回，不放宽断言。

## 维护提示

新增驱动、模型映射种类、EF 模型初始化服务变更时必须复核运行时身份检查。任何性能优化不得移除对已初始化 `UseModel` 的验证；任何共享模型的支持扩展都要同时覆盖只读和写入路径。ConnectionCreating/ConnectionCreated 拦截器主动替换连接属于后续需明确的扩展边界，本计划不以 SetDbConnection 测试宣称已拦截任意用户拦截器代码。

## 批次交付与统一证据

本计划属于 M3，启动实施前须确认 M2 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/009.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为确认问题或验证缺口，须以“已修复并验证”闭合；补足有辨别力的测试证据也属于验证缺口的修复。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M3.md` 的输入。
