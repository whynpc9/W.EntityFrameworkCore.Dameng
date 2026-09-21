# 标识符、模式与参数

达梦默认**大小写敏感**。未加双引号的标识符在解析时折叠为大写。用双引号创建的名称必须始终带双引号访问。

## 合法名称

- 正规标识符最长 128 个英文字符，或 64 个汉字。
- 以数字开头的用户名/对象名必须加双引号。
- 与保留字冲突时加双引号，例如 `"DOMAIN"`。不要默认去改 `dm_svc.conf` 的 `KEYWORDS` 或 JDBC `keyWords`。

```sql
CREATE TABLE "app_user"."Order" (
  "Id"   INT,
  "Name" VARCHAR(50)
);

SELECT "Id", "Name" FROM "app_user"."Order";
```

别名含特殊字符时也必须加双引号：

```sql
SELECT name AS "test@123" FROM app_user.t_test;
```

## 用户与模式

登录用户通常拥有同名默认模式。引用其他模式的对象必须写 `模式.对象`。

```sql
CREATE TABLE test.tttt (c1 INT);
INSERT INTO test.tttt VALUES (1);
COMMIT;

SELECT * FROM tttt;          -- 当前模式不是 TEST 时失败
SET SCHEMA test;
SELECT * FROM tttt;          -- 成功
```

`SET SCHEMA` 只切换当前模式，不是 MySQL 的 `USE database`，也不会换一个物理库。

预定义管理账号包括 `SYSDBA`、`SYSSSO`、`SYSAUDITOR`（四权分立还有 `SYSDBO`）。`SYS` 保存系统内部对象，不能登录。

## `dual`

```sql
SELECT 1 FROM dual;          -- 正确：不要写模式名
SELECT SYSDATE FROM dual;
```

无 `FROM` 的 `SELECT 1` 在 DM8 上通常也可执行。DM6 的 `dual` 曾在 `SYSTEM.SYSDBA` 下，必要时才建 `PUBLIC SYNONYM`。

## 绑定参数

| 场景 | 写法 |
| --- | --- |
| ADO.NET / EF / 本仓库生成的 SQL | `:name` |
| 不要生成 | `@name` |
| disql 替换变量 | `&name`（可用 `SET DEFINE OFF` 关闭，避免字面量 `&` 被当成输入提示） |
| ODBC 位置参数 | `?`（仅当调用方明确是 ODBC） |

```sql
SELECT * FROM app_user.orders WHERE "Id" = :id;
```

`INSERT ... VALUES (...), (...), ...` 的绑定参数个数有上限（文档常见 2048）。超大批次改用 `INSERT ALL ... SELECT 1 FROM dual`，或拆成多条语句。

## 字符串拼接

达梦用 `||` 或 `CONCAT`：

```sql
SELECT employee_name || ' salary is ' || salary AS col1
FROM dmhr.employee;

SELECT CONCAT('ABC', 'BCD', 'DDD') AS output FROM dual;
```

不要用 SQL Server 的 `+` 做字符串拼接（`+` 是数值加法）。
