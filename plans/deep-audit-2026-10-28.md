# 深度审查记录 · 2026-10-28

审查基线：`c80c788fb3cd34d97626d578db580264a28a11f3`。范围为整个仓库的两个产品包、六个测试项目、工具、发布工作流和文档。使用八个并行只读审查分工，主审逐项回读纳入报告的本地证据，并去重、校准置信度。

本报告包含 **14 项静态确认的问题/验证缺口、6 项待复现调查、3 个方向选项**。“高置信度”表示源码控制流或覆盖缺口确定，**不表示本轮已复现真实服务器故障**。本轮只新增 `plans/` 中的审查文档，未修改产品、测试、依赖锁或发布配置。工作区原有 `.agents/`、`.claude/`、`skills-lock.json` 未改动。

## 当前验证基线

| 验证 | 本轮结果 | 证据 |
| --- | --- | --- |
| 官方单元 `scripts/local-test/run.sh test unit` | 791/791；Completed；无失败、跳过 | [官方 TRX](/Users/wanghongyi/Projects/dameng-entityframework-core/artifacts/query-translation/local-test/20261028T042303124Z-8ff3eae4/unit.trx:1) |
| W 单元 `scripts/local-test/run.sh test unit --driver w` | 61/61；Completed；无失败、跳过 | [W TRX](/Users/wanghongyi/Projects/dameng-entityframework-core/artifacts/query-translation/local-test/20261028T042328752Z-662ec138/unit-w.trx:1) |
| 纯脚本拆批 `scripts/local-test/run.sh test functional --filter FullyQualifiedName~DamengScriptExecutorTests` | 26/26；Completed；这 26 项不连接数据库，不能计为实库验收 | [纯脚本 TRX](/Users/wanghongyi/Projects/dameng-entityframework-core/artifacts/query-translation/local-test/20261028T042743009Z-d94a0069/functional.trx:1) |
| `scripts/local-test/run.sh selftest` | 合成 TRX 脱敏、终止及计数验收通过 | 本轮命令结果 |
| `PYTHONDONTWRITEBYTECODE=1 python3 -B scripts/audit/test_scan_aggregate_queries.py` | 7/7 | 本轮命令结果 |
| `dotnet list W.EntityFrameworkCore.Dameng.slnx package --vulnerable --include-transitive --no-restore --format json` | 八个项目没有返回漏洞条目 | 2026-10-28 查询 NuGet.org；不是完整供应链安全证明 |

上述 .NET 调用设置可写临时 `DOTNET_CLI_HOME`、关闭首次体验和遥测；还原/编译/测试沿用仓库入口的单节点及禁用构建服务器配置。未运行完整实库功能、管理员、规范冒烟或探针通道，没有产生新的服务器能力声明。

## 优先队列

工作量包含回归：S 为数小时，M 为约一日，L 为多日；风险指修复引入回归的风险。以下排序综合影响、成本、置信度和验证难度；调查项 I01 因潜在数据精度影响提升到首批建议。

| ID | 发现 | 类别 | 影响 | 工作量 | 修复风险 | 置信度 | 主证据 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| F01 | CLI 回归凭据进入 argv、源码及可能残留的日志/临时文件 | 安全 | 高 | M | 低 | 高 | [DamengDotNetEfCliFunctionalTests.cs:179](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs:179) |
| F02 | Replace 的 null 替换值被错误声明为传播 null | 正确性 | 高：可改变筛选结果 | M | 中 | 高 | [DamengStringMethodTranslator.cs:188](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengStringMethodTranslator.cs:188) |
| F03 | 运行时连接替换和时态参数缺少驱动一致性守卫 | 正确性/架构 | 高 | M | 中 | 高 | [DamengRelationalConnection.cs:7](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalConnection.cs:7) |
| I01 | 时间字面量可能在七位列写入前丢失第七位 | 调查/正确性 | 高 | M | 中 | 中 | [DamengTypeMappingTests.cs:746](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.Tests/DamengTypeMappingTests.cs:746) |
| F04 | 纯注释迁移仍修改列定义，触及 IDENTITY 禁限 | 正确性 | 中高 | M | 中 | 高 | [DamengMigrationsSqlGenerator.cs:289](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs:289) |
| F05 | 仓库缺 PR 离线 CI，已有廉价测试未自动接入 | DX/测试 | 中高 | M | 低 | 高 | [publish-nuget.yml:5](/Users/wanghongyi/Projects/dameng-entityframework-core/.github/workflows/publish-nuget.yml:5) |
| F06 | distinct 集合右侧 LOB 和 LISTAGG 二进制 LOB 排序漏检 | 正确性 | 中 | S | 低 | 高 | [DamengLobQueryRewriter.cs:119](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengLobQueryRewriter.cs:119) |
| F07 | 历史表脚本用文本搜索定位 CREATE TABLE | 正确性 | 中：特殊合法模式名破坏幂等性 | S | 低 | 高 | [DamengHistoryRepository.cs:47](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengHistoryRepository.cs:47) |
| F08 | CLI 回归固定加载 Debug DLL | 测试 | 中：Release 混合产物证据 | S | 低 | 高 | [DamengDotNetEfCliFunctionalTests.cs:44](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs:44) |
| F09 | W 会话淘汰测试未保持故障前连接 | 测试 | 中：关键断言缺乏辨别力 | S | 低 | 高 | [WDriverRuntimeFunctionalTests.cs:85](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRuntimeFunctionalTests.cs:85) |
| F10 | provision 随机口令未保证字符类别 | DX | 中：初始化随机/必然失败 | S | 低 | 高 | [Program.cs:394](/Users/wanghongyi/Projects/dameng-entityframework-core/scripts/local-test/Program.cs:394) |
| F11 | 管理员夹具无法恢复结果不确定的 CREATE | 测试/资源管理 | 中：遗留用户/表空间 | M | 中 | 高 | [DamengScriptTestDatabase.cs:77](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengScriptTestDatabase.cs:77) |
| F12 | HasTablesAsync 同步回退且目录命令绕过 EF 超时/诊断 | 性能/正确性 | 中 | M | 低 | 高 | [DamengDatabaseCreator.cs:74](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDatabaseCreator.cs:74) |
| F13 | 迁移锁没有真实争用、DDL 持锁及失败释放回归 | 测试 | 中 | M | 低 | 高 | [DamengMigrationsFunctionalTests.cs:239](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengMigrationsFunctionalTests.cs:239) |
| F14 | 驱动能力说明和应用用户权限示例发生漂移 | 文档 | 低至中 | S | 低 | 高 | [architecture.md:89](/Users/wanghongyi/Projects/dameng-entityframework-core/docs/architecture.md:89) |

## 已确认项的依据与修复边界

### F01 · CLI 回归的凭据数据流

[DamengDotNetEfCliFunctionalTests.cs:174](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs:174) 将真实连接串传给 scaffold，未使用命名配置或禁止生成 OnConfiguring；[DamengDotNetEfCliFunctionalTests.cs:191](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs:191) 输出生成上下文源码，随后重新编译。[DamengDotNetEfCliFunctionalTests.cs:677](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs:677) 和 [TestLauncher.cs:419](/Users/wanghongyi/Projects/dameng-entityframework-core/scripts/local-test/TestLauncher.cs:419) 仅替换原始值，而 [EF 10.0.12 CSharpHelper](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Design/Design/Internal/CSharpHelper.cs#L338-L350) 会对反斜杠、双引号等字符进行 C# 转义，因此转义后的口令形态可能保留在输出/TRX。

[DamengDotNetEfCliFunctionalTests.cs:55](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs:55) 用默认权限创建临时目录；[DamengDotNetEfCliFunctionalTests.cs:258](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs:258) 先清理数据库，异常会跳过目录删除，删除函数又吞掉 IO/权限失败。进程参数和临时源码不受日志脱敏保护。暴露给哪些本地主体取决于系统权限；未检查历史残留、未读取真实凭据，不声称已经发生泄漏。

建议通过环境解析的命名配置传递连接，避免真实值进入 argv/生成源码；配置调用形态继续用合成连接验证。移除完整敏感源码日志，文件清理独立于数据库清理，限制目录权限。用含转义字符的合成哨兵验证所有输出渠道与失败清理；若维护者核查发现历史真实泄漏，应轮换对应凭据。仅加 `--no-onconfiguring` 不能解决 argv 问题。

### F02 · Replace 空值语义

[DamengStringMethodTranslator.cs:188](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengStringMethodTranslator.cs:188) 对 REPLACE 三个参数统一声明 null 传播。`.Replace("a", null)` 的 CLR 意义是删除旧串，见 [String.Replace 合同](https://learn.microsoft.com/en-us/dotnet/api/system.string.replace?view=net-10.0)。[EF 10.0.12 的空值优化](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/SqlNullabilityProcessor.cs#L2010-L2042) 会据此把函数 IS NULL 展开为参数空值判断之或。

静态推导：必填非空 Name 上 `Name.Replace("a", (string?)null) == null` 可因第三参数恒 null 被优化成 true，改变结果集；尚未执行该最小复现。应先补常量/参数/可空列替换值、null 比较及否定的 SQL 表征和真实结果回归，再归一 null 替换值并修正传播声明。保持 bounded 文本与 LOB 映射，不引入空串补偿截断。

### F03 · 驱动一致性入口

初始连接在 [DamengDriverConfiguration.cs:81](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Infrastructure/DamengDriverConfiguration.cs:81) 校验，但 [DamengRelationalConnection.cs:7](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalConnection.cs:7) 没有覆写运行时 SetDbConnection；[EF 基类](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Storage/RelationalConnection.cs#L164-L178) 直接替换连接。因此可形成 options/映射/ProviderName 与实际驱动不一致的上下文。

同时，[DamengDateTimeTypeMapping.cs:33](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDateTimeTypeMapping.cs:33)、[DamengDateTimeOffsetTypeMapping.cs:59](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDateTimeOffsetTypeMapping.cs:59) 只调用 ConfigureTemporalParameter；[DamengWDriver.cs:72](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng.WDriver/Infrastructure/Internal/DamengWDriver.cs:72) 只设 Precision/Scale，[DamengDriver.cs:114](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Infrastructure/DamengDriver.cs:114) 默认空实现，均未验证参数类型。当前 CLOB 外来参数测试不能覆盖时态映射或 UseModel。

建议分别补运行时连接替换和时态参数守卫；回归双向驱动替换、同驱动接受、null/所有权、拒绝后状态不变、Clone/NULL 参数以及跨驱动 UseModel 的读写路径。不要只修参数后继续过度承诺整个共享模型都会自动拒绝；本次没有证明发生了数据截断。

### F04 · 纯注释变更

[DamengMigrationsSqlGenerator.cs:289](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs:289) 无条件发 MODIFY，之后才 COMMENT；[DamengMigrationsSqlGeneratorTests.cs:719](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.Tests/DamengMigrationsSqlGeneratorTests.cs:719) 的纯清除注释测试还固定断言这一形态。真实注释回归仅覆盖普通文本列。

[达梦 DDL 文档](https://eco.dameng.com/document/dm/zh-cn/pm/definition-statement.html) 的 MODIFY 规则限制自增列及若干索引列；所以给 IDENTITY 键修改注释可能在注释命令执行前失败。应通过模型差异生成注释增删改，结构完全相同则只发 COMMENT，并串行验证普通/幂等脚本、原数据及后续自增。保留真正的类型、默认值、注解变化，不做粗糙的“有注释就跳过 ALTER”。

### F05 · 离线 CI

Git 跟踪的 workflow 仅 [publish-nuget.yml:5](/Users/wanghongyi/Projects/dameng-entityframework-core/.github/workflows/publish-nuget.yml:5) 和 [publish-nuget-wdriver.yml:4](/Users/wanghongyi/Projects/dameng-entityframework-core/.github/workflows/publish-nuget-wdriver.yml:4)，都只监听发行标签。不能据此断言 GitHub 没有外部检查，但仓库本身没有 PR/普通提交验证定义。

建议新增无发布凭据的 PR/主分支流水线，运行锁定还原、Release 构建、两组单元、纯脚本拆批、TRX selftest、Python 工具测试及 W 包身份检查。真实数据库验收仍独立且两驱动串行；spec 四项冒烟不能替代一致性或发行验收。不需要借此升级依赖。

### F06 · LOB 守卫覆盖不完整

[DamengLobQueryRewriter.cs:119](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengLobQueryRewriter.cs:119) 对 distinct 集合只检查 Source1；有界左侧与 LOB 右侧的 Union/Intersect/Except 可绕过守卫。[DamengStringAggregateMethodTranslator.cs:133](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengStringAggregateMethodTranslator.cs:133) 的 IsLob 只列文本类型，组内 BLOB/IMAGE 等排序不会被识别。

建议统一 LOB 分类并检查双方投影及自定义 LISTAGG 排序；补左右互换、三类 distinct 集合、二进制 LOB、有界对照。保持 UNION ALL 和显式有界转换的合法路径。此项确认的是提前拒绝缺口，未验证具体服务器报错形式。

### F07 · 历史脚本关键词定位

[DamengHistoryRepository.cs:47](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengHistoryRepository.cs:47) 在整段脚本第一次匹配 CREATE TABLE 后插入守卫。[DamengMigrationsSqlGenerator.cs:375](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs:375) 会先在 EnsureSchema 的字符串内嵌入模式名。合法带引号模式名若含同样的关键词子串，插入点会落入字符串，改变模式存在性判断并漏改真正建表语句。

建议使用已有词法工具定位引号/注释之外的命令，或按 CreateTable 命令生成。覆盖普通/带引号/关键词名称，并验证重复执行；不将模型标识符当作不可信 SQL 输入泛报注入。

### F08 · CLI 被测程序集来源

[DamengDotNetEfCliFunctionalTests.cs:44](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs:44) 固定寻找 Debug DLL，再由 [DamengDotNetEfCliFunctionalTests.cs:359](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs:359) 写入 HintPath。Release 功能测试在干净目录失败，在旧 Debug 产物存在时可能测试旧实现。

建议从测试宿主实际加载的提供程序程序集取路径并核验身份，覆盖干净 Release 与两配置共存。当前统一入口跑 Debug，不能以此否定历史 Debug 回归或 Release 单元结果。

### F09 · 会话淘汰断言

[WDriverRuntimeFunctionalTests.cs:85](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRuntimeFunctionalTests.cs:85) 与 [WDriverRuntimeFunctionalTests.cs:420](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRuntimeFunctionalTests.cs:420) 在取得初始 SESSID 前未显式持有连接；[WDriverRuntimeFunctionalTests.cs:697](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRuntimeFunctionalTests.cs:697) 本身是一条执行完即结束的 EF 查询。默认不池化时，正常操作之间也可能新建会话。相邻恢复测试 [WDriverRuntimeFunctionalTests.cs:197](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverRuntimeFunctionalTests.cs:197) 已正确显式打开连接。

建议保持故障前会话、断言故障后实际 Broken/Closed 状态，再打开核对新会话；运行中取消使用已发送屏障。现有 FailureInfo 断言仍有价值，不能宣称这些测试完全无效。

### F10 · 初始化口令策略

[Program.cs:394](/Users/wanghongyi/Projects/dameng-entityframework-core/scripts/local-test/Program.cs:394) 只固定大写首字符，随机部分没有符号且不保证数字/小写。[DamengScriptTestDatabase.cs:301](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengScriptTestDatabase.cs:301) 已修为保证四类字符，历史台账也记录过复杂度失败。

建议两处采用一致的字符类别保证，保留随机性和安全存储，并用合成测试验证生成器。不能通过降低服务器策略解决。此项是兼容性/可靠性问题，不指称当前 32 位随机口令熵不足。

### F11 · 管理员 DDL 的不确定结果

[DamengScriptTestDatabase.cs:77](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengScriptTestDatabase.cs:77) 在 CREATE 成功返回后才保存成功标记/拥有名称；[DamengScriptTestDatabase.cs:212](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengScriptTestDatabase.cs:212) 清理只看这些标记。如果服务器提交 CREATE 后丢失响应，夹具无法核对或恢复可能遗留对象。

建议发送前记录唯一名称和阶段，失败时用新会话核对精确对象，无法确认则保存恢复信息。可参考本地 provision 已有 ambiguousCreate 思路。测试模拟“服务器已执行但客户端收到异常”，保持测试失败，不允许按前缀清扫其他对象。

### F12 · HasTables 异步与命令配置

[DamengDatabaseCreator.cs:74](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDatabaseCreator.cs:74) 只有同步 HasTables，直接创建 ADO 命令，不传 EF CommandTimeout。EF 10.0.12 的 [HasTablesAsync 基类实现](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Storage/RelationalDatabaseCreator.cs#L153-L156) 同步调用 HasTables。因此 W 的 EnsureCreatedAsync 在此阶段仍会阻塞，开始执行后的取消无效，还绕过命令诊断/拦截器。

建议改用 EF 关系命令服务，提供真正异步路径并传递取消、超时与诊断。保留“模式存在任意表便不再建表”的 EF 契约及连接所有权；官方驱动自身同步回退仍是既定边界。

### F13 · 迁移锁证据不足

[DamengMigrationsFunctionalTests.cs:239](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengMigrationsFunctionalTests.cs:239) 的显式锁测试只有空的获取/释放块；[DamengHistoryRepository.cs:110](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengHistoryRepository.cs:110) 与 [DamengMigrationDatabaseLock.cs:15](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationDatabaseLock.cs:15) 的争用、DDL 隐式提交后的持锁和失败释放未被对应回归保护。

建议单个通道内使用两条连接、有界等待验证争用，锁内执行自有对象 DDL，再验正常/异常/关闭后的再次获取。两驱动通道顺序执行。此为覆盖缺口，不是已发现锁失效，也不建议未经证据改变固定保守锁 ID。

### F14 · 文档与示例漂移

[architecture.md:89](/Users/wanghongyi/Projects/dameng-entityframework-core/docs/architecture.md:89) 把 Serializable 跟踪保存失败写成通用边界，但 [compatibility.md:148](/Users/wanghongyi/Projects/dameng-entityframework-core/docs/compatibility.md:148) 和 [DamengIsolationLevelFunctionalTests.cs:80](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengIsolationLevelFunctionalTests.cs:80) 已区分 W 的已验成功路径。[migrations.md:74](/Users/wanghongyi/Projects/dameng-entityframework-core/docs/migrations.md:74) 仍称 W 为开发候选，发行记录已更新。

[SKILL.md:93](/Users/wanghongyi/Projects/dameng-entityframework-core/skills/dameng-sql/SKILL.md:93) 为普通 app_user 示范 DBA，而 [migrations.md:21](/Users/wanghongyi/Projects/dameng-entityframework-core/docs/migrations.md:21) 明确说明已验 RESOURCE/SOI 及不应为迁移授 DBA。建议同步驱动/profile限定和权限示例；不把样例漂移描述为产品自动提权漏洞。

## 待复现调查

以下先写调查/表征计划，再根据结果决定代码修改。I01 优先于一般扩展功能，其余按维护者的使用场景选择。

| ID | 调查 | 影响 | 工作量 | 修复风险 | 置信度 | 证据 |
| --- | --- | --- | --- | --- | --- | --- |
| I01 | 七位时态字面量保真 | 种子/默认值/常量比较可能失真 | M | 中 | 中 | [DamengTypeMappingTests.cs:746](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.Tests/DamengTypeMappingTests.cs:746) |
| I02 | 共享物理列的生成策略冲突 | DDL 静默选取某个属性的 seed/sequence | M | 中 | 中 | [DamengRelationalAnnotationProvider.cs:79](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Metadata/Internal/DamengRelationalAnnotationProvider.cs:79) |
| I03 | 非原生日期存储的成员/方法翻译 | 对 BIGINT/text 生成日期函数 | M | 中 | 中 | [DamengDateTimeMemberTranslator.cs:18](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeMemberTranslator.cs:18) |
| I04 | 普通列目录缺行 | 返回不完整反向工程模型 | M | 中 | 中 | [DamengDatabaseModelFactory.cs:440](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Scaffolding/Internal/DamengDatabaseModelFactory.cs:440) |
| I05 | LOB 表默认聚集主键 | 特定服务器配置下建表失败 | M | 中 | 中 | [DamengMigrationsSqlGenerator.cs:170](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs:170) |
| I06 | ComplexProperty 内 rowversion 校验 | 平铺属性守卫被形状变化绕过 | S–M | 低至中 | 低 | [DamengModelValidator.cs:107](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs:107) |

### I01 · 先证明字面量真实精度

[DamengDateTimeTypeMapping.cs:9](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDateTimeTypeMapping.cs:9) 保留默认字面量，DateTimeOffset 亦未重写。[DamengTypeMappingTests.cs:746](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.Tests/DamengTypeMappingTests.cs:746) 仅断言裸 TIMESTAMP 文本含七位。相反，[WDriverTemporalAndMigrationFunctionalTests.cs:258](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/WDriverTemporalAndMigrationFunctionalTests.cs:258) 和 [w-dmprovider-validation.md:131](/Users/wanghongyi/Projects/dameng-entityframework-core/docs/w-dmprovider-validation.md:131) 明确记载参考服务器对裸 TIMESTAMP 只按六位解析，测试辅助构造改用 TO_TIMESTAMP。

因此参数逐 tick 回归不能覆盖 HasData/InsertData、HasDefaultValue 和内联常量。先比较生成字面量直接执行、种子/默认值、参数与常量查询的 tick 及偏移，再选无损表达式或明确拒绝。目标列七位不一定能挽回表达式求值阶段已丢失的精度；本轮未重跑此服务器行为。

### I02 · 共享列分面

[DamengRelationalAnnotationProvider.cs:79](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Metadata/Internal/DamengRelationalAnnotationProvider.cs:79) 用 FirstOrDefault 取一个属性；[DamengModelValidator.cs:284](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs:284) 按列名去重而不比较分面。先构造共享表同列的不同 seed/increment、identity/sequence 及不同 sequence 名/模式模型，核对锁定 EF 的最终化是否已有其他阻断。确认后比较有效策略，区分真实冲突与共享主键外键的约定性 None。

### I03 · 日期值转换器

ToString 在 [DamengDateTimeToStringTranslator.cs:41](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeToStringTranslator.cs:41) 有原生日期检查；成员翻译、[DamengDateTimeMethodTranslator.cs:39](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeMethodTranslator.cs:39) 及 [DamengDateBucketMethodTranslator.cs:37](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateBucketMethodTranslator.cs:37) 没有同等守卫。已有 [DamengDateTimeStringTranslationTests.cs:175](/Users/wanghongyi/Projects/dameng-entityframework-core/test/W.EntityFrameworkCore.Dameng.Tests/DamengDateTimeStringTranslationTests.cs:175) 的 HasConversion<long> 模型可用于最小复现。先验证 Year/Date/AddDays/分桶的 SQL 和结果，再统一边界，不能禁止仍保存原生时态语义的所有转换器，也不改变已确定的 ToString 服务器格式合同。

### I04 · 普通列目录一致性

[DamengDatabaseModelFactory.cs:877](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Scaffolding/Internal/DamengDatabaseModelFactory.cs:877) 原生预检已枚举所有普通列，却只保留虚拟列；[DamengDatabaseModelFactory.cs:440](/Users/wanghongyi/Projects/dameng-entityframework-core/src/W.EntityFrameworkCore.Dameng/Scaffolding/Internal/DamengDatabaseModelFactory.cs:440) 只核对虚拟列完整性。若兼容目录漏掉无约束普通列，可能静默漏列。先用合成目录验证，再定义原生/兼容目录对照范围；没有观察到真实服务器目录漏行，视图不能直接套用普通表规则。

### I05 · LOB 与 PK_WITH_CLUSTER

无注解主键生成普通 PRIMARY KEY，模型检查只拒绝 LOB 自身做键，不处理“普通键+其他 LOB 列”。仓库 SQL skill 要求该组合显式 NOT CLUSTER；[达梦错误码说明](https://eco.dameng.com/document/dm/zh-cn/faq/faq-errorcode.html) 也列出聚集 KEY 与大字段冲突。现有 PK_WITH_CLUSTER=0 证据不能外推。

先验证手写模型 SQL，再在独立合适 profile 测试；禁止为审查修改共享实例全局参数。尊重显式与反向工程聚集注解，明确冲突处理。

### I06 · 嵌套复杂属性

rowversion 校验只枚举实体直接声明属性。EF 10 提供复杂类型属性的 rowversion 配置接口，但本轮未构造模型，也未证明上游最终化一定放行。先比较平铺、单层、嵌套 ComplexProperty，确认可达性后再递归校验所属实体/触发器/计算列语义；保留为低置信度调查，不能先改代码。

## 方向选项（不与缺陷混排）

1. **D01：提交已发生但 ACK 丢失的 EF 端到端验收。** [compatibility.md:145](/Users/wanghongyi/Projects/dameng-entityframework-core/docs/compatibility.md:145) 和 [w-dmprovider-validation.md:280](/Users/wanghongyi/Projects/dameng-entityframework-core/docs/w-dmprovider-validation.md:280) 留有明确缺口；现有代理重置主要验证未提交 SaveChanges。价值是确定“客户端报错但业务已提交”的异常链、事务结果与独立读回合同。工作量 L、风险中、依据置信度高；先做可重复故障注入，不改变 W 禁重试策略。
2. **D02：默认 TLS 的独立验收 profile。** [compatibility.md:135](/Users/wanghongyi/Projects/dameng-entityframework-core/docs/compatibility.md:135) 仅有明文 UTF-8 profile 的 EF 证据，而 W 默认要求 TLS。工作量 L、风险低至中、依据置信度高；需要环境，分别验证匹配能力成功与七位模型等不匹配配置的明确拒绝，不能把模型精度不匹配误判为 TLS 全面失败。
3. **D03：按已有 M2 设计接入 DataSource，再做作用域内 LOB 流。** [w-dmprovider-integration-plan.md:274](/Users/wanghongyi/Projects/dameng-entityframework-core/docs/w-dmprovider-integration-plan.md:274) 已定义调用方拥有数据源、reader 生命周期内回调和不可重放输入规则。价值是共享容量控制及减少大对象完整物化。工作量 L、风险高、依据置信度高；先数据源所有权验证，再 LOB 生命周期/取消/事务，不另造驱动池或让裸流逃逸。

## 建议的依赖与选择

建议首批选择 **F01、F02、F03、I01、F04**。F05 可作为前置验证建设单独加入；修正不会自动依赖 CI 已上线，但必须先建立对应失败回归。

- F01 与 F08 同改 CLI 夹具，应先统一凭据、清理与程序集来源，再重跑 CLI。
- F03 守卫回归先于 D03 的数据源扩展；F09 的会话测试修订应先于扩大故障合同，D01 可复用其可靠的会话观察方式。
- I01 必须先产生字面量/参数对照证据，之后才能更改时间映射或兼容性声明。
- I02/I03/I04/I05/I06 均先写最小表征；若不能复现则关闭或重定界，不强行实现猜测。
- F04/F07/F13 涉及真实迁移路径，需两驱动顺序验证及唯一对象 finally 精确清理；F11 的恢复测试不允许批量删库对象。
- 单元/SQL 文本通过不能替代跨驱动/服务器边界的真实回归。支持声明变化时更新 compatibility，保留锁定 EF/驱动范围。

本文件保留审查阶段的结果及修复草图。审查结束时，用户曾选择只保留报告；随后明确要求将全部问题制定为分批修复计划。后续安排已写入[六个里程碑](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/milestones.md)和[20 份计划索引](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/README.md)，以上首批建议保留为历史审查判断。计划编写不代表已经实施修复，本报告的原始证据与置信度不因此改变。

## 已考虑并排除

| 候选 | 处置原因 |
| --- | --- |
| 官方驱动同步回退、取消有限 | 已记录的驱动能力边界 |
| W 禁止内建自动重试 | 已定 FailureInfo/不确定结果策略 |
| 单命令修改批次 | 两驱动均无往返优化的相反证据 |
| DateTime 无参 ToString 文化差异或 GroupBy 推断时间粒度 | 维护者已明确选择服务器文本语义，不改变业务粒度 |
| 普通 OrderBy 的 NULL 位置 | 未找到 CLR 顺序一致性承诺，不擅自类比 LISTAGG |
| 当前模式反向工程、全部序列枚举、不可表示结构拒绝 | 明确的无损边界 |
| 超过 128 表回退扫描、IDENTITY 每批 64 表 | 已记录预算且已消除逐表往返；无新基准支持重写 |
| 大型反向工程工厂应立即拆分 | 单凭行数不足以证明收益；先修具体守卫与证据 |
| 原始模型 SQL/计算列 SQL 是注入 | 属可信开发者扩展点，未发现越过实际信任边界的路径 |
| 官方 actions 未固定 SHA 即漏洞 | 未找到具体可达漏洞，避免泛报 |
| 物理数据库创建/删除不支持 | 管理既有实例/模式是产品范围 |
| 四项规范冒烟不够即现有功能不合格 | 文档如实限定；未来可按 [EF 官方建议](https://github.com/dotnet/entityframework.docs/blob/main/entity-framework/core/providers/writing-a-provider.md) 渐进继承测试类，不能冒称一致性 |
| 再开 JSON 标量翻译 | 现有无损门槛未通过，不因换驱动自动解锁 |
| CLI --version 未排 stderr/无超时 | 静态韧性弱点，但正常输出很小，暂不单独立项 |
| 当前 EF/驱动应整体升级 | 此次保留锁和已验范围；漏洞查询未提供必须迁移的依据 |

## 未审范围与证据限制

未运行新候选的最小 C# 复现、完整实库、管理员 DDL、锁争用、网络故障注入、TLS/其他版本/页大小 profile、实际执行计划和性能基准；未检查 GitHub 当前分支保护/外部 CI、线上部署、实际凭据泄漏或历史残留。未审查驱动内部实现、反编译官方 EF 提供程序或执行 GUI 验证。已阅读仓库内所有主要包与相应测试/文档，但不把审查理解为穷尽所有 EF 表达式组合。

未读取历史记忆资料作为本轮证据。历史文档中的通过数字仅作设计背景；当前验证结果单列于本报告开头。所有未来修复必须在实施时再次检查 HEAD 与工作区漂移。
