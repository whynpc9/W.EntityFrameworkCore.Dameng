# 兼容模式与常见陷阱

## compatible_mode

`dm.ini` 中的兼容模式：

| 值 | 含义 |
| --- | --- |
| 0 | 不兼容 |
| 1 | SQL92 |
| 2 | 部分 Oracle |
| 3 | 部分 SQL Server |
| 4 | 部分 MySQL |
| 5 | DM6 |
| 6 | 部分 Teradata |

没有证据表明目标实例已设置对应模式时：

- 不要生成 `DROP TABLE IF EXISTS` / `CREATE TABLE IF NOT EXISTS`（常要 mode=4）
- 不要生成 MySQL 的 `REPLACE INTO`、`->` JSON 运算符、`ENGINE=`
- 不要生成 SQL Server 的 `TOP`、`@param`、`NEXT VALUE FOR`、`uniqueidentifier`
- 不要假设 Oracle 的闪回、`DBMS_METADATA.GET_DDL` 的 `transform` 参数、全部 `V$` 视图都在

需要 MySQL 风格 `IF EXISTS` 且不能改 INI 时，改为查 `DBA_TABLES` / `USER_TABLES` 再分支。

## 聚集索引与 LOB

手册缺省 `PK_WITH_CLUSTER=1`，但不要假设目标实例仍是 1。先查：

```sql
SELECT PARA_VALUE FROM V$DM_INI WHERE PARA_NAME = 'PK_WITH_CLUSTER';
```

有 LOB 时始终显式 `NOT CLUSTER PRIMARY KEY`。

```sql
CREATE TABLE test_pk2 (
  a INT,
  b VARCHAR(50),
  CONSTRAINT c1 NOT CLUSTER PRIMARY KEY (a)
);
```

或 `SP_SET_PARA_VALUE(1, 'PK_WITH_CLUSTER', 0)`（影响实例，应用 DDL 里不要依赖）。

报错「表中不能同时包含聚集 KEY 和大字段」→ 改非聚集主键或把 LOB 拆表。

「不能修改或删除聚集索引的列」/「试图删除聚集主键」→ 先去掉聚集属性或重建表。

并发建索引：`CREATE INDEX ... PARALLEL 2`。分区表索引的并行有单独限制，失败时不要假定所有版本都支持。

## 统计信息

过滤条件不走索引时，先收集统计而不是盲目加 hint：

```sql
DBMS_STATS.GATHER_TABLE_STATS('APP_USER', 'ORDERS', NULL, 100, TRUE, 'FOR ALL COLUMNS SIZE AUTO');
SP_COL_STAT_INIT('APP_USER', 'ORDERS', 'STATUS');
```

几乎没有区分度的列不会走索引，这是优化器行为，不是语法错误。

## 空值排序与整数除法

见 [query.md](query.md) 和 [types.md](types.md)。不要按 Oracle「NULLS LAST 默认」或「整数相除得小数」来写断言。

## 权限

- 新用户默认看不到系统表，需要 SOI
- `RESOURCE` 用户跨模式插外键，可能报没有被引用表权限
- 可更新视图带 `WITH CHECK OPTION` 时，DML 不满足谓词会报视图 CHECK 约束

## 其他

| 现象 | 处理 |
| --- | --- |
| 插入含 `&` 的字面量时 disql 提示输入变量 | `SET DEFINE OFF` 或转义 |
| 中文 SQL 报语法错 | 先查中文标点和空白 |
| `create table as select` 丢约束 | 用 `DBMS_METADATA.GET_DDL` |
| 全文索引列不能改精度 | 先删 context index 再改 |
| UNION 视图 / 快速刷新物化视图失败 | 达梦对 UNION 物化视图有额外限制，不要默认生成 |
| `TRUNCATE` 后失效索引重建 | 预期行为 |
| 嵌套层次太深 | 拆 SQL 或改写层次查询 |
| DBLINK / 分布列类型不一致 | 引用列与被引用列的类型、分布、分区、存储位置必须一致 |
| 参数个数过多 | 拆批或 `INSERT ALL` |

## DDL 与事务

DDL 隐式提交。迁移、测试夹具、幂等脚本都按「每条 DDL 立即生效」设计。失败时用精确的 `DROP ...` 清理，而不是 `ROLLBACK`。

## 本仓库边界

本 skill 描述的是达梦 SQL 方言。`W.EntityFrameworkCore.Dameng` 并不实现这里的全部对象（例如 DBLINK、HUGE 表、作业、反向工程）。写 C# 提供程序代码时同时遵守仓库 `AGENTS.md` 和 `docs/compatibility.md`。
