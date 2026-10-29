# 改进审查与修复计划

- [六个里程碑及验收门槛](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/milestones.md)：从这里安排批次、依赖和验收。
- [2026-10-28 深度审查](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/deep-audit-2026-10-28.md)：原始发现、证据、置信度及排除项。
- 原始审查基线：`c80c788fb3cd34d97626d578db580264a28a11f3`。
- 当前范围：按用户后续要求，将全部 **14 项问题/验证缺口 + 6 项调查**拆为 **20 份独立计划、6 个里程碑**。先前“本次只保留审查报告”的选择已由本次制定计划请求更新。
- 当前进度：M1（001–004）与 M2（005–008）已合并（PR #9/#10）；M2 合并为 `c3ca2fb53b783c271dd89ef8ce460f96f5e85661`，官方参数已知风险保留。M3（009–011）功能、本地与实施提交远端CI/Codex及线程已闭环（PR #11），已合并为 `eb79cc8fb8b91e8cb2d6fb4ede255c61759b4056`；M4（012→013）功能、本地及实施提交远端门完成，PR #12，M5功能/验收DONE（PR #13，最后状态文档自身门后合并），M6已启动，独立PK=1及单点W夹具修正已完成；最终all与020收口中；M1记录见[M1执行证据](evidence/M1.md)，M2提交/PR/验证与风险接受决定见[M2执行证据](evidence/M2.md)。
- D01–D03 为未来方向，单独保留在路线图中，不计入缺陷批次或验收分母。

## 批次

| 里程碑 | 目标 | 数量 | 问题编号 | 状态 |
| --- | --- | ---: | --- | --- |
| M1 | 安全与验收基础 | 4 | F01、F08、F10、F05 | DONE |
| M2 | 查询与时间正确性 | 4 | F02、I01、F06、I03 | DONE |
| M3 | 驱动与连接生命周期 | 3 | F03、F09、F12 | DONE |
| M4 | 模型一致性 | 2 | I02、I06 | DONE |
| M5 | 迁移与资源恢复 | 4 | F11、F04、F07、F13 | DONE（功能/验收） |
| M6 | 目录、独立 profile 与文档收口 | 3 | I04、I05、F14 | IN_PROGRESS |

默认 M1 → M2 → M3 → M4 → M5 → M6，每个里程碑完成一个批次。表内是技术前置，跨批次还必须通过前一里程碑验收门。可提前准备环境或只读表征，不越过批次验收。

## 独立计划

每份计划包含当前代码证据、修改白名单、步骤、测试矩阵、完成标准与停止条件。编号用于稳定引用，原始 F/I 编号保留。

| 批次 | 实施计划 | 原发现 | 技术前置 | 实施状态 |
| --- | --- | --- | --- | --- |
| M1 | [001 · CLI 凭据边界](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/001-cli-secret-boundary.md) | F01 | — | DONE |
| M1 | [002 · CLI 被测程序集身份](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/002-cli-assembly-identity.md) | F08 | 001 | DONE |
| M1 | [003 · 初始化口令字符策略](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/003-provision-password-policy.md) | F10 | — | DONE |
| M1 | [004 · 无秘密的离线 CI](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/004-offline-ci.md) | F05 | 001、002、003 | DONE |
| M2 | [005 · Replace 空替换值语义](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/005-replace-null-semantics.md) | F02 | 004 | DONE（本地） |
| M2 | [006 · 时间字面量精度](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/006-temporal-literal-fidelity.md) | I01 | 004 | DONE（本地，官方风险已接受） |
| M2 | [007 · LOB 查询守卫完整性](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/007-lob-query-guards.md) | F06 | 004 | DONE（本地） |
| M2 | [008 · 转换器后的时间翻译](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/008-converted-temporal-translation.md) | I03 | 004、006 | DONE |
| M3 | [009 · 驱动一致性守卫](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/009-driver-invariants.md) | F03 | 004；M2 验收 | DONE |
| M3 | [010 · 会话淘汰证据](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/010-session-eviction-evidence.md) | F09 | 009 | DONE |
| M3 | [011 · HasTables 异步、超时和诊断](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/011-has-tables-async.md) | F12 | 009、010 | DONE |
| M4 | [012 · 共享列值生成一致性](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/012-shared-column-generation.md) | I02 | 009 | DONE |
| M4 | [013 · 复杂属性中的 rowversion 校验](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/013-complex-rowversion-validation.md) | I06 | 012 | DONE |
| M5 | [014 · 管理员 DDL 不确定结果恢复](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/014-admin-ddl-recovery.md) | F11 | 003、010 | DONE（本地） |
| M5 | [015 · 纯注释迁移](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/015-comment-only-migrations.md) | F04 | 012、014 | DONE（本地） |
| M5 | [016 · 历史表脚本定位](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/016-history-script-tokenization.md) | F07 | 014、015 | DONE（本地） |
| M5 | [017 · 迁移锁真实争用证据](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/017-migration-lock-evidence.md) | F13 | 011、014、016 | DONE（本地） |
| M6 | [018 · 目录列完整性](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/018-catalog-column-completeness.md) | I04 | 014 | DONE；[证据](evidence/018.md) |
| M6 | [019 · LOB 表的主键聚集策略 profile](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/019-lob-primary-key-profile.md) | I05 | 012、014、015 | DONE（限定profile的REFUTED）；[证据](evidence/019.md) |
| M6 | [020 · 能力文档与权限示例收口](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/020-documentation-reconciliation.md) | F14 | 001–019 结论闭合 | DONE（本地独立复核闭合）；[证据](evidence/020.md) |

## 状态与证据

- `TODO` 尚未实施；`IN_PROGRESS` 实施或验证中；`BLOCKED` 缺少必需环境、权限、证据或有未解决冲突；`DONE` 完成全部适用验收。
- F 项以“已修复并验证”闭合，测试缺口包括补足具有辨别力的证据。I 项先表征，证实后修复，也可通过充分、可重复证据“确证无缺陷”。缺环境、跳过或单次未复现不能算完成。
- 每项完成后新增 `plans/evidence/NNN.md`，每批新增 `plans/evidence/Mn.md`，记录 SHA、命令、通过/失败/跳过、TRX/日志位置、结论与精确清理结果。M1 已形成 [001](evidence/001.md)、[002](evidence/002.md)、[003](evidence/003.md)、[004](evidence/004.md) 及 [M1](evidence/M1.md) 记录；后续批次不预建验收记录。
- 协调者统一更新状态和证据链接；执行者不并发改索引。原始报告保持历史证据属性，新结论写入实施证据。
- 已考虑并排除项继续保留在[审查报告](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/deep-audit-2026-10-28.md)，避免将已决定的设计边界重复报为缺陷。

本轮M2证据：[005](evidence/005.md)、[006](evidence/006.md)、[007](evidence/007.md)、[008](evidence/008.md)及[M2汇总](evidence/M2.md)。005–008已按维护者接受官方参数已知风险后的边界合并PR #10；历史验证与风险决定原文保留。

M3本地证据：[009](evidence/009.md)、[010](evidence/010.md)、[011](evidence/011.md)及[M3汇总](evidence/M3.md)。先前失败/BLOCKED历史保留；当前9fe源码指纹的实库57/57、Release四项4/4、屏障Debug/Release各14/14及精确清理通过；实施提交远端CI/Codex及P2线程闭环，M3功能/验收DONE，已合并 PR #11 为 `eb79cc8fb8b91e8cb2d6fb4ede255c61759b4056`；M4 按本轮授权执行。

M4 证据：[012](evidence/012.md)、[013](evidence/013.md)、[M4 汇总](evidence/M4.md)。两项均确认模型可达缺口后修复；保留合法共享列和复杂属性对照。指定Astra独立本地审查P2修复闭环；实施提交 `485b5c4` 远端CI/Codex及线程验收通过，M4功能/验收DONE。最后状态文档同样执行自身CI/复审门，合并SHA与主分支CI见PR #12及M4归档；不启动M5。

M5 按 014→015→016→017 实施，当前功能/验收DONE，基线 `900f37aaa93192386e2bd72a95194fe8cd8168c8`（远端 main 一致）。只处理 F11/F04/F07/F13，不启动 M6。正式证据：[014](evidence/014.md)、[015](evidence/015.md)、[016](evidence/016.md)、[017](evidence/017.md)、[M5汇总](evidence/M5.md)。首次阶段015–016完成、014待管理员重测及017目录权限阻断的历史保留；最新014最终源码双驱动管理员门、017官方7/W8及联合交叉均已补齐，等待最新HEAD远端门。

2026-10-29按维护者明确授权创建隔离普通账号并授予精确视图SELECT后，014最终管理员双驱动4/11、017官方7/W8、联合迁移交叉各21通过；临时账号/空间精确清理，96份记录均cleaned。此前BLOCKED为历史，最新本地门已补齐，远端见[M5汇总](evidence/M5.md)。不启动M6。

M5远端收口：恢复证据提交2e0907d自身CI/Codex无未解决意见，线程0；最后状态文档仍要求自身同样门后按既有授权合并PR #13。全部实际SHA/主分支CI见[M5记录](evidence/M5.md)及PR，不启动M6。

M6 从 M5 合并 SHA `195ffe6f0f832822c0d864ff108e474acc940d57` 开始；018 修复、目录对照及独立复核 DONE，019已完成普通各27/独立PK=1各29实证；最终W全量三项夹具诊断仍失败，020未启动最终对账。详见 [M6](evidence/M6.md)。旧段“不启动 M6”保留为 M5 阶段历史，当前状态以本段为准。

M6最新：产品7efb38e同revision官方all973/402/4/11、W all128/489/4/11和独立Release各29全部通过，全部资源精确清理。019本地DONE（服务器安全处理的REFUTED），020本地20项唯一归属/措辞/链接/依赖检查全通过，待独立差量复核及最终文档提交自身CI/Codex/线程门。此前BLOCKED阶段均保留为历史。

020本地20项对账与指定Astra独立差量复核已完成，0项发现；M6目前仅待最后文档提交自身远端门和按授权合并，产品/测试SHA及全部实际TRX见[M6](evidence/M6.md)。
