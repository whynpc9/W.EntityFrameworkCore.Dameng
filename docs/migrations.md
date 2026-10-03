# 用 `dotnet ef` 执行达梦迁移

面向应用开发者和执行变更的 DBA。代理执行细节在
[迁移执行 skill](../skills/dameng-ef-migrations/SKILL.md)。能力边界以
[兼容性矩阵](compatibility.md) 为准。

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
