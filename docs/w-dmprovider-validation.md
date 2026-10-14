# W.DamengProvider 接入验证记录

本文按[实施计划](w-dmprovider-integration-plan.md)任务顺序记录实际执行的验证证据。区分离线单元证据与真实服务器行为证据；未执行或缺环境的门标为 pending，不计为通过。支持范围以[兼容矩阵](compatibility.md)为准。

## T00 基线与候选冻结（2026-10-14）

### 候选身份

身份清单固定在 [`scripts/driver-candidate/w-damengprovider.manifest.json`](../scripts/driver-candidate/w-damengprovider.manifest.json)，核验脚本为 [`scripts/driver-candidate/verify-package.py`](../scripts/driver-candidate/verify-package.py)（只读文件，不加载或执行程序集）。

| 项 | 值 | 核验方式 |
| --- | --- | --- |
| 包 | `W.DamengProvider 0.1.0-preview.1`，约束 `[0.1.0-preview.1]` | 从 NuGet.org 精确版本 URL 下载 |
| 包 SHA-256 | `03e7f9161b2d120e29f8e267d25dc62cef3e4f94ba8c032cb774449dcfec07ba` | 脚本比对，与计划 §1.1 一致 |
| NuGet contentHash | `Gah+YlqywSqIhj9qvnQ2LYDADCJFdSJzYu6isg4P2XqsLARos9813Qa/D9hifhNAvvgz/f5uoyEgddLN3Ud+EQ==` | 读自还原目录 `.nupkg.metadata`；签名包的 contentHash 不含签名条目，因此 pin 在 manifest 中，字节一致性仍由包 SHA-256 保证 |
| DLL SHA-256 / MVID | `a265cd96…ab1d` / `d1773de0-4205-48eb-84a6-0708c9c4aec4` | 脚本从 PE 元数据静态读取 MVID |
| 来源 | nuspec repository commit 与 tag `v0.1.0-preview.1` 均为 `a67a9298…`；无运行时 NuGet 依赖 | 脚本读 nuspec |
| 上游漂移 | main `b5786d5`，相对 tag 的 `src`/`tests` 无差异 | `git diff --stat` |

负例：对包末尾追加 1 字节后，脚本报告 SHA-256 不符并以退出码 1 结束；T03 之前没有锁文件引用该包，脚本不因此报错；加 `--require-lock-reference` 时缺少引用即失败。T03 引入依赖后，脚本必须在全部锁文件上通过（见 T03 记录）。

### 环境

| 项 | 值 |
| --- | --- |
| 本仓库基线 | `6238792`（相对 `5863d37` 仅新增计划文档） |
| SDK / runtime | `10.0.401` / `Microsoft.NETCore.App 10.0.12` |
| 锁定包 | `Microsoft.EntityFrameworkCore.Relational 10.0.12`、`DM.DmProvider 8.3.1.47463` |
| 服务器 profile | 自报 `8.1.5.60`，`GLOBAL_CHARSET=1`（UTF-8），`COMPATIBLE_MODE=0`，页大小 32 KB；W 连接需 `transport_security=PlaintextAllowed` |
| 不具备的 profile | TLS（W 文档中的 `8.1.4.6 / GB18030 / TLS`）、其他服务器版本：相关门 pending |

### 官方基线运行

在 `6238792` 的独立 worktree 中运行 `scripts/local-test/run.sh test all`（运行 ID `20261014T072339444Z-857b6388`）：

| 通道 | 总数 | 通过 | 失败 | 跳过 | 结论 |
| --- | --- | --- | --- | --- | --- |
| unit | 688 | 688 | 0 | 0 | 通过 |
| functional | 233 | 232 | 1 | 0 | 本次运行未被接受（见下） |
| specification | 4 | 4 | 0 | 0 | 通过（冒烟） |
| admin | 7 | 7 | 0 | 0 | 通过 |

失败用例 `DamengReverseEngineeringFunctionalTests.LocalSequenceAfterSetSchemaRecreatesAgainstTheCatalogSchema` 在测试准备 SQL 中收到官方驱动 `DmException 6001, Communication error`，与断言无关。在同一 worktree 单独重跑（运行 ID `20261014T073151199Z-d6875dd0`）1/1 通过。记为官方驱动偶发通信错误，基线功能通道按“233 项，其中 1 项偶发”冻结；后续官方回归若再现同一错误，需单独重跑并记录，不因此修改测试。

完整用例清单（934 行）保存在 [`w-dmprovider-evidence/baseline-tests-6238792.txt`](w-dmprovider-evidence/baseline-tests-6238792.txt)，T04/T06 据此逐项映射 W 通道结果。探针通道（`probes`）不属于 `all`，也不作为基线证据。

### §2.4 / §6 API 复核

- W 时间参数合同与计划 §2.4 一致：非零 `Precision` 一律 `NotSupportedException`（INTERVAL 也适用），小数秒只看 `Scale`；显式 `DbType.DateTime2` 默认七位，显式 `DbType.DateTimeOffset` 默认六位。因此 W 分支必须对所有时间参数清零 Precision 并显式设置 Scale。
- W `GetFieldValue<DateTimeOffset>` 返回带偏移的原值，`GetFieldValue<TimeSpan>` 对 INTERVAL 调用精确转换；W 分支对这两类列使用 typed getter。
- `DmTransaction.SupportsSavepoints` 仅在服务器自报 `8.1.5.60` 时为 true；本地 profile 满足。
- §6 依赖的 `DmDataSource`（`CreateConnection`/`OpenConnectionAsync`/`ClearPool`）与 Reader 的 `GetStream`/`GetTextReader` 在 preview.1 中存在，没有新增需要改变 T08/T09 范围的设施。
