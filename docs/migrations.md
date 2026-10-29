# 用 `dotnet ef` 执行达梦迁移

面向应用开发者和执行变更的 DBA。代理执行细节在
[迁移执行 skill](../skills/dameng-ef-migrations/SKILL.md)。能力边界以
[兼容性矩阵](compatibility.md) 为准，脚手架的对象与目录保真范围见[反向工程](reverse-engineering.md)。

进程内的 `IMigrator.GenerateScript()`、`Database.Migrate()` 与命令行 `dotnet ef`
（`migrations add` / `migrations script` / `database update` / `dbcontext scaffold`）
都在同一套设计时服务和 SQL 生成器上完成过真实库回归。命令行回归使用仓库内
`artifacts/dotnet-ef-tool` 的版本匹配 dotnet-ef 本地工具（全局旧版工具在 Unix 上有已知构建问题）。

不要把连接字符串、主机、用户或口令写进仓库、脚本、日志或本文档。

## 先选路径

| 目的 | 命令 | 谁执行 | 账号 |
| --- | --- | --- | --- |
| 生成 C# 迁移 | `dotnet ef migrations add` | 开发者 | 不连接数据库 |
| 审查后执行 SQL | `dotnet ef migrations script` | 开发者出脚本，DBA 用 DIsql 执行 | 能在目标模式下建对象。已验证子集上 `RESOURCE` 足够 |
| 同一脚本可重复执行 | `dotnet ef migrations script --idempotent` | 同上，必须用 DIsql | 同上。脚本自己判断 `__EFMigrationsHistory` |
| 工具直接改库 | `dotnet ef database update`，或应用里的 `Database.Migrate()` | 发布流程 | `RESOURCE`，以及查询 `SYS.SYSOBJECTS` 所需的 `SOI`，并能调用 `DBMS_LOCK` |

达梦没有独立的 `CREATE DATABASE` 作为这次隔离单位。目标是已经存在的用户及其默认表空间。
提供程序不会创建或删除数据库。表空间和用户的建法见
[达梦 SQL skill](../skills/dameng-sql/SKILL.md)。不要为了跑迁移授予 `DBA`。

三条路径都不会让 DDL 变成事务。`CREATE` / `ALTER` / `DROP` 会隐式提交。
中途失败后，已经成功的语句留在库里，不能靠 `ROLLBACK` 收回。

## 准备工具和设计时入口

应用使用的 EF Core 与 `dotnet-ef` 保持同一 10.0.x 行。本仓库锁定
`Microsoft.EntityFrameworkCore.Relational` 10.0.12：

```bash
dotnet tool install --global dotnet-ef --version 10.0.12
```

启动项目引用 `W.EntityFrameworkCore.Dameng`，并引用 `Microsoft.EntityFrameworkCore.Design`
（通常设为 `PrivateAssets`）。提供程序程序集带有 `DesignTimeProviderServices`，工具会据此加载达梦设计时服务。

设计时要能构造 `DbContext`。把连接字符串放在进程环境变量或启动项目自己的机密存储里，
用 `IDesignTimeDbContextFactory<TContext>` 读出来：

```csharp
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DAMENG_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set DAMENG_CONNECTION_STRING.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseDameng(connectionString, dameng =>
            {
                dameng.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name!);
                dameng.MigrationsHistoryTable("__EFMigrationsHistory");
            })
            .Options;

        return new AppDbContext(options);
    }
}
```

`MigrationsAssembly` 指向放迁移类的程序集。上面的工厂适用于迁移类和 `DbContext` 在同一程序集。
不调用 `MigrationsHistoryTable` 时，历史表名仍是 `__EFMigrationsHistory`，建在当前用户模式下。
只面向达梦时，下面的命令把迁移项目当作 `App.Data`，启动项目当作 `App`。多种数据库并存时，
改用下一节的项目，两个参数都指向达梦迁移项目。

`migrations add` 和 `migrations script` 会构造上下文，但生成脚本本身不连接数据库。
`database update` 和 `migrations list` 会连接数据库。

## 使用 W.DamengProvider 驱动（预览版）

基础包 `0.4.0` 与 W 扩展包 `0.4.0-preview.1` 的既有公开发行见[发布记录](release-0.4.0.md)；
扩展包仍是预览版，后续源码修复和依赖升级没有另行发布包。

启动项目改为引用 `W.EntityFrameworkCore.Dameng.WDriver`，设计时工厂改用 `UseDamengWithWDriver`
（W 连接串；明文服务器需 `transport_security=PlaintextAllowed`）。其他命令不变；反向工程把提供程序参数写成扩展包名称：

```bash
dotnet ef dbcontext scaffold "Name=ConnectionStrings:Dameng" W.EntityFrameworkCore.Dameng.WDriver \
  --project App.Data --startup-project App
```

已发布的 `0.4.0-preview.1` 扩展包依赖 W `0.2.0`；当前源码使用 W `0.3.1`，但尚未发布新的扩展包。
以下行为在两种已验证依赖上保持一致：

- 两种驱动生成相同的迁移 SQL，可以对同一数据库、同一历史表交替执行；切换驱动不会产生新的模型差异。
- `migrationBuilder.ActiveProvider` 在 W 下为 `W.EntityFrameworkCore.Dameng.WDriver`。已有迁移若只比较
  `"W.EntityFrameworkCore.Dameng"`，切换后会走另一分支；执行前审阅这类迁移，显式接受两个名称。
- W 在本地事务内只放行由 DML 组成的匿名 `BEGIN … END;` 块；含 `DECLARE`、控制流、过程调用或事务控制的块
  仍被拒绝。迁移中提供程序生成的块已抑制事务；自定义 `migrationBuilder.Sql(...)` 中的这类块需写
  `suppressTransaction: true`。
- 时间列默认七位（`TIMESTAMP(7)`、`DATETIME(7) WITH TIME ZONE`）。六位→七位是保值的独立变更；七位→六位前必须先检查，
  否则第七位会丢失。守卫与 ALTER 分开、`suppressTransaction: true`，同一迁移内所有待降列先检查再 ALTER：

```csharp
migrationBuilder.Sql("""
    DECLARE v_bad BIGINT;
    BEGIN
        SELECT COUNT(*) INTO v_bad FROM "Orders"
         WHERE MOD(TO_NUMBER(TO_CHAR("CreatedAt", 'FF9')), 1000) <> 0
            OR MOD(TO_NUMBER(TO_CHAR("ShippedAt", 'FF9')), 1000) <> 0;
        IF v_bad > 0 THEN
            RAISE_APPLICATION_ERROR(-20071, 'Seven-digit fractions would be lost by narrowing to six digits.');
        END IF;
    END;
    """, suppressTransaction: true);
migrationBuilder.AlterColumn<DateTime>("CreatedAt", "Orders", type: "TIMESTAMP(6)", oldType: "TIMESTAMP(7)");
migrationBuilder.AlterColumn<DateTime>("ShippedAt", "Orders", type: "TIMESTAMP(6)", oldType: "TIMESTAMP(7)");
```

  提供程序不会自动注入该守卫。守卫拒绝后数据与列精度不变；不要截断数据来适配旧驱动或旧精度。

## 多种数据库各用一个迁移项目

同一套实体要同时面向 SQL Server、PostgreSQL 和达梦时，模型留在共享项目，每种数据库单独放迁移。
第二个 `DbContext`（例如身份库和业务库）再各做一套，不要把两个上下文的迁移混在一个项目里。

```text
App.Dal/                              DbContext 与实体
App.Dal.Migrations.SqlServer/         SQL Server 迁移
App.Dal.Migrations.PostgreSql/        PostgreSQL 迁移
App.Dal.Migrations.Dameng/            达梦迁移
App.Identity.Migrations.SqlServer/
App.Identity.Migrations.PostgreSql/
App.Identity.Migrations.Dameng/
```

达梦迁移项目引用共享模型项目和 `W.EntityFrameworkCore.Dameng`，并引用
`Microsoft.EntityFrameworkCore.Design`（`PrivateAssets`）。设计时工厂放在这个项目里，
固定 `UseDameng`，并把 `MigrationsAssembly` 设成当前程序集：

```csharp
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DAMENG_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set DAMENG_CONNECTION_STRING.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseDameng(connectionString, dameng =>
                dameng.MigrationsAssembly(typeof(AppDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new AppDbContext(options);
    }
}
```

工厂要固定本项目的提供程序。开发机默认连接即使指向另一种数据库，也不能拿来决定这次生成哪套迁移。
共享模型项目不要再实现 `IDesignTimeDbContextFactory`，否则工具可能选错提供程序。
共享模型里的值生成使用对应提供程序的 API；达梦列用 `UseDamengIdentityColumn` 或 `UseDamengSequence`。

同一次模型变更要在每个数据库的迁移项目里各生成一份。类名可以相同，时间戳由各自的
`migrations add` 决定。不要把 SQL Server 或 PostgreSQL 的 `Up` 抄进达梦项目，让达梦工厂重新生成。

在达梦迁移项目目录中执行时，`--project` 和 `--startup-project` 都是该项目：

```bash
cd src/App.Dal.Migrations.Dameng
dotnet ef migrations add InitialCreate --context AppDbContext --output-dir Migrations
dotnet ef migrations script --idempotent --output artifacts/migrate.sql --context AppDbContext
dotnet ef database update --context AppDbContext
```

运行中的应用按当前数据库选择迁移程序集。达梦部署只指向达梦项目，例如
`dameng.MigrationsAssembly("App.Dal.Migrations.Dameng")`。SQL Server 或 PostgreSQL 的迁移程序集不要装进达梦进程。
若用独立进程调用 `Database.Migrate()`，可以把达梦迁移项目做成可执行程序，入口使用上面的工厂。
账号、锁和 DDL 隐式提交与下文的 `database update` 相同。

达梦脚本的审查和 DIsql 执行仍按下文。其他数据库的脚本留在各自的提供程序上执行。

## 生成迁移类

模型先限制在[已支持的迁移子集](compatibility.md)。筛选索引、存储计算列、增加或恢复标识列属性、修改标识列种子/增量和跨模式重命名会在生成 SQL 时抛出 `NotSupportedException`。

```bash
dotnet ef migrations add InitialCreate \
  --context AppDbContext \
  --output-dir Migrations \
  --project src/App.Data/App.Data.csproj \
  --startup-project src/App/App.csproj
```

提交前看生成的 `Up`。`Down` 能否生成取决于操作是否落在支持子集内；即便能生成，
已经执行的达梦 DDL 也不会随事务回滚。

### 移除 IDENTITY

已有自增主键改由应用赋值时，将属性配置改为 `ValueGeneratedNever()`，并移除原来的
`UseDamengIdentityColumn(...)` 配置。审查模型差异中的 `AlterColumn`：只有自增属性
发生变化时，提供程序生成表级命令：

```sql
ALTER TABLE "APP"."Orders" DROP IDENTITY;
```

此命令保留列和已有数据，不附加 `MODIFY`，并禁止 EF 为其包装事务。它不带列名，
模式名和表名分别引用。同一 `AlterColumn` 若还改变类型、可空性、默认值、计算列、
排序规则或其他列注解，提供程序会提前拒绝；切换为序列也不在这项支持范围内。
应将后续变更单独设计并验证，不要通过修改旧模型信息绕过检查。

移除后应用必须按新模型提供主键。自动生成的 `Down` 通常需要恢复 IDENTITY，
该方向仍不受支持，生成 SQL 时会明确失败。上线前应另行审查恢复方案；不能依赖
事务回滚撤销已经执行的 `DROP IDENTITY`，也不要把删列重建当作无损恢复。

## 导出脚本

DBA 审查变更时，先出脚本，再在 DIsql 里执行。

从空库到最新迁移：

```bash
dotnet ef migrations script \
  --output artifacts/migrate.sql \
  --context AppDbContext \
  --project src/App.Data/App.Data.csproj \
  --startup-project src/App/App.csproj
```

只导出一段。`From` 是已经应用、这次不再执行的迁移；`To` 是这次要到达的迁移：

```bash
dotnet ef migrations script 202609220001_InitialCreate 202609220002_AddStatus \
  --output artifacts/upgrade.sql \
  --context AppDbContext \
  --project src/App.Data/App.Data.csproj \
  --startup-project src/App/App.csproj
```

可重复执行的脚本加上 `--idempotent`：

```bash
dotnet ef migrations script \
  --idempotent \
  --output artifacts/migrate.sql \
  --context AppDbContext \
  --project src/App.Data/App.Data.csproj \
  --startup-project src/App/App.csproj
```

非幂等脚本是以分号结束的达梦 DDL，并包含历史表插入。幂等脚本把普通 SQL 按引号/注释
之外的分号拆成独立的 `EXECUTE IMMEDIATE`，置于历史表条件中，块以单独一行的 `/` 结束。
这样同一 `migrationBuilder.Sql` 中先建表、再写入时，后续语句在表创建后才绑定。
匿名 `BEGIN`/`DECLARE` 块应单独放入一个 `SqlOperation`，保持完整并原样内嵌；
生成器会核对外层 `END;` 边界，其后追加的 SQL 必须移到另一个操作。
普通 SQL 后混入匿名块，以及 `CREATE [OR REPLACE] PROCEDURE/FUNCTION/TRIGGER/PACKAGE/TYPE`
存储定义会在幂等生成时明确拒绝拆分，存储定义应另行整体执行。
转义后的单条动态语句字面量按 UTF-8 超过 32767 字节时，生成会失败，需要把 migration 拆小。
自定义 `SqlOperation` 里不能出现单独一行的 `/`。

反向工程的字节长度列显式保留 `CHAR/VARCHAR/VARCHAR2(n BYTE)`。提供程序生成的
建表、加列或修改列 DDL 会先检查 `SF_GET_LENGTH_IN_CHAR()`，仅在 0 时执行；
其他或未知模式明确报错，避免把原字节容量按字符解释。该守卫不修改实例配置；
当前真实验证环境为模式 0，未将模拟拒绝分支当作模式 1 实例验证。

审查时对照[兼容性矩阵](compatibility.md)里的迁移行。第一次取 `NEXTVAL` 之前修改序列增量，
达梦可能不从原来的起点继续计。

## 在 DIsql 里执行

登录目标用户后执行脚本。达梦文档里的写法是 `START`、`@` 或反引号路径，例如：

```text
SQL> START /path/migrate.sql
```

说明见[如何在 DIsql 中使用脚本](https://eco.dameng.com/document/dm/zh-cn/pm/disql-script.html)。
该文档还说明：DIsql 跑完脚本后会再执行一次提交。

幂等脚本必须交给 DIsql。`/` 是客户端批次结束符，不是服务器 SQL。
不要把整个文件交给 ADO.NET、`ExecuteSqlRaw` 或其他按分号拆批的执行器。
应用若要执行同一段幂等 SQL，先去掉单独一行的 `/`，再把每个 `BEGIN ... END;` 作为一条命令发送；
块可以嵌套（历史记录守卫内的 `EnsureSchema` 守卫块）。解析时忽略引号/注释中的关键字，按词法 token 配对，保留 `DECLARE` 声明和前导注释；同一行可以有多个关键字。区分 `END IF`、`END LOOP`、`CASE ... END` 与块结束，不要在内层 `END;` 处提前截断。

本仓库测试执行器支持匿名块的变量声明、局部过程/函数声明及其嵌套体，但不是完整 DMSQL 脚本解析器。`CREATE PROCEDURE/FUNCTION/TRIGGER/PACKAGE` 定义明确拒绝拆批；这类定义应作为完整命令交给相应执行器，不按内部的分号拆开。

脚本正文里如果出现 `&`，DIsql 会把它当成替换变量。执行前在会话里运行 `SET DEFINE OFF`。

执行后查询 `__EFMigrationsHistory`（或工厂里配置的历史表），确认 `MigrationId` 与这次的 `To` 一致。
失败时按对象名核对已经提交的表、序列和历史行，再决定补跑或手工清理。

## 直接更新数据库

不经过脚本审查、由工具连库执行时：

```bash
dotnet ef database update \
  --context AppDbContext \
  --project src/App.Data/App.Data.csproj \
  --startup-project src/App/App.csproj
```

停在指定迁移：

```bash
dotnet ef database update 202609220002_AddStatus \
  --context AppDbContext \
  --project src/App.Data/App.Data.csproj \
  --startup-project src/App/App.csproj
```

这和进程内的 `context.Database.Migrate()` 是同一条应用路径。
2026-09-22 的实例上，历史表存在性查询 `SYS.SYSOBJECTS`；只有 `RESOURCE` 时该查询会失败，
需要再授予 `SOI`。更新期间提供程序用 `DBMS_LOCK` 防止并发迁移。
目标用户和表空间要事先存在。DDL 仍然逐条提交，锁释放不能把已执行的 DDL 撤回去。

查看本地迁移和库上的应用情况：

```bash
dotnet ef migrations list \
  --context AppDbContext \
  --project src/App.Data/App.Data.csproj \
  --startup-project src/App/App.csproj
```

## 仅修改列注释

真实模型差异中的列结构、默认值、生成策略和全部语义注解保持不变时，注释新增、修改和删除只生成抑制事务的`COMMENT ON COLUMN`；删除使用`IS ''`。纯注释不会重发BYTE列定义或插入LENGTH_IN_CHAR守卫。IDENTITY列的注释变更保持数据和后续自增值，普通及幂等脚本均覆盖。其他列分面或未知注解变化仍走原有结构DDL/明确拒绝路径；不扩大IDENTITY结构变更支持。实际双驱动范围见[015证据](../plans/evidence/015.md)。

## 历史表的引用名称与命令边界

自定义历史表模式/名称按组成部分引用，含`CREATE TABLE`文本、引号、点号或中文的名称保留原样。存在性脚本只在独立建表命令的TABLE token后插入`IF NOT EXISTS`，不会修改模式守卫的字符串、注释或引用内容；无法唯一识别时明确拒绝。实际`CreateIfNotExists[Async]`保留模式匿名块与建表命令边界，逐条抑制事务。重复建表不会删除历史行；真实名称及双驱动边界见[016证据](../plans/evidence/016.md)。

## 固定迁移锁与当前取消边界

迁移锁使用固定ID20260723；不同DbContext、历史表或模式仍相互串行，数据库测试必须独占迁移测试时段。当前实例已验证DDL隐式提交前后继续排斥、显式/异常作用域/实际关闭释放后可重取。官方驱动维持同步回退、取消有限，预取消的异常token身份不保证与调用者相同。W预取消有原token及发送边界证据；本轮等待中取消被普通账号缺`SYS.V$DBMS_LOCKS`查询权限阻断，不能以请求在途替代服务器等待队列证据。锁的两驱动整体验收与M5保持BLOCKED，详见[017证据](../plans/evidence/017.md)。

2026-10-29补充：上述“本轮等待中取消被权限阻断”为首次执行历史；维护者授权创建具备精确视图SELECT的普通隔离测试账号后，017官方7/W8及两驱动迁移交叉均通过，已获得真实服务器等待和后续取消/持锁/重取证据。该目录SELECT只为测试观察所需，不是产品迁移执行所需的额外权限。临时测试账号和表空间已精确清理，最新范围见M5证据，不外推MPP或官方运行中取消。

## LOB 表主键与实例聚集缺省

当前源码中，未指定 `Dameng:IsClustered` 的主键仍生成 `PRIMARY KEY`；显式 true/false
分别保留 `CLUSTER PRIMARY KEY` / `NOT CLUSTER PRIMARY KEY`，不会在生成 SQL 时查询实例参数。
019 在 DM 自报8.1.5.60的两个明文profile上完成双驱动实证：PK_WITH_CLUSTER=0、32768字节页，
以及独立PK_WITH_CLUSTER=1、8192字节页。无界string/byte[]、显式CLOB/NCLOB/BLOB的原生成SQL
均成功，实际主键后备索引为NORMAL；即使显式CLUSTER，服务器也保留NORMAL。有界非LOB对照
在PK=1下实际建立CLUSTER索引。该结论来自服务器处理，不是本提供程序注入非聚集注解。

完整模型、模型差异迁移、手工CreateTable、共享表及有模型AddPrimaryKey均有实际索引与
NULL/非空/更新/重复键对照。model-less AddPrimaryKey缺少目标列类型时保持原有行为，
使用方应明确聚集选择并核对目标目录。其他版本、LOB别名、JSON/BFILE、已有表新增LOB或
聚集布局改造未由本次矩阵穷尽；不会自动重建已有表，也不将本结论外推为所有部署都安全。
源SQL、当前profile与TRX见[019证据](../plans/evidence/019.md)，本批没有发布新包。
