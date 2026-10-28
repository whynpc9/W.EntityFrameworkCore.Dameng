# 计划 010：用固定会话、发送屏障和正常对照验证 W 会话淘汰

> 这是待执行的测试改进计划。逐步运行全部验证，保留真实证据；只修改白名单文件。主协调者维护索引，不要自动提交、推送、创建 PR 或标记整个里程碑完成。

## 状态与依赖

- 状态：TODO；里程碑：M3「驱动与连接生命周期」。
- 发现：F09；优先级：P2；类别：测试；实施工作量：M；修复风险：MED。原审查的 S/LOW 估计保留为历史；本计划增加协议阶段观察和自测后按 M/MED 安排，仅修改测试辅助与文档。
- 基线：`c80c788fb3cd34d97626d578db580264a28a11f3`；日期：2026-10-28。
- 该 SHA 是原始审查锚点。实施先读取已验收前置计划的差异，更新证据中的摘录定位和实施 SHA 后继续；可归因的预期前置修改不触发停工，非预期差异或契约冲突才暂停核对。
- 依赖：009 已完成驱动/连接边界，且其单元和实库门通过；M1 的 002 已提供 `--configuration Debug|Release`，004 已提供离线验收入口。011 在本计划完成后复用请求屏障。
- 首先执行 `git status --short`、`git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRuntimeFunctionalTests.cs test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRequestBarrierRelay.cs test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRequestBarrierRelayTests.cs docs/w-dmprovider-validation.md docs/compatibility.md plans/evidence/010.md`。允许经过核对的前置计划变更；其他漂移若与片段不一致则停止。不得覆盖已有未跟踪文件。

## 为什么需要改

目前两项 W 故障测试分别比较故障前后的 SESSID，但没有在第一次查询前显式保持连接。W 默认不池化时，EF 正常关闭再打开连接也会改变会话 ID，因此 `NotEqual` 缺少区分“故障淘汰”和“普通生命周期”的能力。目标是保留同一已打开连接，证明正常操作保持会话，故障才使连接失效；取消还必须在命令发出后触发。

现有 FailureInfo、原始 token 和故障后可继续使用上下文的断言仍然有效，不能删除，也不能把此次证据修复表述为已经发现驱动运行时缺陷。

## 当前代码与应遵循的范例

`test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRuntimeFunctionalTests.cs:85-102`：

```csharp
await using var context = store.CreateContext();
var sessionBefore = await SessionIdAsync(context);
using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
// 执行服务器休眠查询，检查 token 与 FailureInfo。
Assert.NotEqual(sessionBefore, await SessionIdAsync(context));
```

`PrepareFailureBreaksTheSessionAndEfOpensANewOne`（`:420-430`）同样没有显式打开连接。辅助函数 `:697-698` 自己执行完整 EF 查询：

```csharp
private static async Task<long> SessionIdAsync(DbContext context)
    => (await context.Database.SqlQueryRaw<long>("SELECT SESSID() AS \"Value\" FROM DUAL").ToListAsync()).Single();
```

同一文件 `MeasuredStatementErrorsKeepTheSessionAndTheTransaction`（`:196-215`）已经有正确的会话固定方式：

```csharp
await using var context = store.CreateContext();
await context.Database.OpenConnectionAsync();
var sessionBefore = await SessionIdAsync(context);
// 执行可恢复错误。
Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
Assert.Equal(sessionBefore, await SessionIdAsync(context));
```

文件末尾的私有 `ResettingRelay` 只在回环地址监听，将流量转发至既有测试连接的服务器；`:1043-1046` 用循环保证 `SendAsync` 部分发送时仍完整转发。`:938-945` 观察 `_lastClientForward > _lastServerForward` 且持续未应答，再主动 reset。保留这些既有故障模式，但新增屏障不能沿用“首批客户端字节即 Execute”的推断。

manifest 锁定的公开 W 0.2.0 源码提交为 `aef142f05af67d2ce300f0a9f3a9fbd198f304cc`。[DmCommand.cs:1405–1446](https://github.com/whynpc9/W.DamengProvider/blob/aef142f05af67d2ce300f0a9f3a9fbd198f304cc/src/W.DamengProvider/PublicApi/DmCommand.cs#L1405-L1446) 先 EnsureStatement，再执行；[A.Async.cs:19–23、58–83](https://github.com/whynpc9/W.DamengProvider/blob/aef142f05af67d2ce300f0a9f3a9fbd198f304cc/src/W.DamengProvider/Internal/Legacy/A/A.Async.cs#L19-L83) 明确先 Allocate，且直接执行与 Prepare 分别传 true/false。因此登录、预热后，新 EF command 的第一个请求仍可能只是 Allocate。实施使用下列固定版本依据，只写一个测试用流式帧观察器，不反射或调用驱动内部 API：

| 必要协议字段 | 固定版本依据 | 测试中的分类 |
| --- | --- | --- |
| 64 字节头，opcode 在偏移 4 的 little-endian int16，bodyLength 在偏移 6 的 little-endian int32；heartbeat=269 | [DmFrameReader.cs:12–18](https://github.com/whynpc9/W.DamengProvider/blob/aef142f05af67d2ce300f0a9f3a9fbd198f304cc/src/W.DamengProvider/Internal/Protocol/DmFrameReader.cs#L12-L18) | 按完整 wire 帧边界观察，不能按 Socket.Read 次数分包 |
| Allocate opcode=3 | [B.Async.cs:461–468](https://github.com/whynpc9/W.DamengProvider/blob/aef142f05af67d2ce300f0a9f3a9fbd198f304cc/src/W.DamengProvider/Internal/Legacy/A/B.Async.cs#L461-L468) | Allocate，绝不完成 Execute 屏障 |
| opcode=5 共用；执行标志写入 header[21]，false=0、true=1 | [请求编码:111–140](https://github.com/whynpc9/W.DamengProvider/blob/aef142f05af67d2ce300f0a9f3a9fbd198f304cc/src/W.DamengProvider/Internal/Legacy/A/T02_02000093.cs#L111-L140)、[字段定位:905–908](https://github.com/whynpc9/W.DamengProvider/blob/aef142f05af67d2ce300f0a9f3a9fbd198f304cc/src/W.DamengProvider/Internal/Legacy/A/T02_02000092.cs#L905-L908) | 5+0 为 Prepare；5+1 为 DirectExecute；未知标志不得算 Execute |
| PreparedExecute opcode=6 或 13 | [RequestCodec.Async.cs:104–119](https://github.com/whynpc9/W.DamengProvider/blob/aef142f05af67d2ce300f0a9f3a9fbd198f304cc/src/W.DamengProvider/Internal/Legacy/A/RequestCodec.Async.cs#L104-L119) | PreparedExecute，与 DirectExecute 同为可武装目标 |

屏障只证明完整 Execute 帧已由代理转发给上游 socket、被测调用尚待结果。没有服务器侧活动观察时，不能写成“SQL 已在服务器执行中”；原测试名中的 RunningQuery 仅保留历史定位，不扩大证据含义。

## 硬边界

- 仅测试/文档变化，不修改产品、驱动、重试分类、时间映射、锁文件或版本。
- W 自动重试仍禁用；不能通过重试测试掩盖失败。官方驱动同步回退不是本项问题。
- 测试只使用普通持久测试用户；不创建用户/表空间，不改变实例参数，不扩大权限。
- 测试对象使用唯一名称，在 finally 精确清理；DDL 隐式提交，不能靠回滚清理。
- 故障代理只在测试进程的回环端口接收本测试连接；只支持本轮明确选择的明文 profile，不宣称 TLS 故障验证。
- 测试代理仅在内存读取固定帧头的长度、类型及上述执行标志；body 只作有界分块转发，不解释 SQL/参数，不保存抓包。不记录连接串、地址、用户名、口令、参数值、原始帧头或报文；证据只保留阶段枚举、计数、事件顺序、状态和脱敏运行路径。复用缓冲区清零后归还，不把异常正文中的连接信息带入日志。

## 文件白名单

- `test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRuntimeFunctionalTests.cs`
- `test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRequestBarrierRelay.cs`（新增内部测试辅助类）
- `test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRequestBarrierRelayTests.cs`（新增纯内存的阶段/分片/短写自测；不打开 socket 或数据库连接）
- `docs/w-dmprovider-validation.md`
- `docs/compatibility.md`
- `plans/evidence/010.md`（新增）

不修改项目文件：新辅助文件由 SDK 默认编译包含，不添加依赖。011 可在后续复用该内部辅助类；不要建立生产代码可见的网络代理 API。

实施使用协调者指定的隔离工作区/分支；若协调者要求新分支，采用 `codex/010-session-eviction-evidence`。本计划不授权自行创建提交、推送或 PR，也不允许重置或暂存他人的改动。

## 命令与验收规则

所有命令在 `/Users/wanghongyi/Projects/dameng-entityframework-core` 执行。统一入口已经设置可写临时 `DOTNET_CLI_HOME`、关闭首次体验/遥测、锁定还原及单节点构建测试。不得打印 secrets；普通功能通道不可换成管理员账户。官方与 W 数据库通道不能并行；一个测试内部的回环代理、被测连接与独立核对连接可以并存。

新建/改造的四个测试都添加 `[Trait("AuditPlan", "010")]`，固定保留以下方法名：

1. `OpenSessionSurvivesSuccessfulQueriesWithoutReopening`（新增正常对照）。
2. `CancelingARunningQueryKeepsTheTokenAndUsesANewSessionAfterwards`（修订原测试）。
3. `PrepareFailureBreaksTheSessionAndEfOpensANewOne`（修订原测试，名称保留以对接历史证据）。
4. `PreCanceledHeldSessionDoesNotForwardACommand`（新增预取消对照）。

| 用途 | 精确命令 | 通过条件 |
| --- | --- | --- |
| 屏障离线自测 | `scripts/local-test/run.sh test offline-fixtures --driver w --configuration Debug --filter 'FullyQualifiedName~WDriverRequestBarrierRelayTests'` | 下列 8 个固定方法均执行，非零 Completed，无失败/跳过，不读取连接配置 |
| 本项四个用例 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'AuditPlan=010'` | TRX 恰好包含上述四个方法，每个通过，无失败/跳过 |
| 原 W 运行时全类 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'FullyQualifiedName~WDriverRuntimeFunctionalTests'` | 非零 Completed，所有既有 reset/恢复用例仍通过 |
| Release 四个用例 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Release --filter 'AuditPlan=010'` | 四个方法均通过；证据确为 Release |
| Release 屏障自测 | `scripts/local-test/run.sh test offline-fixtures --driver w --configuration Release --filter 'FullyQualifiedName~WDriverRequestBarrierRelayTests'` | 同一 8 个固定方法非零通过 |
| 代码差异 | `git diff --check` | 退出 0，无输出 |

所有数据库运行顺序执行，只有 Completed、total > 0、executed=passed=total、failed=skipped=0 才能验收。Trait 过滤若枚举不到四个方法立即失败，不能把零测试作为通过。

## 步骤

### 1. 固定正常会话和预取消对照

在现有 `RuntimeStore` 夹具中新增两个方法，连接串只在内存通过 W typed builder 显式设置 `Pooling=false`。正常对照用 `OpenConnectionAsync` 打开一次，保存 `DbConnection` 引用和 `SESSID`，顺序执行至少两条普通成功查询，再断言相同引用、Open 状态和相同 SESSID。最后按正常生命周期关闭/释放，不要求池化。

预取消对照使用已打开会话、已经取消的 token，断言原 token、零被测命令发送和原会话仍可成功查询；“零发送”的代理计数在步骤 2 接入前可先由命令拦截计数表征，最终验收必须用转发水位核对。不要仅依靠任务已完成/耗时短来证明未发送。

**验证**：`scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'FullyQualifiedName~OpenSessionSurvivesSuccessfulQueriesWithoutReopening'`，预期恰好 1 项通过；后续变化必须继续通过此正常对照。审查 SQL 前读取 `skills/dameng-sql/SKILL.md`，沿用已有 SESSID/休眠函数和精确清理模式。

### 2. 提取并扩展请求观测辅助类

将现有私有 `ResettingRelay` 平移到新增内部 `WDriverRequestBarrierRelay`，更新本文件已有引用。保留既有 reset-on-login、refused-connect、完整转发和请求后 reset 行为，不能顺手更改其故障分类。

增加以下内部测试能力，接口名称可按实现调整，但语义必须逐项满足：

1. 双向流式累计恰好 64 字节固定头，解析已列出的必要字段，再按 wire bodyLength 消费并完整转发 body。处理任意头/体分片、一次读取多帧、Socket.SendAsync 短写和 EOF；只使用有界转发块，不分配整个声明长度。检查非负长度、checked 总长及 W 的 64 MiB 上限，畸形/截断帧令测试失败，不回退到字节水位。CRC 的尾部 4 字节已经计入 wire bodyLength，不再额外加 4，且无需解码 body 来观察阶段；依据 [D.Async.cs:52–56](https://github.com/whynpc9/W.DamengProvider/blob/aef142f05af67d2ce300f0a9f3a9fbd198f304cc/src/W.DamengProvider/Internal/Legacy/A/D.Async.cs#L52-L56)。原始流量逐字节保持一致。
2. 登录、SESSID 读取及预热结束，确认无未完成 exchange 且处于完整帧边界后，在独占会话武装 `ArmExecute`，取得本次 generation。Allocate、Prepare、关闭 statement 等前导请求及响应正常流过，但不能触发目标屏障。只将该 generation 的首个 DirectExecute 或 PreparedExecute 设为目标；不依赖已知字节数、固定延时或 SQL 文本匹配。目标查询不得与其他命令并发。
3. 在开始转发目标帧前登记 pending exchange，避免服务器快速响应的竞态；**最后一个 header/body/CRC 字节已成功转发上游后**才发布 `ExecuteForwarded`。读到头、只发送部分帧、Allocate 完成、Prepare 完成或 EF `ReaderExecutingAsync` 均不能发布。屏障等待有 cancellation/独立最大期限，不能为制造等待而暂停真实响应转发。
4. 响应不能靠“opcode 等于请求”配对；固定版本存在 opcode=0 的合法响应（[ScriptedTransactionFixture.cs:68–76](https://github.com/whynpc9/W.DamengProvider/blob/aef142f05af67d2ce300f0a9f3a9fbd198f304cc/tests/W.DmProvider.DiagnosticsTests/ScriptedTransactionFixture.cs#L68-L76)）。在独占、逐 exchange 的约束下，首个非 heartbeat 响应归入当前请求，未知响应也不得忽略。服务器方向开始收取尚未分类的头时暂标“响应可能已开始”；只有确认完整 heartbeat 才可略过。测试消费屏障时复核当前 generation、ExecuteForwarded、无目标响应开始且被测 Task 未完成；响应先到则该次等待证据失败，不能用陈旧 latch 假绿。该观察仍不证明服务器 SQL 正在执行。
5. 生命周期跟踪接受/双向转发任务；DisposeAsync 停监听、关闭自有 socket、解除全部等待并等待任务退出。不得留下 fire-and-forget 任务。为纯内存测试提供有限的读/写委托或等价接缝，以同一观察器和完整发送循环测试分片/短写，不改驱动或引入公开产品 API。

新类 `WDriverRequestBarrierRelayTests` 使用普通 `[Fact]`/`[Theory]`、`[Trait("Category", "OfflineFixture")]` 和 `[Trait("AuditPlan", "010Barrier")]`，没有数据库属性或集合夹具，使用合成固定头及无业务含义的 body 哨兵；不读取真实连接或打开 socket。以下 8 个固定方法名必须由离线命令发现（Theory 可展开多例）：

- `AllocateOnlyDoesNotCompleteExecuteBarrier`：完整 Allocate 及其响应均通过仍未触发，取消屏障后正常退出；这是复现旧字节水位假绿的必要负例。
- `AllocateResponseThenDirectExecuteCompletesOnlyAfterFullForward`：同一合成会话先 Allocate/响应，再 5+1；延迟最后一次短写，之前不得触发，之后恰好一次。
- `PrepareOnlyDoesNotCompleteExecuteBarrier`：5+0 与响应不能满足；未知 header[21] 也不能当成执行。
- `PreparedExecuteOpcodesCompleteExecuteBarrier`：6、13 均在完整转发后触发，前导 Prepare 不触发。
- `FragmentedAndCoalescedFramesPreserveBytesAndBoundaries`：每字节分片、多帧合并、非空 body 与短写，转发结果逐字节相等且阶段顺序正确。
- `CrcTrailerIsCountedOnceAndTruncationCannotCompleteBarrier`：bodyLength 包含尾 CRC 的合成帧不多读 4 字节；缺最后字节、EOF、负/溢出/超限长度均不能报告完成。
- `EarlyOrOpcodeZeroResponseInvalidatesPendingObservation`：opcode=0、未知非 heartbeat、部分响应头及响应抢先均不能被遗漏；完整 heartbeat 不冒充 Execute 响应。
- `CancellationAndDisposalReleaseEveryBarrierWaiter`：未见 Execute/只见 Allocate 时，取消及释放终结所有 await，保留真正转发故障，无遗留任务。

**验证**：先运行屏障离线自测，再运行原 W 运行时全类；均非零且全部通过。尤其保留 `ConnectFailuresAreTransportFailures` 和 `AResetAfterSaveChangesWasSentReportsAnUnknownOutcomeThatIsNotCommitted` 的所有既有分支；旧 reset 模式与新 Execute 观察模式分开，不能用旧模式证明新的 Execute 阶段。

### 3. 在真正发送后取消，并观察实际连接终态

修订取消测试：经过代理建立并显式保持连接，保存实际连接引用/SESSID，完成预热后 `ArmExecute`；创建不带 500 ms 自动取消的 CTS，启动已有休眠查询。Allocate/Prepare 正常通过，等完整 DirectExecute/PreparedExecute 的 `ExecuteForwarded`，复核 generation、响应尚未开始及 query Task 仍未完成后才调用 CancelAsync。记录事件次序 ExecuteForwarded→CancelRequested→终态，不把首批字节或 Allocate 误当执行。整体测试期限与屏障期限分离，期限触发必须报告所达阶段，不能被当成预期取消成功。

保留原 token、W 取消类型、CancelSource.User、ConnectionReusable=false 断言。**在任何重开或后续查询之前**检查实际连接为 Broken 或 Closed，并核对终态与 FailureInfo 一致；然后使用 EF 打开/恢复连接，读取新的 SESSID，并验证后续普通查询成功。不添加透明重试，不假定整个事务已回滚。

修订准备错误测试：首次 SESSID 前显式打开连接，确认成功对照保持该会话；执行原来服务器拒绝的语句；保留 driver 错误及不可复用断言，在重开前验证实际状态，然后重新打开查询新会话。该语句在当前 W 0.2.0 中属于直接执行/不可复用的已验证负例，不扩大成“所有 Prepare 错误均破坏会话”的声明。

将预取消对照接入代理水位，命令执行前后的发送计数相同；随后独立的健康检查可以发送，但必须在零发送断言之后进行。

**验证**：运行 `AuditPlan=010` 精确命令，四个方法都通过；TRX/证据记录正常场景同会话、预取消无发送、完整 Execute 转发后且调用待结果时取消、两类故障重开前不可复用。不要写入具体 SESSID 值或网络端点，只记录断言结论和事件次序。

### 4. 完成回归和证据替换

按顺序运行 W 运行时全类、Release 四个用例、Release 屏障自测、`git diff --check`。文档保留历史记录的日期/范围，新增本轮“保持连接且完整 Execute 已转发后的会话淘汰验证”证据，不把旧测试结果改写成新实现的验收。

`plans/evidence/010.md` 记录执行 SHA、固定公开源码 SHA、准确命令、TRX 路径、四个真库与八个屏障自测的方法清单、Debug/Release 计数和实际服务器非敏感 profile。说明仅证明完整 Execute 转发/调用待结果，未观察服务器活动，未做 TLS、其他 profile 或 commit 失确认验证。本轮没有驱动产品变更，不应据此扩张支持范围。

**验证**：上述全部命令成功，且 `git diff --name-only` 与 `git ls-files --others --exclude-standard` 未出现本任务白名单外变更。

## 测试矩阵与完成条件

| 情形 | 发送 | 故障后、重开前的状态 | 会话断言 |
| --- | --- | --- | --- |
| 正常连续查询 | 有 | Open | 两次查询均为原会话 |
| 预取消 | 被测命令无发送 | 原会话仍可用 | 健康检查仍是原会话 |
| Execute 转发后取消 | 完整执行帧已转发，Allocate/Prepare 不能触发，调用仍待结果 | Broken/Closed，FailureInfo 不可复用 | 显式恢复后为新会话 |
| 已知不可恢复直接执行错误 | 有 | Broken/Closed，FailureInfo 不可复用 | 显式恢复后为新会话 |

- [ ] 四个固定方法存在并具有 `AuditPlan=010`；Debug、Release 均恰好执行四个且全通过。
- [ ] 八个固定屏障自测在 Debug/Release 离线入口通过，Allocate-only 负例、完整 Execute 正例、Prepare、opcode=0 响应、分片/短写/CRC/释放均有辨别力。
- [ ] 原 W 运行时全类通过，辅助类提取未削弱既有 reset/事务恢复断言。
- [ ] 取消测试先证明完整 Execute 转发且仍待结果，再取消；不依赖固定 500 ms、Allocate 或 Prepare 水位；重开前确实观察状态。
- [ ] 新辅助类无报文/连接串日志，无遗留 socket/后台任务；对象精确清理通过。
- [ ] 文档和证据区分历史与本轮；交协调者关闭 F09，继续 M3 的 011。

## STOP 条件与维护

如果保持连接后原有行为不再成立，应把它作为新发现报告，不能改成自动关闭再打开以恢复绿灯。固定版本协议与公开源码不符、屏障超时或只到 Allocate/Prepare、响应抢先导致未取得等待证据、代理意外改变终态、需要管理员权限、环境是 TLS、依赖升级或白名单外修改时停止。前述帧观察与慢查询是必走主路径，不能只写 STOP 后交付无执行阶段证据的水位屏障。一次合理修正后同一验证仍失败时记录并交回；缺数据库配置或跳过测试不能关闭本项。

未来修改 W 的连接池默认值、帧格式/消息类型、取消阶段、会话状态或驱动错误恢复合同时，四个真库对照和屏障自测必须成组回归。011、017 可复用辅助类，但各自武装 generation 和期限，不共享活动代理或数据库会话；它们同样不得把 Allocate/Prepare 当 Execute。

## 批次交付与统一证据

本计划属于 M3，启动实施前须确认 M2 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/010.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为确认问题或验证缺口，须以“已修复并验证”闭合；补足有辨别力的测试证据也属于验证缺口的修复。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M3.md` 的输入。
