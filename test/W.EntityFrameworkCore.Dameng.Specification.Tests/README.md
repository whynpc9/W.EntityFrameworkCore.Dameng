# EF Core 关系数据库规范冒烟测试切片

本项目引用 `Microsoft.EntityFrameworkCore.Relational.Specification.Tests`
10.0.x 以使用共享测试工具，包括 `RelationalTestStore`。目前包含四项提供程序自有测试：

- 提供程序/测试存储服务接线；
- 标量类型往返；
- 参数化筛选、排序和投影；
- 跟踪式更新/删除的受影响行数。

它**不**继承上游 EF 关系数据库 `*TestBase` 测试套件。本项目通过只能证明上述冒烟
测试切片通过，并不构成 EF Core 关系数据库一致性结果。

实时测试面向现有数据库和当前用户模式：

```bash
scripts/local-test/run.sh test specification
```

任何测试都不会创建或删除数据库、用户或模式。每项实时测试都会创建名称唯一的表，
并在 `finally` 中仅删除该表。连接字符串从进程环境读取，绝不能记录到日志中。
统一入口从 Git 忽略的本地 secrets 加载测试账号，详见[本地测试环境](../../scripts/local-test/README.md)。

## 跳过分类

- `[environment]`：缺少 `DAMENG_TEST_CONNECTION_STRING`。
- `[unsupported]`：达梦无法提供所需的数据库能力。
- `[provider-gap]`：提供程序尚未实现所需接口。

提供程序缺口不得归类为数据库限制。采用大范围上游基础测试套件时，应拆分为明确、
可审查的切片，并逐项说明提供程序特定的失败和跳过原因。
