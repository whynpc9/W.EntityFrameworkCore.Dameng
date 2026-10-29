# 兼容性与验证

## 预先已知的官方驱动风险：时态参数精度

锁定的官方 `DM.DmProvider` 存在维护者已知并接受的时态参数实现缺陷：在本轮参考实例上，
精确保存的 `2024-02-29 13:14:15.1234567` 用精确 SQL 常量相等比较能命中，使用官方驱动参数
比较却返回空集；7/8/9 位目标列均复现，整微秒对照通过。列精度声明和字面量修复不能保证
官方参数的第七位正确。该差异作为使用官方驱动时应预先了解的风险保留，不阻断本项目验收，
也不表示本项目修复了官方驱动。其他操作/输入的影响没有被本轮样本穷尽。

依赖逐 tick 查询正确性的使用方应使用已验证 profile 上的 `UseDamengWithWDriver`；
W 的精度能力仍受 `ServerCapabilities` 与本文对应边界约束。本轮两驱动的缺陷表征、
正对照及完整 ticks/offset 证据见 [006 执行记录](../plans/evidence/006.md)。
此风险不影响本轮对 SQL 字面量、迁移种子及默认值路径的独立保真要求。

## 证据与版本

当前源码面向 .NET 10，EF Core Relational 范围为 `[10.0.12,11.0.0)`，官方驱动范围为 `[8.3.1.47463,8.4.0)`，锁文件实际解析为 `8.3.1.47463`。本次发行版本为基础包 `0.5.0`、W 扩展包 `0.5.0-preview.1`；实际发布状态及本轮未通过验收项见 [发布记录](release-0.5.0.md)。

当前源码的 W 扩展项目精确依赖 `W.DamengProvider [0.3.1]`；本次 `W.EntityFrameworkCore.Dameng.WDriver 0.5.0-preview.1` 精确依赖基础包 `[0.5.0]`，上一版 `0.4.0-preview.1` 依赖 W `0.2.0`。

- **真实环境已验证**：自动化提供程序测试已在达梦服务器上通过，具体版本、profile 和运行记录以对应发布/验证文档为准。
- **单元级已验证**：确定性测试在无服务器环境下覆盖 SQL、元数据或服务行为，不属于运行时证明。
- **部分支持**：已实现实用路径，但支持范围有意小于该领域名称所暗示的范围。
- **受驱动程序限制**：提供程序公开了 EF API，但当前驱动行为限制其语义或性能。
- **未实现**：调用方不得依赖此能力。
- **不支持**：提供程序有意拒绝该结构，或无法提供 EF 所需语义。

参考服务器和驱动程序只是证据点，不构成最低版本保证。不同实例的版本号、构建号和参数不得拼接成同一次验证；仅有源文件或生成 SQL 断言也不能认定为真实环境验证。探针成功执行只表示测量完成，不等于功能验收。历史发布与实施记录见[文档索引](README.md)。

## 能力矩阵

下表除 W 小节外描述官方驱动及两种驱动共享的能力。W 的行为差异必须按驱动和 profile 单独阅读。

| 领域 | 状态 | 已验证范围与边界 |
| --- | --- | --- |
| .NET 10 连接 | 真实环境已验证 | `DM.DmProvider` 8.3.1.47463 加载其 `net9.0` 资产，并从 .NET 10 进程建立连接；这是观察到的兼容性，不是原生 .NET 10 驱动保证 |
| 公共配置 | 单元级已验证 | 六个 `UseDameng(...)` 重载覆盖泛型/非泛型构建器、连接字符串，以及由调用方/EF 拥有的 `DbConnection`；命令超时、迁移程序集和重试配置均已接通 |
| 标识符与参数 | 真实环境已验证 | 使用双引号引用标识符和 `:name` 参数；不生成 `@name` |
| 标量/无 FROM 查询 | 服务器探测 + 单元级已验证 | 服务器接受不含 `FROM` 的 `SELECT`；提供程序 SQL 生成和布尔值/搜索条件转换具有确定性测试 |
| 筛选与分页 | 真实环境已验证 | 参数化谓词、排序、`Skip`/`Take`、`OFFSET … FETCH`、`Any`、租户和软删除筛选器 |
| 字符串翻译 | 部分支持 / 子集真实环境已验证 | 真实执行覆盖 `Trim`、`Length`、`Contains`、`StartsWith`、`EndsWith` 以及默认 `string.Compare` / `CompareTo`；比较遵循达梦排序规则。`IndexOf`、`Replace`、大小写和 Substring 仅声明现有测试覆盖的形态。`IsNullOrEmpty` 按长度区分空串与纯空格；二/三/四个 string 参数的 `Concat`、字符串 `+` 保留 null 视为空串语义；常量 BMP char 和非空常量 BMP char[] 的 `Trim`/`TrimStart`/`TrimEnd` 支持有界文本与 NCLOB。动态/null/空字符数组及 surrogate 修剪字符拒绝；无参数 Trim 不保证全部 Unicode 空白。`IsNullOrWhiteSpace`、Padding、Split 和 StringComparison 重载仍无法翻译 |
| LOB 拼接与空值回退 | 真实环境已验证 / 有服务器边界 | `COALESCE(NCLOB, '')` 在函数内部把空串转换为 NCLOB，避免拼接前降为有界文本。20,000 字中文 NCLOB 拼接完整回读；普通长列回读、`?? ""`、双方 null 和两种 EF 空值模式具有回归。不承诺所有 LOB 运算或任意长度均可执行 |
| 日期字符串转换 | 部分支持 / 子集真实环境已验证 | 无参数 DateTime.ToString 使用服务器默认 `CAST(... AS VARCHAR(100))`，Nullable<DateTime>.ToString 保留 NULL 到空串回退；投影、筛选、排序和分组采用同一映射。常量数字日期格式的已验证子集映射 TO_CHAR；动态格式、IFormatProvider、时区/文化相关格式及转换到非日期存储类型的 DateTime 不支持。格式契约、重载及服务器精度边界见[日期字符串转换](datetime-string-translation.md) |
| 数值聚合与报表分组 | 部分支持 / 查询形状真实环境已验证 | Count/LongCount、Sum/Average/Min/Max、条件与筛选聚合、复合/日期/表达式键、HAVING、两层投影汇总、JOIN 和相关 EXISTS 已有聚焦回归，含空集与全 NULL。DateTime.ToString 分组按服务器文本结果执行，不推断业务时间粒度；未投影 IGrouping 上的外层 Sum 仍无法翻译；组内 nullable DISTINCT COUNT 忽略 NULL，不能声明 CLR NULL 单项语义。覆盖范围、查询改写及历史验证见[聚合验证](numeric-aggregate-validation.md) |
| 数值函数翻译 | 部分支持 / 真实环境已验证 | 限可表示的有限值；NaN/正负 Infinity 参数在参考实例执行时报数据溢出。支持 `Math.Abs`/`Sign` 的 int、long、decimal、double 重载，`Floor`/`Ceiling` 的 decimal、double 重载，以及 `Exp`、`Log`、二参数 `Log`、`Log10`、`Pow`、`Sqrt` 的 double 重载。int Abs 显式恢复 int 范围，最小有符号整数取绝对值报溢出；decimal 不经过 double；对数底数按达梦顺序交换。域外或溢出可能抛数据库错误，不等同 CLR NaN/Infinity。Round、三角函数、MathF 与未列重载不在支持范围 |
| 日期分桶函数 | 真实环境已验证 / 子集 | `EF.Functions.DamengTruncateHour`、`DamengTruncateMinute`、`DamengStartOfIsoWeek`、`DamengQuarter`，各含 DateTime/DateTime? 重载。ISO 周返回所在周的周一零点日期；null 输入返回 null。覆盖跨年、闰日、四季度及筛选/分组/排序/投影。参考实例拒绝 TRUNC 的 SS 掩码，未提供秒截断 API |
| 字符串分组聚合 | 部分支持 / 真实环境已验证 | `string.Join(string, IEnumerable<string>)` 和 `string.Concat(IEnumerable<string>)` 的分组聚合，selector 和已映射的分隔符都必须是有界可变文本。组内排序显式使用升序 NULLS FIRST、降序 NULLS LAST；保留筛选、null/空元素的位置；null 分隔符视为空串，筛选后空集合和全 null 的 Concat 返回空串。Join 用分隔符前缀补偿 LISTAGG 跳过空元素的差异。Distinct、LOB、固定 CHAR/NCHAR 及 LOB 排序明确拒绝；无显式排序不承诺顺序，长度溢出报数据库错误，不截短。Join 中间结果多一个分隔符，可能先于最终 CLR 结果触及服务器长度边界 |
| 日期/时间与 GUID 翻译 | 部分支持 / 子集真实环境已验证 | 真实执行覆盖 DateTime 年/月、整数 AddDays、DateTimeOffset 列比较和 Guid.NewGuid()/NEWID()；其他已列 DateTime 成员和整数 DATEADD 保留相应单元证据。DateTimeOffset 的 Year/Month/Day/Hour/Minute/Second 按列保存的原偏移提取本地部件，覆盖同瞬间不同日历日期及可空值。Offset、UtcDateTime、DateTime、Now/UtcNow 和 Add* 仍不翻译；不承诺这些查询路径具有 100ns 精度或时区转换能力 |
| 集合 Contains | 有界回归范围真实环境已验证 | 常量/参数集合，参数集合大小 0/1/499/500/501/998/999/1000/2000，含 null、重复、否定、双集合与普通筛选、排序分页和同查询 1→1000→1 重用。每次查询一条命令。现有证据不支持写死 500 个 IN 元素或 999 个总参数限制，因此未增加拆分或上限；2,000 只是已测样本，不是最大容量或性能保证 |
| 小数 `DateTime.Add*` | 无法无损翻译时不支持 | 参数化或非整数 double 参数不会被静默截断；EF 会报告无法翻译。候选日期算术和 INTERVAL 路径未满足第七位小数及 100ns 增量保真要求，未扩大支持 |
| 跟踪式 CRUD | 真实环境已验证 | Unicode 插入/读取/更新/删除、null、转换器、标识列键回读和受影响行报告 |
| 标识列 | 真实环境已验证 / 部分方面单元级已验证 | 生成的键及通过 `SCOPE_IDENTITY()` 回读已在服务器执行；约定和显式种子/增量有单元测试覆盖 |
| 序列 | 真实环境已验证 | `sequence.NEXTVAL` 默认值和通过 `sequence.CURRVAL` 回读生成的键；不使用标准 `NEXT VALUE FOR` |
| 非键生成值回读 | 真实环境已验证 / 子集 | 使用现有 DML + SELECT 路径：客户端单键/复合键、IDENTITY 和序列键插入后，Unicode/数值/NULL 默认值及虚拟计算列在同步/异步 SaveChanges 返回时已回填，无需 Reload。当前值和原始值、独立查询及外部事务回滚均有回归。其他策略的库端生成键继续拒绝，未引入 RETURNING 或输出参数 |
| 乐观并发 | 真实环境已验证 / 子集 | 应用管理的过期并发标记通过 `SQL%ROWCOUNT` 协议抛出异常。显式配置的 BEFORE UPDATE 行触发器版本标记支持更新后回读，连续更新、陈旧标记和已删除行的零行更新均有同步/异步回归；失败方不传播生成值。HasTrigger 只声明元数据，触发器需自行创建；不支持自动生成 SQL Server 风格 rowversion |
| `ExecuteUpdate` / `ExecuteDelete` | 真实环境已验证 | 受影响行数以及持久化的布尔值/转换值更新 |
| 修改批处理 | 单命令批次 / 受驱动程序限制 | 两种驱动都使用 `SingularModificationCommandBatch`。不据此声明底层驱动具有减少往返的专用批处理能力；数组绑定也不能替代生成值回读协议 |
| 常见标量映射 | 真实环境已验证 | 有符号整数、无分面 decimal 与 decimal(38,20)、bool、GUID、Unicode/CJK、可空值、`DateOnly`、微秒精度 `TimeOnly` 和精度为 7 的 `DateTime`。非 Unicode 定界字符串生成 `VARCHAR2(n CHAR)` / `CHAR(n CHAR)`；`LENGTH_IN_CHAR=0` 实例上 `VARCHAR(n)` 按字节截断中文，字符语义声明经真实库回读验证。自动推导按最小受支持页的保守 1900 字节预算（475 字符）；变长超出转 CLOB，定长超过预算时拒绝并要求显式的实例特定存储类型或改用变长列，不自动转换成二进制。显式存储类型按声明保留，由使用方核对实例页大小与整行预算。参考实例（页 32768、UTF-8）验证 475 字符 CHAR 语义与 476/1000 字符 CLOB 往返；未运行 4 KB/8 KB 实例，建表接受不等于能存满。键/索引字符串默认长度为 450 个字符；`NVARCHAR2(n)` 已是字符语义，保持不变 |
| `DateTimeOffset` | 真实环境已验证 / 查询部分支持 | 存储映射为 `DATETIME(7) WITH TIME ZONE`；已有往返样本通过提供程序文本回读保留原始偏移量，包括 `+08:00`。列比较及六个本地日期部件已验证。`Offset`、`UtcDateTime`、`DateTime`、`Now`/`UtcNow` 和 `Add*` 仍不翻译；映射精度声明不构成官方驱动第七位小数保真保证 |
| `TimeSpan` | 真实环境已验证 | 默认 `INTERVAL DAY(9) TO SECOND(6)`，包括超过两位天数的精确正值和负值；字面量保留天/小数秒精度，并拒绝造成信息损失的 tick。两种驱动验证显式 `TIME` / `TIME(0/3/6)` 的写入、读取及参数比较：通过 TimeOnly 转换器读写，只接受一天内非负时刻，负值和大于等于一天的值明确拒绝；小数秒不可精确表示时拒绝参数与字面量。裸 TIME 为零位，默认 TimeOnly 仍为 TIME(6) |
| 二进制与 LOB 映射 | 真实环境已验证 / 查询语义部分支持 | `VARBINARY`、40 KiB `BLOB` 和 40 KiB Unicode `NCLOB` 往返。通过 `TEXT_EQUAL`/`BLOB_EQUAL` 进行参数相等比较，以及通过 NCLOB `INSTR` 搜索，均已在参考服务器执行；排序、分组、distinct 和 distinct 集合操作会提前失败。字符串搜索函数仍受 `CLOB_MAX_CALC_LEN` 约束。键/索引需要有界行内类型，可用行内长度由页面/行配置决定 |
| JSON 存储 | 部分支持 / 查询不支持 | 保留 `JsonElement` 的既有 JSON 往返测试范围。参考实例探针中，原生 JSON 列的补充平面 Unicode 原文回读失真；JSON_VALUE 还将 JSON 空字符串读为 SQL null，数值返回可隐式舍入/转换。因未满足无损契约，未提供 JSON 标量查询 API；`GetProperty` 不翻译，整值 Equals 生成的列比较会被参考服务器以数据类型不匹配拒绝 |
| 无符号整数与分面边界 | 单元级已验证 | 保持范围的转换器和存储映射已有覆盖；尚未将广泛的真实服务器边界数据作为发布声明 |
| 事务 | 真实环境已验证 | 已验证 EF 事务提交/回滚行为 |
| 保存点 | 真实环境已验证 | 官方驱动虽然把基础能力标志报告为不支持，参考驱动/服务器仍可创建保存点并回滚；W 的差异见下文 |
| 隔离级别 | 部分支持 / 子集真实环境已验证 | 官方驱动下，`ReadCommitted` 的 EF 事务提交/回滚与跟踪式 `SaveChanges` 已验证；`RepeatableRead` 和 `Snapshot` 会在 `BeginTransaction` 时被拒绝。`ReadUncommitted` 和 `Serializable` 可以开始事务并执行查询，但跟踪式 `SaveChanges` 会因 `CommandText has no value` 失败；`Serializable` 下的 `ExecuteUpdate` 可以持久化更改。W 的成功子集见下文 |
| 重试执行策略 | 单元级已验证 | 官方驱动使用保守的 `DmException.Number` 分类和有界设置；当前没有真实故障注入套件证明每个错误码都可恢复。W 禁止内建自动重试 |
| DDL 事务性 | 不支持原子迁移 | 达梦 DDL 会隐式提交；生成的 DDL 命令会禁用 EF 事务 |
| 创建/删除物理数据库 | 不支持 | `Create`/`Delete` 会抛出异常；应连接到现有数据库并管理当前模式中的对象 |
| 迁移 DDL | 部分支持 / 子集真实环境已验证 | 已执行 `Database.GenerateCreateScript()`、非幂等 `IMigrator.GenerateScript()`、幂等脚本重复执行以及 `Database.Migrate()`。覆盖标识列种子与 `SET IDENTITY_INSERT`、序列 NEXTVAL、虚拟计算列、主键、唯一约束、检查约束、ON DELETE CASCADE、升降序唯一索引、额外模式中的表、列/表/索引重命名、带默认值的非空列和删除列。参考实例在第一次 NEXTVAL 前把增量从 3 改成 5 时，第一个值是 43 而不是 41。纯移除 IDENTITY 的边界见下行；筛选索引、存储计算列、其他标识属性变更和跨模式重命名在生成时拒绝。`HasComment` 经 `COMMENT ON TABLE` / `COMMENT ON COLUMN` 落库，清除生成 `IS ''`，覆盖幂等与非幂等脚本及真实库回读 |
| 迁移历史记录 | 真实环境已验证 | 存在性、按需创建、插入、查询和删除路径。存在性查询 `SYS.SYSOBJECTS`。参考实例上 `RESOURCE` 可以执行生成的脚本，但不能做这次查询；需要再授予 `SOI`，`Database.Migrate()` 才能通过。脚本里的历史插入不经过这次查询 |
| 迁移锁 | 真实环境已验证 / 服务器特定 | 使用 `DBMS_LOCK`；已在参考非 MPP 服务器上验证。达梦 MPP 不提供相同基线 |
| 幂等迁移脚本 | 真实环境已验证 / 部分支持 | `IMigrator.GenerateScript(Idempotent)` 在历史表守卫中使用已转义的 `EXECUTE IMMEDIATE`，块以单独一行 `/` 结束。剔除 `/` 后把每个 `BEGIN ... END;` 作为一条命令重复执行，Unicode 和引号种子只保留一行，历史记录不重复；`/` 本身不能放进 ADO.NET 命令。自定义 `migrationBuilder.Sql` 匿名块按前导空白、注释和关键字边界识别 BEGIN/DECLARE，校验外层 END 后没有其他语句再原样透传；普通 SQL 按引号/注释之外的分号拆成独立动态语句，保留事务抑制标记并忽略空语句。普通 SQL 后混入匿名块或 CREATE [OR REPLACE] PROCEDURE/FUNCTION/TRIGGER/PACKAGE/TYPE 时明确拒绝；匿名块应单独作为操作，存储定义另行执行。转义后 UTF-8 超过 32767 字节的单条动态语句字面量提前失败 |
| 存储计算列 | 不支持 | 迁移生成与反向工程支持非键虚拟计算列；存储计算列仍不支持，虚拟列参与主键或被选中外键引用为候选键时明确拒绝 |
| 筛选索引 | 不支持 | 提供程序会拒绝迁移索引筛选器，而不是生成其他数据库的语法 |
| 修改标识列 | 部分支持 / 子集真实环境已验证 | 只移除 IDENTITY、其他列定义和语义注解不变时，生成表级 `ALTER TABLE ... DROP IDENTITY`，禁止事务且不附加 MODIFY。真实 EF 模型差异、已有数据/主键保留、目录自增属性消失、显式键插入及缺键失败已验证；限定/转义标识符、幂等动态 SQL 包装具有单元证据。增加/恢复 IDENTITY、修改种子/增量、同次切换序列或改变其他列定义继续拒绝；自动 Down 不保证可执行，参见[迁移说明](migrations.md#移除-identity) |
| 跨模式重命名 | 不支持 | 不能通过表/序列重命名将对象移动到其他模式 |
| 共享列值生成一致性 | 单元级已验证 / 子集 | 表拆分中同一物理列的多个有效生成器必须具有相同策略、identity seed/increment 或序列名称/有效 schema；冲突在模型校验拒绝。有效 `None` 复制、相同 identity/sequence、默认/显式等效 schema 及 TPT/TPC 合法对照保留；不是全面表拆分一致性声明 |
| 复杂属性并发标记校验 | 单元级已验证 / 子集 | 平铺、单层及两层引用复杂属性使用同一 rowversion 拒绝合同；异常定位拥有者和完整属性路径。同 CLR 类型的两个路径独立校验。应用管理标记保留；计算列和拥有者对应表的 HasTrigger 声明沿用例外，无关表触发器不放行；保留仅映射视图的平铺计算列并发标记例外。不增加复杂类型、JSON、值生成或触发器执行的实库支持声明 |
| TPT/TPC 值生成 | 单元级已验证 / 部分支持 | TPT 仅向根表应用标识列/序列生成。由于多个具体表可能发生冲突，因此拒绝 TPC 标识列；可改用共享达梦序列 |
| 模式创建与不常见 DDL | 部分支持 | 已执行 `CREATE SCHEMA` 以及该模式中的表。`EnsureSchema` 生成带 `SYS.SYSOBJECTS`（`TYPE$ = 'SCH'`）存在性守卫的匿名块，模式已存在时不重复创建；服务器不支持 `CREATE SCHEMA IF NOT EXISTS`。其余未测试 DDL 不能据此视为已支持 |
| 设计时迁移代码生成 | 真实环境已验证 | 提供程序和注解代码生成器生成 `UseDameng` 和达梦值生成 API。进程内 GenerateCreateScript、GenerateScript 和 Database.Migrate 已在服务器执行；`dotnet ef migrations add` / `migrations script`（含幂等）/ `database update` / `dbcontext scaffold` 使用版本匹配的本地 dotnet-ef 工具完成真实库端到端回归。人工步骤见[迁移操作说明](migrations.md) |
| 反向工程 | 部分支持 / 真实环境已验证 | 注册 `IDatabaseModelFactory`，覆盖当前模式的表、视图、列、默认值、表/列注释、主键、唯一约束、普通索引、外键、IDENTITY、全部可见序列与非键虚拟计算列。显式筛选必须全部命中；无法保留的类型、关系、目录分面或物理存储配置明确拒绝，不静默省略。完整范围与证据边界见[反向工程](reverse-engineering.md) |
| 列级 COLLATE 目录语义 | 参考实例未持久化 / 不声明通用支持 | 参考 DM8、COMPATIBLE_MODE=0 实例接受列声明中的多种 COLLATE 名称，但 TABLEDEF 不保留子句，兼容/原生列目录没有对应分面，比较与排序同普通列。查询中的 `ORDER BY ... COLLATE ...` 是另一种有效语法；不据此声称所有 DM8 版本均没有列级排序规则。参见[官方查询排序说明](https://eco.dameng.com/document/dm/zh-cn/pm/check-phrases.html)与[MySQL 迁移说明](https://eco.dameng.com/document/dm/zh-cn/faq/faq-mysql-dm8-migrate.html) |
| EF 关系数据库规范一致性 | 未声明 | 规范测试项目包含四项提供程序自有冒烟测试并使用 EF 测试工具，但不继承上游关系数据库 `*TestBase` 测试套件 |
| 裁剪 / NativeAOT | 未验证 | 不对提供程序或当前驱动程序的裁剪、编译模型优化或 NativeAOT 兼容性作任何声明 |
| 异步 I/O 与取消 | 受驱动程序限制 | 官方驱动已测试资产会回退到同步 ADO.NET 实现；EF 异步 API 仍可用，但无法保证非阻塞 I/O 或及时取消。W 的差异见下文 |
| 连接超时 | 未验证的驱动程序设置 | EF `CommandTimeout` 的单位为秒。本仓库不对驱动程序连接字符串的超时关键字或单位作任何断言 |

## 最近验证记录

- [M6 最终验收](../plans/evidence/M6.md)：产品 revision `7efb38e` 的官方/W 完整 `all` 与独立 `PK_WITH_CLUSTER=1` profile，是当前最新完整双驱动实库记录。
- [W 0.3.1 升级记录](w-dmprovider-validation.md#升级到-wdamengprovider-0312026-10-29)：依赖升级分支的 W 全通道回归和基础单元回归。该轮未重跑官方真实库，也不等同于后续合并 revision 的全量验收。
- [0.4.0 发布记录](release-0.4.0.md)：两个已发布包的验收、精确包消费者检查及公共发布验证；发布版本与当前源码依赖不得混写。
- 更早的发布、实施和验收快照按时间保留在[文档索引](README.md)列出的历史文档中，不回写为当前轮次结果。

## W.DamengProvider 驱动（预览版）

当前源码的扩展项目依赖 `W.DamengProvider [0.3.1]`（身份见 `scripts/driver-candidate/w-damengprovider.manifest.json`）。下表只描述与官方驱动不同或需要单独说明的能力；未列出的 SQL/模型/迁移/反向工程能力与上表共用，并在 W 通道中以同一套功能测试重跑。

下表既有能力的证据来自服务器自报 `8.1.5.60`、UTF-8、明文连接的本地 profile；
019 另有独立 `PK_WITH_CLUSTER=1`、8192 字节页、`GLOBAL_CHARSET=0` 的 LOB 主键矩阵，仅用于该项结论。
TLS 与其他版本未因此获得验证。完整记录见 [W 接入验证记录](w-dmprovider-validation.md)。
PR #7 / #8 已合并，审查线程均已解决（[复核记录](w-dmprovider-integration-review.md)）。
本次扩展包 `0.5.0-preview.1` 精确依赖 W `[0.3.1]`；上一版 `0.4.0-preview.1` 依赖 W `0.2.0`。

| 领域 | W 状态 | 范围与边界 |
| --- | --- | --- |
| 配置 | 单元级已验证 | 六个 `UseDamengWithWDriver` 重载、`AddEntityFrameworkDamengWithWDriver`；同一 options 混用两种驱动、手工服务与 options 驱动不符、同一服务集合注册两种驱动均明确失败 |
| 共享功能套件 | 真实环境已验证 | W `0.3.1` 下，W 单元 61 项、功能 365 项（含官方 300 项共享功能）、规范冒烟 4 项、管理员迁移脚本 7 项全部通过，无失败或跳过；按驱动区分的断言与运行 ID 见验证记录 |
| 异步与取消 | 真实环境已验证 | 查询与 `SaveChangesAsync` 在服务器未完成时返回；预取消不发送；运行中取消保留原 token 并关闭会话，EF 下一次操作用新会话；`CommandTimeout` 报客户端期限（`TotalDeadline`）。LOB 同步 getter 不在此结论内 |
| 自动重试 | 明确拒绝 | `EnableRetryOnFailure` 全部重载及直接构造 `DamengRetryingExecutionStrategy` 抛 `Dameng:RetryNotSupportedForWDriver`；默认策略执行一次；自定义 `ExecutionStrategy` 由应用负责 |
| 失败结果 | 部分验证 | EF 异常链保留 W 的 `FailureInfo`（错误类别、阶段、`OperationOutcome`、`ConnectionReusable`）；事务内取消写入报 `Unknown` 且独立核对未写入。套接字失败包装为 `DmException`（`ErrorKind=Transport`、`Number=0`，原异常为 InnerException）：连接被拒报 `NotSent`，中继收到登录请求后重置报 `Unknown`；事务内 SaveChanges 发送后重置（sync/async）得到 `DbUpdateException`，结果未知且连接不可复用，新连接核对未提交。commit 失 ACK 的端到端注入尚未验证 |
| 错误后的会话 | 真实环境已验证（8.1.5.60） | 唯一键、外键、NOT NULL、触发器 RAISE、序列耗尽、未绑定标记 -6804 在 sync/async × 自动提交/事务下保留同一会话且事务不被暗中结束。W 0.2.0 起带参数命令（隐式 Prepare）的 -2007 与 -2106 在 sync/async 事务内保留同一会话与事务；无参数直接执行仍按执行阶段白名单，例如 `SELECT SLEEP(1) FROM DUAL` 使会话 Broken，EF 重新打开 |
| 事务内匿名块 | 真实环境已验证 / 子集 | W 0.2.0 起在本地事务内放行只含 DML 的 `BEGIN … END;`：带参数的 UPDATE + INSERT 块回滚后无效果、提交后可见，会话不变。DECLARE、IF 等控制流、块内 COMMIT 仍在发送前抛 `NotSupportedException`，会话与事务保持可用；事务外正常执行。完整判定规则见 W 的 [issue #16 说明](https://github.com/whynpc9/W.DamengProvider/blob/v0.2.0/docs/compatibility/I16-ef-integration-feedback.md)。迁移中的块由提供程序抑制事务，不受影响 |
| 隔离级别 | 真实环境已验证（8.1.5.60） | RU / Serializable 下跟踪式 `SaveChanges` 成功（官方驱动失败），测试先核对 `DmConnection.ServerCapabilities` 公布对应能力；RepeatableRead/Snapshot 仍拒绝；其他 profile 的发送前拒绝尚未验证 |
| 保存点 | 真实环境已验证 | EF 自动保存点在 `SaveChanges` 失败后回滚并继续事务；显式保存点可用 |
| 时间精度 | 真实环境已验证（8.1.5.60） | 默认 `TIMESTAMP(7)` / `DATETIME(7) WITH TIME ZONE` 逐 tick 往返（含 Min/Max 边界、原偏移、相邻 tick 参数比较）；时间参数只用 Scale；六位列写入非整微秒值在发送前拒绝。W 0.2.0 起 `TIMESTAMP(9)` 与 `TIMESTAMP(9) WITH TIME ZONE` 列可经 EF 写入 NULL 和整 tick 值，`TIMESTAMP(8)` 接受提供程序的参数形状；该能力只在 `ServerCapabilities.MaxTemporalWriteScale` 为 9 的 profile 开放，其他 profile 由 W 拒绝。读取非整 100ns 值拒绝；TIME/INTERVAL 最多六位；显式裸 TIME 为零位，TIME(0/3/6) 的 TimeSpan 转换与 TimeOnly 路径均有回归 |
| 原始 ADO 读取 | 行为差异 | 带时区列 `GetValue` 返回 `DateTimeOffset`（官方为 `DateTime`）；EF 物化对 DateTimeOffset/INTERVAL 使用 typed getter |
| 迁移与切换 | 真实环境已验证 | 6→7 位保值；7→6 位需先运行守卫（见[迁移说明](migrations.md)）；同一历史表可在官方与 W 之间交替应用/回滚迁移；比较 `ActiveProvider` 的迁移须同时接受两个提供程序名称；两驱动的模型互相 diff 无操作 |
| 设计时 | 真实环境已验证 | `dotnet ef migrations add/script`、`database update`、`dbcontext scaffold`（提供程序名 `W.EntityFrameworkCore.Dameng.WDriver`）与生成代码编译；嵌套项目加载的 W DLL 与 manifest 一致 |
| 诊断 | 真实环境已验证 | ActivitySource/Meter 名称 `W.DmProvider`；活动中不含 SQL、参数值、连接串或凭据；LogFragment/DebugInfo 带 `Driver=W` |
| 数据源、流式 LOB、DmDecimal | 未接入 | 属于后续独立设计范围，不能据驱动能力推断 EF 支持 |

连接入口按实际驱动类型校验：`UseDameng(DbConnection)` 只接受官方 `Dm.DmConnection`，
`UseDamengWithWDriver(DbConnection)` 只接受 `W.Dm.DmConnection`；包装连接（如 profiler）会被拒绝，
因为提供程序专用参数类型无法穿过包装设置。

## 真实数据库测试契约

真实数据库测试接收一个机密环境变量：

```text
DAMENG_TEST_CONNECTION_STRING
```

该变量包含完整的基础连接字符串。测试和日志不得打印、快照记录或提交它；W 通道由统一入口在内存中派生对应设置，不另存秘密。配置的账户必须能够在自己的模式中创建/删除表、索引、约束和序列，执行 DML，使用事务/保存点，访问迁移历史记录以及调用 `DBMS_LOCK`。`Database.Migrate()` 的历史表存在性检查还要能查询 `SYS.SYSOBJECTS`；参考实例上 `RESOURCE` 不够，需要 `SOI`。只执行生成脚本、不调用 `Migrate()` 时，`RESOURCE` 已覆盖已验证脚本中的 DDL 和 DML。该账户不需要创建或删除物理数据库的权限。管理员脚本通道另外需要创建和删除表空间与用户，并只精确清理自己创建的临时对象。

测试隔离规则：

1. 为每组对象生成唯一、可识别的前缀。
2. 将发现和清理限制在该前缀及当前用户模式内。
3. 在 `finally` 中删除精确对象，包括断言失败之后。
4. 绝不依赖回滚清理 DDL，因为 DDL 会隐式提交。
5. 不得在测试输出中持久化主机名、用户名、密码或完整连接字符串。
6. 两种驱动的数据库通道共享测试模式，不得并行运行。

统一入口及完整验收规则见[本地测试说明](../scripts/local-test/README.md)：

```bash
scripts/local-test/run.sh test all
scripts/local-test/run.sh test all --driver w --w-transport-security PlaintextAllowed
```

直接运行数据库测试项目时，可通过环境变量提供官方驱动连接：

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

## 2026-10-28 M2 实现与验证边界

本轮证据见 [M2 执行记录](../plans/evidence/M2.md)。官方七位参数差异按维护者决定列为上述已知风险；本轮验证范围如下，不构成发布声明。

- 005：Replace 的 null 替换值删除语义，两种空值模式、有界文本/NCLOB、常量/参数/列替换值，
  两驱动实际执行；20,000 字 NCLOB 完整内容与长度均断言。
- 007：distinct 集合双方及 LISTAGG 文本/二进制 LOB 排序用途拒绝；两驱动验证零发送和
  有界集合、LOB UNION ALL、合法有序聚合。
- 006：确认裸 TIMESTAMP 字面量第七位丢失；显式解析和目标类型 CAST 的候选修复已通过
  直接值、HasData/InsertData 和默认值建表/加列检查。官方精度 7/8/9
  参数查询仍与精确常量不同（作为维护者已接受的已知驱动风险单独表征），不能扩大官方参数精度保证，亦未修改驱动或参数配置。
- 008：日期操作按有效存储语义处理；转换为 ticks/带前缀文本的模型正常直接往返，日期函数
  提前拒绝；原生及 DateTime 恒等转换保留。实际用例与最终计数见单项证据。

只覆盖当前普通持久测试用户、W 显式 PlaintextAllowed 的参考实例；无 TLS、独立 profile、
完整数据库 all 或上游 EF 关系一致性套件声明。既有历史验收数字保持原记录。

### M3 / 009 驱动入口验证（2026-10-28）

已补齐运行时连接、时态参数及已初始化 `UseModel` 的驱动一致性检查。双向外来模型读写零 Open/Execute；同驱动时态插入、更新、参数读取与无参数投影分别通过官方/W 真实库验证，使用可精确表示的整微秒值。仅运行时身份不改变持久模型注解。此结果不修复或撤回 M2 已记录的官方七位时态参数风险；也不新增编译模型或连接包装支持。见 `plans/evidence/009.md`。

### M3 / 010 会话淘汰证据（2026-10-28）

W固定明文profile补齐持续持有连接的正常/预取消对照、完整Execute帧转发后取消、已知直接执行错误的重开前Broken/Closed及恢复新会话证据。纯内存屏障和Debug/Release实库均通过，详见`plans/evidence/010.md`。这不扩大驱动能力，也不把帧转发当成服务器活动观察；旧自动开闭查询的不同SESSID不独自作为会话淘汰证据。

### M3 / 011 HasTables（2026-10-28）

同步/异步目录探测已纳入EF关系命令、实际CommandTimeout、scalar拦截和诊断；两驱动共享实库验证了原先开/关连接、调用方所有权、已有事务及无关表/EnsureCreated语义。W在本轮明文profile以该目录入口拦截后的慢命令验证完整Execute转发/调用待结果、此后取消和客户端期限；官方仍为驱动同步回退、取消有限。返回0由离线受控命令验证，未声称实跑空用户模式；不外推完整EnsureCreatedAsync、TLS或其他profile。见`plans/evidence/011.md`。

### M3 当前验收状态

M3此前最终W真实集成失败及20分钟中止记录保持。用户授权后，在相同源码上从启动前全程防休眠重试原57项，57/57 Completed、零失败/跳过；最终版本Release四项4/4通过，末次表/序列/回收站均0，当前本地门已闭合，远端整合状态见`plans/evidence/M3.md`。此次通过不确定此前失败的根因，不新增自动重试或其他profile支持。

## M4 模型校验补强（2026-10-28）

I02/I06 均已通过公开 EF 最终化路径证实缺口并补充模型拒绝规则。锁定 EF Core 10.0.12 下，共享表实际列映射和复杂属性实际关系映射均有正向断言；两驱动使用各自服务。合法共享列的生成 SQL 与 M3 合并基线逐字节相同，未改值生成解析、注解输出或 SQL 生成器。此轮为离线模型/SQL 证据，没有执行 DDL、创建触发器或更新历史实库计数。详情见 `plans/evidence/012.md`、`013.md` 和 `M4.md`。

### M5 管理员夹具恢复证据（014）

临时管理员夹具在CREATE发送前保存私有所有权日志；不确定结果不重试CREATE，使用新连接逐对象核对类型及关系后精确清理。两驱动各12项离线状态、4项真实故障场景及11项完整管理员测试通过，无自有残留。注入覆盖真实CREATE完成到客户端确认之间的窗口，不是网络失ACK复现，也不扩大产品自动重试保证。见[014证据](../plans/evidence/014.md)。

### M5 纯注释迁移证据（015）

真实EF模型差异仅改变注释时，普通列和IDENTITY列只生成抑制事务的COMMENT；全部列定义与语义注解必须相等，未知注解变化不进入优化。两驱动各4项真实生命周期覆盖普通/幂等脚本、含撇号/多行注释增删改、历史去重、原行和后续生成键；结构定义、默认值和共享列生成策略保持。SQL/模型单元与真实服务器证据分别见[015证据](../plans/evidence/015.md)。

### M5 历史表名称与实际创建入口证据（016）

按独立命令的词法位置插入建表存在性守卫；11项实际仓储名称单元及26项既有拆批回归通过。两驱动各6项实库覆盖普通/关键词/转义引号中文模式、复杂表名重复执行、真正IMigrator幂等历史去重，以及实际同步/异步CreateIfNotExists无事务DDL；交叉各16项通过。该实际入口同时修复模式块与建表被压成一条ADO命令的问题；不扩大为完整DMSQL解析器。见[016证据](../plans/evidence/016.md)。

### M5 迁移锁首次验证边界（017，历史BLOCKED）

本实例官方7项通过，W7项通过/1项被权限阻断。固定全局锁的同步/异步争用、DDL后持锁、正常/异常/实际关闭释放与重取已由独立连接验证；不同历史表/模式options仍共享固定ID，不能据此并行迁移。官方预取消观察到TaskCanceledException，其已取消token与调用者不同；没有网络零发送或运行中取消保证。W预取消取得原token及REQUEST零完整Execute转发证据（排除清理RELEASE）。普通账号缺`SYS.V$DBMS_LOCKS`查询权限，W真实等待中取消尚无服务器等待/取消证据；M5保持BLOCKED，不标为该能力已验证，不外推MPP。见[017证据](../plans/evidence/017.md)。

014补充：独立审查发现对照夹具清理短路，修复后两驱动离线各16通过；017权限阻断后未继续数据库，最终夹具的管理员重测尚待完成，014/M5仍BLOCKED。此前完整管理员各11是真实历史证据，不代表审查后源码已完成实库门。

### M5 最终恢复验收（2026-10-29）

按维护者明确授权新建普通隔离账号，使用RESOURCE、SOI及该账号上的精确`SELECT ON SYS.V$DBMS_LOCKS`完成测试；没有给普通测试DBA权限。014审查修复后的官方/W管理员聚焦各4、完整各11通过；017官方7/W8通过，含完整Execute转发后由普通连接确认服务器等待队列、原token/FailureInfo取消、拥有者不受影响及新会话重取；联合迁移交叉两驱动各21通过。视图SELECT仅为测试观测权限，不是应用Migrate新增要求。临时账号/空间已精确清理，原持久环境不变；历史权限阻断已解除，不外推MPP/TLS/其他profile。详见[最新M5证据](../plans/evidence/M5.md)。

## 2026-10-29：M6 首轮目录/profile边界（以下阻断为历史）

018 的完整工厂合成目录已复现：原生目录有普通 `Id`、`Payload`，兼容目录只返回主键列
`Id` 时，旧路径能静默返回缺列模型。现在复用已有原生预检查询，按 Ordinal 列名集合核对
选中普通表的两套元数据；缺列、额外列或缺少可读原生列均在工厂返回前明确拒绝。
视图仍按投影读取，过滤排除对象不参与核对，当前模式全部序列及无表模式边界不变。

真实 DM `8.1.5.60`、32768 字节页的正常目录对照与合成缺行证据分别归档；
本次没有在服务器制造目录缺行。原生 `COLID` 从 0 起、兼容 `COLUMN_ID` 从 1 起，
不按序号判定损坏；所测普通列与虚拟列没有需要忽略的额外内部列。
详见 [018 执行记录](../plans/evidence/018.md)。

019 当前仅有 SQL/模型离线表征、显式 profile 路由和只读前置核对。local 的独立普通账户
确认当前实例 `PK_WITH_CLUSTER=0`，不能代替独立值为 1 的 profile。未实施新的 LOB 主键策略，
未选择 CONFIRMED/REFUTED，未声称 PK=1 已支持；019、020 与 M6 的最终验收仍 BLOCKED。
普通 all 不包含 probes 或 profile；最终双驱动 all 尚未作为 M6 验收执行。
M2 的官方七位参数已知驱动风险及原接受边界保留。详见 [019 执行记录](../plans/evidence/019.md)。

### M6 独立 profile 恢复验证

019 的原环境阻断已由用户提供的独立实例解除。DM自报8.1.5.60，普通PK=0/32768字节页与
独立PK=1/8192字节页，COMPATIBLE_MODE=0、LENGTH_IN_CHAR=0；两驱动均以所记明文profile验证。
在这两个profile上，无界string/byte[]和显式CLOB/NCLOB/BLOB的原SQL均成功，主键后备索引NORMAL；
即使显式CLUSTER，服务器也保留NORMAL。有界列对照在PK=1下确为CLUSTER。
这属于已测服务器的安全处理，019按REFUTED闭合，没有新增LOB主键推导或改写显式注解。
完整模型、迁移、手工建表、共享表、加主键及NULL/非空/更新/重复键对照，普通各27、独立各29
在官方/W分别通过。其他服务器版本、LOB别名、JSON/BFILE及已有表存储改造仍不外推；
model-less AddPrimaryKey继续保留原合同，应用需明确聚集选择并核对目标目录。
具体指纹、TRX与资源清理见[019](../plans/evidence/019.md)。最终M6集成和020收口另记。

## M6 最终当前源码验收（2026-10-29）

当前源码产品revision为 `7efb38e83c8f5a15febdca45c6a66abc7936787a`，后续020仅修订文档。
018普通列目录完整性修复已验证；019在所记PK=0/32KB与独立PK=1/8KB两个DM8.1.5.60明文profile上
确证由服务器安全处理，无新增LOB主键策略。独立profileRelease官方/W各29项全通过。
授权单行W测试夹具修正保留原网络链路，最终官方all的单元/功能/规范/admin为973/402/4/11，
W为128/489/4/11，全部Completed、非零、零失败/跳过；普通all不含probes/profile。
两实例及管理员脚本的自有账户/表空间均精确清理；历史全量W三项诊断红灯保留。
该结果不表示上游EF全关系一致性、所有页大小/字符集、其他版本、TLS或包发布已完成。
M2官方七位参数已知风险按本文首节原边界保留；无需提升能力声明的安全/夹具/CI项仍仅为对应证据。
全部20项唯一归属、当前文档处置与最终SHA/TRX见[020对账](../plans/evidence/020.md)和[M6](../plans/evidence/M6.md)。
