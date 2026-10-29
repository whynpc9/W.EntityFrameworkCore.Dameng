# 提供程序架构

## 范围与依赖方向

`W.EntityFrameworkCore.Dameng` 是一个独立维护的 EF Core 10 关系数据库提供程序，
直接构建于 `DM.DmProvider` ADO.NET 驱动程序之上。基础包负责数据库行为，不得引用
任何具体应用框架或应用程序集。

```text
              应用
           |          \
           |           v
           |    W.EntityFrameworkCore.Dameng.WDriver ──> W.DamengProvider
           v           |
W.EntityFrameworkCore.Dameng  (共享 SQL/模型/迁移/反向工程 + 官方驱动适配)
        |                    |
        v                    v
EF Core Relational 10     DM.DmProvider
```

驱动通过 `W.EntityFrameworkCore.Dameng.Infrastructure.DamengDriver` 桥接：它同时是驱动身份
（`Name`、EF `ProviderName`、脚手架 `Use...` 方法）和无状态适配器（创建/识别连接、设置
BLOB/CLOB/BINARY/VARBINARY/INTERVAL 参数类型、把时间映射的小数秒位数交给参数、选择时间读取方式、
是否允许内建重试）。`DamengDriverConfiguration` 以指定驱动配置 options 并注册运行时/设计时服务；
其余实现保持 internal，扩展包不使用 `InternalsVisibleTo`。

- `DamengOptionsExtension.Driver` 进入服务提供程序缓存键、LogFragment 与 DebugInfo；两种驱动不会共用
  类型映射、连接或异常服务。
- 类型映射源保留与驱动无关的模板，返回的每个映射都绑定到本服务提供程序的驱动，Clone 保留驱动；
  驱动收到其他驱动的参数时抛异常，因此跨驱动复用模型会显式失败。
- `DamengDatabaseProvider` 按驱动报告 `ProviderName` 并只认领选择该驱动的 options；
  `ISingletonOptions` 在首次初始化和复用时拒绝混合注册或与 options 不符的服务。

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
| 公共配置 | 官方与 W 各有六个 `UseDameng...` 重载，以及用于命令超时、迁移程序集、连接所有权和有界重试的 `DamengDbContextOptionsBuilder`；W 明确拒绝内建重试 |
| 选项与依赖注入 | 提供程序标识、选项缓存、关系数据库服务注册 |
| 连接 | 经 `DamengDriver` 创建或识别对应驱动的 `DbConnection`，并使用 EF 连接状态管理生命周期 |
| SQL 基础组件 | 对每个组成部分使用双引号引用标识符、`:name` 参数和达梦语句终止符 |
| 类型映射 | 常见数值/文本/二进制/时态类型、Unicode 和 LOB 边界、`DateTimeOffset`、基于 INTERVAL 的 `TimeSpan` 以及 `JsonElement` |
| 模型约定 | 标识符最大长度，以及达梦标识列/序列注解、约定和设计时注解生成 |
| 查询 | 分页、布尔搜索条件转换、字符串/数值/DateTime/GUID 翻译器、DateTimeOffset 六个本地日期部件和显式日期分桶函数。字符串分组聚合有专用 LISTAGG 表达式、SQL 生成及空值访问器。JSON 查询运算符和 StringComparison 字符串方法仍不翻译；精确范围见兼容性矩阵 |
| 更新 | 单命令修改批次、标识列/序列值回读、已验证的受影响行协议、并发、`ExecuteUpdate` 和 `ExecuteDelete` |
| 迁移 | 已测试的达梦 DDL 子集、对 DDL 禁用事务、种子数据、幂等脚本、历史记录仓储及 `DBMS_LOCK` 迁移锁 |
| 数据库生命周期 | 连接/表存在性和模式对象管理；不支持创建/删除物理数据库 |
| 设计时 | 用于迁移的提供程序/注解代码生成；当前模式的[反向工程](reverse-engineering.md)及 dotnet ef 命令行端到端 |

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
- 文本 LOB 的 COALESCE 回退值在函数内部保留 LOB 类型，不能等发生有界文本转换后再对结果补 CAST。
- LISTAGG 自定义 SQL 节点必须让参数空值处理、普通表达式访问器及 SQL 生成器访问全部输入与排序表达式；不能丢弃组内筛选/排序。
- 不支持的迁移/模型结构应明确失败，而不是生成从其他数据库复制的 SQL。
- 无界文本/二进制值映射为 `NCLOB`/`BLOB`；当达梦要求值可比较或可索引时，必须使用
  有界行内映射。声明的行内长度并不保证所有页面大小/行存储配置都能存储或索引该长度。

## 事务与迁移

达梦 DDL 会隐式提交。因此，生成的 DDL 迁移命令会禁用 EF 事务，且包含 DDL 的整个
迁移不具备原子性。清理测试会显式删除精确对象，绝不依赖回滚。

DML 事务回滚和保存点回滚已在参考服务器上验证。官方驱动虽然把基础保存点能力标志报告为不支持，
保存点仍可在该服务器上工作；W 的保存点行为按其服务器能力合同验证，不能套用官方驱动的能力标志。

两种驱动都已验证 `ReadCommitted` 下的跟踪式 `SaveChanges`，并都会在开始事务时拒绝
`RepeatableRead` 和 `Snapshot`。官方驱动可以开始 `ReadUncommitted` / `Serializable` 事务并执行查询，
`Serializable` 下的 `ExecuteUpdate` 也已验证，但这两种隔离级别下的跟踪式 `SaveChanges` 会失败。
W 仅在自报 `8.1.5.60`、UTF-8、明文的已验 profile 上验证了这两种隔离级别下的跟踪保存成功；
其他 profile 未验证，详见[兼容性矩阵](compatibility.md)。

迁移历史记录及 `DBMS_LOCK` 的获取/释放均有真实服务器测试。该锁是提供程序级的保守基线，
要求服务器模式提供 `DBMS_LOCK`；这不构成对达梦 MPP 的保证。历史表存在性查询 `SYS.SYSOBJECTS`。
在 2026-09-22 的实例上，执行 `Database.Migrate()` 的账户需要 `SOI`；只执行生成脚本时 `RESOURCE` 足够。

仅声明支持[兼容性矩阵](compatibility.md)中列出的迁移操作。`GenerateCreateScript()`、
非幂等 `IMigrator.GenerateScript()` 和幂等脚本都已在该实例上执行。幂等脚本在迁移历史记录守卫中
使用已转义的动态 SQL，并以 DIsql `/` 终止块；执行前要去掉 `/`，并把每个 `BEGIN ... END;` 作为一条命令
（块可以嵌套：历史记录守卫内的 `EnsureSchema` 守卫块是一个整体命令）。它们不会使达梦 DDL 具备事务性。
给人看的 `dotnet ef` 步骤见 [迁移操作说明](migrations.md)；代理执行细节见 [迁移执行 skill](../skills/dameng-ef-migrations/SKILL.md)。
反向工程覆盖当前模式的表、视图、列、默认值、虚拟计算列、注释、约束、索引与外键，详见[反向工程](reverse-engineering.md)。

## ADO.NET 边界

`DM.DmProvider` 当前提供 `net9.0` 资产，可在已测试的 .NET 10 进程中运行。
这是观察到的兼容性，而非原生 .NET 10 驱动程序保证。

两种驱动的修改命令都使用 EF 单命令批次，不据此声明底层驱动具有减少往返的专用批处理能力。
官方驱动在已测试资产中的异步 ADO.NET 方法会回退到同步实现，非阻塞 I/O 和取消能力受限；
W 驱动的异步路径为真实网络异步，取消/期限与失败结果由驱动的 `FailureInfo` 描述，提供程序不在其上增加重试。

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
