# 计划 018：证明并补齐反向工程普通列的目录完整性边界

> 执行者逐步实施，核对每步验证结果后再继续。此计划是调查后修复，不把合成目录异常当作真实服务器已发生故障。计划索引由协调者维护；执行者只在本文件填写状态、实际证据和结论。

## 状态

- 状态：TODO
- 里程碑：M6，目录/profile 及文档收口
- 对应问题：I04，普通列目录一致性
- 优先级：P2
- 工作量：M
- 修复风险：MED
- 类别：调查、正确性、测试
- 依赖：`plans/014-admin-ddl-recovery.md`。借用其发送前登记自有对象、结果不确定时精确核对和清理的原则；本计划始终只使用普通测试用户，不调用管理员夹具或扩大 admin 通道。
- 工具前置：M1 的 002 已加入 `--configuration Debug|Release`；004 已加入不需要数据库连接的 `offline-fixtures` 通道。这些是前置版本接口，不是本计划编写基线已有接口；若当前入口尚不接受这些选项，停止并报告依赖未满足，不自行另造测试入口。
- 编写基线：`c80c788fb3cd34d97626d578db580264a28a11f3`，2026-10-28。
- 执行分支：`codex/catalog-column-completeness`。优先使用协调者提供的隔离工作树；不自动提交、推送、开 PR、合并或发布。

**漂移规则**：`c80c788` 是原始审查锚点，不要求实施时退回该版本。实施先读取已验收前置计划的差异，更新本计划摘录定位和实际实施 SHA 后继续；可以归因的预期前置修改不触发停工。只有非预期差异、无法解释的行为变化或契约冲突需要暂停核对。

## 为什么做

反向工程承诺对选中对象保留可表示结构，遇到无法证明完整的定义应明确失败。当前普通表原生预检读取列名，但只保留虚拟列集合；构造模型时从另一套兼容目录读取普通列。若后一目录遗漏一个无约束、无索引、非 IDENTITY 的普通列，现有检查可能返回缺列模型。

这只是静态候选：尚无真实达梦目录漏行证据。第一目标是通过完整工厂路径证明候选是否可达，第二目标才是用已核实的普通用户列集合增加有限守卫。不能把普通表规则直接用于视图，也不能断言所有原生/兼容目录差异都是损坏。

## 当前实现和约束

主文件：`src/W.EntityFrameworkCore.Dameng/Scaffolding/Internal/DamengDatabaseModelFactory.cs`。

基线 877–882 行的原生查询：

```csharp
var computedColumns = new Dictionary<(string Table, string Column), string>();
using var columns = CreateCommand(connection,
    "SELECT T.NAME, C.NAME, COALESCE(I.INFO1, 0), C.INFO2, C.DEFVAL FROM SYS.SYSCOLUMNS C "
    + "INNER JOIN SYS.SYSOBJECTS T ON T.ID = C.ID "
    + "INNER JOIN SYS.SYSOBJECTS S ON S.ID = T.SCHID "
    + "LEFT JOIN SYS.SYSCOLINFOS I ON I.ID = C.ID AND I.COLID = C.COLID "
    + "WHERE S.NAME = :schema AND T.TYPE$ = 'SCHOBJ' AND T.SUBTYPE$ = 'UTAB'", tables.Keys, "T.NAME");
```

基线 889–897 行校验加密和生成标记，但只保存虚拟列：

```csharp
ValidateColumnEncryptionFlags(columnReader.GetString(0), columnReader.GetString(1), GetNullableInt64(columnReader, 3));
var flags = Convert.ToInt64(columnReader.GetValue(2), CultureInfo.InvariantCulture);
ValidateColumnGenerationFlags(columnReader.GetString(0), columnReader.GetString(1), flags);
if (IsVirtualColumnFlags(flags))
{
    var expression = GetNullableString(columnReader, 4);
    // ...校验表达式后保存...
    computedColumns.Add((columnReader.GetString(0), columnReader.GetString(1)), expression);
}
```

基线 378–383 行从 `ALL_TAB_COLS` 构造列；440–442 行的完整性校验只有：

```csharp
foreach (var (table, column) in computedColumns.Keys)
    if (!tables[table].Columns.Any(candidate => candidate.Name == column))
        throw new NotSupportedException($"Dameng table '{table}' virtual column '{column}' has missing column metadata.");
```

既有规范和范例：

- `DamengDatabaseModelFactoryTests.IncompleteConstraintAndIndexColumnsAreRejectedBeforeMutatingTheTarget`（基线 117–134 行）先检查完整性，再向目标集合添加列。新守卫沿用这类点名对象的 `NotSupportedException`，不能用日志后跳过代替。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/CatalogQueryRecorder.cs:16–25` 的测试专用 `RecordingDriver` 允许精确包装连接。合成目录夹具也通过独立测试驱动接入，不放宽生产驱动的连接类型检查。
- `DamengReverseEngineeringCompletenessTests.FilteredCatalogReadsFewerRowsAndIdentityFacetsUseOneCommand` 使用唯一模式名、`SET SCHEMA`、精确清理；新增真实用例保持同一风格。
- 数据库模型按 `StringComparer.Ordinal` 保存目录原始名称；引用、大小写、点号和转义双引号不能归一成另一列。
- 只反向工程当前模式；表过滤只限制表/视图，序列仍完整读取当前模式。空模式和只包含序列的模式是受支持路径。
- 必须先读 `skills/dameng-sql/SKILL.md` 和 `skills/dameng-ef-migrations/SKILL.md`。只使用公开目录文档、EF 契约及黑盒结果，不复制或反编译官方 EF 提供程序。

## 文件白名单

仅允许修改：

- `src/W.EntityFrameworkCore.Dameng/Scaffolding/Internal/DamengDatabaseModelFactory.cs`
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengDatabaseModelFactoryTests.cs`：仅调整与新完整性契约直接相关的既有断言。
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengCatalogColumnCompletenessTests.cs`（新建）
- `test/W.EntityFrameworkCore.Dameng.Tests/TestUtilities/DamengCatalogFixture.cs`（新建）：完整 `Create` 路径所需的合成连接、命令、reader 与测试驱动。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengCatalogColumnCompletenessFunctionalTests.cs`（新建，W 功能项目已有链接源码规则，会复用它）
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengCatalogScaffoldingHarness.cs`（仅 REFUTED 依赖下游 EF Design 保护时新建）：运行临时设计时项目的完整脚手架路径，不给仓库测试项目增加包。
- `docs/compatibility.md`：只更新目录完整性边界及证据类型。
- `docs/design-time-plan.md`：只追加本次调查结果与验证台账。
- `plans/018-catalog-column-completeness.md`：执行状态与证据。

禁止修改其他文件，特别是依赖范围/锁文件、生产 `DamengDriver` 连接识别、查询翻译、迁移 SQL 生成器、测试入口、序列范围与表过滤语义。不要读取或修改真实 secrets 内容，不创建新的持久测试空间。

## 命令与通过标准

工作目录为 `/Users/wanghongyi/Projects/dameng-entityframework-core`，若在隔离工作树执行，改为该工作树根目录。所有数据库命令顺序运行，不能将官方与 W 通道放进并行任务。

| 用途 | 命令 | 通过标准 |
| --- | --- | --- |
| 漂移检查 | `git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- src/W.EntityFrameworkCore.Dameng/Scaffolding/Internal/DamengDatabaseModelFactory.cs test/W.EntityFrameworkCore.Dameng.Tests test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengReverseEngineeringCompletenessTests.cs docs/compatibility.md docs/design-time-plan.md` | 对每个变化回读现状；非本计划前置依赖的行为变化需重新评估 |
| 新调查单元 | `scripts/local-test/run.sh test unit --configuration Debug --filter 'FullyQualifiedName~DamengCatalogColumnCompletenessTests'` | 非零测试、Completed、无失败/跳过 |
| 既有目录单元 | `scripts/local-test/run.sh test unit --configuration Debug --filter 'FullyQualifiedName~DamengDatabaseModelFactoryTests'` | 非零测试、Completed、无失败/跳过 |
| 既有序列单元 | `scripts/local-test/run.sh test unit --configuration Debug --filter 'FullyQualifiedName~DamengSequenceScaffoldingTests'` | 非零测试、Completed、无失败/跳过 |
| 官方聚焦真库 | `scripts/local-test/run.sh test functional --configuration Debug --filter 'FullyQualifiedName~DamengCatalogColumnCompletenessFunctionalTests'` | Completed；实际执行新增真库测试，无失败/跳过 |
| W 聚焦真库 | `scripts/local-test/run.sh test functional --configuration Debug --driver w --w-transport-security PlaintextAllowed --filter 'FullyQualifiedName~DamengCatalogColumnCompletenessFunctionalTests'` | 与官方分开生成 TRX；Completed，无失败/跳过 |
| 最终官方单元 | `scripts/local-test/run.sh test unit --configuration Release` | 全部通过，无失败/跳过 |
| 最终 W 单元 | `scripts/local-test/run.sh test unit --configuration Release --driver w` | 全部通过，无失败/跳过 |
| 范围与空白 | `git diff --check` 和 `git status --short` | 无空白错误；变化仅在白名单 |

表内 `PlaintextAllowed` 仅适用于已确认的明文本地 profile；使用 TLS 环境时改用已有入口支持的 `RequireTls`。不得降低环境要求以使测试通过。统一入口已设置可写 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1` 和单节点/禁用构建服务器参数；如必须额外调用 `dotnet`，先设置这些环境，并使用 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`，不清空 NuGet 全局缓存。

## 实施步骤

### 1. 检查基线与前置依赖

运行 `git status --short`、上表漂移检查，回读本文件所有摘录。前置计划的已知变化可以保留，但须在执行证据中解释对调用路径的影响。检查 014 的资源恢复验收结果与 M1 命令接口，确认没有待处理的自有临时资源。若协调者未提供分支，确认工作区无他人未提交源代码后创建 `codex/catalog-column-completeness`。

**验证**：`git branch --show-current` 返回指定分支或协调者已指定的隔离分支；`rg -n '状态|DONE|BLOCKED' plans/014-admin-ddl-recovery.md` 显示依赖完成且无未处理资源。状态文字不能替代依赖的实际证据链接。

### 2. 先在完整工厂路径复现，尚不改生产代码

在新 `DamengCatalogFixture` 中提供最小且自洽的普通表、存储索引、列、约束、序列目录结果。每条预期目录命令必须被识别；未知 SQL 直接使测试失败，不能返回空 reader 掩盖调用变化。通过测试驱动调用 `DatabaseModelFactory.Create(DbConnection, options)`，不要只测试未来新增的集合比较函数。

先添加表征用例 `MissingOrdinaryColumnBaselineCanReturnPartialModel`：原生目录包含普通 `Id` 和未参与任何键/索引的 `Payload`，兼容列目录只包含 `Id`。记录是否工厂成功返回缺少 `Payload` 的模型，并通过锁定 EF 版本的公开源码检查后续 scaffolding 路径是否已有拒绝；单元项目未引用 EF Design 包，不为此增加包或更改锁文件。完整目录、缺虚拟列、缺约束引用列作为对照。

**验证**：运行“新调查单元”命令。候选可达时，基线表征用例通过并明确断言缺列。随后选择下面唯一适用的验收分支并记录依据；不能只因一次异常或没有真实环境判为否证。

- **CONFIRMED**：完整路径确实能返回缺列结果且下游没有可靠保护。继续步骤 3 的产品守卫，最终用拒绝回归替换临时缺列表征。
- **REFUTED**：当前实现或锁定上游已有可靠保护。产品代码保持不变；测试必须固定实际保护层、确切异常或完整返回值及被测程序集版本，不能写“返回或抛异常都算通过”。若保护在工厂内，完整 `Create` 用例必须重现该现有拒绝；若只在 EF Design 下游，不得声称工厂本身已有守卫，按下段保留完整脚手架执行证据。两种情形均继续步骤 3 的目录规则核对、步骤 4 的双驱动真库合法对照和步骤 5 收口。

下游保护分支使用新增 `DamengCatalogScaffoldingHarness`：参照 M1 已修订的 `DamengDotNetEfCliFunctionalTests`，在临时目录创建引用锁定 EF Design 版本及当前被测提供程序程序集的最小项目；不修改仓库 `.csproj`/锁文件，不引入其他版本。让 `DamengCatalogFixture` 仅依赖 BCL/EF，断言留在测试中，便于在临时项目直接链接同一夹具源码。设计时服务中的测试 `IDatabaseModelFactory` 调用真实提供程序工厂和合成目录连接，再把实际结果交给 `IReverseEngineerScaffolder.ScaffoldModel`；不能直接构造预期 `DatabaseModel` 代替工厂。完整目录应产出包含全部列的脚手架模型，缺列目录必须在已定位层明确失败且不返回部分生成文件。仅记录层级、异常类别和生成列清单，不输出源代码全文；合成连接没有真实凭据。临时项目继承 002 的当前配置/程序集身份验证及单节点构建参数，finally 删除自身目录。没有可运行的完整下游证据时保持未验证，源码推断不足以关闭候选。

### 3. 定义普通用户列集合，再增加有限守卫

先阅读公开目录字段定义，并在既有普通测试账户中只读比较**自有新建普通表**的原生和兼容列结果。确定系统内部列、隐藏列、虚拟列的识别规则；已有隐藏/加密/不支持对象拒绝不能因此放宽。不要把 `SYSCOLUMNS` 的全部行直接当作需要输出的用户列，也不要修改系统目录来制造缺行。

仅 **CONFIRMED** 分支令原生预检返回具名的内部结果，同时包含已验证的普通用户列集合与虚拟列表达式。`LoadColumns` 完成后、返回模型前核对集合，按对象列出缺失/不一致信息，拒绝部分模型。使用 Ordinal 集合避免宽表重复扫描；复用当前已执行查询，不为每个列新增往返。视图不经过普通表原生预检，继续按其投影契约处理。已过滤对象不参与校验；无表模式仍返回合法序列模型。**REFUTED** 分支只固定现有保护的测试和目录规则，不新增集合守卫或提前拒绝。

**验证**：CONFIRMED 把基线表征改为 `MissingOrdinaryColumnIsRejectedBeforeReturningModel`，断言返回模型前点名表/列拒绝；REFUTED 使用下节对应的固定方法，按调查确定的现有层级和返回值断言，不强迫工厂新增拒绝。运行新调查单元、既有目录单元和既有序列单元命令；若保护在下游，另由步骤 4 的功能类过滤执行完整脚手架方法。完整目录和受支持视图均成功。无法区分合法目录差异时 STOP，不靠扩大拒绝范围通过测试。

### 4. 补真库对照和双驱动回归

新功能类用唯一 `EF10_CC_` 前缀创建普通表、只投影部分列的视图、同一模式中被表过滤排除的对象；另在该普通用户自行创建并拥有的独立模式验证无表和只有独立序列的路径。仅使用普通测试用户既有 `CREATE SCHEMA` 等权限；权限不足记 BLOCKED，不调用管理员夹具、不授予权限、不扩展 admin 类过滤，也不提升整个功能通道。

每个用例在 `finally` 清理已计划创建的精确对象；DDL 不能靠回滚清理。源表列清单、原生目录、兼容目录与模型列清单逐项比较，并验证所有普通列可插入/读取。CONFIRMED 的合成缺列拒绝和 REFUTED 的现有保护都不代表真实目录漏行已复现；两分支的真库对照均证明正常目录和实际返回值未被误读。下游保护分支在同一功能类内额外运行完整脚手架合成用例，和服务器合法目录对照分别记录，不能互相替代。

**验证**：先“官方聚焦真库”，结束后再“W 聚焦真库”。TRX 各自非零、Completed、无失败/跳过；记录版本、页大小、目录字段形态及驱动名称，但不记录主机、用户、口令或连接串。无数据库或权限不足时保留 BLOCKED/未验证，不能以合成用例替代真库。

### 5. 收口证据和文档

更新兼容性矩阵和设计时台账，分别陈述合成异常拒绝或现有保护、真实合法目录对照。保留视图、当前模式、表过滤、全模式序列的原边界。REFUTED 写清保护层、完整路径的具体返回/拒绝、版本和未改产品代码的理由；不得把“下游阻止生成”写成“工厂拒绝缺列”。证据没有覆盖全部候选时保持未验证。

**验证**：运行两个 Release 单元命令，再运行 `git diff --check`、`git status --short`。把新测试名称、总数、运行 ID、TRX 路径与结论填入本文件，交协调者更新索引。

## 测试清单

新单元类 `DamengCatalogColumnCompletenessTests` 使用 `[Fact]`/`[Theory]` 与 `[Trait("Category", "CatalogCompleteness")]`。先保留以下**两分支共用**方法：

- `CompleteOrdinaryCatalogProducesEveryColumn`：无约束列、可空列、带引用名称与宽表字段完整保留。
- `ExcludedTableDoesNotParticipateInCompletenessCheck`：过滤排除对象的合成不一致不影响选中表。
- `ViewProjectionDoesNotRequireNativeOrdinaryTableColumns`：视图投影不套普通表集合。
- `EmptySchemaAndSequenceOnlySchemaRemainSupported`：零表及只有序列均通过。
- `KnownInternalColumnDifferenceUsesDocumentedRule`：仅在步骤 3 确认存在合法差异后添加对应数据；禁止发明字段规则。

**CONFIRMED 固定方法**（替换临时基线表征）：

- `MissingOrdinaryColumnIsRejectedBeforeReturningModel`
- `EmptyCompatibilityColumnSetIsRejectedForSelectedOrdinaryTable`

**REFUTED 固定方法**：

- `MissingOrdinaryColumnMatchesExistingFactoryProtectionBoundary`
- `EmptyCompatibilityColumnSetMatchesExistingFactoryProtectionBoundary`

REFUTED 两方法必须按已调查结论固定精确异常或返回列清单；若工厂仍返回中间结果，则明确断言这个中间边界，并由下游功能方法证明最终不产出部分模型，不能将中间结果单独当作否证。只保留所选分支的方法，不添加互斥测试后用 Skip/运行时分支隐藏不适用断言。

新真库类 `DamengCatalogColumnCompletenessFunctionalTests` 使用 `[DamengFact]`/`[DamengTheory]` 与同一 Trait：

- `OrdinaryColumnsAndViewProjectionRemainComplete`
- `SelectedTablesIgnoreUnselectedCatalogObjects`
- `EmptyAndSequenceOnlyOwnedSchemaRemainSupported`

若 REFUTED 的保护位于 EF Design 下游，功能类另有固定方法 `ExistingScaffoldingProtectionRejectsMissingOrdinaryColumn`、`ExistingScaffoldingProtectionRejectsEmptyCompatibilityColumns`；两者均通过临时 harness 执行完整工厂→脚手架链路，并带有完整目录正例。若保护已经在工厂内，单元完整入口即为该层证据，不强制增加没有用途的下游 harness。

类名过滤已列于命令表。W 功能项目链接此目录源码，不复制一份不同断言的 W 类。若未来文件链接规则已变化，应先报告漂移。

## 完成条件

- [ ] 已用完整工厂入口完成可达性判断并明确选择 CONFIRMED 或 REFUTED；没有仅用手工列表比较冒充复现。
- [ ] CONFIRMED：共用方法及两项新增拒绝固定方法通过，缺列模型在工厂返回前被拒绝；REFUTED：共用方法及两项现有边界固定方法通过，记录精确保护层/返回值/版本；若依赖下游，其两个完整脚手架方法也通过且没有部分生成文件。只要求所选分支的固定方法，不强迫否证分支修改产品。
- [ ] 官方和 W 真库对照顺序完成，Completed、非零执行、无失败/跳过；未宣称真实目录损坏已被复现。
- [ ] 无表、序列、视图、过滤、名称语义无回归。
- [ ] 两组 Release 单元通过；`git diff --check` 无错误；白名单外无变化。
- [ ] 文档区分单元与真库证据，本计划填写运行 ID 与结论，协调者收到索引状态更新请求。

## STOP 条件

- 基线摘录已被非前置工作改变，导致候选或修复边界不同。
- 不能证明原生目录差异是用户列缺失；需要猜测系统列或视图规则。
- 需要修改系统目录、全局参数、持久测试空间或放宽驱动连接类型检查。
- 需要触碰白名单外文件，或实际需要改变反向工程已确定的范围。
- 同一验证在合理修正后连续失败两次；数据库缺失、清理失败或权限不满足。

## 维护说明与执行记录

目录查询新增字段或支持新的表类型时，要同步复查普通用户列集合的定义，尤其是隐藏列、虚拟列和系统内部列。新增一个目录 query 必须更新合成完整工厂夹具，未知 query 不能静默返回空集。

执行记录：待填写调查结论、实际 SHA、测试命令/数量/运行 ID、TRX 路径、服务器非敏感 profile、已清理对象证据及剩余限制。

## 批次交付与统一证据

本计划属于 M6，启动实施前须确认 M5 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/018.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、所选确认/否证分支、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为调查：确认后修复并验证，或以充分可重复证据确证无缺陷，按正文分支验收。缺环境、跳过、单次未复现均不能闭合，保持 BLOCKED 并记录解除条件。完成后向协调者提交证据，作为本批 `plans/evidence/M6.md` 的输入；020 依赖本项的正式闭合记录。
