---
name: dameng-sql
description: >-
  Writes and reviews Dameng (达梦/DM8) SQL using Dameng-specific dialect, not
  Oracle/MySQL/SQL Server copies. Covers CREATE TABLESPACE, IDENTITY vs
  AUTO_INCREMENT, sequence.NEXTVAL/CURRVAL, DMSQL procedures and packages,
  dual/ROWNUM/LIMIT, quoted identifiers, :name bind parameters, STORAGE(ON),
  CLUSTERBTR, USING LONG ROW, and compatible_mode. Use when generating Dameng
  SQL, migrating from another database, using disql/DIsql, or when the user
  mentions 达梦, DM8, Dameng, SYSDBA, or 表空间.
license: MIT
metadata:
  author: W.EntityFrameworkCore.Dameng contributors
  version: "1.1"
---

# 达梦数据库 SQL

面向 DM8 的 SQL 编写与审查。默认按 **Oracle 风格 + 达梦扩展** 生成语句，不要把 MySQL、SQL Server 或 PostgreSQL 方言原样套进来。

完整手册在服务器安装目录 `/dmdbms/doc` 的《DM8_SQL 语言使用手册》和《DM_SQL 程序设计》。本 skill 只汇总公开文档中会影响正确性的差异。

## 何时使用

- 编写、改写、审查达梦 SQL / DMSQL / disql 脚本
- 从 Oracle、MySQL、SQL Server 迁移 SQL
- 建表空间、用户、自增列、序列、包、触发器、DBLINK
- 处理标识符大小写、页大小、LOB、兼容模式等达梦特有行为

不要用本 skill 声称 EF Core 提供程序已支持这里列出的全部 SQL。提供程序能力以仓库 `docs/compatibility.md` 为准。

## 工作方式

1. 能连上目标实例时，先探测，不要沿用手册示例路径或「产品缺省值」：

```sql
SELECT PAGE() FROM dual;
SELECT PARA_NAME, PARA_VALUE FROM V$DM_INI
 WHERE PARA_NAME IN (
   'COMPATIBLE_MODE', 'PK_WITH_CLUSTER', 'LENGTH_IN_CHAR',
   'CALC_AS_DECIMAL', 'LIST_TABLE', 'GLOBAL_CHARSET');
SELECT PATH FROM V$DATAFILE;
```

2. 再读下面的「与主流一致」和「达梦独有」速查，按主题打开对应 reference。
3. 生成 SQL 时优先双引号标识符、`:name` 绑定参数、显式模式名。
4. 「新建一个数据库」在达梦里是 **表空间 + 用户/模式 + 授权**，不是 `CREATE DATABASE`。
5. `/` 只是 **disql 客户端**结束符，不要放进 ADO.NET/`DbCommand.CommandText`。过程体内部的 `;` 必须保留，且不能按 `;` 切开过程。
6. 脚本注释里不要写 `;`，朴素拆批器会把它当成语句结束。
7. 包含 DDL 的脚本按「隐式提交」处理：不要假设能和 DML 一起回滚。

## 与主流 SQL 一致的部分

下列写法与常见关系数据库足够接近，不必展开：

`SELECT` / `INSERT` / `UPDATE` / `DELETE`、`JOIN`、`WHERE`、`GROUP BY`、`HAVING`、`ORDER BY`、`UNION` / `UNION ALL`、`CASE`、`EXISTS`、标准聚合、`COMMENT ON`、对象级 `GRANT` / `REVOKE`、`CREATE VIEW AS SELECT`、窗口函数 `LEAD` / `LAG` / `SUM() OVER (...)`、`MERGE ... WHEN MATCHED` 的基本形态。

达梦整体更接近 **Oracle**（`DUAL`、`||` 拼接、`NVL`、`TO_CHAR` / `TO_DATE`、`CONNECT BY`、包、序列 `.NEXTVAL`），而不是 MySQL 或 SQL Server。即便如此，也不要假设 Oracle 专有语法都能用。

## 达梦独有（生成 SQL 时必须遵守）

### 1. 标识符：默认大小写敏感，用双引号保留原样

初始化时默认大小写敏感。未加双引号的标识符会被折叠成大写。用双引号创建的小写对象，后续也必须带双引号访问。

```sql
CREATE TABLE "orders" ("id" INT, "name" VARCHAR(50));
SELECT "id", "name" FROM "orders";   -- 正确
SELECT id, name FROM orders;         -- 会去找 ORDERS / ID / NAME，失败
```

关键字冲突同样用双引号，不要依赖客户端 `KEYWORDS` 屏蔽。

绑定参数用 **`:name`**（Oracle/ADO.NET 风格）。不要生成 `@name`（SQL Server）或只依赖 MySQL `?` 位置参数，除非调用方明确是 ODBC 占位。

详细规则见 [references/identifiers.md](references/identifiers.md)。

### 2. 用户、模式、表空间是三件套

达梦里登录用户通常对应同名默认模式。对象写 `模式.对象`。切换当前模式用 `SET SCHEMA`，不是 MySQL 的 `USE db`。

本提供程序和常见应用都连接到**已存在的库和用户**，不负责 `CREATE DATABASE`。

```sql
CREATE TABLESPACE app_data
  DATAFILE '/data/dmdata/DAMENG/app_data.dbf'
  SIZE 128
  AUTOEXTEND ON NEXT 100 MAXSIZE 10240;

CREATE USER app_user IDENTIFIED BY "********"
  DEFAULT TABLESPACE app_data;

GRANT DBA TO app_user;

SET SCHEMA app_user;
```

数据文件最小尺寸由页大小决定：4 KB 页 → 16 MB；8 KB → 32 MB；16 KB → 64 MB；32 KB → 128 MB。页大小在初始化时确定，之后不能改。

**不要复制文档里的** `/data/dmdata/DAMENG/`。先查 `V$DATAFILE.PATH` 或 `V$DM_INI` 的 `SYSTEM_PATH`，把数据文件放到实例实际目录（实机上常见 `/dmdata/data/<实例名>/`）。

表落到哪个表空间，用 `STORAGE (ON ...)`，不是 PostgreSQL 的 `TABLESPACE` 子句单独那一套：

```sql
CREATE TABLE app_user.city (
  city_id   CHAR(3) NOT NULL,
  city_name VARCHAR(40),
  region_id INT
) STORAGE (ON app_data, CLUSTERBTR);
```

`CLUSTERBTR` 表示普通 B 树表（非堆表）。管理工具导出的建表语句经常带 `STORAGE(ON "MAIN", CLUSTERBTR)`。

详细规则见 [references/tablespace-and-users.md](references/tablespace-and-users.md)。

### 3. 三种自增，不要混用

| 方式 | 典型语法 | 回读当前值 | 备注 |
| --- | --- | --- | --- |
| `IDENTITY` | `id INT IDENTITY(1,1)` | `SCOPE_IDENTITY()` / `IDENT_CURRENT('模式.表')` / `@@IDENTITY` | 显式插入需 `SET IDENTITY_INSERT ... ON` |
| `AUTO_INCREMENT` | `id INT PRIMARY KEY AUTO_INCREMENT` | `LAST_INSERT_ID()` | 更接近 MySQL；受会话 INI 影响 |
| 序列 | `seq.NEXTVAL` / `seq.CURRVAL` | `SELECT app_user.order_seq.CURRVAL` | **不要**写 SQL Server 的 `NEXT VALUE FOR` |

```sql
CREATE TABLE app_user.orders (
  id   INT IDENTITY(1, 1) NOT NULL,
  memo VARCHAR(256),
  NOT CLUSTER PRIMARY KEY (id)
);

INSERT INTO app_user.orders (memo) VALUES ('A');
SELECT SCOPE_IDENTITY();
SELECT IDENT_CURRENT('APP_USER.ORDERS');

CREATE SEQUENCE app_user.order_seq START WITH 5 INCREMENT BY 2 MAXVALUE 200;
INSERT INTO app_user.orders_seq (id, memo)
VALUES (app_user.order_seq.NEXTVAL, 'B');
SELECT app_user.order_seq.CURRVAL;
```

需要手工指定 `IDENTITY` 列值：

```sql
SET IDENTITY_INSERT app_user.orders ON;
INSERT INTO app_user.orders (id, memo) VALUES (100, 'manual');
```

同一张表不要同时声明 `IDENTITY` 和 `AUTO_INCREMENT`。HUGE 表不支持 `IDENTITY`。

详细规则见 [references/identity-sequences.md](references/identity-sequences.md)。

### 4. 限制行数：ROWNUM、LIMIT、OFFSET FETCH 都能用

```sql
-- Oracle 风格伪列
SELECT * FROM (
  SELECT ROWNUM AS rn, t.* FROM app_user.employee t WHERE ROWNUM <= 10
) WHERE rn = 2;

-- MySQL 风格
SELECT * FROM app_user.employee LIMIT 2;

-- ANSI / EF 常用
SELECT * FROM app_user.employee
ORDER BY employee_id
OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY;
```

无基表的标量查询可以 `SELECT 1 FROM dual`，也可以没有 `FROM`（服务器接受）。`dual` 前面不要写模式名。GUID：`SELECT SYS_GUID();` 或 `SELECT GUID FROM DUAL;`。

层级查询支持 `CONNECT BY`：

```sql
SELECT LEVEL, employee_id, manager_id
FROM app_user.employee
START WITH manager_id IS NULL
CONNECT BY PRIOR employee_id = manager_id;
```

详细规则见 [references/query.md](references/query.md)。

### 5. DMSQL：过程/函数/包，disql 用 `/` 结束

普通 SQL 用 `;` 结束。创建过程、函数、包、触发器、模式时，**disql** 必须用 **`/`** 结束，否则会提示脚本未完成。

发给数据库服务器的文本（ADO.NET、EF、本仓库测试执行器）是 `CREATE ... END;` **整块一条命令**，不要带 `/`，也不要在 `BEGIN...END` 内部按 `;` 拆批。`/` 只给交互式 disql。

```sql
CREATE OR REPLACE PROCEDURE app_user.add_ten (a IN OUT INT)
AS
  b INT := 10;
BEGIN
  a := a + b;
  PRINT 'result=' || a;
END;
/

CALL app_user.add_ten(3);

CREATE OR REPLACE FUNCTION app_user.add_int (a INT, b INT)
RETURN INT
AS
  s INT;
BEGIN
  s := a + b;
  RETURN s;
END;
/

SELECT app_user.add_int(4, 5);
```

包可以重载；模式级函数不能重载。包头修改后包体会失效，达梦**不会**自动对照包体是否仍匹配包头，需要显式 `ALTER PACKAGE ... COMPILE`。

动态 SQL 用 `EXECUTE IMMEDIATE`。字面量过长（转义后 UTF-8 超过 32767 字节）会失败，必须拆分。

详细规则见 [references/dmsql.md](references/dmsql.md)。

### 6. 类型、页大小、LOB

- `VARCHAR` / `VARCHAR2` / `NVARCHAR2` 在达梦中用法接近，**行内最大长度由页大小决定**（约 4 KB→1900，8 KB→3900，16 KB→8000，32 KB→8188）。超长行用 `STORAGE (USING LONG ROW)`。
- 中文占多少字节取决于 `UNICODE_FLAG`（0=GB18030，1=UTF-8）和 `LENGTH_IN_CHAR`。
- 没有真正的应用级布尔列时，用 `BIT`（0/1/NULL）或 `BOOLEAN`。
- 日期时间：`DATE` / `TIME` / `TIMESTAMP` / `DATETIME`，以及 `DATETIME WITH TIME ZONE`、`INTERVAL YEAR TO MONTH`、`INTERVAL DAY TO SECOND`。
- `TEXT` / `CLOB` / `NCLOB` / `BLOB` / `IMAGE` / `BFILE` 都是大对象。LOB **不能** `ORDER BY`、`DISTINCT`、普通 `=` 比较；长度用 `LENGTHB` 或 `DBMS_LOB.GETLENGTH`，不要对 BLOB 用 `LENGTH`。
- JSON：类型可以是 `JSON`，或 `VARCHAR` + `CHECK (c IS JSON)`。不要写 MySQL 的 `->` / `->>`，改用 `JSON_EXTRACT` / `JSON_VALUE` / `JSON_UNQUOTE`。
- 整数相除默认得到整数。要小数：把操作数写成小数，或设 `CALC_AS_DECIMAL`。

详细规则见 [references/types.md](references/types.md)。

### 7. 聚集主键不能夹带 LOB

手册里 `PK_WITH_CLUSTER` 缺省常为 1，但实例可能已改成 0（先查 `V$DM_INI`）。无论当前值是什么，表上有 CLOB/BLOB 时都显式写非聚集主键，不要赌实例缺省：

```sql
CREATE TABLE app_user.docs (
  id   INT NOT NULL,
  body CLOB,
  CONSTRAINT pk_docs NOT CLUSTER PRIMARY KEY (id)
);
```

不能直接修改或删除聚集索引列。可先在其他列建聚集索引，或重建表。

### 8. DDL 会隐式提交

`CREATE` / `ALTER` / `DROP` / `TRUNCATE` 等 DDL 提交当前事务。含 DDL 的脚本不要包在「失败则全部回滚」的事务里。测试对象用唯一名称，在 `finally` 中按对象精确清理。

### 9. 兼容模式不是默认能力

`compatible_mode`（dm.ini）：0 不兼容，1 SQL92，2 部分 Oracle，3 部分 SQL Server，4 部分 MySQL，5 DM6，6 部分 Teradata。

`DROP TABLE IF EXISTS`、`CREATE TABLE IF NOT EXISTS`、`AUTO_INCREMENT` 的 MySQL 味道，常常依赖 `compatible_mode=4`。没有确认实例参数之前，不要生成这些语法。

其他陷阱见 [references/gotchas.md](references/gotchas.md)。

## 对象速查

表、视图、同义词、序列、触发器、定时作业、DBLINK 的达梦写法见 [references/objects.md](references/objects.md)。

查看表结构：

```sql
SELECT DBMS_METADATA.GET_DDL('TABLE', 'EMPLOYEE', 'DMHR') FROM dual;
SP_TABLEDEF('DMHR', 'EMPLOYEE');
```

## 参考索引

| 文件 | 何时打开 |
| --- | --- |
| [references/identifiers.md](references/identifiers.md) | 引用、大小写、模式、绑定参数、`dual` |
| [references/tablespace-and-users.md](references/tablespace-and-users.md) | 表空间、用户、`STORAGE`、页大小 |
| [references/identity-sequences.md](references/identity-sequences.md) | IDENTITY / AUTO_INCREMENT / 序列 |
| [references/types.md](references/types.md) | 数据类型、LOB、JSON、字符长度 |
| [references/query.md](references/query.md) | 分页、层级、MERGE、分析函数 |
| [references/dmsql.md](references/dmsql.md) | 过程、函数、包、动态 SQL、disql |
| [references/objects.md](references/objects.md) | 视图、同义词、触发器、作业、DBLINK |
| [references/gotchas.md](references/gotchas.md) | 兼容模式、聚集索引、空值排序、权限 |
| [references/sources.md](references/sources.md) | 官方文档出处与阅读范围 |

## 安全

- 绝不要打印或提交真实连接字符串、口令、加密密钥。
- 示例口令一律写成 `"********"`。
- 不要把其他数据库的语法静默改写成「看起来能跑」的近似语句；不确定就标明并拒绝。
