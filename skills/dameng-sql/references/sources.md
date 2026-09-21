# 官方文档出处

本 skill 根据达梦公开文档筛选整理，不是《DM8_SQL 语言使用手册》全文，也不能替代安装目录 `/dmdbms/doc` 中的 PDF/手册。语法细节以目标服务器版本对应手册为准。

## 阅读过并纳入索引的公开页面

### SQL 开发指南

入口：[SQL 开发指南](https://eco.dameng.com/document/dm/zh-cn/sql-dev/)

指南结构：SQL 基础实践、SQL 进阶技巧、DM_SQL、开发最佳实践。示例默认在 `DMHR` 示例库。

已阅读并抽取差异的章节：

| 页面 | 纳入内容 |
| --- | --- |
| [单表查询](https://eco.dameng.com/document/dm/zh-cn/sql-dev/practice-single-table.html) | `DBMS_METADATA.GET_DDL`、`SP_TABLEDEF`、`NVL`、`ROWNUM`、`LIMIT`、`\|\|` / `CONCAT`、WHERE 不能引用别名 |
| [数据类型](https://eco.dameng.com/document/dm/zh-cn/sql-dev/dmpl-sql-datatype.html) | 页大小与 VARCHAR、`BIT`、日期时间精度、间隔、TEXT/BLOB/CLOB/BFILE、`%TYPE` 等程序类型仅点到为止 |
| [函数](https://eco.dameng.com/document/dm/zh-cn/sql-dev/practice-func.html) | 自定义函数 `IS`/`AS`、常用数值/字符串函数表只作「与主流相近」处理 |
| [存储过程](https://eco.dameng.com/document/dm/zh-cn/sql-dev/practice-pro.html) | `CREATE OR REPLACE PROCEDURE`、`FOR .. LOOP`、直接调用 |
| [包和嵌套子程序](https://eco.dameng.com/document/dm/zh-cn/sql-dev/practice-package.html) | 包头/包体、重载、会话级实例、前置声明、编译不会自动同步 |
| [视图、同义词](https://eco.dameng.com/document/dm/zh-cn/sql-dev/practice-view.html) | `WITH CHECK OPTION`、`COMPILE`、`ENABLE_PL_SYNONYM` |
| [DBLINK](https://eco.dameng.com/document/dm/zh-cn/sql-dev/practice-dblink.html) | `CREATE LINK`、`表@链接`、MAL 配置、异构 Oracle/ODBC |
| [范围处理](https://eco.dameng.com/document/dm/zh-cn/sql-dev/practice-range-opeartion.html) | `LEAD`/`LAG` 连续区间（语法本身接近标准窗口函数，示例保留） |

与主流一致、本 skill 只一笔带过的内容：普通单表过滤、列别名、`CASE`、标准聚合、多数数值/字符串函数。

### 入门：表空间与对象

| 页面 | 纳入内容 |
| --- | --- |
| [创建表空间](https://eco.dameng.com/document/dm/zh-cn/start/dm-create-tablespace.html) | `CREATE TABLESPACE`、页大小与文件最小尺寸、`AUTOEXTEND`、`ENCRYPT WITH` |
| [创建数据库对象](https://eco.dameng.com/document/dm/zh-cn/start/dm-create-objects.html) | 表/视图/过程/函数/序列/触发器/代理作业的达梦示例 |

### 系统手册与 FAQ（补充独有语法）

| 页面 | 纳入内容 |
| --- | --- |
| [数据定义语句](https://eco.dameng.com/document/dm/zh-cn/pm/definition-statement.html) | `IDENTITY`/`AUTO_INCREMENT`、`STORAGE`、`CLUSTERBTR`、`USING LONG ROW`、虚拟列、HUGE 表、`CREATE USER`/`SET SCHEMA` |
| [用户标识与鉴别](https://eco.dameng.com/document/dm/zh-cn/pm/identification-authentication.html) | `SYSDBA` 等预定义用户、`CREATE USER IDENTIFIED BY` |
| [SQL 语法 FAQ](https://eco.dameng.com/document/dm/zh-cn/faq/faq-sql-gramm.html) | 自增回读、`dual`、disql `/`、兼容模式、聚集主键与 LOB、整数除法、JSON 函数、IDENTITY_INSERT |

未把 FAQ 里的运维题目（归档清理、AWR、xml 库未加载、PB 客户端等）写进生成规则。

## 未展开、需要手册原文的主题

以下内容公开页只点到名称，完整语法在安装目录手册中。生成前必须查目标版本手册，不要臆造：

- 分区表 / 间隔分区 / MPP `DISTRIBUTED BY`
- 物化视图刷新选项
- 闪回查询
- 空间数据类型、对象类型、VARRAY
- `DBMS_SCHEDULER` 风格作业的完整命令行语法
- 加密算法清单与密钥管理
- 全部系统包参数

## 示例库

图形化安装勾选 `DMHR`。Linux 可用 `$DM_HOME/samples/instance_script/dmhr/` 下对应字符集脚本。UTF-8 示例会先建表空间和用户 `dmhr` 再 `START` 各表脚本。字符集必须与客户端一致，否则中文乱码。
