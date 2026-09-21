# 数据库对象

表的基础 `CREATE TABLE` / `INSERT` / `SELECT` 与主流一致。下面是达梦对象上的差异和示例。官方入门示例使用示例库 `DMHR`。

## 表

```sql
CREATE TABLE dmhr.city (
  city_id   CHAR(3) NOT NULL,
  city_name VARCHAR(40) NULL,
  region_id INT NULL
);

INSERT INTO dmhr.city (city_id, city_name, region_id)
VALUES ('BJ', '北京', 1);
```

建表后不能调整列顺序；需要新顺序就重建表再导数据。

## 视图

```sql
CREATE VIEW dmhr.v_city AS
SELECT city_id, city_name, region_id
FROM dmhr.city
WHERE region_id < 4;

CREATE VIEW purchasing.vendor_excellent AS
SELECT vendorid, accountno, name, activeflag, credit
FROM purchasing.vendor
WHERE credit = 1
WITH CHECK OPTION;

ALTER VIEW purchasing.vendor_excellent COMPILE;
DROP VIEW purchasing.vendor_excellent;
```

- `WITH CHECK OPTION`：通过视图的 DML 必须满足 `WHERE`。MPP 不支持该选项。
- `WITH READ ONLY`：只读。
- 分组视图（`GROUP BY` / 聚合）必须在视图名后列出列名。
- 基表变更后视图可能失效，需要 `COMPILE`。

## 同义词

```sql
CREATE SYNONYM dmhr.s1 FOR dmhr.t1;
SELECT COUNT(*) FROM dmhr.s1;
DROP SYNONYM dmhr.s1;
DROP PUBLIC SYNONYM s2;
```

`ENABLE_PL_SYNONYM=0` 时，禁止通过全局同义词执行非系统用户的包或 DMSQL 程序。

## 序列

见 [identity-sequences.md](identity-sequences.md)。对象创建入门示例：

```sql
CREATE SEQUENCE dmhr.seq_quantity START WITH 5 INCREMENT BY 2 MAXVALUE 200;
SELECT dmhr.seq_quantity.NEXTVAL FROM dual;
```

## 触发器

```sql
CREATE TRIGGER dmhr.trg_upd
AFTER UPDATE ON dmhr.city
FOR EACH ROW
BEGIN
  PRINT 'UPDATE OPERATION ON CITY !!';
END;
/

UPDATE dmhr.city SET region_id = 8 WHERE city_id = 'XA';
```

disql 中触发器体用 `/` 结束。

## 定时作业

图形化：先创建代理环境，再新建作业（SQL 脚本步骤或备份），然后设调度（例如每周日每小时一次）。

命令行作业属于代理子系统，不要用 SQL Server `sp_add_job` 或 Oracle `DBMS_SCHEDULER` 原文硬套。需要脚本化时查当前版本《DM8 系统管理员手册》作业章节，并在目标实例上验证。

## DBLINK

对象名是 `LINK`，远程引用用 `表@链接`。

```sql
CREATE PUBLIC LINK link01
  CONNECT WITH SYSDBA IDENTIFIED BY "********"
  USING '127.0.0.1/5282';

INSERT INTO test@link01 VALUES (1, 'A');
UPDATE test@link01 SET c2 = 'C' WHERE c1 = 1;
SELECT * FROM test@link01;
DROP LINK link01;
```

同构 DM→DM 需要双方 `MAL_INI=1` 且 `dmmal.ini` 中实例名不同、网络互通，**不支持跨平台、不支持 MPP**。

异构：

```sql
CREATE LINK link1
  CONNECT 'ORACLE' WITH user01 IDENTIFIED BY "********"
  USING '127.0.0.1/orcl';
```

目前支持 DM、Oracle、ODBC。限制：增删改不支持 `INTO` 语句、不能用游标远程改数据、不支持远程复合类型列、LOB 仅常量级简单 DML。

## HUGE 表

列存宽表，必须落在混合表空间。不支持 IDENTITY、聚集索引；唯一约束行为受 `HUGE_UNIQUE_CHECK` 等参数影响。非事务型 HUGE 表的 DML 不能回滚。

```sql
CREATE HUGE TABLE orders (
  o_id      INT,
  o_comment VARCHAR(100)
)
FILESIZE 64
STAT SYNCHRONOUS;
```

不要为普通 OLTP 表默认生成 `CREATE HUGE TABLE`。

## 注释与授权

```sql
COMMENT ON TABLE aefd01 IS '会计事件定义';
GRANT SELECT ON sys.v$datafile TO app_user;
```

`CREATE TABLE AS SELECT` / `CREATE TABLE LIKE` 都不会带上注释、主键和索引。
