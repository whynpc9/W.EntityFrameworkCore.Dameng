# 计划 022：保留模型快照中继承的 IDENTITY 种子与增量

> 执行者必须先读完本计划，再逐步执行验证。发生 STOP 条件时报告事实，不扩大修改范围。
> 本文件保留原实施指引；实际完成结果见 [022 证据](evidence/022.md)。`plans/README.md` 及本计划状态由协调者维护。
> 首先执行“步骤 1”的祖先与范围漂移检查；不得在当前旧工作区直接开始实现。

## 状态

- 状态：DONE；已在 [PR #16](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/pull/16) 合并，双驱动离线设计时验收通过。
- 优先级：P1。
- 工作量：M，包含双驱动的真实设计时快照往返夹具。
- 修改风险：MED；错误处理默认值会改变已有模型快照和下一次迁移的差异。
- 类别：bug / 设计时模型正确性。
- 对应发现：2026-11-08 深度审查 R02。
- 技术前置：无；不依赖 021，也不包含共享主键聚集注解发现。
- 编写基线：`5b1f6d407ac61acf7f5658addd551565aed34721`，2026-11-08，Asia/Shanghai。
- 编写时工作区：`/Users/wanghongyi/Projects/dameng-entityframework-core`，HEAD 为 `f626736`，比已核对的 `origin/main` 落后 35 提交。代码摘录来自固定基线的 Git 对象，不是该旧工作树。
- 规划时证据：静态调用链；当时尚未执行最小复现、构建或数据库测试。后续红绿灯与验收结果见 [022 证据](evidence/022.md)。

## 问题与目标

模型调用 `UseDamengIdentityColumns(5, 10)` 后，未覆盖属性分面的约定整数主键实际继承 `5/10`。最终化给该属性保存了 IDENTITY 策略，但没有保存属性级种子或增量。属性代码生成器遇到缺失注解便使用 `1/1`，生成的 `UseDamengIdentityColumn()` 又将这两个默认值显式写回属性，覆盖模型级配置。

因此，设计时生成并编译的模型快照可能与原模型不同，下一次没有业务改动的迁移仍产生自增分面变化；现有 SQL 生成器会拒绝修改自增种子或增量。目标是让公开 EF 设计时流程生成的快照在编译、重建之后保持相同的有效 IDENTITY 分面，第二次 scaffold 不产生迁移操作。只比较 fluent fragment 字符串不能完成本计划。

## 当前实现与约定

以下行号对应固定基线，实施时应重新定位符号。

1. `src/W.EntityFrameworkCore.Dameng/Metadata/Conventions/DamengValueGenerationStrategyConvention.cs:33-44` 写入有效策略，不物化 seed/increment：

   ```csharp
   var strategy = property.GetDamengValueGenerationStrategy();
   // 非 None 时完成兼容性检查。
   property.Builder.HasAnnotation(
       DamengAnnotationNames.ValueGenerationStrategy,
       strategy);
   ```

2. `src/W.EntityFrameworkCore.Dameng/Metadata/Internal/DamengPropertyExtensions.cs:113-128` 的有效值包含模型继承：

   ```csharp
   public static long GetDamengIdentitySeed(this IReadOnlyProperty property)
       => (long?)property[DamengAnnotationNames.IdentitySeed]
           ?? property.DeclaringType.Model.GetDamengIdentitySeed();

   public static int GetDamengIdentityIncrement(this IReadOnlyProperty property)
       => (int?)property[DamengAnnotationNames.IdentityIncrement]
           ?? property.DeclaringType.Model.GetDamengIdentityIncrement();
   ```

3. `src/W.EntityFrameworkCore.Dameng/Design/Internal/DamengAnnotationCodeGenerator.cs:51-57,95-101` 丢失上述继承信息：

   ```csharp
   var damengCall = GeneratePropertyValueGenerationCall(annotations);
   // IdentityColumn 分支：
   annotations.Remove(DamengAnnotationNames.ValueGenerationStrategy);
   var seed = GetIdentitySeedAndRemove(annotations) ?? 1L;
   var increment = GetIntAndRemove(annotations, DamengAnnotationNames.IdentityIncrement) ?? 1;
   return new MethodCallCodeFragment(
       PropertyUseIdentityColumnMethod,
       IdentityArguments(seed, increment));
   ```

4. `src/W.EntityFrameworkCore.Dameng/Metadata/Builders/DamengPropertyBuilderExtensions.cs:14-32` 的公共 `UseDamengIdentityColumn(long seed = 1, int increment = 1)` 总是设置两个属性注解。故 `UseDamengIdentityColumn(seed: 7)` 的含义是 `7/1`，不是“仅覆盖 seed 并继承 increment”。真正的单分面覆盖用公共元数据 API `propertyBuilder.Metadata.SetDamengIdentitySeed(7)` 或 `SetDamengIdentityIncrement(3)` 表达。

5. `test/W.EntityFrameworkCore.Dameng.Tests/DamengDesignTimeTests.cs:71-101,141-151` 是现有设计时测试范例：

   ```csharp
   modelBuilder.UseDamengIdentityColumns(seed: 5, increment: 10);
   modelBuilder.Entity<IdentityEntity>()
       .Property(entity => entity.Id)
       .UseDamengIdentityColumn(seed: 7, increment: 3);
   ```

   该测试只检查显式覆盖后的代码片段，未覆盖继承默认值的快照重建。

6. `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengCliTestHarness.cs` 已提供 `CreatePrivateDirectory()`、`CreateProcess()`、`RunAsync()`、`CaptureAssembly()`、`VerifyNestedCopies()`、`IdentityVerificationSource()` 和 `EnsureToolAsync()`。`CreateProcess` 清除数据库测试连接环境；`RunAsync` 为缺少配置的子进程设置可写 `DOTNET_CLI_HOME`，排空两个输出流，限制运行时间并清理自有目录。直接复用这些接口，不复制或弱化它们。

7. `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs:59-91,350-365,445-480` 展示“显式 restore/build → `dotnet-ef --no-build` → 再 build 新生成文件”的模式，以及本次被测提供程序程序集的直接引用。该测试随后会连接数据库；本计划只借用其设计时编排方式，不能调用整个功能测试作为离线往返夹具。

8. 基线官方单元 `.csproj` 已链接 `DamengCliTestHarness.cs`；W 单元 `.csproj` 没有链接它，也没有全局 `WDRIVER` 编译常量。W 单元项目已复制驱动 manifest。新增共享测试应使用公共抽象测试基类和两个具体派生类，不新增全局预处理常量，不把整个官方测试目录链接到 W 项目。

实现必须保持提供程序独立性、Dameng 前缀公共 API、EF Core 10 和驱动锁定范围。官方异步回退、W 禁止自动重试、双驱动模型隔离、已有 012/013 模型守卫都与本计划无关。纯设计时修复不增加任何真实数据库能力声明。

## 修改白名单

只允许修改或创建以下文件：

- `src/W.EntityFrameworkCore.Dameng/Design/Internal/DamengAnnotationCodeGenerator.cs`。
- `test/Shared/DamengIdentitySnapshotTestBase.cs`（新建，共享离线往返逻辑和测试矩阵）。
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengIdentitySnapshotTests.cs`（新建，官方驱动派生测试）。
- `test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverIdentitySnapshotTests.cs`（新建，W 驱动派生测试）。
- `test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj`（只链接上述共享测试文件）。
- `test/W.EntityFrameworkCore.Dameng.WDriver.Tests/W.EntityFrameworkCore.Dameng.WDriver.Tests.csproj`（只链接上述共享文件和既有 `DamengCliTestHarness.cs`）。
- `plans/evidence/022.md`（新建，真实执行完成后记录命令、源码身份、TRX、红绿灯和清理；不得预写通过结论）。

输出、日志和动态子项目只放标准忽略的 `artifacts/` 或夹具自行创建的私有临时目录。禁止修改依赖清单、任何锁文件、SDK 版本、CLI 公共 harness、驱动代码、模型约定、公共属性 API、SQL 生成器、共享主键聚集注解或已有 001–020 实施记录。`docs/compatibility.md` 若需要补充设计时证据，由协调者依据最终证据统一更新；不得把离线结果改写成实库通过。

## Git 与验证环境

在隔离分支 `codex/022-identity-snapshot` 实施。当前旧工作区不能直接快进、重置或覆盖；它有未跟踪目录，计划也在其中。可从固定基线另建工作树，或使用已核对的后继提交：

```bash
git worktree add -b codex/022-identity-snapshot ../dameng-efcore-plan022 5b1f6d407ac61acf7f5658addd551565aed34721
```

若分支/路径已存在，先检查其身份和未提交改动，不覆盖它们。将本计划作为只读执行输入传入，不依赖旧 `plans/README.md` 的状态。若协调者将本计划预置到新工作树，须在实施前单独登记其路径和指纹；范围检查将这份原样输入单独扣除，不允许执行者改动它。无需提交、推送、开 PR、合并或发布即可完成本计划的实现与证据准备；这些动作由后续明确授权决定。

统一测试入口已负责 CLI 环境和单节点参数。若必须直接运行 `dotnet`，先在当前任务 shell 中设置：

```bash
export DOTNET_CLI_HOME="$(mktemp -d "${TMPDIR:-/tmp}/dameng-plan022.XXXXXX")"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
```

后续直接 restore/build/test 均加 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false`，build/test 加 `--disable-build-servers`。不清理全局 NuGet 缓存。不读取 `.local-test.secrets.json`，不调用 `provision` 或数据库通道；不能以缺少秘密为由跳过本计划测试。当前锁定解析值为 EF Core `10.0.12`、官方驱动 `8.3.1.47463`、W 驱动 `0.3.1`，子项目应从相关 `packages.lock.json` 读取精确 `resolved` 值，不把范围下界或最新 NuGet 版本当作实际版本。

## 实施步骤

### 步骤 1：核对祖先、漂移和工作区

在拟实施工作树中首先执行：

```bash
git merge-base --is-ancestor 5b1f6d407ac61acf7f5658addd551565aed34721 HEAD
git diff --stat 5b1f6d407ac61acf7f5658addd551565aed34721..HEAD -- src/W.EntityFrameworkCore.Dameng/Design/Internal/DamengAnnotationCodeGenerator.cs test/Shared/DamengIdentitySnapshotTestBase.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengIdentitySnapshotTests.cs test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverIdentitySnapshotTests.cs test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj test/W.EntityFrameworkCore.Dameng.WDriver.Tests/W.EntityFrameworkCore.Dameng.WDriver.Tests.csproj plans/evidence/022.md
git status --short --branch
```

第一条必须返回 0；在已知旧 HEAD 上返回非零时，先按上文另建隔离工作树，再重新开始本步骤。范围 diff 为空是最简单的可接受结果；若非空，逐个比对本文摘录和测试依赖，出现不一致先 STOP。记录执行起点 SHA。工作树必须无别人的待提交修改，禁止将现有未跟踪目录整体加入提交。

**验证**：上述三条命令；祖先检查 exit 0、范围无未解释漂移、分支和起点已记录，才进入下一步。

### 步骤 2：建立现有离线基线

先读当前 `AGENTS.md` 和 `scripts/local-test/README.md`。按顺序执行：

```bash
scripts/local-test/run.sh test unit --filter 'FullyQualifiedName~DamengDesignTimeTests|FullyQualifiedName~DamengValueGenerationTests|FullyQualifiedName~DamengCliFixtureTests'
scripts/local-test/run.sh test unit --driver w --filter 'FullyQualifiedName~WDriverSharedColumnGenerationTests|FullyQualifiedName~WDriverConfigurationTests'
```

**验证**：两条命令均 exit 0，报告 `Completed`、总数大于零、执行数等于通过数、失败和跳过均为零。记录 TRX 路径；已有失败或工具缺失先调查，不把无关失败当成 R02 复现。

### 步骤 3：添加真实快照往返测试并取得缺陷红灯

创建白名单中的共享基类和两个驱动派生测试，统一标记 `[Trait("Plan", "022")]`。官方/W 各提供六个参数化用例，新增测试名应包含 `IdentitySnapshot`。共享基类通过派生类提供的提供程序名称、配置方法及程序集身份生成同驱动子项目；不要让官方生成模型在 W 服务中重建。

每个用例必须完整走以下过程：

1. 在 `CreatePrivateDirectory()` 创建的目录内写最小 net10.0 控制台项目、一个 `DbContext` 和一个实体。引用当前测试已加载的提供程序 DLL，引用锁定版本的 EF Design 和驱动；W 同时引用基础包 DLL 与扩展包 DLL。不从 NuGet 还原已发布的本提供程序来代替当前实现。
2. 上下文使用 `UseDameng((string?)null)` 或 `UseDamengWithWDriver((string?)null)`，并注册一个遇到连接打开就抛出固定异常的 `DbConnectionInterceptor`。通过公开 `IDesignTimeDbContextFactory<TContext>` 提供这个上下文；不读取环境连接、不接入宿主应用配置、不设置任何真实连接串。所有子进程用 `CreateProcess(..., connection: null)`。
3. 首次 restore 只在该临时项目解析锁定的精确版本，随后 build；所有命令遵循上文单节点参数。通过 `CaptureAssembly` / `VerifyNestedCopies` 验证输出 DLL，并让子进程运行 `IdentityVerificationSource` 验证实际加载的 MVID/SHA-256。W 另核对 manifest 中的驱动身份；版本正确但程序集不匹配必须失败。
4. 用 `EnsureToolAsync` 确认 `dotnet-ef` 与已解析的 EF 版本一致，执行 `migrations add InitialIdentity --context IdentitySnapshotContext --no-build`。这里是离线生成迁移，不是反向工程，不调用 `dbcontext scaffold`、`database update` 或 `Database.Migrate()`。涉及生成迁移时先读取 `skills/dameng-ef-migrations/SKILL.md`；不执行生成的 SQL。
5. 再 build，使真实生成的 `*ModelSnapshot.cs` 成为子项目程序集的一部分。通过该上下文的 `IMigrationsAssembly.ModelSnapshot` 取得真正快照，使用同一上下文提供的 `IModelRuntimeInitializer` 以 `designTime: true` 初始化，再获取关系模型；原模型取 `IDesignTimeModel.Model`。不得手写一个“预期快照”替代生成文件，也不能只通过 `context.Model` 对自身比较。
6. 对原模型与重建快照逐项检查表名、列名、有效策略、有效 seed/increment 和实际关系列注解；用 `IMigrationsModelDiffer.GetDifferences(snapshotRelationalModel, sourceRelationalModel)` 断言空列表。先断言两个模型对象不相同，且实际表/列映射存在，防止空模型假通过。
7. 对同一未改动的上下文再次执行 `migrations add NoChanges --context IdentitySnapshotContext --no-build`，再次 build；通过 `IMigrationsAssembly.CreateMigration` 构造第二个真实迁移，断言 `UpOperations`、`DownOperations` 都为空，重建最新快照与原模型仍零差异。重复生成不要求时间戳文件名相同，要求有效关系模型及操作稳定。
8. 所有分支在 `finally` 中精确删除该用例的临时目录，错误和断言不得跳过清理。保留脱敏后的运行摘要、身份结果和失败阶段，不能为了调试打印进程完整环境。缺工具/还原失败/编译失败/连接拦截失败都不是产品缺陷红灯。

两份 `.csproj` 均显式链接新的共享基类；官方继续使用已存在的 CLI harness 链接，W 额外链接同一既有 harness。两份具体测试类只负责真实驱动选择及程序集来源，不复制完整六例矩阵。项目不新增 PackageReference，不改锁文件；EF Design 只存在于运行时创建的临时项目中。

**验证**：产品代码尚未修改时执行：

```bash
scripts/local-test/run.sh test unit --filter 'Plan=022'
scripts/local-test/run.sh test unit --driver w --filter 'Plan=022'
```

预期两条均发现六个用例；全局 `5/10` 继承、仅 seed、仅 increment 的用例出现有效分面/模型差异断言失败，`1/1` 与显式双分面覆盖对照通过。记录每个失败的断言与原/快照值。若没有这个产品红灯，STOP，先核实假设，不能预先编写修复或弱化测试来制造红灯。

### 步骤 4：只修正属性代码生成的有效分面取值

在 `DamengAnnotationCodeGenerator.GenerateFluentApiCalls(IProperty, ...)` 将属性传给内部 `GeneratePropertyValueGenerationCall`。IDENTITY 分支先保留现有“读取并从 annotations 移除显式注解”的行为；缺失属性 seed/increment 时，分别回退到该属性的 `GetDamengIdentitySeed()` / `GetDamengIdentityIncrement()` 有效值。继续调用既有 `IdentityArguments`，保持默认 `1/1` 的简洁输出。不要改变模型级生成函数、Sequence 分支、显式 None 原始注解保留、公共 API 默认参数或运行时元数据解析。

该建议以步骤 3 确认的行为为前提。如果快照调用路径没有到达此函数，或需要改变模型约定才能生效，STOP 并提交实际调用证据，不在其他层补偿。

**验证**：重新执行步骤 3 的两条命令；每驱动六例全部 `Completed`，六例通过、失败/跳过均为零；每例包含首次真实快照零差异、第二次迁移无操作及第二次快照零差异。不得删除红灯所用断言。

### 步骤 5：执行回归并收集有限结论

```bash
scripts/local-test/run.sh test unit --filter 'Plan=022|FullyQualifiedName~DamengDesignTimeTests|FullyQualifiedName~DamengValueGenerationTests|FullyQualifiedName~DamengSharedColumnGenerationTests|FullyQualifiedName~DamengCliFixtureTests'
scripts/local-test/run.sh test unit --driver w --filter 'Plan=022|FullyQualifiedName~WDriverSharedColumnGenerationTests|FullyQualifiedName~WDriverConfigurationTests'
scripts/ci/verify-offline.sh
git diff --check
```

**验证**：所有命令 exit 0，指定聚焦及全量离线通道非零、零失败/跳过；最终离线脚本包含官方/W Release 单元、两组离线夹具、启动器自测、Python 回归及 W 包身份验证。不要固定沿用历史总数，记录当前实际 TRX 数量；新增 `Plan=022` 在两个单元通道都必须恰有六例。若只修了设计时代码并保持此范围，无需数据库测试；结论仅为双驱动离线设计时模型/快照证据。

### 步骤 6：核对范围并交付实施证据

写 `plans/evidence/022.md`：执行起点/最终源码 SHA 或未提交文件指纹、SDK/锁定依赖身份、十二例矩阵、缺陷红灯与最终绿灯、TRX 路径、嵌套程序集核对、第二次迁移零操作、临时目录清理结果。明确无数据库连接和无发布。不要在证据中声称本轮检查了真实 IDENTITY 值生成。

**验证**：执行以下范围检查及 `git diff --check`，均 exit 0，再交协调者更新索引。

```bash
python3 - <<'PY'
import subprocess
allowed = {
    'src/W.EntityFrameworkCore.Dameng/Design/Internal/DamengAnnotationCodeGenerator.cs',
    'test/Shared/DamengIdentitySnapshotTestBase.cs',
    'test/W.EntityFrameworkCore.Dameng.Tests/DamengIdentitySnapshotTests.cs',
    'test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverIdentitySnapshotTests.cs',
    'test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj',
    'test/W.EntityFrameworkCore.Dameng.WDriver.Tests/W.EntityFrameworkCore.Dameng.WDriver.Tests.csproj',
    'plans/evidence/022.md',
}
changed = set()
for args in (
    ['git', 'diff', '--name-only'],
    ['git', 'diff', '--cached', '--name-only'],
    ['git', 'ls-files', '--others', '--exclude-standard'],
):
    changed.update(subprocess.check_output(args, text=True).splitlines())
unexpected = sorted(changed - allowed)
assert not unexpected, unexpected
print('022 scope check passed')
PY
git diff --check
```

该脚本用于步骤 1 已确认干净的实施工作树，检查未提交实现。若协调者预置了未跟踪的本计划，先与登记的指纹核对完全相同，再仅从 changed 集合扣除 `plans/022-identity-snapshot-inheritance.md`；内容不同则停止，不将其加入实现白名单。若已按另行授权提交，另对记录的实施起点与最终提交执行同白名单 diff 检查，不能把提交后的空工作区当成“未改白名单外文件”的证据。

## 六例测试矩阵（每驱动相同）

每例均使用真实 `long Id` 主键和不同的隔离子项目，表名固定且不依赖真实数据库。

| 用例 | 公开配置入口 | 原模型有效 seed/increment | 原模型必须确认的注解状态 |
| --- | --- | --- | --- |
| 默认 | 不调用模型/属性自增 API，保留约定整数主键 | 1 / 1 | 属性策略为 IdentityColumn，属性 seed/increment 未显式配置 |
| 模型继承 | `modelBuilder.UseDamengIdentityColumns(5, 10)` | 5 / 10 | 属性 seed/increment 都为空，模型两值为 5/10 |
| 仅 seed 覆盖 | 模型 5/10；`id.Metadata.SetDamengIdentitySeed(7)` | 7 / 10 | 属性仅 seed=7，increment 注解为空 |
| 仅 increment 覆盖 | 模型 5/10；`id.Metadata.SetDamengIdentityIncrement(3)` | 5 / 3 | 属性仅 increment=3，seed 注解为空 |
| 双分面覆盖 | 模型 5/10；`id.UseDamengIdentityColumn(7, 3)` | 7 / 3 | 属性两个注解都是显式值 |
| 显式恢复 API 缺省 | 模型 5/10；`id.UseDamengIdentityColumn()` | 1 / 1 | 属性两个注解都显式为 1，不能错误继承模型 5/10 |

快照可为保持值而物化继承分面；验收的是重建后的有效关系模型等价，不要求快照继续保留原配置来源层级。原模型的注解状态断言用于保证用例确实覆盖继承与单分面输入。

## 完成标准

- [ ] 步骤 1 的祖先与范围漂移核对通过，执行起点与最终被测源码身份可追溯。
- [ ] 未修复版本在真实生成并编译的快照上复现非默认继承失败；合法对照有效，失败不是工具、连接或编译问题。
- [ ] 两驱动 `Plan=022` 各六例通过，实际运行官方/W 的服务及对应本次构建程序集。
- [ ] 每例真实快照初始化后存在预期表/列；有效 IDENTITY 分面一致；关系模型差异为空。
- [ ] 每例第二次 `migrations add` 后重新编译，真实迁移 Up/Down 操作均为空，最新快照再次零差异。
- [ ] 既有设计时、值生成、共享列与 CLI 夹具回归通过；`scripts/ci/verify-offline.sh` exit 0、所有适用 TRX 非零且零失败/跳过。
- [ ] 无真实连接、无数据库对象、所有用例私有目录清理完成；依赖及锁文件无变化。
- [ ] 范围检查与 `git diff --check` 通过，`plans/evidence/022.md` 只写实际证据；协调者收到索引状态更新所需材料。

## STOP 条件

- 固定基线不是实施 HEAD 的祖先，或范围漂移与摘录不符；不得把旧工作区偷偷切换或将后继改动回退。
- 真实快照流程未复现继承分面丢失，或上游 EF 在到达生成器之前已有不同处理；先报告，不实施猜测修复。
- 需要修改驱动、公共模型默认语义、模型约定、SQL 生成器、EF 框架或仓库锁定依赖才能通过。
- 需要读取真实连接配置、启动数据库、运行更新迁移、变更共享主键聚集策略或修复旧 012/013 问题。
- 嵌套进程实际使用已发布的提供程序、错版本 EF 工具、错 MVID/SHA-256；任何身份核对不能确立。
- 快照重建 API 与锁定版本不符、还原不可用或编译失败；不得以字符串断言或手写快照替代真实往返。
- 在一次合理修正后同一步验证仍失败，或需要修改白名单外文件；报告失败阶段、错误类型和脱敏日志位置。

## 维护说明

以后修改模型级默认值、属性注解或 fluent API 默认参数时，必须继续跑此完整往返矩阵。审查重点是“缺失属性分面”和“显式值为 1”的区别，以及快照是否引用本次构建的提供程序。新增更多快照支持可以单独立项；本计划不扩展复杂类型值生成、TPC 支持范围、共享主键存储策略或数据库 IDENTITY 运行时合同。
