# 改进审查与修复计划

- [六个里程碑及验收门槛](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/milestones.md)：从这里安排批次、依赖和验收。
- [2026-10-28 深度审查](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/deep-audit-2026-10-28.md)：原始发现、证据、置信度及排除项。
- 原始审查基线：`c80c788fb3cd34d97626d578db580264a28a11f3`。
- 当前范围：按用户后续要求，将全部 **14 项问题/验证缺口 + 6 项调查**拆为 **20 份独立计划、6 个里程碑**。先前“本次只保留审查报告”的选择已由本次制定计划请求更新。
- 当前进度：M1（001–004）已完成，本地/远端验收与 review 均闭环；M2 正在按维护者接受官方驱动风险后的边界收口（不扩展驱动修复），M3–M6 保持 TODO；本批提交、PR、合并和验证结果见 [M1 执行证据](evidence/M1.md)。
- D01–D03 为未来方向，单独保留在路线图中，不计入缺陷批次或验收分母。

## 批次

| 里程碑 | 目标 | 数量 | 问题编号 | 状态 |
| --- | --- | ---: | --- | --- |
| M1 | 安全与验收基础 | 4 | F01、F08、F10、F05 | DONE |
| M2 | 查询与时间正确性 | 4 | F02、I01、F06、I03 | IN_PROGRESS |
| M3 | 驱动与连接生命周期 | 3 | F03、F09、F12 | TODO |
| M4 | 模型一致性 | 2 | I02、I06 | TODO |
| M5 | 迁移与资源恢复 | 4 | F11、F04、F07、F13 | TODO |
| M6 | 目录、独立 profile 与文档收口 | 3 | I04、I05、F14 | TODO |

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
| M2 | [008 · 转换器后的时间翻译](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/008-converted-temporal-translation.md) | I03 | 004、006 | DONE（本地） |
| M3 | [009 · 驱动一致性守卫](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/009-driver-invariants.md) | F03 | 004；M2 验收 | TODO |
| M3 | [010 · 会话淘汰证据](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/010-session-eviction-evidence.md) | F09 | 009 | TODO |
| M3 | [011 · HasTables 异步、超时和诊断](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/011-has-tables-async.md) | F12 | 009、010 | TODO |
| M4 | [012 · 共享列值生成一致性](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/012-shared-column-generation.md) | I02 | 009 | TODO |
| M4 | [013 · 复杂属性中的 rowversion 校验](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/013-complex-rowversion-validation.md) | I06 | 012 | TODO |
| M5 | [014 · 管理员 DDL 不确定结果恢复](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/014-admin-ddl-recovery.md) | F11 | 003、010 | TODO |
| M5 | [015 · 纯注释迁移](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/015-comment-only-migrations.md) | F04 | 012、014 | TODO |
| M5 | [016 · 历史表脚本定位](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/016-history-script-tokenization.md) | F07 | 014、015 | TODO |
| M5 | [017 · 迁移锁真实争用证据](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/017-migration-lock-evidence.md) | F13 | 011、014、016 | TODO |
| M6 | [018 · 目录列完整性](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/018-catalog-column-completeness.md) | I04 | 014 | TODO |
| M6 | [019 · LOB 表的主键聚集策略 profile](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/019-lob-primary-key-profile.md) | I05 | 012、014、015 | TODO |
| M6 | [020 · 能力文档与权限示例收口](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/020-documentation-reconciliation.md) | F14 | 001–019 结论闭合 | TODO |

## 状态与证据

- `TODO` 尚未实施；`IN_PROGRESS` 实施或验证中；`BLOCKED` 缺少必需环境、权限、证据或有未解决冲突；`DONE` 完成全部适用验收。
- F 项以“已修复并验证”闭合，测试缺口包括补足具有辨别力的证据。I 项先表征，证实后修复，也可通过充分、可重复证据“确证无缺陷”。缺环境、跳过或单次未复现不能算完成。
- 每项完成后新增 `plans/evidence/NNN.md`，每批新增 `plans/evidence/Mn.md`，记录 SHA、命令、通过/失败/跳过、TRX/日志位置、结论与精确清理结果。M1 已形成 [001](evidence/001.md)、[002](evidence/002.md)、[003](evidence/003.md)、[004](evidence/004.md) 及 [M1](evidence/M1.md) 记录；后续批次不预建验收记录。
- 协调者统一更新状态和证据链接；执行者不并发改索引。原始报告保持历史证据属性，新结论写入实施证据。
- 已考虑并排除项继续保留在[审查报告](/Users/wanghongyi/Projects/dameng-entityframework-core/plans/deep-audit-2026-10-28.md)，避免将已决定的设计边界重复报为缺陷。

本轮M2证据：[005](evidence/005.md)、[006](evidence/006.md)、[007](evidence/007.md)、[008](evidence/008.md)及[M2汇总](evidence/M2.md)。005/007/008已完成本地验收及独立review；006官方参数问题已获维护者接受为已知风险，整批正按修订边界复验并推进远端整合。
