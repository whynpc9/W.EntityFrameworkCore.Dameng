# 计划 008：调查并约束转换后日期列的成员与方法翻译

## 状态与执行约定

- 状态：TODO。
- 里程碑：M2，查询与时间正确性；对应调查 I03。
- 优先级 P2；工作量 M；风险 MED；置信度 MED。此计划先调查，不预定一定存在产品缺陷。
- 依赖：[004-offline-ci.md](004-offline-ci.md) 完成，以及 [006-temporal-literal-fidelity.md](006-temporal-literal-fidelity.md) 已产出可审查结论。006 涉及时态字面量/参数精度；本计划不得将其尚未解决的精度差异误归因于值转换器。
- 编写基线：`c80c788fb3cd34d97626d578db580264a28a11f3`，2026-10-28。
- 仓库：`/Users/wanghongyi/Projects/dameng-entityframework-core`。按“表征→证据判定→必要修复→回归”执行；不自动提交、推送或创建 PR。索引由协调者维护，结束时报告本计划状态与证据。

## 调查问题与不变契约

`DateTime.ToString` 翻译已阻止把存成 ticks 或文本的 DateTime 当作原生日期，但 DateTime/DateTimeOffset 成员、DATEADD 和日期分桶没有同等检查。合法 `HasConversion<long>()` 模型上的 `.Year`、`.Date` 等可能把 BIGINT 直接交给日期函数；也可能存在上游保护，使某些形状根本不可达。必须先执行模型、SQL 和结果表征，区分这两种情况。

本计划不解析任意转换器表达式，不尝试把 ticks/text 重新还原为 SQL 日期来扩大支持。确认缺口后，采用明确的可支持操作数边界；仍以原生时态保存的安全对照不能因“存在 Converter”被一概拒绝。

维护者已选定无参 `DateTime.ToString()` 的服务器 `CAST(... AS VARCHAR(100))` 文本契约，`Nullable<DateTime>.ToString()` 有空串回退；这与客户端文化独立。投影、谓词、排序、分组使用同一规则，不推断年月/日期等业务粒度。本计划不得撤回、重新定义这些语义，也不扩展原本拒绝的小数/参数化 double DATEADD、DateTimeOffset 时区转换等能力。

## 当前实现与可复用模式

`src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeToStringTranslator.cs:41–45`：

```csharp
// A DateTime converted to ticks/text is not a native temporal SQL operand.
if (instance.TypeMapping?.Converter is { } converter && converter.ProviderClrType != typeof(DateTime))
{
    return null;
}
```

`DamengDateTimeMemberTranslator.cs:18–25` 只按 CLR 成员声明类型选择 DATEPART：

```csharp
if (member.DeclaringType != typeof(DateTime))
{
    return null;
}

return member.Name switch
{
    nameof(DateTime.Year) => DatePart("year"),
```

同文件 `.Date` 使用 `TRUNC(value)`，结果沿用 `value.TypeMapping`。`DamengDateTimeMethodTranslator.cs:51–61` 的 DATEADD 也沿用接收者映射：

```csharp
return sqlExpressionFactory.Function(
    "DATEADD",
    [
        sqlExpressionFactory.Fragment(datePart),
        sqlExpressionFactory.Convert(arguments[0], typeof(int)),
        instance
    ],
    nullable: true,
    argumentsPropagateNullability: [false, true, true],
    instance.Type,
    instance.TypeMapping);
```

`DamengDateBucketMethodTranslator.cs:37–47` 直接把 `arguments[1]` 用作 DATEPART/TRUNC 的日期操作数；`DamengDateTimeOffsetMemberTranslator.cs:28–55` 只检查接收者、声明类型和返回 int。

已有 `test/W.EntityFrameworkCore.Dameng.Tests/DamengDateTimeStringTranslationTests.cs:175–179` 提供最小转换模型：

```csharp
private sealed class ConvertedContext(DbContextOptions<ConvertedContext> options) : DbContext(options)
{
    public DbSet<ConvertedRow> Rows => Set<ConvertedRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<ConvertedRow>().Property(r => r.At).HasConversion<long>();
}
```

该类的 `DateTimeConvertedToTicksDoesNotBecomeNumericText` 断言 `.ToString()` 分组无法翻译，未测试 Year/Date/AddDays/分桶。EF 10.0.12 的 [VisitMember](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/RelationalSqlTranslatingExpressionVisitor.cs#L579-L612) 绑定列后调用成员翻译器，但本次调查仍需检查实际 pipeline 的最终结果，不能仅据这段源码判定 bug。

功能测试参照 `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDateBucketFunctionalTests.cs` 的做法：通过 `UseDamengUnderTest` 选择驱动，捕获服务器 SQL，清空跟踪后执行查询，与独立 CLR 预期比较；由唯一表名和模型缓存键隔离对象，在 finally 精确清理。使用非午夜且含分钟/秒的数据才能发现 `.Date` 没有截断的问题。

## 文件白名单与禁止范围

允许修改下列文件；产品源码仅在调查确认缺口之后修改：

- `src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeMemberTranslator.cs`。
- `src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeMethodTranslator.cs`。
- `src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeOffsetMemberTranslator.cs`。
- `src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateBucketMethodTranslator.cs`。
- `src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeToStringTranslator.cs`，仅在提取共同守卫确有必要时等价接入，格式和 nullable 回退不变。
- `src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengTemporalTranslationGuard.cs`（允许新建 internal helper）。
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengConvertedTemporalTranslationTests.cs`（允许新建）。
- `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengConvertedTemporalFunctionalTests.cs`（允许新建）。
- `docs/query-functions.md`、`docs/compatibility.md`、`docs/datetime-string-translation.md`，仅补充有证据的存储转换边界。
- 本计划的调查结论/执行记录；脱敏运行证据保存到忽略目录 `artifacts/query-translation/plan-008/`。

不得修改 Storage 类型映射和字面量生成器（由计划 006 负责）、模型验证器、驱动适配器、公共日期函数签名、迁移或设计时实现、依赖版本/锁、测试通道或项目文件。不得为了“修复”而把全部 DateTime 查询改成客户端计算、删除现有 ToString 支持或在分组中加入自动截断。

先读 `skills/dameng-sql/SKILL.md`、`docs/datetime-string-translation.md`；如拟使用 EF 迁移创建夹具，还须读 `skills/dameng-ef-migrations/SKILL.md`，但本计划优先沿用功能夹具的精确建/删表模式，避免引入迁移内容。保持中文文档、分段引用、`:name` 参数、单命令批次和驱动桥接边界，不修改共享服务器参数或读取/输出凭据。

## 漂移与运行约束

```bash
git status --short
git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeMemberTranslator.cs src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeMethodTranslator.cs src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeOffsetMemberTranslator.cs src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateBucketMethodTranslator.cs src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeToStringTranslator.cs src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengTemporalTranslationGuard.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengConvertedTemporalTranslationTests.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengConvertedTemporalFunctionalTests.cs docs/query-functions.md docs/compatibility.md docs/datetime-string-translation.md
```

004 和 006 的完成状态及结论需由协调者提供；先读落地代码与证据，不能凭计划文字视为完成。`c80c788` 是原始审查锚点，实施前须读取已验收前置计划的实际差异。可归因的预期变化应更新本计划摘录定位和实施 SHA 后继续，不触发停工；非预期差异或契约冲突才暂停核对。已有变化改变控制流时先更新调查范围，不能覆盖前置成果。

所有 .NET 操作通过统一入口，入口负责可写临时 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1` 以及单节点、禁复用/共享编译和 `--disable-build-servers`。无需手工 dotnet；不清 NuGet 缓存。计划 002 已增加 `--configuration Debug|Release`；本计划默认 Debug，不能把其结果写成 Release 证据。

```bash
scripts/local-test/run.sh test unit --filter FullyQualifiedName~DamengConvertedTemporalTranslationTests
scripts/local-test/run.sh test functional --filter FullyQualifiedName~DamengConvertedTemporalFunctionalTests
scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --filter FullyQualifiedName~DamengConvertedTemporalFunctionalTests
scripts/local-test/run.sh test unit --filter 'FullyQualifiedName~DamengDateTimeStringTranslationTests|FullyQualifiedName~DamengDateBucketTranslationTests|FullyQualifiedName~DamengDateTimeOffsetMemberTranslationTests|FullyQualifiedName~DamengQueryTranslationTests'
scripts/local-test/run.sh test functional --filter 'FullyQualifiedName~DamengDateTimeStringFunctionalTests|FullyQualifiedName~DamengDateBucketFunctionalTests|FullyQualifiedName~DamengDateTimeOffsetMemberFunctionalTests'
scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --filter 'FullyQualifiedName~DamengDateTimeStringFunctionalTests|FullyQualifiedName~DamengDateBucketFunctionalTests|FullyQualifiedName~DamengDateTimeOffsetMemberFunctionalTests'
scripts/local-test/run.sh test unit
scripts/local-test/run.sh test unit --driver w
git diff --check
```

W 功能项目会自动链接新官方功能类；W 独立单元没有该链接，因此只对官方单元使用新类过滤器。每条命令必须非零命中；真实库只用持久普通测试用户，各功能命令逐一完成并清理后才执行下一条，绝不并行运行双驱动数据库通道。环境缺失、跳过、零命中或未完成宿主均保持 BLOCKED。

## 调查与实施步骤

### 1. 建立最小矩阵并捕获实际翻译结果

新建 `DamengConvertedTemporalTranslationTests`，固定类名保证过滤器稳定。建立相互隔离的模型配置，至少包含：DateTime→long 内置转换、DateTime→格式固定且可往返的 bounded string、自定义 DateTime→DateTime 恒等转换、无转换的原生 DateTime；DateTimeOffset→long/text 与无转换原生列作为第二组。nullable 同类也纳入，记录实际 `ClrType`、`StoreType`、转换器类型和 `ProviderClrType`，不凭 `.HasConversion` 调用推断映射。

使用固定方法 `ConvertedTemporalBaselineIsCharacterized` 对矩阵生成查询，分别捕获 SQL 或确切翻译异常。输出不含连接配置。DateTime 必测 Year、Date、AddDays(1)、AddMonths(1) 和四个分桶；DateTimeOffset 必测已发布的六个本地成员。将成员放在谓词/GroupBy 中强制服务器翻译，直接顶层投影单独记录，防止客户端计算掩盖不可达路径。

这个表征方法用于记录已运行的每一格，不把“生成 SQL 或抛异常都接受”的诊断结果当成正确性测试。最终必须由后续固定契约回归代替或补充，不能以其绿色状态关闭 I03。

**验证**：运行新单元类过滤器，预期编译通过、每个矩阵条目都有 SQL/异常和类型映射记录。若模型创建本身失败，明确标为“模型入口被阻断”并追踪原因；不得把它混写成日期翻译保护。保留可达路径和被阻断路径清单。

### 2. 用真实读回隔离转换器语义

新建 `DamengConvertedTemporalFunctionalTests`，固定方法 `ConvertedTemporalBaselineIsCharacterizedOnServer`。用普通测试用户和唯一前缀 `EF10_CT_` 创建小表，原生及转换列均通过参数写入相同逻辑值。先直接读回，确认转换器本身正常；初轮值使用整秒和明确 offset，避免字面量第七位问题干扰。主例取闰日的非午夜时间，另含跨年值和 nullable null。

只执行步骤 1 确认可生成 SQL 的 SELECT；记录结果或服务器错误，并与独立 CLR 求值对照。对于 DateTime.Date，比较日期和时间部分，不能只比较 Year；对于 DateTimeOffset 成员，使用原偏移本地字段作为预期。原生对照必须成功，且命令捕获证明函数在服务器执行。之后按计划 006 的已验精度范围补充小数秒对照，不冒称两种驱动精度一致。

**验证**：依次运行两个驱动的新功能类过滤命令。调查阶段不正确结果造成的断言失败属于预期基线红色，应保存证据并进入下一步判定；连接失败、表创建失败、转换器自身往返失败和清理失败不属于缺陷复现，报告 BLOCKED。不允许通过 catch 后不断言，把错误结果变成“全部通过”。

### 3. 明确选择有证据的处理分支

在本计划新增标题恰为 `## I03-结论` 的执行记录段，逐项列出触发表达式、有效映射、SQL/异常阶段、真实结果和预期，再选择以下分支：

1. **确认缺口**：至少一格非原生存储查询进入 DATEPART/TRUNC/DATEADD 且产生错误结果或服务端类型错误；下一步按最小范围修复。只把已确认的路径标为 bug，不外推所有转换器。
2. **证明已有保护**：所有怀疑入口均由上游/现有 provider 在发送前明确拒绝，并有命令计数及原生对照证据；保留针对该保护的回归，产品源码不改。报告“调查完成，未复现为缺陷”，附具体保护位置与锁定 EF 版本，协调者可以以此关闭 I03。
3. **部分可达但结果正确/合同不明确**：记录独立证据，先核实转换器的支持合同和每类操作语义；不能因单个样例碰巧可解析而宣布任意 text 转换都支持。需要超出本计划的语义决策时报告 BLOCKED，不能直接强制拒绝既有正确路径。

**验证**：`rg -n '^## I03-结论$' plans/008-converted-temporal-translation.md` 应仅定位执行者新增的独立结论标题；同时核对该段引用的两个驱动运行文件存在，所有怀疑格子都有结论，不存在“没环境因此未复现”式关闭。

### 4. 仅对确认缺口实施最小守卫

分支 1 才执行产品修改。定义清晰的原生时态操作数检查，必要时引入 internal `DamengTemporalTranslationGuard`，由确认受影响的翻译器调用。检查依据是有效提供程序存储语义/映射，不能仅看 CLR `DateTime`，也不能因 Converter 非 null 全部拒绝。对非原生 unsupported 操作返回无法翻译或抛出项目一致的明确诊断，在谓词/分组中发送前失败；不尝试猜测 ticks/text 编码。

不改变 DateTime.Now/UtcNow/Today 的静态入口，不把 null instance 当作非原生列。DateTimeOffset 成员的本地原偏移语义保持不变。只有在减少实际重复且语义等价时才让 ToString 接入 helper，其全部原有格式/装箱/null 回退回归必须保持。分支 2 跳过所有产品修改。

添加最终固定单元方法 `NonNativeTemporalOperationsAreRejectedBeforeExecution` 和 `NativeTemporalControlsRemainServerTranslated`；它们断言明确合同，不允许继续接受任意成功或异常。分支 1/2 中确认应拒绝的矩阵都进入第一方法；原生与恒等转换的已验安全对照进入第二方法。若某类不能共享合同，拆成有明确命名与原因的 Theory 数据，不能整类 Skip。

**验证**：新单元类及命令区的四类原日期/查询单元回归全部通过。基线红色已转为对应契约断言绿色；若发生旧日期转换行为变化或仍有未解释矩阵项，停止扩大修复。

### 5. 固定发送前失败及原生正对照，完成结论

将功能调查转为稳定方法 `NonNativeTemporalOperationsAreRejectedBeforeExecution` 与 `NativeTemporalControlsMatchClr`。前者在已插入并可直接读回的转换模型上执行不支持谓词，断言提供程序翻译错误且该阶段命令数为 0；后者检查原生/安全恒等转换的函数 SQL 和结果。保留独立的直接往返断言，确保拒绝的是操作而非整个转换模型。

清理仅删除本夹具唯一对象。文档按调查结论更新，不把“拒绝”写成新增日期存储支持，不把单元证据写成真实库能力。记录 006 精度结论如何影响测试输入；不在此计划重新实现其字面量修复。

**验证**：依次运行命令区的新功能两驱动、原日期功能两驱动、两组全单元和 `git diff --check`。最终每个必需 TRX 均 Total > 0、Total = Executed = Passed、Failed = Skipped = 0、宿主 Completed。`git diff --name-only` / `git status --short` 对照白名单和起始工作区记录，不删除他人变化。

## 完成标准

- [ ] 004 完成、006 已有审查结论，当前源码漂移已核对。
- [ ] 调查覆盖有效映射、强制服务器查询位置、实际 SQL/拒绝阶段和两驱动结果，不以顶层客户端计算证明翻译支持。
- [ ] 有明确的 `I03-结论`：确认并修复，或有证据证明既有保护而关闭；证据不足保持 BLOCKED。
- [ ] 两个最终单元方法和两个最终功能方法均存在、非零命中且通过；诊断型表征绿色不能替代这些契约断言。
- [ ] 既有 DateTime/DateTimeOffset、分桶和 ToString 单元及功能回归通过；无参 ToString 服务器文本契约未变。
- [ ] 失败基线与最终通过结果分开保存，真实对象精确清理，文档只描述已验证范围。
- [ ] 未改 Storage 字面量、依赖锁、公共 API 或白名单外产品代码；差异格式检查通过。
- [ ] 向协调者报告 DONE（含调查关闭依据）或 BLOCKED；不自动 commit/push/PR。

## STOP 条件与维护提示

表征阶段得到预期错误 SQL/结果是调查进展，不应因“失败两次”提前阻断；只有环境失败、输入合同不清或真实路径无法覆盖才阻断依赖步骤。若模型被上游拒绝，应追踪并验证该保护，不能强行绕过验证器制造可达 bug。要修改 Storage/驱动/公共 API、任意推断自定义转换器的数学含义，或无法保持原生恒等转换正对照时先报告范围变化。

后续 EF 或映射变动会影响哪些表达式保留 Converter，必须保留 end-to-end LINQ 表征，不能只测 helper。新增日期函数时同时测试“原生列、非原生转换列、静态函数”三类入口；成员支持不能由 CLR 类型本身推断。任何真实环境不足或测试跳过都不构成“已有保护”的关闭证据。

## 批次交付与统一证据

本计划属于 M2，启动实施前须确认 M1 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/008.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为调查：确认后修复并验证，或用充分可重复证据确证无缺陷；缺环境、跳过和单次未复现不能闭合。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M2.md` 的输入。
