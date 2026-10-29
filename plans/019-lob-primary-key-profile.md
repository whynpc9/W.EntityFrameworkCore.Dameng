# 计划 019：在独立聚集主键 profile 验证并固定 LOB 表的主键生成策略

> 此计划先调查，再按证据修复。离线 SQL 断言不能替代 `PK_WITH_CLUSTER=1` 的真实验收；没有独立合适 profile 时保留 BLOCKED/未验证。不得修改共享实例任何全局参数。索引由协调者维护，执行者只填写本文件状态和证据。

## 状态

- 状态：IN_PROGRESS（REFUTED正式矩阵已闭合，最终集成与复核中）
- 里程碑：M6，目录/profile 及文档收口；独立 profile 真库结果是 M6 硬门。
- 对应问题：I05，LOB 与 `PK_WITH_CLUSTER`
- 优先级：P2
- 工作量：M
- 修复风险：MED
- 类别：调查、迁移、正确性、测试
- 依赖：`plans/012-shared-column-generation.md`、`plans/015-comment-only-migrations.md`、`plans/014-admin-ddl-recovery.md`。分别先解决共享表/列的元数据一致性、列修改判定和结果不确定的资源清理，避免覆盖这些工作。
- 工具前置：M1 的 002 已提供 `--configuration Debug|Release`，004 已提供 `offline-fixtures`。它们是前置版本接口；本计划另外增加最小的显式 `profile` 测试通道。
- 编写基线：`c80c788fb3cd34d97626d578db580264a28a11f3`，2026-10-28。
- 执行分支：`codex/lob-primary-key-profile`。使用协调者提供的隔离工作树，不自动提交、推送、开 PR、合并或发布。

**漂移规则**：`c80c788` 是原始审查锚点，不要求实施时退回该版本。实施先读取已验收前置计划的差异，更新本计划摘录定位和实际实施 SHA 后继续；可以归因的预期前置修改不触发停工。只有非预期差异、无法解释的行为变化或契约冲突需要暂停核对。

## 为什么做

普通手写模型上的无注解主键目前生成 `PRIMARY KEY`，实际聚集方式由服务器 `PK_WITH_CLUSTER` 决定。模型校验防止 LOB 属性本身成为键或普通索引，但没有证明“普通整数键加其他 LOB 列”在默认聚集主键 profile 下安全。仓库 SQL 技能要求含 LOB 的表显式使用非聚集主键，现有 `PK_WITH_CLUSTER=0` 验证不能外推到值为 1 的实例。

需要用真实模型和迁移操作确认是否已有上游保护；候选成立时，使已知含 LOB 的新表主键行为不依赖实例缺省，同时保留显式与反向工程注解的含义。现有表的聚集主键变更可能需要重建，本计划不自动重建或改变历史数据布局。

## 当前实现

`src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs:165–174`：

```csharp
protected override void PrimaryKeyConstraint(
    AddPrimaryKeyOperation operation,
    IModel? model,
    MigrationCommandListBuilder builder)
{
    if (operation[DamengAnnotationNames.IsClustered] is not { } clustering)
    {
        base.PrimaryKeyConstraint(operation, model, builder);
        return;
    }
```

同文件 188–190 行在有注解时已正确输出：

```csharp
builder.Append(clustered ? "CLUSTER PRIMARY KEY (" : "NOT CLUSTER PRIMARY KEY (")
    .Append(ColumnList(operation.Columns))
    .Append(")");
```

`src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs:31–39` 只对键/索引属性做 LOB 校验：

```csharp
foreach (var property in model.GetEntityTypes()
             .SelectMany(entityType => entityType.GetDeclaredProperties()))
{
    var isKey = property.IsKey();
    var isIndex = property.GetContainingIndexes().Any();
    if (!isKey && !isIndex)
    {
        continue;
    }
```

`src/W.EntityFrameworkCore.Dameng/Metadata/Internal/DamengRelationalAnnotationProvider.cs:59–65` 当前仅转发主键的显式 `Dameng:IsClustered` 注解，没有从表列推导缺省。012 会改动附近共享元数据处理，必须合并其结果再实施本计划。

范例与既定边界：

- `DamengMigrationsSqlGeneratorTests.PrimaryKeyClusteringAnnotationIsExplicitInSql`（基线 167–180 行）验证 true/false；`InvalidPrimaryKeyClusteringAnnotationIsRejected` 验证非布尔注解拒绝。保留这些行为。
- `DamengModelValidator.IsLobStoreType`（基线 319–329 行）已有文本/二进制 LOB 名称集合。复用或窄范围提取该分类，不能只依据 CLR `string`/`byte[]`，也不能把全部这两类属性都当 LOB。
- `skills/dameng-sql/SKILL.md:231–241` 要求含 CLOB/BLOB 的表显式 `NOT CLUSTER PRIMARY KEY`；先读整个 SQL 技能及 `skills/dameng-ef-migrations/SKILL.md`。SQL 标识符逐段引用，参数用 `:name`；DDL 禁止事务且不能依赖回滚清理。
- 反向工程保留的 `Dameng:IsClustered=true/false` 是源数据库定义。冲突应明确报告，不能为跑通自动把 true 改 false。
- 两种驱动共享 SQL/模型，全部驱动行为经 `DamengDriver`；不新增驱动 ADO 类型判断，不改 EF/驱动依赖与锁文件。
- 真实回归不允许改变 `PK_WITH_CLUSTER`、`COMPATIBLE_MODE`、页大小或其他实例/会话设置来制造通过条件。只使用事先存在、与共享测试环境隔离的合适 profile。

## 文件白名单

仅允许修改：

- `src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs`
- `src/W.EntityFrameworkCore.Dameng/Metadata/Internal/DamengRelationalAnnotationProvider.cs`
- `src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs`
- `src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengLobTypeClassifier.cs`（仅确需共享分类时新建，internal，不改公共 API）
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengLobPrimaryKeyTests.cs`（新建）
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengMigrationsSqlGeneratorTests.cs`：仅相关既有断言。
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengModelValidatorTests.cs`：仅相关既有断言。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengLobPrimaryKeyFunctionalTests.cs`（新建，普通 profile 对照）
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengLobPrimaryKeyProfileFunctionalTests.cs`（新建，独立 `PK_WITH_CLUSTER=1` 验收）
- `scripts/local-test/TestLauncher.cs`：显式 profile 路由、普通通道排除规则和合成自检。
- `scripts/local-test/Program.cs`：只更新 usage，保持原初始化、secrets 解析与权限边界。
- `scripts/local-test/README.md`：profile 前置配置、独立用户要求、命令与证据边界。
- `docs/compatibility.md`、`docs/migrations.md`：只记录 LOB 主键策略、迁移边界和 profile 证据。
- `plans/019-lob-primary-key-profile.md`：状态与执行记录。

禁止修改 `.local-test.secrets.json`、任何真实 secret 文件、实例配置、依赖/锁文件、批次策略、查询翻译、反向工程目录查询、历史迁移文件、其他计划和索引。不得新建第二套 secret 文件解析机制；不得运行 `provision`、`grow` 或为此重新创建持久测试空间。

## 测试入口与独立 profile 合同

扩展现有入口为：

```bash
scripts/local-test/run.sh test profile --profile pk-cluster-1 --configuration Debug
```

新增通道必须满足以下全部条件：

1. 仅接受显式 `--profile pk-cluster-1`；其他名字、重复参数、在其他通道带 `--profile` 均拒绝。
2. 必须已有非空 `DAMENG_TEST_CONNECTION_STRING` 环境覆盖，连接到独立 profile 的**普通测试用户**。缺失时在启动测试前退出非零，绝不回退 `.local-test.secrets.json` 的默认持久连接或管理员连接。
3. 连接值只经已有子进程环境和脱敏通道传递，不进入 argv、日志、TRX 自定义属性或文档。由操作者在已有安全环境中配置该变量，不在计划里粘贴值或生成带值的 shell 命令。新增非敏感 `DAMENG_TEST_PROFILE=pk-cluster-1` 只传给 profile 子进程，其他通道必须清除它。
4. 新 profile lane 只筛选 `Profile=PkWithCluster1`，同时排除管理员类和 `Category=CapabilityProbe`；用户 `--filter` 与固定过滤器相与，不能扩大权限或范围。TRX 命名为 `profile-pk-cluster-1.trx` 与 `profile-pk-cluster-1-w.trx`。
5. 普通 functional/all 排除 `Profile=PkWithCluster1`，probes、admin、offline-fixtures 也不能包含该组。`all` 不隐式加入 profile；M6 单独要求其通过。
6. profile 测试在任何 DDL 前核对标记及只读 `V$DM_INI` 的 `PK_WITH_CLUSTER` 值为 1。缺失、不可读或值不符必须失败，不能 Skip、无断言 return 或动态改参数。普通测试用户没有读取必要参数的权限时报告环境缺口，不给整个测试用户授予 DBA。

## 命令与标准

在仓库根目录或隔离工作树根目录执行。所有数据库命令串行。下表配置参数和 offline-fixtures 名称依赖 M1 前置版本，profile 命令由本计划步骤 2 实现。

| 用途 | 命令 | 通过标准 |
| --- | --- | --- |
| 新单元 | `scripts/local-test/run.sh test unit --configuration Debug --filter 'FullyQualifiedName~DamengLobPrimaryKeyTests'` | 非零测试、Completed、无失败/跳过 |
| 既有迁移单元 | `scripts/local-test/run.sh test unit --configuration Debug --filter 'FullyQualifiedName~DamengMigrationsSqlGeneratorTests'` | 全部通过，显式注解行为不退化 |
| 入口自检 | `scripts/local-test/run.sh selftest` | profile 路由、缺覆盖拒绝、过滤、环境隔离及原自检全通过，无真实连接 |
| 离线夹具 | `scripts/local-test/run.sh test offline-fixtures --configuration Debug` | 全部通过且不依赖数据库连接 |
| 普通官方对照 | `scripts/local-test/run.sh test functional --configuration Debug --filter 'FullyQualifiedName~DamengLobPrimaryKeyFunctionalTests'` | 非零执行、Completed、无失败/跳过 |
| 普通 W 对照 | `scripts/local-test/run.sh test functional --configuration Debug --driver w --w-transport-security PlaintextAllowed --filter 'FullyQualifiedName~DamengLobPrimaryKeyFunctionalTests'` | 单独 TRX，Completed，无失败/跳过 |
| 独立 profile 官方 | `scripts/local-test/run.sh test profile --profile pk-cluster-1 --configuration Debug --filter 'FullyQualifiedName~DamengLobPrimaryKeyProfileFunctionalTests'` | 先证实参数为 1，再执行建表/数据断言；Completed、非零、无失败/跳过 |
| 独立 profile W | `scripts/local-test/run.sh test profile --profile pk-cluster-1 --configuration Debug --driver w --w-transport-security PlaintextAllowed --filter 'FullyQualifiedName~DamengLobPrimaryKeyProfileFunctionalTests'` | 官方完成后运行；同一 profile 独立 TRX，全部通过 |
| Release 官方单元 | `scripts/local-test/run.sh test unit --configuration Release` | 全部通过，无失败/跳过 |
| Release W 单元 | `scripts/local-test/run.sh test unit --configuration Release --driver w` | 全部通过，无失败/跳过 |
| 范围 | `git diff --check` 和 `git status --short` | 无空白错误，变化仅白名单 |

`PlaintextAllowed` 只适用于明确使用明文的测试 profile；TLS profile 改用 `RequireTls`。本计划不建立 TLS 验收声明。入口负责可写 `DOTNET_CLI_HOME`、关闭首次体验和遥测及 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`；额外 .NET 调用也必须遵循这些条件，禁止清空全局 NuGet 缓存。

## 实施步骤

### 1. 回读依赖、确认候选可达

执行 `git status --short` 和以下漂移检查，回读摘录及三个前置计划的实际验收记录：

```bash
git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs src/W.EntityFrameworkCore.Dameng/Metadata/Internal/DamengRelationalAnnotationProvider.cs src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs scripts/local-test test/W.EntityFrameworkCore.Dameng.Tests docs/compatibility.md docs/migrations.md
```

先添加表征单元：真实 EF 手写模型包含 int 主键和无界字符串/字节数组，经 `GenerateCreateScript` 及公开模型差异生成迁移 SQL；确认是否只有普通 `PRIMARY KEY`，记录最终存储类型。补 `CLOB`、`NCLOB`、`BLOB` 和有界普通列对照，不只手工构造 `AddPrimaryKeyOperation` 来猜测主路径。检查上游是否已拒绝模型或注入注解。

**验证**：运行“新单元”命令，基线表征通过且明确记录上述结果；编译失败、任意异常或仅手工操作测试都不能算保护证据。分支应为 `codex/lob-primary-key-profile` 或协调者提供的隔离分支。此时只能记录初步判断，必须在步骤 3 的独立 PK=1 对照后选择最终分支：

- **CONFIRMED**：真实模型/迁移路径缺少保护，独立 PK=1 服务器上确实发生已确认的 LOB 聚集冲突；继续步骤 4 的定向修复。
- **REFUTED**：真实模型/迁移已有可靠保护，或在 PK=1 下原候选由已经验证的服务器行为安全处理。保留现有产品代码和 SQL，不强制生成新的 `NOT CLUSTER` 或新增拒绝；按下节 REFUTED 方法固定现有保护层、输入类型、返回值/精确异常、实际后备索引及版本。即使保护在发送前，仍须跑独立 PK=1 的服务器对照和受支持模型正例。

逐项判断无界字符串/字节数组、CLOB、NCLOB、BLOB、共享表以及手工迁移入口，不能用一种类型的否证覆盖其他类型。结论混合时记录每项分支，只修 CONFIRMED 子集；所有子项都有对应证据才能关闭计划。没有 PK=1 环境不构成 REFUTED。

### 2. 建立不能误用默认环境的 profile 通道

按上节合同修改 `TestLauncher.Run`、`SelectLanes`、固定过滤器与 `SelfTest`，保持 M1 的配置和离线通道逻辑。对真实环境的覆盖值只做存在性判断与原有解析，不输出值；不增加 secret 文件，也不修改其内容。新测试类的 `[Trait("Profile", "PkWithCluster1")]` 确保默认完整回归不误跑它。

自检用合成连接哨兵和纯路由配置验证：无覆盖、只存在默认 secrets 连接、未知 profile、profile 参数用在普通 lane 都被拒绝；正确路由不选择 admin；用户过滤不能逃出 profile；其他 lane 清除非敏感 profile 标记；官方/W 的结果路径不同。所有自检不连接数据库。

**验证**：运行 `scripts/local-test/run.sh selftest` 和 offline-fixtures 命令；均通过。用 `env -u DAMENG_TEST_CONNECTION_STRING scripts/local-test/run.sh test profile --profile pk-cluster-1 --configuration Debug` 验证退出非零且没有启动数据库测试，不输出连接值。此预期拒绝是入口验证，不是 profile 通过证据。

### 3. 用独立 profile 校准服务器行为

先由操作者确认已有独立 `PK_WITH_CLUSTER=1` profile 和普通测试用户，并在安全环境注入连接覆盖；执行者不得把共享实例参数改成 1。测试先读取版本、页大小、`PK_WITH_CLUSTER` 等必要非敏感参数，断言值为 1，再创建唯一命名的最小对照对象。

对照包括“普通键+LOB、未指定聚集方式”和显式 `NOT CLUSTER` 的同形表；核对确切成功/失败、后备索引类型及失败后无遗留对象。这是限定范围的服务器调查，不把未指定聚集方式的对照 DDL 当作推荐应用写法。不能仅捕获任意异常就认为发现冲突：确认服务器错误语义及 DDL 是否已生效。测试对象在创建前登记，`finally` 按精确对象查目录并清理，沿用 014 解决的不确定结果处理原则。

把完整模型→关系模型差异/手工操作→SQL 的结果与服务器对照逐项关联。REFUTED 若由上游拒绝保护成立，断言该层精确拒绝和零 DDL，并证明同形显式非聚集正例实际建表读写成功；若上游已有安全注解/SQL，则记录原值并实际执行其生成结果。由服务器行为成立时同样执行未经修改的生成 SQL；所有成功路径均断言建表、NULL/非空 LOB 往返、主键唯一性及实际后备索引形态符合已记录合同。只发现“未报错”而没有目录和数据断言不足以否证。调查完成后为每个输入固定唯一预期，正式测试不能在运行时接受成功和失败任一结果。

**验证**：先运行独立 profile 官方命令，再运行 W 命令。调查阶段允许保留明确的基线失败记录；正式通过用例应断言已证实的服务器行为，并且任何未知错误都失败。若对照安全地推翻候选，选择 REFUTED 并继续其验收，不把该结果作为停工失败。若 profile 不存在、参数不为 1 或无法核对，记录 BLOCKED；可以继续离线防御性工作，但不得宣布本计划或 M6 DONE。

### 4. 按结论实施定向修复或固定现有保护

以下产品策略仅用于 **CONFIRMED** 子项。以**关系表实际列映射**为依据决定 LOB，覆盖共享表映射，不能只看一个实体的声明属性。优先在设计时关系注解阶段为已知 LOB 新表的未显式主键生成 `Dameng:IsClustered=false`，使正常模型差异和脚手架代码一致；迁移生成器还需检查手工 `CreateTableOperation.Columns`，避免绕过模型路径。

处理矩阵必须明确：

- 已知含 LOB、未配置聚集：新表主键显式 `NOT CLUSTER`。
- 显式 false：原样保留。
- 显式 true 且含已确认冲突的 LOB：在模型/SQL 生成阶段明确拒绝并点名表和列；不静默改 false。
- 不含 LOB且无注解：保留当前行为，不能无依据将所有主键全局改成非聚集。
- 反向工程所得显式注解：保留其定义；冲突报告而非改写。
- 独立 AddPrimaryKey：有目标关系模型可核对列时应用同一策略；无模型且操作不包含列类型时，不凭空推断表是否有 LOB。保留现有 model-less 行为并在文档要求应用显式选定聚集方式；如果安全目标必须改变该已存在合同，STOP 交协调者决策。
- 已有显式聚集主键的表新增 LOB：不能自动重建或修改主键。若目标模型可证明冲突则明确拒绝；历史无注解主键的实际布局未知，文档要求先检查目录并制定独立迁移。

不要在生成时读取真实服务器参数，保证同一模型生成确定 SQL。复用现有 LOB 类型集合；若提取分类器，仅改三处白名单生产文件的调用，不趁机重构 Query。JSON/JSONB 或未知类型若未被本次证据覆盖，单独记录边界，不推断其聚集兼容性。

**REFUTED** 子项跳过上述产品改动，新增下节现有行为回归，按步骤 3 固定的层级/返回值验证完整模型与操作路径。包括已有拒绝时的明确错误与零 DDL、已有注解时的原值、服务器安全处理时未经改写的原 SQL；不凭新测试人为添加 `NOT CLUSTER`、新拒绝或改变显式注解。显式 false、非 LOB 和 model-less 边界仍按原合同验证。

**验证**：将步骤 1 的临时表征替换为所选分支的固定回归，运行新单元及既有迁移单元命令。CONFIRMED 断言完整链路的新策略、事务抑制和发送前冲突拒绝；REFUTED 断言已验证的现有层级与确定返回值，不强迫采用新策略。两者均验证迁移差异稳定；只改 SQL 但模型差异持续产生反复变更不能通过。

### 5. 完成真库往返及迁移边界验证

两分支均在普通 profile 和独立 PK=1 profile 运行完整模型生成、模型差异迁移与自定义 CreateTable 路径，保留同形显式非聚集表的非空/NULL LOB 插入更新读取和主键重复值拒绝正例。

- CONFIRMED：生成的 LOB 表主键后备索引为非聚集；已确认的显式冲突用命令计数证明未发 DDL，不能只检查异常类型。
- REFUTED：对原候选按已记录合同验证。现有客户端拒绝必须记录完整路径的精确层级和零 DDL，再完成受支持模型正例；客户端已有安全注解/SQL 或服务器安全处理原生成 SQL 时，原样执行其结果并核对真实索引类型和数据，不强制加入新注解或非聚集策略。显式聚集的既有行为按实测固定，不要求新增拒绝；服务器发生预期拒绝时必须确认无对象或按精确名称恢复清理，不能接受任意错误。

所有测试使用唯一对象名，任何失败后精确清理，不依赖 DDL 回滚。不要把 014 的管理员连接用于 profile lane，独立用户已有普通对象权限即可；权限不足报告环境缺口。

**验证**：按命令表先完成普通官方/W 对照，再在安全切换环境覆盖后顺序完成 PK=1 官方/W。两个 profile 的参数证据和四份 TRX 分开记录。不得因 PK=0 上通过就省略 PK=1；缺任何必需 profile 证据维持 BLOCKED/未验证。

### 6. 完成文档与验收

文档按实际分支说明未显式 LOB 新表行为、显式注解合同、model-less AddPrimaryKey 边界及已有表改造不自动化。REFUTED 写清现有保护层、确切输入与返回/错误、实际索引和服务器/程序集版本，不宣称已实施新策略。`docs/compatibility.md` 分开列 SQL 单元、PK=0/既有 profile 对照、PK=1 真库结果；只有实际执行过的服务器组合可称已验证。README 说明 profile 不属于 `all`，必须显式覆盖到独立普通用户，默认连接不会被采用。

**验证**：Release 官方/W 单元、自检、offline-fixtures、`git diff --check` 全部通过；检查 `git status --short` 的修改名单。向协调者提交状态及 M6 硬门结果，不能因离线部分可以形成提交而标记整体 DONE。

## 新增测试命名与覆盖

`DamengLobPrimaryKeyTests`（`[Fact]`/`[Theory]`，`[Trait("Category", "LobPrimaryKey")]`）的**两分支共用**方法：

- `ExplicitNonclusteredAnnotationIsPreserved`
- `NonLobUnannotatedPrimaryKeyKeepsExistingBehavior`
- `ModelLessAddPrimaryKeyDoesNotInventColumnMetadata`

**CONFIRMED 固定方法**：

- `HandwrittenLobModelProducesExplicitNonclusteredPrimaryKey`
- `CreateTableOperationWithLobUsesSamePrimaryKeyPolicy`
- `ExplicitClusteredLobConflictIsRejectedBeforeSql`
- `SharedTableLobColumnParticipatesInPrimaryKeyPolicy`
- `AddPrimaryKeyWithKnownModelUsesLobPolicy`
- `LobPrimaryKeyPolicyProducesStableMigrationDiff`

**REFUTED 固定方法**：

- `HandwrittenLobModelRetainsVerifiedExistingProtection`
- `CreateTableOperationRetainsVerifiedExistingProtection`
- `ExplicitClusteredLobOperationRetainsVerifiedExistingBehavior`
- `SharedTableLobColumnRetainsVerifiedExistingProtection`
- `AddPrimaryKeyRetainsVerifiedExistingProtection`
- `ExistingLobPrimaryKeyBehaviorProducesStableMigrationDiff`

REFUTED 方法名称中的保护可能是已存在的注解/拒绝或经真库确认的服务器合同；每个 Theory 输入必须写死调查得到的唯一预期及保护层，不能仅断言“没有未处理异常”。只为选定分支添加对应方法；混合结论按类型拆分明确测试数据，不用 Skip 或运行时探测选择绿灯分支。

`DamengLobPrimaryKeyFunctionalTests` 使用 `[DamengFact]`/`[DamengTheory]` 与 `Category=LobPrimaryKey`：

- 共用：`ExplicitNonclusteredServerControlRoundtrips`、`UnannotatedServerDdlMatchesRecordedProfileOutcome`。
- CONFIRMED：`GeneratedLobTablesRoundtripWithNonclusteredPrimaryKey`、`ExplicitClusteredConflictSendsNoDdl`。
- REFUTED：`ExistingGeneratedLobPathMatchesRecordedProfileContract`、`ExistingExplicitClusteredPathMatchesRecordedProfileContract`。必须执行实际完整生成/操作路径并断言已记录的保护层/返回值，受支持结果做数据与目录验证。

`DamengLobPrimaryKeyProfileFunctionalTests` 使用同样真库属性与 `[Trait("Profile", "PkWithCluster1")]`、`[Trait("Category", "LobPrimaryKey")]`。每个测试调用共同的前置核对，值不等于 1 则失败：

- 共用：`ClusteredDefaultProfileIsVerifiedBeforeDdl`、`ExplicitNonclusteredServerControlRoundtrips`、`UnannotatedServerDdlMatchesRecordedProfileOutcome`。
- CONFIRMED：`GeneratedLobTablesRoundtripWhenDefaultPrimaryKeyIsClustered`、`MigrationsPreserveNonclusteredLobPrimaryKey`、`ExplicitConflictLeavesNoObjects`。
- REFUTED：`ExistingGeneratedLobPathIsProtectedWhenDefaultPrimaryKeyIsClustered`、`ExistingMigrationPathMatchesRecordedClusteredDefaultContract`、`ExistingExplicitClusteredPathMatchesRecordedProfileContract`。真实执行原有路径，分别证明既有保护/安全结果，不要求新 SQL 或新拒绝。

不得使用 `CapabilityProbe`；“执行过候选”不是验收。两类功能测试的共用服务器对照与分支方法均必须在官方/W 各自过滤结果中出现，不能由仅参数检查的方法凑出非零结果。

W 功能项目已链接官方功能目录源文件，同一回归经 `UseDamengUnderTest` 和 `DamengTestDriver` 切换；不复制新 W 测试文件。动态表名/模型配置参照 `DamengExtendedTypeMappingFunctionalTests` 的专用模型缓存键做法，不能让不同表的模型缓存串用。

## 完成条件

- [ ] 手写模型、共享表与手工迁移操作逐项完成判断并选择 CONFIRMED/REFUTED，保护层、返回值/错误及版本有可重复证据。
- [ ] 共用固定方法和相关既有迁移/模型验证回归通过；CONFIRMED 子项的六个固定单元方法与相应功能方法通过，证明新策略及拒绝；REFUTED 子项的六个固定单元方法与相应功能方法通过，证明已有保护/服务器安全行为，产品代码不为满足测试被迫改变。
- [ ] profile 入口只接受显式独立连接覆盖；自检证明无默认/管理员回退且固定过滤不可扩大。
- [ ] 既有 profile 官方/W 对照和独立 PK=1 官方/W 验收顺序完成，四组证据分别 Completed、非零、无失败/跳过；每组都包含共用服务器对照及所选分支的完整路径方法。REFUTED 同样必须满足全部四组真实证据。
- [ ] PK=1 参数在 DDL 前真实读到；未修改任何共享或独立实例参数来通过测试；所有自有对象清理完成。
- [ ] Release 两组单元、自检、offline-fixtures、`git diff --check` 通过，变更限于白名单。
- [ ] 文档区分单元与各 profile 真库证据，索引协调者获得本计划结论和 M6 硬门结果。

**缺独立 PK=1 profile 时，本计划和该 M6 验收项必须保持 BLOCKED/未验证。** 可交付离线防御性修改供单独审查，但这不满足以上完成条件。

## STOP 条件

- 独立 profile 不存在、连接覆盖未配置、值不为 1 或必要只读参数不可见；不得改用共享默认环境冒充。
- 有人要求修改 `PK_WITH_CLUSTER` 或其他全局/会话参数、扩大管理员权限或重建持久测试空间。
- 已观测结果无法归入明确的既有保护、服务器安全处理或已确认冲突，或者目录/错误含义不明。服务器对照可靠推翻候选时按 REFUTED 分支继续，不因此 STOP；未证明的 LOB 别名/JSON 不得外推，需要超出本次候选范围时报告独立边界。
- 修复需要自动重建现有聚集表、覆盖反向工程显式定义或改变 model-less 迁移合同。
- 三项前置计划未通过、发生未解释源码漂移、需要修改白名单外文件、清理失败或合理修正后同一验证连续失败两次。

## 维护说明与执行记录

未来增加 LOB 别名、表拆分/复杂属性、迁移主键策略或服务器 profile 时，同步复查关系表级分类及冲突处理。不要把 `all` 通过写成 profile 已验证；profile 路由的固定过滤和缺覆盖拒绝必须保留自检。

执行记录：待填写实际 SHA、调查结果、普通/PK=1 的非敏感版本参数、各驱动运行 ID/TRX、对象清理结果、是否存在离线完成但真库阻塞的部分交付，以及 M6 硬门状态。

## 批次交付与统一证据

本计划属于 M6，启动实施前须确认 M5 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/019.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、所选确认/否证分支、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为调查：确认后修复并验证，或以充分可重复证据确证无缺陷，按正文分支验收。缺环境、跳过、单次未复现均不能闭合，保持 BLOCKED 并记录解除条件。完成后向协调者提交证据，作为本批 `plans/evidence/M6.md` 的输入；020 依赖本项的正式闭合记录。

2026-10-29 执行记录见 [正式证据](evidence/019.md)，实施起点为 M5 合并 `195ffe6f0f832822c0d864ff108e474acc940d57`。M6 因缺独立 PK=1 环境保持 BLOCKED。

### 2026-10-29：维护者授权的单点白名单补充

维护者明确回复“允许这一处测试夹具修正并继续”。本项最终集成所需范围仅增加
`test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverDatabaseCreatorAsyncFunctionalTests.cs`
中构造测试DbContext的一行：`UseDamengWithWDriver`改为既有统一`UseDamengUnderTest`。
该helper在W项目仍选择原W入口，原样传递relay连接及CommandTimeout配置，并仅为当前测试context
沿用ManyServiceProvidersCreatedWarning日志策略。不得据此修改产品、其他既有夹具、受测SQL、
实网/取消/超时断言或过滤条件。F12归属仍为011，本次只修复完整套件的测试夹具诊断配置。
先验证三项实际进入网络链路，再按影响刷新profile及同一最终产品revision的双驱动all；
未通过前不关闭020/M6或合并。
