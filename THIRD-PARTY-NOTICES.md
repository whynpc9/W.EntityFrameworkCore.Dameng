# 第三方声明

本项目是独立维护的非官方 Entity Framework Core 提供程序。
它与达梦或 Microsoft 不存在关联，亦未获得其赞助或认可。

运行时依赖项：

- `Microsoft.EntityFrameworkCore.Relational` 采用 MIT License。
- `DM.DmProvider` 由达梦依据 Apache License 2.0 分发。该驱动程序仍是独立的
  NuGet 依赖项，不会由本包嵌入或再分发。
- 可选扩展包 `W.EntityFrameworkCore.Dameng.WDriver` 依赖的 `W.DamengProvider 0.2.0`
  声明采用 Apache License 2.0；其版权声明为“Portions Copyright (C) DM;
  modifications Copyright (C) W.DamengProvider contributors”。该驱动程序保持独立
  NuGet 依赖，不嵌入本项目包；其许可证与第三方声明随驱动包分发。

实现参考：

- 提供程序服务结构遵循 `dotnet/efcore` 仓库中的公开提供程序扩展点及采用
  MIT 许可证的提供程序示例，并锁定到 EF Core 10.0.12 以保持 API 兼容性。
- FreeSql 采用 MIT 许可证的达梦提供程序和 SqlSugar 公开的达梦行为仅用作功能检查清单。
  本项目不包含它们的实现代码。

达梦数据库服务器软件、安装介质及许可证密钥均不属于本项目。
