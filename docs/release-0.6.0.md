# 0.6.0 发布记录

日期为 2026-11-09（Asia/Shanghai）。两个正式包均已公开发布，来源提交为 `4a00bf22d88d6e6d96ea2e3f94f72f9b150c20ae`。
维护者在获知官方迁移锁释放失败和 W 锁等待观测权限缺口后确认发布。实库验收结果见下文。

| 包 | 版本 | 发布标签 | NuGet 依赖 |
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

## 公开发布

[PR #21](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/21) 合并后，先发布基础包。
基础包通过公开下载和签名验证后，才从同一提交发布 W 扩展包。两个发布工作流均通过
NuGet Trusted Publishing 完成上传。GitHub Latest Release 指向基础包 `v0.6.0`。

| 包 | 发布流水线 | GitHub Release | NuGet |
| --- | --- | --- | --- |
| 基础包 | [42568885412](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/actions/runs/42568885412) | [v0.6.0](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/releases/tag/v0.6.0) | [0.6.0](https://www.nuget.org/packages/W.EntityFrameworkCore.Dameng/0.6.0) |
| W 扩展包 | [42569571564](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/actions/runs/42569571564) | [wdriver-v0.6.0](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/releases/tag/wdriver-v0.6.0) | [0.6.0](https://www.nuget.org/packages/W.EntityFrameworkCore.Dameng.WDriver/0.6.0) |

公开包通过 NuGet.org 仓库签名验证，包内版本、nuspec 来源提交及依赖与发布预期一致。
W 扩展包精确依赖基础包 `[0.6.0]` 和 `W.DamengProvider [0.3.2]`。

| 公开签名包 | SHA-256 |
| --- | --- |
| 基础包 | `30a1f4ecf7a606a25f1b72c193ecb314aaf31b9f8396aedd99ff5028e0bb2bcb` |
| W 扩展包 | `fe0992bcfd96351842accf520f705c86e770157b9bc5871a8e0377df90396c48` |

仅使用 NuGet.org 的两个独立消费者均成功构建并运行。基础包单独注册成功，W 扩展包与官方达梦及
SQL Server 提供程序可在同一进程注册。消费者缓存中的发行包与已验签的公开包字节一致。
消费者验证未连接数据库。签名、包清单和消费者日志保存在本地 `artifacts/release-0.6.0/public/`。
