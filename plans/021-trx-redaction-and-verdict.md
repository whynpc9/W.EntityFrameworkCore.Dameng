# 计划 021：分离 TRX 验收语义与脱敏产物，并关闭不安全的失败回退

> 本文件保留原实施指引；实际修复、验证及合并结果见[实施证据](evidence/021.md)和下方状态。原执行者须先检查基线，再运行各步骤的验证命令；发生停止条件时报告具体原因，不修改真实配置或扩大范围。协调者维护 `plans/README.md`。

## 状态

- 状态：DONE；离线验收通过，已在 [PR #17](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/17) 合并。实际结果见 [021 证据](evidence/021.md)。
- 实施与验证记录见 [021 证据](evidence/021.md)。
- 发现：R01（损坏 TRX 的脱敏回退）与 R05（脱敏改变验收语义）。
- 优先级：P2；工作量：M；修改风险：MED，涉及验收判定与 TRX 结构保留，但无需数据库。
- 类别：security / dx / tests。
- 技术依赖：无；不要求真实数据库、管理员权限或其他新计划先完成。
- 规划基线：`5b1f6d407ac61acf7f5658addd551565aed34721`，2026-11-08。
- 仓库：`/Users/wanghongyi/Projects/dameng-entityframework-core`。
- 规划时本地 HEAD：`f626736a75dd1854a1210f90ed92c7b955cfb447`，落后所选基线 35 个提交。**不能在这个旧工作树上直接实施。** 本文代码摘录来自固定规划基线的 `git show`，不是旧工作树。

## 问题与目标

统一测试启动器先对 TRX 的全部属性脱敏，再重新读取脱敏文件判断测试是否通过。用户提供的合法连接字段若与 `Completed` 或计数重叠，脱敏会改变验收值，使完整通过的测试被错误拒绝。另一条失败路径在 XML 损坏时直接对原始文件做字面替换；含 XML 实体编码的秘密可能没有被替换，第二次写入失败还会静默保留原文件。

目标是先从原始、合法的 XML 提取并验证类型化验收数据，再独立生成安全产物；任何解析、脱敏或安全落盘失败都不能接受该轮测试。无法处理的原文不得作为“已安全脱敏”的诊断保存。此次审查没有证明真实凭据已经泄漏，现有离线 CI 也不含数据库凭据；不要收集历史真实秘密来验证本项。只有维护者独立证实历史泄漏时才安排对应凭据轮换。

## 当前状态与代码约定

以下定位均为规划基线，实施时重新核对行号和方法内容。

| 文件 | 角色 |
| --- | --- |
| `scripts/local-test/TestLauncher.cs` | 通道选择、子进程启动、TRX 脱敏与验收；本项唯一必需的实现修改位置 |
| `scripts/local-test/Program.cs` | 命令路由；`SelfTest()` 最后调用 `TestLauncher.SelfTest()`，无需为本项修改 |
| `scripts/local-test/run.sh` | 先锁定还原、构建 LocalTest，再执行其 DLL；不支持传入 `--no-build` 作为 selftest 选项 |
| `scripts/local-test/LocalTest.csproj` | SDK 风格控制台项目，默认自动编译同目录 `.cs`；必要的新内部 helper 不需要改项目文件 |
| `scripts/local-test/README.md` | 通道、安全产物及验收合同 |

`TestLauncher.cs:298–304` 当前会修改全部非命名空间属性：

```csharp
var document = XDocument.Load(path);
foreach (var element in document.Descendants())
{
    foreach (var attribute in element.Attributes())
    {
        if (!attribute.IsNamespaceDeclaration) attribute.Value = redact(attribute.Value);
    }
}
```

`:326–340` 在保存后从脱敏产物重新读取语义：

```csharp
using (var stream = new FileStream(tempPath, options))
{
    document.Save(stream);
    stream.Flush(flushToDisk: true);
}
File.Move(tempPath, path, overwrite: true);

var saved = XDocument.Load(path);
var summary = saved.Descendants()
    .FirstOrDefault(element => element.Name.LocalName == "ResultSummary");
var counters = summary?.Descendants()
    .FirstOrDefault(element => element.Name.LocalName == "Counters");
if (counters is null) return null;
var total = AttributeInt(counters, "total");
var executed = AttributeInt(counters, "executed");
```

`:350–355` 的不安全回退：

```csharp
catch
{
    // Preserve a safe diagnostic artifact even when the TRX is malformed.
    try
    {
        File.WriteAllText(path, redact(File.ReadAllText(path)));
```

`:373–376` 的验收条件必须保留，不可为使测试变绿而放宽：

```csharp
private static bool IsAccepted(int exitCode, TrxCounts counts)
    => exitCode == 0 && counts.Outcome == "Completed" && counts.Total > 0
        && counts.Total == counts.Executed && counts.Passed == counts.Executed
        && counts.Failed == 0 && counts.Skipped == 0;
```

`:616–647` 用忽略大小写的整词正则替换用户名，用字面正则替换连接串、主机和口令；这会命中合法结构值，也不会在原始 XML 中匹配对应的实体编码。正常路径解析 XML 后替换 `XText` 和属性的做法可以保留；XML 注释内容不会自动解码实体，本计划在安全产物中移除注释，不尝试保留注释中的任意原文。不要把“支持更多原始文本替换形式”作为损坏 XML 的可靠安全保证。

本仓库已有不访问数据库的自检模式，`:529–551` 创建合成 TRX、调用同一个生产方法、解析产物并抛固定安全异常；`:554–575` 检查 Aborted 和计数不完整必须拒绝，`:582–584` 在 `finally` 精确删除自有临时目录。沿用此模式，不把合成值或原始 XML 拼进异常消息。文件保存继续使用同目录唯一临时文件、`CreateNew`、0600、刷新到磁盘、原子替换；目录保持私有，不削弱权限。

## 命令与验证入口

在核对后的隔离实施工作树根目录运行下表命令。`scripts/local-test/run.sh` 已设置独立可写的 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`，并使用 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false`；build 还使用 `--disable-build-servers`。其 LocalTest 构建为 Debug，本项 selftest 不接受 `--configuration` 或 `--no-build`。不要重复手工构建来规避脚本错误。

| 目的 | 精确命令 | 成功预期 |
| --- | --- | --- |
| 确认包含规划基线 | `git merge-base --is-ancestor 5b1f6d407ac61acf7f5658addd551565aed34721 HEAD` | 退出 0；否则不得实施 |
| 工作区检查 | `git status --short --branch` | 位于隔离分支，能区分既有改动与本项改动，无未解决合并 |
| 作用域漂移 | `git diff --stat 5b1f6d407ac61acf7f5658addd551565aed34721..HEAD -- scripts/local-test/TestLauncher.cs scripts/local-test/TrxResultProcessor.cs scripts/local-test/TrxResultSelfTests.cs scripts/local-test/README.md` | 固定基线通常无差异；后继有差异则逐项核对本文摘录与归属 |
| 核心自检 | `env -u DAMENG_TEST_CONNECTION_STRING -u ConnectionStrings__DamengCliFixture scripts/local-test/run.sh selftest` | 退出 0；原有成功消息保留，新增本计划成功消息出现；不加载 secrets、不访问数据库 |
| 实际 TRX 集成 | `env -u DAMENG_TEST_CONNECTION_STRING -u ConnectionStrings__DamengCliFixture scripts/local-test/run.sh test offline-fixtures --configuration Release --filter FullyQualifiedName~DamengScriptExecutorTests` | Completed、总数非零、全执行通过、零失败/跳过；TRX 可解析且计数与控制台一致 |
| 补丁规范 | `git diff --check` | 退出 0 |
| 差异归属 | `git diff --name-only && git diff --cached --name-only && git ls-files --others --exclude-standard` | 未提交的新增/修改只在白名单；既有未跟踪文件保留，不纳入本项。已提交改动另对记录的实施起点检查，不能以空工作区代替 |

以上集成通道只用合成/离线夹具，选择脚本拆批类是为了用真实 xUnit/TRX 产物穿过启动器，不是数据库证据。不需要双驱动重复此完全共享的 TRX 处理流程，也不需要运行 `all`。

若确需直接调用 `dotnet` 进行获准的诊断，须先在任务临时目录设置上述三个环境变量，使用相同单节点旗标，build/test 加 `--disable-build-servers`；不清理全局 NuGet 缓存、不更改依赖锁、不关闭审计。常规实施全部使用表内包装脚本即可。

## 范围白名单

仅允许修改或新增：

- `scripts/local-test/TestLauncher.cs`。
- `scripts/local-test/TrxResultProcessor.cs`（可选；只承载本项内部 TRX 处理，禁止做通用文件框架）。
- `scripts/local-test/TrxResultSelfTests.cs`（可选；纯合成 selftest，必须由现有 `TestLauncher.SelfTest()` 调用）。
- `scripts/local-test/README.md`（只说明验收语义、安全产物和失败行为）。
- `plans/021-trx-redaction-and-verdict.md`（状态与核对结果）。
- `plans/evidence/021.md`（实施时新增，记录实际命令、结果、指纹与精确清理；本次规划不预建证据）。

禁止修改 `Program.cs` 的 provision、口令或 secrets 逻辑，禁止修改真实 `.local-test.secrets.json`、数据库、测试用户/表空间、公共提供程序、驱动桥接、CLI scaffold 辅助库、任何 CI/发布 workflow、SDK/包版本或锁文件。不要改通道、过滤条件、管理员连接选择、全量回归顺序；不为这两个缺陷扩大为日志系统重写。`plans/README.md` 由协调者维护。

## Git 工作方式

实施分支：`codex/021-trx-redaction`。在规划基线或已核对的后继提交建立隔离实施工作树；当前旧主工作树不切分支、不快进、不覆盖。开始前先执行上表的 ancestor、status 和漂移检查。若后继修改可明确归因且已修其中一个问题，先告知协调者并缩小剩余范围；不能机械重做。

本计划不授权自动提交、推送、创建 PR 或发布。需要提交时遵循操作者当次授权，使用仓库中文提交说明风格；例如“修正 TRX 脱敏与验收语义”。保留既有未提交内容，不执行 reset/clean。

## 实施步骤

### 1. 建立隔离基线与可辨别的合成回归

完成 Git 前置检查后，先运行一次核心 selftest，记录原有成功摘要。扩展现有 selftest（或新增白名单内的 selftest helper），只在新建私有临时目录生成 XML 和合成连接字段。不要读取真实 secrets，也不要读取父进程连接值来造测试。

先增加两个回归：合法 TRX 的结构状态与合成用户名碰撞；包含实体编码秘密的截断 TRX。第一个用例须证明原实现错误拒绝本应通过的结果；第二个须证明原实现的产物仍含合成秘密的编码形式。所有断言错误只给用例名/固定消息，不打印合成哨兵。每个用例单独拥有文件，`finally` 清理，不能让第一项失败阻断其余用例的清理。

现有 Program.Main 只输出异常类型，selftest 默认首异常即退出，因此本项新增用例组须在 selftest 内逐例捕获失败，只输出预定义用例 ID 与固定失败分类，完成全部用例和清理后统一抛出固定异常。这样一次红灯运行可分别记录两项回归，且不必修改 Program.cs 或输出原始断言消息。

**验证**：先执行一次表内核心 selftest，预期旧自检退出 0。加入两个回归后再次执行，预期因明确的新断言退出非零；分别记录已命中的两个回归，不把此阶段红灯称为最终失败或通过。不得为获得红灯制造数据库或文件系统全局故障。

### 2. 在脱敏前解析并验证类型化验收数据

拆清 `ReadAndRedactTrx` 的阶段：读取/解析 → 提取并验证结果 → 生成脱敏文档 → 安全落盘 → 返回可验收结果。计数必须从首次解析的 XML 中获取，不从脱敏后的字符串/文件回读。结果只保留整数和允许的标准状态（或内部枚举），不将未知原始 outcome 内容带入控制台或异常。

保持 `IsAccepted` 的全部条件；必要字段缺失、非数字/负数/溢出、非法状态、重复且有歧义的摘要/计数结构应判为无效，不能通过默认零值把错误计数变成成功。`failed/error/timeout/aborted` 与跳过项相加也要防溢出；支持正常 TRX 中可缺省的非核心计数字段，但要明确区分“缺省的可选字段为零”和“字段存在却不可解析”。

安全产物仍应是下游可解析的 TRX。依据当前 SDK 的实际离线 TRX，在 `XDocument` 内建立小型、按结构位置匹配的字段表：摘要和每条 `UnitTestResult` 的标准 outcome、Counters 的非负整数、运行/测试/执行及关联字段中的 GUID、Times 与结果起止时间、duration。对已识别字段先按相应类型校验，再用不变文化和该字段格式规范化重写；GUID 的定义与引用必须保持一致。字段若存在却非法，转为固定失败诊断。只有原合同必需的字段缺失才拒绝；不能强迫现有精简合成夹具提供可选的结果/定义/时间字段。不得用全局属性名白名单放行任意字符串，也不必建设通用 TRX schema 引擎。

其余属性、诊断文本继续脱敏，包括 name、runUser、computerName、testName 等自由文本；移除 XML 注释与处理指令，不保留其中原文。状态词、数字或 GUID/时间片段恰好与合成秘密相同，不意味着应删掉其可信结构含义；测试须在诊断区域断言秘密消失，并独立断言标准字段的值、类型和关联正确，不能要求整个文档绝不出现相同的公共数字或标准结构值。

只有安全产物成功写入并替换原文件后才返回可被 `IsAccepted` 接受的计数；若持久化失败，即便原始计数全绿，本轮仍失败。不要将原始 XML 或原始异常信息公开给调用者。

**验证**：执行核心 selftest。摘要/逐条结果状态、计数、GUID、时间和 duration 碰撞及原有成功/Aborted/部分执行用例均应符合预期；有效文档重读后字段仍可按其类型解析，定义与引用关联、结果数量保持一致。仅 `XDocument.Load` 成功不足以证明产物可用。若步骤 1 的截断回归尚待步骤 3 修复，必须单独记录该唯一已知红灯，不把其他失败解释为预期。

### 3. 对非法 XML 和落盘失败执行安全关闭

删除 `redact(File.ReadAllText(path))` 形式的原文回退。无法解析的文件不继续尝试猜测 XML 实体或文本片段；生成固定、不含原文的 XML 诊断，明确该轮不能验收，不能伪造 Completed/非零通过计数。

正常安全文档和固定失败诊断都复用当前同目录唯一临时文件、0600、flush、原子替换方式。替换失败时尝试精确删除本轮原始 TRX 和本轮自有临时文件。若删除也失败，返回明确“不安全产物未清理”的状态并输出固定安全诊断与本轮文件路径；保持整体非零，不能继续打印“安全 TRX”或把该文件作为可验收产物。不要吞掉此状态，不改目录以外的文件，不扩大权限或删除整个结果根目录。诊断可列路径，不能包含原始文件内容、原始异常消息或连接值。

为可重复验证，采用最小、局部传入的内部 I/O 委托或等效故障缝隙，模拟临时写入、原子替换和删除失败；不要使用全局可变故障开关，也不要依赖 `chmod` 在特权进程下必然失败。真实正常文件路径仍用实际临时文件，故障用例只操作自身合成文件。

**验证**：执行核心 selftest，预期退出 0；截断/非法 XML 用例产物只含固定安全诊断，不能验收；写入/替换失败不能返回绿色结果；删除失败有明确不安全状态且非绿色，解除注入后 `finally` 能清理全部自有文件。新增固定摘要 `TRX verdict and redaction self-test passed.`，保留原有 `Synthetic TRX self-test passed.`。

### 4. 用实际离线 TRX 验证集成并更新文档

更新 README，说明验收数据来自原始结构的类型化校验，产物另行脱敏，任一安全处理失败都拒绝验收；损坏文档只有固定诊断可分享，无法删除的原始文件明确不安全，不能将目录私有等同于脱敏成功。不要写“已发生泄漏”或“已获得真实数据库验证”。

用表内离线命令产生的真实 SDK TRX 补充结构兼容性检查：仅在自有临时目录复制这份无数据库秘密的产物，插入合成诊断及碰撞字段，再通过同一生产处理函数。覆盖逐条 Passed 状态，以及与 GUID、时间和 duration 片段碰撞的短数字。处理后检查已识别字段的类型、定义/执行关联、结果/定义数量及每条状态；记录本次 SDK 实际出现的字段表。可在白名单 selftest helper 中嵌入去除自由文本的最小结构样本以固定回归，不能增加新的 CLI 入口或读取历史数据库 TRX。

**验证**：依次执行核心 selftest、表内实际 TRX 集成命令、`git diff --check` 和差异归属命令。全部退出 0；实际集成 TRX 为合法 XML，Completed/非零/全执行通过/零失败跳过，控制台计数与文件一致；新 selftest 成功消息出现，原消息保留。只在改动或新失败需要时重跑，不为本项启动数据库通道。

在 `plans/evidence/021.md` 记录实施 SHA（未提交时补白名单文件指纹）、两项旧行为红灯与最终绿灯、实际命令/退出码、自检用例名、离线 TRX 路径、清理结论。日志不复制合成秘密、原始 XML、环境或任何真实配置。交协调者更新索引。

## 必需测试矩阵

测试放在现有 `TestLauncher.SelfTest()` 或白名单 selftest helper 中，通过同一个生产处理函数执行。沿用固定安全异常、合成输入和 `finally` 精确清理模式；不能只测试重新实现的一份算法。

| 用例 | 必需断言 |
| --- | --- |
| 有效完整通过 TRX | 退出零时接受；保存的 XML 可读，控制台/产物计数一致 |
| 合成用户名与 Completed 忽略大小写碰撞 | 验收仍正确；诊断中的用户名被隐藏，结构状态保留标准值 |
| 合成用户名与逐条 Passed 状态碰撞 | UnitTestResult 的合法状态仍为 Passed，关联与结果数量不变；自由文本中的哨兵脱敏 |
| 合成秘密与通过/失败/跳过数字重叠 | 完整通过仍通过，失败/跳过仍拒绝；数字不经字符串脱敏参与验收 |
| 短数字与 GUID、时间、duration 片段重叠 | 字段仍按其标准类型解析、GUID 定义和引用一致；来自真实 SDK 结构的样本通过同一生产路径 |
| 状态为 Aborted/Failed、未知状态或缺状态 | 均拒绝；未知原始状态不输出 |
| 缺核心计数、负数、非法数字、溢出、加法溢出 | 拒绝；不可因默认零值或整数绕回而通过 |
| 总数零、未执行完、执行数与通过数不等、非零进程退出 | 拒绝，原验收规则全部保留 |
| 多份有歧义的 ResultSummary/Counters | 拒绝，不能任取首个看似绿色的结果 |
| 合法 XML 中的实体编码秘密 | 属性、文本中的解码值脱敏；注释和处理指令直接移除，保存后无对应诊断泄漏；结构碰撞例外按步骤 2 判断 |
| 含实体编码秘密的截断/非法 XML | 固定安全诊断，无原文；拒绝验收 |
| 缺失 TRX | 拒绝，不生成伪通过记录 |
| 临时文件写入或原子替换失败 | 拒绝；能清理时原始/临时文件均消失，不宣称安全产物已保存 |
| 替换失败且删除原始文件也失败 | 返回可辨认的不安全残留状态，只输出固定诊断和自有路径；解除注入后清理 |
| Unix 权限 | 正常产物0600、私有测试目录0700；Windows跳过Unix位检查，不跳过核心语义 |

## 完成标准

全部满足才可向协调者报告 DONE：

- [x] 基线祖先检查退出 0，实施工作树不是规划时的旧 HEAD；漂移检查已记录。
- [x] `ReadAndRedactTrx` 不再从脱敏文件读取验收数据，不再使用原文替换作为损坏 XML 回退。
- [x] 必需矩阵全部通过同一生产路径，核心 selftest 退出 0，包含固定新摘要和全部原有摘要。
- [x] 非法 XML、写入/替换/删除失败用例不能返回可接受结果；残留不能被宣称为安全产物。
- [x] 实际离线 TRX 集成命令退出 0，非零、Completed、零失败/跳过；产物计数一致，逐条状态、GUID 关联、时间与 duration 的类型及结构碰撞检查通过。
- [x] `git diff --check` 退出 0，本项差异只在白名单，未读取/修改真实 secrets、未运行数据库测试。
- [x] `plans/evidence/021.md` 包含可核对的 SHA/指纹、命令、结果、用例与清理证据；本计划状态由实际证据更新，索引交协调者处理。

可用 `rg -n -g 'TestLauncher.cs' -g 'TrxResult*.cs' 'ReadAllText\(path\)|XDocument.Load\(path\)|TRX verdict and redaction self-test passed' scripts/local-test` 定位复核：`ReadAllText(path)` 若仍出现，只能属于明确的合成产物断言，不能位于生产 fallback；重复加载合法产物可以用于保存校验，但不得重新决定计数。这是定位辅助，最终完成标准由合成故障断言和实际集成 TRX 共同证明。

## 停止条件

- ancestor 检查非零、存在未解决合并，或后继代码已改变处理/验收合同且无法解释。
- 需要修改白名单外文件、读取真实连接、调用数据库、改变依赖或降低既有验收条件才能继续。
- 无法区分安全产物与未清理原始产物，或实现会在保存失败后返回通过。
- 新设计要求输出原始 XML/原始异常/秘密以定位错误，或以增加目录权限代替安全处理。
- 相同验证失败经一次合理修正后仍失败；先报告具体用例和非敏感错误类型，不无界重试。
- 锁定还原被外部网络/审计服务阻断；报告环境限制，不清全局缓存、不关闭审计、不伪造绿灯。

## 维护注意事项

以后增加 TRX 字段或更换测试平台时，先扩展结构校验和可选字段契约，再更新产物序列化；不要再次从展示用脱敏字符串推导验收结果。审查应特别检查：未知字符串是否能进入日志、数字求和是否溢出、故障注入是否使用生产路径、临时文件是否都归属本轮，以及成功摘要是否只有在全部自检完成后打印。

本项不扩展连接串别名识别、不重写 CLI harness 的脱敏器、不增加整体日志上传流程，也不调查历史凭据。上述范围如出现独立证据，由协调者另行决定。
