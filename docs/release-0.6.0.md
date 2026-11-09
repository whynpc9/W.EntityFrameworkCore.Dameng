# 0.6.0 发布准备

日期：2026-11-09（Asia/Shanghai）。源码基线：`75af5f6`。
状态：准备中。以下版本均为正式版，候选包仅用于本地验证。

| 包 | 版本 | 拟用标签 | NuGet 依赖 |
| --- | --- | --- | --- |
| `W.EntityFrameworkCore.Dameng` | `0.6.0` | `v0.6.0` | EF Core Relational `[10.0.12,11.0.0)`、官方驱动 `[8.3.1.47463,8.4.0)` |
| `W.EntityFrameworkCore.Dameng.WDriver` | `0.6.0` | `wdriver-v0.6.0` | 基础包 `[0.6.0]`、W 驱动 `[0.3.2]`；EF Core 与官方驱动范围同基础包 |

W 扩展包自此版本起去掉 `preview` 后缀；从 `0.5.0-preview.1` 升级时，
若应用显式引用基础包或 W 驱动，请同步调整到表中的配对版本。仍通过
`UseDamengWithWDriver` 显式选择 W 驱动。目标框架仍为 .NET 10，EF Core 和官方驱动范围不变。

## 变更

- 模型快照保留继承属性的 Dameng IDENTITY 分面，避免重新载入快照后丢失生成策略
  （[#16](https://github.com/whynpc9/dameng-entityframework-core/pull/16)）。
- 可空日期恒等转换后的 `ToString()` 沿用原生时间列的服务器翻译和空值语义
  （[#18](https://github.com/whynpc9/dameng-entityframework-core/pull/18)）。
- 统一显式 LOB 类型的识别，使查询守卫和模型校验对同一类型一致
  （[#19](https://github.com/whynpc9/dameng-entityframework-core/pull/19)）。
- 收紧测试 TRX 的脱敏和结果判定；两条 NuGet 工作流已迁移到 Trusted Publishing
  （[#17](https://github.com/whynpc9/dameng-entityframework-core/pull/17)、
  [#15](https://github.com/whynpc9/dameng-entityframework-core/pull/15)）。
- W 扩展包升级到 [W.DamengProvider 0.3.2](https://github.com/whynpc9/W.DamengProvider/releases/tag/v0.3.2)。
  上游修复了远端 CLOB 多字节分块、年月间隔边界、数组绑定校验及数据源等待连接时的取消和输入快照。
  这是 ADO.NET 驱动更新；EF 扩展包的 API 不因此新增数据源或流式 LOB 支持。

## 验证

公开签名的 W 驱动包已与 NuGet 还原缓存逐字节比对，且通过 `dotnet nuget verify --all`。
四份 W 锁文件的 contentHash、驱动 DLL 的 SHA-256/MVID 及 nuspec 来源提交均通过身份校验。
详细身份见[驱动验证记录](w-dmprovider-validation.md#升级到-wdamengprovider-0322026-11-09)。

- 解决方案锁定还原、Release 构建及两个正式版本候选包均成功，构建无警告或错误。
- nuspec 核对了包版本和依赖范围。两个包均包含 MIT 元数据、net10.0 DLL/XML、README 和第三方声明；
  驱动 DLL 继续由独立依赖提供。
- 两个独立 `PackageReference` 消费者从本地候选源安装并运行：仅基础包，以及 W 扩展包加
  SQL Server。安装缓存与候选包字节一致，基础包消费者不引入 W 驱动。SQL Server 对照沿用仓库
  单元测试的 Pkcs `[9.0.21]` 约束；该约束不进入候选产品包。消费者未连接数据库。
- 离线编排、自检、聚合审计、包身份检查通过；W 发布工作流通过 actionlint，并用正式版和
  预览版标签核对了 GitHub Release 参数。
- 候选包清单及日志位于本地 `artifacts/release-0.6.0/`。候选包来自本地准备工作区，nuspec
  的来源提交是上述基线；正式发布工作流将从最终标签重建。


## 发布顺序

1. 复核候选包和回归结果、完成候选提交的远端 CI，重新核对两个标签和 NuGet 版本均未占用。
2. 在最终提交创建并推送 `v0.6.0`，由基础包工作流发布；验证 Actions、GitHub Release、
   NuGet 可下载包的内容和签名。
3. 基础包可从 NuGet.org 获取后，在同一提交创建并推送 `wdriver-v0.6.0`。扩展包的
   GitHub Release 为正式版，仓库的 Latest Release 仍指向基础包。
4. 核对公共 W 包的签名、精确依赖和独立消费者安装，再更新本记录及文档首页的公开状态。
