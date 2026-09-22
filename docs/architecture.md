# 提供程序架构

## 范围与依赖方向

`W.EntityFrameworkCore.Dameng` 是一个独立维护的 EF Core 10 关系数据库提供程序，
直接构建于 `DM.DmProvider` ADO.NET 驱动程序之上。基础包负责数据库行为，不得引用
任何具体应用框架或应用程序集。

```text
              应用
                |
                v
W.EntityFrameworkCore.Dameng
        |                    |
        v                    v
EF Core Relational 10     DM.DmProvider
```

公共配置公开 EF 抽象和标准 `DbConnection` 重载，不公开驱动程序专用的连接类型。
达梦专用模型 API 使用 `UseDameng...` / `GetDameng...` 命名前缀，从而可在同一使用方中
与 SQL Server、Npgsql 及其他提供程序包共存。

提供程序管理现有数据库和用户模式中的对象。物理数据库的创建/删除属于管理员操作，
通过 EF 数据库创建器 API 调用时会有意抛出异常。

## EF Core 服务层

提供程序遵循
[EF Core 提供程序服务接口](https://learn.microsoft.com/ef/core/providers/writing-a-provider)。
下表描述的是当前基线，而非期望中的路线图。

| 层 | 当前职责与边界 |
| --- | --- |
| 公共配置 | 六个 `UseDameng(...)` 重载，以及用于命令超时、迁移程序集、连接所有权和有界重试的 `DamengDbContextOptionsBuilder` |
| 选项与依赖注入 | 提供程序标识、选项缓存、关系数据库服务注册 |
| 连接 | 根据 EF 连接状态创建 `DmConnection`，或使用调用方提供的 `DbConnection` |
| SQL 基础组件 | 对每个组成部分使用双引号引用标识符、`:name` 参数和达梦语句终止符 |
| 类型映射 | 常见数值/文本/二进制/时态类型、Unicode 和 LOB 边界、`DateTimeOffset`、基于 INTERVAL 的 `TimeSpan` 以及 `JsonElement` |
| 模型约定 | 标识符最大长度，以及达梦标识列/序列注解、约定和设计时注解生成 |
| 查询 | 分页、布尔搜索条件转换、部分字符串/`DateTime`/GUID 翻译器及关系数据库查询管线。`DateTimeOffset` 仅验证列比较；JSON 查询运算符和带 `StringComparison` 的字符串方法无法翻译 |
| 更新 | 单命令修改批次、标识列/序列值回读、已验证的受影响行协议、并发、`ExecuteUpdate` 和 `ExecuteDelete` |
| 迁移 | 已测试的达梦 DDL 子集、对 DDL 禁用事务、种子数据、幂等脚本、历史记录仓储及 `DBMS_LOCK` 迁移锁 |
| 数据库生命周期 | 连接/表存在性和模式对象管理；不支持创建/删除物理数据库 |
| 设计时 | 用于迁移的提供程序/注解代码生成；尚未实现反向工程 |

EF 提供程序服务使用可能发生变化的实现 API。因此，该包将 EF Core Relational
约束为 `>= 10.0.12 && < 11.0.0`，并锁定解析后的依赖项。成功编译是必要条件，
但不能作为兼容性证据。

仓库引用 EF 关系数据库规范包只为使用测试工具。目前的规范测试项目没有继承任何
上游 `*TestBase` 测试套件，因此它只是冒烟测试切片，而非一致性测试套件。

## SQL 与模型不变量

- 标识符按组成部分使用双引号引用。模式名和对象名绝不能先拼接再添加分隔符。
- 参数使用 `:name`；提供程序 SQL 不得生成 `@name`。
- 默认使用 Unicode 字符串。中文标识符和值属于必需的回归测试用例。
- 访问序列使用 `sequence.NEXTVAL`，回读生成的键使用 `sequence.CURRVAL`。
- 由于达梦不能原样接受所有 EF 关系数据库布尔结构，因此必须显式转换布尔标量值和
  SQL 搜索条件。
- 默认模式不是 `dbo`。除非模型配置了其他模式，否则未限定的对象会解析到已连接用户的模式中。
- 拒绝有损翻译。例如，不得将不支持的小数 `DateTime.Add*` 参数截断为整数。
- 不支持的迁移/模型结构应明确失败，而不是生成从其他数据库复制的 SQL。
- 无界文本/二进制值映射为 `NCLOB`/`BLOB`；当达梦要求值可比较或可索引时，必须使用
  有界行内映射。声明的行内长度并不保证所有页面大小/行存储配置都能存储或索引该长度。

## 事务与迁移

达梦 DDL 会隐式提交。因此，生成的 DDL 迁移命令会禁用 EF 事务，且包含 DDL 的整个
迁移不具备原子性。清理测试会显式删除精确对象，绝不依赖回滚。

DML 事务回滚和保存点回滚已在参考服务器上验证。尽管当前驱动程序的基础能力标志不支持，
保存点仍可在该服务器上工作。隔离级别方面，已验证 `ReadCommitted` 可用于跟踪式
`SaveChanges`；`Serializable` 可用于查询和 `ExecuteUpdate`，但不能用于跟踪式
`SaveChanges`。`RepeatableRead` 和 `Snapshot` 会在开始事务时被拒绝。

迁移历史记录及 `DBMS_LOCK` 的获取/释放均有真实服务器测试。该锁是提供程序级的保守基线，
要求服务器模式提供 `DBMS_LOCK`；这不构成对达梦 MPP 的保证。历史表存在性查询 `SYS.SYSOBJECTS`。
在 2026-09-22 的实例上，执行 `Database.Migrate()` 的账户需要 `SOI`；只执行生成脚本时 `RESOURCE` 足够。

仅声明支持[兼容性矩阵](compatibility.md)中列出的迁移操作。`GenerateCreateScript()`、
非幂等 `IMigrator.GenerateScript()` 和幂等脚本都已在该实例上执行。幂等脚本在迁移历史记录守卫中
使用已转义的动态 SQL，并以 DIsql `/` 终止块；执行前要去掉 `/`，并把每个 `BEGIN ... END;` 作为一条命令。
它们不会使达梦 DDL 具备事务性。给人看的 `dotnet ef` 步骤见 [迁移操作说明](migrations.md)；代理执行细节见 [迁移执行 skill](../skills/dameng-ef-migrations/SKILL.md)。
反向工程仍不在当前基线范围内。

## ADO.NET 边界

`DM.DmProvider` 当前提供 `net9.0` 资产，可在已测试的 .NET 10 进程中运行。
这是观察到的兼容性，而非原生 .NET 10 驱动程序保证。

驱动程序未公开提供程序专用的 `DbBatch`，因此修改命令使用 EF 单命令批次。
在已测试资产中，其异步 ADO.NET 方法会回退到同步实现；即使 EF 异步 API 仍可调用，
非阻塞 I/O 和取消能力也会受到限制。

`CommandTimeout(...)` 以秒为单位配置 `DbCommand.CommandTimeout`。本项目不对
连接字符串超时关键字或其单位作任何断言，因为该行为尚未针对锁定的驱动程序进行验证；
使用方必须遵循对应驱动程序版本的文档。

## 洁净室与许可证边界

这个采用 MIT 许可证的提供程序可以使用：

- 公开的 EF Core 契约和采用 MIT 许可证的 EF Core 源代码；
- 公开的达梦 SQL 和 ADO.NET 文档；
- 从已安装包和测试数据库中获得的黑盒 SQL/运行时观察结果；
- 在许可证允许时，将第三方提供程序用作公开功能检查清单。

它不得复制反编译所得的达梦官方 EF 提供程序源代码、许可证不兼容的提供程序代码，
或其他 ORM 的供应商二进制文件。`DM.DmProvider` 仍是独立的 Apache-2.0 NuGet
依赖项，不会被嵌入。本项目不声称获得达梦官方支持。

相关资料：

- [EF Core：编写数据库提供程序](https://learn.microsoft.com/ef/core/providers/writing-a-provider)
- [EF Core 10 关系数据库包](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Relational/10.0.12)
- [`DM.DmProvider` 包](https://www.nuget.org/packages/DM.DmProvider)
- [达梦 .NET 编程指南](https://eco.dameng.com/document/dm/zh-cn/pm/net-rogramming-guide.html)
