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
| 把已有 migration 交给执行器 | `IMigrator.GenerateScript()`，或尚未做命令行回归的 `dotnet ef migrations script` | 写 | 进程内生成并执行已验证；`dotnet ef` 命令行没有端到端回归 |
| 同一脚本重复执行 | `GenerateScript(MigrationsSqlGenerationOptions.Idempotent)` | 写，并用 `IF NOT EXISTS` 守卫 | 是，同一脚本执行两遍 |

三条脚本都不会让 DDL 变成事务。`CREATE` / `ALTER` / `DROP` 会隐式提交。失败后按对象名清理，不要指望 `ROLLBACK` 收回已执行的 DDL。

`dotnet ef dbcontext scaffold` 没有实现。

## 账户

连接到已经存在的实例和用户。提供程序不创建、不删除物理数据库。

2026-09-22 的实例上：

- `RESOURCE` 足够执行本次验证过的建表、序列、模式、索引、约束和种子 DML，也能调用 `DBMS_LOCK`。
- `Database.Migrate()` 会查询 `SYS.SYSOBJECTS` 判断历史表是否存在。`RESOURCE` 不够。该实例上再授予 `SOI` 后可以通过。不要为了这一项授予 `DBA`。
- 直接执行生成脚本里的 `INSERT` 历史行不经过这次查询。
- 达梦 MPP 没有同一套 `DBMS_LOCK` 基线。

需要隔离的测试库时，用管理员连接创建表空间和用户，而不是 `CREATE DATABASE`。数据文件目录取 `V$DATAFILE.PATH` 的所在目录，大小按 `PAGE()`：4 KB→16 MB，8 KB→32 MB，16 KB→64 MB，32 KB→128 MB。结束时 `DROP USER "<user>" CASCADE`，再 `DROP TABLESPACE "<tablespace>"`。

## 非幂等脚本

`GenerateCreateScript()` 和 `GenerateScript()` 都不加 `Idempotent`。语句以 `;` 结束，标识符使用双引号，不生成 `@name`。

执行时按分号切开。分号出现在单引号字符串、双引号标识符或注释里时不要切开。`''` 和 `""` 是转义。空语句丢掉。每条剩下的文本单独 `ExecuteNonQuery`。`COMMIT;` 可以单独执行。

`GenerateCreateScript()` 只反映当前模型，不写 `__EFMigrationsHistory`，之后的模型变化不会变成增量升级。

## 幂等脚本

`GenerateScript(MigrationsSqlGenerationOptions.Idempotent)` 的形态是：

1. `CREATE TABLE IF NOT EXISTS` 创建历史表。
2. 每个命令外面包一层 `BEGIN ... IF NOT EXISTS ... THEN ... END IF; END;`。
3. DDL 和种子放在 `EXECUTE IMMEDIATE '...'` 里。历史插入留在块内，不再套一层动态 SQL。
4. 每个块后面有单独一行 `/`。这是 disql 批次分隔符，不是 SQL。

disql 可以直接跑带 `/` 的文件。ADO.NET 不能把 `/` 放进 `CommandText`。应用执行时：

1. 按不在字符串内、且 trim 后恰好是 `/` 的行切开。
2. `/` 行本身不执行。
3. 每个片段里，`BEGIN` 之前的语句按分号执行。
4. 从 `BEGIN` 到单独一行 `END;` 作为一条命令执行。

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
