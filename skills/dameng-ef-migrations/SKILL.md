---
name: dameng-ef-migrations
description: >-
  Generates and applies EF Core migration scripts for the Dameng provider
  W.EntityFrameworkCore.Dameng. Use when the user mentions Database.Migrate,
  GenerateCreateScript, IMigrator.GenerateScript, dotnet ef migrations script,
  idempotent Dameng scripts, __EFMigrationsHistory, or executing EF migrations
  against DM8.
license: MIT
metadata:
  author: W.EntityFrameworkCore.Dameng contributors
  version: "1.0"
---

# 达梦 EF Core 迁移执行

面向 `W.EntityFrameworkCore.Dameng` 已经实现、并在 2026-09-22 真实库上执行过的迁移路径。给人看的 `dotnet ef` 步骤在仓库 `docs/migrations.md`。同一模型面向多种数据库时，按该文档「多种数据库各用一个迁移项目」拆项目，达梦命令的 `--project` 和 `--startup-project` 都指向达梦迁移项目。方言细节用 [dameng-sql](../dameng-sql/SKILL.md)。能力边界以仓库 `docs/compatibility.md` 为准。

不要把连接串、主机、用户或口令写进仓库、脚本、日志或文档。

## 先选一条路径

| 目的 | 做法 | 历史表 | 已验证 |
| --- | --- | --- | --- |
| 按当前模型建对象 | `Database.GenerateCreateScript()` | 不写 | 是 |
| 应用内升级 | `Database.Migrate()` | 写 | 是 |
| 把已有 migration 交给执行器 | `IMigrator.GenerateScript()` 或 `dotnet ef migrations script` | 写 | 进程内生成并执行已验证；`dotnet ef` 命令行已做端到端回归 |
| 同一脚本重复执行 | `GenerateScript(MigrationsSqlGenerationOptions.Idempotent)` | 写，并用 `IF NOT EXISTS` 守卫 | 是，同一脚本执行两遍 |

三条脚本都不会让 DDL 变成事务。`CREATE` / `ALTER` / `DROP` 会隐式提交。失败后按对象名清理，不要指望 `ROLLBACK` 收回已执行的 DDL。

`dotnet ef dbcontext scaffold` 已实现：注册 `IDatabaseModelFactory`，反向工程当前模式的表、视图、列、默认值、注释、主键、唯一约束、索引（含升降序）与外键。只扫 `SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())` 判定的当前模式。选中表/视图的列必须可由注册的提供程序类型映射源按存储类型映射，否则点名对象、列和类型并明确拒绝，避免脚手架静默丢列。HUGE 等非普通原生表类型、LONG ROW 表（SYSOBJECTS.INFO3 位 50）、全局临时表、分区表与选中表上的表达式/函数索引、位图等专用索引、表/视图触发器（含禁用）、用户 CHECK、AUTO_INCREMENT、虚拟计算列、DEFAULT ON NULL、ON UPDATE、禁用或延迟/未验证主键/唯一/外键、独立用户聚集索引和聚集唯一约束在反向工程时明确拒绝；这不影响显式模型生成 CHECK 或虚拟列迁移 DDL。命令行回归使用 `artifacts/dotnet-ef-tool` 下与锁定 EF Core 版本匹配的 dotnet-ef 本地工具，并在 `dotnet test` 宿主内以显式 `dotnet restore` + `dotnet build` + `--no-build` 驱动（ef 的进程内构建在测试宿主下不可靠）。

复合或其他未支持默认值若引用未限定/当前模式的 NEXTVAL/CURRVAL，反向工程明确拒绝；
字符串、注释和已限定外模式引用保留原处理边界。

本地 NEXTVAL 识别允许限定点号两侧的空白，引用名称内部不改写；用目录模式限定；只有兼容的整数 CLR 类型使用序列策略，非整数列保留限定默认 SQL，序列定义仍保留；默认 SQL 列若参与主键或被选中外键引用成候选键，且无支持的键生成策略，则反向工程明确拒绝，普通非键列继续支持。

IDENTITY 反向工程只接受映射为 int/long 的列。达梦服务器可有 DEC(n,0) IDENTITY，
但当前提供程序不支持该 CLR decimal 标识策略，因此明确拒绝。

反向工程本地序列仅接受 CACHE_SIZE=0、ORDER_FLAG=N；缓存序列与 ORDER 序列明确拒绝。
CREATE SEQUENCE 显式生成 NOCACHE NOORDER，ALTER 不重置缓存/排序设置。

反向工程按目录保留显式 CHAR/BYTE 单位。BYTE 仅支持 CHAR/VARCHAR/VARCHAR2，
提供程序生成的建表/加列/改列 DDL 先检查 SF_GET_LENGTH_IN_CHAR()=0，否则拒绝；
不要把模式 0 真库中的守卫分支测试描述为模式 1 实例验证。

反向工程的 B 树表保留 Dameng:IsClusterBtree 注解并生成 STORAGE(CLUSTERBTR)，
堆表/未知存储拒绝，存储变更须重建；未设置注解的手写模型保持原行为。
选中外键若跨模式、主体未选中/不可读或列无法解析，明确拒绝从表，不省略关系。

## 账户

连接到已经存在的实例和用户。提供程序不创建、不删除物理数据库。

2026-09-22 的实例上：

- `RESOURCE` 足够执行本次验证过的建表、序列、模式、索引、约束和种子 DML，也能调用 `DBMS_LOCK`。
- `Database.Migrate()` 会查询 `SYS.SYSOBJECTS` 判断历史表是否存在。`RESOURCE` 不够。该实例上再授予 `SOI` 后可以通过。不要为了这一项授予 `DBA`。
- 直接执行生成脚本里的 `INSERT` 历史行不经过这次查询。
- 达梦 MPP 没有同一套 `DBMS_LOCK` 基线。

需要隔离的测试库时，用管理员连接创建表空间和用户，而不是 `CREATE DATABASE`。数据文件目录取 `V$DATAFILE.PATH` 的所在目录，大小按 `PAGE()`：4 KB→16 MB，8 KB→32 MB，16 KB→64 MB，32 KB→128 MB。结束时 `DROP USER "<user>" CASCADE`，再 `DROP TABLESPACE "<tablespace>"`。

## 非幂等脚本

`GenerateCreateScript()` 和 `GenerateScript()` 都不加 `Idempotent`。语句以 `;` 结束，标识符使用双引号，不生成 `@name`。`EnsureSchema` 和自定义匿名块是例外：`EnsureSchema` 生成自带存在性守卫的匿名 `BEGIN ... END;` 块；`BEGIN` 或 `DECLARE` 打开的自定义块也必须作为一条命令执行，不按内部的分号切开。

执行时按分号切开。分号出现在单引号字符串、双引号标识符或注释里时不要切开。`''` 和 `""` 是转义。空语句丢掉。每条剩下的文本单独 `ExecuteNonQuery`。`COMMIT;` 可以单独执行。

`GenerateCreateScript()` 只反映当前模型，不写 `__EFMigrationsHistory`，之后的模型变化不会变成增量升级。

## 幂等脚本

`GenerateScript(MigrationsSqlGenerationOptions.Idempotent)` 的形态是：

1. `CREATE TABLE IF NOT EXISTS` 创建历史表。
2. 每个命令外面包一层 `BEGIN ... IF NOT EXISTS ... THEN ... END IF; END;`。
3. 普通 SQL 按引号/注释外的分号拆成独立 `EXECUTE IMMEDIATE '...'`，DDL 和种子各自动态执行，避免 CREATE 后续 DML 提前绑定。历史插入留在块内，不再套一层动态 SQL。
4. 例外：`EnsureSchema` 生成自带 `SYS.SYSOBJECTS`（`TYPE$ = 'SCH'`）存在性守卫的匿名块。服务器不接受把块再包进 `EXECUTE IMMEDIATE`，所以该命令以原样的内嵌 `BEGIN ... END;` 出现在历史守卫内（块可以嵌套）。
5. 每个块后面有单独一行 `/`。这是 disql 批次分隔符，不是 SQL。

disql 可以直接跑带 `/` 的文件。ADO.NET 不能把 `/` 放进 `CommandText`。应用执行时：

1. 按不在字符串内、且 trim 后恰好是 `/` 的行切开。
2. `/` 行本身不执行。
3. 每个片段里，`BEGIN` 之前的语句按分号执行。
4. 从 `BEGIN` 或 `DECLARE` 到配平的 `END;` 作为一条命令执行，连同紧邻的前导注释一起保留。按引号/注释外的词法 token 识别，不能要求关键字独占一行；区分 `END IF`、`END LOOP`、`CASE ... END` 与块结束。块内允许再嵌套 `BEGIN ... END;`。

本仓库测试执行器支持匿名块的变量声明、局部过程/函数声明及其嵌套体，但不是完整 DMSQL 脚本解析器。`CREATE PROCEDURE/FUNCTION/TRIGGER/PACKAGE` 定义明确拒绝拆批；这类定义应作为完整命令交给相应执行器，不按内部的分号拆开。

幂等生成拒绝拆分 `CREATE [OR REPLACE] PROCEDURE/FUNCTION/TRIGGER/PACKAGE/TYPE` 存储定义，
以及普通 SQL 后混入的 `BEGIN`/`DECLARE`；匿名块应单独作为一个 `SqlOperation`，外层 END 后追加 SQL 也会拒绝；存储定义另行整体执行。

同一脚本执行第二遍应保持种子一行、每个 `MigrationId` 一行。单条动态 SQL 转义后的 UTF-8 超过 32767 字节时，生成阶段会抛 `NotSupportedException`，把 migration 拆小。自定义 `migrationBuilder.Sql(...)` 里不能出现单独一行 `/`。

## 本次执行通过的模型

- `BIGINT IDENTITY(seed, increment)`，以及种子行周围的 `SET IDENTITY_INSERT`。显式插入 20、增量为 2 时，下一行是 22。
- 序列列默认 `"序列".NEXTVAL`。未改增量时，起点 41、增量 3，插入一行后再取 `NEXTVAL` 得到 44。
- 在第一次 `NEXTVAL` 之前把增量从 3 改成 5 时，达梦按起始前位置加新增量给出 43，再下一次是 48。不要假设改增量后的第一个值仍是 `START WITH`。
- 虚拟计算列 `AS (UPPER("NAME"))`。存储计算列会在生成时拒绝。
- 主键、唯一约束、`CHECK`、`ON DELETE CASCADE`。
- 唯一索引可以混用升序和 `DESC`。筛选索引会在生成时拒绝。
- `CREATE SCHEMA`，以及该模式中的普通表。不要用重命名把表或序列挪到另一个模式。
- 重命名列、表、索引；删除列。
- 给已有行增加 `INT NOT NULL DEFAULT 1` 时，该实例把已有行回填为 1。
- Unicode 种子和包含撇号的种子。

生成阶段会拒绝、测试也没有执行的结构：筛选索引、存储计算列、用 `ALTER COLUMN` 增删 `IDENTITY` 或修改其种子/增量、跨模式重命名、TPC 上的标识列。标识列改用删列再建列；TPC 改用共享序列。

## 不要做的事

- 不要把幂等脚本整段连同 `/` 交给一次 `ExecuteNonQuery`。
- 不要在应用启动时创建或删除物理库。
- 不要因为 `GenerateScript` 能生成，就宣布所有 EF 迁移操作都支持。
- 不要把跳过的数据库测试写成发布证据。
