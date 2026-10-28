# W.DamengProvider 接入验证记录

本文按[实施计划](w-dmprovider-integration-plan.md)任务顺序记录实际执行的验证证据。区分离线单元证据与真实服务器行为证据；未执行或缺环境的门标为 pending，不计为通过。支持范围以[兼容矩阵](compatibility.md)为准。

2026-10-16 发布准备以 `b3e8da3` 为源码基线，使用 `W.DamengProvider 0.2.0`，
重新运行完整双驱动验收与 Release/精确包消费者检查，结果统一记录在
[0.4.0 发布准备](release-0.4.0.md)。下文保留各阶段历史结果及当时的 pending 状态；
当前复审状态见[审查记录](w-dmprovider-integration-review.md)。

## T00 基线与候选冻结（2026-10-14）

### 候选身份

身份清单固定在 [`scripts/driver-candidate/w-damengprovider.manifest.json`](../scripts/driver-candidate/w-damengprovider.manifest.json)，核验脚本为 [`scripts/driver-candidate/verify-package.py`](../scripts/driver-candidate/verify-package.py)（只读文件，不加载或执行程序集）。

| 项 | 值 | 核验方式 |
| --- | --- | --- |
| 包 | `W.DamengProvider 0.1.0-preview.1`，约束 `[0.1.0-preview.1]` | 从 NuGet.org 精确版本 URL 下载 |
| 包 SHA-256 | `03e7f9161b2d120e29f8e267d25dc62cef3e4f94ba8c032cb774449dcfec07ba` | 脚本比对，与计划 §1.1 一致 |
| NuGet contentHash | `Gah+YlqywSqIhj9qvnQ2LYDADCJFdSJzYu6isg4P2XqsLARos9813Qa/D9hifhNAvvgz/f5uoyEgddLN3Ud+EQ==` | 读自还原目录 `.nupkg.metadata`；签名包的 contentHash 不含签名条目，因此 pin 在 manifest 中，字节一致性仍由包 SHA-256 保证 |
| DLL SHA-256 / MVID | `a265cd96…ab1d` / `d1773de0-4205-48eb-84a6-0708c9c4aec4` | 脚本从 PE 元数据静态读取 MVID |
| 来源 | nuspec repository commit 与 tag `v0.1.0-preview.1` 均为 `a67a9298…`；无运行时 NuGet 依赖 | 脚本读 nuspec |
| 上游漂移 | main `b5786d5`，相对 tag 的 `src`/`tests` 无差异 | `git diff --stat` |

负例：对包末尾追加 1 字节后，脚本报告 SHA-256 不符并以退出码 1 结束；T03 之前没有锁文件引用该包，脚本不因此报错；加 `--require-lock-reference` 时缺少引用即失败。T03 引入依赖后，脚本必须在全部锁文件上通过（见 T03 记录）。

### 环境

| 项 | 值 |
| --- | --- |
| 本仓库基线 | `6238792`（相对 `5863d37` 仅新增计划文档） |
| SDK / runtime | `10.0.401` / `Microsoft.NETCore.App 10.0.12` |
| 锁定包 | `Microsoft.EntityFrameworkCore.Relational 10.0.12`、`DM.DmProvider 8.3.1.47463` |
| 服务器 profile | 自报 `8.1.5.60`，`GLOBAL_CHARSET=1`（UTF-8），`COMPATIBLE_MODE=0`，页大小 32 KB；W 连接需 `transport_security=PlaintextAllowed` |
| 不具备的 profile | TLS（W 文档中的 `8.1.4.6 / GB18030 / TLS`）、其他服务器版本：相关门 pending |

### 官方基线运行

在 `6238792` 的独立 worktree 中运行 `scripts/local-test/run.sh test all`（运行 ID `20261014T072339444Z-857b6388`）：

| 通道 | 总数 | 通过 | 失败 | 跳过 | 结论 |
| --- | --- | --- | --- | --- | --- |
| unit | 688 | 688 | 0 | 0 | 通过 |
| functional | 233 | 232 | 1 | 0 | 本次运行未被接受（见下） |
| specification | 4 | 4 | 0 | 0 | 通过（冒烟） |
| admin | 7 | 7 | 0 | 0 | 通过 |

失败用例 `DamengReverseEngineeringFunctionalTests.LocalSequenceAfterSetSchemaRecreatesAgainstTheCatalogSchema` 在测试准备 SQL 中收到官方驱动 `DmException 6001, Communication error`，与断言无关。在同一 worktree 单独重跑（运行 ID `20261014T073151199Z-d6875dd0`）1/1 通过。记为官方驱动偶发通信错误，基线功能通道按“233 项，其中 1 项偶发”冻结；后续官方回归若再现同一错误，需单独重跑并记录，不因此修改测试。

完整用例清单（934 行）保存在 [`w-dmprovider-evidence/baseline-tests-6238792.txt`](w-dmprovider-evidence/baseline-tests-6238792.txt)，T04/T06 据此逐项映射 W 通道结果。探针通道（`probes`）不属于 `all`，也不作为基线证据。

### §2.4 / §6 API 复核

- W 时间参数合同与计划 §2.4 一致：非零 `Precision` 一律 `NotSupportedException`（INTERVAL 也适用），小数秒只看 `Scale`；显式 `DbType.DateTime2` 默认七位，显式 `DbType.DateTimeOffset` 默认六位。因此 W 分支必须对所有时间参数清零 Precision 并显式设置 Scale。
- W `GetFieldValue<DateTimeOffset>` 返回带偏移的原值，`GetFieldValue<TimeSpan>` 对 INTERVAL 调用精确转换；W 分支对这两类列使用 typed getter。
- `DmTransaction.SupportsSavepoints` 仅在服务器自报 `8.1.5.60` 时为 true；本地 profile 满足。
- §6 依赖的 `DmDataSource`（`CreateConnection`/`OpenConnectionAsync`/`ClearPool`）与 Reader 的 `GetStream`/`GetTextReader` 在 preview.1 中存在，没有新增需要改变 T08/T09 范围的设施。

## T01/T02 桥接 API 与官方路径抽象化（2026-10-14）

提交 `c0273a0`。离线证据：基础单元 734/734，其中新增 `DamengDriverSelectionTests` 46 项，用基于 SqlClient 类型的假驱动验证与 W 无关的合同：

- 驱动身份进入服务缓存相等/hash、LogFragment（`UsingDameng Driver=<Name>`）和 DebugInfo（`Dameng:Driver`），哨兵口令不进入两者；同一 DbContext 类型交替使用两种驱动、DbContext pooling 时 type mapping source 与驱动不串用。
- 同一 builder 先后选择两种驱动在两个方向上都抛 `Dameng:ConflictingDriver`；同驱动重复配置保持原语义。
- 连接入口拒绝其他驱动的连接（`Dameng:ForeignConnection`）；一个驱动的映射遇到另一驱动的参数抛 `Dameng:ForeignParameter`；未经映射源绑定的模板映射抛 `Dameng:UnboundTypeMapping`。
- 手工 `UseInternalServiceProvider`：官方服务 + 其他驱动 options、反向组合、首个上下文成功后复用同一 provider 换驱动、同一集合注册两种驱动（两种顺序）均在构建模型前失败；同驱动重复注册允许。
- 不支持重试的驱动：`EnableRetryOnFailure` 全部重载（含空集合）、先配重试后选驱动、直接构造 `DamengRetryingExecutionStrategy`（dependencies/DbContext 两种构造）均抛带 `Dameng:RetryNotSupportedFor<Name>Driver` 的 `NotSupportedException`；非法参数仍先抛标准参数异常；默认策略执行次数为 1。官方 `EnableRetryOnFailure` 行为不变。
- 时间映射把有效小数秒位数交给驱动：默认 DateTime/DateTimeOffset 7、`HasPrecision(n)` 为 n、裸 `TIMESTAMP`/`DATETIME WITH TIME ZONE` 6、`DATE` 0、TimeOnly/INTERVAL 6。
- 设计时服务按驱动生成 `Use...` 调用；模型工厂 `Create(DbConnection)` 拒绝其他驱动连接。

真实库回归（官方驱动，T02 提交）：unit 734、specification 4/4、admin 7/7；functional 首轮 222/233，其中 2 项为官方驱动 `6001 Connection reset by peer`，9 项由基线运行中那次 6001 遗留的序列 `EF10_SQ_F280B261463A`（及其模式）造成。按唯一后缀精确清理遗留对象后单独重跑 functional 233/233（运行 ID `20261014T074834277Z-a939d559`）。T02 与 T03 之间发现功能测试项目直接构造 `DamengDatabaseModelFactory` 导致编译失败，已在同一提交中修正（构造参数改为从上下文取驱动）。

## T03 W 基础适配（2026-10-14）

提交 `c732428`。新增 `W.EntityFrameworkCore.Dameng.WDriver`，依赖 `W.DamengProvider [0.1.0-preview.1]`；新锁文件 contentHash 与 manifest 一致（`verify-package.py --require-lock-reference` 全部通过）。W 单元 43/43：

- 六个 `UseDamengWithWDriver` 重载都选择 W；`Database.ProviderName` 为 `W.EntityFrameworkCore.Dameng.WDriver`，并能由该名称加载程序集找到 W 设计时服务；脚手架生成 `UseDamengWithWDriver`。
- 官方驱动、W、SqlServer 三者同进程共存；两驱动对同一模型生成的查询 SQL 与 `GenerateCreateScript` 完全相同，参数为 `:name`。
- 参数：CLOB/BLOB/VARBINARY/BINARY/INTERVAL 设置 W `DmSqlType`；LOB NULL 与大值 Size=-1；JSON 参数最终绑定为 `DbType.String`/`DmDbType.VarChar`，与官方一致，Clone 后不变；decimal 的 Precision/Scale 不变。
- 时间参数（值、NULL、重复创建、Clone）一律 `Precision=0`，`Scale` 为映射的有效位数；DateTimeOffset/INTERVAL 读取使用 `GetFieldValue<T>`。
- 测试进程实际加载的 `W.DamengProvider.dll` SHA-256/MVID 与 manifest 一致，且未加载旧 `W.DmProvider`。

## T04 测试通道与身份审计（2026-10-14）

启动器新增 `--driver w` 与 `--w-transport-security`（见[本地测试说明](../scripts/local-test/README.md)）。W 功能/规范项目链接官方测试源码并定义 `WDRIVER`；共享源码改为 `global using` + `UseDamengUnderTest`。按驱动区分的断言：

| 测试 | 官方 | W |
| --- | --- | --- |
| `DamengIsolationLevelFunctionalTests` RU/Serializable 跟踪保存 | `SaveChangesFailsAfterBeginningReadUncommittedOrSerializable`（`CommandText has no value`） | `SaveChangesSucceedsAfterBeginningReadUncommittedOrSerializable`：更新提交、插入回滚、最终状态核对（仅 8.1.5.60 profile；其他 profile 的发送前拒绝 pending） |
| `DateTimeOffsetRawAdoReadbackPreservesServerEvidence` | 原始 `GetValue` 为 `DateTime` | 原始 `GetValue` 为带 `+08:00` 偏移的 `DateTimeOffset`，七位 tick 一致 |
| `DamengDotNetEfCliFunctionalTests` | 引用基础 DLL、`UseDameng`、scaffold 提供程序名为基础包 | 另引用 WDriver DLL 与 `W.DamengProvider`，生成/断言 `UseDamengWithWDriver`，scaffold 用 W 提供程序名；嵌套项目输出目录的 W DLL SHA-256 与运行时 MVID 与 manifest 一致，且不含旧 `W.DmProvider` |

测试基础设施修正（两驱动共用）：管理员脚本夹具派生临时用户连接时先移除再设置 `UID`/`Pwd`（W builder 拒绝以不同值覆盖同义键）；反向工程测试的 `finally` 清理改为 `CleanupAsync`：当前会话被传输层重置时，在新连接上对同一唯一名称对象重试至多 3 次，服务器报告的错误仍直接失败。

验收运行（同一工作树、顺序执行）：

| 通道 | 结果 | 运行 ID |
| --- | --- | --- |
| 官方 unit / functional / specification / admin | 734 / 233 / 4 / 7，全部通过 | `20261014T080739221Z-eb292b02` |
| W unit | 43/43 | `20261014T080603467Z-e003b86b` |
| W specification | 4/4 | `20261014T080609995Z-0149faa4` |
| W admin | 7/7 | `20261014T080654475Z-2efa4647` |
| W functional | 233/233 | `20261014T081026131Z-53c59781` |

此前的 W functional 运行（140/233 → 225/233 → 229/233）失败原因依次为：一次遗留序列引起的级联、2 项真实驱动差异（已改为按驱动断言）、以及多次 `Connection reset by peer`/连接期限到期。

**环境说明**：本地服务器位于经 WSL2 网络访问的局域网地址，当天两种驱动都间歇出现传输层连接重置（官方 `6001`、W `SocketException`/`DmTimeoutException`），服务器会话数正常（8/1200）。传输层失败不计为通过；验收以完整的干净运行为准，失败运行的 TRX 保留在 `artifacts/query-translation/local-test/`（Git 忽略）。两种驱动的数据库通道共享持久测试模式，必须顺序运行。

## T05 异步、取消、故障与会话恢复（2026-10-14）

W 专属回归 `WDriverRuntimeFunctionalTests`（36 项，只在 W 功能项目中编译）。单独运行 35/36，唯一失败为准备阶段的 `Connection reset by peer`；在全量 W 功能运行中同样通过（见 T06）。经实库验证的行为：

- **异步**：服务器端延迟 2 秒的查询（`DBMS_LOCK.SLEEP` 函数）与带延迟触发器的 `SaveChangesAsync`，调用在 1 秒内返回未完成的 Task，结束后结果正确。普通标量路径以外（LOB 同步 getter）不在此结论内。
- **取消/期限**：预取消 token 不发送且上下文可继续使用；运行中取消保留原 token，抛 `DmOperationCanceledException`（`CancelSource=User`、`ConnectionReusable=false`），下一次操作使用新会话（`SESSID()` 变化）；EF `CommandTimeout(1)` 抛 `DmTimeoutException`（`ErrorKind=Timeout`、`CancelSource=TotalDeadline`、`Number=0`）；旧 token 不影响新调用。事务内取消写入：`OperationOutcome=Unknown`、提交失败，独立连接核对最终状态为未写入。
- **有条件同会话恢复**：唯一键 -6602、外键 -6607、NOT NULL -6609、触发器 RAISE -20071、序列耗尽 -7069、零参数未绑定标记 -6804，各在 sync/async × autocommit/事务 共 24 种组合下：EF 异常链中的 W 异常带服务器错误号、`ErrorKind=Server`、`OperationOutcome=ServerReported`、`ConnectionReusable=true`；同一 `SESSID()`；失败语句无部分效果；事务内先前写入在提交后保留；EF 不重试。
- **准备阶段失败**（语法错误）使 W 会话进入 Broken；EF 下一次操作打开新会话。这是 W 的保守合同，官方驱动保持会话。
- **事务内匿名块**：W 在本地事务内拒绝动态控制 SQL（`BEGIN … END;`），在发送前抛 `NotSupportedException`，会话与事务保持可用；事务外同一块正常执行。依赖事务内 PL/SQL 块的应用代码需改写。迁移中的块已由提供程序抑制事务，不受影响（T06 守卫用例验证）。
- **保存点**：事务内 `SaveChanges` 失败后 EF 自动回滚到保存点，事务继续；显式 `CreateSavepoint`/`RollbackToSavepoint` 生效。
- **严格绑定**：有参数但 SQL 未使用时 W 拒绝且无写入；`ExecuteSqlRaw` 位置参数正常绑定。
- **诊断**：`W.DmProvider` ActivitySource 在 EF 读写期间产生活动，标签/名称/事件中不含连接串、主机、用户、口令、表名、参数值或 SQL 文本。

pending：commit 失 ACK、写入发送后断线等需故障代理的端到端用例（驱动侧有上游证据，EF 端到端未验证）；非 8.1.5.60 profile 的 RU/Serializable 与恢复拒绝；`-3510` 负例（本环境未能构造）。

## T06 时间精度、迁移与驱动切换（2026-10-14）

W 专属 `WDriverTemporalAndMigrationFunctionalTests`（15 项）单独运行 15/15（运行 ID `20261014T082326165Z-ec750fa7`）：

- DateTime `TIMESTAMP(7)`：`MaxValue`、`MaxValue-1tick`、相邻整微秒/非整微秒值、`MinValue+1tick` 逐 tick 往返；参数化相等/大小比较能区分相邻 tick；服务器 `TO_CHAR(…FF7)` 文本一致。
- DateTimeOffset `DATETIME(7) WITH TIME ZONE`：ticks、原始偏移（+08:00、-05:30、+13:59、+00:00）与 UtcTicks 保持。
- `HasPrecision(6)` 列写入非整微秒值在写入前被拒绝且无行写入；整微秒值成功。
- `TIMESTAMP(9)`：整 tick 值可读；非整 100ns 值读取抛 `OverflowException`；W 拒绝任何 8/9 位写参数（含 NULL），EF 侧不放开。注意：服务器把无精度的 `TIMESTAMP '…'` 字面量按六位解析，构造多位数据需用 `TO_TIMESTAMP(…FF9)`。
- 迁移：`TIMESTAMP(6)→(7)` 保留旧值；七位→六位时守卫块在任何 ALTER 前检查所有待降列，存在非整微秒值时以 -20071 拒绝且数据与列精度不变；清理数据后守卫通过并完成降精度。生成的 DDL/守卫命令均为 `TransactionSuppressed`。
- 驱动切换：官方驱动应用迁移 1 → W 读取历史并应用 2、3 → 官方回滚 3 再重新应用 → W 回滚全部；使用 `ActiveProvider` 分支的迁移在两种驱动下分别走对应分支，说明旧迁移若只比较旧名称需显式接受两个名称。离线单元证明两驱动的设计时模型互相 diff 无任何迁移操作。

全量 W 功能（284 项 = 共享 233 + T05 36 + T06 15）：283/284，唯一失败为 `Connection reset by peer`（运行 ID `20261014T082855838Z-27148e00`）。为避免进程内服务提供程序数量告警误报，共享测试助手把 EF `ManyServiceProvidersCreatedWarning` 改为记录日志（测试代码有意创建多种模型变体）。最终干净运行见 T07。

## T07 M1 开发候选（2026-10-14）

- 版本：基础包 `W.EntityFrameworkCore.Dameng` 0.4.0（新增公开驱动桥接 API，连接类型校验收紧）；扩展包
  `W.EntityFrameworkCore.Dameng.WDriver` 0.4.0-preview.1（依赖预览版驱动，按 NU5104 必须为预发布版）。
- 扩展包 nuspec：`W.EntityFrameworkCore.Dameng [0.4.0]`（精确配对，由 `PinBasePackageVersion` 目标固定）、
  `W.DamengProvider [0.1.0-preview.1]`。
- 本地打包（Release，最终树，未发布）：基础包 SHA-256 `8b2f02b7…3289`，扩展包 SHA-256 `eec7f6ad…5c75`。nupkg 是构建产物，不提交。
- 精确包消费者矩阵（独立 `NUGET_PACKAGES`、显式 `NuGet.config` 把两个候选包映射到本地目录，`TreatWarningsAsErrors`）：

| 消费者 | 结果 |
| --- | --- |
| 仅基础包 | 构建无警告；`ProviderName=W.EntityFrameworkCore.Dameng`，连接 `Dm.DmConnection`，未加载 `W.DamengProvider` |
| 仅扩展包 | 构建无警告；`ProviderName=W.EntityFrameworkCore.Dameng.WDriver`，连接 `W.Dm.DmConnection`，加载 MVID 与 manifest 一致；实库查询返回 1 |
| 两包 + SqlServer | 构建无警告；三个提供程序名称各自正确，扩展方法无歧义 |

- 发布工作流：`v<SemVer>`（`publish-nuget.yml`）只发布基础包，并新增 PackageId/版本与标签一致的校验；
  `wdriver-v<SemVer>`（新 `publish-nuget-wdriver.yml`，不匹配 `v*`）只打包扩展包，核验 W 包身份、运行两组单元测试，
  校验恰好一个同名同版本 nupkg，并要求其精确依赖的基础包版本已在 NuGet.org 可取得后才推送。本地已用实际 nupkg
  验证 nuspec 解析；工作流本身未触发，未创建任何标签或发布。
- 文档：README、兼容矩阵（新增 W 小节与连接类型兼容性变化）、架构、迁移说明（W 设计时入口、`ActiveProvider`、七位→六位守卫）、
  AGENTS、本地测试说明。

### 最终验收（同一工作树、顺序执行）

| 通道 | 官方 | W |
| --- | --- | --- |
| unit | 734/734 | 44/44 |
| functional | 233/233 | 284/284 |
| specification | 4/4 | 4/4 |
| admin | 7/7 | 7/7 |
| 运行 ID | `20261014T121220978Z-a4f897fa` | `20261014T121451942Z-66a9e53f` |

两次运行均满足宿主 Completed、退出 0、Total=Executed=Passed、无失败或跳过；`dotnet format --verify-no-changes`
与 `--locked-mode` 还原通过。

### 仍为 pending

- 用户指定的独立复审（计划 §8，针对 M1 代码 diff）。
- TLS profile、非 8.1.5.60 服务器的拒绝合同；commit 失 ACK / 发送后断线的 EF 端到端故障注入；`-3510` 负例。
- 公开发布：需要发布授权、按先基础包后扩展包的顺序打标签。
- M2（数据源、流式 LOB）、T11（DmDecimal）。

## PR #7 修复与 main 合并回归（2026-10-14）

本轮把 `main` 的 `1b9a85a` 合入候选，处置 Codex 的 TIME 参数桥接 P2 与 Cursor 的 visitor 注册 P1。
共享桥接注册保留驱动专用 DatabaseProvider，同时加入 DateTime 翻译 visitor。
`TimeSpan` 显式 TIME 映射通过 TimeOnly 转换器读写，克隆保留驱动、NULL 参数保留精度配置；W 只用 Scale。
转换器拒绝负值与大于等于一天的值，避免把有符号间隔或天数静默变成时刻。
显式裸 TIME 按服务器和[达梦官方文档](https://eco.dameng.com/document/dm/zh-cn/sql-dev/dmpl-sql-datatype.html)的缺省 0 位处理，
TimeOnly 同步修正；默认 TimeOnly 的 `TIME(6)` 与默认 TimeSpan 的 INTERVAL 映射保持原精度。
参数与字面量对不可精确表示的小数秒均在发送前拒绝。
用户指定由 GitHub Codex / Cursor 进行修复后的独立复审，详见[审查记录](w-dmprovider-integration-review.md#pr-7-代码独立复审2026-10-14)。

### 失败运行与处置

以下失败均保留在已忽略的本地证据目录，不能作为验收通过：

- `20261014T141045318Z-87f0e38f`：W 单元 49/50。新增测试把非空列转换成 nullable 后期望 COALESCE，EF 已知列非空而省略该表达式；改用真正可空的 DateTime 属性，随后 50/50 通过（`20261014T141125049Z-3ae565f4`）。
- 初次官方功能构建失败：main 新测试直接构造模型工厂时未传入 DamengDriver；更新为当前通道的服务。没有 TRX，不计运行通过。
- `20261014T141151894Z-bc8b8a72`：官方功能 294/299。四项 TIME 用例暴露官方 GetFieldValue&lt;TimeSpan&gt; 无法读取 DateTime，另一项目录计数测试的包装连接被生产校验拒绝。增加 TIME 读取适配，计数测试采用只识别 CatalogQueryRecorder 的测试驱动；生产校验保持拒绝包装连接。
- `20261014T141510397Z-e4efb1db`：官方 unit 789/789、functional 298/299、specification 4/4、admin 7/7；整个 all 命令退出非零。剩余失败是把裸 TIME 误按六位使用，服务器读回没有小数；核对官方契约后改成零位，并补充 TimeOnly 的裸 TIME 单元与实库回归。

- `20261014T141844018Z-5b6395fc`：W unit 58/58、functional 347/351、specification 4/4、admin 7/7；整个 all 退出非零。四项 TIME 写入收到 W 的“TimeSpan requires INTERVAL DAY TO SECOND”，因此最终改为复用 TimeOnly 映射和转换器，而不是直接发送 TimeSpan。

转换器引入时一次构建因静态字段 TimeOnly 遮蔽类型名称而失败（CS1061），改用 System.TimeOnly 限定；没有 TRX，不计通过。
修正后 W 单元 61/61 通过（`20261014T142413186Z-04901ba3`），五项 TIME 聚焦实库 5/5 通过（`20261014T142449924Z-a48b78f7`）。
后续验收只使用修复后的干净运行，不以单项重跑抵消失败的 all 通道。

### 修复后的全量验收

冻结修复源码后顺序运行两种驱动的 `all`，使用相同的持久测试模式与 8.1.5.60 / UTF-8 / 明文 profile：

| 通道 | 官方 | W |
| --- | --- | --- |
| unit | 791/791 | 61/61 |
| functional | 300/300 | 351/351 |
| specification | 4/4 | 4/4 |
| admin | 7/7 | 7/7 |
| 运行 ID | `20261014T142527110Z-e5460fd6` | `20261014T142755580Z-06cb58d1` |

两次命令均退出零，各通道宿主 Completed、Total=Executed=Passed，无失败或跳过。
TRX 位于 `artifacts/query-translation/local-test/<运行 ID>/`，W 文件带 `-w` 后缀。
规范项目仍是四项冒烟测试。另：解决方案 `--locked-mode` 还原、全解决方案构建（零警告/错误）、
`dotnet format --no-restore --verify-no-changes` 与 `git diff --check` 通过。
依赖版本、锁文件与精确包身份未因本轮修复改变。最终独立复审结果以 PR 当前提交上的 Codex / Cursor 记录为准。

## 升级到 W.DamengProvider 0.2.0（2026-10-15）

### 包身份

| 项 | 值 |
| --- | --- |
| 包 | `W.DamengProvider 0.2.0`，约束 `[0.2.0]`；[GitHub Release](https://github.com/whynpc9/W.DamengProvider/releases/tag/v0.2.0) 非预发布 |
| 包 SHA-256 | `6db5972551506f9510f563239b360afabd2b6ef31f99b678599d6e9d9733d848`，与上游[发布记录](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/release-0.2.0.md)中的 NuGet 公开签名包一致；`dotnet nuget verify --all` 为有效的 NuGet.org 仓库签名 |
| NuGet contentHash | `nBvZJyYk+CvdZq8fgZSZGfXYcxp4zEarIo1/F5ADLLlYcrDYK7E4PiF3CPWbKsGZFrdoHUNNdh6N2rbROlbVbw==` |
| net10.0 DLL SHA-256 / MVID | `d4bc3f28…1f18` / `1756fef6-b801-4182-91ab-ed3ef6945264` |
| 来源 | nuspec repository commit 与 tag `v0.2.0` 均为 `aef142f0…`；tag 发布运行 `38749310165` |
| 依赖 | 包多目标（net6/8/9/10，含 en、zh-CN、zh-HK、zh-TW 资源）；net10.0 依赖组为空，net6.0/net8.0 组的依赖不进入本仓库还原图 |

`verify-package.py` 原先要求整个 nuspec 没有依赖；多目标包的 net6.0/net8.0 组有依赖，因此改为只检查 manifest
`targetFramework`（net10.0）对应组及未分组依赖。对实际还原目录执行 `--require-lock-reference` 全部通过。
本机经 `api.nuget.org` 访问时被重定向到 `nuget.azure.cn` 镜像，当时镜像只有 `0.1.0-preview.1`；包从
`globalcdn.nuget.org` 取得，散列与签名核验后以临时本地源加 nuget.org 还原。锁文件只记录 contentHash，与来源无关。

### 行为变化与回归

- **8/9 位时间写入**：测试模型不再忽略 `TIMESTAMP(9)` 列，并新增 `TIMESTAMP(9) WITH TIME ZONE` 列；
  `NineDigitColumnsAcceptNullAndWholeTickWrites` 四组（NULL、`.1234567`、`MaxValue`、`MinValue+1tick`）先核对
  `ServerCapabilities.MaxTemporalWriteScale=9`，再异步插入、同步更新为互换后的 NULL/值，`FF9` 文本为七位加 `00`。
  `EightDigitColumnsAcceptTheProviderParameterShape` 以提供程序的 `Precision=0`、`Scale=8` 写入值和 NULL。
  原 `EightAndNineDigitWritesStayClosed` 在 0.2.0 下不再抛出（运行 `20261015T151758858Z-79136028`），已删除。
- **事务内匿名块**：`DmlOnlyAnonymousBlocksRunInsideATransaction` 在事务内执行带参数的 UPDATE + INSERT 块，
  回滚后无效果、提交后可见，`SESSID()` 不变；`ControlAnonymousBlocksAreRejectedInsideATransactionBeforeSending`
  对 `DECLARE`、`IF`、块内 `COMMIT` 仍得到发送前 `NotSupportedException`，会话与事务可继续提交，事务外 `IF` 块正常执行。
- **传输失败**：新增本地 TCP 中继（零 linger 关闭发送 RST）。连接被拒报 `ErrorKind=Transport`、`NotSent`；
  中继收到客户端的首段登录字节后再重置，报 `Unknown`（登录请求已发送，符合 W 的阶段规则）。事务内带 2 秒延迟触发器的 `SaveChanges`
  在 500 ms 时重置，sync/async 均得到 `DbUpdateException`，内部 `DmException` 为 `Transport`、`OperationOutcome=Unknown`、
  `TransactionOutcome=OutcomeUnknown`、不可复用、`Number=0`，原 `SocketException`/`IOException` 在异常链中；
  3 秒后新连接核对无提交行。首次编写时连接重置用例按 `NotSent` 断言而失败（运行 `20261015T152659410Z-5ae302cd`），
  核对 W 规则后拆为“拒绝”和“接受后重置”两种情形。
- **准备阶段恢复**：`ParameterizedPrepareFailuresKeepTheSessionAndTheTransaction` 对带参数命令的 `-2007` 与 `-2106`
  在 sync/async 事务内核对同一会话、事务继续、失败语句无效果。无参数直接执行的
  `PrepareFailureBreaksTheSessionAndEfOpensANewOne` 在 0.2.0 下仍成立，保持不变。
- **能力描述**：RU/Serializable 用例先核对 `ServerCapabilities.SupportsReadUncommitted`/`SupportsSerializable`。
- **CLI 嵌套项目**：原先把 `W.DamengProvider` 版本写死为 `[0.1.0-preview.1]`，升级后加载旧 DLL 并在 SHA-256 断言失败；
  改为读取 manifest 的 `versionConstraint`。

### 验收

| 运行 | 结果 | 运行 ID |
| --- | --- | --- |
| W all（首次） | unit 61/61、functional 364/365（CLI 嵌套项目版本写死）、specification 4/4、admin 7/7；退出 1，不计验收 | `20261015T152855256Z-19f087db` |
| W all（修复后） | unit 61/61、functional 365/365、specification 4/4、admin 7/7；退出 0 | `20261015T153238756Z-6a4f1143` |
| 官方 all | unit 791/791、functional 300/300、specification 4/4、admin 7/7；退出 0 | `20261015T153624161Z-cd632258` |

PR #8 Codex 审查（2026-10-15）指出两处中继问题并已修正：转发时 `SendAsync` 可能只发送部分字节，改为循环直到整段转发；
发送后重置原先按固定 500 ms 计时，不能证明 INSERT 已发出，改为由中继在“最近一次转发来自客户端、服务器 500 ms 内未应答”时
才重置。只影响这四项传输用例，聚焦重跑连续三次 4/4（`20261015T154646739Z-43ccf2c4`、`20261015T154700188Z-ef4d661b`、
`20261015T154713619Z-c6de8590`）。第二轮审查指出“接受后重置”只证明 TCP 握手完成，可能在登录请求发出前就重置；
中继改为先读到客户端字节（10 秒超时）再重置，用例断言读到的字节数大于零，聚焦重跑连续三次 4/4
（`20261015T155244353Z-4ebb2ed6`、`20261015T155258560Z-1ce70229`、`20261015T155312043Z-c0112254`）。上表全量运行早于这两处修正。

W 功能通道从 351 项增至 365 项。证据只来自自报 `8.1.5.60`、UTF-8、明文的本地 profile；TLS 与其他服务器版本仍未验证。
commit 失 ACK 的端到端注入、`-3510` 负例仍 pending。W 0.2.0 的 `DbBatch` 逐条往返、数组绑定只报告总影响行数且不能回读生成值，
本轮不改变单命令修改批次；数据源、流式 LOB、DmDecimal 仍未接入。

## 2026-10-28 M3 / 010 会话淘汰补强

本轮新增持有连接的正常/预取消对照，替换历史自动开闭查询间的会话ID比较。取消用例通过固定公开版本的完整Execute帧转发屏障，再确认无目标响应开始且调用待结果后取消；重开前观察Broken/Closed和不可复用FailureInfo，显式恢复后新会话。固定直接执行错误同样先持有连接再观察终态。Debug/Release四项各4/4，纯内存八方法展开11项均通过；W运行时全类48/48保留旧reset/恢复分支。见`plans/evidence/010.md`。

屏障仅表示完整Execute已转发、调用仍待结果；未观察服务器活动，不宣称服务器正在执行。仅本轮明文profile，未验证TLS或commit确认丢失；不引入自动重试。历史验证日期/范围保持。

### M3 当前验收状态

M3此前最终W真实集成失败及20分钟中止记录保持。用户授权后，在相同源码上从启动前全程防休眠重试原57项，57/57 Completed、零失败/跳过；最终版本Release四项4/4通过，末次表/序列/回收站均0，当前本地门已闭合，远端整合状态见`plans/evidence/M3.md`。此次通过不确定此前失败的根因，不新增自动重试或其他profile支持。

PR #11补强：观察器在响应已交付、完成记账尚未执行的合法交错中，异步等待上一交换记账后再转发下一请求；不暂停响应泵、不重发帧。确定性DirectExecute/PreparedExecute三例先红后绿，当前9个纯内存方法展开14例，Debug/Release均通过；57项实库及Release四项再次通过。详见`plans/evidence/M3.md`，不据此归因此前非代理连接超时。
