# W.DamengProvider 双驱动接入实施计划

初稿：2026-10-08；基线核查与修订：2026-10-13；执行修订：2026-10-14。状态：以公开 `W.DamengProvider 0.1.0-preview.1` 为接入目标，2026-10-14 起按 §10 进入 M1 实施。Kimi Code CLI 首轮审查的 4 项 P2、4 项 P3 已处置；10-13 版本更新与 10-14 执行修订均未经过新一轮独立复审，旧“无 P1”结论不覆盖新稿。版本变化与审查边界见[审查记录](w-dmprovider-integration-review.md)，实施证据见[验证记录](w-dmprovider-validation.md)。支持声明以兼容矩阵和验证记录为准，本计划本身不构成验收。

## 1. 目标、基线与完成定义

在现有官方驱动版本之外，增加显式选择 W.DamengProvider 的 EF Core 实现版本，复用查询翻译、关系模型、SQL、迁移与反向工程逻辑。首先交付异步网络调用、取消/超时、故障结果识别、时间参数与 100ns 精度适配及已验证隔离级别的保存路径，再扩展数据源与 LOB 使用方式。

10-13 轮只交付计划及审查记录。10-14 起实施 M1：依赖变更只限新增 W 接入所必需的引用，保持现有 EF Core 10 与官方驱动版本范围；不发布产品、不推送发行标签。

### 1.1 已核查的输入

| 输入 | 当前快照 | 可用于什么结论 |
| --- | --- | --- |
| 本仓库 | `5863d37`，开始规划时工作树干净 | 当前产品和完整测试集的接入基线 |
| 官方驱动 | `DM.DmProvider [8.3.1.47463,8.4.0)`；实际还原版本由锁文件确定 | 官方版本回归基线，不因 W 接入升级 |
| EF / SDK | EF 包范围 `[10.0.12,11.0.0)`；SDK `10.0.401`、`latestPatch` | 实施时同时记录锁定包版本与实际 SDK patch |
| W 公开源码 / tag | `whynpc9/W.DamengProvider`；main 与 `v0.1.0-preview.1` 均为 `a67a9298daaa42b18203a9b405ec05a3357ed4b0` | 本次以固定提交读取实现，不依赖旧本地 checkout |
| W 精确依赖 | NuGet `W.DamengProvider 0.1.0-preview.1`；目标 `net10.0`，无额外运行时 NuGet 依赖 | 已从公开 flat-container 下载并读回 nuspec，版本可取得；仍为预览版 |
| NuGet 原始包 SHA-256 | `03e7f9161b2d120e29f8e267d25dc62cef3e4f94ba8c032cb774449dcfec07ba` | 后续还原和归档使用这份公开签名包身份 |
| 产品 DLL SHA-256 / MVID | `a265cd965ee8dde007d5198a283289a34b3e8516f7e8310f2d43b9a37cd3ab1d` / `d1773de0-4205-48eb-84a6-0708c9c4aec4` | 由包内 `lib/net10.0/W.DamengProvider.dll` 静态读取；尚无本项目运行时加载证明 |
| 发布 CI 构件 | run `38339061188`，artifact `11786486904`（`tagged-nuget-validation`）；CI nupkg SHA `71371d9de32a788950c0463f9ba511069bcde6fae21afbccc6e8006b5d015400` | 本次下载比较：公开包仅多 `.signature.p7s`，所有共同条目逐字节一致；DLL 与四份语言资源均一致。包签名密码学验证尚未执行 |
| 来源与发布状态 | nuspec `repository commit` 为上述 `a67a929…`；发布 workflow 和该提交普通 CI 均 success；GitHub Releases 列表为空 | NuGet 可用已证实，不要求 GitHub Release 才算可下载；CI 不替代本项目实库验收 |

本轮仅下载源码/包/构件、读取元数据并做静态比较，没有执行下载的程序集、构建、包安装或数据库调用。MVID 读回不等于测试进程加载证明。T00 仍需落地身份 manifest、可重现还原、当前测试清单和环境清单；“包可获取”这一事实已确认，不再把它列为未知阻塞。

2026-10-14 执行前复核：

| 输入 | 复核结果 | 影响 |
| --- | --- | --- |
| 本仓库 | `6238792`，相对 `5863d37` 仅增加两份计划文档 | 产品/测试基线不变，T00 以 `6238792` 重新枚举测试 |
| W 上游 | main 前进到 `b5786d57836e8aa72f4400cbae1013070e44914f`；`git diff a67a929 b5786d5 -- src tests` 为空，40 个变化文件只涉及文档/工具 | 驱动产品源码与 preview.1 tag 相同，不改变接入目标 |
| 公开包 | 从精确版本 URL 重新下载：包 SHA-256、DLL SHA-256 与 §1.1 一致 | T00 manifest 以此为准 |
| 本地实库 profile | 服务器自报 `8.1.5.60`，全局字符集 UTF-8；W 连接需显式 `transport_security=PlaintextAllowed` | 与 W 已验证的七位时间、RU/Serializable、保存点、错误恢复 profile 一致；本地无 TLS profile，TLS 门保持 pending |

旧基线仅供追溯：本地 `824a944 → fe89bd4` 为相同 tree；v23 `W.DmProvider 0.1.0-r3.20261007143838` 的包 SHA 为 `6696a26d89e4caa1b9e7c40a73f024c4df9d8a695da1446bdbecc17757d3bcf9`，DLL SHA/MVID 为 `c70f7d4b7bd37187f55483d186d658206232a2e1f60a662530b3a0282e3231e5` / `69e5632e-75c4-4672-b924-57b8ffb58a52`，下游固定 EF `113014cc74dd1f751ef97226a78d2ec855b32c8c`。其 `production_release_accepted=false` 是历史候选状态，不能覆盖新版已公开发行的事实，也不能把旧 76 项下游通过改记为新版通过。

### 1.2 相对旧计划的变化与证据层级

| 变化 | 对本计划的影响 | 不能据此推导 |
| --- | --- | --- |
| 包/程序集更名为 `W.DamengProvider`，namespace 仍 `W.Dm` | 更新依赖、锁文件目标、DLL 审计、源码路径和 factory invariant；旧 DLL 引用须重编译 | 不是仅替换 nupkg 文件名；不能同时引入两份 W 产品程序集来保兼容 |
| .NET tick 时间编解码及精度约束 | M1 必做 EF facet→参数 Scale 桥接、W typed reader 与七位往返；T06 增加精度迁移 | 不自动解锁 TIME/INTERVAL 七位、日期算术 100ns 或任意服务器版本 |
| 有条件的语句错误恢复 | T05 新增失败后同一会话/事务继续执行合同；保留 Unknown 与重试禁令 | 可复用连接不等于可自动重试；白名单不是 EF 重试名单 |
| 无参数命令保留 `:NEW` / `:OLD` 给服务器 | T04/T06 恢复完整触发器创建、并发标记和生成值回读回归 | 不解锁存储过程/输出参数，也不扩大带参数 DDL 支持 |
| 上游 EF 兼容修复与测试补丁 | 用作差异定位，按双驱动设计迁移断言；不得整包套用旧单驱动替换 patch | 下游 patch 未自动进入本仓库，权限受限测试不算通过 |

上游报告必须分层读取：

- 发布前检查报告记载离线 `2050/2050`、包消费者/首次初始化与两环境 smoke；报告里的 v2/v3 包和 DLL hash 均不是本次 NuGet 字节身份。本次确认公开包与 tag CI 包内容对应，但未复跑上游测试，也未证明公开包已重跑那些本地实库检查。
- 时间专项的七位 SaveChanges、查询、迁移、scaffold/guarded Down 实库记录对应历史 temporal 包及独立 EF 产品快照。它提供验收用例，不能代替当前双驱动包、CLI 或 TLS 的验收。
- EFCompat 最终 D 记录为 **219/234**，另 15 项为权限失败；临时授权的 B 为 **230/234**，两轮包与权限条件不同，不能拼成全绿。D 还早于后续变基与 `-3510` 收窄、`-6804` 增量；后者有独立 16 场及离线证据，不能改写 D。
- 新上游已归档部分生成证据/旧工具，公开说明仍有历史相对链接。T00 检查需要的 manifest/patch 是否实际存在；缺原件时据当前代码独立重建合同，不引用已删除路径假装已复现。

### 1.3 能力目标

| 能力 | M1 基础接入 | M2 数据源与 LOB | 额外边界 |
| --- | --- | --- | --- |
| Open/Execute/Read/NextResult/事务异步 | 接通并验证 | 沿用 | 同步字段 getter 仍可能为 LOB 发起同步 I/O，不能把所有 EF 异步查询宣传为全程异步 |
| 取消、期限、清理 | 接通并验证 | 覆盖等待与 LOB | 发送后取消关闭会话，不能据客户端异常宣称服务端已停止或已回滚 |
| RU / Serializable SaveChanges | 验证成功及拒绝边界 | 沿用 | 当前仅服务器自报精确 `8.1.5.60`；Snapshot/RepeatableRead 仍拒绝 |
| 失败阶段和 OutcomeUnknown | 暴露稳定分类并约束重试 | 沿用 | Number 白名单不能覆盖未知结果保护 |
| DateTime / DateTimeOffset 100ns | 新增时间桥接与精确存取验收 | 沿用 | 七位写仅已验证 profile；列 scale 不足则拒绝，保留原偏移；SQL 算术另行验证 |
| 语句错误后继续使用会话 | 新增有条件恢复回归 | 沿用 | 仅实测 profile、严格回执和允许上下文；Unknown / Broken 不继续 |
| 无参数触发器 DDL | 纳入生成值/并发基线回归 | 沿用 | 修复 W 接入障碍，现有官方路径已支持，不计为 EF 新 SQL 能力 |
| W ActivitySource / Meter | 接入说明及脱敏测试 | 增加等待/LOB观测 | 名称仍为 `W.DmProvider`；诊断不含 SQL、值、连接串或凭据 |
| DbConnection 外部传入 | 支持，显式验证驱动类型/所有权 | 支持 DataSource 创建的连接 | 不能因 DbConnection 形状通用就省略参数/异常适配 |
| DmDataSource | M1 可由调用方先创建连接再传入 | 增加直接数据源配置 | 归还即销毁，物理复用率 0；不能声称握手优化 |
| BLOB/CLOB流式访问 | ADO.NET 互操作示例 | 专门的回调式访问入口 | 不将 Stream 存成普通跟踪实体属性；物化接口仍有 64 MiB 返回 payload 上限 |
| DmDecimal 高位精确映射 | 保持现有 CLR decimal | 不并入 M2 | 独立 M3 设计/验收，不能只靠类型转换器宣布查询可用 |

已有 IDENTITY/序列键、非键默认值/计算列/触发器并发标记回读、保存点、ExecuteUpdate/Delete、幂等脚本与反向工程属于需要保持的基线，不计为新增能力。

## 2. 架构决策

### 2.1 包与公共 API

首轮选择“现有包 + W 扩展包”：

- `W.EntityFrameworkCore.Dameng`：保持程序集身份和现有公共 API，内含共享关系服务、官方适配器与默认 `UseDameng(...)`。
- 新增 `W.EntityFrameworkCore.Dameng.WDriver`：依赖兼容的基础包和精确 `W.DamengProvider 0.1.0-preview.1`（NuGet 版本约束 `[0.1.0-preview.1]`），提供 `UseDamengWithWDriver(...)`、W 适配器与 W 设计时入口。包/程序集名称在 T01 固化。
- 不复制整套 SQL/迁移/翻译源码，不通过替换官方 DLL 或反射猜测类型接入。
- 这一结构会让 W 扩展包传递依赖官方驱动包；W 模式只实例化 W 连接/命令，包中存在官方资产不算官方执行回退。消费者加载证明需分别核对两种身份，不能用“官方 DLL 未被加载”作为唯一断言。
- 若未来要求 W 消费图完全不含官方驱动，另做驱动中立 Core 提取、类型转发与 API 兼容项目；该拆包不是 M1 的隐含任务。

身份映射固定为：NuGet/产品程序集 `W.DamengProvider`、CLR namespace `W.Dm`、可选 factory invariant `W.DamengProvider`、ActivitySource/Meter `W.DmProvider`。日志中的 Driver=W 仍是 EF 的稳定标签。不要全局替换所有旧字符串；测试程序集和历史证据保留其原名。依赖图拒绝同时引用旧 `W.DmProvider` 与新 `W.DamengProvider`，避免相同 namespace/type 的重复定义；官方 `Dm` 与 W `W.Dm` 共存按原架构验证。

`UseDamengWithWDriver` 镜像现有字符串、DbConnection、所有权及泛型重载。M2 才增加 DmDataSource 重载。新 API 使用 Dameng 前缀，不注册含糊的同签名 `UseDameng` 扩展方法。

同一进程可包含两种 DbContext，每个 options 只绑定一种驱动。字符串入口选择由方法决定；DbConnection 入口验证实际驱动类型，传错立即失败。同一个 options builder 混用两种驱动配置必须报明确冲突，不使用“最后一次调用赢”；同驱动重复配置遵循现有 EF 语义。已有 `UseDameng(DbConnection)` 传入非官方连接的行为收紧需作为兼容性变化记录。

### 2.2 驱动桥接与生命周期

在基础包提供最小的跨程序集桥接契约（拟名 `IDamengDriverAdapter` / `DamengDriverDescriptor`，置于明确的 Infrastructure 命名空间；T01 输出 API 清单）：

1. 使用配置创建连接、校验外部连接类型；创建命令继续使用实际连接的 ADO.NET API。
2. 为 BLOB/CLOB/INTERVAL/二进制等参数设置正确的 provider-specific 类型；`DbType`、Size、Precision、Scale、NULL、类型来源的语义均保留；时间 facet 按§2.4转换到驱动参数，不要求字段机械复制。JSON等未显式设置provider类型的路径也属于桥接合同：分别核对“新参数默认值→EF设置DbType/Value→实际绑定”的完整顺序，而非只比较new参数的初值；覆盖Clone、重复绑定与NULL。当前JSON mapping设置DbType.String，官方已有VarChar断言，W需证明最终绑定/往返语义兼容；不兼容时显式配置可表示的类型或拒绝，不能静默接受默认类型漂移，也不据此扩大JSON查询支持。
3. 分类驱动异常、识别取消/超时/未知结果及 ConnectionReusable；返回保守重试结论。会话能继续使用与语句可重放是独立判断。
4. 注册对应运行时、设计时服务并公开稳定驱动标识。

SQL 和模型服务依赖桥接契约，不再直接判断官方 `DmParameter`；类型映射的 Clone 路径必须保留驱动适配器。适配器单例无状态，不持有 DbContext、连接、事务、Reader、凭据、数据源或服务器动态能力。连接服务仍 scoped，实际配置从当前 scoped options 获取。

基础包现有 connection、type mapping、model factory 等实现多数为 internal。T01 必须固定跨程序集可调用的最小服务注册入口，由基础包内部注册这些共享实现，W扩展包只提供driver descriptor/adapter和自己的公开入口；不能让W项目直接引用不可访问的internal类。共享设计时注册入口同样由基础包提供，接受明确的driver配置及配置代码生成器。公共桥接类型的构造/方法签名、服务注册替换顺序和兼容承诺纳入API基线；不把所有内部实现批量公开。

`DamengOptionsExtension` 增加不可变驱动 descriptor。Clone、Validate、ApplyServices、Info 的服务缓存相等/hash/DebugInfo 都包含驱动身份与真正影响服务的配置；不能保留现有仅以 `other is ExtensionInfo` 判断等价的实现。LogFragment也明确输出稳定的驱动标识，例如 `UsingDameng Driver=Official` / `UsingDameng Driver=W`，不包含连接串、服务器、用户、数据源对象字符串或其他秘密；日志/DebugInfo脱敏和区分两驱动列入T01测试。不同驱动不能误用同一组 type mapping、异常或连接服务。相同驱动的不同连接串/数据源可共用无状态服务，不能把每租户连接信息放进单例或缓存键日志。

模型缓存测试覆盖：同一 DbContext CLR 类型以两种驱动创建 options、交替创建、显式 UseModel/编译模型、DbContext pooling。M1 两驱动共用 SQL/模型声明，也要证明时间参数与 reader 等运行时服务不串用；M3 增加驱动专属类型时模型及编译模型必须区分能力集。数据源实例不能通过模型缓存被捕获。

显式 `UseInternalServiceProvider` 场景若预注册驱动与 options 不符，在创建连接/构建模型前明确失败。共享扩展类型会使默认 `DatabaseProvider<DamengOptionsExtension>.IsConfigured` 无法区分驱动，不能依靠EF默认错配诊断。两包分别注册descriptor感知的 `IDatabaseProvider`，`IsConfigured`比较驱动身份；另用 `ISingletonOptions.Initialize/Validate` 或等价机制核对实际注册的adapter/provider与当前options，包含首次初始化和之后复用两个阶段。校验器仅保存稳定身份，不捕获连接或凭据。T01原型必须执行“官方服务+W options”“W服务+官方options”、相同/不同驱动重复使用、同时注册两组冲突服务及注册顺序互换用例；冲突统一失败，不让TryAdd顺序暗中选择驱动。公共descriptor须能被基础包options builder/重试API读取，T01固定其最小可见性和校验入口。

### 2.3 Provider 身份与设计时链路

- 官方 `Database.ProviderName` 保留 `W.EntityFrameworkCore.Dameng`；W 入口使用新程序集名 `W.EntityFrameworkCore.Dameng.WDriver`，便于 dotnet-ef 查找其 DesignTimeProviderServices。两者由descriptor感知的 `IDatabaseProvider` 实现显式声明Name和IsConfigured；W不能直接复用默认 `DatabaseProvider<DamengOptionsExtension>`，因为其Name仍指向扩展类型所在的基础程序集。不靠“最后加载的程序集”。
- 两驱动共享 `Dameng:` 模型注解、历史表约定和现有 SQL。新 W 设计时服务必须注册 W 适配器、正确的 IDatabaseModelFactory 连接工厂及代码生成器，生成 `UseDamengWithWDriver`。
- 现有 `DamengDatabaseModelFactory.Create(string, ...)` 不再直接 new 官方连接；`Create(DbConnection, ...)` 同样校验类型和所有权。
- 既有迁移若用 `ActiveProvider` 比较旧名字，切换后分支会不同。T06 必须包含此类迁移夹具及迁移指导；迁移 SQL 审阅后显式兼容两个身份。不能保证自动分析任意用户 C# 分支，也不能直接承诺所有旧迁移无需改动。
- 对无驱动分支的相同模型，比较生成操作、SQL、历史记录和 model snapshot，要求无非预期模式差异。ProviderName 改变不应触发无意义删表重建。

### 2.4 M1 必需的时间映射桥接

当前本仓库已默认声明 `TIMESTAMP(7)` / `DATETIME(7) WITH TIME ZONE`，但 DateTime 使用通用 `DateTimeTypeMapping(..., DbType.DateTime2)`，DateTimeOffset 通过 `GetString` + Parse 物化；这些声明不代表当前官方路径已保证 100ns。新驱动明确拒绝时间参数非零 `Precision`，只认 `Scale`，因此不能仅更换连接工厂。

T01 固定桥接合同，T03 实现：

1. EF 模型 `HasPrecision(n)` 和存储类型精度继续表达小数秒位数；官方分支保留行为。W 分支在所有基础配置完成之后，按映射的最终有效精度设置 `Scale=n`，保持时间参数 `Precision=0`。不能调用 base 后又覆盖回非零 Precision。覆盖默认、显式 store type、精度 0、NULL、Clone、缓存参数重用、查询谓词与更新参数。decimal 的 Precision/Scale 不变；INTERVAL 的天数精度和小数秒分开适配，不能机械清除模型 facet 或误用小数协议。
2. W DateTimeOffset 使用已验证的 typed getter / `GetFieldValue<DateTimeOffset>`，验证 ticks、offset、UtcTicks 与服务端文本；官方仍保留文本兼容路径。W 原始 `GetValue` 为 DateTimeOffset，与官方 DateTime 差异须按 driver 分开断言。不得经通用字符串 Parse 把不可表示的 8/9 位值舍入后交给实体。
3. 有效列/参数精度优先于值推断。上游显式 `DbType.DateTimeOffset` 默认仍为六位，不可依赖其值推断得到七位；本仓库七位映射必须显式传 Scale=7。固定 describe 为六位时不能用参数七位绕过它。值不能精确表示则拒绝，不取整、不饱和、不改值重试。
4. 无精度裸类型、DATE/TIME/INTERVAL、DateOnly/TimeOnly、DateTime Kind 及 nullable 分别冻结映射合同。默认六位 TIME/INTERVAL 不随 timestamp 七位一起放开。服务器 profile 不进入单例模型缓存；不支持 profile 由驱动明确拒绝，同一模型不得因连接服务器改变而暗中改成六位。

这不是把默认 schema 从六位自动改成七位。本仓库默认声明已有七位；已有显式六位数据库需要单独迁移。T06 验证 6→7 保留旧值、7→6 在任一待降列含非整微秒值时先拒绝（所有列 guard 在任何 ALTER 之前），DDL 禁止事务，历史表和失败后连接状态正确；回退不能丢弃第七位。M1 不自动为任意用户迁移注入 guard，提供经实库验证的迁移指导与夹具。

### 2.5 已固定的实施决策（2026-10-14）

以下决策取代 §2.1–§2.3 中“拟名/T01 固化”的开放项，T01 不再另建可丢弃原型（见 §4）：

1. **桥接 API**：基础包新增公开命名空间 `W.EntityFrameworkCore.Dameng.Infrastructure`，只放两类驱动包使用的类型：
   - `DamengDriver`：抽象类，同时充当 descriptor 和无状态 adapter。成员覆盖稳定 `Name`（`Official`/`W`）、EF `ProviderName`、创建/识别连接、设置 BLOB/CLOB/BINARY/VARBINARY/INTERVAL 参数类型、时间参数 Scale 桥接、DateTimeOffset 读取方式、是否允许内建重试、读取错误号，以及脚手架生成的 `Use...` 方法。
   - `DamengDriverConfiguration`：普通静态方法（不是扩展方法），负责以指定驱动配置 options，并注册运行时/设计时服务。
   内部实现仍为 internal，不加 `InternalsVisibleTo` 给扩展包。
2. **驱动身份**：`DamengOptionsExtension.Driver` 未显式配置时视为官方驱动。在同一 builder 上切换到另一驱动立即抛 `InvalidOperationException`，同驱动重复配置遵循现有语义。服务缓存相等/hash、DebugInfo（`Dameng:Driver`）、LogFragment（`UsingDameng Driver=<Name> `）都包含驱动名。
3. **连接类型**：两种入口都严格检查连接的实际驱动类型，包装类型（profiler 等）一律拒绝。理由：参数类型适配依赖具体驱动参数类型，包装后会静默丢失 LOB/INTERVAL 类型设置。这条收紧作为兼容性变化写入兼容矩阵。
4. **参数与模型错配**：驱动在设置 provider 专用类型时，若参数不是本驱动的类型就抛异常，不静默跳过。因此“用一个驱动构建的模型被另一驱动的上下文复用”（`UseModel`）会显式失败。
5. **DI 校验**：`DamengDatabaseProvider` 的 Name/IsConfigured 按注入的驱动决定。每次注册都追加一条驱动登记，`ISingletonOptions` 在 Initialize/Validate 两个阶段都检查：不能同时登记两种驱动，options 的驱动必须与已注册驱动一致。
6. **重试**：`EnableRetryOnFailure` 各重载在 options 上写入标记。当前驱动不支持内建重试时立即拒绝；若重试先于选择驱动配置，则在选择驱动和 `Validate` 时拒绝。直接构造 `DamengRetryingExecutionStrategy` 时同样检查驱动。异常的 `Data["Dameng:ErrorId"]` 及消息中都带 `Dameng:RetryNotSupportedForWDriver`。
7. **扩展包**：项目/包/程序集名 `W.EntityFrameworkCore.Dameng.WDriver`，根命名空间同名。扩展方法 `UseDamengWithWDriver`（字符串、DbConnection、所有权、泛型各重载）和 `AddEntityFrameworkDamengWithWDriver` 放在与基础包相同的 `Microsoft.EntityFrameworkCore` / `Microsoft.Extensions.DependencyInjection` 命名空间，便于发现。包依赖精确的 `W.DamengProvider [0.1.0-preview.1]`，并以 ProjectReference 引用基础包（打包后为同版本精确依赖）。
8. **测试通道**：新增 W 单元项目和 W 功能项目。W 功能项目以链接方式编译官方功能项目的全部源码，并定义 `WDRIVER`。共享源码通过两个项目各自的 `global using`（`Dm` / `W.Dm`）和测试环境中的 `UseDamengUnderTest`/`CreateConnection` 助手切换驱动。必须按驱动区分的断言写成显式的 `#if WDRIVER` 分支，不能删测试。W 连接设置从同一测试连接经 W typed builder 在内存中派生，`transport_security` 由启动器以非敏感参数传入。

## 3. 故障、安全默认值和能力约束

### 3.1 W 重试策略

官方模式保留既有行为并回归。W 模式初始不提供自动重放，所有 `EnableRetryOnFailure` 重载在配置期抛 `NotSupportedException`，错误带稳定标识 `Dameng:RetryNotSupportedForWDriver`，说明当前候选未开放自动重试并指向独立核对业务结果的恢复流程；包括无参、次数、错误码集合及完整参数重载，空集合也不能默默退化为单次执行。非法参数仍可先抛标准参数异常，但任意合法参数组合均不得在W模式启用重试。

`UseDamengWithWDriver`须在调用damengOptionsAction之前设置descriptor，使共享builder能立即识别W模式。直接构造 `DamengRetryingExecutionStrategy` 时同样根据context/dependencies中的descriptor拒绝W；若提供程序重试设置先于驱动选择，通过options中的明确配置标记在选择W/最终Validate时拒绝，不能靠反射识别闭包绕过顺序问题。T01固定API访问方式，T03/T05覆盖全部重载、直接构造及配置顺序，官方行为不变。

EF标准 `ExecutionStrategy(...)` 允许用户注入任意自定义策略，应用拥有该策略的重放责任；本提供程序不声称能拦截任意用户代码。默认W策略、提供程序自带入口及标准EF调用链才属于单次执行保证。故障测试分别证明默认W执行次数为1、自带重试入口配置期拒绝；自定义策略仅验证为显式扩展点，不将其产生的重放算作本提供程序保证。保留原始异常及token，业务层可以在独立连接核对最终状态后决定恢复动作。

任何后续重试开放必须同时考虑 ErrorKind、OperationOutcome、TransactionOutcome、ConnectionReusable、事务状态、流输入可重放性与应用幂等性。`NotSent` 仅描述失败子阶段，不能覆盖前面事务步骤已发送的事实。Commit 的 OutcomeUnknown 无条件禁止自动重放。

验证 EF 包装后的 DbUpdateException.InnerException、Transaction 异常和用户取消传播；禁止以字符串匹配异常 Message 实现分类。命令/Reader发送后的取消、超时和不确定连接不能自动重连后继续旧事务。Cleanup失败也不能覆盖主业务异常。

### 3.2 配置与服务器差异

- W 默认 RequireTls、Pooling=false，连接超时/命令超时/空闲期限/清理期限单位分别按 W typed builder 契约记录；EF CommandTimeout 为秒。迁移普通连接串时明确处理不支持的旧关键字。
- 普通业务连接串不能直接注入 W 专用键到官方共用 secret。先读取既有连接，经当前驱动的 typed builder 在内存形成各自设置，不打印最终字符串。
- 对每个实库 profile 记录服务器自报版本、字符集、TLS、兼容模式等非敏感信息。RU/Serializable及保存点按 W当前公开门限；TLS `8.1.4.6` 的拒绝测试不能代替 shared `8.1.5.60` 的成功测试。
- 基础 EF RelationalTransaction 是否绕过驱动 SupportsSavepoints 必须实测；必要时提供 W事务适配服务，在调用前明确拒绝不支持的保存点。不因自动保存点失败把外部事务当作可继续使用。
- 时间精度按 wire 类型与 profile 验证：七位写仅 `8.1.5.60 / message 21 / UTF-8` 已验证；`8.1.4.6 / message 11 / GB18030 / TLS` 走六位范围。TLS 六位成功需要显式六位模型/参数，默认七位模型在该 profile 应拒绝，不能把默认模型全部失败写成 TLS 全量兼容。
- TIME/INTERVAL 七位、8/9 位写、非 NULL TIMESTAMP WITH LOCAL TIME ZONE 写、1582-10-05…14 写、BCE 和 TIME hour=24 投影仍关闭；读取 8/9 位无法整除 100ns 的值时 CLR getter 拒绝。不因 codec 修复开放小数 DateTime.Add*、时区转换或新的 LINQ 翻译。
- M1 不开放 TransactionScope/enlist、多活动Reader、分布式事务、透明重连、NativeAOT/裁剪保证或未经验证 native 入口。

### 3.3 错误恢复与触发器的最新合同

上游 `DmServerErrorRecovery` 已在精确 `8.1.5.60` profile 对实测语句错误保留会话；需完整严格错误回执、当前操作/会话 owner、Query/Reader 执行上下文、已确认 statement close，以及同一 Active 本地事务或无本地事务的 AutoCommit 状态。Prepare、在途事务、取消先胜、白名单外错误和其他 profile 仍保守处理。T05 不复制该内部白名单到 EF，不增加按错误码重试逻辑；读取公开结果并验证 EF 异常包装没有丢信息。

聚焦验证唯一键/外键/不存在对象、RAISE guard、序列耗尽及未绑定标记 `-6804`；在 sync/async × autocommit/Active 下验证同一会话、失败语句无部分效果、无遗留锁和原事务未暗中提交/回滚。`-3510` 有歧义，保持 Broken 负例；不执行 BACKUP 或扩大错误白名单。错误恢复成功也不扩大事务隔离或 profile 范围。

零参数命令由驱动保留 `:NEW`/`:OLD` 等标记形状文本给服务器，但词法完整性检查仍执行；有参数时匹配、未用参数、混用标记规则仍严格。T04 加触发器创建→更新→生成值/并发回读正例；另测零参数 `:X`/`?` 由服务器报 -6804 及其恢复、实际参数化命令负例，禁止在 EF 层用替换 SQL 文本规避绑定。

## 4. 工作包与依赖

每个任务有独立提交/PR边界；每次只启动前置门已完成的任务。计划中的新文件名称为拟定名称，T01 固化后变更须记录原因。

| 任务 | 前置 | 主要文件/产出 | 完成门 |
| --- | --- | --- | --- |
| T00 基线与候选冻结 | 首轮审查完成且意见已处置 | `docs/w-dmprovider-validation.md`、候选身份 manifest、包核验脚本、现有测试清单 | 以公开 preview.1 的身份冻结来源，并按 §5.3 实现可复现的核验；复核§2.4及§6 API；记录当前锁文件/实际SDK、服务器profile；不挪用旧测试通过数 |
| T01 API与合同（并入产品代码） | T00 | §2.5 的桥接 API、options/DI/重试/日志合同及其离线单元测试 | 不另建可丢弃原型：T01 的门作为 T02/T03 实际代码中的单元测试执行。跨程序集可访问性、descriptor感知的Name/IsConfigured与首次/复用错配拒绝、重试API可见性、两驱动并存、重载解析、设计时发现、缓存与日志、基础API兼容均有可执行证明；失败先修设计再继续 |
| T02 官方路径抽象化 | T01 | 现有连接、类型映射、异常分类、设计时工厂 | 官方全量单元及受影响实库回归；SQL无非预期改变；旧消费者编译通过 |
| T03 W基础适配 | T02 | 新 WDriver 项目、桥接实现、UseDamengWithWDriver、解算锁 | 新 PackageId 精确包构建、时间 Precision→Scale/typed reader、参数映射/类型拒绝/NULL/所有权离线单元通过；聚焦实库合同写好但执行门归T04，不使用尚不存在的W启动器；时间桥接/reader与参数语义列入 §8 的独立复审范围 |
| T04 测试通道与身份审计 | T03 | `scripts/local-test/*`、共享fixture、W单元/功能/规范项目 | 驱动/profile显式选择；独立结果目录；嵌套CLI实际DLL证明；普通/管理员账号隔离；执行T03聚焦实库合同及七位/六位 profile、触发器回归；失败/skip无法被聚合成通过 |
| T05 异步/故障/事务与错误恢复 | T04 | W专属回归及重试/事务桥接修正 | barrier驱动的取消/超时/Unknown断言、真实最终状态、有条件同会话恢复与Broken负例、RU/Serializable成功及拒绝profile、精确清理 |
| T06 新版EF完整回归与设计时 | T05 | 完整功能/规范/迁移/CLI/反向工程矩阵 | 当前 `5863d37` 测试意图全量分类；旧76项及上游219/234不替代新版；七位存取/迁移guard与schema精度保真、两种包CLI生成/编译/执行与旧迁移切换通过 |
| T07 M1候选交付 | T06 | README、compatibility、architecture、AGENTS及配置/测试指南；候选包、release manifest、双包标签路由 | 精确nupkg消费者矩阵、API/依赖检查、文档按驱动限定能力、标签与包名/数量校验、当前PR review/checks闭合；公开发布另有门 |
| T08 DataSource产品接入 | T07 | 新数据源重载、scoped连接创建、生命周期测试 | 获取/关闭/Dispose责任、等待取消、同driver不同source无串用、容量耗尽及证书轮换说明通过 |
| T09 LOB显式访问 | T08 | 回调式LOB接口/示例、读写及诊断回归 | 大值/hash/内存/生命周期/取消/不可重放流、异步路径完整性均有实证 |
| T10 M2候选交付 | T09 | M2兼容矩阵与精确包证据 | M1回归 + M2新增门；无性能或streaming过度声明 |
| T11 DmDecimal专项 | T07之后单独立项 | 映射、比较器、getter、SQL字面量/参数、查询策略 | 超CLR范围正负数/scale/NULL精确往返；不支持算子明确拒绝；迁移/脚手架保真 |
| R4写入扩展 | 上游R4对应能力独立验收 | 另写批量方案，不属于本计划实现批次 | 数组执行/结果归属/错误行/事务/生成值契约齐备后再启动 |

关键路径：`T00 → T01 → T02 → T03 → T04 → T05 → T06 → T07 → T08 → T09 → T10`。T11可以在M1闭合后独立进行，不能卡住基础异步交付。T04 的测试接口设计可随 T01 并行讨论，但实际测试任务不能抢占尚未冻结的构建窗口。

### 4.1 关键文件清单

- 连接/选项：`Storage/Internal/DamengRelationalConnection.cs`、`Infrastructure/Internal/DamengOptionsExtension.cs`、`Extensions/DamengDbContextOptionsBuilderExtensions.cs`、`Extensions/DamengServiceCollectionExtensions.cs`。
- 参数/物化：`DamengStringTypeMapping.cs`、`DamengByteArrayTypeMapping.cs`、`DamengTimeSpanTypeMapping.cs`、`DamengTimeOnlyTypeMapping.cs`、`DamengDateTimeOffsetTypeMapping.cs`、`DamengTypeMappingSource.cs`；新增驱动感知的时间 mapping/adapter 合同，不只改当前直接引用官方类型的文件；回归 decimal/JSON。
- 故障：`DamengTransientExceptionDetector.cs`、`DamengRetryingExecutionStrategy.cs`、`DamengDbContextOptionsBuilder.cs`。
- 设计时：`Scaffolding/Internal/DamengDatabaseModelFactory.cs`、`DamengCodeGenerator.cs`、`Design/Internal/DamengDesignTimeServices.cs`。
- 包/测试：新 WDriver 项目、solution、`Directory.Packages.props`、相关 `packages.lock.json`、精确包消费者、CLI临时项目模板与本地启动器。
- 文档/工作流：`docs/architecture.md`、`AGENTS.md`、README/compatibility、配置/本地测试指南及发布workflow。AGENTS中异步回退改为按驱动限定；DbBatch当前两驱动均未开放，仍保持单命令批次。不在实现/验收之前提前修改当前支持声明。
- 现有 `DamengUpdateSqlGenerator` / `SingularModificationCommandBatch`、迁移SQL与查询翻译保持共用；修改必须由具体回归失败或新能力设计驱动。

## 5. 验收矩阵与测试方法

### 5.1 测试分类

T00 冻结当前测试名称与意图；T04 分成共享契约、官方行为表征、W增量、profile拒绝、管理员专用五类。旧驱动失败表征转为官方专属时，为 W 添加对应成功/拒绝断言并建立逐项映射，不直接删除测试。每个通道按运行前冻结的清单校验实际执行用例，不能临时追加过滤器得到“全绿”。

`DamengIsolationLevelFunctionalTests`明确列入按driver/profile区分断言的清单：官方RU/Serializable跟踪保存的已知失败、W在8.1.5.60上的成功、W在其他profile上的发送前拒绝分别验证。共享SQL/模型语义不等于事务能力相同。历史测试计数仅作定位线索，T00以本次枚举及实际运行结果冻结数量，不硬编码review文档中的旧统计。

| 类别 | 必须覆盖 |
| --- | --- |
| 包/公共API | 官方only、W扩展only、两包共存三个独立消费者；现有API签名、W重载、实际创建的连接/参数/异常类型；新W精确依赖、程序集/MVID及factory身份；拒绝旧W和新W同时引用；依赖图含官方包这一取舍公开 |
| DI/缓存 | T01即验证两方向手工内部service provider错配、首次初始化/复用/注册顺序冲突；同进程两种driver；同CLR DbContext交替使用；独立连接串/数据源；上下文池租还；不缓存连接/凭据 |
| 映射 | CLOB/NCLOB/BLOB、VARBINARY/fixed BINARY、INTERVAL、Unicode含补充平面字符、NULL/空、decimal边界、DateOnly/TimeOnly/DateTimeOffset、显式类型与facet、JSON等默认provider类型经DbType/Value设置后的最终绑定、Clone及重复绑定 |
| 时间精度 | DateTime/DateTimeOffset 的 MaxValue、MaxValue-1 tick、整微秒邻值、MinValue+1 tick；可表示 offset 与 UtcTicks；默认/显式六位/七位、NULL、Scale=0、Clone/重用；参数化相邻 tick 等值/大小比较；CLR/服务端文本/schema 三方核对；8/9 位与未开放 profile 的明确拒绝 |
| 会话恢复 | sync/async × autocommit/Active；白名单正例/未知与 -3510 负例、Prepare/关闭失败/profile拒绝；同 session、无部分效果/锁、事务前后效果与 ConnectionReusable；EF 不自动重试 |
| CRUD/生成值 | 当前完整CRUD、IDENTITY/序列、客户端复合键、非键默认/计算列、无参数触发器 :NEW/:OLD、未绑定 :X/? 与带参数负例、触发器并发标记、原始值/当前值、零行更新、删除、外部事务回滚；同步/异步对应 |
| 查询 | 当前字符串/聚合/时间/数值/Contains/LOB比较与既有拒绝边界；JSON不新增支持；SQL快照及真实结果对应 |
| 异步 | 可控通道/barrier证明调用在网络未完成时返回、无Task.Run伪异步；实际EF查询/SaveChanges/事务执行；普通标量与LOB路径分开报告 |
| 取消/期限 | 预取消、Send后、Fetch、Reader切换、取消与完成竞争、旧token不取消新调用、cleanup期限；原token、首失败及会话终态正确 |
| 重试/未知结果 | W所有EnableRetryOnFailure重载/自带strategy直接构造/配置顺序拒绝；自定义ExecutionStrategy责任边界；commit失确认、写入发送后断线、事务开始中后阶段失败；默认W策略尝试次数=1；新连接验证最终状态，不以异常推断回滚 |
| 隔离/保存点 | RC基本合同；RU/Serializable已支持profile真实跟踪保存；不支持profile发送前拒绝；EF自动保存点、显式保存点、失败后的事务状态 |
| 迁移 | GenerateCreateScript、非幂等/幂等script、Migrate、历史表/锁、纯移除IDENTITY与生成值现有增量；6→7保值与7→6多列guard、失败后历史/列scale/连接状态；DDL事务抑制、动态SQL边界、重复执行无重复种子 |
| 设计时 | 两套provider CLI名称、生成正确Use方法、生成代码编译、add/script/update/scaffold；当前完整反向工程保真与拒绝结构；Create(string)/Create(connection)两条路径 |
| 迁移切换 | 官方创建并写历史→W读取/应用下一迁移；反向切回；无分支与ActiveProvider条件分支夹具；对生成SQL进行审阅 |
| 诊断 | T01验证LogFragment/DebugInfo的稳定driver标识与脱敏；ActivitySource/Meter仍订阅`W.DmProvider`；取消/Unknown分类与业务错误一致；合成敏感哨兵不进入标签/输出；后台诊断丢弃不能影响业务结果 |
| 权限与清理 | 固定身份/当前Schema、唯一对象、finally精确清理；失败残留清单；一般测试不提升管理员；四项规范测试保持冒烟标签 |

故障注入优先复用驱动已提供的受控测试设施；EF层还需证明它的异常包装、执行策略与事务生命周期。若精确包不能在不修改产品的情况下稳定制造commit失ACK，应另建有界、隔离的故障代理/测试夹具，记录“驱动门已通过”和“EF端到端待验证”两种证据；不得用合成异常注入冒充网络端到端验收。该门缺失时M1故障恢复声明保持pending。

### 5.2 profile及权限

普通测试首先使用本仓库已配置的持久测试用户，不重新 provision。W既有 shared / TLS 环境的账号、schema与本仓库可能不同，T00 显式建立profile清单，由启动器选择该profile允许的测试合同。本文不授权读取/复制驱动仓库管理员secrets或变更数据库权限。

必须具有：官方当前参考profile完整基线；W至少一个能够执行M1成功合同的profile；W TLS profile成功子集及不支持能力的拒绝合同。若当前持久实例自报版本不在W白名单，应记录为预期拒绝，不能伪造版本或扩大白名单；完整成功门等待合适的既有授权环境。不同profile的通过不能互相替代。七位时间成功、RU/Serializable成功、错误恢复成功属于不同能力门，不能仅因版本字符串相同就忽略 message/编码/TLS 等记录。

上游的临时 CREATE SCHEMA 授权不随报告继承，本项目不自动提权。将上游未关闭的序列耗尽正向场景、CREATE MATERIALIZED VIEW 和 admin/CLI 缺口映射到当前真实测试名称及所需权限；允许执行的环境完整运行，缺环境保留 pending，不通过删用例或采纳历史警告屏蔽补丁得到绿色。

官方与W的迁移/脚手架完整管理员合同使用本项目明确的admin通道，并且仅该通道允许精确创建/清理临时用户和表空间。若某个W profile没有管理员执行环境，则该profile的admin门未验证，不得宣传等价的全范围迁移/脚手架支持；可交付明确限定范围的开发预览。普通测试不以`all`误触管理员通道。

### 5.3 命令、证据与预算

统一保留 `scripts/local-test/run.sh test <unit|functional|specification|admin|probes|all>`。T04 为启动器增加驱动/profile选择和W项目路由；参数形式由该任务实现并写进 README，未实现前不能把设想的参数当现成命令使用。官方默认入口保持兼容。

运行dotnet前设独立可写的DOTNET_CLI_HOME、DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1、DOTNET_CLI_TELEMETRY_OPTOUT=1；构建/测试单节点 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false`，支持时加 `--disable-build-servers`。使用locked restore，不清NuGet全局缓存。

精确候选的还原来源（2026-10-14 修订：preview.1 已在 NuGet.org 公开发行并签名，原“自建只读候选 feed”机制降为未发行开发候选的后备方案，本轮不实现）：

1. 还原来源就是公开 NuGet.org 上的精确版本 `[0.1.0-preview.1]`。不解析 latest，不提交二进制。`packages.lock.json` 记录的 `contentHash`（SHA-512）在 `--locked-mode` 下固定包字节，同版本不同字节的包无法通过还原。
2. T00 增加不含秘密的 `scripts/driver-candidate/w-damengprovider.manifest.json`，固定 PackageId、精确版本、包 SHA-256、DLL SHA-256/MVID、nuspec repository commit、tag commit、发布 run/artifact ID 与公开 URL。另加 `scripts/driver-candidate/verify-package.py`，从实际还原目录（`NUGET_PACKAGES` 或默认全局缓存）读取 `.nupkg`/`.nupkg.metadata`/DLL，逐项比对 manifest 与锁文件 contentHash。缺件、hash 不符或来源无法关联时退出非零。脚本也可以对显式提供的 nupkg 路径运行，供 CI/审查者复现。
3. 官方默认通道沿用当前 NuGet 配置与行为，不新增仓库级 `NuGet.config`。需要验证缓存污染时，W 通道可使用独立 `NUGET_PACKAGES` 目录重新还原后再核验。
4. T03 首次引入依赖时受控更新相关锁文件并审查 diff。冻结后所有验收均使用 `--locked-mode`，不在验证中 force-evaluate 或改锁。
5. T04 审计包括：错误 hash、锁不匹配的负例（以脚本单元测试或合成输入覆盖）。来源校验不能代替测试进程和嵌套 CLI 实际加载 DLL 的 SHA/MVID 证明；W 功能通道在测试进程内读取已加载 `W.DamengProvider` 程序集的路径与 MVID 并与 manifest 比较。
6. 若未来需要接入未发行的开发候选，再按原机制（隔离 feed、显式 `--configfile`、packageSourceMapping、独立缓存）另行实现，不能用“最新成功构建”替代固定身份。

每个候选记录：EF/source commit及dirty状态、producer来源manifest、包版本/SHA、加载DLL SHA/MVID、依赖图/锁文件、实际SDK/runtime、profile非敏感标识、测试清单、命令/退出码/TRX计数、预注册预算、前后对象清理结论。CLI子进程也核验其实际加载资产，不能只验证父进程。pack后的精确nupkg消费者与ProjectReference测试分开记录。

历史W上游76项功能通道的1500秒只属于其固定合同，不能直接作为本仓库更大测试集预算。T00/T04先按分组和当前基线预注册合理预算；超时保存失败，不在同一验收中自动延长或重试。修复/预算修订另立执行版本。

通过条件：宿主Completed、退出0、预期清单齐全、Total=Executed=Passed>0、Failed=Skipped=0、身份相符、清理闭合。没有连接/权限/候选包的通道标pending；探针与规范冒烟不得代替完整功能测试。

## 6. M2：数据源和LOB接口的具体约束

复用W现有 `DmDataSource`、连接容量控制和Reader的 `GetStream`/`GetTextReader`；EF层新增的是options重载、连接/事务作用域衔接及回调生命周期包装。当前公开API未提供EF上下文回调式LOB访问入口，不重写驱动的池、分块或编码算法。T00重新核查若上游增加等价设施，记录复用决策并调整T08/T09范围，不重复建设。

DataSource由调用方/DI容器创建并拥有，EF仅拥有由它创建的连接；DbContext.Dispose不能Dispose共享DataSource。M2首版不引入模糊的“自动拥有数据源”选项。容器先释放所有上下文/连接，再释放数据源；等待者取消、Clear epoch、Dispose后新请求拒绝、在借请求完成均有测试。把含秘密的DataSource.ConnectionString脱敏值拿来重建连接会丢凭据，必须直接调用原DataSource的CreateConnection。

提供方拟定 `UseDamengWithWDriver(DmDataSource, ...)`，options中保存调用方引用，服务单例/模型不能捕获它。typed连接、当前事务、外部连接所有权与DbContext pooling分别验证；不允许同一上下文并行操作。

LOB首版使用回调式作用域：在Reader当前行有效期间调用用户的异步消费回调，回调结束再推进/释放Reader；不返回脱离Reader生命周期的裸Stream。写入接受显式Blob Stream/Clob TextReader，所有权归调用方；执行失败不rewind、不dispose调用方源、不自动重试。回调不可重入同一个DbContext。

测试至少包含：40 KiB已有基线、64 MiB边界两侧BLOB（含64 MiB+1 KiB）、跨块Unicode/代理对、NULL/空、未知长度不可seek、源在发送前/后抛异常、提前关闭/取消/提交/回滚导致失效、单活动流、顺序读取限制、内存峰值随chunk而非总payload增长。超64 MiB完整物化应明确失败，流式路径独立成功；不将某一实测大小宣称为无限容量。

M2接口设计必须说明是否走EF执行策略与当前事务，默认单次执行；API、SQL参数化、Reader/stream所有权和回调异常规则在T09编码前完成审查。禁止用任意实体Stream属性规避EF跟踪/比较语义问题。

## 7. 不属于本轮的能力

数组绑定、原生DbBatch、BulkCopy公开产品能力、StoredProcedure/Output/RefCursor、RETURNING输出参数、多行VALUES优化、通用多命令批处理属于后续独立设计。现有多结果Reader不等价于EF高效批处理；没有DbBatch也不构成多行VALUES在原理上不可做，后者仍需独立SQL/参数/并发/生成值验证。

JSON标量翻译、更多LINQ算子、LOB排序分组、原子DDL、增加/恢复IDENTITY、跨模式迁移、更多复杂反向工程结构不由换驱动自动解锁。保持现有明确拒绝，只有新增实现与对应真实证据后才更新支持范围。

## 8. 审查、发布和回退

本次由主代理编写计划，用户指定的Kimi Code独立审查架构、遗漏和证据门。记录实际provider/model、每轮taskId、所审文档hash、结论、逐项响应；阻断项修订后新开一次委派复审，不能把旧审查结论套到新文档。

本轮输入由用户提供的Kimi Code CLI交互会话完成；原两次T3子代理失败记录保留，CLI审查没有taskId，不能补造。首轮结论为无P1、4项P2和4项P3；计划方已将8项处置和当前修订hash写入审查记录。2026-10-08意见处置满足T00的首轮审查前提；2026-10-13新增包身份、时间桥接和恢复合同，单独记录本次更新hash，不沿用旧“无P1”结论。10-13 轮止于计划交付；10-14 执行修订把时间桥接/reader与参数语义的独立复审移到 M1 开发候选的代码 diff 上（见下文“独立复审门”），本计划没有代替用户提供的 Kimi review，也不宣称新复审已完成。实施中若推翻 §2.5 的架构、改变包依赖方向或自动重试保护，或发现新的阻断项，先停下记录，再提交独立复审。

实施分工（2026-10-14 修订）：本轮由单个主代理按任务顺序编码和验证。原“Sol High/Sol Low/Kimi”分工不再作为前置条件，但“源码冻结后再执行验收运行、失败证据保留、修复后另起执行版本”的规则保留。

独立复审门（2026-10-14 修订）：时间桥接/reader、参数语义和重试保护不再阻塞 T02/T03 编码，改为在 M1 开发候选形成后、PR 合并前，对实际代码 diff 做一次独立复审，复审者由用户指定（原定 Kimi Code）。复审记录实际 provider/model、所审提交与结论；未完成复审时 M1 只算开发候选，不能合并为已验收能力。

每个PR合并前检查当前提交的全部review/comment/thread、未决意见与checks；修改后重跑受影响门。M1/M2先形成开发候选及精确包消费证据。公开发布另需：W精确依赖在目标feed可获取（preview.1已确认）、上游证据与本次使用范围相符、两包兼容版本成对验证、license/NOTICE完整、发布自动化能正确选择/顺序发布所需包、当前实库门完成及明确发布授权。上游合并或离线CI通过不等于本项目发布通过。

扩展包应约束到已验证的基础包兼容范围；开发候选优先精确配对。现有tag工作流只打包官方项目，T07须增加W候选打包/consumer检查及防止误触公开发布的门；不能依靠现有单包流水线自动得到双包发行。

标签路由固定为：保留 `v<SemVer>` 只对应基础包 `W.EntityFrameworkCore.Dameng`；新增 `wdriver-v<SemVer>` 只对应扩展包 `W.EntityFrameworkCore.Dameng.WDriver`，它不匹配旧工作流的 `v*`。开发候选使用无发布权限的手工构建/构件任务，不创建发行标签。当前任何符合版本规则的 `v*` 标签仍会触发现有官方包发布，所以新路由和所有发行门就绪前禁止用它给W候选打标签。

T07改造构建、推包与GitHub Release任务的统一标签解析及包白名单，验证标签→目标项目→PackageId→版本→nupkg数量全部一致（每个标签只允许恰好1个目标nupkg）。标签错配、额外/缺失包、错误PackageId/版本均拒绝；不只按文件扩展名数包。W标签发布前验证其精确基础包依赖已在目标feed可获取，成对交付时先基础包后扩展包。发布凭据仅在这些校验和发行门通过后的步骤可用，离线CI绿色不能绕过实库门。

回退时pin上一个已验收的EF+driver组合；已有七位数据先核对可表示性，保留七位schema，不截断数据来适配旧驱动；应用停止新请求，释放上下文/连接/数据源后切回官方配置。仍用共享SQL/模式并不保证无损回退任意新增迁移，发布前对迁移SQL与ActiveProvider分支单独审阅；DDL失败按对象最终状态处理。OutcomeUnknown先核对业务效果，切驱动不能作为自动重放理由。

## 9. 证据索引

本仓库相对路径：

- [兼容矩阵](compatibility.md)、[前置生成值/迁移计划](stage3-pre-driver-plan.md)。
- [本地测试运行规则](../scripts/local-test/README.md)。
- [达梦SQL skill](../skills/dameng-sql/SKILL.md)、[迁移 skill](../skills/dameng-ef-migrations/SKILL.md)。

上游固定提交证据（均为 `a67a9298daaa42b18203a9b405ec05a3357ed4b0`；历史报告中的样本不等于本轮公开包）：

- [产品身份与公开边界](https://github.com/whynpc9/W.DamengProvider/blob/a67a9298daaa42b18203a9b405ec05a3357ed4b0/README.md)、[项目元数据](https://github.com/whynpc9/W.DamengProvider/blob/a67a9298daaa42b18203a9b405ec05a3357ed4b0/src/W.DamengProvider/W.DamengProvider.csproj)。
- [NuGet preview.1](https://www.nuget.org/packages/W.DamengProvider/0.1.0-preview.1)、[精确版本索引](https://api.nuget.org/v3-flatcontainer/w.damengprovider/index.json)、[精确签名包](https://api.nuget.org/v3-flatcontainer/w.damengprovider/0.1.0-preview.1/w.damengprovider.0.1.0-preview.1.nupkg)、[发布CI](https://github.com/whynpc9/W.DamengProvider/actions/runs/38339061188)。索引可变化，验收以固定版本与散列为准。
- [发布前检查](https://github.com/whynpc9/W.DamengProvider/blob/a67a9298daaa42b18203a9b405ec05a3357ed4b0/docs/release-preflight.md)、[发布流程](https://github.com/whynpc9/W.DamengProvider/blob/a67a9298daaa42b18203a9b405ec05a3357ed4b0/docs/release.md)。其中“未创建tag/未上传”的文字是发布准备时状态；本次在线tag、CI和NuGet读回已证明后续发生了发布，不将旧文字当现状。
- [时间契约](https://github.com/whynpc9/W.DamengProvider/blob/a67a9298daaa42b18203a9b405ec05a3357ed4b0/docs/compatibility/TP-temporal-precision.md)、[时间专项EF报告](https://github.com/whynpc9/W.DamengProvider/blob/a67a9298daaa42b18203a9b405ec05a3357ed4b0/docs/implementation/temporal-native/TP07-TP08-closure.md)。旧T09概述仍称微秒，本计划采用专项文档及当前实现更细的profile限定，不扩大支持。
- [EF兼容缺口报告](https://github.com/whynpc9/W.DamengProvider/blob/a67a9298daaa42b18203a9b405ec05a3357ed4b0/docs/implementation/ef-functional-compat-gaps.md)、[错误恢复实现](https://github.com/whynpc9/W.DamengProvider/blob/a67a9298daaa42b18203a9b405ec05a3357ed4b0/src/W.DamengProvider/Internal/Sessions/DmServerErrorRecovery.cs)、[参数绑定实现](https://github.com/whynpc9/W.DamengProvider/blob/a67a9298daaa42b18203a9b405ec05a3357ed4b0/src/W.DamengProvider/Internal/Types/DmParameterBinding.cs)。
- [R3边界与历史证据](https://github.com/whynpc9/W.DamengProvider/blob/a67a9298daaa42b18203a9b405ec05a3357ed4b0/docs/compatibility/R3.md)、[隔离级别合同](https://github.com/whynpc9/W.DamengProvider/blob/a67a9298daaa42b18203a9b405ec05a3357ed4b0/docs/compatibility/T11-isolation.md)。

本仓库实现对照：`Storage/Internal/DamengTypeMappingSource.cs`、`DamengDateTimeOffsetTypeMapping.cs`、`DamengTimeOnlyTypeMapping.cs`、`DamengTimeSpanTypeMapping.cs`（均在 `src/W.EntityFrameworkCore.Dameng/` 下）。当前文件没有W桥接；上游EF patch仅作为设计参考，按本计划双驱动契约重做适配与测试。

## 10. 本轮执行范围（2026-10-14）

- 执行 M1：T00 → T01/T02 → T03 → T04 → T05 → T06 → T07（只到开发候选与本地精确包消费者证据；不推送发行标签、不发布 NuGet、不创建 GitHub Release）。
- 每个任务一个本地提交；推送分支和创建 PR 需要用户另行确认。
- 真实数据库门在本地 `8.1.5.60 / UTF-8 / plaintext` profile 上执行，记录在[验证记录](w-dmprovider-validation.md)。TLS profile、其他服务器版本、commit 失 ACK 端到端故障注入、需要额外权限的上游缺口保持 pending，不当作通过。
- M2（T08–T10）、T11 与 R4 不在本轮执行；M1 闭合并经独立复审后再按本计划启动。
