# 兼容性与验证

## 证据术语

本矩阵记录截至 2026-09-17 的仓库基线。

- **真实环境已验证**：自动化提供程序测试已在参考达梦 8.1.5.60 服务器上通过。
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
| 字符串翻译 | 部分支持 / 子集真实环境已验证 | 真实执行覆盖 `Trim`、`Length`、`Contains`、`StartsWith`、`EndsWith`，以及默认的 `string.Compare` / `CompareTo`（生成 SQL 比较运算符，语义跟随达梦排序规则，而不是 .NET 区域性）。`IndexOf`、`Replace`、大小写转换、子字符串及其他 trim 结构的生成 SQL 有单元测试覆盖。`IsNullOrWhiteSpace`、`PadLeft`/`PadRight`、带 `char[]` 的 `Trim`、`Split`，以及带 `StringComparison` 的 `Contains`/`StartsWith`/`EndsWith`/`IndexOf`/`Compare` 无法翻译 |
| 日期/时间与 GUID 翻译 | 部分支持 / 子集真实环境已验证 | 真实执行覆盖 `DateTime` 年/月提取、整数 `AddDays`、`DateTimeOffset` 列比较和 `Guid.NewGuid()`/`NEWID()`；其他已实现的 `DateTime` 成员和整数 `DATEADD` 单位具有生成 SQL 测试。`DateTimeOffset` 的日期部件、`Offset`、`UtcDateTime`、`Now`/`UtcNow` 和 `Add*` 无法翻译 |
| 小数 `DateTime.Add*` | 无法无损翻译时不支持 | 参数化或非整数 double 参数不会被静默截断；EF 会报告无法翻译的表达式 |
| 跟踪式 CRUD | 真实环境已验证 | Unicode 插入/读取/更新/删除、null、转换器、标识列键回读和受影响行报告 |
| 标识列 | 真实环境已验证 / 部分方面单元级已验证 | 生成的键及通过 `SCOPE_IDENTITY()` 回读已在服务器执行；约定和显式种子/增量有单元测试覆盖 |
| 序列 | 真实环境已验证 | `sequence.NEXTVAL` 默认值和通过 `sequence.CURRVAL` 回读生成的键；不使用标准 `NEXT VALUE FOR` |
| 乐观并发 | 真实环境已验证 | 过期并发标记通过驱动程序已验证的 `SQL%ROWCOUNT` 结果协议抛出异常 |
| `ExecuteUpdate` / `ExecuteDelete` | 真实环境已验证 | 受影响行数以及持久化的布尔值/转换值更新 |
| 修改批处理 | 受驱动程序限制 | 驱动程序没有提供程序专用的 `DbBatch`；提供程序使用 `SingularModificationCommandBatch` |
| 常见标量映射 | 真实环境已验证 | 有符号整数、无分面 decimal 与 decimal(38,20)、bool、GUID、Unicode/CJK、可空值、`DateOnly`、微秒精度 `TimeOnly` 和精度为 7 的 `DateTime` |
| `DateTimeOffset` | 真实环境已验证 / 查询部分支持 | 存储映射为 `DATETIME(7) WITH TIME ZONE`；精度为 7 的往返和提供程序文本回读保留原始偏移量，包括 `+08:00`。列比较查询已在参考服务器执行。日期部件、偏移量和 `Add*` 方法无法翻译 |
| `TimeSpan` | 真实环境已验证 | `INTERVAL DAY(9) TO SECOND(6)`，包括超过两位天数的精确正值和负值；字面量保留映射的天/小数秒精度，并拒绝造成信息损失的 tick |
| 二进制与 LOB 映射 | 真实环境已验证 / 查询语义部分支持 | `VARBINARY`、40 KiB `BLOB` 和 40 KiB Unicode `NCLOB` 往返。通过 `TEXT_EQUAL`/`BLOB_EQUAL` 进行参数相等比较，以及通过 NCLOB `INSTR` 搜索，均已在参考服务器执行；排序、分组、distinct 和 distinct 集合操作会提前失败。字符串搜索函数仍受 `CLOB_MAX_CALC_LEN` 约束。键/索引需要有界行内类型，可用行内长度由页面/行配置决定 |
| JSON 存储 | 真实环境已验证 / 查询不支持 | `JsonElement` 通过 `JSON` 往返。整值 `Equals` 会生成 `"column" = :parameter`，但参考服务器以“数据类型不匹配”拒绝该比较；`GetProperty` 及 JSON 运算符无法翻译 |
| 无符号整数与分面边界 | 单元级已验证 | 保持范围的转换器和存储映射已有覆盖；尚未将广泛的真实服务器边界数据作为发布声明 |
| 事务 | 真实环境已验证 | 已验证 EF 事务提交/回滚行为 |
| 保存点 | 真实环境已验证 | 尽管驱动程序的基础能力标志不支持，参考驱动程序/服务器仍可创建保存点并回滚到保存点 |
| 隔离级别 | 部分支持 / 子集真实环境已验证 | `ReadCommitted` 下的 EF 事务提交/回滚与跟踪式 `SaveChanges` 已在参考服务器执行。`RepeatableRead` 和 `Snapshot` 会在 `BeginTransaction` 时被驱动程序拒绝。`ReadUncommitted` 和 `Serializable` 可以开始事务并执行查询，但跟踪式 `SaveChanges` 会因驱动程序 `CommandText has no value` 失败；`Serializable` 下的 `ExecuteUpdate` 可以持久化更改 |
| 重试执行策略 | 单元级已验证 | 保守的 `DmException.Number` 分类和有界设置；当前没有真实故障注入套件证明每个错误码都可恢复 |
| DDL 事务性 | 不支持原子迁移 | 达梦 DDL 会隐式提交；生成的 DDL 命令会禁用 EF 事务 |
| 创建/删除物理数据库 | 不支持 | `Create`/`Delete` 会抛出异常；应连接到现有数据库并管理当前模式中的对象 |
| 迁移 DDL | 部分支持 / 子集真实环境已验证 | 真实测试覆盖表、标识列、序列、索引、虚拟计算列和清理；单元测试覆盖种子数据 SQL、其他关系约束和显式拒绝路径 |
| 迁移历史记录 | 真实环境已验证 | 存在性、按需创建、插入、查询和删除路径 |
| 迁移锁 | 真实环境已验证 / 服务器特定 | 使用 `DBMS_LOCK`；已在参考非 MPP 服务器上验证。达梦 MPP 不提供相同基线 |
| 幂等迁移脚本 | 真实环境已验证 / 部分支持 | 生成的命令在迁移历史记录 DMSQL 守卫中使用已转义的 `EXECUTE IMMEDIATE`。重复执行、Unicode、引号、标识列种子/会话命令和历史记录插入均在参考服务器通过。转义后 UTF-8 表示超过 32767 字节的动态命令字面量会提前失败，必须拆分 |
| 存储计算列 | 不支持 | 支持虚拟计算列；拒绝存储计算列 |
| 筛选索引 | 不支持 | 提供程序会拒绝迁移索引筛选器，而不是生成其他数据库的语法 |
| 修改标识列 | 不支持 | 拒绝通过 `ALTER COLUMN` 添加/移除 `IDENTITY`，或修改其种子/增量；应重新创建该列 |
| 跨模式重命名 | 不支持 | 不能通过表/序列重命名将对象移动到其他模式 |
| TPT/TPC 值生成 | 单元级已验证 / 部分支持 | TPT 仅向根表应用标识列/序列生成。由于多个具体表可能发生冲突，因此拒绝 TPC 标识列；可改用共享达梦序列 |
| 模式创建与不常见 DDL | 部分支持 | 仅声明支持提供程序测试所覆盖的操作；不得推断已覆盖所有 EF 迁移操作 |
| 设计时迁移代码生成 | 单元级已验证 / 部分支持 | 提供程序和注解代码生成器会生成 `UseDameng` 和达梦值生成 API；端到端 `dotnet ef` 覆盖仍然有限 |
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

## 真实数据库测试契约

真实数据库测试接收一个机密环境变量：

```text
DAMENG_TEST_CONNECTION_STRING
```

该变量包含完整的 `DM.DmProvider` 连接字符串。测试和日志不得打印、快照记录或提交它。
配置的账户必须能够在自己的模式中创建/删除表、索引、约束和序列，执行 DML，使用
事务/保存点，访问迁移历史记录以及调用 `DBMS_LOCK`。该账户不需要创建或删除物理数据库的权限。

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
