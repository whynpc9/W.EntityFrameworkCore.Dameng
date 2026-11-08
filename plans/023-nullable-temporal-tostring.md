# 计划 023：恢复可空恒等日期转换的 ToString 翻译

> 执行者须逐步完成本计划，每一步达到所列预期再继续。出现 STOP 条件时停止并报告，不扩大范围。协调者维护 `plans/README.md`，执行者只提交本计划范围内的实现和 `plans/evidence/023.md`，不要并发修改索引。
>
> **先检查基线**：本计划基于 `5b1f6d407ac61acf7f5658addd551565aed34721`，不是编写时本地旧 `main` 的 `f626736`。先运行下文祖先检查与范围差异检查；本地旧工作树检查失败是预期情况，不能直接在该工作树实施，也不能通过丢弃用户文件或切换其分支解决。

## 状态

- 实施状态：DONE；已在 [PR #18](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/18) 合并，双驱动聚焦实库验收通过。结果见 [023 证据](evidence/023.md)。
- 原发现：R03，可空恒等日期转换的 ToString 判断遗漏。
- 优先级：P2。
- 工作量：M，包含聚焦单元测试、双驱动真实库对照和文档证据。
- 修复风险：LOW；主要风险是把“返回未翻译”改成抛异常，或者混淆两个可空重载的 NULL 语义。
- 产品实现前置：无；023 的代码和单元测试可独立进行，使用已包含旧 008 修复的基线。
- 正式验收前置：计划 021 的 TRX 安全与计数修复须已完成，并包含在 023 的验收工作树中。这是验收依赖，不是 023 产品实现依赖；不得用尚未纳入 021 的启动器结果将本项标为 DONE。
- 类别：正确性、回归测试。
- 编写基线：`5b1f6d407ac61acf7f5658addd551565aed34721`，2026-11-08。
- 规划时只有静态审查依据，旧测试通过记录不能充当本计划验收；后续复现、修复和实库结果见 [023 证据](evidence/023.md)。

## 为什么需要修复

为 `DateTime?` 属性配置 `.HasConversion(v => v, v => v)` 后，有效的 provider CLR 类型可以是 `DateTime?`。日期成员的公共守卫已经解包 Nullable，因此 `.Value.Year` 等操作仍能翻译；日期 ToString 翻译器却只接受精确的 `DateTime`，把同一原生时态列错误地当成非日期编码，导致谓词或分组无法翻译。

目标是恢复这类恒等转换的既有日期文本能力，保持无参调用使用服务器默认文本、显式格式白名单和 NULL 行为。不扩充新的日期格式、数据类型或精度保证，也不改变 ticks/文本转换的拒绝边界。

## 当前状态与必须保持的契约

所有行号均属于上述固定 SHA，执行前用 `git show 5b1f6d407ac61acf7f5658addd551565aed34721:<路径>` 核对。当前工作树中的同名文件可能更旧。

`src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeToStringTranslator.cs:41–45` 是遗漏点：

```csharp
// A DateTime converted to ticks/text is not a native temporal SQL operand.
if (instance.TypeMapping?.Converter is { } converter && converter.ProviderClrType != typeof(DateTime))
{
    return null;
}
```

同文件 `34–35` 识别 Nullable 转换；`59–72` 对常量格式生成 `TO_CHAR`，对无参调用生成 `CAST`，只为 Nullable.ToString 增加 `COALESCE`：

```csharp
return nullableConversion
    ? sqlExpressionFactory.Coalesce(result, sqlExpressionFactory.Constant(string.Empty, textMapping))
    : result;
```

`src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengTemporalTranslationGuard.cs:14–15` 已有正确的 CLR 类型解包先例：

```csharp
var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
providerType = Nullable.GetUnderlyingType(providerType) ?? providerType;
```

但该类的 `EnsureNative` 在不支持时抛异常。**不能直接把旧 ToString 的 `return null` 换成 `EnsureNative(instance)`**：ToString 返回 null 表示本翻译器不支持，允许 EF 按通常规则判断顶层投影是否可以客户端求值；直接抛异常会把已有合法客户端投影也变成失败。本计划采取最小修改：在 ToString 当前的 converter 检查内部解包 `ProviderClrType`，继续以 `return null` 表示拒绝。若实际实现必须复用公共逻辑，则需要独立的非抛出 Try/判断接口，并先获得扩大白名单的授权；本计划不包含这种重构。

`test/W.EntityFrameworkCore.Dameng.Tests/DamengConvertedTemporalTranslationTests.cs:67–80` 已有可达模型与局部回归：

```csharp
Assert.Contains("DATEPART(", context.Rows.Where(r => r.NativeAtN!.Value.Year == 2024)
    .ToQueryString(), StringComparison.Ordinal);
// WhitespaceContext.OnModelCreating:
builder.Entity<Row>().Property(r => r.NativeAtN).HasConversion(v => v, v => v).HasPrecision(7);
```

这项测试没有检查 ToString。新增测试必须确认实际 `GetRelationalTypeMapping().Converter.ProviderClrType` 为 `typeof(DateTime?)`，不能误用 `.HasConversion<DateTime>(...)`，后者会避开漏洞。

遵循以下现有测试方式：

- `test/W.EntityFrameworkCore.Dameng.Tests/DamengDateTimeStringTranslationTests.cs:12–39`：通过 `ToQueryString()` 同时检查 SQL 位置、`VARCHAR(100)`、`COALESCE`，不能把仅有 `SELECT` 当成服务器翻译证据。
- 同文件 `139–145`：使用 `Expression.Constant(format)` 构造格式调用，避免 Theory 参数被当作动态格式参数。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDateTimeStringFunctionalTests.cs:15–69`：无参文本用独立原生 CAST 结果做 oracle，核对投影、谓词命中与分组计数。
- 同文件 `197–203`：捕获实际执行的命令，断言目标 SQL，而不是仅凭查询结果宣称服务器支持。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengConvertedTemporalFunctionalTests.cs:251–262,295–313`：唯一表名、`finally` 清理、清理后参数化查询 `USER_TABLES` 确认对象消失。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengTestDriver.cs:27–35`：共享功能测试使用 `UseDamengUnderTest`。W 功能项目通过 `Compile Include="$(SharedFunctionalTests)/**/*.cs"` 链接共享源文件，新增该目录下的测试无需改项目文件。

文档合同（`docs/datetime-string-translation.md:5–9,18–23,46–61`）必须原样保持：

1. 无参 `DateTime.ToString()` 是 `CAST(value AS VARCHAR(100))`，输出采用服务器默认日期时间文本，不读取客户端 `CurrentCulture`。
2. `Nullable<DateTime>.ToString()` 把 SQL NULL 转为空串；`nullable.Value.ToString()` 保留 SQL NULL，不为它增加空串回退。包含 NULL 的后一种服务器投影不能用 CLR `.Value` 求预期，否则测试自己会抛异常。
3. 投影、谓词、排序与分组采用同一转换。不得自动增加 `TRUNC` 或推断业务时间粒度。
4. 格式只允许 `yyyy`、`yyyy-MM`、`yyyy-MM-dd`、`yyyy-MM-dd HH:mm:ss`、`yyyy-MM-dd HH:mm:ss.fffffff`、`s`、`yyyy-MM-dd'T'HH:mm:ss` 这七个常量；动态格式、`O`、`IFormatProvider` 仍不支持。
5. ticks/文本编码不能传给原生日期格式函数。装箱的引用类型接收者、DateTimeOffset/DateOnly/TimeOnly ToString 不在本计划范围。
6. 未翻译的谓词/分组失败；已有顶层客户端投影规则继续有效，但这种成功不算服务器翻译支持。

## 修改范围

只允许修改或创建以下文件，所有路径相对于独立实施工作树：

1. `src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeToStringTranslator.cs`。
2. `test/W.EntityFrameworkCore.Dameng.Tests/DamengNullableTemporalToStringTranslationTests.cs`（新建）。
3. `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengNullableTemporalToStringFunctionalTests.cs`（新建，双驱动共用）。
4. `docs/datetime-string-translation.md`，只补本项恒等转换范围和证据。
5. `docs/compatibility.md`，只更新日期字符串转换这一项的限定能力与证据。
6. `plans/evidence/023.md`（新建，脱敏记录）。

本计划文件由协调者管理；执行者不修改 `plans/README.md`。

明确排除：`DamengTemporalTranslationGuard` 及其他翻译器重构、公共 API、新格式、值转换器编码设计、驱动桥接、类型映射、迁移、旧 008 测试扩展、项目/锁文件、EF Core/驱动依赖升级、TLS/profile、管理员通道及全量数据库回归。不修改已接受的官方七位参数精度边界，不复制或反编译达梦官方 EF 提供程序。

## Git 与基线工作流

编写时 `/Users/wanghongyi/Projects/dameng-entityframework-core` 的 HEAD 是 `f626736`，比审查基线旧 35 个提交，且有用户文件。以下是未来实施步骤，不是已执行记录。

在收到实施授权后，先只读检查：

```bash
git status --short --branch
git rev-parse HEAD
git merge-base --is-ancestor 5b1f6d407ac61acf7f5658addd551565aed34721 HEAD
```

最后一条在旧工作树应退出 1；此时禁止在原工作树写代码。用固定基线建立独立工作树，若分支或目标目录已存在则停止并交协调者处理，不覆盖：

```bash
git worktree add -b codex/023-nullable-tostring /Users/wanghongyi/.codex/worktrees/023-nullable-tostring/dameng-entityframework-core 5b1f6d407ac61acf7f5658addd551565aed34721
cd /Users/wanghongyi/.codex/worktrees/023-nullable-tostring/dameng-entityframework-core
git merge-base --is-ancestor 5b1f6d407ac61acf7f5658addd551565aed34721 HEAD
git diff --stat 5b1f6d407ac61acf7f5658addd551565aed34721..HEAD -- src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeToStringTranslator.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengNullableTemporalToStringTranslationTests.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengNullableTemporalToStringFunctionalTests.cs docs/datetime-string-translation.md docs/compatibility.md plans/evidence/023.md
```

预期：祖先检查退出 0；固定基线新工作树的范围 diff 为空；`git status --short` 为空。若协调者提供更新的、已包含该基线的工作树，逐项核对范围 diff 与以上摘录，有语义漂移时先报告。不要自动拉取、重置或切换原工作树。

现有普通测试连接由协调者通过已批准的私密配置路径提供给隔离工作树，或安全注入普通测试连接环境变量。不打印、不复制到计划/命令参数，不重建持久用户/表空间，不将普通通道提升为管理员。缺失配置时停在单元测试结果，不标 DONE。

分支使用 `codex/023-nullable-tostring`。仅在实施授权包含提交时按逻辑单元提交，中文消息可用 `修复可空恒等日期转换的字符串翻译`；未经另行指示不推送、不创建 PR、不合并、不发布。

## 命令与通过条件

所有测试统一使用已核对的 `scripts/local-test/run.sh`。它设置可写临时 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`，使用锁定还原、`-m:1 /nodeReuse:false /p:UseSharedCompilation=false` 与受支持的 `--disable-build-servers`。不要另行运行无这些设置的 dotnet 命令，不清全局 NuGet 缓存。

| 目的 | 命令 | 成功预期 |
| --- | --- | --- |
| 新单元测试 | `scripts/local-test/run.sh test unit --configuration Release --filter 'FullyQualifiedName~DamengNullableTemporalToStringTranslationTests'` | 修复后 Completed，非零执行、全通过、零失败/跳过 |
| 聚焦单元回归 | `scripts/local-test/run.sh test unit --configuration Release --filter 'FullyQualifiedName~DamengNullableTemporalToStringTranslationTests\|FullyQualifiedName~DamengDateTimeStringTranslationTests\|FullyQualifiedName~DamengConvertedTemporalTranslationTests'` | Completed，非零执行、全通过、零失败/跳过 |
| 官方功能回归 | `scripts/local-test/run.sh test functional --configuration Release --filter 'FullyQualifiedName~DamengNullableTemporalToStringFunctionalTests'` | Completed，全部新功能测试通过、零跳过、精确清理成功 |
| W 功能回归 | `scripts/local-test/run.sh test functional --configuration Release --driver w --w-transport-security PlaintextAllowed --filter 'FullyQualifiedName~DamengNullableTemporalToStringFunctionalTests'` | 与官方相同，保存独立 W TRX |
| 差异检查 | `git diff --check` | 退出 0 |

上表单元回归过滤器中的 `\|` 是 Markdown 表格转义；实际命令直接复制下面代码块，不把反斜杠传给测试过滤器：

```bash
scripts/local-test/run.sh test unit --configuration Release --filter 'FullyQualifiedName~DamengNullableTemporalToStringTranslationTests|FullyQualifiedName~DamengDateTimeStringTranslationTests|FullyQualifiedName~DamengConvertedTemporalTranslationTests'
```

真实数据库先官方、后 W 串行执行；任何其他计划使用同一模式时等待协调者释放资源。`PlaintextAllowed` 仅用于仓库已有明文测试环境，不修改应用默认 TLS 配置。开始正式实库验收前，由协调者提供 021 已完成验收的实际提交 SHA，核对 `plans/evidence/021.md`，再以 `git merge-base --is-ancestor <021实际验收提交SHA> HEAD` 确认其存在于当前验收工作树（必须退出 0；此处占位符不得原样执行）。缺少该提交时可继续 023 的代码和单元工作，不能运行或报告正式实库验收。

## 实施步骤

### 1. 添加能够证明遗漏的单元回归

在新单元文件建立四个可空属性：普通原生 `DateTime?`、普通 Nullable 恒等转换、ticks 转换、带前缀的文本转换；另设非可空 `DateTime` 恒等转换作为既有正对照。原生列使用 TIMESTAMP(7)，ticks 为 BIGINT，文本为有界 NVARCHAR2。不要用共享旧 008 夹具的 private 类型，也不要修改旧测试文件。

固定新方法名：

- `NullableIdentityToStringTranslatesInRequiredServerPositions`：无参 Nullable.ToString、`.Value.ToString()` 和七个白名单格式；覆盖投影 SQL、谓词、排序和分组，格式以表达式常量构造。
- `NullableIdentityRetainsDistinctNullContracts`：直接 Nullable 的 SQL 含转换外 COALESCE，`.Value` 调用不新增空串回退。
- `NonNativeToStringRemainsUntranslatedInServerPositions`：ticks/文本的无参和常量格式在谓词、分组失败；普通无 converter 与非可空恒等转换继续成功。
- `NonNativeTopLevelProjectionKeepsClientEvaluationBoundary`：ticks/文本的合法顶层投影仍能形成查询，SQL 只读取原值，不含日期 CAST/TO_CHAR；不能因共享守卫抛错而失败。此单元仅验证编译/SQL 形态，真实求值由步骤 3 验证。

每个恒等用例先断言有效 provider CLR 类型为 `DateTime?`，并确认映射是 TIMESTAMP。Nullable 没有带格式的 ToString 重载；格式用例必须在 `.Value` 上调用，不能编造 `nullable.ToString(format)`。

**验证**：运行“新单元测试”命令。未修改产品时预期至少恒等转换的必需服务器位置断言失败，失败是“无法翻译”，不是编译/连接/夹具错误；已有原生与非原生边界对照应通过。保存基线 TRX 及失败方法到未来 `plans/evidence/023.md`。若所有新测试通过，停止并报告“静态发现未复现”，不要为了制造红灯改断言或改产品。

### 2. 仅修正 ToString 的 Nullable 类型判定

在 `DamengDateTimeToStringTranslator.Translate` 当前 converter 分支内，使用 `Nullable.GetUnderlyingType(converter.ProviderClrType) ?? converter.ProviderClrType` 后再与 `typeof(DateTime)` 比较。保持非日期 provider 类型的 `return null`；不要改变格式白名单、方法识别、映射大小、COALESCE 位置和现有错误路径。

不要直接调用会抛异常的 `EnsureNative`，不要为 ticks/text 强制 CAST，不在客户端预格式化被查询列，也不依赖查询结果值去挑分支。

**验证**：运行“新单元测试”和“聚焦单元回归”两条命令，均须 Completed、非零、全通过、零跳过；`git diff --check` 退出 0。基线失败的同一批测试必须在不放宽断言的情况下转绿。

### 3. 添加共用的双驱动真实结果矩阵

新功能文件使用 `[DamengFact]`/`[DamengTheory]`、`UseDamengUnderTest`、现有驱动别名和 `DbCommandInterceptor`，不直接判断官方/W ADO.NET 类型。建立唯一表前缀 `EF10_NTS_`；原生和恒等列的 SQL 类型均为 TIMESTAMP(7)，非原生对照列为 BIGINT/有界 NVARCHAR2。读取 SQL skill 后编写表 DDL，DDL 不假定事务回滚；在 `finally` 精确 DROP 并查询 `USER_TABLES` 断言该表已消失。

种子包含 null、闰日非午夜、跨年、同值重复项，日期小数秒只用整微秒（ticks 必须是 10 的倍数，例如 `.1234560`），不要使用第七位非零值。参数写入后清空跟踪并直接读回所有属性，逐值验证 ticks/文本 converter 往返，先确认存储正确，再做 ToString 测试。不要复用旧 DateStringStore 的七位非零种子，以免把已接受的官方参数问题混入此项。

固定新方法名：

- `NullableIdentityDefaultTextMatchesNativeCast`：普通无 converter、Nullable 恒等、非可空恒等的默认文本投影/谓词命中/分组计数与同连接原生 CAST oracle 一致；Nullable null 用空串，`.Value.ToString()` 的 null 保持 SQL NULL。每次查询清空捕获列表并验证实际 CAST、WHERE 或 GROUP BY。
- `NullableIdentityConstantFormatsMatchNativeTemporalControls`：七个白名单常量格式逐项执行；非 null 与 CLR InvariantCulture 数字格式和原生对照列一致，NULL 结果用原生 SQL oracle，不能在 CLR 上访问空 Nullable.Value。
- `NonNativeTemporalTextCallsFailBeforeSendingSql`：ticks/文本两种映射，无参与格式方法，谓词及分组枚举失败，清空后的命令捕获列表始终为零；直接属性往返仍成功。
- `NonNativeTopLevelTextProjectionKeepsClientEvaluation`：在非原生存储上执行已有合法顶层客户端投影，用捕获 SQL 确认无日期 CAST/TO_CHAR，用 CLR 在固定文化下计算预期并核对结果；包含 null 的直接 Nullable.ToString 投影须保留空串。`.Value` 客户端用例只选非 null 行，不把 CLR 对 null 的异常误报为服务器回退回归。修改文化后必须在 finally 恢复。

**验证**：先确认验收工作树已包含 021 的实际验收提交，再运行官方功能命令；成功并清理后才运行 W 功能命令。两次都须 Completed、总数等于执行数等于通过数、零失败/跳过。W 链接共享新文件，过滤结果必须非零。记录两套独立 TRX 与实际命令、服务器测试范围、清理断言结果，不合并计数。任何连接/清理失败时停止数据库工作。

### 4. 更新限定文档并形成可审查证据

更新两处日期文本文档，说明普通 `DateTime?→DateTime?` 恒等 converter 与无 converter 的原生日期保持相同 ToString 翻译；非原生仍拒绝必需服务器位置。只有步骤 3 双驱动通过后才写“实库已验证”，明确本项整微秒测试范围，不声称新 100ns 保证或 TLS 验证。保留顶层投影客户端求值与服务器翻译的区别。各新计划都会修改 `docs/compatibility.md`，该文件的合并由协调者串行处理，不能并发覆盖其他计划的结论。

创建 `plans/evidence/023.md`，记录初始 SHA、最终 SHA 或未提交差异指纹、基线红灯、修复后各命令、各 TRX 的真实总数/通过/失败/跳过、执行配置 Release、两驱动顺序、精确清理和未验证范围。不抄写连接串、主机、用户、口令或 secrets 内容；不覆盖旧 008 的历史证据。

**验证**：`git diff --check` 退出 0；运行以下路径白名单检查（默认在干净独立工作树上实施，现有这份计划文件不计作实现改动）：

```bash
python3 - <<'PY'
import subprocess
allowed = {
    'src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeToStringTranslator.cs',
    'test/W.EntityFrameworkCore.Dameng.Tests/DamengNullableTemporalToStringTranslationTests.cs',
    'test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengNullableTemporalToStringFunctionalTests.cs',
    'docs/datetime-string-translation.md',
    'docs/compatibility.md',
    'plans/evidence/023.md',
}
changed = set()
for args in [
    ['git', 'diff', '--name-only', '5b1f6d407ac61acf7f5658addd551565aed34721'],
    ['git', 'ls-files', '--others', '--exclude-standard'],
]:
    changed.update(subprocess.check_output(args, text=True).splitlines())
unexpected = changed - allowed
assert not unexpected, f'超出范围的文件: {sorted(unexpected)}'
assert 'plans/evidence/023.md' in changed, '缺少本项执行证据'
print('023 scope check passed')
PY
```

预期输出 `023 scope check passed`。若协调者在隔离分支预置了计划或已验收的 021 改动，应先记录干净的实施起点并用该起点替换白名单检查的差异基线；不得把其他产品文件加入 allowed 来绕过检查。若 023 产品实现先于 021 完成，应由协调者在包含 021 的干净验收工作树重放本项差异，记录该工作树的实施起点后再做正式验收与范围检查，不把 021 文件混入 023 的修改白名单。

## 测试矩阵摘要

| 维度 | 必须覆盖 | 判定方式 |
| --- | --- | --- |
| 映射 | 无 converter 的 DateTime?、普通 Nullable 恒等、非可空恒等、ticks、带前缀文本 | 记录/断言有效 store type 与 provider CLR type；属性读写先往返 |
| 调用 | Nullable 无参、Value 无参、Value 的七个白名单常量格式 | CAST/TO_CHAR 正确，常量通过 Expression.Constant 构造 |
| SQL 位置 | 投影、谓词、分组；单元补排序 | SQL 断言加真实命中行及分组计数，不以客户端成功代替服务器支持 |
| 值 | null、非 null、重复、闰日、跨年、整微秒 | 服务器默认文本用原生 CAST oracle；NULL 单独判断 |
| 负对照 | ticks/文本的谓词、分组；不支持格式/动态格式/装箱既有回归 | 必需 SQL 位置拒绝、功能测试零发送；既有单元仍通过 |
| 客户端边界 | ticks/文本顶层投影 | 允许已有客户端求值；命令无日期格式 SQL；结果与固定文化 CLR 对照 |
| 驱动 | 官方，再 W | 同一共享功能源文件；独立 TRX；串行、普通用户、精确清理 |

## 完成标准

- [ ] 基线祖先检查与范围漂移检查完成，实施发生在独立 `codex/023-nullable-tostring` 分支，原工作树保持原状。
- [ ] 新单元测试曾在未改产品的基线上暴露同一 Nullable 恒等转换问题；有对应失败 TRX。
- [ ] 新单元与既有日期字符串/转换后时态单元回归全部通过，Release、非零、零跳过。
- [ ] 普通 Nullable 恒等模型实际 converter 的 ProviderClrType 为 `DateTime?`；没有用非可空 converter 偷换输入。
- [ ] 正式验收工作树包含已完成的 021 实际提交，祖先检查退出 0，证据中记录该 SHA；不是仅依赖旧启动器的 Passed 汇总。
- [ ] 两套聚焦功能测试先官方后 W 全通过，确认实际服务器文本、NULL 区别、命中行和分组计数；测试表精确清理并确认不存在。
- [ ] ticks/文本服务器位置仍拒绝且零发送；合法顶层客户端投影不因新守卫抛错而失效。
- [ ] 文档只声称已有环境、整微秒样本及本项覆盖范围，保留无参服务器文本和七个格式白名单。
- [ ] `git diff --check` 和白名单检查通过；依赖、锁文件、公共 API、其他源文件无改动。
- [ ] `plans/evidence/023.md` 包含可核查的命令、源码身份、分驱动结果及清理证据；协调者据证据更新索引。

## STOP 条件

出现任一项即停止并报告，不自行扩展计划：

- 无法建立包含固定基线的干净隔离工作树，或范围摘录发生语义漂移。
- 普通 `.HasConversion(v => v, v => v)` 未形成 `DateTime?` provider 类型，或基线新测试全部通过，因而原发现未复现。
- 修复需要改公共守卫/其他翻译器、驱动、类型映射、项目文件或依赖版本。
- 方案只有把 ToString 的 `return null` 改成抛异常才能通过，或导致 ticks/文本的既有顶层投影失败。
- 普通用户真实库连接不可用、测试被跳过、两驱动未串行、W 过滤结果为零，或任何对象清理/不存在断言失败。
- 正式实库验收工作树尚未包含已验收的 021 提交；停止正式验收，允许独立的代码/单元工作继续，但不能标 DONE。
- 整微秒原生对照仍出现参数读写精度问题；先报告独立阻断，不通过更换种子、放宽比较或升级驱动掩盖。
- 任一步出现与本目标无关的失败，或合理修正后同一验证连续失败两次；保存证据并交协调者判断。

## 维护提示

未来新增 converter 支持时，要分别判断模型 CLR 类型、有效 provider CLR 类型和 SQL 存储类型；Nullable 的解包问题不能只在日期成员中修一次。翻译器“返回未翻译”和直接抛异常不是等价接口，审查时必须检查顶层投影的客户端边界。后续若统一所有时态守卫，应另立重构范围，先用本计划的正负矩阵表征行为；本计划不承担该重构。
