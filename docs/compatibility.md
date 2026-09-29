# 兼容性与验证

## 证据术语

原有矩阵记录截至 2026-09-22 的仓库基线。2026-09-17 的参考记录仍写明服务器版本 8.1.5.60。2026-09-22 的全量功能测试和迁移脚本测试运行在另一轮真实库上，该实例自报为 `DM Database Server 64 V8`、`DB Version: 0x7000d`、构建号 `03134284604-20260707-335949-20228`，`PAGE()` 为 32768，`COMPATIBLE_MODE` 为 0。这次运行没有返回 8.1.5.60，文档不把两个版本号当成同一个事实。

2026-09-29 的查询增量另在用户指定实例的独立测试用户/表空间验证。只读环境记录为
`DM Database Server 64 V8` / `DM Database Server x64 V8`、`PAGE()=32768`、
`COMPATIBLE_MODE=0`、`LENGTH_IN_CHAR=0`、`GLOBAL_CHARSET=1`、`CALC_AS_DECIMAL=0`、
`JSON_MODE=0`、`CLOB_MAX_CALC_LEN=20480`。没有将旧实例版本或构建号套用到新实例。
工作包、探针与回归结果见[实施台账](query-translation-execution.md)；探针成功执行不等于功能验收。

- **真实环境已验证**：自动化提供程序测试已在达梦服务器上通过。版本标识以文首两轮记录为准。
- **单元级已验证**：确定性的提供程序测试在无服务器环境下覆盖 SQL、元数据或服务行为。
  这不属于运行时证明。
- **部分支持**：已实现实用路径，但受支持的 EF 或达梦范围有意小于该领域名称所暗示的范围。
- **受驱动程序限制**：提供程序公开了 EF API，但当前 `DM.DmProvider` 的行为限制其语义或性能。
- **未实现**：调用方不得依赖此能力。
- **不支持**：提供程序有意拒绝该结构，或无法提供 EF 所需的语义。

参考服务器和驱动程序只是一个证据点，不构成最低版本保证。仅有源文件或生成 SQL
断言不能被认定为真实环境验证。

## 能力矩阵

| 领域 | 状态 | 已验证范围与边界 |
| --- | --- | --- |
| .NET 10 连接 | 真实环境已验证 | `DM.DmProvider` 8.3.1.47463 加载其 `net9.0` 资产，并从 .NET 10 进程建立连接 |
| 公共配置 | 单元级已验证 | 六个 `UseDameng(...)` 重载覆盖泛型/非泛型构建器、连接字符串，以及由调用方/EF 拥有的 `DbConnection`；命令超时、迁移程序集和重试配置均已接通 |
| 标识符与参数 | 真实环境已验证 | 使用双引号引用标识符和 `:name` 参数；不生成 `@name` |
| 标量/无 FROM 查询 | 服务器探测 + 单元级已验证 | 服务器接受不含 `FROM` 的 `SELECT`；提供程序 SQL 生成和布尔值/搜索条件转换具有确定性测试 |
| 筛选与分页 | 真实环境已验证 | 参数化谓词、排序、`Skip`/`Take`、`OFFSET … FETCH`、`Any`、租户和软删除筛选器 |
| 字符串翻译 | 部分支持 / 子集真实环境已验证 | 原有真实执行覆盖 `Trim`、`Length`、`Contains`、`StartsWith`、`EndsWith`，以及默认的 `string.Compare` / `CompareTo`（比较遵循达梦排序规则）。2026-09-29 新增验证：`IsNullOrEmpty` 按长度区分空串与纯空格；二/三/四个 string 参数的 `Concat`、字符串 `+` 保留 null 视为空串语义；常量 BMP char 和非空常量 BMP char[] 的 `Trim`/`TrimStart`/`TrimEnd` 在有界文本、NCLOB 上执行。动态/null/空字符数组及 surrogate 修剪字符继续拒绝。无参数 Trim 的历史实现不构成全部 Unicode 空白保证。`IndexOf`、`Replace`、大小写和 Substring 保留原证据范围；`IsNullOrWhiteSpace`、Padding、Split 和 StringComparison 重载仍无法翻译 |
| LOB 拼接与空值回退 | 真实环境已验证 / 有服务器边界 | 修正 `COALESCE(NCLOB, '')` 的内部回退类型，显式转换空串为 NCLOB，避免在拼接前降为有界文本。20,000 字中文 NCLOB 拼接完整回读；普通长列回读、`?? ""`、双方 null 和两种 EF 空值模式具有回归。不承诺所有 LOB 运算或任意长度均可执行 |
| 数值函数翻译 | 部分支持 / 真实环境已验证 | 限可表示的有限值；NaN/正负 Infinity 参数在本实例执行时报数据溢出。`Math.Abs`/`Sign` 的 int、long、decimal、double 重载，`Floor`/`Ceiling` 的 decimal、double 重载；`Exp`、`Log`、二参数 `Log`、`Log10`、`Pow`、`Sqrt` 的 double 重载。int Abs 显式恢复 int 范围，最小有符号整数取绝对值报溢出；decimal 不经过 double。对数底数会按达梦顺序交换。正常定义域有结果验证；域外或溢出可能抛数据库错误，不等同 CLR NaN/Infinity。Round、三角函数、MathF 与未列重载不在本批支持范围 |
| 日期分桶函数 | 真实环境已验证 / 子集 | `EF.Functions.DamengTruncateHour`、`DamengTruncateMinute`、`DamengStartOfIsoWeek`、`DamengQuarter`，各含 DateTime/DateTime? 重载。ISO 周返回所在周的周一零点日期；null 输入返回 null。覆盖跨年、闰日、四季度及筛选/分组/排序/投影。参考实例拒绝 TRUNC 的 SS 掩码，未发布秒截断 API |
| 字符串分组聚合 | 部分支持 / 真实环境已验证 | `string.Join(string, IEnumerable<string>)` 和 `string.Concat(IEnumerable<string>)` 的分组聚合，selector 和已映射的分隔符都必须是有界可变文本。组内排序显式使用升序 NULLS FIRST、降序 NULLS LAST；保留筛选、null/空元素的位置；null 分隔符视为空串，筛选后空集合和全 null 的 Concat 返回空串。Join 用分隔符前缀补偿 LISTAGG 跳过空元素的差异。Distinct、LOB、固定 CHAR/NCHAR 及 LOB 排序明确拒绝；无显式排序不承诺顺序，长度溢出报数据库错误，不截短。Join 中间结果多一个分隔符，可能先于最终 CLR 结果触及服务器长度边界 |
| 日期/时间与 GUID 翻译 | 部分支持 / 子集真实环境已验证 | 原有真实执行覆盖 `DateTime` 年/月、整数 AddDays、DateTimeOffset 列比较和 Guid.NewGuid()/NEWID()；其他 DateTime 成员和整数 DATEADD 保留原单元证据。2026-09-29 新增 DateTimeOffset 的 Year/Month/Day/Hour/Minute/Second 真库回归，按列保存的原偏移提取本地部件，覆盖同瞬间不同日历日期及可空值。Offset、UtcDateTime、DateTime、Now/UtcNow 和 Add* 仍不翻译；本批不承诺 100ns 精度或时区转换 |
| 集合 Contains | 有界回归范围真实环境已验证 | 常量/参数集合，参数集合大小 0/1/499/500/501/998/999/1000/2000，含 null、重复、否定、双集合与普通筛选、排序分页和同查询 1→1000→1 重用。每次查询一条命令。本实例证据不支持写死 500 个 IN 元素或 999 个总参数限制，因此本轮未增加拆分或上限；2,000 只是已测样本，不是最大容量或性能保证 |
| 小数 `DateTime.Add*` | 无法无损翻译时不支持 | 参数化或非整数 double 参数不会被静默截断；EF 会报告无法翻译。2026-09-29 候选日期算术和 INTERVAL 路径的第七位小数未满足保真要求，服务器文本输出也未保留探针中的 100ns 增量，本轮未扩大支持 |
| 跟踪式 CRUD | 真实环境已验证 | Unicode 插入/读取/更新/删除、null、转换器、标识列键回读和受影响行报告 |
| 标识列 | 真实环境已验证 / 部分方面单元级已验证 | 生成的键及通过 `SCOPE_IDENTITY()` 回读已在服务器执行；约定和显式种子/增量有单元测试覆盖 |
| 序列 | 真实环境已验证 | `sequence.NEXTVAL` 默认值和通过 `sequence.CURRVAL` 回读生成的键；不使用标准 `NEXT VALUE FOR` |
| 乐观并发 | 真实环境已验证 | 过期并发标记通过驱动程序已验证的 `SQL%ROWCOUNT` 结果协议抛出异常 |
| `ExecuteUpdate` / `ExecuteDelete` | 真实环境已验证 | 受影响行数以及持久化的布尔值/转换值更新 |
| 修改批处理 | 受驱动程序限制 | 驱动程序没有提供程序专用的 `DbBatch`；提供程序使用 `SingularModificationCommandBatch` |
| 常见标量映射 | 真实环境已验证 | 有符号整数、无分面 decimal 与 decimal(38,20)、bool、GUID、Unicode/CJK、可空值、`DateOnly`、微秒精度 `TimeOnly` 和精度为 7 的 `DateTime` |
| `DateTimeOffset` | 真实环境已验证 / 查询部分支持 | 存储映射为 `DATETIME(7) WITH TIME ZONE`；已有往返样本通过提供程序文本回读保留原始偏移量，包括 `+08:00`。列比较及 `Year`/`Month`/`Day`/`Hour`/`Minute`/`Second` 六个本地日期部件已验证，部件按列上保存的原偏移提取。`Offset`、`UtcDateTime`、`DateTime`、`Now`/`UtcNow` 和 `Add*` 仍不翻译；映射精度声明不构成第七位小数保真保证 |
| `TimeSpan` | 真实环境已验证 | `INTERVAL DAY(9) TO SECOND(6)`，包括超过两位天数的精确正值和负值；字面量保留映射的天/小数秒精度，并拒绝造成信息损失的 tick |
| 二进制与 LOB 映射 | 真实环境已验证 / 查询语义部分支持 | `VARBINARY`、40 KiB `BLOB` 和 40 KiB Unicode `NCLOB` 往返。通过 `TEXT_EQUAL`/`BLOB_EQUAL` 进行参数相等比较，以及通过 NCLOB `INSTR` 搜索，均已在参考服务器执行；排序、分组、distinct 和 distinct 集合操作会提前失败。字符串搜索函数仍受 `CLOB_MAX_CALC_LEN` 约束。键/索引需要有界行内类型，可用行内长度由页面/行配置决定 |
| JSON 存储 | 部分支持 / 查询不支持 | 保留原有 `JsonElement` 的 JSON 往返测试范围。2026-09-29 新实例探针中，原生 JSON 列的补充平面 Unicode 原文回读已失真，不能扩大为完整 Unicode 保证；JSON_VALUE 还将 JSON 空字符串读为 SQL null，数值返回可隐式舍入/转换。因未满足无损契约，本轮未发布 JSON 标量 API；`GetProperty` 仍不翻译。整值 Equals 仍生成列比较并被参考服务器以数据类型不匹配拒绝 |
| 无符号整数与分面边界 | 单元级已验证 | 保持范围的转换器和存储映射已有覆盖；尚未将广泛的真实服务器边界数据作为发布声明 |
| 事务 | 真实环境已验证 | 已验证 EF 事务提交/回滚行为 |
| 保存点 | 真实环境已验证 | 尽管驱动程序的基础能力标志不支持，参考驱动程序/服务器仍可创建保存点并回滚到保存点 |
| 隔离级别 | 部分支持 / 子集真实环境已验证 | `ReadCommitted` 下的 EF 事务提交/回滚与跟踪式 `SaveChanges` 已在参考服务器执行。`RepeatableRead` 和 `Snapshot` 会在 `BeginTransaction` 时被驱动程序拒绝。`ReadUncommitted` 和 `Serializable` 可以开始事务并执行查询，但跟踪式 `SaveChanges` 会因驱动程序 `CommandText has no value` 失败；`Serializable` 下的 `ExecuteUpdate` 可以持久化更改 |
| 重试执行策略 | 单元级已验证 | 保守的 `DmException.Number` 分类和有界设置；当前没有真实故障注入套件证明每个错误码都可恢复 |
| DDL 事务性 | 不支持原子迁移 | 达梦 DDL 会隐式提交；生成的 DDL 命令会禁用 EF 事务 |
| 创建/删除物理数据库 | 不支持 | `Create`/`Delete` 会抛出异常；应连接到现有数据库并管理当前模式中的对象 |
| 迁移 DDL | 部分支持 / 子集真实环境已验证 | 2026-09-22 已在独立用户模式中执行 `Database.GenerateCreateScript()`、非幂等 `IMigrator.GenerateScript()`、幂等脚本的重复执行，以及 `Database.Migrate()`。覆盖标识列种子与 `SET IDENTITY_INSERT`、序列 `NEXTVAL`、虚拟计算列、主键、唯一约束、检查约束、`ON DELETE CASCADE`、升降序唯一索引、额外模式中的表、列/表/索引重命名、带默认值的非空列（已有行回填为该默认值）和删除列。在第一次 `NEXTVAL` 之前把增量从 3 改成 5 时，第一个值是 43 而不是 41。筛选索引、存储计算列、修改标识列和跨模式重命名仍在生成时拒绝 |
| 迁移历史记录 | 真实环境已验证 | 存在性、按需创建、插入、查询和删除路径。存在性查询 `SYS.SYSOBJECTS`。`RESOURCE` 可以执行生成的脚本，但不能做这次查询；2026-09-22 的实例上要再授予 `SOI`，`Database.Migrate()` 才能通过。脚本里的历史插入不经过这次查询 |
| 迁移锁 | 真实环境已验证 / 服务器特定 | 使用 `DBMS_LOCK`；已在参考非 MPP 服务器上验证。达梦 MPP 不提供相同基线 |
| 幂等迁移脚本 | 真实环境已验证 / 部分支持 | `IMigrator.GenerateScript(Idempotent)` 在历史表守卫中使用已转义的 `EXECUTE IMMEDIATE`，块以单独一行 `/` 结束。2026-09-22 将 `/` 剔除后，把每个 `BEGIN ... END;` 作为一条命令执行了两遍，Unicode 和引号种子只保留一行，历史记录不重复。`/` 本身不能放进 ADO.NET 命令。转义后 UTF-8 表示超过 32767 字节的动态命令字面量会提前失败，必须拆分 |
| 存储计算列 | 不支持 | 支持虚拟计算列；拒绝存储计算列 |
| 筛选索引 | 不支持 | 提供程序会拒绝迁移索引筛选器，而不是生成其他数据库的语法 |
| 修改标识列 | 不支持 | 拒绝通过 `ALTER COLUMN` 添加/移除 `IDENTITY`，或修改其种子/增量；应重新创建该列 |
| 跨模式重命名 | 不支持 | 不能通过表/序列重命名将对象移动到其他模式 |
| TPT/TPC 值生成 | 单元级已验证 / 部分支持 | TPT 仅向根表应用标识列/序列生成。由于多个具体表可能发生冲突，因此拒绝 TPC 标识列；可改用共享达梦序列 |
| 模式创建与不常见 DDL | 部分支持 | 2026-09-22 已执行 `CREATE SCHEMA` 以及该模式中的表。其余未测试的 DDL 不能据此视为已支持 |
| 设计时迁移代码生成 | 单元级已验证 / 部分支持 | 提供程序和注解代码生成器会生成 `UseDameng` 和达梦值生成 API。进程内 `GenerateCreateScript`、`IMigrator.GenerateScript` 和 `Database.Migrate()` 已在服务器执行。`dotnet ef` 命令行端到端覆盖仍然有限。人工步骤见 [迁移操作说明](migrations.md) |
| 反向工程 | 未实现 / 单元级已验证为缺失 | 设计时服务不注册 `IDatabaseModelFactory`；`dotnet ef dbcontext scaffold` 不在当前提供程序范围内 |
| EF 关系数据库规范一致性 | 未声明 | 规范测试项目包含四项提供程序自有冒烟测试并使用 EF 测试工具，但不继承上游关系数据库 `*TestBase` 测试套件 |
| 裁剪 / NativeAOT | 未验证 | 不对提供程序或当前驱动程序的裁剪、编译模型优化或 NativeAOT 兼容性作任何声明 |
| 异步 I/O 与取消 | 受驱动程序限制 | 已测试的驱动程序资产会回退到同步 ADO.NET 实现；EF 异步 API 仍然可用，但无法保证非阻塞 I/O 或及时取消 |
| 连接超时 | 未验证的驱动程序设置 | EF `CommandTimeout` 的单位为秒。本仓库不对驱动程序连接字符串的超时关键字或单位作任何断言 |

最终的 2026-09-17 验证快照：

- 锁定模式还原：成功；
- Release 构建：0 个警告，0 个错误；
- 确定性单元测试套件：188/188 通过；
- 参考服务器功能测试套件：44/44 通过；
- 提供程序自有关系数据库冒烟测试套件：4/4 通过；
- 标准 `dotnet format --verify-no-changes`：通过。

2026-09-22 脚本执行验证（Debug，不是 Release 发布快照）：

- 确定性单元测试套件：188/188 通过；
- 功能测试套件：48/48 通过，其中 4 项为迁移脚本执行；测试在管理员连接上创建独立表空间和用户，结束时 `DROP USER ... CASCADE` 与 `DROP TABLESPACE` 已确认对象不留存；
- 提供程序自有关系数据库冒烟测试套件：4/4 通过；
- `dotnet format --verify-no-changes`：通过；
- 未重新做 Release 构建。上面的 2026-09-17 Release 快照仍然有效，但不能把 48 这个数字写回那次快照。

## 真实数据库测试契约

真实数据库测试接收一个机密环境变量：

```text
DAMENG_TEST_CONNECTION_STRING
```

该变量包含完整的 `DM.DmProvider` 连接字符串。测试和日志不得打印、快照记录或提交它。
配置的账户必须能够在自己的模式中创建/删除表、索引、约束和序列，执行 DML，使用
事务/保存点，访问迁移历史记录以及调用 `DBMS_LOCK`。`Database.Migrate()` 的历史表存在性检查还要能查询 `SYS.SYSOBJECTS`；2026-09-22 的实例上，`RESOURCE` 不够，需要 `SOI`。只执行生成脚本、不调用 `Migrate()` 时，`RESOURCE` 已覆盖本次脚本测试中的 DDL 和 DML。该账户不需要创建或删除物理数据库的权限。脚本测试使用的管理员连接另外需要创建和删除表空间与用户。

测试隔离规则：

1. 为每组对象生成唯一、可识别的前缀。
2. 将发现和清理限制在该前缀及当前用户模式内。
3. 在 `finally` 中删除精确对象，包括断言失败之后。
4. 绝不依赖回滚清理 DDL，因为 DDL 会隐式提交。
5. 不得在测试输出中持久化主机名、用户名、密码或完整连接字符串。

直接运行数据库测试项目：

```bash
export DAMENG_TEST_CONNECTION_STRING='<完整连接字符串>'

dotnet test \
  test/W.EntityFrameworkCore.Dameng.FunctionalTests/W.EntityFrameworkCore.Dameng.FunctionalTests.csproj

dotnet test \
  test/W.EntityFrameworkCore.Dameng.Specification.Tests/W.EntityFrameworkCore.Dameng.Specification.Tests.csproj
```

缺少该变量时，数据库事实测试会被明确跳过。跳过测试对离线开发有帮助，但不能作为发布证据。

## 规范测试边界

`W.EntityFrameworkCore.Dameng.Specification.Tests` 引用
`Microsoft.EntityFrameworkCore.Relational.Specification.Tests` 以使用共享测试工具。
其当前测试是提供程序自有的冒烟场景，覆盖：

- 提供程序/测试存储接线；
- 标量值往返；
- 参数化查询/排序/投影；
- 跟踪式更新/删除的受影响行数。

它不继承任何上游 EF 关系数据库基础测试套件。因此，“规范测试项目通过”仅表示这一
狭窄切片通过，不得将其报告为 EF Core 关系数据库一致性。

## 达梦参考资料

- [DML、`RETURNING` 与 `MERGE`](https://eco.dameng.com/document/dm/zh-cn/pm/insertion-deletion-modification.html)
- [查询子句与分页](https://eco.dameng.com/document/dm/zh-cn/pm/check-phrases.html)
- [DDL 与序列](https://eco.dameng.com/document/dm/zh-cn/pm/definition-statement.html)
- [事务](https://eco.dameng.com/document/dm/zh-cn/pm/management-affairs.html)
- [`DBMS_LOCK`](https://eco.dameng.com/document/dm/zh-cn/pm/dbms_lock-package.html)
- [JSON](https://eco.dameng.com/document/dm/zh-cn/pm/json)
