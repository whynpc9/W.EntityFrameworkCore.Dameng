# 0.4.0 发布准备

检查日期：2026-10-16（Asia/Shanghai）。源码基线：`b3e8da3ab31a4eeae99718adbcf05444868ddd6b`。
状态：**本地验收通过，已获公开发布授权；公共发布执行结果待补**。

## 版本与顺序

| 包 | 候选版本 | 发行标签 | 关键依赖 |
| --- | --- | --- | --- |
| `W.EntityFrameworkCore.Dameng` | `0.4.0` | `v0.4.0` | `DM.DmProvider [8.3.1.47463,8.4.0)`、EF Core `[10.0.12,11.0.0)` |
| `W.EntityFrameworkCore.Dameng.WDriver` | `0.4.0-preview.1` | `wdriver-v0.4.0-preview.1` | 基础包 `[0.4.0]`、`W.DamengProvider [0.2.0]` |

基础包和扩展包独立发行，扩展包保持预发布状态。先完成基础包发布并确认其精确版本在
NuGet.org 可下载，再发布扩展包。两个标签应指向同一最终提交。
准备阶段未创建发行标签或公开发布；用户随后已授权按上述顺序公开发布。

2026-10-16 在线检查时，NuGet 基础包版本索引最新为 `0.3.0`；扩展包索引返回 404，
远端无上述两个新标签。正式推送标签前须重新检查版本唯一性。

## 相对 0.3.0 的变化

- 反向工程补足非键虚拟计算列、显式表筛选完整性、可空主体候选键及元数据完整性检查，
  减少目录查询往返；无法保留的结构继续明确拒绝。对应 PR #5 和后续 main 修订。
- 新增 `DateTime.ToString()` 的服务器端翻译，覆盖投影、谓词、排序和分组。
  无格式重载使用达梦默认日期时间文本；受支持的常量格式使用 `TO_CHAR`。
  不模拟客户端文化格式，不推断业务分组粒度，见[日期字符串翻译合同](datetime-string-translation.md)。
- 提供公开的 `DamengDriver` 桥接，并增加可选 W 扩展包：`UseDamengWithWDriver`、
  `AddEntityFrameworkDamengWithWDriver` 和独立设计时入口。官方驱动仍为默认。
- W 通道接入异步网络调用、取消与期限、`FailureInfo`、保存点、七位小数秒参数与读取。
  使用 `W.DamengProvider 0.2.0` 后，在匹配 profile 上覆盖整 tick 的八/九位时间列写入、
  事务内纯 DML 匿名块、参数化准备阶段错误后的会话保留和传输失败分类。
- `TimeSpan` 显式 TIME 映射经 TimeOnly 转换器读写；裸 TIME 为零位小数秒，
  不可表示的负值、跨日值或精度损失在发送前拒绝。

## 升级注意事项

- `UseDameng(DbConnection)` 只接受官方 `Dm.DmConnection`；W 对应入口只接受
  `W.Dm.DmConnection`。包装连接（如 profiler）会被拒绝。
- 每个 options 只能选择一种驱动。W 的 `Database.ProviderName` 为
  `W.EntityFrameworkCore.Dameng.WDriver`；已有迁移若按旧 `ActiveProvider` 名称分支，
  需要显式接受两种名称，见[迁移指南](migrations.md)。
- W 默认要求 TLS，明文连接必须显式选择 `PlaintextAllowed`；本地验收仅有明文 profile。
  W 禁止 `EnableRetryOnFailure` 自动重试。结果未知时先在独立连接核对业务效果。
- 不自动调整既有列精度；七位降六位需要先核对数据并使用守卫，不可截断第七位。
  DDL 会隐式提交，切换驱动不能作为迁移回滚或重放未知结果的手段。
- 依然只支持 .NET 10 / EF Core 10；不扩展最低达梦版本声明。两种驱动都使用单命令修改批次。

## 本轮验收

本地 `info` 重新取得服务器标识 `DM Database Server 64 V8` / `DM Database Server x64 V8`，
`PAGE()=32768`、`COMPATIBLE_MODE=0`、`LENGTH_IN_CHAR=0`、`GLOBAL_CHARSET=1`、
`CALC_AS_DECIMAL=0`、`JSON_MODE=0`、`CLOB_MAX_CALC_LEN=20480`。
不把旧记录中的 `8.1.5.60` 视为本次 `info` 返回值；W 专属测试另核对驱动能力 profile。
运行前测试空间表、序列、回收站对象均为 0，容量充足，无需重新初始化。

首轮官方 `all`（`20261016T021830739Z-b6b43050`）的单元通道锁定还原被 NU1902 / NU1903 阻断：SqlServer 对照测试传递引入
`System.Security.Cryptography.Pkcs 9.0.11`，命中
[GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) 与
[GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx)。该轮不得记为发布通过。
用户已授权仅对两组单元测试将此传递依赖固定到修复版 `[9.0.21]`，更新对应锁文件；
EF Core 和两种达梦驱动依赖范围保持原样，不禁用 NuGet 安全审核。

该首轮的功能 300/300、规范 4/4、管理员脚本 7/7 通过，但整个命令退出 1；单元没有 TRX。
修复后冻结依赖，顺序重跑官方与 W 的完整 `all`：

| 通道 | 官方 | W |
| --- | --- | --- |
| unit | 791/791 | 61/61 |
| functional | 300/300 | 365/365 |
| specification | 4/4 | 4/4 |
| admin | 7/7 | 7/7 |
| 合计 | 1102/1102 | 437/437 |
| 运行 ID | `20261016T022332508Z-f333d3c1` | `20261016T022715863Z-7af7a36c` |

两个命令均退出 0，全部八个宿主为 Completed，Total=Executed=Passed，Failed=Skipped=0。
脱敏 TRX 位于 `artifacts/query-translation/local-test/<运行 ID>/`，W 文件带 `-w` 后缀。
W 使用显式 `--w-transport-security PlaintextAllowed`，两种驱动数据库通道没有并行运行。
运行后 `inspect` 确认持久测试空间的表、序列、回收站对象仍全部为 0。

### Release 与包检查

- SDK `10.0.401`，解决方案锁定还原通过；Release 全解决方案构建 0 警告、0 错误。
- Release 单元：基础包 791/791，W 扩展包 61/61，均 Completed、无失败或跳过；
  TRX 在 `artifacts/release-0.4.0/results/`。
- `dotnet format --verify-no-changes --no-restore` 与 `git diff --check` 通过。
- `verify-package.py --require-lock-reference` 全部通过，实际 W 包、DLL SHA-256/MVID、
  来源提交和锁文件 contentHash 与 manifest 一致。
- 两个 nupkg 的 ZIP、PackageId/版本、MIT license、net10.0 DLL/XML、README 与第三方声明通过检查；
  不嵌入两种驱动 DLL。W 驱动的 Apache-2.0 与版权声明已补入第三方声明。
- 两份单元锁文件只改变 Pkcs 一项；两个提供程序项目的锁文件逐字节未变。
  Pkcs 未进入任何产品包依赖图。

本地候选包在 `artifacts/release-0.4.0/packages/`：

| 包 | SHA-256 |
| --- | --- |
| `W.EntityFrameworkCore.Dameng.0.4.0.nupkg` | `e8cdb2e2b6fc34153b0d4fa05c9cc4614887bd8ac8995115eed30b3ad77c2024` |
| `W.EntityFrameworkCore.Dameng.WDriver.0.4.0-preview.1.nupkg` | `4d06737c469e2c4f36a323889cf97cfbc4490b6e24d190b4984b7c410748eb83` |

扩展包 nuspec 精确依赖基础包 `[0.4.0]`、W 驱动 `[0.2.0]`；中央传递依赖固定还使其显式列出
与基础包一致的 `DM.DmProvider [8.3.1.47463,8.4.0)` 和 EF Relational `[10.0.12,11.0.0)`。
完整元数据见本地 `artifacts/release-0.4.0/package-manifest.json`。
这些包包含工作区的 README/第三方声明准备变更，nuspec repository commit 仍为源码基线 `b3e8da3`；
提交准备变更后，由正式标签 CI 在最终提交重建，不把本地包散列当作未来公开签名包散列。

### 精确包消费者

三个独立项目只使用 PackageReference，通过显式源映射把两个候选 PackageId 限定到本地 feed，
并使用与开发缓存分离的 `NUGET_PACKAGES`；还原后的 nupkg 字节与上表候选包一致。

| 消费者 | 结果 |
| --- | --- |
| 仅基础包 `[0.4.0]` | Release 构建零警告/错误；官方 ProviderName/连接类型、日期字符串 CAST 与 `:name` SQL 正确；不加载 W 驱动 |
| 仅 W 扩展包 `[0.4.0-preview.1]` | 构建零警告/错误；传递取得基础包 `[0.4.0]`，W ProviderName/连接类型、日期/参数 SQL 正确；W DLL MVID 与 manifest 一致 |
| 两包 + SqlServer `[10.0.12]` | 构建零警告/错误；三种提供程序同进程选择正确，扩展方法无歧义；对照消费者单独固定 Pkcs `[9.0.21]` |

消费项目及记录在 `artifacts/release-0.4.0/consumers/` 和 `consumer-verification.json`。
这是打包、服务注册与 SQL 翻译证据，没有额外执行数据库操作；真实服务器行为证据来自上面的统一测试通道。

## 审查与发布流程

本轮在线核对：PR #7 已合并，其 2 条 review threads 均 resolved，最终 head `6279bcb`
的 Cursor review 为 No findings；PR #8 已合并，其 3 条 threads 均 resolved，最终 head
`59370af` 的 Cursor review 为 No findings。Codex 的初轮意见均已处置，
不将其旧提交评论写成最终 head 的新审查。合并状态不替代本轮运行验收。

W 工作流补充依赖 `publish` 成功后才执行的 GitHub Release job，权限仅该 job 为
`contents: write`；幂等检查已有 Release，预发布版本标记为 prerelease。
工作流尚未触发，本地检查不构成远端 Actions 成功。
两个发布工作流的 YAML 解析、Release shell 语法以及稳定版/预览版/已有 Release/创建失败四类
本地 mock 检查通过；检查未调用真实 GitHub 写入。记录在 `artifacts/release-0.4.0/workflow-checks/`。

公开发布前按工作流原样增加 `-p:PackageVersion=0.4.0-preview.1` 复核打包，发现该全局属性
会传播到基础包 ProjectReference，使扩展包错误依赖 `[0.4.0-preview.1]`。此前本地默认打包
未覆盖此发行参数。改用扩展包专属 `WDriverPackageVersion` 属性，工作流
通过该属性只覆盖扩展包自身版本；锁文件不变。按修正后的工作流重跑标签参数，扩展包版本为 `0.4.0-preview.1`，
基础包精确依赖恢复为 `[0.4.0]`。本项只改变打包属性传播，不改变运行时代码；另行重跑
锁定还原、Release 构建和 W 单元，真实库完整运行仍为上文两次记录。
另外用不同于默认版本的 `0.4.0-check.20261016` 本地打包验证覆盖确实生效，基础包依赖仍为
`[0.4.0]`；该检查包不发布。两项结果在 `artifacts/release-0.4.0/tag-pack-final/` 和
`tag-pack-override-check/`，Release 构建 0 警告/错误、W 单元 61/61，格式验证通过。

发布执行时：

1. 确认最终变更、依赖、完整验收及候选包身份，提交发布准备内容。
2. 推送最终提交，为基础包创建并推送附注标签 `v0.4.0`。
3. 验证基础包 Actions 的 `publish` / `release`、GitHub Release 和 NuGet 公共包内容。
4. 基础包可下载后，在同一提交创建并推送 `wdriver-v0.4.0-preview.1`。
5. 分别验证 W 扩展包 Actions、预发布 GitHub Release、NuGet 精确依赖及公共包内容。

## 保留的验证边界

TLS、其他服务器版本、commit 丢失 ACK 的 EF 端到端注入、`-3510` 负例未验证。
数据源接入、流式 LOB、DmDecimal 不在本轮提供程序交付范围。
规范通道仅 4 项自有冒烟测试，不代表上游 EF 关系数据库一致性验收。
详细驱动差异和历史失败见[兼容矩阵](compatibility.md)及[W 接入验证记录](w-dmprovider-validation.md)。
