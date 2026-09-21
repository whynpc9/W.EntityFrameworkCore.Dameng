# 数据类型、LOB 与 JSON

达梦覆盖 SQL-92 的大部分类型，并带 Oracle / SQL Server 别名。生成 DDL 时按达梦类型写，不要输出 `uniqueidentifier`、`NVARCHAR(MAX)`、`BYTEA`、`SERIAL`。

## 字符

`CHAR` / `VARCHAR` / `VARCHAR2` / `NVARCHAR2` 在达梦中行为接近；不要按 Oracle 那套「VARCHAR 与 VARCHAR2 不同」来设计。

行内变长字符串上限由**页大小**决定；表达式计算中 `VARCHAR` 上限为 32767，不受页大小限制。指定 `USING LONG ROW` 后，插入长度不再被页大小卡住。

中文长度：

- `UNICODE_FLAG=0`：GB18030；`=1`：UTF-8。初始化后不能改。`SELECT UNICODE;` 可查。
- `LENGTH_IN_CHAR=0`：按字节。UTF-8 下一个汉字约 3 字节，GBK/GB18030 约 2 字节。
- `LENGTH_IN_CHAR=1`：`CHAR(1)` 可存一个字符。

```sql
CREATE TABLE dmhr.char_test (name VARCHAR(3));
INSERT INTO dmhr.char_test VALUES ('测');     -- UTF-8 且 LENGTH_IN_CHAR=0 时可能已占满
INSERT INTO dmhr.char_test VALUES ('测试');   -- 往往会超长失败
```

## 数值

精确：`NUMERIC` / `DECIMAL` / `DEC` / `NUMBER` / `INT` / `INTEGER` / `BIGINT` / `SMALLINT` / `TINYINT` / `BYTE`。精度 1–38。

近似：`FLOAT` / `DOUBLE` / `REAL` / `DOUBLE PRECISION`。

另外还有 `MONEY`（精度 19，标度 4）、`BIT`、`BOOLEAN`、`BINARY` / `VARBINARY`。

```sql
CREATE TABLE dmhr.numeric_test (
  cust_id INT NOT NULL,
  amt     NUMERIC(10, 2)
);
```

**整数除法默认得到整数**（`SELECT 1/5 FROM dual` → `0`）。要小数：

```sql
SELECT 1.0 / 5 FROM dual;
SELECT TRUNCATE(1 / 5.0, 4) FROM dual;
```

或把 INI `CALC_AS_DECIMAL` 设为 2 后再重启（影响实例行为，不要在应用 SQL 里擅自依赖）。

缩小 `NUMBER(p,s)` 标度时，超出部分按文档为截断/四舍五入规则处理；不要假设与 Oracle 完全相同。

## 位与布尔

达梦没有到处可用的应用级 `BOOL` 列约定。用：

```sql
CREATE TABLE dmhr.bit_test (
  cust_id   INT NOT NULL,
  cust_name VARCHAR(10),
  sex       BIT          -- 0 / 1 / NULL；非 0 非 NULL 视为真
);
```

`BOOLEAN` 字面量是 `TRUE` / `FALSE` 或 1 / 0。

## 日期时间与间隔

```sql
CREATE TABLE dmhr.date_test (
  pay_date      DATE,
  pay_time      TIME(2),
  pay_ts        TIMESTAMP,
  pay_dt        DATETIME,
  pay_dt_tz     DATETIME(7) WITH TIME ZONE,
  span_ym       INTERVAL YEAR(4) TO MONTH,
  span_ds       INTERVAL DAY(9) TO SECOND(6)
);
```

- `TIME` 小数秒 0–6，缺省 0。
- `TIMESTAMP` 小数秒 0–6，缺省 6。
- 间隔不写精度时缺省 6。间隔值有符号。
- 默认当前时间用函数，不要加引号：`CURRENT_TIMESTAMP` / `SYSDATE` / `SYSTIMESTAMP`。

日期字面量可用 `DATE '2020-10-01'`、`DATETIME '2020-10-01 09:28:00'`。

## 多媒体 / LOB

| 类型 | 用途 |
| --- | --- |
| `TEXT` / `LONG` / `LONGVARCHAR` | 长文本 |
| `CLOB` / `NCLOB` | 字符大对象 |
| `BLOB` / `IMAGE` / `LONGVARBINARY` | 二进制大对象 |
| `BFILE` | 操作系统上的只读外部文件 |

长度数量级为 100G-1（以当前手册为准）。BLOB/IMAGE 字面量用十六进制串。

约束：

- 不能 `ORDER BY` / `DISTINCT` / 普通比较 BLOB、CLOB
- 模糊匹配 CLOB 时注意截断；不要 `CAST(clob AS VARCHAR) LIKE ...` 当通用方案
- BLOB 长度：`DBMS_LOB.GETLENGTH(col)` 或 `LENGTHB(col)`，不要 `LENGTH(blob)`
- 聚集主键/聚集索引表不能同时包含大字段

字符串聚合超长时，`LISTAGG` 可能报截断，可改 `LISTAGG2`（仍要评估返回类型）。

## JSON

达梦有 `JSON` 类型，也可用 VARCHAR 加检查：

```sql
CREATE TABLE jemp (
  c VARCHAR CHECK (c IS JSON),
  g INT GENERATED ALWAYS AS (JSON_VALUE(c, '$.id'))
);

CREATE INDEX idx_jemp ON jemp(g);
```

不要写 MySQL 运算符 `->` / `->>`：

```sql
SELECT
  JSON_EXTRACT(data, '$.name') AS name_json,
  JSON_UNQUOTE(JSON_EXTRACT(data, '$.name')) AS name_str,
  JSON_VALUE(data, '$.address.city') AS city
FROM users;
```

整值 `JSON` 列用 `=` 比较可能报数据类型不匹配。需要等值时用文档提供的 JSON/文本函数，而不是假设 JSON 是普通标量。

## 虚拟列

```sql
ALTER TABLE accounts ADD line_total AS (TO_CHAR(price * quan));
```

`GENERATED ALWAYS` 与 `VIRTUAL` 可写可不写，语义相同。虚拟列：

- 不能是 IDENTITY
- 表达式必须确定；不能有分组、子查询、分析函数
- 有索引后不能再改类型或表达式

## 空间与对象类型

空间类型、`TYPE` 对象数组、VARRAY 集合属于扩展，不要在常规业务 DDL 里默认生成。需要时查《DM8_SQL 语言使用手册》空间/对象章节，而不是套 Oracle 对象类型全文。
