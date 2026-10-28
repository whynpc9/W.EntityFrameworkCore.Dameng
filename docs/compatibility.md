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

## 证据术语

原有矩阵记录截至 2026-09-22 的仓库基线。2026-09-17 的参考记录仍写明服务器版本 8.1.5.60。2026-09-22 的全量功能测试和迁移脚本测试运行在另一轮真实库上，该实例自报为 `DM Database Server 64 V8`、`DB Version: 0x7000d`、构建号 `03134284604-20260707-335949-20228`，`PAGE()` 为 32768，`COMPATIBLE_MODE` 为 0。这次运行没有返回 8.1.5.60，文档不把两个版本号当成同一个事实。

2026-09-29 的查询增量另在用户指定实例的独立测试用户/表空间验证。只读环境记录为
`DM Database Server 64 V8` / `DM Database Server x64 V8`、`PAGE()=32768`、
`COMPATIBLE_MODE=0`、`LENGTH_IN_CHAR=0`、`GLOBAL_CHARSET=1`、`CALC_AS_DECIMAL=0`、
`JSON_MODE=0`、`CLOB_MAX_CALC_LEN=20480`。没有将旧实例版本或构建号套用到新实例。
工作包、探针与回归结果见[实施台账](query-translation-execution.md)；探针成功执行不等于功能验收。

2026-10-02 的纯移除 IDENTITY 与生成值回读增量见[独立验收记录](stage3-pre-driver-validation.md)。
该批使用现有测试环境，未重新采集服务器 build/profile，不将既有版本记录套用为本轮新证据。

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
| 日期字符串转换 | 部分支持 / 子集真实环境已验证 | 无参数 DateTime.ToString 使用服务器默认 `CAST(... AS VARCHAR(100))`，Nullable<DateTime>.ToString 保留 NULL 到空串回退；投影、筛选、排序和分组采用同一映射。常量数字日期格式的已验证子集映射 TO_CHAR；动态格式、IFormatProvider、时区/文化相关格式及转换到非日期存储类型的 DateTime 不声明支持。格式契约、重载及服务器精度边界见[日期字符串转换](datetime-string-translation.md) |
| 数值聚合与报表分组 | 部分支持 / 查询形状真实环境已验证 | Count/LongCount、Sum/Average/Min/Max、条件与筛选聚合、复合/日期/表达式键、HAVING、两层投影汇总、JOIN 和相关 EXISTS 已有聚焦回归，含空集与全 NULL。DateTime.ToString 分组现在按服务器文本结果执行，不推断业务时间粒度；未投影 IGrouping 上的外层 Sum 仍无法翻译；组内 nullable DISTINCT COUNT 忽略 NULL，不能声明 CLR NULL 单项语义。对应改写已真实验证。覆盖范围、查询改写及原始失败记录见[聚合验证](numeric-aggregate-validation.md) |
| 数值函数翻译 | 部分支持 / 真实环境已验证 | 限可表示的有限值；NaN/正负 Infinity 参数在本实例执行时报数据溢出。`Math.Abs`/`Sign` 的 int、long、decimal、double 重载，`Floor`/`Ceiling` 的 decimal、double 重载；`Exp`、`Log`、二参数 `Log`、`Log10`、`Pow`、`Sqrt` 的 double 重载。int Abs 显式恢复 int 范围，最小有符号整数取绝对值报溢出；decimal 不经过 double。对数底数会按达梦顺序交换。正常定义域有结果验证；域外或溢出可能抛数据库错误，不等同 CLR NaN/Infinity。Round、三角函数、MathF 与未列重载不在本批支持范围 |
| 日期分桶函数 | 真实环境已验证 / 子集 | `EF.Functions.DamengTruncateHour`、`DamengTruncateMinute`、`DamengStartOfIsoWeek`、`DamengQuarter`，各含 DateTime/DateTime? 重载。ISO 周返回所在周的周一零点日期；null 输入返回 null。覆盖跨年、闰日、四季度及筛选/分组/排序/投影。参考实例拒绝 TRUNC 的 SS 掩码，未发布秒截断 API |
| 字符串分组聚合 | 部分支持 / 真实环境已验证 | `string.Join(string, IEnumerable<string>)` 和 `string.Concat(IEnumerable<string>)` 的分组聚合，selector 和已映射的分隔符都必须是有界可变文本。组内排序显式使用升序 NULLS FIRST、降序 NULLS LAST；保留筛选、null/空元素的位置；null 分隔符视为空串，筛选后空集合和全 null 的 Concat 返回空串。Join 用分隔符前缀补偿 LISTAGG 跳过空元素的差异。Distinct、LOB、固定 CHAR/NCHAR 及 LOB 排序明确拒绝；无显式排序不承诺顺序，长度溢出报数据库错误，不截短。Join 中间结果多一个分隔符，可能先于最终 CLR 结果触及服务器长度边界 |
| 日期/时间与 GUID 翻译 | 部分支持 / 子集真实环境已验证 | 原有真实执行覆盖 `DateTime` 年/月、整数 AddDays、DateTimeOffset 列比较和 Guid.NewGuid()/NEWID()；其他 DateTime 成员和整数 DATEADD 保留原单元证据。2026-09-29 新增 DateTimeOffset 的 Year/Month/Day/Hour/Minute/Second 真库回归，按列保存的原偏移提取本地部件，覆盖同瞬间不同日历日期及可空值。Offset、UtcDateTime、DateTime、Now/UtcNow 和 Add* 仍不翻译；本批不承诺 100ns 精度或时区转换 |
| 集合 Contains | 有界回归范围真实环境已验证 | 常量/参数集合，参数集合大小 0/1/499/500/501/998/999/1000/2000，含 null、重复、否定、双集合与普通筛选、排序分页和同查询 1→1000→1 重用。每次查询一条命令。本实例证据不支持写死 500 个 IN 元素或 999 个总参数限制，因此本轮未增加拆分或上限；2,000 只是已测样本，不是最大容量或性能保证 |
| 小数 `DateTime.Add*` | 无法无损翻译时不支持 | 参数化或非整数 double 参数不会被静默截断；EF 会报告无法翻译。2026-09-29 候选日期算术和 INTERVAL 路径的第七位小数未满足保真要求，服务器文本输出也未保留探针中的 100ns 增量，本轮未扩大支持 |
| 跟踪式 CRUD | 真实环境已验证 | Unicode 插入/读取/更新/删除、null、转换器、标识列键回读和受影响行报告 |
| 标识列 | 真实环境已验证 / 部分方面单元级已验证 | 生成的键及通过 `SCOPE_IDENTITY()` 回读已在服务器执行；约定和显式种子/增量有单元测试覆盖 |
| 序列 | 真实环境已验证 | `sequence.NEXTVAL` 默认值和通过 `sequence.CURRVAL` 回读生成的键；不使用标准 `NEXT VALUE FOR` |
| 非键生成值回读 | 真实环境已验证 / 子集 | 2026-10-02 使用现有 DML + SELECT 路径验证：客户端单键/复合键、IDENTITY 和序列键插入后，Unicode/数值/NULL 默认值及虚拟计算列在同步/异步 SaveChanges 返回时已回填；无需 Reload。当前值和原始值、独立查询及外部事务回滚均有回归。其他策略的库端生成键继续拒绝，未引入 RETURNING 或输出参数 |
| 乐观并发 | 真实环境已验证 / 子集 | 原有应用管理的过期并发标记通过 `SQL%ROWCOUNT` 协议抛出异常。2026-10-02 新增显式配置的 BEFORE UPDATE 行触发器版本标记：更新后回读标记与虚拟计算列、连续两次更新、陈旧标记和已删除行的零行更新均有同步/异步回归；失败方不传播生成值。HasTrigger 只声明元数据，触发器需自行创建；不支持自动生成 SQL Server 风格 rowversion |
| `ExecuteUpdate` / `ExecuteDelete` | 真实环境已验证 | 受影响行数以及持久化的布尔值/转换值更新 |
| 修改批处理 | 受驱动程序限制 | 官方驱动没有提供程序专用的 `DbBatch`；W 0.2.0 的 `DbBatch` 按命令逐条往返、不减少往返次数，数组绑定只报告总影响行数且不能回读生成值。两种驱动都使用 `SingularModificationCommandBatch` |
| 常见标量映射 | 真实环境已验证 | 有符号整数、无分面 decimal 与 decimal(38,20)、bool、GUID、Unicode/CJK、可空值、`DateOnly`、微秒精度 `TimeOnly` 和精度为 7 的 `DateTime`。2026-09-30 起，非 Unicode 定界字符串生成 `VARCHAR2(n CHAR)` / `CHAR(n CHAR)`：`LENGTH_IN_CHAR=0` 实例上 `VARCHAR(n)` 按字节截断中文，字符语义声明经真实库回读验证（`CHAR_USED='C'`）；自动推导按最小受支持页的保守 1900 字节预算，即 `n × 4 ≤ 1900`（475 字符）；变长超出转 CLOB，定长推导模型/无显式类型迁移拒绝，不会自动转换成二进制。显式存储类型仍按声明保留，由使用方核对实例页大小与整行预算。参考实例（页 32768、UTF-8）验证 2/3/475 字符 CHAR 语义与 476/1000 字符 CLOB 的 EF 往返；本轮未运行 4 KB/8 KB 实例，新预算依据官方最小页容量保守选取；页大小探针仅记录探索结果，遇到 SQL 错误不会失败，不能作为容量边界验收。建表接受不等于能存满。键/索引字符串默认长度统一为 450 个字符（最坏 1800 字节），在文档表最小 4 KB 页的列上限（1900 字节）内。`NVARCHAR2(n)` 已是字符语义，保持不变 |
| `DateTimeOffset` | 真实环境已验证 / 查询部分支持 | 存储映射为 `DATETIME(7) WITH TIME ZONE`；已有往返样本通过提供程序文本回读保留原始偏移量，包括 `+08:00`。列比较及 `Year`/`Month`/`Day`/`Hour`/`Minute`/`Second` 六个本地日期部件已验证，部件按列上保存的原偏移提取。`Offset`、`UtcDateTime`、`DateTime`、`Now`/`UtcNow` 和 `Add*` 仍不翻译；映射精度声明不构成第七位小数保真保证 |
| `TimeSpan` | 真实环境已验证 | 默认 `INTERVAL DAY(9) TO SECOND(6)`，包括超过两位天数的精确正值和负值；字面量保留天/小数秒精度，并拒绝造成信息损失的 tick。2026-10-14 两种驱动验证显式 `TIME` / `TIME(0/3/6)` 的写入、读取及参数比较：通过 TimeOnly 转换器读写，只接受一天内非负时刻，负值和大于等于一天的值明确拒绝；小数秒不可精确表示时拒绝参数与字面量。裸 TIME 为零位，默认 TimeOnly 仍为 TIME(6) |
| 二进制与 LOB 映射 | 真实环境已验证 / 查询语义部分支持 | `VARBINARY`、40 KiB `BLOB` 和 40 KiB Unicode `NCLOB` 往返。通过 `TEXT_EQUAL`/`BLOB_EQUAL` 进行参数相等比较，以及通过 NCLOB `INSTR` 搜索，均已在参考服务器执行；排序、分组、distinct 和 distinct 集合操作会提前失败。字符串搜索函数仍受 `CLOB_MAX_CALC_LEN` 约束。键/索引需要有界行内类型，可用行内长度由页面/行配置决定 |
| JSON 存储 | 部分支持 / 查询不支持 | 保留原有 `JsonElement` 的 JSON 往返测试范围。2026-09-29 新实例探针中，原生 JSON 列的补充平面 Unicode 原文回读已失真，不能扩大为完整 Unicode 保证；JSON_VALUE 还将 JSON 空字符串读为 SQL null，数值返回可隐式舍入/转换。因未满足无损契约，本轮未发布 JSON 标量 API；`GetProperty` 仍不翻译。整值 Equals 仍生成列比较并被参考服务器以数据类型不匹配拒绝 |
| 无符号整数与分面边界 | 单元级已验证 | 保持范围的转换器和存储映射已有覆盖；尚未将广泛的真实服务器边界数据作为发布声明 |
| 事务 | 真实环境已验证 | 已验证 EF 事务提交/回滚行为 |
| 保存点 | 真实环境已验证 | 尽管驱动程序的基础能力标志不支持，参考驱动程序/服务器仍可创建保存点并回滚到保存点 |
| 隔离级别 | 部分支持 / 子集真实环境已验证 | `ReadCommitted` 下的 EF 事务提交/回滚与跟踪式 `SaveChanges` 已在参考服务器执行。`RepeatableRead` 和 `Snapshot` 会在 `BeginTransaction` 时被驱动程序拒绝。`ReadUncommitted` 和 `Serializable` 可以开始事务并执行查询，但跟踪式 `SaveChanges` 会因驱动程序 `CommandText has no value` 失败；`Serializable` 下的 `ExecuteUpdate` 可以持久化更改 |
| 重试执行策略 | 单元级已验证 | 保守的 `DmException.Number` 分类和有界设置；当前没有真实故障注入套件证明每个错误码都可恢复 |
| DDL 事务性 | 不支持原子迁移 | 达梦 DDL 会隐式提交；生成的 DDL 命令会禁用 EF 事务 |
| 创建/删除物理数据库 | 不支持 | `Create`/`Delete` 会抛出异常；应连接到现有数据库并管理当前模式中的对象 |
| 迁移 DDL | 部分支持 / 子集真实环境已验证 | 2026-09-22 已在独立用户模式中执行 `Database.GenerateCreateScript()`、非幂等 `IMigrator.GenerateScript()`、幂等脚本的重复执行，以及 `Database.Migrate()`。覆盖标识列种子与 `SET IDENTITY_INSERT`、序列 `NEXTVAL`、虚拟计算列、主键、唯一约束、检查约束、`ON DELETE CASCADE`、升降序唯一索引、额外模式中的表、列/表/索引重命名、带默认值的非空列（已有行回填为该默认值）和删除列。在第一次 `NEXTVAL` 之前把增量从 3 改成 5 时，第一个值是 43 而不是 41。2026-10-02 增加纯移除 IDENTITY 的生成命令实库回归，边界见下行；筛选索引、存储计算列、其他标识属性变更和跨模式重命名仍在生成时拒绝。2026-09-30 起 `HasComment` 经 `COMMENT ON TABLE` / `COMMENT ON COLUMN` 落库（服务器不接受 `IS NULL`，清除生成 `IS ''`），覆盖幂等与非幂等脚本及真实库回读 |
| 迁移历史记录 | 真实环境已验证 | 存在性、按需创建、插入、查询和删除路径。存在性查询 `SYS.SYSOBJECTS`。`RESOURCE` 可以执行生成的脚本，但不能做这次查询；2026-09-22 的实例上要再授予 `SOI`，`Database.Migrate()` 才能通过。脚本里的历史插入不经过这次查询 |
| 迁移锁 | 真实环境已验证 / 服务器特定 | 使用 `DBMS_LOCK`；已在参考非 MPP 服务器上验证。达梦 MPP 不提供相同基线 |
| 幂等迁移脚本 | 真实环境已验证 / 部分支持 | `IMigrator.GenerateScript(Idempotent)` 在历史表守卫中使用已转义的 `EXECUTE IMMEDIATE`，块以单独一行 `/` 结束。2026-09-22 将 `/` 剔除后，把每个 `BEGIN ... END;` 作为一条命令执行了两遍，Unicode 和引号种子只保留一行，历史记录不重复。`/` 本身不能放进 ADO.NET 命令。自定义 `migrationBuilder.Sql` 匿名块按忽略前导空白、行/块注释与关键字大小写识别 `BEGIN`/`DECLARE`（含关键字边界）并校验外层 END 后没有其他语句再原样透传——服务器拒绝把块包进 `EXECUTE IMMEDIATE`；普通 SQL 按引号/注释之外的分号拆成独立动态语句，避免 CREATE 后续 DML 提前绑定尚不存在的表；保留每条命令的事务抑制标记，忽略空语句。普通 SQL 后混入 BEGIN/DECLARE 或 CREATE [OR REPLACE] PROCEDURE/FUNCTION/TRIGGER/PACKAGE/TYPE 时明确拒绝拆分，匿名块应单独作为操作，存储定义另行执行。转义后 UTF-8 表示超过 32767 字节的单条动态语句字面量会提前失败，必须拆分 |
| 存储计算列 | 不支持 | 迁移生成与反向工程支持非键虚拟计算列；存储计算列仍不支持，虚拟列参与主键或被选中外键引用为候选键时明确拒绝 |
| 筛选索引 | 不支持 | 提供程序会拒绝迁移索引筛选器，而不是生成其他数据库的语法 |
| 修改标识列 | 部分支持 / 子集真实环境已验证 | 只移除 IDENTITY、其他列定义和语义注解不变时，生成表级 `ALTER TABLE ... DROP IDENTITY`，禁止事务且不附加 MODIFY。真实 EF 模型差异、已有数据/主键保留、目录自增属性消失、显式键插入及缺键失败已验证；限定/转义标识符、幂等动态 SQL 包装具有单元证据。增加/恢复 IDENTITY、修改种子/增量、同次切换序列或改变其他列定义继续拒绝；自动 Down 不保证可执行，参见[迁移说明](migrations.md#移除-identity) |
| 跨模式重命名 | 不支持 | 不能通过表/序列重命名将对象移动到其他模式 |
| TPT/TPC 值生成 | 单元级已验证 / 部分支持 | TPT 仅向根表应用标识列/序列生成。由于多个具体表可能发生冲突，因此拒绝 TPC 标识列；可改用共享达梦序列 |
| 模式创建与不常见 DDL | 部分支持 | 2026-09-22 已执行 `CREATE SCHEMA` 以及该模式中的表。2026-09-30 起 `EnsureSchema` 生成带 `SYS.SYSOBJECTS`（`TYPE$ = 'SCH'`）存在性守卫的匿名块，模式已存在时不重复创建；服务器不支持 `CREATE SCHEMA IF NOT EXISTS`。其余未测试的 DDL 不能据此视为已支持 |
| 设计时迁移代码生成 | 真实环境已验证 | 提供程序和注解代码生成器会生成 `UseDameng` 和达梦值生成 API。进程内 `GenerateCreateScript`、`IMigrator.GenerateScript` 和 `Database.Migrate()` 已在服务器执行。2026-09-30 起 `dotnet ef migrations add` / `migrations script`（含幂等）/ `database update` / `dbcontext scaffold` 在以版本匹配的 dotnet-ef 本地工具（`artifacts/dotnet-ef-tool`）驱动下完成真实库端到端回归：注释、`IDENTITY` 种子/增量、`NEXTVAL` 默认和降序索引均往返。人工步骤见 [迁移操作说明](migrations.md) |
| 反向工程 | 部分支持 / 真实环境已验证 | 设计时服务注册 `IDatabaseModelFactory`，覆盖当前模式（以 `SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())` 判定；`SET SCHEMA` 切换后有独立真库用例，不受登录用户与当前模式分离影响）的表、视图、列、列默认值、表/列注释、主键、唯一约束、普通索引（含升降序）和外键（含复合键与 CASCADE/SET NULL/NO ACTION）。主键从后备索引 `INDEX_TYPE` 保留聚集方式，经 `Dameng:IsClustered` 注解进入脚手架模型与迁移 DDL，明确生成 `CLUSTER PRIMARY KEY` 或 `NOT CLUSTER PRIMARY KEY`，不依赖目标 `PK_WITH_CLUSTER` 缺省；未知后备索引类型明确拒绝。CHAR/VARCHAR/VARCHAR2 按 CHAR_USED 明确输出 n CHAR 或 n BYTE，显式类型的名称与左括号之间的空格/制表符会从映射查找基名去除，原始类型文本、长度与单位仍保留；显式 CHAR/BYTE 长度限定仅对 CHAR/VARCHAR/VARCHAR2 解析；其他类型（如 INT(9 CHAR)）无映射，不生成非法 DDL。字节单位不再省略，未知 CHAR_USED 的错误点名表/视图、列及实际值（含 NULL）；BYTE 映射与重建已在 LENGTH_IN_CHAR=0 实例验证，相关 CREATE/ADD/ALTER 列 DDL 前生成目标语义守卫，其他或未知模式拒绝，未声称已验证模式 1 实例迁移。列目录使用 ALL_TAB_COLS；普通表的 HIDDEN_COLUMN 仅接受 NO，NOT VISIBLE（YES）或未知状态明确拒绝并点名对象，避免改变 SELECT * 与无列清单 INSERT 的行为。可空/非空隐藏列的目录、查询、默认插入及过滤隔离已有真实断言；视图仍按原投影读取。普通表列先读取原生 SYSCOLUMNS.INFO2，位 14 标记的加密列及未知标记明确拒绝，点名表和列，不读取密钥目录；透明/半透明列加密的写入读回、目录标记、拒绝与表过滤均已有真库断言。列加入模型前复用注入的 `IRelationalTypeMappingSource` 按完整存储类型查找映射，映射缺失时点名表/视图、列和类型并抛 `NotSupportedException`，避免 EF 脚手架仅警告后省略列；BFILE、TIME WITH TIME ZONE、INTERVAL HOUR TO MINUTE 和含 BFILE 的视图已有真实拒绝与普通表过滤隔离断言。IDENTITY 注解前复用模型最终化的 CLR 兼容性判断，仅接受 int/long；目录中的 DEC(n,0) IDENTITY 明确拒绝并点名表/列/类型，不返回无法最终化的注解。自增识别原生 `IDENTITY`（`SYSCOLUMNS.INFO2` 位 0 与 `SYSOBJECTS.INFO6` 第 25–26 字节共同判定，`IDENT_SEED`/`IDENT_INCR` 按 `模式.表` 限定还原种子与增量）与列默认值 `序列.NEXTVAL`；仅存在同名序列不算自增。`AUTO_INCREMENT` 与未知/缺失的自动生成类型明确拒绝，不当作 IDENTITY。`NEXTVAL` 识别支持未引用名称中的 `$`、`#`，限定点号两侧允许空格、制表符和换行，引用名称内部的点、空格与转义引号保持原义。非简单默认 SQL 中出现未限定或当前模式的 NEXTVAL/CURRVAL 依赖时，按词法 token 明确拒绝并点名对象/列/序列，避免保留表达式却遗漏序列定义；字符串、注释、函数调用和已限定外模式依赖不会被该检查当作本地序列。本地序列一律用已解析目录模式限定：整数 CLR 类型使用提供程序序列策略，DECIMAL/NUMBER 等非整数映射保留限定模式的默认 SQL，序列定义仍进入模型，避免不兼容策略导致最终化失败；若默认 SQL 列属于主键，或被选中外键提升为候选键，而没有受支持的 IDENTITY/Sequence 键生成策略，则明确拒绝并点名表、列和类型，避免延迟到模型验证器才失败。当前模式的全部可见序列（含独立使用、未被任何列引用的序列）从 `ALL_SEQUENCES` 读入真实 facet（`LAST_NUMBER` 作起点、增量、上下界、循环）进入模型，重建不再虚构默认 facet；非循环未耗尽时 `LAST_NUMBER` 为下一个待发放值（未消费时等于 `START WITH`，消费一个 `NEXTVAL` 后按增量前移，41→44 已验证）；最小值须小于最大值，非循环续号越界明确拒绝。循环序列到达边界时目录可能先报越界算术续号，仅当它是从界内前值按正确方向迈出的一步时，按官方循环规则还原为另一端起点；升降循环的原序列与重建序列下一值一致已有真库断言，其他不一致状态拒绝；当前模式已读到的序列如果任一 facet 超出 EF 可精确表示范围（非零增量 int、其余 long）或循环标记未知则明确拒绝，避免漏掉已知序列的 CREATE SEQUENCE；当前模式序列还须 CACHE_SIZE=0、ORDER_FLAG=N，其他缓存/排序状态（含未知）明确拒绝；生成 CREATE SEQUENCE 显式带 NOCACHE NOORDER，ALTER 不改未建模的缓存/排序分面。只有显式跨模式引用保留原始默认值作为外部依赖；未限定或当前模式的简单 NEXTVAL 必须读到序列目录，否则点名对象、列、序列并拒绝，不把缺失本地依赖当作外部依赖。序列目录每次按当前模式完整查询，只绑定一个模式参数，不再按列引用名称分批。表过滤不限制序列；没有选中表或模式内没有表时，序列仍进入模型。当前模式中任何不可精确保留的序列都会阻止反向工程，不能靠筛选其他表绕过；列名查找使用每次反向工程独立的 Ordinal 字典，减少宽表的重复扫描。`FLOAT(p)` 按目录 DATA_PRECISION 保留二进制精度；`INTERVAL DAY TO SECOND` 从目录 `DATA_PRECISION`/`DATA_SCALE` 还原天精度与秒小数位；`TIME`/`TIMESTAMP`/`DATETIME`（含 `WITH TIME ZONE` 变体）按目录 `DATA_SCALE` 原样还原，显式 `(0)` 不回落为服务器默认精度；`TIMESTAMP WITH LOCAL TIME ZONE` 的目录 `DATA_SCALE` 带 4096 偏移，按偏移还原精度。标识符按目录原始拼写加引号，不强制大写。普通 B 树表根据 CLUSTER 索引保留 `Dameng:IsClusterBtree=true`，经实体、关系表注解与迁移输出显式 `STORAGE(CLUSTERBTR)`，不依赖目标 LIST_TABLE 缺省；堆表或未识别存储明确拒绝，存储方式变更须重建。表对象 SYSOBJECTS.INFO2 的空间页数上限必须为 0（无限制）；非零或未知 DISKSPACE LIMIT 明确拒绝，避免重建时移除资源上限。真实用例验证 4 MB 限额的目录页数、超量插入拒绝、同数据在无限额表成功，以及普通表过滤隔离。B 树表的聚集存储索引 SYSINDEXES.GROUPID 必须等于模式所属用户 SYSOBJECTS.INFO3 低 16 位的默认数据表空间 ID，否则点名表及空间 ID 并拒绝；目录缺失同样拒绝。通过模式对象 PID 关联所属用户，不以模式名或登录用户替代；不新增 DBA_USERS 查询权限要求。聚集存储仅接受默认数据放置；其余 NORMAL 物理索引（含普通二级、非聚集主键、唯一约束及 WITH INDEX 外键）按所属用户 INFO3 第 16–31 位校验默认索引表空间，非默认或未知位置点名表/索引并拒绝，约束后备索引也不豁免。该字段为 0 表示未单独配置，按服务器规则回退到表的数据空间；已接受表须位于默认数据空间。外键 VIRTUAL 索引不进行物理空间比较。真实用例覆盖默认数据/索引空间分离、第三空间上的普通/主键/唯一索引拒绝，以及未配置索引默认时的回退；不声称保留显式非默认表空间。表填充因子从当前参考服务器的原生 SYSOBJECTS.INFO1 高字节读取，经 Dameng:TableFillFactor 注解进入实体、关系表和迁移，显式生成 FILLFACTOR；0 按官方定义归一化为 100，85 等非默认值保留，未知/越界拒绝，后续变更须重建。兼容视图 PCT_FREE 在参考实例为空，不作为读取依据；0/85/100 的真实重建与 CLI 注解/SQL 链路已有断言。未配置该注解的手写模型保持原建表行为。普通索引和唯一约束后备索引从原生索引对象 INFO1 低字节读取填充因子，经 Dameng:IndexFillFactor 保留到 CREATE INDEX STORAGE(FILLFACTOR n)，0 规范为 100；唯一约束按 EF 脚手架契约转成唯一索引。聚集存储/主键后备索引仅接受与表相同的有效填充因子，否则明确拒绝。独立索引 70、表及约束索引 85 的目录与重建断言和 CLI 索引注解/SQL 链路已验证。边界：原生 SYSOBJECTS.INFO3 低六位必须为普通表类型 0，HUGE 和未知/其他非普通表类型明确拒绝；INFO3 位 57 标识的物化视图预建表明确拒绝，不作为普通表重建；真实回归验证手动刷新前后数据、目录标记、选中/未过滤扫描拒绝及普通表过滤隔离。INFO3 位 50 标识的 LONG ROW 表也明确拒绝，因为 CLUSTERBTR 注解不能保留该独立存储能力；真实回归验证 USING LONG ROW 表写入并读回超过半页的记录、目录标记及普通表过滤隔离；HUGE 的 TEMPORARY=N/PARTITIONED=NO/UTAB 及普通混合表空间表的隔离已有管理员通道真实断言。全局临时表（事务级/会话级）及未知 TEMPORARY 标记明确拒绝，避免变为持久表；PARTITIONED 非 NO 的表（含未知标记）明确拒绝，范围/哈希分区已有真实库拒绝与过滤隔离断言，避免变为非分区表；选中表上的位图等未支持的非 NORMAL 索引类型明确拒绝；普通及聚集物理索引还须 STATUS=VALID 且原生 SYSINDEXES.XTYPE 的不可见位 65536 未置位；INVISIBLE、UNUSABLE 及未知状态明确拒绝，包含主键/唯一约束后备索引，外键 VIRTUAL 索引不作物理状态比较。真实库验证 ALTER INDEX 状态变化、表过滤隔离、VISIBLE/REBUILD 恢复后正常读回；依据原生 SYSCONS 关联确认的外键 VIRTUAL 索引作为约束实现细节跳过，不变成普通索引；WITH INDEX 创建的普通物理外键索引仍保留，主键/唯一约束的后备索引不重复建模；选中表包含禁用或未知状态、非 NOT DEFERRABLE/IMMEDIATE/VALIDATED 的主键、唯一约束或外键时明确拒绝（EF 无法保留这些状态），被表过滤排除的对象不影响扫描；只扫当前模式，`--schema` 请求其他模式时抛 `NotSupportedException`；`--table` 过滤中任一限定其他模式的条目同样逐条点名拒绝，不再静默跳过；当前模式内显式请求的表/视图也必须全部命中，否则列出未命中名称并拒绝返回部分模型；模式/表过滤按标识符组成部分解析：未加引号的部分折成大写，双引号中的大小写、点和转义引号保留，目录名称本身保持原样；选中从表的外键动作以原生 `SYSCONS.FACTION` 两个字符为准：更新只接受空格标记的 NO ACTION，其他及未知动作明确拒绝；删除保留空格/C/N 对应的 NO ACTION/CASCADE/SET NULL，D（SET DEFAULT）及未知动作明确拒绝。参考实例的兼容视图 DELETE_RULE 会把原生 D 显示为 CASCADE，不能据此推断动作；EF 脚手架关系模型不能保留 SET DEFAULT，故不只映射一个枚举值后继续。真实用例已验证更新级联/置空/默认及删除置默认的数据效果与拒绝边界。参考实例对未指定 MATCH、MATCH SIMPLE/FULL/PARTIAL 使用相同的默认匹配行为：半空元组允许、完整非法引用拒绝，TABLEDEF 不保留 MATCH；原表与按元数据重建表的四组行为均有真实断言。不据语法接受推断其他版本已实现 FULL/PARTIAL 的独立语义，也不虚构原生匹配模式字段。选中从表的外键按 `R_OWNER` 判定模式，跨模式主体、同模式但未选中/不可读的主体、列目录不完整或不能解析时明确拒绝，不能省略关系；外键引用的主体唯一列若可空则明确拒绝，避免 EF 候选键强制改成不可空；未被选中关系引用的可空唯一列继续保留；表达式/函数索引明确拒绝，既检查类型头 FUNCTION-BASED 类型，也拒绝 COLUMN_POSITION 非正或缺失的列行，避免遗漏唯一表达式索引约束；ALL_TRIGGERS 按目标 TABLE_OWNER 检查，选中表/视图存在触发器即明确拒绝（含禁用），不静默丢失触发器定义与状态；视图注释不可用（服务器拒绝 `COMMENT ON` 视图）；通过 `SYSCOLINFOS.INFO1` 位 0 识别虚拟计算列，从 `SYSCOLUMNS.DEFVAL` 保留表达式到 `ComputedColumnSql`，设置 `IsStored=false` 与 `OnAddOrUpdate`，不把表达式当插入默认值；缺失表达式和计算生成键明确拒绝。位 4/6 的 DEFAULT ON NULL、ON UPDATE 仍拒绝；位 5 的隐式 NOT NULL 不单独拒绝；原生 `SYSCONS` 中的用户 CHECK（含禁用及显式 IS NOT NULL 检查）明确拒绝，列的普通 NOT NULL 不受影响；独立用户聚集索引和非 NORMAL 后备索引的唯一约束明确拒绝，系统行存储索引不误判为用户索引 |
| 列级 COLLATE 目录语义 | 参考实例未持久化 / 不声明通用支持 | 2026-10-02 的 DM8、COMPATIBLE_MODE=0 实例接受列声明中的 `Chinese_PRC_CS_AS_KS_WS`、`utf8mb4_bin`、`utf8mb4_general_ci` 及不存在的名称，但 `TABLEDEF` 不保留子句，兼容/原生列目录没有对应分面，比较与排序同普通列。查询中的 `ORDER BY ... COLLATE Chinese_PRC_CS_AS_KS_WS` 是另一种有效语法；不据此声称所有 DM8 版本均没有列级排序规则。参见[官方查询排序说明](https://eco.dameng.com/document/dm/zh-cn/pm/check-phrases.html)与[MySQL 迁移说明](https://eco.dameng.com/document/dm/zh-cn/faq/faq-mysql-dm8-migrate.html) |
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

2026-09-30 设计时工作验证（Debug，不是 Release 发布快照；实例参数同 2026-09-29 记录）：

- 确定性单元测试套件：303/303 通过；
- 功能测试套件：85/85 通过，含反向工程（表/视图/列/默认值/注释/约束/索引/外键/自增、跨模式外键跳过、自引用外键、`SET SCHEMA` 模式分离）、注释迁移（含多行注释幂等脚本）、模式守卫、非 Unicode 字符语义和 `dotnet ef` 命令行端到端（版本匹配的本地 dotnet-ef 工具，`artifacts/dotnet-ef-tool`）；
- 提供程序自有关系数据库冒烟测试套件：4/4 通过；
- 管理员迁移脚本通道：4/4 通过，临时用户与表空间已确认清理；
- 能力探针通道：24/24 执行完成（探针通过只表示候选均已尝试，不构成能力声明）。

2026-10-02 PR #3 审查修复的聚焦验证（Debug）：

- 单元 339/339，真实库规范冒烟 4/4，管理员迁移脚本 4/4；
- 新增过滤/匿名块回归与脚本拆批用例 20/20；未引用的小写过滤、双引号名称、前导注释与同一行块均有断言；
- `dotnet format --verify-no-changes` 通过；容量探针不计入功能验收。完整功能套件和后续 head 的结果单独记录在 PR #3。

2026-10-04 的 0.3.0 发布候选验收：

- 修复后的统一 `test all`：单元 682/682、真实库功能 232/232、规范冒烟 4/4、
  管理员脚本 7/7，合计 925/925；四个宿主均为 Completed，无失败或跳过。
- 首轮字符语义用例因 `ManyServiceProvidersCreatedWarning` 失败；对该用例的专用模型
  服务关闭内部服务容器缓存后，聚焦与完整真实库回归均通过。产品实现及数据库断言未变。
- Release 构建、Release 单元 682/682、格式验证和候选包检查通过。
- 当前实例参数、失败历史、修复及最终证据路径见[0.3.0 发布准备](release-0.3.0.md)。
  此处记录本地候选验收，不表示版本已发布。

2026-10-16 的基础包 `0.4.0` 与 W 扩展包 `0.4.0-preview.1` 本地发布准备验收：
官方单元/功能/规范冒烟/管理员脚本分别 791/300/4/7，W 分别 61/365/4/7，
全部 Completed，无失败或跳过。Release 构建、两组 Release 单元、格式、双包及三个精确包消费者检查通过。
本轮运行 ID、测试依赖安全修复、升级注意事项和未验证边界见
[0.4.0 发布记录](release-0.4.0.md)。两个包均已公开发布，标签 CI、GitHub Release、公共包与 NuGet 签名已分别验证。

## W.DamengProvider 驱动（预览版）

扩展包 `W.EntityFrameworkCore.Dameng.WDriver` 0.4.0-preview.1 依赖 `W.DamengProvider [0.2.0]`
（身份见 `scripts/driver-candidate/w-damengprovider.manifest.json`）。下表只描述与官方驱动不同或需要单独说明的能力；
未列出的 SQL/模型/迁移/反向工程能力与上表共用，并在 W 通道中以同一套功能测试重跑。
证据只来自服务器自报 `8.1.5.60`、UTF-8、明文连接的本地 profile；TLS 与其他版本均未验证。
完整记录见 [W 接入验证记录](w-dmprovider-validation.md)。PR #7 / #8 已合并，审查线程
均已解决（[复核记录](w-dmprovider-integration-review.md)）；扩展包 `0.4.0-preview.1` 已公开发布。

| 领域 | W 状态 | 范围与边界 |
| --- | --- | --- |
| 配置 | 单元级已验证 | 六个 `UseDamengWithWDriver` 重载、`AddEntityFrameworkDamengWithWDriver`；同一 options 混用两种驱动、手工服务与 options 驱动不符、同一服务集合注册两种驱动均明确失败 |
| 共享功能套件 | 真实环境已验证 | 升级到 W 0.2.0 后，官方 300 项功能在 W 下全部通过；W 功能通道共 365 项，另有 4 项规范冒烟、7 项管理员迁移脚本全部通过；按驱动区分的断言与最新运行 ID 见验证记录 |
| 异步与取消 | 真实环境已验证 | 查询与 `SaveChangesAsync` 在服务器未完成时返回；预取消不发送；运行中取消保留原 token 并关闭会话，EF 下一次操作用新会话；`CommandTimeout` 报客户端期限（`TotalDeadline`）。LOB 同步 getter 不在此结论内 |
| 自动重试 | 明确拒绝 | `EnableRetryOnFailure` 全部重载及直接构造 `DamengRetryingExecutionStrategy` 抛 `Dameng:RetryNotSupportedForWDriver`；默认策略执行一次；自定义 `ExecutionStrategy` 由应用负责 |
| 失败结果 | 部分验证 | EF 异常链保留 W 的 `FailureInfo`（错误类别、阶段、`OperationOutcome`、`ConnectionReusable`）；事务内取消写入报 `Unknown` 且独立核对未写入。W 0.2.0 把套接字失败包装为 `DmException`（`ErrorKind=Transport`、`Number=0`，原异常为 `InnerException`）：经本地 TCP 中继注入 RST，连接被拒报 `NotSent`，中继收到登录请求后重置报 `Unknown`；事务内 `SaveChanges` 发送后重置（sync/async）得到 `DbUpdateException`，`OperationOutcome=Unknown`、`TransactionOutcome=OutcomeUnknown`、不可复用，新连接核对未提交。commit 失 ACK 的端到端注入 pending |
| 错误后的会话 | 真实环境已验证（8.1.5.60） | 唯一键、外键、NOT NULL、触发器 RAISE、序列耗尽、未绑定标记 -6804 在 sync/async × 自动提交/事务下保留同一会话且事务不被暗中结束。W 0.2.0 起带参数命令（隐式 Prepare）的 `-2007` 语法错误与 `-2106` 无效表在 sync/async 事务内保留同一会话与事务；无参数的直接执行仍按执行阶段白名单，例如 `SELECT SLEEP(1) FROM DUAL` 使会话 Broken，EF 重新打开 |
| 事务内匿名块 | 真实环境已验证 / 子集 | W 0.2.0 在本地事务内放行只含 DML 的 `BEGIN … END;`：带参数的 UPDATE + INSERT 块回滚后无效果、提交后可见，会话不变。`DECLARE`、`IF` 等控制流、块内 `COMMIT` 仍在发送前抛 `NotSupportedException`，会话与事务保持可用；事务外正常执行。完整判定规则见 W 的 [issue #16 说明](https://github.com/whynpc9/W.DamengProvider/blob/v0.2.0/docs/compatibility/I16-ef-integration-feedback.md)。迁移中的块由提供程序抑制事务，不受影响 |
| 隔离级别 | 真实环境已验证（8.1.5.60） | RU / Serializable 下跟踪式 `SaveChanges` 成功（官方驱动失败），测试先核对 `DmConnection.ServerCapabilities` 公布对应能力；RepeatableRead/Snapshot 仍拒绝；其他 profile 的发送前拒绝 pending |
| 保存点 | 真实环境已验证 | EF 自动保存点在 `SaveChanges` 失败后回滚并继续事务；显式保存点可用 |
| 时间精度 | 真实环境已验证（8.1.5.60） | 默认 `TIMESTAMP(7)` / `DATETIME(7) WITH TIME ZONE` 逐 tick 往返（含 Min/Max 边界、原偏移、相邻 tick 参数比较）；时间参数只用 `Scale`；六位列写入非整微秒值在发送前拒绝；W 0.2.0 起 `TIMESTAMP(9)` 与 `TIMESTAMP(9) WITH TIME ZONE` 列可经 EF 写入 NULL 和整 tick 值（异步插入、同步更新，NULL 与值互换，`.1234567` 存为 `.123456700`，含 Min/Max 边界），`TIMESTAMP(8)` 接受提供程序的参数形状；该能力只在 `ServerCapabilities.MaxTemporalWriteScale` 为 9 的 profile 开放，其他 profile 由 W 拒绝。读取非整 100ns 值拒绝；TIME/INTERVAL 最多六位；显式裸 TIME 为零位，TIME(0/3/6) 的 TimeSpan 转换与 TimeOnly 路径均有回归 |
| 原始 ADO 读取 | 行为差异 | 带时区列 `GetValue` 返回 `DateTimeOffset`（官方为 `DateTime`）；EF 物化对 DateTimeOffset/INTERVAL 使用 typed getter |
| 迁移与切换 | 真实环境已验证 | 6→7 位保值；7→6 位需先运行守卫（见[迁移说明](migrations.md)）；同一历史表可在官方与 W 之间交替应用/回滚迁移；比较 `ActiveProvider` 的迁移须同时接受两个提供程序名称；两驱动的模型互相 diff 无操作 |
| 设计时 | 真实环境已验证 | `dotnet ef migrations add/script`、`database update`、`dbcontext scaffold`（提供程序名 `W.EntityFrameworkCore.Dameng.WDriver`）与生成代码编译；嵌套项目加载的 W DLL 与 manifest 一致 |
| 诊断 | 真实环境已验证 | ActivitySource/Meter 名称 `W.DmProvider`；活动中不含 SQL、参数值、连接串或凭据；LogFragment/DebugInfo 带 `Driver=W` |
| 数据源、流式 LOB、DmDecimal | 未接入 | M2/M3 范围，不能据驱动能力推断 EF 支持 |

连接入口的兼容性变化（两种驱动）：`UseDameng(DbConnection)` 现在只接受官方 `Dm.DmConnection`，
`UseDamengWithWDriver(DbConnection)` 只接受 `W.Dm.DmConnection`；包装连接（如 profiler）会被拒绝，
因为提供程序专用参数类型无法穿过包装设置。

## 2026-10-07 反向工程补足

- 主键、唯一约束、索引与外键统一要求完整列元数据；缺列或零列明确拒绝，独立索引目录头存在但无可读列行也拒绝。这是目录不完整时的防御性契约，使用合成单元测试验证，不声称已在真实服务器制造目录缺损。
- 选中表名最多 128 个时，目录查询使用参数化名称谓词；超过该数量保留完整模式扫描和内存过滤，不丢弃对象。序列仍独立枚举当前模式全部可见序列，不受表过滤限制。
- IDENTITY 先由原生目录确认生成类型，再按引用后的模式与表名批量读取种子/增量，每批最多 64 张表、128 个参数，不再逐表发出查询；避免对 AUTO_INCREMENT 调用仅支持 IDENTITY 的函数。独立模式 12 张 IDENTITY 表的真实对比中，两种选择均为 18 条命令，筛选一张表将返回行数从 216 降到 18；IDENTITY 分面命令均为 1 条。耗时样本为 2247 ms 与 2186 ms，不能据此声称显著延迟提升或所有规模的性能保证。
- 虚拟列真实回归比较源表和迁移 SQL 重建表的数值、带引用名称/字符串常量的文本表达式、NULL、插入与更新结果及表达式目录读回。CLI 回归覆盖最终 EF 模型、复合外键、自引用、视图、可空性、存储注解和虚拟列，生成代码重新编译后执行模型断言。
- 虚拟列中的函数等外部依赖仍由目标环境提供；反向工程不创建这些依赖。完整运行证据见[设计时台账](design-time-plan.md)。

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
