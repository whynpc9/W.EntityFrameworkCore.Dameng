# 计划 024：统一 LOB 类型语义分类，保留显式类型的写法与驱动绑定

> **原执行者说明**：本文保留实施步骤和停止条件；实际完成结果见 [024 证据](evidence/024.md)。协调者维护 `plans/README.md`。
>
> **先检查基线**：本计划针对 `5b1f6d407ac61acf7f5658addd551565aed34721`，不是撰写时当前工作树的 `f626736`。撰写时执行 `git merge-base --is-ancestor 5b1f6d407ac61acf7f5658addd551565aed34721 HEAD` 返回 **1**，当前工作树比审查基线旧 35 个提交。禁止直接在旧工作树实施。按第 1 步建立隔离分支，再逐文件核对漂移。

## 状态

- **状态**：DONE；已在 [PR #19](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/19) 合并，双驱动聚焦实库验收通过。
- **优先级**：P2。
- **工作量**：M，包含双驱动聚焦实库回归。
- **修复风险**：MED；分类涉及参数类型、模型守卫和查询守卫。
- **依赖**：代码实施独立；**正式实库验收依赖 [021 · TRX 脱敏与验收语义](021-trx-redaction-and-verdict.md) 已进入验收工作树**，以免结果文件被错误改写或错误判定。不依赖 022/023 的产品实现。共用数据库的验证由协调者排队；官方、W 及其他计划均不能并行占用同一模式，`docs/compatibility.md` 的整合也串行进行。
- **类别**：bug，原发现 R04。
- **计划基线**：`5b1f6d407ac61acf7f5658addd551565aed34721`，2026-11-08。
- **规划时证据**：仓库代码及 EF Core 10.0.12 公开源码的静态调用链，当时尚未运行新测试。后续红绿灯、实库回读及限定范围见 [024 证据](evidence/024.md)。

## 为什么需要修改

`HasColumnType(" NCLOB ")` 的首尾空白在类型查找时被去掉，但随后映射 Clone 恢复原字符串。参数配置、模型键/索引守卫及查询 LOB 守卫各自比较没有去空白的名称，因此同一个已识别的类型会在后续被当成普通行内类型。目标是让同一 LOB 类型的空白和大小写变体具有一致的语义，同时保留用户指定的类型写法、分面和驱动绑定；不通过全局改写 `StoreType` 达成修复。

## 当前实现与证据

以下片段均直接读取审查基线，不是当前旧工作树。

1. `src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengTypeMappingSource.cs:204-214` 先绑定驱动，再使用原始 mapping info 克隆：

   ```csharp
   var mapping = base.FindMapping(mappingInfo) ?? FindRawMapping(mappingInfo);
   if (mapping is IDamengDriverBoundMapping driverBoundMapping)
   {
       mapping = driverBoundMapping.WithDriver(_driver);
   }
   return mappingInfo.StoreTypeName is null
       ? mapping
       : mapping?.WithTypeMappingInfo(mappingInfo);
   ```

   同文件 `:238` 有 `var trimmedStoreType = storeTypeName.Trim();`；`:260` 将 trimmed 值交给基类解析，`:321-330` 可以按解析后的名称命中映射。

2. `src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengStringTypeMapping.cs:35-39`：

   ```csharp
   private DamengStringTypeMapping(RelationalTypeMappingParameters parameters, DamengDriver? driver)
       : base(parameters)
   {
       _isLob = IsLobStoreType(parameters.StoreType);
       Driver = driver;
   }
   ```

   `:62-68` 仅在 `_isLob || IsLobStoreType(StoreType)` 时设置 `DamengParameterType.Clob`；`:76-82` 的 `Equals` 没有 Trim。`DamengByteArrayTypeMapping.cs:30-34,60-75,78-82` 有相同问题，失败时参数会走 `VarBinary` 分支。

3. `src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengLobTypeClassifier.cs:7-14`：

   ```csharp
   public static LobKind? GetLobKind(RelationalTypeMapping? typeMapping)
       => typeMapping?.StoreTypeNameBase.ToUpperInvariant() switch
       {
           "CLOB" or "NCLOB" or "TEXT" or "NTEXT" or "LONG" or "LONGVARCHAR"
               => LobKind.Text,
           "BLOB" or "IMAGE" or "LONGVARBINARY" or "BFILE"
               => LobKind.Binary,
           _ => null
       };
   ```

4. `src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs:109` 调 `IsLobStoreType(mapping.StoreTypeNameBase)`，`:401-411` 再维护一份未 Trim 的别名判断。

5. 已核对的公开契约：[EF Core 10.0.12 RelationalTypeMappingSource.cs](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Storage/RelationalTypeMappingSource.cs) `:253-282` 分别保留原始列类型与解析结果；[RelationalTypeMapping.cs](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Storage/RelationalTypeMapping.cs) `:138-150` 的 WithTypeMappingInfo 使用原始 StoreTypeName，`:288-307` 对没有括号的名称不会去掉空白。未来锁定版本若改变，重新核对契约，不依赖本段猜测。

### 必须保持的约定

- 类型名用于语义判断时可以规范化；映射的显示名称与显式分面不随之重写。未支持的类型不因分类器认识名称就成为受支持映射。
- 共用代码只通过 `DamengDriver` 设置驱动参数；不能引入 `DmParameter` 或 `W.Dm` 类型分支。驱动相关断言放在相应测试项目。
- 标识符按组成部分引用，参数使用 `:name`。DDL 隐式提交；测试对象用唯一名称，`finally` 精确清理并查证残留为零。
- JSON/JSONB、结构 JSON、`JsonElement` 的已有合同保持原样；不能因模板中 `lob: true` 标志顺手改变 JSON 的参数类型或查询支持。
- 不改 EF Core 10、DM.DmProvider、W.DamengProvider 的版本范围和锁文件；不反编译达梦官方 EF 提供程序。
- 先读 `skills/dameng-sql/SKILL.md`；如实施中改用 EF 迁移生成/执行入口，另读 `skills/dameng-ef-migrations/SKILL.md`。本计划无需新增迁移。

## 可修改范围

仅允许修改或新增以下文件；不存在的测试/helper/evidence 文件可以创建：

```text
src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengLobStoreType.cs
src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengStringTypeMapping.cs
src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengByteArrayTypeMapping.cs
src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengLobTypeClassifier.cs
src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs
test/W.EntityFrameworkCore.Dameng.Tests/DamengLobTypeNormalizationTests.cs
test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverLobTypeNormalizationTests.cs
test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengLobTypeNormalizationFunctionalTests.cs
docs/compatibility.md
plans/evidence/024.md
```

以下是漂移检查必须读取、但不允许修改的契约/范例：`DamengTypeMappingSource.cs`、`IDamengDriverBoundMapping.cs`、`DamengTypeMappingTests.cs`、`WDriverTypeMappingTests.cs`、`DamengLobGuardTranslationTests.cs`、`DamengLobGuardFunctionalTests.cs`、两个 W 测试项目的 csproj、`scripts/local-test/run.sh` 和 `TestLauncher.cs`。

**不在范围内**：LOB `IN`/窗口表达式覆盖调查 I01、其他查询翻译、复杂属性模型校验范围、LOB 存储布局、其他别名的服务器能力、流式 LOB/数据源、官方驱动参数缺陷、批处理、自动重试、发布流程、依赖和锁文件。不得把 Storage helper 放到 Query 层后让 Storage/Infrastructure 反向依赖 Query。

## Git 与工作区

隔离分支名使用 `codex/024-lob-normalization`。从已包含计划基线的当前 main 建立隔离 worktree；不切换、不 reset、不清理现有旧工作区。示例（目录/分支已存在则先检查，不能覆盖）：

```bash
git worktree add -b codex/024-lob-normalization ../dameng-efcore-plan-024 origin/main
cd ../dameng-efcore-plan-024
git merge-base --is-ancestor 5b1f6d407ac61acf7f5658addd551565aed34721 HEAD
git status --short --branch
```

若 origin/main 未包含基线，停止交协调者处理。计划文档可能尚未在隔离分支；由协调者带入已选定的计划，不能为此复制旧源码或旧计划索引。建议按逻辑单元提交，提交信息使用仓库的中文动词风格，例如“统一 LOB 类型语义分类并补充双驱动回归”。本计划不授权推送、创建 PR、合并或发布。

## 命令与结果约定

本地测试一律通过现有入口，它会设置可写的 `DOTNET_CLI_HOME`、禁用首次体验/遥测，执行 locked restore 和单节点构建，并使用 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`。不要清 NuGet 全局缓存。若必须单独调用 dotnet，先设置相同环境变量和参数；本计划的验收命令无需绕过入口。

| 目的 | 命令 | 成功预期 |
| --- | --- | --- |
| 官方新增单元 | `scripts/local-test/run.sh test unit --filter 'FullyQualifiedName~DamengLobTypeNormalizationTests'` | 宿主 Completed，非零，总数=执行=通过，失败/跳过均0 |
| W 新增单元 | `scripts/local-test/run.sh test unit --driver w --filter 'FullyQualifiedName~WDriverLobTypeNormalizationTests'` | 同上 |
| 官方完整单元 | `scripts/local-test/run.sh test unit --configuration Release` | 同上 |
| W 完整单元 | `scripts/local-test/run.sh test unit --driver w --configuration Release` | 同上 |
| 官方实库 | `scripts/local-test/run.sh test functional --configuration Release --filter 'FullyQualifiedName~Lob'` | 同上，包含新增类及原 LOB 回归 |
| W 实库 | `scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed --configuration Release --filter 'FullyQualifiedName~Lob'` | 同上，须在官方结束并查证清理后运行 |
| 空白检查 | `git diff --check` | 退出0、无输出 |

`PlaintextAllowed` 仅用于已有明文测试 profile；TLS profile 应由协调者提供匹配配置，不擅自降级。实库使用现有普通测试用户；隔离 worktree 的秘密配置由协调者提供，或使用已有 `DAMENG_TEST_CONNECTION_STRING`。不得打印秘密、重新 provision、提升普通测试为管理员，或把缺连接/跳过计作验收。

## 实施步骤

### 1. 建立隔离基线并精确检查漂移

执行上述 worktree 和祖先检查。在新 worktree 定义以下数组（bash/zsh 均可），同时检查所有可修改文件和直接依赖的契约文件：

```bash
audit_base=5b1f6d407ac61acf7f5658addd551565aed34721
audit_paths=(
  src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengLobStoreType.cs
  src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengStringTypeMapping.cs
  src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengByteArrayTypeMapping.cs
  src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengLobTypeClassifier.cs
  src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs
  test/W.EntityFrameworkCore.Dameng.Tests/DamengLobTypeNormalizationTests.cs
  test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverLobTypeNormalizationTests.cs
  test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengLobTypeNormalizationFunctionalTests.cs
  docs/compatibility.md
  plans/evidence/024.md
  src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengTypeMappingSource.cs
  src/W.EntityFrameworkCore.Dameng/Storage/Internal/IDamengDriverBoundMapping.cs
  test/W.EntityFrameworkCore.Dameng.Tests/DamengTypeMappingTests.cs
  test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverTypeMappingTests.cs
  test/W.EntityFrameworkCore.Dameng.Tests/DamengLobGuardTranslationTests.cs
  test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengLobGuardFunctionalTests.cs
  test/W.EntityFrameworkCore.Dameng.WDriver.Tests/W.EntityFrameworkCore.Dameng.WDriver.Tests.csproj
  test/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests/W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests.csproj
  scripts/local-test/run.sh
  scripts/local-test/TestLauncher.cs
)
git merge-base --is-ancestor "$audit_base" HEAD
git diff --stat "$audit_base"..HEAD -- "${audit_paths[@]}"
git diff "$audit_base"..HEAD -- "${audit_paths[@]}"
```

**验证**：祖先命令退出0；基线启动时 diff 为空。若存在差异，逐项核对当前实现片段，相关行为或测试接缝改变则停止；纯文档变化也记录。021 带入的启动器/TRX 改动属于已声明依赖，应核对其实施证据、确认上述通道和命令接口仍成立并记录接受的差异；不能默默忽略，也不因这一预期差异重复实施或撤销 021。记录实际起始 SHA、文件指纹和 `git status`，保存到本项 evidence。不要用当前旧工作树的差异抵消远端已经完成的修复。

### 2. 先添加能区分缺陷的离线回归

在两个新增单元类中建立表驱动矩阵。官方参数断言沿用 `DamengTypeMappingTests.DamengParametersUseLobBinaryIntervalAndJsonTypes`，W 参数断言沿用 `WDriverTypeMappingTests.ParametersUseWProviderTypesForLobBinaryAndInterval`；它们通过真实映射源取得 mapping，在未打开连接的 `DmCommand` 上创建参数，核对实际 `DmSqlType`。

基线官方示例的核心结构如下，W 对应使用其测试项目已有的 W 类型别名：

```csharp
var blobMapping = source.FindMapping(typeof(byte[]), null, size: 32768)!;
var blobParameter = Assert.IsType<DmParameter>(
    blobMapping.CreateParameter(command, "blob", new byte[] { 1, 2, 3 }));
Assert.Equal(DmDbType.Blob, blobParameter.DmSqlType);
```

测试对象需通过同一驱动的服务配置创建，不手工反射改写驱动标记。使用独立 context 类型或包含类型配置的 `IModelCacheKeyFactory`，避免不同类型字符串复用第一个模型而令矩阵假通过。

具体矩阵见下文。先写映射/参数和模型/查询可达路径的断言，不依赖尚未创建的 helper，使测试能在旧产品上编译并呈现语义失败。

**验证**：分别运行命令表中“官方新增单元”和“W 新增单元”。预期无空白/有界正例通过，至少文本与二进制的含空白参数断言及一项守卫断言失败；保存失败 TRX 和精确用例名。编译失败、连接错误、空筛选或全跳过不是有效红灯。若全绿，停止并重新确认基线与可达性。

### 3. 在 Storage 层统一语义分类并接入四处使用者

新增 `Storage/Internal/DamengLobStoreType.cs`，使用 internal 静态 helper 和内部文本/二进制分类；只负责稳定的类型语义，不持有连接、driver、模型或服务器 profile。对分类输入做首尾空白规范化并用不变文化的大小写比较。别名集合取现有分类器的并集，不能由字符串前缀猜测类型。

- String/ByteArray mapping 的 Clone、显式存储类型检测与参数分支使用该 helper；保持所有 Clone 继承原 Driver，WithDriver 绑定和拒绝外来参数的合同不变。不要把一个 `_isLob` 真值无条件带到已改为普通类型的 Clone。
- Query 中保留 `DamengLobTypeClassifier.GetLobKind` 和既有调用接口作为薄适配层，转调 Storage helper，避免扩大所有查询文件的修改范围。
- ModelValidator 的键/索引判断转调同一 helper，不扩展属性遍历或其他校验。
- 不改 `DamengTypeMappingSource` 的原始 StoreType 保留逻辑，也不重写显式 `StoreType`、精度、长度或 converter。如果发现必须改这里，先停止说明理由，由协调者调整白名单。
- 原模板的 `_isLob` 与 JSON 行为先用对照测试冻结；本项不得把 JSON/JSONB 的参数改为 CLOB，亦不得让 BFILE 通过原来失败的映射查找。

**验证**：两个新增单元命令均 Completed、零失败/跳过；再执行 `git diff --check`。用 `rg -n 'DamengLobStoreType'` 限定上述两个 mapping、Query classifier 和 ModelValidator，四处均须存在实际使用；用 `git diff` 确认原各自的 LOB 别名列表已被委托替代，而非添加第五份列表。

### 4. 添加真实大值回读与守卫回归

先核对计划 021 已完成的验收证据及进入当前验收 worktree 的提交；若尚未进入，可继续准备本项测试，但不得启动正式实库验收或将旧入口结果标为完成。

在新增共享功能测试类中，沿用 `DamengLobGuardFunctionalTests` 的 `UseDamengUnderTest`、带 table 名的模型缓存键、唯一名称 Store 和 `finally` 精确清理结构。W 功能项目通过 `Compile Include="$(SharedFunctionalTests)/**/*.cs"` 自动编译此文件，不需改 csproj；W 单元项目没有该链接机制，已由独立 W 单元类覆盖。

用原始 `NCLOB`、`CLOB`、`BLOB` 声明创建自有临时表，EF 模型分别使用含空白/大小写变体和正常类型作对照。这能隔离“语义分类”与“服务器接受某个别名 DDL”两个问题。至少写入并更新 40 KiB 以上的文本和二进制值，文本含 CJK、引号及支持 profile 已验证的补充平面字符；使用确定性的非重复二进制内容，避免全零数组掩盖截断。覆盖 NULL、空值、小值和大值。

清空跟踪器后，用新 context 重新查询并逐字符/逐字节比较内容，同时比较长度；用参数化相等谓词核对返回的主键集合。单独对相等查询使用服务器既有函数长度范围内的小值，避免把服务器 `CLOB_MAX_CALC_LEN` 限制误当作本项错误；大值要求写入/更新/回读完整，不要求扩大现有搜索范围。捕获参数类型与命令数，只记录类型/长度/事件，不记录值、连接串、主机、用户名。

对含空白映射执行原已禁止的排序、分组、distinct/去重集合操作；用命令拦截器断言失败发生在发送前。有界文本/二进制的相应合法操作应正常执行。新增测试标记 `[Trait("AuditPlan", "024")]`，并遵循现有 `[DamengFact]`/`[DamengTheory]` 的环境与跳过合同。

**验证**：先运行官方实库命令，核对新增类出现在 TRX 且清理结果为零；完成后再运行 W 实库命令。两次均需 Completed、非零、总数=执行=通过、失败/跳过=0。测试运行中不得并发其他数据库通道；DDL 或清理结果未知则停止，不盲目重试。

### 5. 跑回归并记录限定结论

执行官方/W 完整 Release 单元命令。更新 `docs/compatibility.md`，明确此次补齐的是显式 LOB 类型首尾空白/大小写的一致性，区分全别名离线矩阵与实库仅 NCLOB/CLOB/BLOB 的已验 profile。不要把未跑的其他服务器、TLS、别名或 I01 查询形状列为支持。

在 `plans/evidence/024.md` 记录基线/最终 SHA 或未提交文件指纹、逐条命令、失败基线、最终 TRX 路径与总/执行/通过/失败/跳过、真实回读断言和精确清理结果；不得只写“测试通过”。协调者更新索引。

**验证**：`git diff --check` 退出0；`git diff --name-only`、`git diff --cached --name-only` 与 `git ls-files --others --exclude-standard` 的新增/修改集合完全落在白名单内（预置的计划文档按实施前登记的指纹核对不变，由协调者另外记账）。若已提交，还须对记录的实施起点至最终提交执行同白名单检查，不能以空工作区代替；所有测试 receipt 都满足下方标准。无需为了完成此计划执行发布或管理员全量套件。

## 测试矩阵

| 层次 | 必须覆盖 |
| --- | --- |
| 支持的类型别名 | 文本 CLOB/NCLOB/TEXT/NTEXT/LONG/LONGVARCHAR；二进制 BLOB/IMAGE/LONGVARBINARY。每项有正常、大写、小写、混合大小写、首尾空格、tab/换行组合；保留未支持 BFILE 的映射拒绝对照 |
| 映射及 Clone | FindMapping 实际入口、WithTypeMappingInfo、WithStoreTypeAndSize/Clone、WithDriver 同驱动绑定、WithComposedConverter；原始 StoreType 写法、Unicode/长度/精度/scale/converter 保留，driver 不丢失 |
| 参数 | 官方与 W 实际参数类型，NULL/空/小/大值、复用与克隆；文本为各自 Clob、二进制为 Blob；有界 VARCHAR2/NVARCHAR2/CHAR/BINARY/VARBINARY 保持原合同 |
| 模型 | 主键、备用键、普通索引的 LOB 拒绝；错误仍定位属性；有界键/索引正例。不扩展复杂属性校验遍历 |
| 查询 | 文本/二进制相等仍使用既有 LOB 比较路径；排序/分组/distinct 与现有去重集合守卫一致；有界正例。不要增加 IN、窗口表达式支持或新的 I01 结论 |
| JSON 及其他对照 | JsonElement 的现有 VarChar 参数、现有查询拒绝不变；时间、decimal 分面通过完整单元回归；不要把普通名称含 CLOB 子串的未知类型视为 LOB |
| 实库 | NCLOB/CLOB/BLOB 原类型与空白变体映射，新增/更新大值、新 context 完整内容及长度回读，小值参数相等返回正确 ID；原守卫发送前失败，finally 精确清理 |

分类 helper 的直接测试可补充所有已有分类别名，但不能用它替代 FindMapping→Clone→CreateParameter、模型和 ToQueryString 的可达路径测试。

## 完成条件

- [ ] 隔离分支包含审查基线，起始漂移已逐项核对，未修改旧工作区。
- [ ] 旧产品上的红灯来自参数类型/守卫语义断言，非编译、环境或筛选问题。
- [ ] 官方/W 新增单元及完整 Release 单元均 Completed、非零、零失败/跳过。
- [ ] 官方→W 串行的 Release `FullyQualifiedName~Lob` 功能回归均满足同一结果标准，新类确实被执行。
- [ ] 正式实库验收的实际源码版本已包含 021，验收证据记录该前置提交和自身源码指纹。
- [ ] 真实大值新增与更新后，用新 context 回读的长度、内容均完全相同；不得仅验证 SQL 文本或参数类型。
- [ ] 四个使用者共用 Storage 层语义分类；Storage 没有增加对 Query 的引用；显式 StoreType/facets/converter/driver 对照全部通过。
- [ ] JSON、BFILE、不支持查询形状与依赖边界未扩展，锁文件无差异。
- [ ] 每个自有临时对象清理为零；没有实例配置修改或其他对象清扫。
- [ ] `git diff --check` 退出0；变更只在白名单；`plans/evidence/024.md` 可对应到精确源版本和 TRX，索引交协调者更新。

## 停止条件

- 隔离 HEAD 不含基线，或白名单/直接依赖漂移使上述片段和接口不再成立。
- 红灯无法通过真实 mapping、模型或查询入口复现；不能为复现而制造只有私有 helper 才能出现的状态。
- 修复需要 Storage 引用 Query、改依赖、改公开 API、全局重写 StoreType、扩大别名/JSON/BFILE/查询支持，或触碰白名单外文件。
- 真实数据库不可用、所需 profile 不匹配、测试被跳过、还有其他代理占用共享数据库，或清理/DDL 结果未知。
- 两次合理修正后同一步仍失败，或服务器拒绝原本作为正常对照的类型/内容；保留证据交协调者，不降低断言。
- 修复让无空白正常类型、显式分面、converter、Driver 绑定或 JSON 参数合同改变。

## 维护说明

未来新增 LOB 别名时，应先确认映射支持范围，再更新唯一分类 helper，并同时补参数、模型、查询及必要实库证据。审查 Clone/WithTypeMappingInfo 的改动时，重点检查语义分类是否仍与显示类型分离，以及显式模板标志是否意外扩大 JSON 合同。I01 的 LOB IN/窗口等访问路径仍是独立调查，不能借本项“分类统一”宣称查询守卫已穷尽。
