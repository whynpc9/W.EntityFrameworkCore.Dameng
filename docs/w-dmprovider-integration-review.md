# W.DamengProvider 接入计划审查记录

初始审查：2026-10-08；版本更新记录：2026-10-13。关联[实施计划](w-dmprovider-integration-plan.md)。

## 发布准备复核（2026-10-16）

本轮通过 GitHub 在线读取已合并 PR 的 reviews、checks 和全部 review threads：

| PR | 最终 head / 合并提交 | 审查闭合 |
| --- | --- | --- |
| [#7](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/7) | `6279bcb` / `d4df636` | 2 条 threads 均 resolved；最终 head 的 Cursor review 为 No findings，check 为 SUCCESS；Codex 初轮 TIME 桥接意见已修复 |
| [#8](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/8) | `59370af` / `b3e8da3` | 3 条 threads 均 resolved；最终 head 的 Cursor review 为 No findings，check 为 SUCCESS；Codex 中继意见已修复 |

Codex 的审查评论对应较早提交，不将其当成最终 head 上新生成的审查结果。
下面的“尚未完成复审”等措辞保留为各次执行时的历史记录；本轮发布准备与验收见
[0.4.0 发布准备](release-0.4.0.md)。本轮本地依赖及工作流变更不在上述旧 PR 审查范围内。

## PR #7 代码独立复审（2026-10-14）

用户在修复与合并任务中明确指定沿用 Codex / Cursor 复审，替代原定 Kimi Code。
两者通过 GitHub 集成独立读取 PR diff；未披露具体模型 ID，没有 T3 委派 taskId。
初轮均审查 `abc5c292449cf94a69583353dd63cfa4f972b515`，结论不覆盖后续修复提交。

| 审查方 | 结论与出处 | 处置 |
| --- | --- | --- |
| Codex（`chatgpt-codex-connector`） | [P2：TimeSpan TIME(n) 未经过驱动桥接](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/7#discussion_r4270910190) | 将 TimeSpan TIME 转为带驱动的 TimeOnly 映射，参数与读取均经转换器，分面克隆保留驱动；W 将小数秒位数转到 Scale、清零 Precision，NULL 同样配置；拒绝负值、超过一天的值与参数/字面量精度损失；显式裸 TIME 按 0 位处理，TimeOnly 同样修正 |
| Cursor（`cursor`） | [P1：共享服务注册遗漏 DateTime 翻译 visitor](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/7#discussion_r4271088276) | 将 main 新增的 visitor 工厂注册到共享桥接入口，保留驱动专用 DatabaseProvider；两种通道验证 nullable 与 boxed DateTime 的翻译边界 |

同时合并 main 的反向工程补足与 DateTime 翻译：保留 CLI 最终模型断言，更新新增测试的驱动入口、工厂构造和共享聚合用例引用。
目录命令计数使用仅限测试的 RecordingDriver 识别 CatalogQueryRecorder，生产驱动继续拒绝包装连接。
当前修复的运行证据见[验证记录](w-dmprovider-validation.md)。修复提交推送后重新请求复审；此处尚不宣称最终复审通过。

## 执行修订记录（2026-10-14，非独立复审）

用户要求先审阅、按需修订计划，然后执行。本节记录主代理的修订；它不是独立复审，也不覆盖下文任何审查结论。

- 修订前计划 SHA-256：`35e1bc853a7d2c6fb71ed19fc39feabd55104aa6de38bfa59cfaa0a39d3fcc6b`（即 `6238792` 提交版本）。
- 修订后计划 SHA-256：`9ff76a934cb99c4b3b2d85a095e0a1971552beadf7911de197f1b31c6b1857b9`。
- 复核依据：本仓库 `6238792`、W 上游 `b5786d5`（相对 preview.1 tag 的 `src`/`tests` 无差异）、重新下载的公开包与 DLL hash、本地实库只读探测（服务器自报 `8.1.5.60`、UTF-8）。

| 修订 | 计划位置 | 理由 |
| --- | --- | --- |
| 刷新执行前基线 | §1.1 | 上游 main 已前进，但驱动源码与 tag 相同；本地实库正好是 W 已验证 profile，真实库门可执行 |
| 固定开放的 API/命名/测试通道决策 | §2.5 | 原计划多处写“拟名/T01 固化”；实现前固定，避免编码中反复改设计 |
| T01 不再建可丢弃原型 | §4 T01 行 | 原型与正式实现的验证点相同，丢弃原型只会重复工作；T01 门改为正式代码中的单元测试 |
| 候选还原改为公开包 + 锁文件 + manifest 核验 | §5.3 | preview.1 已公开签名，锁文件 contentHash 已固定字节；自建 feed 只在未发行候选时才有必要 |
| 独立复审移到 M1 代码 diff | §8 | 在代码上复审比在设计稿上复审更能发现实际问题；未复审前不合并、不宣称验收 |
| 明确本轮范围 | §10 | 执行 M1 到开发候选；不发布、不推送标签；M2/T11 后续进行 |

## 最新版本更新记录（2026-10-13，非独立复审）

依据用户要求，以公开仓库 `whynpc9/W.DamengProvider` 最新版本更新计划，并提交、推送两份文档。本节是主代理核查及修订记录；下面 2026-10-08 的 Kimi CLI 原文、意见响应、hash 和失败委派记录作为历史保留，不将其结论移用于新版。

- 更新前计划 SHA-256：`b043dc92f5c2d738a762205d9d7a295c2272fab959efe03ec83b43cf9a3255cc`。
- 本次更新后计划 SHA-256：`35e1bc853a7d2c6fb71ed19fc39feabd55104aa6de38bfa59cfaa0a39d3fcc6b`。
- 本仓库产品基线：`5863d3755dbd2d333ac359bf1c0a789762adbaed`；本轮仅改计划/审查记录。
- 最新驱动：`W.DamengProvider 0.1.0-preview.1`；main、tag 与 NuGet nuspec repository commit 为 `a67a9298daaa42b18203a9b405ec05a3357ed4b0`。
- 实际核查：GitHub metadata/tag/CI、固定提交源码、公开 NuGet 包及发布 CI artifact；静态读取包/DLL SHA 与 MVID，逐条比较包内容。公开包仅多签名条目，共同文件逐字节一致。精确身份和不可变引用见计划§1.1、§9。
- 未执行下载所得程序集，未运行 dotnet 构建/测试、数据库、包签名密码学验证或新一轮 Kimi review。上游测试数字均标为报告的历史结果，没有改写为本次通过。

| 更新项 | 计划位置 | 本次处理 |
| --- | --- | --- |
| 新包及公开来源 | §1.1、§2.1、§5.3 | 固定 preview.1、公开签名包/DLL/MVID与CI构件；依赖与source mapping改新PackageId，保留namespace和诊断源旧名称；T00不再等待未知包来源，仍需实现复现与身份审计。 |
| 时间精度 | §1.3、§2.4、§3.2、T01/T03/T04/T06 | M1新增时间Precision→Scale桥接、typed reader、七位profile及拒绝合同；明确现有默认schema已声明七位，不自动更改schema；加入6→7保值和7→6多列guard回归。 |
| 错误后的会话恢复 | §3.3、T05、§5.1 | 按严格回执/profile/事务状态验证同会话继续执行；-6804正例、-3510负例；恢复不等于重试，原Unknown保护保留。 |
| 触发器及参数绑定 | §3.3、T04/T06 | 纳入零参数:NEW/:OLD与完整并发回读；有参数严格绑定、未绑定标记服务器拒绝仍需回归；不新增SQL能力声明。 |
| 上游证据限度 | §1.2、§5.2、§9 | 区分v23、temporal、EFCompat D与公开包；219/234和230/234不拼成通过，权限/admin/CLI缺口仍入本项目矩阵；处理归档链接与过时发布文字。 |
| 审查与交付 | T01、§8 | 原8项处置保留。本次改动涉及新的参数/reader语义，T01定稿需独立复审后进入产品实现；不把旧无P1结论当新稿验收。用户已追加授权文档提交推送。 |

本轮文档校验覆盖相对链接、固定上游源码链接路径、任务编号、当前计划hash及空白；当前支持矩阵、产品代码、依赖和锁文件未修改。此记录不代表已启动T00产品实施或完成新版EF实库验收。

## 计划方处理状态（2026-10-08）

用户已将下面的 Kimi Code CLI review 作为本轮输入提供。计划方逐项核查并完成 **4 项 P2、4 项 P3** 的文档处置；原始发现及两次失败委派历史继续保留。实际审查形态是CLI交互会话，不补写T3 taskId，也不把历史失败任务改为成功。

- 首轮被审计划：`3587d3fd3356694b186cf0caca94b385a8a2ae39946993670aa8efa808bbc7ae`。
- 意见处置后计划 SHA-256：`b043dc92f5c2d738a762205d9d7a295c2272fab959efe03ec83b43cf9a3255cc`。
- 当前结论：计划层面的8项意见已处置，无遗留的文档修订项；Kimi的“无P1”结论仅对应首轮被审版本。本修订稿由主代理静态核对，未声称已获新一轮独立复审或产品验收。
- 后续门：按计划§8，无新增阻断项且属于本轮意见的澄清/补齐，T00的首轮审查前提已满足。T00候选取得、真实环境及T01可执行原型等门仍需后续实施完成；本轮没有启动它们。

| 发现 | 处置 | 计划修改位置与具体结果 |
| --- | --- | --- |
| P2-1 上游基线推进 | 采纳更新，校正漂移含义 | §1.1更新到`fe89bd4`，T00增加来源/API复核；实际`git diff 824a944 HEAD`为空，两提交tree均为`2818afa547c4da07bb294c55722718887df89c08`。因此没有本次合并导致的源码/API变化。原v23精确包可保留原证据；重新构建仍必须另立身份和验收。§6明确复用已有DataSource/LOB公开API，只新增EF生命周期衔接。 |
| P2-2 重试入口语义 | 采纳并补齐配置顺序 | §3.1明确W下所有EnableRetryOnFailure重载配置期拒绝，固定异常类型及错误标识；覆盖空集合、直接构造自带strategy、先重试后驱动选择等路径。T01固定descriptor可见性，T03/T05验证。用户ExecutionStrategy自定义策略的责任与默认单次执行保证分开。 |
| P2-3 EF提供程序错配 | 采纳 | §2.2/§2.3明确descriptor感知的Name/IsConfigured；增加首次初始化及复用的singleton等价校验。两方向手工service provider错配、冲突注册及顺序互换在T01即为可执行门，不能依赖默认扩展类型存在性判断。 |
| P2-4 候选还原来源 | 采纳 | §5.3固定待实现的prepare-feed入口、不可变producer/artifact坐标和manifest、忽略目录feed、包/DLL核验、显式NuGet.Config/source mapping、隔离NUGET_PACKAGES、锁文件更新与冻结顺序。T00来源未明确不能完成，CI/审查者从同一构件复现，T04包含来源/缓存负例。 |
| P3-1 默认参数类型 | 采纳 | §2.2/§5.1补充JSON等默认provider类型路径，检查默认值经DbType/Value设置后的实际绑定、Clone、NULL与重复绑定；存在差异须显式适配或拒绝，不因默认初值相同就认为运行时等价。 |
| P3-2 文档清单 | 采纳并限定当前事实 | T07/§4.1纳入architecture、AGENTS、测试指南与工作流。AGENTS异步回退需按驱动限定；当前两驱动仍无DbBatch，不把它误写成已解锁。当前仅修改计划清单，尚未改动产品支持文档。 |
| P3-3 tag误触发布 | 采纳 | §8固定`v<SemVer>`仅基础包、`wdriver-v<SemVer>`仅W扩展包；候选构建不打发行tag。增加标签→项目→PackageId→版本→包数量的一致性和负例门，涵盖推包/Release两条路径，W依赖基础包先可用。 |
| P3-4 日志区分驱动 | 采纳 | §2.2/§5.1要求LogFragment与DebugInfo都有稳定driver标识并做敏感哨兵测试；T01完成门前移，禁止连接串/数据源ToString进入诊断标签。 |

同时处理审查备注：§5.1将 `DamengIsolationLevelFunctionalTests` 明确列为按driver/profile区分预期的测试，历史数量由T00重新枚举和执行冻结；计划头部已更新审查状态。

核查依据包括当前官方options builder/DI/JSON mapping/测试、W公开DataSource/Reader/Parameter实现、本地EF10.0.12 XML契约、AGENTS、architecture及发布workflow。主代理只做静态读取与文档检查，未运行构建、数据库测试、网络查询或发布。

## 首轮独立审查结论（原文，对应修订前计划）

独立审查**已完成**（第一轮）。结论：**未发现 P1 阻断项**；4 项 P2、4 项 P3 见下文发现明细。计划中的架构前提与仓库现状断言经逐项抽查全部属实。建议计划方将 P2 修订并入计划后再启动 T00；因无阻断项，修订后是否需要再次完整复审由计划所有方按第 8 节协议决定。

注意：本次审查由 Kimi Code CLI **交互式主会话**执行，并非原先两次失败的 T3 `delegate_task` 委派通道（无 clientRequestId/taskId 可提供）。执行者与原计划指定的 Kimi Code 系审查一致，但会话形态不同，是否满足“用户指定的 Kimi Code 独立 review”的意图由用户判定。历史委派证据保留在文末。

审查元数据：

- 所审计划 SHA-256：`3587d3fd3356694b186cf0caca94b385a8a2ae39946993670aa8efa808bbc7ae`（若计划修订，复审必须读取新文件并记录新 hash，不能沿用本结论）。
- 本仓库基线：`5863d3755dbd2d333ac359bf1c0a789762adbaed`（与计划 §1.1 一致；两个计划文档为未跟踪新文件，符合预期）。
- 审查方式：只读静态核查。通读计划全文；与本仓库源码、配置、文档逐项交叉验证；对相邻 W 仓库仅执行只读 `git log`/`git status`。未运行构建、测试、数据库或网络访问，未读取 secrets，未修改计划文档。

## 计划断言核查结果（抽查全部属实）

| 计划断言 | 仓库证据 | 结论 |
| --- | --- | --- |
| §1.1 版本范围：`DM.DmProvider [8.3.1.47463,8.4.0)`、EF `[10.0.12,11.0.0)`、SDK `10.0.401`/`latestPatch` | `Directory.Packages.props:7-8`、`global.json` | 准确 |
| §2.2 “现有仅以 `other is ExtensionInfo` 判断等价” | `DamengOptionsExtension.cs:51-52` 确为该实现；`PopulateDebugInfo` 只写单一固定键 | 准确 |
| §2.2 共享实现多为 internal、需跨程序集桥接 | `DamengRelationalConnection`、`DamengTypeMappingSource`、`DamengDatabaseModelFactory` 等均为 internal；`AddEntityFrameworkDameng` 在单程序集内注册 | 准确 |
| §2.2 桥接四条范围覆盖现有耦合 | src 内驱动类型耦合恰好 8 个文件：3 个类型映射（`DmSqlType` 的 Clob/Blob/Binary/VarBinary/IntervalDayToSecond）、`DamengRelationalConnection`、`DamengDatabaseModelFactory`、`DamengTransientExceptionDetector`、`DamengRetryingExecutionStrategy`、`DamengDbContextOptionsBuilder`；无 `DmDataReader`/`DmCommand`/`DmTransaction` 使用 | 准确，M1 桥接范围完整 |
| §2.1 `UseDameng(DbConnection)` 收紧属兼容性变化 | `DamengDbContextOptionsBuilderExtensions.cs:61-78` 当前无任何连接类型校验 | 准确 |
| §2.3 官方 ProviderName 即程序集名 | `IDatabaseProvider` 注册为 `DatabaseProvider<DamengOptionsExtension>`，Name 解析为 `W.EntityFrameworkCore.Dameng` | 准确 |
| §2.3 设计时链路需 W 自有入口 | `DesignTimeProviderServicesAttribute` 挂在基础程序集；`DamengCodeGenerator` 硬编码 `UseDameng` 的 MethodInfo | 准确 |
| §2.3 `Create(string)` 直接 new 官方连接 | `DamengDatabaseModelFactory.cs:49` `new DmConnection(connectionString)` | 准确 |
| §4.1 关键文件清单 | 全部存在；`SingularModificationCommandBatch` 经 `DamengModificationCommandBatchFactory.cs:13` 使用 | 准确 |
| §5.3 启动器与环境变量现状 | `scripts/local-test/run.sh` 已设独立 DOTNET_CLI_HOME/遥测关闭并透传参数；`test <unit|functional|specification|admin|probes|all>` 通道与 README 一致 | 准确 |
| §8 “现有 tag 工作流只打包官方项目” | `publish-nuget.yml`：`v*` 标签 → 锁定还原、Release 构建、**仅单元测试**、打包单个项目并断言恰好 1 个 nupkg、推送 NuGet.org、创建 GitHub Release | 准确 |
| §1.2 基线能力（幂等脚本、反向工程、保存点、ExecuteUpdate/Delete） | `docs/compatibility.md` 对应行均为真实环境已验证/部分支持 | 准确 |
| §9 引用文档 | `stage3-pre-driver-plan.md`、`scripts/local-test/README.md`、两个 skill 均存在 | 准确 |

补充核查备注（不构成发现）：

- `docs/compatibility.md:59` 记录官方驱动下 RU/Serializable 的跟踪式 `SaveChanges` 以 `CommandText has no value` 失败。W 模式在同一能力上声称“仅 8.1.5.60 自报 profile 成功”，两驱动在此存在**有意的行为差异**。§2.2“M1 两驱动 SQL/模型语义相同”不受影响（这是事务能力而非 SQL/模型语义），但 §5.1 的逐项映射应把 `DamengIsolationLevelFunctionalTests` 类用例显式标为按驱动断言，避免被“语义相同”笼统覆盖。
- `compatibility.md` 2026-10-04 快照为单元 682、功能 232、规范 4、管理员 7；T00 冻结时以实际重跑计数为准。

## 发现明细

### P2-1 W 上游源码已漂移，且落在 M2 设计范围

计划 §1.1 记录 W 源码为 `824a944`（开始规划时工作树干净）。本次审查时该仓库 HEAD 为 `fe89bd4`（“Merge pull request #2 from whynpc9/codex/r3-datasource-streaming”），即 `824a944` 的直接后继合并，工作树干净。合入主题正是计划 M2（T08/T09、§6）的设计对象（datasource/streaming）。

计划本身已要求“实施启动重新读取上述状态”，因此这不是计划缺陷，而是该门必要性的实证——门已触发。建议：

- T00 重新基线时，除重建候选身份清单外，显式复核 §6 的 DataSource 所有权与 LOB 回调约束是否仍匹配 `fe89bd4` 的公开 API；若上游已提供相近设施，应说明是复用还是另建。
- v23 候选身份（包/DLL SHA-256、MVID）仅对应旧代码；任何基于 `fe89bd4` 的构建都是新候选，不能沿用 v23 证据（计划 §1.1 已有此规则，此处确认其即将适用）。

依据：相邻仓库只读 `git log`/`git status`。建议修改位置：§1.1 输入表、T00 完成门。

### P2-2 W 模式下 `EnableRetryOnFailure` 的行为未定义

§3.1 写“调用已有 EnableRetryOnFailure 不能获得绕过保护的能力，文档说明该候选没有已验收的自动重试分类”，未明确两种读法：(a) 配置期显式失败；(b) 仍可调用但重试不发生、仅文档说明。(b) 会让用户误以为有重试保护，与全文“明确拒绝不支持、不静默改变语义”的立场不一致。

建议：明确为 (a)——W 配置下所有 `EnableRetryOnFailure` 重载（含无参与 `errorNumbersToAdd`）在配置期抛出可识别异常并给出指引；同时说明用户经 `ExecutionStrategy(...)` 注入自定义策略的路径责任自负（EF 标准扩展点，不属于保护承诺）。注意实现约束：`DamengDbContextOptionsBuilder` 与 `DamengRetryingExecutionStrategy` 是基础包公共 API，拒绝逻辑需要能读到当前 options 的驱动 descriptor，这把 descriptor 的可见性要求提前到了 T01 的 API 设计。

建议修改位置：§3.1、§2.2。

### P2-3 共享 `DamengOptionsExtension` 使 EF 内建提供程序错配检测失效

两驱动共用同一扩展类型后，`DatabaseProvider<DamengOptionsExtension>.IsConfigured` 对 W options 同样返回 true（其实现只查扩展类型存在性）。后果：手工 `UseInternalServiceProvider` 只注册官方服务时，W options 会被官方服务**静默接受**；EF 的“multiple providers configured”等诊断也会失真。§2.2 要求“Validate 阶段失败”方向正确，但 EF 默认机制不会自动产生该失败，朴素实现会漏。

建议 T01 原型必须包含可执行证明：每个包注册 descriptor 感知的 `IDatabaseProvider` 实现，并以单例 options 校验（`ISingletonOptions.Validate` 或等价机制）在错配时抛出可识别异常；T01 完成门中的“两驱动并存/服务缓存”证明应覆盖此手工错配用例（目前该用例只在 §5.1 测试矩阵的 DI/缓存行，建议前移到 T01 门）。

连带实现备注：W 包不能复用 `DatabaseProvider<DamengOptionsExtension>`——其 Name 由扩展类型所在程序集解析，会得到基础程序集名而非 `W.EntityFrameworkCore.Dameng.WDriver`。§2.3“通过对应 `IDatabaseProvider` 实现声明”已隐含此点，建议写明，避免 T03 实现时走错捷径。

建议修改位置：§2.2（手工内部 provider 段）、§2.3、T01 完成门。

### P2-4 W 精确 nupkg 的还原来源未定义

T03/T04 起 `--locked-mode` 还原引入新的精确版本依赖；锁文件只记录哈希，不解决**来源**。精确 nupkg 放在哪里（仓库外目录 feed、`NuGet.Config` 源、生产者构件目录）、CI 与审查者如何获得同一包、是否允许随库携带二进制，均未规定。T00 完成门的“精确包可获取”应落为仓库级机制（不改产品、不进 secrets、可重现）。

建议修改位置：T00 完成门、§5.3。

### P3-1 JSON 等参数依赖驱动默认 provider 类型，桥接契约未立规则

桥接契约第 2 条枚举 BLOB/CLOB/INTERVAL/二进制，但 `DamengJsonTypeMapping` 不覆盖 `ConfigureParameter`：JSON 字符串参数依赖驱动默认 provider 类型（官方默认 `DmDbType.VarChar`，见 `DamengTypeMappingTests.cs:712`）。建议契约补一条“未显式设置时依赖驱动默认类型”的规则，W 适配器需证明其默认与官方语义一致，而不是等运行时漂移。

建议修改位置：§2.2 第 2 条。

### P3-2 文档修订清单遗漏 `architecture.md` 与 `AGENTS.md`

T07 产出列出 README/compatibility/配置指南。双驱动落地后，`docs/architecture.md` 需更新；`AGENTS.md` 的“提供程序不变量”中若干陈述将变成按驱动成立（如无专用 `DbBatch`、保存点行为、隔离级别差异、异步回退），按仓库规则应同步修订。建议列入 T07 产出。

建议修改位置：§4 任务表 T07 行。

### P3-3 发布 tag 命名空间与防误触方向

`publish-nuget.yml` 对任何 `v*` 标签自动打包并发布官方包（断言恰好 1 个 nupkg）。双包后需要 per-package 标签方案与打包/断言改造；改造完成前，W 开发期间误推任何 `v*` 标签都会触发一次官方包公开发布。§8“防止误触公开发布的门”建议显式覆盖这个方向（限定标签前缀、校验包名/数量与标签的对应关系）。

建议修改位置：§8。

### P3-4 EF 日志中的驱动可辨识性

双驱动同进程时，options 的 `LogFragment` 目前固定为 `UsingDameng `（`DamengOptionsExtension.cs:46-49`）。§2.2 已要求 DebugInfo 包含驱动身份，建议同样覆盖 LogFragment，使应用日志/诊断能区分驱动。

建议修改位置：§2.2。

## 首轮提出的未决项（历史，当前处置见文首）

1. 上述 P2-1～P2-4、P3-1～P3-4 待计划方逐项响应与处置（按第 8 节协议记录依据、响应、修改位置）。
2. 计划文档头部第 3 行“独立审查未完成”的状态描述已过时，应由计划方在吸收本审查后更新（审查方不修改被审文档）。
3. 计划 T00 前置“本计划审查完成”：本次审查完成后该项可满足；建议 P2 修订并入后再启动 T00。
4. 若计划修订导致内容变化，任何复审必须读取新文件并记录新 hash，不得沿用本结论于新文档。

## 历史：此前未完成的委派尝试

以下记录保留自修订前版本，供审计；与本次已完成的审查无关。

详细计划及主代理静态自检完成后，用户指定的 Kimi Code 独立 review 曾两次经 T3 委派均以 `failed` 结束，返回的唯一原因是 `Provider turn failed.`，没有模型审查意见，也没有可采纳或关闭的发现。

T3 实时模型目录没有独立名为 Kimi Code 的 provider；可委派的对应模型是 Cursor provider 中的 `kimi-k2.7-code`（标签 Kimi K2.7 Code）。两次均实际选择此模型，没有改用其他模型替代独立审查。

委派证据：

- 委派接口：T3 `delegate_task`，role=`review`，异步子任务，属于当时会话；未创建普通顶层会话。
- providerInstanceId：`cursor`。
- model：`kimi-k2.7-code`。
- 初稿 SHA-256：`05cfef4a83d26eced83b927292ff51e1c8acdac9dc8fad7fc340ebbf1fc4ec2c`（注意：此为初稿 hash，与本次审查的 `3587d3fd…` 不是同一版本）。
- 两次审查目标均为上述同一初稿；第二次包括完整原始 brief、上一轮失败状态及“未产生意见”的说明。
- 限定只读：不得编辑文件、运行构建/测试/数据库/网络、读取 secrets、读取反编译样本或派生子任务。

| 尝试 | clientRequestId | taskId | 结果 |
| --- | --- | --- | --- |
| 首次 | `wdm-plan-kimi-r1-20261008` | `node:delegated-task:command%3Amcp%3Afa6fbeb5-f208-4765-9e2a-05fe74455e74%3Adelegate-task%3Awdm-plan-kimi-r1-20261008` | failed / Provider turn failed. |
| 基础设施失败后重试 | `wdm-plan-kimi-r1-retry-20261008` | `node:delegated-task:command%3Amcp%3Afa6fbeb5-f208-4765-9e2a-05fe74455e74%3Adelegate-task%3Awdm-plan-kimi-r1-retry-20261008` | failed / Provider turn failed. |

首轮读取了子任务 activity，仍只有通用 provider 错误，没有鉴权、额度、模型不存在等细分原因；本记录不推测根因。两次 task_status 均为终态且无 pending 子运行，不存在仍在后台进行的 review。

## 历史：主代理自检与修订

以下不是 Kimi 意见，也不替代独立审查：

1. 补充跨程序集可访问性合同。现有共享服务多为 internal，计划要求基础包导出最小桥接/设计时注册入口；W 扩展包不能直接访问 internal 实现，也不复制整套实现。
2. 修正 T03/T04 执行门的先后关系。T03 只要求 W 基础编译与离线单元，实库合同执行归 T04 新启动器完成后；避免使用尚未实现的 W 测试通道。
3. 明确 T01 是可丢弃的双包最小原型，用来验证服务、缓存、设计时发现和 API，不将其作为 T03 产品完成证据。
4. 核查计划相对文档链接、任务编号、关键现有文件路径和空白。没有运行 dotnet 或数据库测试；该轮没有产品/依赖/锁文件变更。

以上自检针对初稿；本次独立审查以修订后文件（`3587d3fd…`）为准。
