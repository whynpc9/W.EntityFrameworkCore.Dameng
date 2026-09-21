# 查询：分页、层级、MERGE、分析函数

`SELECT` / `JOIN` / `WHERE` / `GROUP BY` / 窗口函数与主流数据库足够接近。下面只列达梦容易写错的部分。

## 限制行数

三种都能用，按调用方习惯选一种，不要混在同一条语句里叠三套分页。

```sql
-- 1) ROWNUM（赋值前过滤；分页取第 N 行要包一层）
SELECT * FROM (
  SELECT ROWNUM AS rn, t.*
  FROM dmhr.employee t
  WHERE ROWNUM <= 10
) WHERE rn = 2;

-- 2) LIMIT
SELECT * FROM dmhr.employee LIMIT 2;

-- 3) OFFSET / FETCH（EF 常用）
SELECT * FROM dmhr.employee
ORDER BY employee_id
OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY;
```

`WHERE` 里不能直接引用 SELECT 列表别名，必须再包一层：

```sql
SELECT * FROM (
  SELECT employee_id AS emid, salary AS sal FROM dmhr.employee
) WHERE sal > 10000;
```

## NULL

`NULL` 不能做算术和普通比较。替换用 `NVL` / `COALESCE`。

排序时空值默认靠前（包括 `ASC` 和 `DESC`）。要控制：

```sql
ORDER BY hire_date ASC NULLS LAST;
ORDER BY hire_date DESC NULLS FIRST;
ORDER BY NVL(hire_date, DATE '1970-01-01');
```

INI `ORDER_BY_NULLS_FLAG=1` 会让升序时空值靠后，降序时该参数无效。不要在应用 SQL 里依赖实例参数。

## 层级查询

```sql
INSERT INTO app_user.table1
SELECT LEVEL, LEVEL, LEVEL FROM dual CONNECT BY LEVEL < 10000;

SELECT LEVEL, employee_id, manager_id
FROM dmhr.employee
START WITH manager_id IS NULL
CONNECT BY PRIOR employee_id = manager_id;
```

## MERGE / 替代 MySQL REPLACE INTO

没有 `REPLACE INTO`。用 `MERGE`：

```sql
MERGE INTO app_user.table1
USING app_user.stage t
ON (app_user.table1.code = t.code)
WHEN MATCHED THEN
  UPDATE SET
    app_user.table1.amount = t.amount,
    app_user.table1.age = t.age
WHEN NOT MATCHED THEN
  INSERT (code, amount, age) VALUES (t.code, t.amount, t.age);
```

`UPDATE ... ORDER BY` 不是标准写法；需要按序更新时改用子查询或 MERGE。

## 分析函数

`LEAD` / `LAG` / `SUM() OVER` 与标准窗口函数一致，达梦文档用它们做连续区间合并：

```sql
SELECT
  log_name,
  log_time,
  LEAD(log_time) OVER (PARTITION BY log_name ORDER BY log_time) AS next_time
FROM v;
```

连续段分组：用 `LAG` 标出间断，再 `SUM(flag) OVER (ORDER BY id)` 得到分组键。

## LIKE 与下划线

`_` 是单字符通配符。要按字面匹配：

```sql
SELECT * FROM t_test WHERE name LIKE 'W/_%' ESCAPE '/';
```

前缀 `name LIKE 'str%'` 可以走索引；前后缀 `%str%` 通常不能。

绑定变量类型必须和列一致，否则优化器会加 `CAST` 并放弃索引：列是 `VARCHAR2` 时，谓词用 `'5'` 而不是数值 `5`。

## FOR UPDATE

部分查询表达式不允许 `FOR UPDATE`（含某些视图、去重、集合运算）。被拒绝时改成对基表加锁，或拆语句。

## 复制表

```sql
CREATE TABLE t_new AS SELECT * FROM t_old;   -- 不含约束/索引/自增
CREATE TABLE t_new LIKE t_old;               -- 同样不复制主键约束
```

要完整结构：

```sql
SELECT DBMS_METADATA.GET_DDL('TABLE', 'T_OLD', 'APP_USER') FROM dual;
```

然后改表名再执行。`CTAS` 也不会复制 IDENTITY。

## 条件函数

达梦支持 `CASE`、`DECODE`、`IF`。优先 `CASE`。不要假设 MySQL `IF(cond, a, b)` 在所有 `compatible_mode` 下都可用。
