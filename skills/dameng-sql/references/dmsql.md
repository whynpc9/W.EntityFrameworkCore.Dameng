# DMSQL：过程、函数、包与 disql

DMSQL 是达梦对 SQL 的过程化扩展，外形接近 Oracle PL/SQL，不是 SQL Server T-SQL，也不是 PostgreSQL plpgsql。

## disql 结束符

| 语句 | 结束 |
| --- | --- |
| 普通 SQL | `;` |
| 过程 / 函数 / 包 / 触发器 / 模式 / 匿名块 | `/` |

只写 `;` 创建过程时，disql 会提示脚本不完整：`The script file is not complete`。

`/` 是 **disql / 脚本客户端** 的批次结束符，不是达梦服务器 SQL 的一部分。通过 `DmCommand.CommandText` 执行时：

- 发送完整的 `CREATE OR REPLACE PROCEDURE ... END;`（含过程体内的 `;`）
- 去掉末尾单独一行的 `/`
- 不要把过程体拆成多条 `ExecuteNonQuery`

注释行里不要出现 `;`。`-- ... MERGE; not REPLACE` 会被按 `;` 拆批的执行器切成半截注释，后面的 `MERGE` 永远到不了服务器。

打印：

```sql
SET SERVEROUTPUT ON;
BEGIN
  DBMS_OUTPUT.ENABLE();
  DBMS_OUTPUT.PUT_LINE('hello');
END;
/
```

某些环境也支持 `PRINT`。匿名块和过程里的 `PRINT` 在 disql 中若无输出，改用 `DBMS_OUTPUT`。

系统包未创建时先：

```sql
SP_CREATE_SYSTEM_PACKAGES(1, 'DBMS_OUTPUT');
SP_CREATE_SYSTEM_PACKAGES(1, 'DBMS_LOB');
```

## 存储过程

```sql
CREATE TABLE test_tab (
  id   INT PRIMARY KEY,
  name VARCHAR(30)
);

CREATE OR REPLACE PROCEDURE p_test (i IN INT)
AS
  j INT;
BEGIN
  FOR j IN 1 .. i LOOP
    INSERT INTO test_tab VALUES (j, 'p_test' || j);
  END LOOP;
END;
/

p_test(3);
CALL p_test(3);
```

无参过程可以直接 `p_test2;` 调用。`IN OUT` 参数在部分客户端（例如某些 Declare 绑定）需要可写缓冲区。

大小写：`CREATE PROCEDURE p_test` 存成 `P_TEST`；`CREATE PROCEDURE "p_test1"` 必须 `CALL "p_test1"()`。

跨用户调用别人的过程/函数/包需要显式授权。`ENABLE_PL_SYNONYM=0` 时，不能通过全局同义词执行非系统用户的包或 DMSQL 程序。

过程中动态表名用动态 SQL，不要指望静态游标接受表名变量。

## 函数

```sql
CREATE OR REPLACE FUNCTION get_sex (id_card IN VARCHAR(50))
RETURN CHAR(2)
AS
  v_sex CHAR(2);
BEGIN
  IF TO_NUMBER(SUBSTR(id_card, 17, 1)) % 2 = 1 THEN
    v_sex := '男';
  ELSE
    v_sex := '女';
  END IF;
  RETURN v_sex;
END;
/

SELECT identity_card, get_sex(identity_card) FROM dmhr.employee;
```

函数必须有返回值。`IS` 与 `AS` 均可。模式级函数**不能重载**；要重载放到包里，或写嵌套子程序。

## 包

先包头后包体。包头只放对外接口。包内子程序可以重载。

```sql
CREATE OR REPLACE PACKAGE triangle AS
  FUNCTION calc_area (a NUMBER, b NUMBER, c NUMBER) RETURN NUMBER;
  FUNCTION calc_area (sidelist VARCHAR(24)) RETURN NUMBER;
END;
/

CREATE OR REPLACE PACKAGE BODY triangle IS
  FUNCTION calc_area (a NUMBER, b NUMBER, c NUMBER) RETURN NUMBER
  IS
    p NUMBER := (a + b + c) / 2;
  BEGIN
    RETURN ROUND(SQRT(p * (p - a) * (p - b) * (p - c)), 2);
  END;

  FUNCTION calc_area (sidelist VARCHAR(24)) RETURN NUMBER
  IS
    a NUMBER := TO_NUMBER(REGEXP_SUBSTR(sidelist, '\d+', 1, 1));
    b NUMBER := TO_NUMBER(REGEXP_SUBSTR(sidelist, '\d+', 1, 2));
    c NUMBER := TO_NUMBER(REGEXP_SUBSTR(sidelist, '\d+', 1, 3));
  BEGIN
    RETURN calc_area(a, b, c);
  END;
END;
/
```

包头改过之后包体会变成 invalid。达梦**不会**自动判断包体是否仍匹配，需要：

```sql
ALTER PACKAGE triangle COMPILE PACKAGE;
ALTER PACKAGE triangle COMPILE SPECIFICATION;
ALTER PACKAGE triangle COMPILE BODY;
```

每个会话有自己的包实例。包体底部的初始化块在首次引用时执行一次，结果在该会话内保持。

## 嵌套子程序与前置声明

```sql
DECLARE
  PROCEDURE pro1 (n1 NUMBER);

  PROCEDURE pro2 (n2 NUMBER) IS
  BEGIN
    pro1(n2);
  END;

  PROCEDURE pro1 (n1 NUMBER) IS
  BEGIN
    NULL;
  END;
BEGIN
  NULL;
END;
/
```

互相调用必须先声明。嵌套子程序可以重载，且不会进数据字典。

## 动态 SQL

```sql
BEGIN
  EXECUTE IMMEDIATE 'INSERT INTO app_user.t1(name) VALUES (:1)' USING 'x';
END;
/
```

也可用 `DBMS_SQL.OPEN_CURSOR` / `PARSE` / `BIND_VARIABLE` / `EXECUTE`。`PARSE` 的第三个参数在达梦示例里常写成 `'1'`。

`EXECUTE IMMEDIATE` 的字符串字面量转义后 UTF-8 超过 32767 字节会失败，必须拆分。这是幂等迁移脚本的硬限制。

从动态 SQL 取新行 `ROWID` 时，用 `RETURNING ROWID INTO ...`，不要假设 JDBC/`@@` 变量自动带回。

## 权限与定义者

```sql
CREATE OR REPLACE PROCEDURE app_user.cs
AUTHID DEFINER
AS
BEGIN
  NULL;
END;
/
```

默认定义者权限。需要调用者权限时用 `AUTHID CURRENT_USER`。未授权时表现为「当前用户无法调用其他用户创建的存储函数/过程/包」。
