# W.EntityFrameworkCore.Dameng

这是一个独立维护的非官方达梦数据库 Entity Framework Core 10 关系数据库提供程序。
它使用官方 `DM.DmProvider` ADO.NET 驱动程序，不依赖任何具体应用框架。

当前预览版基线面向：

- .NET 10 和 Entity Framework Core 10.0.x；
- 达梦 DM8；自动化验证使用的参考服务器版本为 8.1.5.60，这并非声明的最低服务器版本；
- `DM.DmProvider` 8.3.1.47463，使用该包随附的 `net9.0` 资产；
- 现有数据库和用户模式。本提供程序有意不负责创建或删除物理数据库。

真实数据库回归测试覆盖常规查询和 CRUD、标识列和序列生成的键、乐观并发、
事务、保存点、已验证的隔离级别边界、`ExecuteUpdate` / `ExecuteDelete`、常见业务数据结构、
迁移基础操作、迁移历史记录/锁、Unicode/CJK，以及
[兼容性矩阵](docs/compatibility.md)中列出的扩展映射。
参考服务器上的最终验证通过了全部 44 项提供程序功能测试和全部 4 项提供程序自有的
关系数据库冒烟测试。

这并不是一个完整的 EF Core 提供程序：

- 尚未实现反向工程（`dotnet ef dbcontext scaffold`）；
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

真实数据库测试只读取一个机密环境变量：

```bash
export DAMENG_TEST_CONNECTION_STRING='<完整的 DM.DmProvider 连接字符串>'
dotnet test test/W.EntityFrameworkCore.Dameng.FunctionalTests/W.EntityFrameworkCore.Dameng.FunctionalTests.csproj --no-build
dotnet test test/W.EntityFrameworkCore.Dameng.Specification.Tests/W.EntityFrameworkCore.Dameng.Specification.Tests.csproj --no-build
```

缺少该变量时，数据库测试会被明确跳过。跳过的测试不能作为真实数据库或发布证据。
绝不能提交或打印连接字符串。

## 重要运行时边界

- 达梦 DDL 会隐式提交。即使 EF 已开启事务，包含 DDL 的迁移也不具备原子性。
- EF 幂等脚本使用达梦 `EXECUTE IMMEDIATE` 和迁移历史记录守卫。它们属于
  DIsql 风格脚本，其中 DMSQL 块以 `/` 结尾；包含客户端 `/` 批次分隔符的自定义
  `SqlOperation` 文本会被拒绝，转义后动态命令字面量的 UTF-8 表示超过
  32767 字节时也会被拒绝。
- 无界 `string` 和 `byte[]` 属性分别映射为 `NCLOB` 和 `BLOB`。
  键、索引、排序/分组以及其他需要普通可比较行内值的操作必须配置有限最大长度。
  实际可用的行内值和索引长度还取决于数据库页面及行存储配置。
- 当前驱动程序没有提供程序专用的 `DbBatch`；EF 修改命令会有意以单命令批次执行。
- 在已测试的资产中，驱动程序的异步 ADO.NET 方法会回退到同步实现，因此 EF 异步
  API 并不意味着非阻塞网络 I/O 或及时取消。
- `CommandTimeout(...)` 以秒为单位配置 `DbCommand.CommandTimeout`。
  本项目不对驱动程序连接字符串的超时关键字或单位作任何断言；连接建立相关配置只能
  依据已安装驱动程序对应版本的文档。

另请参阅：

- [兼容性与验证](docs/compatibility.md)
- [提供程序架构](docs/architecture.md)
- [第三方声明](THIRD-PARTY-NOTICES.md)
