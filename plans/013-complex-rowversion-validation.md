# 计划 013：查明复杂属性中的 rowversion 守卫是否可绕过

> 此项置信度较低，必须先运行平铺、单层和嵌套复杂属性模型，再决定修复。禁止因为循环只枚举直接属性就先写递归实现。上游确证阻断时保留测试与证据关闭；环境不足、没有构造出有效形状或测试跳过均不能算否证。主审维护索引，执行者不改 `plans/README.md`。

## 状态

- 状态：DONE
- 批次：M4
- 审查 ID：I06
- 优先级：P3
- 工作量：M
- 修复风险：MED
- 置信度：LOW；静态遗漏可见，但锁定 EF 下的可达性尚未验证。
- 类别：调查、模型校验、测试
- 依赖：`plans/012-shared-column-generation.md`。该项会先明确共享列模型与兼容性校验边界；本项不得回退其结果或将共享表误判为复杂类型。
- 编写基线：`c80c788fb3cd34d97626d578db580264a28a11f3`，2026-10-28。

## 目的

提供程序不自动生成 SQL Server 风格的 rowversion，平铺实体上的这种配置会在模型阶段被拒绝；显式计算列或触发器声明允许另外的生成机制。当前守卫只枚举实体直接属性，未递归 `ComplexProperty`。EF 10 暴露复杂类型属性的 rowversion 配置接口，但这不能证明最终化一定放行，因此首先确定真实可达行为。

如果缺口成立，应使相同配置在平铺、单层、嵌套复杂属性中保持同一校验合同，并正确定位所属实体、实际映射表、触发器和计算列。此项不实现完整复杂类型、JSON 复杂类型、复杂类型值生成或触发器自动创建支持。

## 当前实现与测试参照

`src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs:107-127`：

```csharp
foreach (var entityType in model.GetEntityTypes())
{
    foreach (var property in entityType.GetDeclaredProperties())
    {
        if (!property.IsConcurrencyToken
            || property.ValueGenerated != ValueGenerated.OnAddOrUpdate
            || property.GetComputedColumnSql() is not null
            || entityType.GetTriggers().Any())
        {
            continue;
        }
        throw new InvalidOperationException(
            $"The property '{property.DeclaringType.DisplayName()}.{property.Name}' is configured "
            + "as a row version, but Dameng does not automatically generate SQL Server-style "
            + "rowversion values. Use an application-managed concurrency token, a computed column, "
            + "or a database trigger.");
    }
}
```

`test/W.EntityFrameworkCore.Dameng.Tests/DamengModelValidatorTests.cs:72-95` 覆盖平铺拒绝和声明触发器后的接受。该文件 338-362 行的 fixture 模式为：

```csharp
modelBuilder.Entity<RowVersionEntity>().Property(entity => entity.Version)
    .IsRowVersion();
// 另一正向模型使用：
entity.Property(value => value.Version)
    .IsConcurrencyToken()
    .ValueGeneratedOnAddOrUpdate();
entity.ToTable("RowVersionEntities",
    tableBuilder => tableBuilder.HasTrigger("TR_RowVersionEntities_Version"));
```

新 fixture 参考同文件按 DbContext 类型区分模型的写法，使用 EF 10 `ComplexProperty(...).Property(...)` 的实际 builder。复杂属性 builder 不是必须能套用所有 `PropertyBuilder` 扩展；以当前锁定公开 API 为准，不通过不安全强制转换凑调用。验证应访问实际 `context.Model` / `IDesignTimeModel`，不能直接调用私有校验方法替代公开路径。

## 修改白名单与固定合同

只允许修改：

- `src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs`
- `test/W.EntityFrameworkCore.Dameng.Tests/DamengComplexRowVersionValidationTests.cs`（新建）
- `test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverComplexRowVersionValidationTests.cs`（新建）
- `docs/compatibility.md`
- `plans/evidence/013.md`（新建）
- 本计划文件，仅状态和结果。

范围外：Metadata 值生成 convention、共享列注解输出、类型映射、更新 SQL、驱动服务、真实触发器实现、测试环境工具、项目/锁文件、其他复杂类型能力及发布配置。不把本项扩为“递归修复所有 validator”，不引入新的公共 API。

保留官方/W 的驱动隔离、原有计算列与触发器声明边界、不新增自动重试、不修改任何服务器设置。不读取或输出真实凭据。本计划主要是离线模型合同；如果调查证明必须改变实际存储、读取或触发器执行行为，则停止并上报，需要另行补充范围与真实库回归，不能凭模型通过宣称运行时支持。

如仅为观察模型生成的 DDL 而调用 `GenerateCreateScript`，先读 `skills/dameng-sql/SKILL.md` 和 `skills/dameng-ef-migrations/SKILL.md`；生成 DDL 不得执行，不把它作为真实服务器证据。任何后来授权的数据库验证都必须走统一入口、两驱动串行、对象唯一命名、`finally` 精确清理，DDL 不依赖事务回滚。

## Git 与命令

使用隔离 `codex/013-complex-rowversion-validation` 分支，先识别工作区已有改动。不得自动 commit、push、创建 PR、合并或发布。由主审更新索引。

统一入口已设置可写临时 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`，并使用锁定还原、`-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`。额外 dotnet 调用先配置相同环境/标志；不清 NuGet 缓存。以下运行是默认配置，不能称为 Release 证据；M1 的配置入口工作无需在本项重复实现。

| 验证 | 命令 | 预期 |
| --- | --- | --- |
| 官方本项 | `scripts/local-test/run.sh test unit --filter 'Plan=013'` | 非零数、Completed、无失败/跳过 |
| W 本项 | `scripts/local-test/run.sh test unit --driver w --filter 'Plan=013'` | 同上 |
| 前项及既有模型 | `scripts/local-test/run.sh test unit --filter 'Plan=012|FullyQualifiedName~DamengModelValidatorTests|FullyQualifiedName~DamengValueGenerationTests'` | 全通过 |
| 全部官方单元 | `scripts/local-test/run.sh test unit` | Completed、无失败/跳过 |
| 全部 W 单元 | `scripts/local-test/run.sh test unit --driver w` | Completed、无失败/跳过 |

新测试统一 `[Trait("Plan", "013")]`。前置 012 的测试过滤命中数须大于零，不能因为缺少前置测试而算依赖验证通过。

## 执行步骤

### 1. 验证漂移与前置计划

```bash
git status --short
git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengComplexRowVersionValidationTests.cs test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverComplexRowVersionValidationTests.cs docs/compatibility.md plans/evidence/013.md plans/013-complex-rowversion-validation.md
scripts/local-test/run.sh test unit --filter 'Plan=012|FullyQualifiedName~DamengModelValidatorTests|FullyQualifiedName~DamengValueGenerationTests'
```

`c80c788fb3cd34d97626d578db580264a28a11f3` 是原始审查锚点。预期前项状态/证据和本轮回归一致；先读取已验收前置计划的实际差异，更新本计划摘录定位和实施 SHA 后继续。012 修改同一个 validator 属于可归因的预期漂移，不触发停工，必须保留；只有非预期差异、未解释的调用链变化、契约冲突或依赖不完整才暂停核对，不覆盖前项实现。新证据文件记录实际 HEAD、依赖结果、锁定 EF 版本和当前三个形状的待测矩阵。

### 2. 先证明公开模型路径的可达性

创建平铺实体、单层引用型 ComplexProperty、两层嵌套引用型 ComplexProperty 三种 fixture。每种提供普通标量对照、应用管理并发令牌、`.IsRowVersion()`。优先使用非空复杂属性与非空 byte[] Version，消除可空复杂类型本身的上游限制；待核心形状验证后再加入受当前 EF 支持的可空边界，不能用无关可空性错误替代 rowversion 拒绝。

测试名称至少包含：

- `ComplexRowVersionBaselineBuildsOrdinaryScalarModel`：单层/嵌套普通属性确实进入关系模型。
- `ComplexRowVersionBaselineAcceptsApplicationManagedToken`：仅 IsConcurrencyToken、ValueGenerated.Never 不误拒绝。
- `UnsupportedRowVersionIsRejectedForFlatAndNestedProperties`：平铺、单层、两层的无计算列/无触发器配置应有同等拒绝。
- `ComputedComplexConcurrencyTokenUsesExistingExceptionRule`：合法计算列声明继续按现有例外处理。
- `TriggerDeclaredComplexConcurrencyTokenUsesOwningEntity`：所属实体有明确触发器声明时沿用既有例外。
- `UnrelatedEntityTriggerDoesNotAllowComplexRowVersion`：另一张表/实体的触发器不能为该复杂属性放行。
- `TwoComplexInstancesAreValidatedIndependently`：同一 CLR 复杂类型在两个属性路径出现，不能因类型去重漏掉第二条路径。

先不改产品代码运行两个 `Plan=013` 命令。若目标模型被上游规则拒绝，记录异常类型、具体 API、拒绝时机以及同形状普通模型能否成功。若 model 构建成功而平铺应拒绝的配置被复杂路径放行，记录属性 `DeclaringType`、拥有者路径、ValueGenerated/IsConcurrencyToken 和实际表映射，不记录业务值或连接信息。

本步骤允许出现明确“期望 rowversion 拒绝但模型成功”的调查红灯。若普通复杂属性基线就失败，不能宣称漏洞已被证明或否证；先确定该形状是否受锁定 EF 支持，再决定是否需要主审修订调查范围。禁止泛化 `Assert.ThrowsAny<Exception>` 掩盖无效 fixture。

### 3. 确认缺口后才递归实施同一合同

若缺口可达，只改 `DamengModelValidator`：遍历实体及其声明的复杂属性树中的标量属性，并保留所属实体/实际表上下文；调用同一个 rowversion 判断，避免复制多份分支。不得递归导航到其他实体，也不得按 CLR 类型去重导致两个映射路径只校验一次。异常应包含完整复杂属性路径和所属实体，便于定位。

计算列与触发器例外必须对应属性的实际映射对象；当表片段/继承/共享表使归属不明确时，不把任意实体触发器视为充分证明，应先最小模型表征并报告需要扩大规则的情况。不要建立一个会查询真实数据库触发器的模型校验器；HasTrigger 仍只是调用方元数据声明。

如果锁定 EF 已准确拒绝所有本项目标配置，保留可重复负例/正例与拒绝阶段证据，以“不成立：上游已阻断所测形状”关闭，不加冗余产品代码。若部分路径可达，只修复已证实部分并写明剩余拒绝边界；不把未测试路径写成支持。

验证：两个 `Plan=013` 命令 → 全通过、非零数、无跳过；平铺原异常契约与已有触发器/计算列例外保持。修复分支应有最小失败到通过记录；关闭分支须有每种形状的可重复阻断测试，而不是只写解释文字。

### 4. 回归并交付明确范围的证据

运行命令表中的前项回归和两套完整单元。再执行：

```bash
git diff --check
git status --short
rg -n 'Plan.*013|FlatAndNested|OwningEntity|UnrelatedEntity|Independently' test/W.EntityFrameworkCore.Dameng.Tests/DamengComplexRowVersionValidationTests.cs test/W.EntityFrameworkCore.Dameng.WDriver.Tests/WDriverComplexRowVersionValidationTests.cs
```

预期：全部测试通过，diff 检查退出 0，关键测试及 Trait 存在，新增改动均在白名单。证据文件记录两个驱动的模型结果、实际拒绝来源、TRX、测试配置、当前 HEAD 和修复/否证结论。`docs/compatibility.md` 只注明模型校验覆盖，明确没有新增复杂类型/触发器真实服务器支持声明；不改写历史服务器验收计数。

## 完成标准

- [x] 平铺、单层、嵌套的普通标量正例和 rowversion 候选都经过真实 EF 模型最终化。
- [x] 应用管理令牌、计算列、拥有者触发器、无关实体触发器、两个相同 CLR 复杂类型路径有独立断言。
- [x] 结果是已验证修复，或完整候选被上游准确拒绝的可重复否证；环境不足明确标 BLOCKED。
- [x] 官方/W 聚焦单元与完整单元 Completed、无失败/跳过；012 与现有模型回归未退化。
- [x] 异常能够定位属性路径；没有新增真实数据库调用、公共 API 或复杂类型功能承诺。
- [x] 证据文件和本计划状态更新，文档准确区分模型证据与实库证据，`git diff --check` 通过。

## STOP 条件与维护

若当前 EF 的复杂属性 API 与预期不同、上游阻断原因无法区分、拥有者/表片段归属需要改变现有公共语义，或测试要求修改白名单外文件，停止并报告。除明确初始调查红灯外，同一验证合理修正一次仍失败时停止。不得通过取消并发标记、改 ValueGenerated、忽略异常或删除复杂属性使回归通过。

后续 EF 复杂类型 API、继承/表拆分规则、触发器元数据或计算列映射改变时重跑本组。审查重点是递归访问的映射路径而非 CLR 类型，以及“模型接受”与“服务器实际生成版本值”的区别。不自动提交、推送、合并或发布。

## 批次交付与统一证据

本计划属于 M4，启动实施前须确认 M3 已完成整批验收；正文技术前置之外仍保留这一批次门。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/013.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为调查：确认后修复并验证，或用充分可重复证据确证无缺陷；缺环境、跳过和单次未复现不能闭合。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M4.md` 的输入。

## 本轮实施结果（2026-10-28）

从已合并 M3 `eb79cc8fb8b91e8cb2d6fb4ede255c61759b4056` 开始，在 `codex/m4-model-consistency` 串行集成；M1–M3 的漂移可归因，未回退。调查确认缺口并已补充精确模型校验，合法对照保持。正式证据见 [执行记录](evidence/013.md)；最终批次与远端门见 [M4](evidence/M4.md)。仅 M4，不启动 M5。

实施提交：`485b5c44ccd147a967a4e85ac76791050a983406`。最终产品/测试指纹、独立审查和 PR #12 远端验收见 [M4](evidence/M4.md)；未新增实库能力声明，不启动 M5。
