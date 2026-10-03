# 阶段 3 双驱动接入前增量验收

日期：2026-10-02。基线：`9d1939935c8ef15e08e34b302e1a04629932ae40`。
范围与设计见[实施计划](stage3-pre-driver-plan.md)。本记录为本地验收，不代表 CI 或发行验收。

## 实现与审查

- 支持纯移除 IDENTITY：旧列自增、新列不再自增且其他列定义/语义注解不变时，生成一条表级 DROP IDENTITY，事务抑制且不附加 MODIFY。其他列变化、切换序列、恢复自增和种子/增量修改继续拒绝。
- 回读产品实现保持原状；新增测试证明所列场景可通过现有 DML + SELECT 路径即时回填，无需引入 RETURNING、输出参数或驱动切换。
- 协调者审查产品差异、真实模型差异测试、回读/并发/事务断言及清理路径。审查中将表定义查询从登录 USER 修正为当前模式，并改用独立连接清理测试表。
- Sol High 编码，Sol Low 在冻结后独立执行构建、测试、格式检查；验证期间未修改源码或测试。依赖及锁文件未变。

## 独立执行结果

统一入口为 `scripts/local-test/run.sh test <通道>`；聚焦功能过滤器为
`FullyQualifiedName~DamengDropIdentityFunctionalTests|FullyQualifiedName~DamengGeneratedValueReadbackFunctionalTests`。
SDK 固定为仓库要求的 .NET 10.0.401，启动器使用锁定还原、单节点、禁用共享编译和构建服务器。
使用既有持久测试账户；admin 通道沿用既有脚本测试的临时用户/表空间创建与精确清理流程，没有重新 provision 持久测试空间。

所有 TRX 路径相对于本地忽略目录 `artifacts/query-translation/local-test/`，均已脱敏。

| 通道 | 通过/总数 | 失败/跳过 | TRX |
| --- | --- | --- | --- |
| unit | 293/293 | 0/0 | `20261002T145418523Z-60ccd1c9/unit.trx` |
| functional 聚焦 | 15/15 | 0/0 | `20261002T145631304Z-d728fc93/functional.trx` |
| functional 全量 | 87/87 | 0/0 | `20261002T145756583Z-d5534c8b/functional.trx` |
| specification | 4/4 | 0/0 | `20261002T150649068Z-06e077fb/specification.trx` |
| admin | 4/4 | 0/0 | `20261002T150719622Z-1b9d43f1/admin.trx` |

所有测试宿主均为 Completed，执行数等于总数。
`dotnet format W.EntityFrameworkCore.Dameng.slnx --no-restore --verify-no-changes` 与 `git diff --check` 均退出 0。

本地证据目录：`artifacts/stage3-independent/20261002-225409/`、
`artifacts/stage3-independent/20261002-225622/`，包含日志及 115 个源码/测试文件的冻结哈希。
第一轮 unit 通过后，功能测试编译出现 CS4007（集合表达式推断为 ReadOnlySpan，跨 await）。
开发者仅将查询与断言拆成两句，第二轮重新冻结；两轮之间只有该功能测试文件变化，
unit 所覆盖的产品/单元代码未变，因此沿用第一轮 unit 结果。失败日志未覆盖。

## PR #4 审核修复复验

Cursor 指出恢复 IDENTITY 和修改种子/增量时的异常仍建议删列再建，与数据保留边界不一致。
修复仅调整两处异常文本及对应单元断言：要求审查数据保留迁移方案，并明确重建列不保留现有数据。
协调者确认相对 `afa7f971ae95267b37c4b5f24a823a0f1693c1d0` 没有 SQL、控制流或功能测试变化。

Sol Low 独立重新运行 unit：293/293，Completed，0 失败/跳过；TRX 为
`artifacts/query-translation/local-test/20261002T152423582Z-b09542b7/unit.trx`。
格式验证与 diff 检查再次通过，115 个源码/测试文件哈希在本轮验证前后不变。
证据保存在 `artifacts/stage3-independent/20261002-232415/`；真实库沿用上表结果。

## 验证边界

- 新增 32 个单元用例、15 个功能用例。DROP IDENTITY 来自真实 EF 模型差异，检查数据/主键保留、服务器定义移除自增、显式键成功、缺键/重复键失败及 Down 拒绝。
- 回读覆盖客户端单键/复合键、IDENTITY/Sequence；同步/异步 SaveChanges；Unicode、数值、NULL 默认值及虚拟计算列。断言 SaveChanges 返回时的实体和原始值，并用独立上下文核对持久化结果，不调用 Reload。
- 并发覆盖显式配置的 BEFORE UPDATE 行触发器递增标记、连续保存、陈旧标记和已删除行零更新。失败实体不得传播其他行生成值。外部事务中的插入/更新回读后回滚，由独立上下文确认数据库恢复。
- 测试对象使用唯一名称，在 finally 中按确切名称清理。触发器由测试创建；HasTrigger 不代表自动创建触发器。
- 新增幂等包装只有单元证据；admin 的 4 项通过是原有迁移脚本回归，不能视为新 DROP IDENTITY 幂等脚本/CLI 往返证明。
- specification 仅 4 项仓库自有冒烟，不是上游 EF 全量关系一致性测试；本轮未运行语义探针或 dotnet ef CLI，也未重新采集服务器 build/profile，不能扩展最低服务器版本声明。
- 未接入 W.DmProvider，未改变官方驱动同步回退/取消边界，未实现批量写入或 BulkCopy。
