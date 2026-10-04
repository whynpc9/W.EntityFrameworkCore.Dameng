# 0.3.0 发布记录

检查日期：2026-10-04。候选版本：`0.3.0`。
状态：**已发布，CI、GitHub Release、NuGet 版本页及公共包下载已验证**。

源代码基线：`dd046370513f769d97439b432f69b7f4f2e5dff8`（PR #3 已合并的 `main`）。
本地验收基线为已发布的 `0.2.0`；最终发布提交为 `e14e9fd473002e1799404d1e3e518fd8d4f8b594`，附注标签为 `v0.3.0`。

## 相对 0.2.0 的变化

- 增加 `dotnet ef dbcontext scaffold` 设计时反向工程：读取当前模式的表、视图、列、
  默认值、表/列注释、主键、唯一约束、普通索引、外键、IDENTITY 和序列。
- 按目录保留受支持的类型精度、字符长度语义、序列分面、B 树存储及表/索引填充因子，
  经模型注解、C# 脚手架和迁移 SQL 链路还原。过滤器区分未引用与双引号标识符。
- 对不能精确保留的结构明确拒绝，包括跨模式依赖、触发器、用户 CHECK、表达式索引、
  不支持的约束动作/状态、临时表、分区表、LONG ROW、HUGE、物化视图预建表、
  非默认物理空间和表空间页数上限；完整边界见[兼容性矩阵](compatibility.md)。
- 增加表/列注释迁移及幂等脚本处理，按 SQL token 识别匿名块，避免拆分块内语句。
- 支持只移除 IDENTITY 且不改变其他列语义的迁移；保留现有数据与主键。
  恢复 IDENTITY、修改种子/增量及同时改变其他列定义仍明确拒绝。
- 补充生成值即时回读、乐观并发、事务回滚和 `dotnet ef` 命令行端到端真实库回归。

## 保留边界

- 仍使用官方 `DM.DmProvider`，未接入 `W.DmProvider`；异步同步回退和取消限制不变。
- 未扩展为跨模式全量反向工程，不承诺任意数据库结构的无损重建。
- DROP IDENTITY 的新增幂等包装只有单元证据，不将原有管理员脚本回归作为其真实库证明。
- 四项 specification 测试只是仓库自有关系数据库冒烟，不是上游 EF 全量一致性测试。
- 不扩展最低服务器版本声明；能力探针不计入发布验收。
- EF Core 10、驱动依赖范围和锁文件保持不变。

## 候选验证

- 首轮统一 `scripts/local-test/run.sh test all` 退出 1。单元 682/682、功能 231/232、
  specification 冒烟 4/4、管理员脚本 7/7；均无跳过。功能宿主为 Failed，
  其他三个宿主为 Completed，不能将总计 924/925 作为发布通过。
- 脱敏 TRX：`artifacts/query-translation/local-test/20261004T015503915Z-d780ea17/`
  下的 `unit.trx`、`functional.trx`、`specification.trx`、`admin.trx`。
- 首轮唯一失败：`DamengMigrationsFunctionalTests.AnsiSizedStringColumnsUseCharSemanticsAndHoldMultibyteText`。
  在构造上下文时触发 `ManyServiceProvidersCreatedWarning`，尚未执行该用例的 SQL。
  本用例替换 `IModelCacheKeyFactory`；应检查测试专用服务容器缓存/隔离策略，
  避免完整套件的服务容器累积影响用例，同时保留字符语义及真实读回断言。
  首轮失败证据保留，不覆盖。
- 修复仅在上述用例的专用模型服务配置中加入 `EnableServiceProviderCaching(false)`，
  独立创建内部服务容器，避免依赖进程级缓存容器数量；不抑制全局告警，
  字符语义、DDL、持久化和读回断言未变，产品实现未变。
- 修复后的聚焦真实库回归 1/1，Completed，无失败或跳过；TRX：
  `artifacts/query-translation/local-test/20261004T021922666Z-8c29cfa3/functional.trx`。
- 修复后的统一 `test all` 退出 0：单元 682/682、功能 232/232、规范冒烟 4/4、
  管理员脚本 7/7，合计 925/925；四个宿主均为 Completed，无失败或跳过。
  脱敏 TRX 位于 `artifacts/query-translation/local-test/20261004T022408997Z-30a21d6e/`。
  修复后的 Release 构建为 0 警告、0 错误，格式验证通过。
- 本轮只读环境查询：`DM Database Server 64 V8` / `DM Database Server x64 V8`，
  `PAGE()=32768`、`COMPATIBLE_MODE=0`、`LENGTH_IN_CHAR=0`、`GLOBAL_CHARSET=1`、
  `CALC_AS_DECIMAL=0`、`JSON_MODE=0`、`CLOB_MAX_CALC_LEN=20480`；未取得新的构建号。
- SDK 10.0.401 锁定还原通过；更新版本后的 Release 构建 0 警告、0 错误。
- Release 单元测试 682/682 通过；格式验证通过。
- 本地包 `artifacts/release-0.3.0/W.EntityFrameworkCore.Dameng.0.3.0.nupkg` ZIP 完整性正常，
  包含 net10.0 程序集、XML 文档、README 和第三方声明；nuspec 版本为 0.3.0，
  依赖范围为 `DM.DmProvider [8.3.1.47463,8.4.0)`、
  `Microsoft.EntityFrameworkCore.Relational [10.0.12,11.0.0)`。
- 检查时 NuGet 公共版本索引最新为 0.2.0，Git 标签无 v0.3.0。
- `git diff --check` 通过；依赖与锁文件未变化。

## 后续发布步骤

1. 所有本地门禁通过后提交候选版本及验收记录，确认秘密和本地证据未进入 Git。
2. 发布时推送主分支，并在最终提交上创建、推送附注标签 `v0.3.0`。
3. 标签工作流重新构建、测试、打包和推送 NuGet，随后创建 GitHub Release。
4. 分别验证标签 CI、GitHub Release 与 NuGet 版本页/包下载；本地真实库结果不能替代这三层证据。

## 公共发布验证

- 发布日期：2026-10-04（Asia/Shanghai）。发布提交及附注标签已推送。
- [标签 CI](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/actions/runs/37172053951)
  两个任务均成功；Release 单元 682/682，NuGet 推送日志为 `Your package was pushed.`。
- [GitHub Release v0.3.0](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/releases/tag/v0.3.0)
  已公开，不是草稿或 prerelease。
- [NuGet 版本页](https://www.nuget.org/packages/W.EntityFrameworkCore.Dameng/0.3.0)
  已公开，安装命令及 PackageReference 显示 0.3.0。
- 从 NuGet 公共 CDN 下载正式包，ZIP 完整性、net10.0 程序集/XML、README、第三方声明均通过检查。
  nuspec 版本为 0.3.0，repository commit 与标签提交一致，依赖范围与候选包一致。
  SHA-256：`734fe627695e0f27867c1ae47f0241f2ee522e3f8fc1cfc7be815b6236f52d19`。
- 发布初期 NuGet v3 索引与下载接口暂返回旧索引/404；版本页及公共 CDN 随后可用。
  没有重复推送包或移动标签。
