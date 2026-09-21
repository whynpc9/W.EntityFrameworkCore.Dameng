# 仓库指南

## 范围

- 文档默认使用中文；仅在保留代码标识、命令、链接、正式名称或行业惯用术语时使用英文。
- 保持 `W.EntityFrameworkCore.Dameng` 独立于具体应用框架和具体应用。
- 除非明确要求依赖项相关工作，否则保留锁定的 EF Core 10 /
  `DM.DmProvider` 版本范围和锁文件。
- 不得复制或反编译达梦官方 EF 提供程序。仅使用公开的 EF 契约、达梦公开文档和黑盒测试。
- 在缺少相应实现和验证时，不得声称支持反向工程或幂等脚本。
- 编写或审查达梦 SQL / DMSQL / 表空间 / 自增列时，读取并遵循
  `skills/dameng-sql/SKILL.md`（需要细节时再打开其 `references/`）。

## 命令

```bash
dotnet restore W.EntityFrameworkCore.Dameng.slnx --locked-mode --disable-parallel
dotnet build W.EntityFrameworkCore.Dameng.slnx --no-restore
dotnet test test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj --no-build
```

真实数据库：

```bash
export DAMENG_TEST_CONNECTION_STRING='<完整连接字符串>'
dotnet test test/W.EntityFrameworkCore.Dameng.FunctionalTests/W.EntityFrameworkCore.Dameng.FunctionalTests.csproj --no-build
dotnet test test/W.EntityFrameworkCore.Dameng.Specification.Tests/W.EntityFrameworkCore.Dameng.Specification.Tests.csproj --no-build
```

绝不能提交或打印连接字符串。跳过数据库测试套件不能作为发布证据。

## 提供程序不变量

- 按组成部分引用标识符，并生成 `:name` 参数，绝不能生成 `@name`。
- 达梦公共模型 API 必须带有提供程序前缀（`UseDameng...`、`GetDameng...`、
  `SetDameng...`），以确保多提供程序使用方可以正常编译。
- 明确拒绝不支持或会造成信息损失的结构；不得生成其他数据库的语法，也不得静默截断值。
- 达梦 DDL 会隐式提交。生成 DDL 时应正确禁止事务，为测试对象使用唯一名称，并在
  `finally` 中精确清理对象。
- 驱动程序没有提供程序专用的 `DbBatch`；在真实证据支持其他策略之前，保持单命令修改批次。
- 将驱动程序异步视为同步回退，其取消能力有限。

## 证据

- 支持情况发生变化时更新 `docs/compatibility.md`。
- 区分单元级 SQL/元数据证据与真实服务器行为证据。
- 规范测试项目只是包含四项测试的冒烟切片，并非上游 EF 关系数据库一致性测试。
- 在将某项能力标记为已验证之前，应添加聚焦的单元测试；如果行为跨越驱动程序/服务器边界，
  还应添加真实数据库回归测试。
