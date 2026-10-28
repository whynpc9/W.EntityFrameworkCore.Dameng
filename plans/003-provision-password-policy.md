# 计划 003：保证本地初始化口令的必要字符类别

> 逐步实现并验证，只修改白名单。不要连接管理员数据库验证随机口令，不要重建现有测试空间或改写真实 secrets。不得自动提交、推送、创建 PR 或发布；索引由协调者维护。

## 状态与依赖

- 状态：DONE
- Milestone：M1 安全与验收基础
- 执行证据：[003](evidence/003.md)。
- Finding ID：F10
- 优先级：P2；工作量：S；修复风险：LOW
- 类别：dx / tests
- 依赖：无；本计划是 004 的前置条件，可与 001 并行，但与 002/004 对 Program.cs 的编辑须顺序合并。
- 规划基线：c80c788fb3cd34d97626d578db580264a28a11f3，2026-10-28。
- 仓库根目录：/Users/wanghongyi/Projects/dameng-entityframework-core

## 问题与目标

本地 provision 的口令生成器只固定一个大写字符，随机余位可能不含数字或小写字母，且整个字符集不包含符号。需要这些字符类别的实例会偶发或持续拒绝初始化。管理员功能夹具已经因真实失败修过同类问题，本地工具仍保留旧策略。

目标是保持 32 字符长度和密码学随机余位，确定性保证大写、小写、数字及允许的符号各至少一个，并用不接触数据库/真实 secrets 的 selftest 防止回退。这只满足明确的字符类别基线，不承诺适配所有定制服务器口令策略。

## 当前状态与模式

scripts/local-test/Program.cs:394–403：

    const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    Span<char> result = stackalloc char[32];
    result[0] = 'A';
    for (var index = 1; index < result.Length; index++)
    {
        result[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
    }
    return new string(result);

test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengScriptTestDatabase.cs:303–312 已显式保证四类字符，再随机填充剩余位置。docs/design-time-plan.md:179 记录过随机口令复杂度失败并修复管理员夹具的历史事实；该事实不证明本地当前实例的策略未改变。

scripts/local-test/Program.cs:24 将 selftest 直接路由到 TestLauncher.SelfTest；TestLauncher.cs:332–412 只用合成数据，失败抛异常、成功打印固定短消息，finally 删除自有临时目录。这是新增密码策略自检的模式。Program.cs:32–35 对 setup 错误只输出异常类型，不能为了排查生成器而打印生成值。

## 白名单和边界

仅修改：

- scripts/local-test/Program.cs
- scripts/local-test/README.md

不修改管理员夹具、产品代码、数据库策略、SDK/包版本/锁文件、工作流或真实 .local-test.secrets.json。不提取跨项目公共随机库，不改变既有用户密码，不运行 provision/inspect/grow/info 或管理员测试。现有 persistent 测试空间不能为本计划重建。

## Git 与漂移规则

分支：codex/003-provision-password-policy。开始运行：

    git status --short
    git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- scripts/local-test/Program.cs scripts/local-test/README.md

原始 SHA 是审查锚点。若 002 已验收并修改帮助信息，核对其差异、更新实施 SHA/摘录定位后继续，正常已归因的前置或并行整合不需要再次请求确认。只有不可归因改动、行为契约冲突或未完成合并才暂停协调。

## 分步实施及验证

### 1. 保留长度和随机性并补齐类别

NewPassword 继续产生 32 个字符。按管理员夹具已经采用的模式，显式保留大写、小写、数字、符号各一位，剩余至少 28 位使用 RandomNumberGenerator.GetInt32 从现有允许集合选择；可保持固定类别位置，不为此增加洗牌算法。符号选择与管理员夹具的已用安全字符一致，不引入双引号、单引号、分号、空白或反斜杠，也不通过降低服务端策略绕过限制。

加入 Program 私有的策略验证函数，验证长度、四类字符、允许字符集合；它只返回布尔值或抛出固定诊断，不返回/打印生成口令。若将生成值加入断言错误消息，会重新形成凭据输出风险，禁止这样做。

验证：

    git diff --check
    scripts/local-test/run.sh selftest

预期工具可编译、原有 TRX 自检继续通过；此时下一步的新密码自检也必须完成后才可报告计划通过。

### 2. 将确定性策略检查接到 selftest

在 Program 中新增 SelfTest 包装入口，先验证生成器再执行原 TestLauncher.SelfTest，保持两者都失败关闭。验证固定的合成有效/无数字/无小写/无大写/无符号/长度错误/包含不允许字符的候选；候选在代码内构造且永不输出。再调用真实生成器至少 1,000 次，逐个断言满足策略；不使用“所有值必须唯一”这类概率断言，也不把随机碰撞误判为漏洞。

包装入口不调用 LoadSecrets、FindReadySchema、Provision 或任何数据库 API；Program 原有根目录定位可以保留。selftest 完成应新增固定消息“Password policy self-test passed.”，再保留原“Synthetic TRX self-test passed.”。任意策略断言失败使进程退出非零。

验证：

    scripts/local-test/run.sh selftest

预期两个固定成功消息均出现、退出码零、没有任何生成口令、用户名或连接信息。该 selftest 不是 xUnit/TRX 宿主，不伪造 Completed 计数；其子断言必须全部执行且不能跳过。

### 3. 文档与完整离线回归

更新 README 的首次初始化说明和 selftest 范围：新生成值保证类别、已有账户不变、仍由服务器最终验证策略；运行 selftest 不需要真实 secrets。不要将此测试写成真实服务器接受性证据。

    scripts/local-test/run.sh selftest
    scripts/local-test/run.sh test unit
    scripts/local-test/run.sh test unit --driver w
    git diff --check
    git status --short

预期两个单元通道均非零、Completed、执行数等于总数且全部通过、无跳过；selftest 退出零且两个固定成功消息存在；仅白名单文件变化。两个单元通道不连接数据库。本计划不需要实库回归；后续如另有实库验收要求，官方/W 仍必须串行，且不能为此轮测试重新 provision。

## 测试矩阵及完成标准

| 场景 | 预期 |
| --- | --- |
| 缺任一必要字符类别 | 合成策略检查拒绝 |
| 长度错误/禁止字符 | 拒绝且错误信息不包含候选值 |
| 真实生成器 1,000 次 | 每个值满足 32 字符、四类及允许字符集合 |
| selftest 运行 | 无 secrets 读取，无数据库连接，无值输出 |
| 现有 TRX 自检 | 脱敏、终止、计数拒绝用例仍通过 |

- [x] NewPassword 确定性保证四类字符，至少 28 位仍用密码学随机生成。
- [x] selftest 的正/负候选和真实生成器循环全部执行；输出仅固定消息。
- [x] 两组单元 TRX 均非零、Completed、无失败/跳过。
- [x] 未改变任何现有账户、测试空间、secrets 或服务端策略。
- [x] 白名单检查通过，协调者收到证据后更新状态。

## STOP 与维护

如果修复要求重置测试用户、读取真实密码、改变服务端密码策略、使用不安全字符或新增依赖，停止并报告。若现有服务器有比本计划更严格的政策，记录未知边界，不尝试猜测通用密码规则。两个合理修复尝试后仍不能通过 selftest 也停止。

今后改动口令长度或允许集合时，必须同步调整正负合成候选并保留密码学随机余位。004 将运行本 selftest，不能把口令测试移入需要管理员连接的通道。

## 批次交付与统一证据

本批 M1 为起始批次，按正文的技术前置顺序执行。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/003.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为确认问题或验证缺口，须以“已修复并验证”闭合；补足有辨别力的测试证据也属于验证缺口的修复。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M1.md` 的输入。
