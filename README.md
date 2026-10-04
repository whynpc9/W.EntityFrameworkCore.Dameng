# W.EntityFrameworkCore.Dameng

这是一个独立维护的非官方达梦数据库 Entity Framework Core 10 关系数据库提供程序。
它使用官方 `DM.DmProvider` ADO.NET 驱动程序，不依赖任何具体应用框架。

当前预览版基线面向：

- .NET 10 和 Entity Framework Core 10.0.x；
- 达梦 DM8；各轮实例版本记录见兼容性矩阵，不声明最低服务器版本；
- `DM.DmProvider` 8.3.1.47463，使用该包随附的 `net9.0` 资产；
- 现有数据库和用户模式。本提供程序有意不负责创建或删除物理数据库。

真实数据库回归测试覆盖常规查询和 CRUD、标识列和序列生成的键、乐观并发、
事务、保存点、已验证的隔离级别边界、`ExecuteUpdate` / `ExecuteDelete`、常见业务数据结构、
迁移脚本的生成与执行、迁移历史记录/锁、Unicode/CJK，以及
[兼容性矩阵](docs/compatibility.md)中列出的扩展映射。
2026-09-17 的参考记录是 44 项功能测试，服务器版本记为 8.1.5.60。2026-09-22 在自报为
`DM Database Server 64 V8`（`DB Version: 0x7000d`，构建号 `03134284604-20260707-335949-20228`）
的实例上通过了全部 48 项功能测试和全部 4 项冒烟测试。这次实例没有返回 8.1.5.60。

2026-09-29 的查询扩展工作树经 PR 审查修订后，在独立测试空间完成 261 项单元、72 项普通功能、
4 项规范冒烟和 4 项管理员迁移脚本测试，全部通过且无跳过。17 项候选能力探针的前轮结果单独记录。
交付范围与延期项目见[实施台账](docs/query-translation-execution.md)，这不是新版本发布声明。

2026-10-03 合并的设计时工作（PR #3，随 0.3.0 发布）补充了当前模式反向工程、注释迁移、
模式存在性守卫与非 Unicode 字符长度语义。2026-10-04 的发布候选验收完成 682 项单元、
232 项普通功能（含 `dotnet ef` 命令行端到端）、4 项规范冒烟和 7 项管理员迁移脚本测试，
全部通过且无跳过。实例参数与证据路径见[兼容性矩阵](docs/compatibility.md)和
[0.3.0 发布准备](docs/release-0.3.0.md)。

这并不是一个完整的 EF Core 提供程序：

- 反向工程（`dotnet ef dbcontext scaffold`）覆盖当前模式的表、视图、列、默认值、注释、
  主键、唯一约束、索引和外键；跨模式对象与视图注释不在范围内，精确边界见
  [兼容性矩阵](docs/compatibility.md)；
- 规范测试项目是使用 EF 测试工具构建的小型自有冒烟测试切片；它不继承上游 EF
  关系数据库测试套件，也不构成一致性声明。

## 使用方式

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseDameng(
        connectionString,
        dameng =>
        {
            dameng.CommandTimeout(30);
            dameng.MigrationsAssembly("App.EntityFrameworkCore.Dameng");
            dameng.EnableRetryOnFailure();
        }));
```

对于连接字符串和现有 `DbConnection` 实例，提供程序均提供泛型和非泛型重载。
不含所有权参数的连接重载由调用方负责释放连接；当 EF 应拥有连接时，可使用显式的
`contextOwnsConnection` 重载。

达梦专用的值生成使用带提供程序前缀的 API，因此模型可以同时引用本包与其他 EF
提供程序，而不会产生扩展方法歧义：

```csharp
modelBuilder.Entity<Order>()
    .Property(order => order.Id)
    .UseDamengIdentityColumn(seed: 1, increment: 1);

modelBuilder.Entity<AuditEvent>()
    .Property(item => item.Id)
    .UseDamengSequence("AuditEventIds");
```

## 构建与测试

```bash
dotnet restore W.EntityFrameworkCore.Dameng.slnx --locked-mode --disable-parallel
dotnet build W.EntityFrameworkCore.Dameng.slnx --no-restore
dotnet test test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj --no-build
```

本地真实数据库回归统一使用已配置的持久测试空间。入口从 Git 忽略的
`.local-test.secrets.json` 加载连接，并只通过子进程环境传给测试：

```bash
scripts/local-test/run.sh test all
```

`all` 包含单元、普通功能、规范冒烟和管理员迁移脚本测试；候选 SQL 语义探针单独用
`scripts/local-test/run.sh test probes` 执行，不计入功能通过率。详见
[本地测试入口](scripts/local-test/README.md)。普通测试可通过已有的
`DAMENG_TEST_CONNECTION_STRING` 覆盖测试连接；缺配置时入口会失败。
直接运行测试项目且缺环境变量时仍会跳过，跳过不能作为真实数据库或发布证据。
绝不能提交或打印连接字符串。

## 重要运行时边界

- 达梦 DDL 会隐式提交。即使 EF 已开启事务，包含 DDL 的迁移也不具备原子性。
- EF 幂等脚本使用达梦 `EXECUTE IMMEDIATE` 和迁移历史记录守卫。它们属于
  DIsql 风格脚本，其中 DMSQL 块以 `/` 结尾。应用执行时要去掉这一行，并把每个
  `BEGIN ... END;` 作为一条命令；包含客户端 `/` 批次分隔符的自定义
  `SqlOperation` 文本会被拒绝，转义后动态命令字面量的 UTF-8 表示超过
  32767 字节时也会被拒绝。给人看的 `dotnet ef` 步骤见
  [迁移操作说明](docs/migrations.md)；代理执行细节见
  [迁移执行 skill](skills/dameng-ef-migrations/SKILL.md)。
- 无界 `string` 和 `byte[]` 属性分别映射为 `NCLOB` 和 `BLOB`。
  键、索引、排序/分组以及其他需要普通可比较行内值的操作必须配置有限最大长度。
  实际可用的行内值和索引长度还取决于数据库页面及行存储配置。
- 当前驱动程序没有提供程序专用的 `DbBatch`；EF 修改命令会有意以单命令批次执行。
- 在已测试的资产中，驱动程序的异步 ADO.NET 方法会回退到同步实现，因此 EF 异步
  API 并不意味着非阻塞网络 I/O 或及时取消。
- `CommandTimeout(...)` 以秒为单位配置 `DbCommand.CommandTimeout`。
  本项目不对驱动程序连接字符串的超时关键字或单位作任何断言；连接建立相关配置只能
  依据已安装驱动程序对应版本的文档。

## 达梦 SQL Skill

仓库在 [`skills/dameng-sql`](skills/dameng-sql/SKILL.md) 提供可安装的 Agent Skill，按达梦公开 SQL 文档整理方言差异：与 Oracle / MySQL / SQL Server 一致的部分只作索引，表空间、`IDENTITY` / 序列、DMSQL、页大小、聚集主键、`compatible_mode` 等达梦特有写法带有示例。

用 [`npx skills`](https://skills.sh/docs/cli) 安装（CLI 会扫描仓库里的 `SKILL.md`）：

```bash
npx skills add whynpc9/dameng-entityframework-core
npx skills add whynpc9/dameng-entityframework-core --skill dameng-sql
npx skills add whynpc9/dameng-entityframework-core --skill dameng-ef-migrations
npx skills add whynpc9/dameng-entityframework-core@dameng-sql
```

在已经 clone 的仓库里本地安装：

```bash
npx skills add . --skill dameng-sql
npx skills add ./skills --skill dameng-sql
```

列出该源中的 skill、跳过交互提示或装到用户全局目录：

```bash
npx skills add whynpc9/dameng-entityframework-core --list
npx skills add whynpc9/dameng-entityframework-core --skill dameng-sql -y
npx skills add -g whynpc9/dameng-entityframework-core --skill dameng-sql -y
```

该 skill 描述的是达梦 SQL 方言，不代表本提供程序已实现其中每一项能力。提供程序范围仍以 [兼容性矩阵](docs/compatibility.md) 为准。

另请参阅：

- [用 dotnet ef 执行达梦迁移](docs/migrations.md)
- [兼容性与验证](docs/compatibility.md)
- [查询翻译后续扩展计划](docs/query-translation-extension-plan.md)
- [查询函数与边界](docs/query-functions.md)
- [查询翻译实施台账](docs/query-translation-execution.md)
- [本地达梦测试环境](scripts/local-test/README.md)
- [提供程序架构](docs/architecture.md)
- [第三方声明](THIRD-PARTY-NOTICES.md)
- [达梦 SQL Skill](skills/dameng-sql/SKILL.md)
