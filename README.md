# W.EntityFrameworkCore.Dameng

这是一个独立维护的非官方达梦数据库 Entity Framework Core 10 关系数据库提供程序。
默认使用官方 `DM.DmProvider` ADO.NET 驱动程序，不依赖任何具体应用框架。可选的扩展包
`W.EntityFrameworkCore.Dameng.WDriver`（预览版）让同一提供程序运行在
[`W.DamengProvider`](https://github.com/whynpc9/W.DamengProvider) 驱动上，见下文“使用 W.DamengProvider 驱动”。

当前基线面向：

- .NET 10 和 Entity Framework Core 10.0.x；
- 达梦 DM8；已验证实例与证据边界见兼容性矩阵，不声明最低服务器版本；
- `DM.DmProvider` 8.3.1.47463，使用该包随附的 `net9.0` 资产；
- 现有数据库和用户模式。本提供程序有意不负责创建或删除物理数据库。

基础包 `0.5.0` 与 W 扩展包 `0.5.0-preview.1` 均已公开发布，公共包、签名与独立消费者已验证，详见
[0.5.0 发布记录](docs/release-0.5.0.md)。W 扩展包精确依赖 `W.DamengProvider 0.3.1`。
本轮官方锁释放测试曾间歇失败，W 等待取消用例受测试观测权限限制；完整结果与发行边界
保留在发布记录中，不声明本轮全量实库通过。上一版的公开发布证据见
[0.4.0 发布记录](docs/release-0.4.0.md)。

真实数据库回归覆盖常规查询和 CRUD、键生成与回读、乐观并发、事务、迁移、Unicode/CJK、
反向工程及[兼容性矩阵](docs/compatibility.md)列出的扩展映射。各能力的驱动、服务器和证据边界
以该矩阵为准。

本提供程序不声明完整的 EF Core 兼容性：

- 反向工程只覆盖当前模式中可完整保留的对象和分面；跨模式对象、视图注释及无法保真的结构
  不在支持范围内，详见[反向工程](docs/reverse-engineering.md)；
- 规范测试项目是使用 EF 测试工具构建的四项自有冒烟测试切片；它不继承上游 EF
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

### 使用 W.DamengProvider 驱动（预览版）

扩展包 `W.EntityFrameworkCore.Dameng.WDriver 0.5.0-preview.1` 精确依赖基础包
`W.EntityFrameworkCore.Dameng 0.5.0` 和 `W.DamengProvider 0.3.1`。上一版
`0.4.0-preview.1` 依赖 W `0.2.0`。改用 `UseDamengWithWDriver`：

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseDamengWithWDriver(
        "Server=...;Port=5236;User=...;Password=...;transport_security=RequireTls",
        dameng => dameng.CommandTimeout(30)));
```

- 连接字符串使用 W 自己的键；W 默认要求 TLS，明文服务器必须显式写 `transport_security=PlaintextAllowed`。
- SQL、模型、迁移与反向工程与官方驱动共用；`Database.ProviderName` 为
  `W.EntityFrameworkCore.Dameng.WDriver`，`dotnet ef dbcontext scaffold` 使用该名称并生成 `UseDamengWithWDriver`。
- `EnableRetryOnFailure` 在 W 下抛 `NotSupportedException`：W 会报告命令或提交结果未知，
  不提供自动重放；需要时在独立连接上核对最终状态后再由业务决定。
- 每个 `DbContextOptions` 只能选择一种驱动；同一进程可以同时有官方与 W 两类上下文。
- 与官方驱动的行为差异（真正的异步网络调用与取消、七位小数秒、RU/Serializable 跟踪保存、
  错误后的会话保留、事务内只放行纯 DML 匿名块、传输失败的 `FailureInfo` 等）及验证范围见[兼容性矩阵](docs/compatibility.md#wdamengprovider-驱动预览版)。

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
- 两种驱动下的 EF 修改命令都有意使用单命令批次；不要据此推断底层驱动具备减少往返的
  提供程序专用批处理能力。
- 官方驱动在已测试资产中的异步 ADO.NET 方法回退到同步实现，因此 EF 异步 API
  并不意味着非阻塞网络 I/O 或及时取消。W 驱动的打开/执行/读取/事务异步路径已在实库验证为非阻塞，
  发送后取消会关闭会话；LOB 同步 getter 仍可能发起同步 I/O。
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

- [文档索引](docs/README.md)
- [兼容性与验证](docs/compatibility.md)
- [用 dotnet ef 执行达梦迁移](docs/migrations.md)
- [反向工程](docs/reverse-engineering.md)
- [查询函数与边界](docs/query-functions.md)
- [提供程序架构](docs/architecture.md)
- [本地达梦测试环境](scripts/local-test/README.md)
- [第三方声明](THIRD-PARTY-NOTICES.md)
- [达梦 SQL Skill](skills/dameng-sql/SKILL.md)
