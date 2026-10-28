# 计划 014：为管理员测试 DDL 保存所有权并恢复不确定结果

> 执行者逐步完成本计划及验证门禁；发生停止条件时报告 BLOCKED，不扩大权限、不清扫模式。协调者维护总索引，本计划执行者只回报状态与证据。本文授权范围是未来实施规范，编写计划不代表已经执行修复。

## 状态

- 状态：TODO；里程碑：M5（迁移与资源恢复）；发现：F11。
- 优先级：P1；工作量：M；修改风险：MED；类别：测试/资源管理。
- 计划基线：`c80c788fb3cd34d97626d578db580264a28a11f3`，2026-10-28。
- 依赖：`003-provision-password-policy.md`、`010-session-eviction-evidence.md`。003 完成口令策略与纯测试惯例；010 完成可辨别的会话故障观察方式。M1 的 002 已提供 `--configuration Debug|Release`，004 已提供不读取 secrets 的 `offline-fixtures` 通道。
- 分支：`codex/m5-014-admin-ddl-recovery`；不自动 commit、push、创建发布或发布包。

## 目标与边界

管理员夹具创建独立用户/表空间。服务器完成 CREATE 后，客户端仍可能收到异常；当前夹具在成功返回后才记下对象名称和成功标记，因而漏掉已经提交的对象。本计划让创建尝试在发送前留下不含凭据的所有权记录，失败后用新连接核对精确对象，再决定安全清理或保留待恢复状态。原测试仍应失败，不能把成功清理包装成创建成功。

DDL 隐式提交，不依靠事务回滚。未知结果不触发 CREATE 重试；不能根据公共前缀、登录用户下的全部对象或猜测路径执行删除。管理员连接只来自现有 admin 通道；没有相应权限/连接时记 BLOCKED，普通测试连接不能代替管理员。

## 当前实现与约定

仓库根目录为 `/Users/wanghongyi/Projects/dameng-entityframework-core`；在所选隔离工作树根目录执行命令。实施前读取 `AGENTS.md`、`skills/dameng-sql/SKILL.md`、`skills/dameng-ef-migrations/SKILL.md`、`scripts/local-test/README.md`。

`test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengScriptTestDatabase.cs:77-91` 当前形态：

```csharp
await ExecuteAsync(admin, /* CREATE TABLESPACE */);
_tablespaceCreated = true;
TablespaceName = tablespace;
await ExecuteAsync(admin, /* CREATE USER */);
_userCreated = true;
UserName = user;
```

同文件 `:225-243` 清理条件是 `_userCreated && UserName is not null` / `_tablespaceCreated && TablespaceName is not null`，只覆盖已确认成功的操作。`:259-270` 的 `ExecuteAsync` 将原异常转成脱敏文本，恢复逻辑不能再依赖被抹去的驱动错误类别判断“肯定没创建”。

可参考 `scripts/local-test/Program.cs:97-108` 的发送前记录模式，但不要修改持久测试环境的 provision：

```csharp
document["ProvisioningStatus"] = "planned";
document["OwnedTestTablespaceName"] = tablespace;
document["OwnedTestUserName"] = user;
document["TestDatafilePath"] = datafile;
SaveSecrets(document);
ambiguousCreate = true;
```

此处只借鉴状态机，不把管理员夹具记录写入 `.local-test.secrets.json`。夹具现有 `CreatePassword` 已保证字符类别，保持其标准。W 功能项目用 `Compile Include="$(SharedFunctionalTests)/**/*.cs"` 和 `WDRIVER` 链接官方功能源码；新增共享文件无需复制两份。真实管理员用例必须继续放在现有 `DamengMigrationScriptFunctionalTests`，不能仅凭 Trait 扩大 admin 类过滤。

## 文件白名单与漂移检查

只允许修改/新增：

- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengScriptTestDatabase.cs`。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengAdminDdlRecoveryState.cs`（新增：纯状态/所有权决策，不依赖数据库）。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengAdminDdlRecoveryStateTests.cs`（新增：离线测试）。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengMigrationScriptFunctionalTests.cs`。
- `scripts/local-test/TestLauncher.cs`（仅离线发现列表自检及可选的管理员恢复目录环境传递）。
- `scripts/local-test/README.md`、`docs/compatibility.md`（证据与恢复操作说明）。
- 本计划文件（记录结果，不编辑其他计划或审查报告）。

不修改产品代码、驱动包/锁、发布工作流、持久 provision 行为、数据库全局参数或权限策略。不得删除服务器文件。

```bash
git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengScriptTestDatabase.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengAdminDdlRecoveryState.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengAdminDdlRecoveryStateTests.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengMigrationScriptFunctionalTests.cs scripts/local-test/TestLauncher.cs scripts/local-test/README.md docs/compatibility.md
git status --short
```

`c80c788` 是原始审查锚点。实施先读取 003/010 已验收差异和 M1 入口变化，更新执行记录中的实施 SHA 与摘录定位；可归因的预期前置修改不触发停工，非预期差异或契约冲突才暂停复核。保留已有未提交修改，不覆盖其他恢复实现。

## 命令与验收口径

本地入口会建立可写临时 `DOTNET_CLI_HOME`，设置 `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`，使用锁定还原、单节点及禁用构建服务器配置。不要另行使用裸 `dotnet` 绕过入口，不清 NuGet 缓存。

| 用途 | 命令 | 通过条件 |
| --- | --- | --- |
| 离线状态回归 | `scripts/local-test/run.sh test offline-fixtures --configuration Debug --filter 'FullyQualifiedName~DamengAdminDdlRecoveryStateTests'` | 新类被发现，至少 8 个场景，无连接/secret 读取，全部通过 |
| W 离线状态回归 | `scripts/local-test/run.sh test offline-fixtures --driver w --configuration Debug --filter 'FullyQualifiedName~DamengAdminDdlRecoveryStateTests'` | 相同场景全部通过，仍无数据库连接 |
| 管理员聚焦 | `scripts/local-test/run.sh test admin --configuration Debug --filter 'Plan=014'` | 至少 4 个真实场景通过，且独立目录核对无自有残留 |
| W 管理员聚焦 | `scripts/local-test/run.sh test admin --driver w --w-transport-security PlaintextAllowed --configuration Debug --filter 'Plan=014'` | 相同范围通过；必须在官方命令结束后运行 |
| 完整管理员 | `scripts/local-test/run.sh test admin --configuration Debug` | 全部 Completed，零失败/跳过 |
| 完整 W 管理员 | `scripts/local-test/run.sh test admin --driver w --w-transport-security PlaintextAllowed --configuration Debug` | 前一命令结束后运行，全部 Completed，零失败/跳过 |

所有门禁均要求退出码 0、总数大于 0、total=executed=passed、failed=skipped=0。故意注入的异常由测试断言捕获，不能让预期失败 TRX 冒充最终通过。明文开关只适用于现有明文 profile，不降低其他环境要求。

## 实施步骤

### 1. 固化状态与测试接线

将每个对象的状态分为未尝试、已记录待创建、已发送但结果未知、确认存在、待清理、确认不存在。名称、对象类型、唯一运行标识和精确数据文件路径在发送前保存；运行中仍把尝试记录与“确认可用”的属性分开，避免 `EnableHugeStorageAsync` 等把未知对象当作可用。纯状态类不得持有连接串或口令。

新建 `DamengAdminDdlRecoveryStateTests`，类级 `[Trait("Category", "OfflineFixture")]`；使用普通 Fact/Theory，不用 `DamengFact`，不挂 `DamengMigrationScriptSet` 或其他数据库 Collection。维护 004 的离线发现列表自检，不新增通道、不把 admin 用例标成离线。

**验证**：执行两条离线状态回归命令。首先测试应能证明“发送前记录、未知不重试、所有权不足不删除”的决策；最终两驱动均通过。执行 `scripts/local-test/run.sh selftest`，发现/脱敏/计数自检全部通过。

### 2. 在 CREATE 前落地所有权日志

为每次临时夹具创建独立恢复记录，放在 Git 忽略的 `artifacts/query-translation/local-test/<runid>/recovery/` 下。可由启动器只向 admin 子进程传递非敏感的恢复目录；直接调用夹具没有该目录时使用独立用户私有临时目录。Unix 目录 0700、文件 0600，原子替换记录；Windows 使用当前用户私有目录并保持测试可验证的权限边界。

记录只包含恢复必需的生成对象名、阶段、时间、类型及精确文件路径，不包含连接串、密码、主机、完整异常或 CREATE USER 文本。不得将记录正文打印到控制台/TRX；报告只给本地恢复文件位置。文件写入失败时必须在发送 DDL 前停止。

创建前通过当前管理连接确认精确名称不存在并记录该事实。每条 CREATE 发送前将状态写成结果未知；成功返回后再标记确认存在。整个过程不改变已有大小、字符类别、RESOURCE/SOI 授权和临时对象标准。

**验证**：离线类新增 `JournalIsDurableBeforeCreateIsSent`、`JournalFailurePreventsCreate`、`JournalContainsNoCredentialSentinel`；运行官方离线命令，全部通过。使用合成哨兵检查文件和输出，不能用真实 secrets 做快照。

### 3. 用新会话核对并精确清理

初始化失败后先保留原失败，再用新的管理员连接核对尝试记录。只有名称、创建前不存在证据、实际对象类型及自有数据文件/默认表空间关系均匹配时，才允许依次删除自有用户、核对用户消失、删除自有表空间。旧连接发生传输/取消异常后不能作为恢复查询连接。DROP 响应未知时再查询精确对象；确认不存在可标记清理完成，不能仅因执行未返回就假设失败或再次无界重试。

目录不可读、对象不匹配、删除失败、结果继续未知时保留 `cleanup_required` 记录，并让测试失败。禁止自动重新创建；禁止因用户删除失败就删除仍被其使用的表空间。清理入口可重复调用，但已确认不存在的对象不再发 DROP。恢复也不得把原测试异常吞掉。

**验证**：离线测试 `UnknownCreateDoesNotRetryCreate`、`UnknownCreateRequiresFreshConnection`、`MismatchedOwnershipBlocksDrop`、`CleanupOrdersUserBeforeTablespace`、`RepeatedCleanupDoesNotDeleteUnownedObjects`、`UnresolvedCleanupRetainsRecord` 全部通过。运行官方及 W 离线聚焦命令，均通过。

### 4. 验证服务器完成 CREATE 后客户端报错的恢复

在夹具内部增加仅测试使用的故障注入缝隙：在真实 CREATE 已执行、但成功状态尚未记录时抛合成异常。它模拟该结果窗口，不宣称网络失 ACK 已被端到端复现。新测试均写入既有 `DamengMigrationScriptFunctionalTests`，使用 `[Trait("Plan", "014")]`：

- `CreateResponseFailureReconcilesExactOwnedObjects`：Theory，分别在 TABLESPACE / USER CREATE 后注入；断言初始化仍抛原故障标识，独立新会话确认两个精确对象不存在，CREATE 每个最多发送一次。
- `RecoveryPreservesUnrelatedObjects`：单独夹具创建对照对象；恢复故障夹具后核对对照仍存在并可使用，再在 finally 精确清理对照。
- `RecoveryFailureRetainsPrivateJournal`：真实创建配合可控恢复查询失败缝隙，确认记录保留；解除缝隙后由同一已知所有权记录恢复并在 finally 核对不存在。测试不得故意留下真实资源。

所有权不匹配、无权限目录等危险分支使用纯测试模拟，不能在真实环境冒用别人的对象。真实用例的外层 finally 必须保有准确名称，即使断言失败也可安全处理；若恢复仍未证实，停止后续管理员通道并报告 BLOCKED。

**验证**：依次运行官方聚焦、W 聚焦、官方完整 admin、W 完整 admin。每次完成后记录相应 TRX、清理核对和 profile；不可并行运行两驱动或其他共享数据库通道。

### 5. 写明恢复契约与收尾

在本地测试 README 说明私有恢复文件位置、`cleanup_required` 的含义和管理员必须核对的精确对象信息。未知所有权只允许人工调查，不能提供按前缀批量删除命令。兼容矩阵仅记录夹具证据，不能扩大产品的失败重试保证。

**验证**：`git diff --check` 退出 0；`git diff --name-only` 与白名单一致；`scripts/local-test/run.sh selftest` 通过。报告测试结果与未消除的不确定状态，不打印记录正文。

## 完成标准

- [ ] 发送任何 CREATE 前已保存可用的所有权记录；记录失败时发送计数为 0。
- [ ] 未知结果不重试 CREATE；缺少所有权或目录证据时 DROP 计数为 0。
- [ ] 两驱动离线状态测试均通过并纳入 004 发现自检；无数据库 Collection/真实凭据依赖。
- [ ] 至少上述四个管理员场景在两驱动聚焦及完整 admin 通道通过，故障源未被掩盖。
- [ ] 所有新真实对象在 finally 后独立确认不存在；无 unresolved 恢复记录。
- [ ] 记录、日志与异常使用合成敏感哨兵检查通过，权限验证通过，文档和范围检查通过。

## 停止条件

- 缺管理员连接、CREATE/目录/DROP 权限或所需 SDK：BLOCKED，不提权、不跳过验收后标 DONE。
- 无法证明所有权、发现已有同名对象、数据文件关系不符、目录结果未知：保留私有记录并停止删除。
- 故障注入无法做到真实 CREATE 之后、状态确认之前，或只能通过真实断网/改服务器实现：停止重新设计，不谎称覆盖失 ACK。
- 所需修复超出白名单，或必须改变全局配置/持久用户/驱动重试政策：交回协调者。
- 合理修正后同一门禁连续失败两次，或任何自有残留未解决：停止后续数据库工作并报告准确状态。

## 维护提示

添加新的管理员对象类型时必须同步所有权记录、依赖顺序和未知结果恢复测试。尤其关注 HUGE 路径与默认索引表空间：只有真实证明它们属于当前临时夹具，才纳入其清理；不能把现有成功布尔标记当作对未知结果的证明。

## 批次交付与统一证据

本计划属于 M5，启动实施前须确认 M4 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/014.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为确认问题或验证缺口，须以“已修复并验证”闭合；补足有辨别力的测试证据也属于验证缺口的修复。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M5.md` 的输入。
