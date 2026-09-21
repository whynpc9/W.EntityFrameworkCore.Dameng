# 表空间、用户与存储子句

达梦没有应用层「随手 CREATE DATABASE」的惯例。实例初始化后，日常对象落在**表空间 + 用户/模式**上。本仓库的 EF 提供程序也有意不创建或删除物理数据库。

## 页大小先于一切

页大小在初始化时设定，之后不能改。它同时限制：

- 表空间单个数据文件的最小/最大尺寸
- 每个字符类型字段的实际最大长度
- 一行除大字段外的总长度

| 页大小 | 字符列大约上限（字节） | 行内其他字段大约上限 | 数据文件最小 |
| --- | --- | --- | --- |
| 4 KB | 1938 | 2047 | 16 MB |
| 8 KB | 3878 | 4095 | 32 MB |
| 16 KB | 8000 | 8195 | 64 MB |
| 32 KB | 8188 | 16176 | 128 MB |

查询当前页大小：

```sql
SELECT '页大小', CAST(PAGE() / 1024 AS VARCHAR);
```

数据文件最小约为 `4096 * 页大小`。32 KB 页时，官方示例常用 `SIZE 128`。

文件路径必须来自当前实例。先查：

```sql
SELECT PATH FROM V$DATAFILE;
SELECT PARA_VALUE FROM V$DM_INI WHERE PARA_NAME = 'SYSTEM_PATH';
```

不要把公开文档里的 `/data/dmdata/DAMENG/...` 抄到另一台机器上。

## 创建与修改表空间

```sql
CREATE TABLESPACE "APP_DATA"
  DATAFILE '/data/dmdata/DAMENG/APP_DATA.DBF'
  SIZE 128
  AUTOEXTEND ON NEXT 100 MAXSIZE 10240
  CACHE = NORMAL;

ALTER TABLESPACE "APP_DATA"
  DATAFILE '/data/dmdata/DAMENG/APP_DATA.DBF'
  AUTOEXTEND ON NEXT 100 MAXSIZE 10240;
```

可选加密（算法和口令都可以空；生产口令不要写进仓库或对话）：

```sql
CREATE TABLESPACE "APP_SECURE"
  DATAFILE '/data/dmdata/DAMENG/APP_SECURE.DBF'
  SIZE 128
  AUTOEXTEND ON NEXT 100 MAXSIZE 10240
  CACHE = NORMAL
  ENCRYPT WITH RC4;
```

普通表空间只能存普通表。带 `WITH HUGE PATH` 的是混合表空间，才能存 HUGE 表：

```sql
CREATE TABLESPACE ts_mix
  DATAFILE '/data/dmdata/DAMENG/ts_mix.dbf' SIZE 128
  WITH HUGE PATH '/data/dmdata/DAMENG/ts_mix';

ALTER TABLESPACE ts_mix ADD HUGE PATH '/data/dmdata/DAMENG/ts_mix/huge2';
```

HUGE 路径最多约 127 个。对普通表空间 `ADD HUGE PATH` 会把它升级为混合表空间。

## 用户

```sql
CREATE USER app_user IDENTIFIED BY "********"
  DEFAULT TABLESPACE app_data;

ALTER USER app_user DEFAULT TABLESPACE app_data2;

GRANT RESOURCE TO app_user;
GRANT DBA TO app_user;          -- 仅在确实需要时
```

用 Manager 创建的用户会带上 SVI（属于 PUBLIC）和 VTI；用 DIsql 创建的用户通常只有 SVI。查看系统表还需要额外的 SOI。

跨模式写外键时，仅有 `RESOURCE` 往往不够，需要对被引用表授权。

## 表的 STORAGE

达梦建表常用 `STORAGE` 指定表空间、簇和填充因子，而不是只写一个独立的 `TABLESPACE` 关键字：

```sql
CREATE TABLESPACE fg_person DATAFILE 'FG_PERSON.DBF' SIZE 128;

CREATE TABLE person.person (
  personid INT IDENTITY(1, 1) CLUSTER PRIMARY KEY,
  sex      CHAR(1) NOT NULL,
  name     VARCHAR(50) NOT NULL
)
STORAGE (
  INITIAL 5,
  MINEXTENTS 5,
  NEXT 2,
  ON fg_person,
  FILLFACTOR 85
);
```

管理工具导出常见形态：

```sql
CREATE TABLE "APP_USER"."CITY" (
  "CITY_ID" CHAR(3) NOT NULL
) STORAGE (ON "MAIN", CLUSTERBTR);
```

- `ON <表空间>`：数据落点
- `CLUSTERBTR`：普通 B 树表。当 `LIST_TABLE=1` 时，必须指定它才不是堆表
- `USING LONG ROW`：行长度不再受「页大小一半」硬限制，超长变长列可转行外存储

```sql
CREATE TABLE app_user.wide (
  id   CHAR(8188),
  c1   VARCHAR(8188),
  c2   VARCHAR(8188)
) STORAGE (USING LONG ROW);
```

临时表、HUGE 表、外部表不支持 `USING LONG ROW`。

## 不要做的事

- 不要用 SQL Server 的 `ON [PRIMARY]` 文件组语法
- 不要用 MySQL 的 `ENGINE=InnoDB`
- 不要假设可以在应用里 `DROP DATABASE` / `CREATE DATABASE`
- 不要忽略页大小就声明 `VARCHAR(8000)`——4 KB/8 KB 页上会失败或截断预期
