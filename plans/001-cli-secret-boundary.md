# 计划 001：使 CLI 回归的凭据只通过子进程环境传递

> 执行者须逐步执行并保存非敏感证据。每一步的检查通过后再继续；遇到 STOP 条件先报告。只实现本文白名单内的修复，不自动提交、推送、创建 PR 或发布。索引由协调者维护。

## 状态与依赖

- 状态：DONE
- Milestone：M1 安全与验收基础
- 执行证据：[001](evidence/001.md)。
- Finding ID：F01
- 优先级：P1；工作量：M；修复风险：LOW
- 类别：security / tests
- 依赖：无；本计划是 002、004 的前置条件。
- 规划基线：c80c788fb3cd34d97626d578db580264a28a11f3，2026-10-28。
- 仓库根目录：/Users/wanghongyi/Projects/dameng-entityframework-core

## 问题与目标

现有真实库 CLI 回归将连接串放进 scaffold 的进程参数，生成含连接配置的源码后输出全文。字符串脱敏只能处理原始值，不能保护进程参数，也不能可靠处理 C# 转义后的值。数据库清理异常还会跳过临时目录删除。目标是在保留迁移、反向工程、生成代码重新编译及模型断言的前提下，让真实连接仅进入必要子进程的环境；argv、生成源码、日志及遗留文件均不得承载凭据。

本次发现是源码数据流证据，尚未证明历史上真实凭据已泄漏。不得搜集或打印历史真实凭据；若维护者独立确认泄漏，应轮换对应凭据，不能把删除文件等同于完成轮换。

## 当前状态与已有模式

- test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs:174–193：

      [
          "dbcontext", "scaffold", connectionString,
          DamengTestDriver.ProviderName,
          "--no-build",
          // 表过滤和输出选项省略
      ]);
      output.WriteLine(Redact(connectionString, scaffoldedContext));

- 同文件 :258–272：数据库打开、逐对象 DROP 均在目录删除之前；任一异常可跳过 TryDeleteDirectory。:751–762 的 TryDeleteDirectory 吞掉 IO/权限异常。
- 同文件 :677–698：Redact 对原始连接串和原始字段值做 Replace，未处理 C# 字面量转义。
- 同文件 :348–380 在临时工程中引用提供程序 DLL 和锁定版本的 EF Design；:493 向子进程环境传入连接串。保留“临时工程不引用仓库项目”的现有设计。
- scripts/local-test/TestLauncher.cs:327–330 的验收要求为退出零、Completed、总数非零、执行数等于总数且全部通过、无跳过。:332–412 的 SelfTest 是合成数据、故障注入和 finally 清理的现有测试模式。
- test/W.EntityFrameworkCore.Dameng.Tests/DamengDesignTimeTests.cs 是设计时单元测试位置模式；新的合成 CLI 测试使用普通 Fact/Theory，不使用 DamengFact，不需要数据库连接。

## 已核实的 EF 接线约束

EF 命名连接并非自动读取任意环境变量。EF 10.0.12 的 [NamedConnectionStringResolverBase](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Storage/Internal/NamedConnectionStringResolverBase.cs) 从应用服务容器中的 IConfiguration 查找指定键；[AppServiceProviderFactory](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Design/Design/Internal/AppServiceProviderFactory.cs) 通过宿主发现机制取得该容器。必须用实际 EF Design 流程验证配置可达，不能只测试自己写的字典读取函数。

采用固定的非敏感别名 Name=ConnectionStrings:DamengCliFixture，并在临时宿主中提供 Host/IConfiguration，配置值来自子进程环境 ConnectionStrings__DamengCliFixture。真实 scaffold 同时传 --no-onconfiguring；重新编译及 dbcontext script 所需的上下文配置由单独的部分类型/设计时工厂从环境取得，源码只包含环境变量名。仅增加 --no-onconfiguring 不能解决 argv 问题。

临时测试工程可增加 Microsoft.AspNetCore.App 的 FrameworkReference，以取得 .NET 10 自带的 Host/配置组件；该引用只存在于夹具生成的临时工程，不进入提供程序项目、不修改仓库包版本或锁文件，也不启动 HTTP 服务器。合成试验若证明此接线在已锁定 EF 工具上不可用，停止报告；禁止回退为原始连接串参数。

## 修改白名单与排除范围

仅允许修改或新增：

- test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs
- test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengCliTestHarness.cs（新增，无具体驱动类型的夹具辅助代码）
- test/W.EntityFrameworkCore.Dameng.Tests/DamengCliFixtureTests.cs（新增）
- test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj（只增加上述 helper 的链接编译）
- test/W.EntityFrameworkCore.Dameng.FunctionalTests/README.md（凭据/清理边界）

W 功能项目已链接官方功能目录中的全部源码，新 helper 会自然进入 W 功能项目。不得修改产品 SQL、公开 API、驱动版本、任何 packages.lock.json、发布 workflow、真实 secrets、真实服务器策略。本文不修复 Debug DLL 来源，交给 002；也不增加 CI 或离线通道，交给 004。

## Git 与开始检查

分支名为 codex/001-cli-secret-boundary。检查并保留现有未提交文件；不要覆盖 .agents、.claude 或其他人的 plans 文件。先运行：

    git status --short
    git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengCliTestHarness.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengCliFixtureTests.cs test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj test/W.EntityFrameworkCore.Dameng.FunctionalTests/README.md

原始 SHA 是审查锚点。实施时先核对已验收计划造成的差异，更新实施 SHA 和摘录定位后继续；可明确归因且不冲突的正常整合无需再次请求确认。只有不可归因差异、未完成合并或契约冲突才暂停核对。本计划没有前置依赖，不能把其他未验收修复默认为已完成。

## 分步实施及验证

### 1. 抽取最小夹具边界并建立合成回归

将进程启动参数、环境、临时目录生命周期和必要源码生成收敛到 DamengCliTestHarness。通过参数传入提供程序名称、程序集路径和配置方法名；helper 不直接依赖 Dm 或 W.Dm 类型。用链接源码让官方单元项目直接测试相同实现，不复制一套测试专用实现。

新增 DamengCliFixtureTests。所有哨兵都在测试进程中合成，不读取环境中的真实连接值，也不输出哨兵。覆盖包含反斜杠、双引号、分号及非 ASCII 字符的值；至少断言 argv、生成源码、日志和最终输出文件中没有原始值及其 C#/JSON 转义形式。使用进程请求对象或可替换的进程启动接口观察参数，不调用系统进程列表读取其他进程。

验证：

    scripts/local-test/run.sh test unit --filter FullyQualifiedName~DamengCliFixtureTests

预期新增测试被发现且全部通过，TRX 为 Completed、非零、无失败/跳过。首次写回归时可记录针对旧实现的预期失败，修复完成后不得保留失败。

### 2. 用真实 EF Design 合成路径证明命名连接解析

在合成临时工程中生成显式 Program.CreateHostBuilder，注册 IConfiguration 并只从环境解析固定连接键。保持原 CLI 工具按锁定 EF 版本安装/调用的方式。合成工程通过 IDesignTimeServices 替换 IDatabaseModelFactory 为不连接网络的记录型工厂：它核对收到的是环境中的合成值，返回最小 DatabaseModel，使实际 dotnet ef dbcontext scaffold 完成。断言输出源码不含合成值；缺失配置、错误别名和缺少 IConfiguration 均必须失败，记录型工厂不得收到原始别名或调用真实驱动。

合成案例另验证提供程序代码生成的 UseDameng 调用形态，避免为保留当前 Assert.Contains 而在真实源码中重新引入连接字符串。临时宿主不启动服务，不读 appsettings/user-secrets，不输出配置对象。用于安装工具/restore/build 的子进程须移除继承的连接环境；只有配置解析、scaffold 或实际数据库运行子进程可获得必要值。

验证仍使用上一步的聚焦 unit 命令。预期实际 EF Design 正向用例成功，反向用例由测试断言为预期错误，整个测试宿主仍为 Completed 且全部通过；真实数据库打开次数为零。

### 3. 切换真实 CLI 回归并保证两种清理互不阻断

真实 scaffold 的 argv 改为固定 Name 别名并加 --no-onconfiguring；移除生成上下文全文输出。按环境配置上下文的补充源码必须允许现有模型断言、重新编译和 dbcontext script 继续工作。需要日志时仅输出步骤名、退出码和脱敏后的有限错误，不输出环境或配置值。

Unix 下以仅当前用户可访问的权限创建临时目录，不能先以默认权限写敏感文件再补权限。用嵌套 try/finally 使文件清理在数据库打开失败、DROP 失败、编译失败和断言失败时仍执行；清理失败必须返回安全诊断并使测试失败，不能吞掉异常。保留原始失败及清理失败两类信息，但不附加原始连接或完整敏感源码。只清理本次唯一目录和精确数据库对象。

验证：

    scripts/local-test/run.sh test unit --filter FullyQualifiedName~DamengCliFixtureTests
    scripts/local-test/run.sh test functional --filter FullyQualifiedName~MigrationsAndScaffoldRunThroughDotNetEfCli
    scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --filter FullyQualifiedName~MigrationsAndScaffoldRunThroughDotNetEfCli

后三行按顺序运行，严禁两驱动同时占用数据库测试模式。使用已有安全配置，不能打印/重建 secrets。两条真实库通道各应非零、Completed、无失败或跳过，并保留原有迁移、模型与重建 SQL 断言。缺少真实库连接时停在待验收状态，不把跳过算通过。

### 4. 回归与证据整理

    scripts/local-test/run.sh test unit
    scripts/local-test/run.sh test unit --driver w
    scripts/local-test/run.sh selftest
    git diff --check
    git status --short

预期两组单元满足统一 TRX 验收，selftest 退出零，diff 无空白错误，仅白名单文件变化。记录命令、提交/工作区身份、非敏感 TRX 路径和结果，不复制原始日志内容。

## 必需测试矩阵

| 场景 | 预期 |
| --- | --- |
| 正确命名键 + 合成 IConfiguration + 实际 EF Design | 记录工厂收到解析值，不访问网络，源码无值 |
| 错误别名/缺少配置/缺少 IConfiguration | 失败关闭，无原始连接 fallback |
| 特殊字符合成值 | argv、源码、日志、TRX 无原始及转义值 |
| restore/build 子进程 | 不携带连接环境 |
| 数据库清理异常 | 文件清理仍执行，运行失败且诊断安全 |
| 文件删除异常 | 不吞错，不报告通过，报告可定位但非敏感的残留状态 |
| 官方/W 真实 CLI | 原有迁移、scaffold、重新编译和模型断言保留 |

## 完成标准

- [x] scaffold argv 只有固定 Name 别名，真实生成源码不包含连接字面量。
- [x] 实际 EF Design 合成测试证明 IConfiguration 接线成立，失败路径没有 raw fallback。
- [x] 临时目录权限和独立 finally 清理已由合成测试覆盖。
- [x] 官方/W 聚焦实库串行通过；两组单元及 selftest 通过，所有 TRX 非零、Completed、无跳过。
- [x] 不改变产品能力、依赖范围或锁文件；协调者收到非敏感证据后更新索引状态。

## STOP 条件与维护

若必须将真实连接写进 argv/源码才能通过、命名配置无法被实际 EF 工具取得、需要改变产品依赖、清理无法证明对象归属，立即停止并说明证据。出现真实凭据输出时停止扩大采集，报告渠道和位置，交由维护者核查轮换；不得把值写入报告。两个合理修复尝试后同一验证仍失败也停止。

后续 EF 版本或 CLI 宿主方式变化必须重跑命名连接正反向合成测试；添加新子进程调用需明确是否需要连接环境。002 将继续调整同一夹具的程序集身份，须保留本文全部安全回归。004 会把合成单元纳入无凭据 CI。

## 批次交付与统一证据

本批 M1 为起始批次，按正文的技术前置顺序执行。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/001.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为确认问题或验证缺口，须以“已修复并验证”闭合；补足有辨别力的测试证据也属于验证缺口的修复。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M1.md` 的输入。
