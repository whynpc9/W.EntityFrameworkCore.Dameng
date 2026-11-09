# 0.6.0 发布准备

日期：2026-11-09（Asia/Shanghai）。源码基线：`75af5f6`。
状态：候选包已生成。以下版本均为正式版；本轮双驱动实库验收未全部通过，尚未公开发布。

| 包 | 版本 | 拟用标签 | NuGet 依赖 |
| --- | --- | --- | --- |
| `W.EntityFrameworkCore.Dameng` | `0.6.0` | `v0.6.0` | EF Core Relational `[10.0.12,11.0.0)`、官方驱动 `[8.3.1.47463,8.4.0)` |
| `W.EntityFrameworkCore.Dameng.WDriver` | `0.6.0` | `wdriver-v0.6.0` | 基础包 `[0.6.0]`、W 驱动 `[0.3.2]`；EF Core 与官方驱动范围同基础包 |

W 扩展包自此版本起去掉 `preview` 后缀；从 `0.5.0-preview.1` 升级时，
若应用显式引用基础包或 W 驱动，请同步调整到表中的配对版本。仍通过
`UseDamengWithWDriver` 显式选择 W 驱动。目标框架仍为 .NET 10，EF Core 和官方驱动范围不变。

## 变更

- 模型快照保留继承属性的 Dameng IDENTITY 分面，避免重新载入快照后丢失生成策略
  （[#16](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/16)）。
- 可空日期恒等转换后的 `ToString()` 沿用原生时间列的服务器翻译和空值语义
  （[#18](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/18)）。
- 统一显式 LOB 类型的识别，使查询守卫和模型校验对同一类型一致
  （[#19](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/19)）。
- 收紧测试 TRX 的脱敏和结果判定；两条 NuGet 工作流已迁移到 Trusted Publishing
  （[#17](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/17)、
  [#15](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/15)）。
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

| 通道 | 官方通过/总数 | W 通过/总数 |
| --- | ---: | ---: |
| Release 单元 | 1051/1051 | 187/187 |
| 离线夹具 | 46/46 | 60/60 |
| 真实库功能 | **412/414** | **500/501** |
| 规范冒烟 | 4/4 | 4/4 |
| 管理员迁移脚本 | 11/11 | 11/11 |

官方实库运行 ID：`20261109T043546664Z-e2d25d6e`；无跳过。
`ClosingOwningConnectionReleasesLock` 的同步、异步两项失败：关闭拥有锁的连接后，
独立连接的零等待请求返回 1，预期为 0。现象与 [0.5.0 发布记录](release-0.5.0.md#候选验证)
所记失败一致；未确定根因。两轮聚焦复测分别为 2/2（`20261109T044231556Z-85258b38`）
和 1/2（`20261109T044237616Z-fe454351`，同步用例再次失败），全量失败仍然成立。

W 实库运行 ID：`20261109T042538967Z-c4ce2346`；无跳过。唯一功能失败为
`CancelWaitingAcquirePreservesTokenAndAllowsLaterAcquisition`：持久普通测试账号没有
`SYS.V$DBMS_LOCKS` 的查询权限，目录预检失败，未执行等待取消。需要在具备该视图只读权限的
普通隔离账号补测，不能用管理员连接替代。W 使用显式 `PlaintextAllowed`，本轮不验证 TLS；
规范冒烟为仓库自有四项切片。两通道各 9 份管理员恢复记录均为 `cleaned`；
最终检查确认专用模式中的表、序列和回收站对象均为 0。

## 发布顺序

1. 复核候选包和回归结果。先定位官方锁释放间歇失败，并在普通隔离账号补齐 W 等待取消验证。
   完成候选提交的远端 CI 后，重新核对两个标签和 NuGet 版本均未占用。
2. 在最终提交创建并推送 `v0.6.0`，由基础包工作流发布；验证 Actions、GitHub Release、
   NuGet 可下载包的内容和签名。
3. 基础包可从 NuGet.org 获取后，在同一提交创建并推送 `wdriver-v0.6.0`。扩展包的
   GitHub Release 为正式版，仓库的 Latest Release 仍指向基础包。
4. 核对公共 W 包的签名、精确依赖和独立消费者安装，再更新本记录及文档首页的公开状态。
