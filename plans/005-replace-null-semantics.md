# 计划 005：修正字符串 Replace 的 null 替换值语义

## 状态与执行约定

- 状态：DONE（本地验收）。
- 里程碑：M2，查询与时间正确性。
- 对应发现：F02；优先级 P1；工作量 M；修复风险 MED；静态证据置信度高，最小执行复现尚未完成。
- 依赖：[004-offline-ci.md](004-offline-ci.md) 完成。它提供无需真实连接的离线验证入口；本计划的固定过滤命令仍使用现有 `unit` / `functional` 通道。
- 编写基线：`c80c788fb3cd34d97626d578db580264a28a11f3`，2026-10-28。
- 工作目录：`/Users/wanghongyi/Projects/dameng-entityframework-core`。实施者须按顺序执行步骤、记录实际命令/结果和最终提交身份。不要自动提交、推送或创建 PR；索引由协调者维护，完成后报告本计划状态及证据路径。

## 问题与目标

`string.Replace(oldValue, null)` 的 CLR 含义是删除 `oldValue`，不是产生 null。当前翻译器把替换值直接送入 `REPLACE`，并声明三个参数均传播 null。EF 根据这个声明优化函数的 null 检查，因此必填字符串上的 `Name.Replace("a", (string?)null) == null` 可能被折叠为 true，选中错误的行。

本计划先固定失败用例，再修正替换值归一化和传播声明，确保谓词、否定和投影一致。范围只覆盖既有 `Replace(string, string?)` 重载；不扩展 `StringComparison`、文化比较或其他字符串 API。真实服务器结果和 SQL/表达式单元证据分别记录。

## 当前实现与应遵循的模式

`src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengStringMethodTranslator.cs:188–199`：

```csharp
if (method == Replace)
{
    return Function(
        "REPLACE",
        [
            instance,
            ApplyStringMapping(arguments[0], instance.TypeMapping),
            ApplyStringMapping(arguments[1], instance.TypeMapping)
        ],
        typeof(string),
        instance.TypeMapping,
        [true, true, true]);
}
```

`DamengParameterBasedSqlProcessor` 先调用 EF 的空值处理，再调用 `DamengLobQueryRewriter`。因此不能期望后面的 LOB visitor 撤销前面已经错误折叠的谓词。锁定 EF 10.0.12 的 [ProcessNullNotNull](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/SqlNullabilityProcessor.cs#L2010-L2042) 会用传播参数的空值判断替代函数空值判断；[String.Replace 契约](https://learn.microsoft.com/en-us/dotnet/api/system.string.replace?view=net-10.0) 明确 null 替换值的删除含义。

单元测试模式是 `test/W.EntityFrameworkCore.Dameng.Tests/DamengBasicStringTranslationTests.cs`：相同查询覆盖 bounded/NCLOB 和 `UseRelationalNulls` 两种模式，在 `ToQueryString()` 中断言服务器翻译。已有 `DamengQueryTranslationTests.StringMembersAndMethodsUseDamengFunctions` 只使用非空替换常量，不能捕获本问题。

功能测试参照 `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengBasicStringTranslationFunctionalTests.cs`，在真实执行后比较结果和捕获的 SQL：

```csharp
.UseDamengUnderTest(store.ConnectionString, damengOptions =>
{
    if (useRelationalNulls)
    {
        damengOptions.UseRelationalNulls();
    }
})
.ReplaceService<IModelCacheKeyFactory, StringModelCacheKeyFactory>()
.AddInterceptors(commands)
```

该文件的 `StringStore.WithTableAsync` 在 `finally` 精确删表，并为带 NCLOB 的表显式生成 `NOT CLUSTER PRIMARY KEY`。新夹具复用这些做法，但保持独立类名和唯一表名前缀，例如 `EF10_RN_`。不要直接复用其中 private 类型。随机表名须进入模型缓存键，避免测试串表。

## 文件白名单与边界

只允许修改：

- `src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengStringMethodTranslator.cs`。
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengReplaceNullTranslationTests.cs`（允许新建）。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengReplaceNullFunctionalTests.cs`（允许新建）。
- `docs/query-functions.md`、`docs/compatibility.md`，仅修改本能力及新增证据。
- 本计划的状态/执行记录；可在 Git 忽略的 `artifacts/query-translation/plan-005/` 保存脱敏证据，不提交运行产物。

不得修改 LOB 通用重写器、其他翻译器、公共 API、测试项目文件、依赖清单/锁文件、驱动适配器、数据库实例设置或凭据文件。W 功能项目已使用 `Compile Include="$(SharedFunctionalTests)/**/*.cs"` 链接官方功能源码，新功能类会自动进入 W 通道；W 单元项目没有链接官方单元源码，不能对 W 单元通道使用新类过滤器。

先读 `skills/dameng-sql/SKILL.md`。SQL 使用分段双引号标识符和 `:name` 参数；DDL 隐式提交，清理不能依赖事务回滚。文档使用中文。保留 EF Core 10、官方驱动及 W 0.2.0 的锁定范围。DateTime 无参 ToString 的服务器文本契约不在本计划范围。

## 漂移检查与验证命令

```bash
git status --short
git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengStringMethodTranslator.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengReplaceNullTranslationTests.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengReplaceNullFunctionalTests.cs docs/query-functions.md docs/compatibility.md
```

`c80c788` 是原始审查锚点，不要求前置计划完成后的工作树仍逐字相同。实施前先读取已验收前置计划的实际差异，将可归因的预期修改合并到本计划的摘录定位并记录新的实施 SHA，然后继续；这类漂移不触发停工。非预期差异、重写顺序变化或契约冲突才暂停核对，不覆盖他人修改。004 完成后 `unit` 不读取 secrets，另有只允许纯脚本白名单的 `offline-fixtures`；本计划无需使用该新通道。所有测试从仓库根目录运行：

| 目的 | 命令 | 成功条件 |
| --- | --- | --- |
| 新单元回归 | `scripts/local-test/run.sh test unit --filter FullyQualifiedName~DamengReplaceNullTranslationTests` | 发现新类，执行数大于 0，全部通过 |
| 原字符串回归 | 使用步骤 2 中的完整过滤命令 | 三类均被发现，无失败/跳过 |
| 官方真实结果 | `scripts/local-test/run.sh test functional --filter FullyQualifiedName~DamengReplaceNullFunctionalTests` | 新类全部执行，Completed，零失败/跳过 |
| W 真实结果 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --filter FullyQualifiedName~DamengReplaceNullFunctionalTests` | 与官方相同矩阵全部执行 |
| 全单元 | `scripts/local-test/run.sh test unit`，随后 `scripts/local-test/run.sh test unit --driver w` | 两组分别全部通过 |
| 差异格式 | `git diff --check` | 退出 0 |

统一入口负责可写临时 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`、单节点、禁用节点复用/共享编译和构建服务器。无需手动 `dotnet`，不清 NuGet 缓存。真实库只用已有普通测试连接，不能用管理员通道替代；两个驱动的真实库命令必须顺序执行。

## 实施步骤

### 1. 固定可达性与失败基线

新建 `DamengReplaceNullTranslationTests`。建立明确 `.IsRequired()` 的 bounded `Name` 和 NCLOB `LargeText`，以及可空 bounded `Replacement`；所有断言中的接收者非空，避免混入 CLR null 接收者异常问题。新增固定方法：

- `NullReplacementDoesNotMakeNullPredicateTrue`：常量 null、捕获变量 null 两种入口，检查 `== null` 与 `!= null` 的 SQL 真值方向；与同模型 `Where(_ => false)` / `Where(_ => true)` 的规范 SQL 形状对照，不能只断言包含 `REPLACE`。
- `NullableReplacementKeepsNullCompensationCorrect`：列替换值覆盖 null/non-null，对 null 判断、普通相等、否定谓词生成 SQL。
- `ReplacePreservesBoundedAndLobMappings`：直接投影仍在服务器求值，NCLOB 不因回退空串降成有界类型。

先保留失败输出并分类为常量折叠、参数空值处理或映射问题。若基线全部通过，不能照猜测改产品代码；进入 STOP 复核。

**验证**：`scripts/local-test/run.sh test unit --filter FullyQualifiedName~DamengReplaceNullTranslationTests`。此步骤预期至少一个针对错误真值的断言失败，且没有编译/连接错误；记录失败测试名和生成的非敏感 SQL。最终步骤必须转绿。

### 2. 修正翻译器的替换值处理

仅在 `method == Replace` 分支对第三 SQL 参数实施 null 到空串的归一化。沿用 `ISqlExpressionFactory` 的类型映射、`Coalesce` 与常量 API，保持函数结果 `instance.TypeMapping`；不要自行拼 SQL，也不要只对 `SqlConstantExpression` 打补丁而漏掉参数/列。

根据实际归一后的表达式声明 null 传播：传播列表只能表达真实函数契约，不能继续把原始 nullable 替换值当成结果必为 null 的原因。结合后置 LOB COALESCE 规则核对生成 SQL；如果 NCLOB 回退要求越过白名单修改通用 visitor，先停下报告接口需求，不夹带计划 007 的改动。

**验证**：新单元回归全部通过；执行原字符串回归：

```bash
scripts/local-test/run.sh test unit --filter 'FullyQualifiedName~DamengBasicStringTranslationTests|FullyQualifiedName~DamengQueryTranslationTests|FullyQualifiedName~DamengStringAggregateTranslationTests'
git diff --check
```

预期退出 0，原 LOB 拼接/聚合边界不变。

### 3. 添加真实结果与未截断回归

新建 `DamengReplaceNullFunctionalTests`，添加固定方法 `ReplaceResultsAndPredicatesMatchClr` 和 `NclobReplacementPreservesLongResult`。以 `DamengTheory` 覆盖两种 `UseRelationalNulls`，通过 `UseDamengUnderTest` 进入当前驱动。插入、清空跟踪、执行服务器投影和筛选；所有预期由内存中非空字符串的 CLR `Replace` 计算。

数据至少包括 `"abcabc"`、`"aaa"`、`"xyz"`、空串、含中文和引号的字符串；分别删除部分字符、全部删除、没有匹配及非空替换。长 NCLOB 数据应超过有界文本容量但位于当前已验 `CLOB_MAX_CALC_LEN` 能力内，借鉴现有 20,000 字用例；记录结果完整长度和内容，不凭 SQL 成功宣称所有长度都支持。替换值使用 bounded 列，避免未经验证地扩展 LOB 函数参数类型。

至少捕获一条真正含 `REPLACE` 的投影 SQL，防止顶层客户端计算假通过。null 检查若合法折叠为常量，则应断言返回 ID 集合，而不是强求函数仍在谓词中。

**验证**：依次运行命令表中的官方和 W `DamengReplaceNullFunctionalTests` 过滤命令。两个 TRX 各自 Total > 0、Total = Executed = Passed、Failed = 0、Skipped = 0、宿主 Completed。第二通道只能在第一通道结束并完成清理后启动。

### 4. 收敛文档与验收

在 `docs/query-functions.md` 说明 null 替换值删除语义与已验重载；在 `docs/compatibility.md` 记录真实验证的驱动/profile和边界，保持原有文化、排序及不支持重载描述。记录失败基线、最终两个 TRX、对象清理及最终 HEAD；历史测试数字不能替代本次运行。

**验证**：执行两组全单元和 `git diff --check`；用 `git status --short`、`git diff --name-only` 对照白名单。只有已经存在的他人变更可以留在范围外，必须在记录中明确区分，不清理或回滚它们。

## 回归矩阵与完成标准

| 维度 | 必测值 |
| --- | --- |
| 替换值来源 | 常量 null、参数 null/空串/非空、列 null/非空 |
| 接收者 | 必填 bounded、必填 NCLOB；含空串、中文、重复匹配、无匹配 |
| 查询位置 | 服务器投影、相等、`== null`、`!= null`、否定与组合条件 |
| EF 空值模式 | 默认、UseRelationalNulls |
| 驱动 | 官方/W 功能通道顺序执行 |
| 容量 | 普通样本、长 NCLOB 不截断；不扩大服务器长度承诺 |

- [x] 三个固定单元方法及两个固定功能方法存在并被过滤器发现。
- [x] 保留至少一个旧实现失败的真实单元断言；所有新增单元和两驱动功能回归最终通过。
- [x] `== null` / `!= null` 返回行集与独立 CLR 预期一致，未用禁用优化规避问题。
- [x] 两组完整单元通过；两个功能 TRX 无失败/跳过且清理完成。
- [x] 没有锁文件、公共 API、无关翻译器或未授权源文件变更；`git diff --check` 通过。
- [x] 本计划记录最终证据并向协调者报告 DONE；环境未满足时报告 BLOCKED 及缺失门槛。

## STOP 条件与维护提示

基线回归无法复现、归一化需要更改公开语义或其他重载、LOB 正确性要求白名单外产品改动、服务器将“全部删除”视为 null 而无法满足既定空串合同，均先报告并重新界定方案。缺连接、测试被跳过、过滤器零命中、实库清理失败不得记为通过，也不得改用管理员连接或降低断言。

复核重点是“null 元数据会驱动优化”，不能只看最终函数参数；今后添加函数翻译时应同时测试函数结果的 null 比较和否定。LOB 类型修正的处理顺序也是维护边界，不能在后处理前留下错误的 null 传播声明。

## 批次交付与统一证据

本计划属于 M2，启动实施前须确认 M1 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/005.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为确认问题或验证缺口，须以“已修复并验证”闭合；补足有辨别力的测试证据也属于验证缺口的修复。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M2.md` 的输入。

## 本轮执行摘要（2026-10-28）

实施基线 `14acdae6753c7e7a796f0e32cf5787de889684ba`；详见 [唯一执行记录](evidence/005.md)。
