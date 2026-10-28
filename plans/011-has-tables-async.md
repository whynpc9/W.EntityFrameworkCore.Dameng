# 计划 011：让 HasTables 通过 EF 关系命令执行并提供真实异步入口

> 按步骤实施和验证；本文件尚未执行。只修改白名单文件，主协调者维护索引/里程碑状态。不要自动提交、推送或创建 PR。

## 状态、依赖与漂移

- 状态：DONE（已验收，见 [执行证据](evidence/011.md)）；里程碑：M3「驱动与连接生命周期」。
- 发现：F12；优先级：P2；类别：正确性/性能；工作量：M；修复风险：LOW。
- 原始审查锚点：`c80c788fb3cd34d97626d578db580264a28a11f3`；日期：2026-10-28。
- 依赖：009 已通过驱动一致性门，010 已通过会话/完整 Execute 帧屏障及其 8 个离线自测门；M1 的 002 已增加 `--configuration Debug|Release`，004 已接入离线验收。只有字节水位或 Allocate/Prepare 屏障不满足此前置门。
- 实施先读已验收前置计划的实际差异，更新本计划证据中的摘录定位和实施 SHA，再继续。可归因的预期前置修改不触发停工；非预期差异或契约冲突才暂停核对。
- 起始命令：`git status --short`、`git rev-parse HEAD`、`git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDatabaseCreator.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengDatabaseCreatorTests.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDatabaseCreatorFunctionalTests.cs test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverDatabaseCreatorAsyncFunctionalTests.cs test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRequestBarrierRelay.cs docs/architecture.md docs/compatibility.md plans/evidence/011.md`。辅助类应由 010 新增；缺失表示依赖门尚未完成。

## 目标与当前实现

`EnsureCreatedAsync` 的关系数据库流程会调用 `HasTablesAsync`。本提供程序未覆写它，EF 10.0.12 [基类实现](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Storage/RelationalDatabaseCreator.cs#L153-L156) 检查预取消后同步调用 `HasTables`。因此 W 的真实网络异步在目录探测阶段被降为同步，而目录命令也绕过 EF CommandTimeout、命令拦截器和诊断。

`src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDatabaseCreator.cs:74-98` 的关键片段：

```csharp
var openedHere = Dependencies.Connection.DbConnection.State != System.Data.ConnectionState.Open;
// 必要时同步 Open。
using var command = Dependencies.Connection.DbConnection.CreateCommand();
command.CommandText = "SELECT COUNT(*) FROM USER_TABLES";
var result = command.ExecuteScalar();
return Convert.ToInt64(result, CultureInfo.InvariantCulture) > 0;
// finally 在 openedHere 时同步 Close。
```

这个类的 `ExistsAsync` 已调用 `OpenAsync(cancellationToken)`；不能据此推断 `HasTablesAsync` 也已经异步。当前 `Create`/`Delete` 明确拒绝物理数据库操作，必须保留。

同仓库可复用的关系命令模式在 `src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengHistoryRepository.cs:169-187`：

```csharp
Dependencies.RawSqlCommandBuilder.Build(sql)
    .ExecuteScalarAsync(CreateRelationalCommandParameters(), cancellationToken);

new RelationalCommandParameterObject(
    Dependencies.Connection,
    parameterValues: null,
    readerColumns: null,
    Dependencies.CurrentContext.Context,
    Dependencies.CommandLogger,
    CommandSource.Migrations);
```

注意：此示例的 dependencies 是 `HistoryRepositoryDependencies`。**`RelationalDatabaseCreatorDependencies` 没有 RawSqlCommandBuilder 属性**；本任务必须通过 `DamengDatabaseCreator` 构造函数单独注入 `IRawSqlCommandBuilder`，不要复制一个不存在的成员。DI 已由 `TryAddCoreServices` 提供该服务，无需新增依赖包或手工构造依赖容器。

## 必须保留的契约

- 查询仍是 `SELECT COUNT(*) FROM USER_TABLES`，返回任意表存在与否，不按当前模型过滤、不在本任务变更模式目录选择语义、不顺带改成性能优化的 EXISTS。
- 同步入口走 ExecuteScalar，异步入口走 ExecuteScalarAsync 并传递原 token；不使用 Task.Run、`.Result`、`.Wait()` 或 Task.FromResult 包装同步执行。
- 通过关系命令正确沿用 EF CommandTimeout、当前连接/事务、命令诊断及拦截器。使用 `CommandSource.Migrations` 与仓库模式管理命令保持一致。
- 连接原先打开的状态应保持；本次内部打开的连接在成功、失败、取消后按 EF 所有权规则关闭，但不擅自 Dispose 调用方连接，不清理调用方事务。
- 官方驱动本身异步同步回退仍允许，不以 W 的证据声称官方实现非阻塞。W 不添加任何自动重试；不要包裹额外 execution strategy。
- 不改变 EF/驱动版本和锁文件，不创建或删除物理数据库，不扩大管理员权限。

## 文件白名单

- `src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDatabaseCreator.cs`
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengDatabaseCreatorTests.cs`（新增）
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDatabaseCreatorFunctionalTests.cs`（新增；W 项目已链接共享源码）
- `test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverDatabaseCreatorAsyncFunctionalTests.cs`（新增）
- `test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRequestBarrierRelay.cs`（010 的内部辅助类；只允许修复或补充本计划需要的观测/释放接口，须重跑 010）
- `test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRequestBarrierRelayTests.cs`（010 的纯内存自测；仅在观察/释放接口改变时补对应断言，不接入真实网络）
- `docs/architecture.md`、`docs/compatibility.md`
- `plans/evidence/011.md`（新增）

不修改 `Exists`/`ExistsAsync` 的语义、`Create`/`Delete` 的拒绝合同、迁移 SQL、USER_TABLES 查询范围、测试启动器或项目依赖。若生产改动需要越界，先报告原因与最小扩展方案。

实施使用协调者指定的隔离工作区/分支；若协调者要求新分支，采用 `codex/011-has-tables-async`。本计划不授权自行提交、推送或创建 PR；不得重置、清理或暂存任务外改动。

## 环境与精确命令

在 `/Users/wanghongyi/Projects/dameng-entityframework-core` 执行。统一入口设置临时可写 `DOTNET_CLI_HOME`、关闭首次体验和遥测，使用锁定还原、`-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`。本地回归只能走入口；连接由忽略的 secrets/既有环境传入，不打印其内容。两个驱动数据库通道必须串行；测试内部被测连接与辅助核对连接可以并存。

新增类分别使用 `[Trait("AuditPlan", "011")]`；下表使用固定类名过滤，避免不同项目的 Trait 范围歧义。

| 用途 | 命令 | 预期 |
| --- | --- | --- |
| 离线表征 | `scripts/local-test/run.sh test unit --configuration Debug --filter 'FullyQualifiedName~DamengDatabaseCreatorTests'` | 非零 Completed，所有固定方法执行 |
| 官方共享实库 | `scripts/local-test/run.sh test functional --configuration Debug --filter 'FullyQualifiedName~DamengDatabaseCreatorFunctionalTests'` | 非零，无失败/跳过 |
| W 共享实库 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'FullyQualifiedName~DamengDatabaseCreatorFunctionalTests'` | 非零，无失败/跳过 |
| W 异步实库 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'FullyQualifiedName~WDriverDatabaseCreatorAsyncFunctionalTests'` | 非零，无失败/跳过，完整 Execute 已转发/调用待结果的证据成立 |
| 010 回归 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'AuditPlan=010'` | 010 的四个固定方法通过 |
| 010 屏障离线回归 | `scripts/local-test/run.sh test offline-fixtures --driver w --configuration Debug --filter 'FullyQualifiedName~WDriverRequestBarrierRelayTests'` | 010 的 8 个固定方法非零通过，包含 Allocate-only 负例及 Allocate 后 Execute 正例 |
| Release 新单元 | `scripts/local-test/run.sh test unit --configuration Release --filter 'FullyQualifiedName~DamengDatabaseCreatorTests'` | 非零且全通过 |
| 官方全部单元 | `scripts/local-test/run.sh test unit --configuration Debug` | 全通过 |
| W 全部单元 | `scripts/local-test/run.sh test unit --driver w --configuration Debug` | 全通过 |
| 差异检查 | `git diff --check` | 退出 0 |

所有验收要求 total > 0、executed=passed=total、failed=skipped=0、宿主 Completed；核对 TRX 固定方法名，未发现测试不是通过。运行结果是当前 SHA 的证据，不沿用审查时 791/61 的历史数目。

## 实施步骤

### 1. 用受控异步命令固定可观察合同

新增 `DamengDatabaseCreatorTests`，通过测试服务替换 `IRawSqlCommandBuilder`/返回的关系命令，以受控 TaskCompletionSource 阻塞标量完成；同步 ExecuteScalar 若在异步测试中被调用就立即失败。此测试证明入口选择、取消 token 和未完成任务传播，不用 Task.Delay 猜测非阻塞，也不冒称真实网络证据。

从真实 context 的 DI 解析 `RelationalDatabaseCreatorDependencies`，使用 `with` 替换必须的测试依赖，不手工依赖其不稳定构造函数签名。离线测试还必须把 `Dependencies.Connection` 换成纯内存的测试实现，其 DbConnection/DbCommand 不创建 socket；这样基线绕开关系命令时会命中可断言的同步调用计数或哨兵失败，而不会真的尝试连接数据库。不得依赖真实连接串或“恰好连不上”作为红灯。方法固定为：

- `HasTablesInterpretsZeroAndPositiveCounts`：0、1、大于 1 的目录结果，同步/异步一致。
- `HasTablesAsyncReturnsBeforeControlledCommandCompletes`：入口立即返回未完成任务，释放屏障后得到结果，同步命令执行次数为零。
- `HasTablesAsyncPassesCancellationAndPreservesTheOriginalToken`：预取消零执行；执行中取消传到命令，原 token 保留。
- `HasTablesUsesTheCurrentContextConnectionAndCommandDiagnostics`：命令参数对象使用当前上下文、连接和诊断服务，命令来源一致。

**验证**：离线表征命令应在基线发现预期行为失败；保存失败名称。编译失败或假命令未真正被调用不是有效红灯；若不能构造这种可观察测试，先停止报告，不退回仅搜索源码字符串的测试。

### 2. 统一关系命令并增加异步覆写

为 `DamengDatabaseCreator` 构造函数注入并保存 `IRawSqlCommandBuilder`。用一个私有 SQL 常量及一个参数对象辅助方法连接两条入口，避免两份目录 SQL/配置漂移。

- `HasTables()`：构建关系命令，调用 ExecuteScalar，按现有 invariant-culture 数值转换判断 > 0。
- `HasTablesAsync(CancellationToken)`：首先检查预取消，然后 await ExecuteScalarAsync，传原 token，使用 `ConfigureAwait(false)`。
- 让关系命令按 EF 打开计数管理连接，不保留原始 ADO CreateCommand 或重复手工开关导致的计数失衡。若仍需要手工持有，必须有测试证明每次 Open/Close 平衡且调用方打开的连接不被关闭。
- 正常结果、命令异常、取消均保留主异常，不能因为清理失败将其吞掉或替换为成功；不新增异常重试或自动重连旧事务。

**验证**：运行离线表征命令，全部固定方法通过；执行 `rg -n 'HasTablesAsync|ExecuteScalarAsync|IRawSqlCommandBuilder' src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDatabaseCreator.cs`，应找到真实异步覆写和关系命令；`git diff --check` 退出 0。源码搜索只是辅助检查，不替代行为测试。

### 3. 两驱动共享实库验证目录、超时配置、诊断与所有权

创建 `DamengDatabaseCreatorFunctionalTests`，按 `DamengProviderFunctionalTests` 使用 `UseDamengUnderTest` 和 `[DamengFact]/[DamengTheory]`。固定方法：

- `HasTablesSyncAndAsyncSeeAnExistingUnrelatedTable`
- `HasTablesFlowsCommandTimeoutAndScalarDiagnostics`
- `HasTablesPreservesCallerOwnedConnectionAndTransaction`
- `HasTablesClosesOnlyConnectionsItOpened`

使用唯一名称的自有表保证目录非空，且表不在被测 context 模型中，验证“任意表”合同；禁止清空持久测试模式来制造空库。返回 0 的逻辑由离线受控标量验证，文档不能称已经实跑空用户模式。

添加只记录必要字段的 `DbCommandInterceptor`，覆盖 ScalarExecuting/ScalarExecutingAsync 及成功/失败事件，断言目录 SQL、`CommandTimeout`、IsAsync、CommandSource 和当前事务一致；不启用敏感日志。超时配置验证应检查实际 DbCommand.CommandTimeout，不只读 options。

对传入且调用方已打开的连接，在成功/异常后保持打开且未被 Dispose；对未打开的 provider-owned 连接，完成后应关闭。用受控 interceptor 抛出一个合成命令异常检查失败路径，明确这只是所有权表征，非网络故障证据。已有事务用查询确认仍可用，最后由测试自行回滚/释放。

**验证**：依次执行官方共享实库、W 共享实库命令，两者均非零且全通过。表 DDL 审查前读 `skills/dameng-sql/SKILL.md`，对象 finally 精确删除，不使用管理员连接或物理数据库 API。

### 4. 用 W 的完整 Execute 转发、取消和期限验证异步行为

新增 `WDriverDatabaseCreatorAsyncFunctionalTests`，复用 010 的 `WDriverRequestBarrierRelay`；不复制一份故障代理。固定方法：

- `HasTablesAsyncReturnsWhileExecuteResponseIsPending`
- `HasTablesAsyncCancellationAfterExecuteSendPreservesTheToken`
- `HasTablesAsyncHonorsConfiguredCommandTimeout`

目录查询通常太快，不能靠它天然变慢来验证。测试创建唯一命名的自有休眠函数，在仅匹配本次 HasTables SQL 的标量命令拦截器中，替换为该函数的标量调用；不修改产品 SQL。拦截器在内存逐字比较替换前命令与本计划固定的 `SELECT COUNT(*) FROM USER_TABLES`，保存匹配计数/断言结果，证明拦截的是正确入口，而不是另开一条普通 EF 查询；不要把任意命令文本写进证据。休眠函数返回正数，正常结果仍对应 true；finally 精确删除函数。此手段验证同一关系命令路径的驱动行为，不声称真实 USER_TABLES 查询本身需要数秒。

**屏障合同必须完整继承 010**：按 manifest 固定公开提交 `aef142f05af67d2ce300f0a9f3a9fbd198f304cc`，[DmCommand.cs:1405–1446](https://github.com/whynpc9/W.DamengProvider/blob/aef142f05af67d2ce300f0a9f3a9fbd198f304cc/src/W.DamengProvider/PublicApi/DmCommand.cs#L1405-L1446) 证明新命令可能先 Allocate；不能把预热后的首批字节当作被测 SQL。010 的测试专用观察器按 64 字节头、LE opcode@4/bodyLength@6 分类：3=Allocate；5+header[21]=0 是 Prepare，5+1 是 DirectExecute；6/13 是 PreparedExecute。只有完整 Execute 帧（包括 bodyLength 中已有的 CRC 尾）转发到上游后才发布事件。前导 Allocate/Prepare 不满足屏障；响应按独占 exchange 配对首个非 heartbeat，不能要求响应 opcode 与请求相同，也不能漏掉 opcode=0 响应。未知/部分响应头、响应抢先或 generation 不符均不能形成待结果证据。固定头只在内存读取必要字段，body 不解析/保存，禁止报文、SQL、地址或凭据日志。无需复制观察器、修改驱动或引用其内部 API。

先显式保持连接，登录/SESSID/预热完成且处于空闲帧边界后 `ArmExecute`，不与其他命令并发。直接调用 HasTablesAsync，不以 Task.Run 包装；完整 ExecuteForwarded 事件到达后，复核当前 generation、目标响应尚未开始及返回 Task 仍未完成。正常用例接着让真实服务器返回并断言 true。此证据准确表述为“完整 Execute 已转发、调用仍待结果”；没有服务器侧活动观察，不命名或宣称“服务器正在执行”。Allocate-only 或 Prepare-only 即使等待很久也必须无法满足条件。

取消用例仅在上述 Execute 检查通过后取消 CTS，记录 ExecuteForwarded→CancelRequested→终态，断言原 token、W 用户取消/不可复用 FailureInfo、恢复前实际状态和恢复后新会话；依照 010 的已验证机制处理连接，不加自动重试。期限用例设置 EF `CommandTimeout(1)`、服务器休眠明显更长，同样先确认完整 ExecuteForwarded，再断言实际命令收到 1 秒及 W `TotalDeadline`。若期限在 Allocate/Prepare 阶段耗尽，该次测试应失败并记录阶段，不能算作 Execute 待结果超时验收。屏障期限和整体 watchdog 分离；watchdog 只防挂死，不能替代驱动超时或通过暂停响应来制造假证据。

官方驱动不运行这些“非阻塞/Execute 待结果时取消”的能力断言；步骤 3 已验证其共同配置/诊断/所有权。只允许本地明文 profile 的已知代理，不推断 TLS 支持。

**验证**：先执行 010 屏障离线回归（8 个固定方法），再执行 W 异步实库（三方法）和 010 真库回归（四方法），均非零全通过。若改动代理，额外执行 `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'FullyQualifiedName~WDriverRuntimeFunctionalTests'`，全部原有故障测试也必须通过。未通过 Allocate-only 负例及 Allocate 后 Execute 正例，不允许只凭慢命令三项关闭本计划。

### 5. 完成回归和 M3 交接

按顺序运行官方/W 全部单元、Release 新单元和差异检查。更新架构/兼容矩阵，准确写出“提供程序 HasTablesAsync 经 EF 关系命令，W 在本轮 profile 验证真正异步/取消，官方仍受驱动同步回退约束”。不要把此改动扩写成完整 EnsureCreatedAsync 或任意数据库操作的异步保证。

证据文件记录实施 SHA、固定 W 公开源 SHA、方法清单、精确命令、TRX 路径、计数和非敏感 profile，区分受控命令离线证据、普通目录真实证据与测试拦截后的慢命令真实证据。慢命令分别记录已完成 Execute 转发、调用待结果、取消/期限事件次序；不能把它升级为服务器实际执行阶段的观察。

**验证**：全部规定命令成功；`git diff --name-only` 和 `git ls-files --others --exclude-standard` 仅含本任务白名单变更及已记录的既有改动。将证据交主协调者，结合 009、010 决定 M3 是否整体关闭。

## 测试矩阵与 Done

| 维度 | 离线 | 官方实库 | W 实库 |
| --- | --- | --- | --- |
| 0/正数返回 | 同步/异步全覆盖 | 自有非模型表为 true | 同左 |
| CommandTimeout/诊断 | 关系命令参数对象 | 实际 scalar command/事件 | 同左，加真实客户端期限 |
| 所有权/事务 | 受控失败表征 | 原先开/关、调用方所有权、已有事务 | 同左，取消后不可复用另列 |
| 异步/取消 | 屏障、预取消、token；Allocate/Prepare 不能触发 Execute 的纯内存反例 | 只声明入口和驱动既定边界 | 完整 Execute 转发、任务待结果、此后取消/期限、原 token/终态 |

- [x] 四个离线固定方法、四个共享实库固定方法、三个 W 异步固定方法均存在并真实执行；每次过滤均非零。
- [x] 官方/W 共享实库、W 异步实库、010 四项回归全部 Completed，无失败/跳过。
- [x] 010 的 8 个离线屏障方法通过；三项 W 异步实库均证明完整 Execute 已转发，Allocate/Prepare 阶段终止不能假绿；文档未称服务器执行中。
- [x] 两驱动全部单元及 Release 新单元通过；`git diff --check` 通过。
- [x] 目录 SQL 范围、物理库拒绝合同和依赖锁未变；没有 Task.Run/同步阻塞伪异步、额外自动重试。
- [x] 所有自有测试对象和代理完成清理；证据/文档只声明本轮实际验证的范围。

## STOP 条件与维护

前置门缺失、非预期漂移、需要管理员权限或修改目录范围、关系命令改变已有事务/连接所有权、现有驱动公开合同不支持预期取消/期限、需要升级依赖或越界文件时停止报告。完整 Execute 帧屏障加自有慢函数是可实施主路径；只到 Allocate/Prepare、响应抢先或超时发生在 Execute 前必须记录未完成验证，不能退回字节水位、模拟异常或“服务器正在执行”的推断。测试跳过/服务器不可用不能关闭 F12。一次合理修正后同一验证仍失败，保留失败证据并交回。

后续若更改 HasTables SQL、EF 关系命令 API、驱动超时单位或连接恢复合同，须一起维护三个层次的证据。不要将本次可观测性改进扩成对任意目录性能的承诺；性能优化另需规模与执行计划证据。

## 批次交付与统一证据

本计划属于 M3，启动实施前须确认 M2 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/011.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为确认问题或验证缺口，须以“已修复并验证”闭合；补足有辨别力的测试证据也属于验证缺口的修复。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M3.md` 的输入。
