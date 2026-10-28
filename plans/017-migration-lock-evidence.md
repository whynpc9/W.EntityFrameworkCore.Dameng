# 计划 017：验证迁移锁的争用、DDL 持锁与释放契约

> 本计划补验证证据，不预先认定迁移锁失效。新增测试若发现产品缺陷，记录并交回协调者，不通过放宽断言、改变锁 ID 或偷偷加入重试使测试通过。协调者维护总索引。

## 状态

- 状态：TODO；里程碑：M5（迁移与资源恢复）；发现：F13。
- 优先级：P2；工作量：M；修改风险：LOW；类别：测试。
- 原始审查锚点：`c80c788fb3cd34d97626d578db580264a28a11f3`，2026-10-28。
- 依赖：`014-admin-ddl-recovery.md`、`011-has-tables-async.md`、`016-history-script-tokenization.md`。014 确立不确定 DDL 的记录/恢复原则；011 提供取消和命令诊断的可靠测试模式；016 完成历史仓储前置变更后再验证锁，避免共享服务并行改动。
- 分支：`codex/m5-017-migration-lock-evidence`；隔离工作树执行，不自动 commit、push、合并或发布。

## 目标及已有取舍

当前测试只证明单次获取与释放调用未报错，没有证明另一个会话被排斥、DDL 隐式提交后锁仍有效、作用域异常/连接关闭/取消后不会泄漏锁。本计划使用同一测试内的独立连接增加这些证据。

固定 `MigrationLockId=20260723` 是现有保守设计，锁不按 DbContext、历史表或模式划分。本计划接受由此产生的跨模式串行化，不能改成每个测试随机锁 ID 来避免争用，也不能宣称迁移锁允许不同模式并行。两驱动数据库通道及其他使用该实例迁移锁的测试/任务必须串行调度；单个争用用例内允许多条连接，且每条连接/DbContext 只由一个执行分支操作。

官方驱动异步允许同步回退、运行中取消有限；W 的异步/取消遵守 FailureInfo，并禁止自动重试。不能对官方驱动套用 W 的任务挂起或即时取消断言。

## 当前实现与测试模式

仓库根目录 `/Users/wanghongyi/Projects/dameng-entityframework-core`。先读取 `AGENTS.md`、`skills/dameng-sql/SKILL.md`、`skills/dameng-ef-migrations/SKILL.md`、`docs/architecture.md` 的事务/迁移章节及 `docs/w-dmprovider-validation.md` 的取消边界。

`src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengHistoryRepository.cs:13-16,169-178`：

```csharp
private const int MigrationLockId = 20260723;
public override LockReleaseBehavior LockReleaseBehavior
    => LockReleaseBehavior.Connection;
// Acquire:
$"SELECT DBMS_LOCK.REQUEST({MigrationLockId}, 6){SqlGenerationHelper.StatementTerminator}"
// Release:
$"SELECT DBMS_LOCK.RELEASE({MigrationLockId}){SqlGenerationHelper.StatementTerminator}"
```

同文件 `:117-135,146-166` 仅在 REQUEST 返回 0 时视为获取成功，异常或其他返回值尝试 Dispose 后抛出。`DamengMigrationDatabaseLock.cs:15-23` 以关系命令释放，异步释放使用 CancellationToken.None。

`test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengMigrationsFunctionalTests.cs:239-241` 当前唯一显式锁验证：

```csharp
await using (await historyRepository.AcquireDatabaseLockAsync())
{
}
```

`test/W.EntityFrameworkCore.Dameng.FunctionalTests/AssemblyInfo.cs` 已设置 `[assembly: CollectionBehavior(DisableTestParallelization = true)]`；W 功能项目链接共享源码并定义 WDRIVER。保持这些隔离条件。目录与 DDL 命令沿用唯一名称、组成部分引用、finally 精确清理，不依赖回滚。

参考 [达梦 DBMS_LOCK 官方说明](https://eco.dameng.com/document/dm/zh-cn/pm/dbms_lock-package.html) 核对锁模式、REQUEST 返回码、非等待 timeout 参数及 RELEASE 行为。实现前必须核实当前服务器签名；不能把 Oracle 的参数规则直接套用。

## 文件白名单与漂移

只允许新增/修改：

- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengMigrationLockFunctionalTests.cs`（新增，W 自动链接）。
- `test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRequestBarrierRelay.cs`（010 已验收辅助类；默认只读复用经校准的 Execute 完成转发屏障，确有必要时仅补充本计划所需的阶段水位/等待/释放接口，必须重跑 010 与 011）。
- `docs/migrations.md`、`docs/compatibility.md`、`docs/w-dmprovider-validation.md`。
- 本计划文件。

只读产品 HistoryRepository/DatabaseLock、现有夹具、命令诊断和超时实现。W 发送屏障复用 010 已验收的 Execute 阶段识别与完整转发能力，不复制代理或协议识别实现。010 仅在内存识别公开帧头/消息类型且不记录 payload 的约束继续适用。禁止修改产品锁算法、锁 ID、默认重试、驱动/EF 版本、测试并行设置、全局 DBMS_LOCK/INI 权限或实例参数。禁止释放/终止不属于当前用例的会话。

```bash
git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengMigrationLockFunctionalTests.cs test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRequestBarrierRelay.cs docs/migrations.md docs/compatibility.md docs/w-dmprovider-validation.md
git status --short
```

原始 SHA 只用于定位审查时的证据。先读完 014/011/016 的已验收差异，更新实施 SHA 与摘录位置；可归因的前置变更不触发停工，未解释的差异/契约冲突才暂停。测试需针对当前已集成的产品代码，而非重新构建旧版本。

## 有界执行与命令

M1 的 002 已提供 `--configuration`。所有测试仍通过 `scripts/local-test/run.sh` 进入，其设置临时可写 DOTNET_CLI_HOME、首次体验/遥测关闭、单节点和禁构建服务器选项。无连接串放入 argv，无裸 dotnet 或全局包缓存清理。

锁测试必须同时有服务器端非等待探测、单个异步操作 deadline、最终测试进程 deadline。以下 shell 函数只是对统一入口增加 180 秒进程外上限，不创建新的测试通道：

```bash
run_lock_lane() {
python3 - "$@" <<'PY'
import os
import signal
import subprocess
import sys

process = subprocess.Popen(["scripts/local-test/run.sh", *sys.argv[1:]], start_new_session=True)
try:
    code = process.wait(timeout=180)
except subprocess.TimeoutExpired:
    os.killpg(process.pid, signal.SIGTERM)
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        os.killpg(process.pid, signal.SIGKILL)
        process.wait()
    print("Migration lock lane exceeded its deadline; inspect exact owned resources before retrying.", file=sys.stderr)
    code = 124
sys.exit(code)
PY
}
```

适用于仓库已使用的 Unix shell 环境；其他平台须先提供等效进程树终止方式，不照搬 killpg。180 秒包括构建，首次构建超时也只能判基础设施失败，不能据此判锁错误。超时退出后不自动重跑；核对锁会话结束及私有记录的自有对象，清理未确认则 BLOCKED。wrapper 仅打印启动器已脱敏输出。

| 用途 | 命令 | 预期 |
| --- | --- | --- |
| 官方锁聚焦 | `run_lock_lane test functional --configuration Debug --filter 'Plan=017'` | 至少 7 个共享场景通过 |
| W 锁聚焦 | `run_lock_lane test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'Plan=017'` | 至少 8 个场景通过，包含运行中取消 |
| 官方迁移交叉 | `scripts/local-test/run.sh test functional --configuration Debug --filter 'FullyQualifiedName~DamengMigrationsFunctionalTests|FullyQualifiedName~DamengHistoryScriptFunctionalTests'` | 全部通过 |
| W 迁移交叉 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'FullyQualifiedName~DamengMigrationsFunctionalTests|FullyQualifiedName~DamengHistoryScriptFunctionalTests'` | 上一命令结束后运行，全部通过 |
| 修改屏障后的 010 回归 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'AuditPlan=010'` | 010 的四个固定方法全部通过 |
| 修改屏障后的 011 回归 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'FullyQualifiedName~WDriverDatabaseCreatorAsyncFunctionalTests'` | 011 的真实发送/取消/期限用例全部通过 |
| 修改屏障后的原代理回归 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'FullyQualifiedName~WDriverRuntimeFunctionalTests'` | reset/拒绝连接等原场景均通过 |

锁用例单操作等待建议不超过 10 秒，持锁测试段不超过 30 秒，外层 finally 始终释放持有者。最终接受必须退出 0、Completed、total>0、total=executed=passed、failed=skipped=0；code 124、预期过滤发现 0 项、连接不可用/缺 DBMS_LOCK 权限均不算通过。所有上表命令顺序执行，禁止重叠数据库通道。

## 实施步骤

### 1. 建立不会无限阻塞的争用夹具

新类 `DamengMigrationLockFunctionalTests` 使用 `[Trait("Plan", "017")]` 和 DamengFact/Theory。创建持有者 A、竞争者 B、必要时核对者 C 的独立连接与 DbContext。通过 `Database.OpenConnection[Async]` 显式保持 A 的同一物理会话；每次锁内操作前后核对会话 ID，不能因为 EF 自动关闭再开连接而让“锁释放”测试天然成立。

实现私有辅助 `ProbeExclusiveLockWithoutWaiting`：按当前达梦签名，以有限/零等待 timeout 请求同一个真实锁 ID，断言返回“超时未取得”或“成功取得”的精确返回码；成功时在 finally 立即释放自己取得的锁。不得对其他会话的锁执行 RELEASE。开始用例前先探测；若固定锁被外部迁移持有，报告环境被占用并退出，不等待/终止对方。

对已打开连接的产品 IHistoryRepository 调用是真实被测对象；辅助 SQL 只用来观测锁，不替换产品的获取或释放。为 A/B 配置不同的历史表名称/模式元数据，证明锁的作用域仍是既定固定 ID；这些仅为 options 元数据，不需要创建额外模式。

**验证**：先加入并执行下一步的第一组场景，运行官方锁聚焦命令；若探测签名/返回码与官方说明不符，停止记录，不将未知码视为成功。

### 2. 验证 DDL 隐式提交前后仍排斥其他会话

固定方法 `ContenderIsExcludedAcrossDdlAndReleasedAfterDispose`，Theory 覆盖同步/异步产品获取与释放：

1. A 通过产品 IHistoryRepository 获取锁；B 非等待探测必须未取得。
2. A 在同一已保持连接上执行唯一自有表 CREATE，并明确无事务/事务被抑制；B 再次探测仍未取得。
3. A 显式 Dispose/DisposeAsync 锁；B 必须取得再释放；再次用产品获取/释放成功。
4. 独立核对自有表存在且内容可用，finally 精确 DROP 并确认不存在。

DDL 发送前将唯一表名/模式和创建尝试写入用户私有、Git 忽略的恢复诊断文件（遵循 014 的无凭据及所有权准则），确保进程外 deadline 中止后仍能准确调查；不得在终止后按前缀清扫。日志只输出文件位置和脱敏状态。

**验证**：官方锁聚焦应发现此方法 2 个场景并通过；随后运行 W 锁聚焦，同样 2 个场景通过。两轮不得重叠。若隐式提交释放锁，保留失败并停止，禁止换成测试专用随机锁掩盖产品行为。

### 3. 验证异常作用域与关闭连接释放

加入下列共享用例，每个同步/异步各一项：

- `ExceptionInsideLockScopeReleasesOwnedLock`：A 已取得并验证 B 被排斥，在 using/await using 体内抛合成异常；断言异常仍被观察到，B 随后能取得，A 再次通过产品取得/释放成功。
- `ClosingOwningConnectionReleasesLock`：先证明 A 持有且 B 被排斥，再关闭 A 的实际持有连接；B 随后取得。处理原锁对象 Dispose 时不能释放 B 的锁或吞掉非预期错误；用 C 的非等待探测证明 B 持有期间依然排斥其他会话，再由 B 正常释放。

不模拟“关闭”成创建一个新 Context，也不在故障前让 EF 自动归还连接。断言要同时覆盖正对照（健康持有确实阻塞）和释放后的取得，避免单独比较两次会话 ID。

**验证**：依次运行官方与 W 锁聚焦；此时共享场景至少 6 项通过，所有用例结束后同一锁可再次获得，私有记录无待处理自有表。

### 4. 分驱动验证取消边界

共享方法 `PreCanceledAcquireDoesNotLeaveLockOwned`：使用已取消 token 调用 AcquireDatabaseLockAsync，确认未获得有效锁、异常取消语义符合现有驱动边界，随后另一连接可取得。官方命令诊断只证明 EF 执行路径，不能证明网络零发送；W 复用 010 的代理，在登录/预热完成后的阶段水位上断言本次锁 REQUEST 的 Execute 完成转发计数为零，并保留 010 已验收的预取消原始零转发对照。随后健康检查的发送与该断言分开；AllocateStatement/Prepare 流量不能当作锁 REQUEST 已执行的证据。若锁定官方驱动不满足发送前取消契约，保留实测并交回协调者，不直接复制 W 的期望。

仅在 WDRIVER 编译分支增加 `CancelWaitingAcquirePreservesTokenAndAllowsLaterAcquisition`：

1. 保持 A 持锁。B 通过已验收的 `WDriverRequestBarrierRelay` 建立并显式保持连接，完成登录、B sessionID 查询和预热后，确认无待处理操作并保存独立阶段水位。B 随后只调用真实产品 AcquireDatabaseLockAsync；等待 010 经校准的屏障确认水位之后本次锁 REQUEST 对应的 Execute 请求已完整转发、对应执行响应尚未完成，并确认任务仍未完成。W 0.2.0 新命令可先发送 AllocateStatement；任意首批字节、AllocateStatement 或 Prepare 计数均不能满足该屏障。EF CommandExecuting、Task 启动或固定 Sleep 同样不能充当真实 Execute 发送证据。若 010 尚未完成阶段校准或接口只能报告原始字节，前置门未满足，停止本用例。
2. 再用独立连接按 B sessionID 读取当前服务器公开支持的锁等待状态，证明 B 已进入目标锁的等待队列；诊断查询必须有期限，不能借管理员权限绕过。Execute 屏障只证明执行请求在途，不能独自证明服务器锁队列等待。普通权限下无法确认等待状态时保留在途证据并报告该门禁 BLOCKED，不标记“等待中取消”通过。
3. 对 B 取消。10 秒内观察 OperationCanceledException，核对原 token、当前驱动 FailureInfo/连接可复用性合同；不要求已经取消的坏连接被原地复用。
4. 保持 A 持锁时 C 的非等待探测仍失败，证明取消 B 没有释放 A。
5. finally 释放 A 后，以新 B 会话通过产品获取/释放成功；所有辅助会话和代理在自己的路径完成后关闭，水位/屏障有独立期限且不记录报文内容。

官方驱动不运行必须即时响应的等待中取消用例；文档明确它仅有本轮发送前取消/作用域释放证据，不能标“运行中取消已验证”。任何测试内 Task.Run 只能用于隔离已知同步回退的测试调用，不能成为产品非阻塞 I/O 的证据；本计划优先避免发起官方无限等待的产品获取调用。

**验证**：官方锁聚焦至少 7 项，W 至少 8 项全部通过；两轮统计分开。若补充了代理接口，继续顺序运行表中 010、011 和原代理回归；保持原 reset 行为和完整转发，不以降低屏障要求使旧测试通过。W 运行中取消失败、不能证明等待状态或异常合同漂移时停止，不能引入自动重试、接受任意异常或简单放大 deadline。

### 5. 交叉回归并校准能力声明

运行两条迁移交叉命令，验证历史表正常创建/重复脚本未受测试夹具影响。文档写明固定锁的跨模式争用范围、当前实例 DBMS_LOCK 可用条件、DDL 前后持锁证据、正常/异常/关闭释放证据，以及两驱动不同取消结论。保持非 MPP 边界，不因单机通过声明 MPP 支持。

**验证**：迁移交叉命令通过；`git diff --check` 退出 0；`git diff --name-only` 仅白名单。保存各测试名称、驱动、实施 SHA、profile、TRX 和恢复记录状态，不打印连接/用户/密码。

## 完成标准

- [ ] 使用真实固定锁 ID、真实 IHistoryRepository 和保持打开的持有会话；竞争者为独立连接。
- [ ] DDL 前/后两次争用均被排斥，Dispose 后取得成功；同步/异步均覆盖。
- [ ] 异常作用域和连接关闭释放均有“先持有、后释放”的可辨别对照。
- [ ] 官方至少 7、W 至少 8 个场景通过；W Execute 完成转发有预热后阶段水位证据（不是 AllocateStatement/Prepare），锁等待有 B sessionID 对应服务器证据，取消保留 token 且未释放其他会话锁。
- [ ] 若最小补充了 010 的屏障接口，010/011 及原代理回归全部通过；没有复制代理或把 EF 执行事件当成网络发送证据。
- [ ] 等待、持锁和进程均有上限；没有因超时留下未解决的自有对象/会话。
- [ ] 两驱动交叉回归通过；文档未把官方同步回退或未验证 MPP 写成新能力。

## 停止条件

- 固定锁被其他任务占用、无法独占数据库测试时段：BLOCKED；不杀会话、不变锁 ID、不并发重跑。
- 缺测试连接、DBMS_LOCK 权限或当前服务器不提供相同基线：BLOCKED，不提权或跳过后宣称完成。
- 产品锁实际违反持锁/释放契约：保留最小失败证据并提出独立修复，不改本计划白名单外代码。
- deadline 触发、finally 失败、无法确认准确对象/会话是否已清理：立即停止后续库操作并按私有记录精确核对。
- 需要依赖官方驱动未承诺的运行中取消或并发访问同一 DbContext 才能完成测试：停止重新设计。
- 前置改动无法归因、出现契约冲突或合理修正后门禁连续失败两次：交回协调者。

## 维护提示

未来改变固定锁 ID、REQUEST 参数、LockReleaseBehavior、连接重开策略或迁移执行事务时，必须重跑整个矩阵。争用回归必须继续隔离于其他迁移测试；只删除“空 acquire/release 测试”或增加超时数字都不能替代真实互斥证据。

## 批次交付与统一证据

本计划属于 M5，启动实施前须确认 M4 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/017.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为确认问题或验证缺口，须以“已修复并验证”闭合；补足有辨别力的测试证据也属于验证缺口的修复。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M5.md` 的输入。
