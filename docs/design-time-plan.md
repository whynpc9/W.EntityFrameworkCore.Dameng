# 设计时实现计划

状态：已执行完毕，2026-09-30 全量回归通过。编制于 2026-09-29，交付结果以[兼容性矩阵](compatibility.md)为准。

依据：[issue #1 的阶段 1](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/issues/1)、[兼容性矩阵](compatibility.md)（2026-09-22 基线与 2026-09-29 增量）。当前锁文件解析为 EF Core `10.0.12` 和 `DM.DmProvider` `8.3.1.47463`。本计划不修改依赖范围，不涉及查询翻译和写入批次。

阶段 1 的目标是：让 `dotnet ef dbcontext scaffold` 可用，并让模型注释落到库上。编制计划时核对了提供程序源码、现有测试和两个项目 skill（`skills/dameng-sql`、`skills/dameng-ef-migrations`）。下文每一项落地前都要先在参考库上确认目录结构或函数语义，不能把手册形态当成服务器事实。

## 1. 评估结论与取舍

issue 阶段 1 的十条全部可行，没有需要拒绝的项目；区别在于证据门槛和先后依赖。

| issue 中的功能点 | 决定 | 价值与理由 |
| --- | --- | --- |
| 注册 `IDatabaseModelFactory` 反向工程 | 核心交付，拆 v1/v2 | EF Core 反向工程的标准入口；设计时服务框架已在，缺的就是这一个服务 |
| 当前模式判定 `SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())` | 首批 | 同一 SQL 已在[历史表仓库](../src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengHistoryRepository.cs)真实执行过，直接复用，无探测风险 |
| 读取表、列、默认值、注释、主键、唯一约束、索引（含升降序） | v1 范围 | 目录查询是唯一大不确定点：达梦 `SYS.SYSOBJECTS` / `SYS.SYSCOLUMNS` / 注释目录的字段形态必须先在参考库探测，再写查询 |
| 视图、外键 | v2 范围 | issue 已建议放阶段后半；外键涉及约束目录与删除规则映射，视图只是多一类对象 |
| 列类型分面保留、`INTERVAL DAY TO SECOND` 还原 | v1 范围，公式先核对 | [类型映射源](../src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengTypeMappingSource.cs)已有完整映射表和 `ParseStoreTypeName`；TimeSpan 映射 `INTERVAL DAY(9) TO SECOND(6)` 已真实验证，反向还原公式需在参考库核对 |
| 自增识别 `IDENTITY` 与 `序列.NEXTVAL` 默认值 | v1 范围 | [注解代码生成器](../src/W.EntityFrameworkCore.Dameng/Design/Internal/DamengAnnotationCodeGenerator.cs)已能生成 `UseDamengIdentityColumn` / `UseDamengSequence` 调用，反向工程只需设置注解；注意 issue 的边界：仅存在同名序列不算自增，必须列默认值确实是 `序列.NEXTVAL` |
| 标识符按目录原始拼写加引号 | v1 范围 | [SQL 生成助手](../src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengSqlGenerationHelper.cs)的双引号引用已真实验证；反向工程不强制大写即可 |
| 模式存在性查 `SYSOBJECTS` 且 `TYPE$ = 'SCH'` | 小项，随迁移注释同批 | `CREATE SCHEMA` 已真实执行，缺的只是存在性判断；用于幂等守卫，避免重复 `CREATE SCHEMA` 报错 |
| 迁移生成 `COMMENT ON TABLE` / `COMMENT ON COLUMN` | 首批 | [迁移 SQL 生成器](../src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs)目前完全没有注释处理；达梦 `COMMENT ON` 与主流一致（skill 速查确认）；这是 scaffold 出 `HasComment` 后能落库的闭环前提 |
| 字符串长度语义核对 | 先探针，再定实现 | 参考实例 `LENGTH_IN_CHAR=0`（按字节）。默认 `NVARCHAR2(n)` 若已是字符语义则保持；显式 `VARCHAR(n)` 按字节会截断中文时，生成 `VARCHAR(n CHAR)` 或在[模型验证器](../src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs)拒绝有损长度。两条路取哪条由探针结果决定 |
| `dotnet ef` 命令行端到端 | 末批 | 进程内三条路径已真实执行；命令行回归依赖反向工程和注释落地完成后才有完整意义，但 `migrations add` / `script` / `database update` 的 CLI 切片可以先行 |

不纳入本阶段：issue 阶段 2、3 的内容；达梦或驱动做不到的结构继续维持拒绝（见兼容性矩阵"不支持"行）。

## 2. 源码基线

- [设计时服务](../src/W.EntityFrameworkCore.Dameng/Design/Internal/DamengDesignTimeServices.cs)已通过 `DesignTimeProviderServices` 特性暴露，注册了 `IAnnotationCodeGenerator` 和 `IProviderConfigurationCodeGenerator`，唯独没有 `IDatabaseModelFactory`。反向工程落地就是在这里补一行注册，加上新建 `DamengDatabaseModelFactory`。
- [脚手架代码生成器](../src/W.EntityFrameworkCore.Dameng/Scaffolding/Internal/DamengCodeGenerator.cs)已生成 `UseDameng` 配置调用，scaffold 产物的提供程序接线侧已就绪。
- [历史表仓库](../src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengHistoryRepository.cs)的存在性查询连接 `SYS.SYSOBJECTS` 并用 `SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())` 解析当前模式，是反向工程目录查询的现成范式。
- [迁移 SQL 生成器](../src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs)对 `EnsureSchemaOperation` 直接生成 `CREATE SCHEMA`（约 209 行），无存在性分支；全文无 `COMMENT ON` 生成（现有 "Comment" 匹配只是 `/` 终止符的词法检查）。
- [模型验证器](../src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs)已有 LOB 键长度、标识列、decimal 分面等验证入口，字符串长度语义的拒绝路径落在同类方法里。
- 测试基建齐备：单元测试项目已有 `DamengDesignTimeTests`、`DamengMigrationsSqlGeneratorTests` 等；功能测试项目有 `DamengScriptExecutor` / `DamengScriptTestDatabase` 支持真实库脚本执行；本地回归统一走 `scripts/local-test/run.sh test <unit|functional|specification|admin|probes|all>`。
- `dotnet ef` 命令行进程没有端到端回归（[迁移操作说明](migrations.md)第 8 行明确记载）。

## 3. 实施工作包

### D0：目录与语义探针（先行，阻塞 D3/D5）

交付：功能测试项目中的探针测试与结果记录，不改提供程序行为。

- 探测当前模式下表、列、默认值、注释、主键、唯一约束、索引（含升降序）在 `SYS.SYSOBJECTS` / `SYS.SYSCOLUMNS` 及相关目录中的实际字段形态；确认注释目录是 `SYSCOMMENTS` 还是兼容视图。记录实例版本与 `V$DM_INI` 参数。
- `INTERVAL DAY TO SECOND` 从目录读回的标度到天精度/秒小数位的还原公式，用多个精度组合核对。
- 字符串长度语义：在 `LENGTH_IN_CHAR=0` 实例上核对 `NVARCHAR2(n)` 是否字符语义、`VARCHAR(n)` 与 `VARCHAR(n CHAR)` 对中文的截断差异。
- 探针成功不等于功能验收；结果写入兼容性矩阵的证据记录。

验收：探针在参考库通过，结论可供 D3/D5 直接引用；不产出任何支持声明。

### D1：迁移生成 `COMMENT ON TABLE` / `COMMENT ON COLUMN`

交付：迁移 SQL 生成器处理表/列注释注解，单元测试 + 真实库回归。

- `CreateTableOperation`、`AlterTableOperation`、`AlterColumnOperation` 上的 `Comment` 注解生成 `COMMENT ON TABLE "模式"."表" IS '...'` 与 `COMMENT ON COLUMN ... IS '...'`；注释变更时重新 `COMMENT ON` 覆盖旧值；清除注释生成 `IS ''` 或 `IS NULL` 的形态由 D0 探针或手册确认后锁定。
- 字符串字面量按现有转义规则处理；注释文本进幂等脚本时走同一套 `EXECUTE IMMEDIATE` 转义和 32767 字节上限。
- 单元测试断言生成 SQL；真实库回归验证注释写入目录且可被读回（为 D3 的注释反向工程提供对照）。

验收：`HasComment` 经 `GenerateCreateScript()`、`GenerateScript()`（含幂等）、`Database.Migrate()` 三条路径落库；兼容性矩阵更新为真实环境已验证。

### D2：模式存在性判断

交付：`EnsureSchemaOperation` 及幂等守卫中的存在性检查。

- 查 `SYS.SYSOBJECTS` 且 `TYPE$ = 'SCH'`，存在则跳过 `CREATE SCHEMA`。
- 单元测试断言守卫 SQL 形态；真实库回归覆盖"模式已存在时脚本可重复执行"。

验收：幂等脚本对已有模式不再报错；矩阵"模式创建与不常见 DDL"行更新。

### D3：反向工程 v1（当前模式：表、列、默认值、主键、唯一约束、索引、注释、自增）

交付：`DamengDatabaseModelFactory` 与设计时注册，单元测试 + 真实库 scaffold 回归。

- 注册 `IDatabaseModelFactory`；连接后先用 `SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())` 判定当前模式，只扫当前模式的对象（连接串模式与登录用户可不同）。
- 目录查询按 D0 结论实现：表、列（类型、可空、默认值）、表/列注释、主键、唯一约束、普通索引含升降序。
- 列类型保留目录中的精度、标度和字符长度；`INTERVAL DAY TO SECOND` 按 D0 公式还原为映射声明。
- 自增识别：目录中的 `IDENTITY` 标记 → `UseDamengIdentityColumn`；列默认值形如 `序列.NEXTVAL` → `UseDamengSequence`；两种都不命中则不设值生成，仅存在同名序列不得当作自增。
- 标识符按目录原始拼写保留，不强制大写。
- 真实库回归：在独立测试用户下建一组覆盖上述结构的表，运行反向工程，断言 `DatabaseModel` 的表、列、分面、注释、约束、索引和值生成策略。

验收：`dotnet ef dbcontext scaffold` 之外的核心服务在真实库通过；矩阵"反向工程"行从"未实现"改为"部分支持"，明确列出 v1 不含视图和外键。

### D4：反向工程 v2（视图、外键）

交付：扩展目录查询，补视图定义与外键（含删除规则）。

- 视图作为无键只读实体 scaffold；外键从约束目录还原主表/从表列序与 `ON DELETE` 行为。
- 真实库回归覆盖自引用、复合外键和级联删除。

验收：矩阵"反向工程"行更新已验证范围。

### D5：字符串长度语义落地

交付：按 D0 结论二选一，并配单元与真实库证据。

- 若 `VARCHAR(n)` 按字节且中文截断成立：显式非 Unicode 字符串生成 `VARCHAR(n CHAR)`，或在模型验证器对会截断的显式长度抛出有信息量的错误。两路的取舍以"不静默截断"为底线，默认倾向生成 `VARCHAR(n CHAR)`。
- 若 `NVARCHAR2(n)` 已是字符语义：保持现状，只补证据。
- 反向工程侧同步：目录读回 `CHAR` 语义长度时还原为对应的 EF 长度。

验收：中文长度边界用例在真实库通过；矩阵"常见标量映射"或单列一行更新。

### D6：`dotnet ef` 命令行端到端

交付：命令行回归通道，覆盖 `migrations add`、`migrations script`（含 `--idempotent`）、`database update`、`dbcontext scaffold`。

- 在 `scripts/local-test` 增加一个通道（或在功能测试中以进程方式调用 `dotnet ef`），使用临时项目与独立测试用户；环境需具备 dotnet-ef 工具，缺失时明确跳过而非判通过。
- scaffold 产物断言：`UseDameng` 接线、`HasComment`、自增 fluent 调用、标识符原样。

验收：四条命令在真实库端到端通过；矩阵"设计时迁移代码生成"行的"命令行端到端覆盖仍然有限"改写为已验证范围。

## 4. 顺序与依赖

```text
D0 探针 ─┬─> D3 反向工程 v1 ─> D4 反向工程 v2 ─┐
         └─> D5 字符串长度语义                  ├─> D6 dotnet ef 端到端
D1 注释迁移 ─> D2 模式存在性 ────────────────────┘
```

D1/D2 与 D0 互不阻塞，可并行；D3 依赖 D0 的目录结论；D6 依赖 D1、D3、D4 完成后的闭环才有完整价值（其中 `migrations` 三条命令的 CLI 切片可提前到 D1 之后先行回归）。

## 5. 总体验收

- 每个工作包落地时更新[兼容性矩阵](compatibility.md)，区分单元级证据与真实服务器证据；跳过数据库测试不作为完成证据。
- 真实库测试走 `DAMENG_TEST_CONNECTION_STRING` 与 `scripts/local-test/run.sh` 现有通道；DDL 测试用唯一对象名并在 `finally` 精确清理，不依赖回滚。
- 不在输出中打印连接串；不改动锁定的 EF Core / `DM.DmProvider` 版本范围。
- 反向工程全部落地后，issue 阶段 1 的十条应全部关闭或在矩阵中有明确的已验证范围。

## 6. 执行结果（2026-09-30）

- D0 探针确认了目录形态：`ALL_TAB_COLUMNS`/`ALL_CONSTRAINTS`/`ALL_IND_COLUMNS` 等 Oracle 兼容视图可用；`INTERVAL DAY TO SECOND` 的 `DATA_PRECISION`/`DATA_SCALE` 即天精度与秒小数位；`NVARCHAR2(n)` 是字符语义、`VARCHAR(n)` 按字节、`VARCHAR(n CHAR)` 可用；注释清除用 `IS ''`（`IS NULL` 是语法错误）；`IDENTITY` 列由 `SYS.SYSCOLUMNS.INFO2 = 1` 标记，种子/增量经 `IDENT_SEED`/`IDENT_INCR` 还原；`CREATE SCHEMA IF NOT EXISTS` 不受支持，守卫块可用且服务器不接受把块再包进 `EXECUTE IMMEDIATE`。
- D1–D5 按计划落地：迁移生成 `COMMENT ON TABLE/COLUMN`；`EnsureSchema` 生成 `SYS.SYSOBJECTS` 守卫块（幂等模式下块原样透传）；`DamengDatabaseModelFactory` 注册并覆盖表、视图、列、默认值、注释、约束、索引、外键与两种自增模型；非 Unicode 定界字符串改生成 `VARCHAR2(n CHAR)` / `CHAR(n CHAR)`，`ParseStoreTypeName` 识别 `CHAR` 限定。
- D6 发现并绕过两个工具链问题：dotnet-ef 10.0.3 在 Unix 上把构建输出写到字面量 `bin\Debug` 目录导致自身构建失败（改用 `artifacts/dotnet-ef-tool` 下与锁定 EF Core 版本匹配的本地工具）；`dotnet ef` 的进程内项目评估/构建在 `dotnet test` 宿主下会触发 NuGet `LockFile.GetTarget` 空引用（改为显式 `dotnet restore` + `dotnet build` 后全程 `--no-build`，新增迁移后须重新构建再 `script`/`update`）。
- 测试执行器 `DamengScriptExecutor` 的拆批逻辑改为按 `BEGIN`/`END;` 深度配对，容纳历史守卫内嵌套的 `EnsureSchema` 守卫块；PR 审查后进一步跟踪引号与块注释，字符串字面量内的 `BEGIN`/`END;` 行不再截断批次。
- PR 审查后收口：外键按主表 `OWNER` 判定并跳过跨模式主体，列解析不完整的整条外键不收；`IDENT_SEED`/`IDENT_INCR` 按 `模式.表` 限定（两部分各自分隔引用，带点表名已验证），补 `SET SCHEMA` 分离用例；非当前模式的 `--schema` 过滤与全属其他模式的表过滤改为抛 `NotSupportedException`；非 Unicode 字符串的 ` CHAR` 限定按 `字符数 × 4` 字节预算约束在行内上限内，超出回退 CLOB（定长拒绝），键/索引默认长度统一 450 字符以在最小 4 KB 页上可建表；CLI 测试的失败/超时异常同样脱敏连接串，双管道并行排空，迁移历史表使用独立名称。
- 第三轮 PR 审查收口（2026-09-30）：`NEXTVAL` 默认值不再直接转换为序列策略——先按 `ALL_SEQUENCES` 读入被引用序列的真实 facet（起点取 `LAST_NUMBER`，即序列下一个待发放值；另有增量、上下界、循环标志）写入 `DatabaseModel.Sequences`，EF 脚手架管道 `RelationalScaffoldingModelFactory.VisitSequence` 消费后 scaffold 出带 facet 的 `HasSequence`，重建模型不再虚构默认 facet；跨模式或读不到 facet 的引用保留原始默认值表达式，不再生成序列策略。`TIME`/`TIMESTAMP`/`DATETIME`（含 `WITH TIME ZONE` 变体）的 `DATA_SCALE` 为零时保留 `(0)`，不再回落为服务器默认精度。`--table` 过滤按标识符组成部分解析（分隔引号内的点不作模式分隔符，`""` 转义还原），`APP."A.B"` 与 `"MY.SCHEMA".T` 均能匹配目录名。真库回归新增：序列 facet（含 `MAXVALUE`/`CYCLE`）读回、零小数秒精度、带点分隔名过滤、跨模式序列默认值保留原始表达式；探针补充全范围 `MINVALUE 1 MAXVALUE 9223372036854775807` 显式拼写可被服务器接受的证据，保证 scaffold 重建的 `CREATE SEQUENCE` 可执行。
- 第五轮 PR 审查收口（2026-10-01）：非 Unicode `CHAR` 语义预算从页派生声明上限（32767）收紧到文档的 32 KB 页字符列上限 8188 字节（`n × 4 ≤ 8188`，即 2047 字符），更大回退 CLOB（定长拒绝）。容量探针仅作探索记录，SQL 错误不会导致探针失败，因此不作为填充容量或页边界的验收证据。EF 级真库回归验证 2047 字符 `VARCHAR2(2047 CHAR)` 往返与 2048 回退 CLOB。`TIMESTAMP WITH LOCAL TIME ZONE` 的目录 `DATA_SCALE` 带 4096 偏移（探针：缺省 4102、`(0)` 4096、`(3)` 4099），`BuildStoreType` 按偏移还原精度。
- 第四轮 PR 审查收口（2026-10-01）：幂等生成与测试执行器的匿名块识别归一化为忽略前导空白与关键字大小写（`BEGIN`/`DECLARE`，含关键字边界，`BEGINNING` 不误判），本轮覆盖的 `migrationBuilder.Sql` 块原样透传，真库脚本回归插入行验证块被执行且幂等。表达式/函数索引按探针证据处理：目录 `ALL_IND_COLUMNS` 对其报基列名且 `COLUMN_POSITION=-1`、`DESCEND` 不可靠（升序也报 DESC），整条索引丢弃，只保留可完整解析的普通索引。`LAST_NUMBER` 语义补消费后证据（NEXTVAL 取 41 后目录报 44，重建起点随之迁移）；序列 facet 任一超出 EF 可表示范围（增量 int、上下界/起点 long）改为放弃 facet 保留原文，不再让单个异常序列中断整个反向工程。`CHAR` 预算在本轮仍使用 32767 的声明上限，该实现已被第五轮的 8188 字节保守生成预算替代。32767 不是通用行内容量或填充边界，单独建表探针不构成存满验证。
- 第六轮 PR 审查（2026-10-02）：核对整个 PR 的设计时服务、目录读取、类型分面、迁移注释/模式守卫、字符长度映射与 CLI 链路。模式/表过滤及 NEXTVAL 名称统一按未引用部分大写、引用部分原样处理。匿名块与测试拆批共享词法读取器，跳过前导注释，保留字符串/引用标识符，支持同一行与嵌套块，并区分 CASE、END IF/LOOP；不完整匿名块明确报错。移除无断言容量探针在兼容性矩阵及技能中的验收表述。新增聚焦回归 20/20（含真实库过滤与注释开头块执行）、单元 339/339、规范冒烟 4/4、管理员迁移脚本 4/4 均通过，格式检查通过。初版夹具因参考库拒绝仅大小写不同的同名对象而失败，改为不同名称后通过，原失败记录保留。全量功能套件及最新 head 的远端复审结论见 PR #3 的验证记录，不把历史绿色检查视作本轮验收。
- 第七轮 PR 审查（2026-10-02）：上一提交 `167340d` 的完整功能套件 106/106、单元 339/339、规范冒烟 4/4、管理员 4/4 通过，原有 19 条 review 线程逐条核验并关闭。最新复审新增的禁用约束语义通过预检查解决：仅对选中表的主键/唯一约束/外键要求目录 STATUS=ENABLED，其他状态明确拒绝，过滤掉的表不影响扫描；三类禁用、恢复与范围隔离均有真实库断言。约束启停语义参考[达梦官方完整性约束说明](https://eco.dameng.com/document/dm/zh-cn/pm/management-pattern.html)。测试执行器进一步保留 DECLARE 声明区域，跟踪局部子程序、前置声明与嵌套体，避免首个子程序 END 提前完成整个批次；CREATE 存储定义的拆批范围明确拒绝。`USING LONG ROW` 未经断言支持的拒绝结论已删除。本轮单元 343/343、全部反向工程 13/13、拆批/匿名块/CLI 28/28、格式检查通过；完整功能套件与管理员脚本结果在 PR 的最新验证记录单独更新，不覆盖前轮证据。
- 第八轮 PR 审查（2026-10-02）：`19b31f5` 完整功能 117/117 通过，Cursor 无新增必须处理项，但完整分页的 Codex 线程读回仍发现主键聚集方式丢失；后续只按完整线程分页判断闭环，不从检查成功或首屏 review 列表推断零意见。新增 `Dameng:IsClustered` 主键注解，按约束后备索引 NORMAL/CLUSTER 读取，通过 EF 主键、脚手架通用 HasAnnotation 与关系注解提供器传给迁移，显式输出 CLUSTER / NOT CLUSTER PRIMARY KEY；未知目录类型拒绝。真实库已通过聚集/非聚集（含 CLOB）重建及 CLI 生成上下文重新编译后生成建表 DDL，聚焦 3/3。
- 第九轮 PR 审查（2026-10-02）：`f1bfe9f` 的完整功能套件 119/119 通过；最新 review 继续指出未表示的结构，现按“拒绝有损结构”处理。按[达梦数据字典](https://eco.dameng.com/document/dm/zh-cn/pm/dm8-admin-manual-appendix1.html)使用原生 SYSCONS 区分用户 CHECK 与普通 NOT NULL，选中表上的 CHECK（含禁用或显式 IS NOT NULL 检查）拒绝；SYSCOLINFOS.INFO1 位 0 检出虚拟列后拒绝，不再按普通列读回。独立用户聚集索引和非 NORMAL 后备索引的唯一约束拒绝，SYSINDEXES.FLAG 的系统索引位用于排除隐式行存储索引。CLI 源模型显式设置 Dameng:IsClustered=false，并同时断言生成代码字面量、DDL 和目录读回，构建子进程统一带单节点/禁用共享编译参数。CHECK/虚拟列聚焦 4/4，聚集方式/独立聚集索引/聚集唯一约束/CLI 聚焦 5/5 通过；首次聚集唯一夹具因缺少 KEY 关键字失败，修正为 CLUSTER UNIQUE KEY 后通过，失败记录保留。
- 第十轮 PR 审查（2026-10-02）：`08f1de2` 完整功能 125/125 通过。新增 FLOAT 按 DATA_PRECISION 保留存储类型分面，真实库比较 FLOAT(7/24/25/53) 原表与重建表的类型、精度、标度目录行，避免空断言；NEXTVAL 未引用模式/序列名支持 `$`、`#`，真实序列分面进入模型。对 SYSCOLINFOS.INFO1 位 4/6 的 DEFAULT ON NULL / ON UPDATE 明确拒绝，位 5 隐式 NOT NULL 不误拒绝；真实库确认 DATA_DEFAULT 不包含这两种生成子句，并断言拒绝与过滤隔离。单元 374/374、本轮全部受影响的反向工程与 CLI 回归 26/26、format 通过。
- 第十一轮 PR 审查与主动优化（2026-10-02）：当前模式已经读到的序列若分面超出 EF 范围、非整数或循环标记未知，明确拒绝并点名序列，不再只留下 NEXTVAL 原文；跨模式/目录未读到的引用继续作为外部依赖保留。序列目录查询去重并按每批 500 名称构建，含模式参数最多 501，逐批释放命令与读取器；真实库用 1001 个候选名称、位于批边界的 4 条序列验证三批结果完整。列读取完成后按表建立本次扫描专用的 Ordinal 字典，标识列、主键/唯一约束、索引、外键与注释复用查找，避免宽表反复线性扫描，不跨扫描缓存。单元 395/395、全部受影响的反向工程/CLI 28/28、format 通过；新增真实库超范围增量拒绝及表过滤隔离回归。初次测试的字典类型分析器与驱动空连接夹具失败均已修正，失败记录保留。
- 第十二轮 PR 审查（2026-10-02）：全局临时表在读取 ALL_TABLES.TEMPORARY 后明确拒绝，事务级 DELETE ROWS 与会话级 PRESERVE ROWS 均有真实库断言，排除临时表的过滤仍可正常扫描持久表。索引先读类型头信息，位图等未支持类型在生成普通 DatabaseIndex 前拒绝，不依赖 ALL_IND_COLUMNS 是否有行。真实回归发现外键的 VIRTUAL 索引未在 ALL_CONSTRAINTS.INDEX_NAME 暴露关联，改用原生 SYSOBJECTS + SYSCONS.INDEXID/TABLEID/TYPE$ 确认约束角色：P/U 后备索引不重复建模，F 的 VIRTUAL 跳过，WITH INDEX 产生的 NORMAL 物理索引保留。单元 412/412，全部受影响反向工程/CLI 33/33，format 通过。初版类型拒绝误伤外键的失败回归及目录关联诊断记录保留。
- 第十三轮 PR 审查（2026-10-02）：读取 ALL_TABLES.PARTITIONED，仅 NO 进入普通表模型；分区或未知标记明确拒绝，避免重建成非分区表。真实库分别创建范围和哈希分区表，断言 TEMPORARY=N、PARTITIONED=YES、选中时拒绝以及普通表过滤隔离。单元 415/415，全部受影响反向工程/CLI 35/35，format 通过。此前中断发生在聚焦验证完成之后，恢复时读回原始 TRX 并完成剩余调用链回归。
- 第十四轮 PR 审查（2026-10-02）：HUGE 表可与普通表一样显示 TEMPORARY=N、PARTITIONED=NO、SUBTYPE$=UTAB，因此读取 SYSOBJECTS.INFO3 低六位，仅接受普通原生表类型 0；HUGE、未知或其他非普通类型明确拒绝。参考[官方目录查询说明](https://eco.dameng.com/document/dm/zh-cn/faq/SQL_SEL.html)。管理员迁移测试只为其自己创建的临时表空间增加随机唯一 HUGE 路径，创建 WITH DELTA 的小型 HUGE 表，断言原生标记、拒绝与同空间普通表过滤隔离，随后沿用精确临时用户/表空间清理。未升级持久测试空间、未更改实例 INI。单元 424/424、全部受影响反向工程/CLI 35/35、管理员通道 5/5 通过；完整回归耗时偏长，只读查询对照不支持拆分查询更快的结论，保留单次目录查询，临时诊断代码未提交。

- 第十五轮 PR 审查（2026-10-02）：`SYSCOLUMNS.INFO2` 位 0 同时标记 IDENTITY 与 AUTO_INCREMENT，改按 `SYSOBJECTS.INFO6` 第 25–26 字节校验，只有完整的 IDENTITY 类型 1 才读取种子/增量；AUTO_INCREMENT、未知或缺失标记明确拒绝。真实库原失败实际是误入 IDENT_SEED 查询后报“无效的表名”，并非反馈推测的静默普通列；修复后点名表/列拒绝，同时验证 IDENTITY(7,3) 和表过滤。列级 COLLATE 反馈经四种名称（含不存在的名称）探针确认：当前 DM8/COMPATIBLE_MODE=0 实例仅接受声明语法，不保留至 TABLEDEF 或兼容/原生列目录，比较与排序同普通列；查询 ORDER BY COLLATE 中文拼音排序独立有效。将该结论转为有断言的真实库回归，不新增不存在的目录读取，不外推到所有服务器版本。首次断言夹具把含小写 GUID 的引用表名用未引用过滤查询导致空结果，改为全大写唯一名；失败记录保留。 本轮单元 433/433、全部受影响反向工程/CLI 40/40（含四条列级 COLLATE 对照断言）、format 通过；探索探针四项完成不作为上述 40 项验收的替代。证据分别为 `artifacts/query-translation/local-test/20261002T101147032Z-00ca7a9e/unit.trx` 与 `20261002T101425558Z-6d95cb00/functional.trx`。

- 第十六轮 PR 审查（2026-10-02）：最新 review 将问题写在正文而非 reviewThread，50 条线程全部解决仍不代表零意见。多语句动态 SQL 经实测确认：纯 DML 可以执行，但 CREATE TABLE 与后续 INSERT 放进同一动态文本会提前绑定而报无效表名；[官方动态 SQL 说明](https://eco.dameng.com/document/dm/zh-cn/pm/dm8_sql-sql-statement)也区分普通语句与脚本方式，因此不采用“EXECUTE IMMEDIATE 一概只支持单条”的推测。幂等生成对普通 SQL 按词法分号拆成各自 EXECUTE IMMEDIATE，保留字符串/引用标识符/注释中的分号、原事务抑制标记和每条转义后字面量限制；空语句不产生动态命令。过程/函数/触发器/包/类型定义，以及普通 SQL 后混入的匿名块明确拒绝拆分，完整匿名块继续单独原样内嵌。新增真实库纯 DML、DDL/DML 混合两次执行与历史只一行的断言，并在管理员迁移夹具加入真正 migrationBuilder.Sql 多语句操作。第一次测试夹具拼接遗漏 THEN 后换行的失败已修正，第二次保留混合 DDL 的真实失败证据；修复后聚焦两项通过。 本轮完整单元 441/441、受影响迁移/CLI 真库回归 10/10、管理员迁移脚本 5/5、format 通过，均无失败或跳过；对应 TRX 为 `20261002T110740810Z-3aab05e2/unit.trx`、`20261002T110936899Z-c339e8dc/functional.trx`、`20261002T110941995Z-cfd50937/admin.trx`（均在 `artifacts/query-translation/local-test/`）。

- 第十七轮 PR 审查（2026-10-02）：Codex 与 Cursor 均指出未映射列会被 EF 脚手架警告后省略。工厂新增 IRelationalTypeMappingSource 构造依赖，列进入模型前以完整存储类型调用同一个注册的类型映射源；映射缺失立即抛 NotSupportedException，点名表/视图、列及类型，不复制第二份支持名单。表过滤在目录分面处理与映射校验之前执行。单元覆盖 BFILE、TIME WITH TIME ZONE（含精度）、未支持区间与未知类型拒绝，及普通数值/字符/二进制/时间/区间类型通过；设计时服务实际解析工厂依赖也纳入验证。真实库四项分别覆盖 BFILE、TIME WITH TIME ZONE、INTERVAL HOUR TO MINUTE、含 BFILE 的视图，全部断言拒绝以及同模式普通表过滤仍保留两列。 本轮完整单元 454/454、全部受影响反向工程/CLI 44/44、管理员 HUGE 聚焦回归 1/1、format 通过，均无失败或跳过；TRX 分别为 `20261002T113547691Z-ae44f725/unit.trx`、`20261002T113710315Z-08b23067/functional.trx`、`20261002T113714324Z-51c6d28d/admin.trx`（均在 `artifacts/query-translation/local-test/`）。本轮没有重跑管理员整个五项通道，不能把第十六轮的 5/5 当作当前结果。

- 第十八轮 PR 审查（2026-10-02）：表达式/函数索引由第四轮的整条跳过收紧为明确拒绝选中表，避免尤其是唯一表达式索引的完整性约束被遗漏。类型头 FUNCTION-BASED NORMAL 不再跳过；列行 COLUMN_POSITION 非正或缺失也拒绝，点名表与索引。真实用例覆盖普通/唯一 UPPER 索引拒绝，过滤到另一张普通表时仍保留降序索引。新增 ALL_TRIGGERS 预检查，以目标 TABLE_OWNER（而非触发器 OWNER）限定当前模式，拒绝选中表或视图上的触发器，包括禁用触发器；[官方触发器管理说明](https://eco.dameng.com/document/dm/zh-cn/pm/manage-triggers.html)确认禁用对象仍保留定义。真实夹具覆盖启用、禁用和 INSTEAD OF 视图触发器，以及普通表过滤隔离。首次夹具误按 ENABLED/DISABLED 断言目录状态，实测为 Y/N，已修正；生产检查不依赖该状态拼写，失败记录保留。 本轮完整单元 458/458、全部受影响反向工程/CLI 真库回归 48/48、format 通过，均无失败或跳过；对应 `artifacts/query-translation/local-test/20261002T120604383Z-4ef6fddf/unit.trx` 与 `20261002T120738058Z-8f8248a5/functional.trx`。本轮未重跑管理员通道。

- 第十九轮 PR 审查（2026-10-02）：参考实例默认序列目录为 CACHE_SIZE=0、ORDER_FLAG=N，显式 NOCACHE 与默认相同；CACHE 2/50 与 ORDER 则有独立目录分面。反向工程仅接受 NOCACHE NOORDER，未知/其他值明确拒绝；CREATE SEQUENCE 显式生成这两个选项，ALTER 保留原缓存/排序设置。真实库覆盖默认/显式 NOCACHE 重建目录相同，以及两种缓存值与 ORDER 的拒绝/表过滤隔离。约束检查增加 DEFERRABLE、DEFERRED、VALIDATED，仅接受 NOT DEFERRABLE/IMMEDIATE/VALIDATED；官方说明 NOVALIDATE 对唯一约束/外键生效，两者真库目录 NOT VALIDATED 已验证拒绝与过滤隔离。初始主键探针中的 DEFERRABLE/NOVALIDATE 声明仍返回普通默认目录分面，未外推该探针为功能支持。匿名块边界解析从测试执行器提取到共享 DamengSqlBatchParser，保留局部子程序、嵌套块与 CASE/END IF/LOOP 行为；幂等生成要求匿名块独占操作，外层 END 后追加 DDL/DML/另一个块明确拒绝。新增真库验证分开的匿名块、建表与插入操作在守卫中执行两次仍仅一行；临时探索探针未提交。 本轮完整单元 479/479、共享解析器纯拆批测试 26/26、受影响反向工程/迁移/CLI 真库回归 65/65、管理员迁移脚本 5/5、format 通过，均无失败或跳过。对应 `artifacts/query-translation/local-test/` 下 `20261002T124002682Z-27e9670a/unit.trx`、`20261002T124312082Z-4b8fa586/functional.trx`（纯拆批）、`20261002T124310515Z-a4bcaf92/functional.trx`（真库）和 `20261002T124316269Z-ce2190cb/admin.trx`。

- 第二十轮 PR 审查（2026-10-02）：CHAR_USED=B 不再输出无单位的 CHAR/VARCHAR/VARCHAR2(n)，而是保留显式 n BYTE；类型映射解析 BYTE 长度，限定为已验证的三种非 Unicode 字符类型。参考实例 SF_GET_LENGTH_IN_CHAR()=0，三种 BYTE 语法均接受且目录为 B/n，字符对照列为 C。为避免对未验证目标模式作假设，提供程序生成的 CREATE TABLE/ADD COLUMN/ALTER COLUMN 字节列 DDL 前加入只读模式守卫，非 0 或未知模式抛错；幂等生成中守卫保持完整匿名块。真实库回归比较原表与重建表目录、短中文写入和超字节预算拒绝，并执行常量替换模拟的守卫拒绝分支且断言错误消息；明确区分该分支测试与真实 LENGTH_IN_CHAR=1 实例测试，未修改实例或会话参数。BYTE 探索探针仅留本地 TRX，未提交。 本轮完整单元 490/490、受影响反向工程/迁移/CLI 真库回归 68/68、管理员迁移脚本 5/5、format 通过；随后强化 CLI 夹具为真实 BYTE 列，断言脚手架 C# 的 HasColumnType、重新编译后 DDL 的 BYTE 与目标守卫，最终 BYTE/CLI 聚焦复核 4/4 通过。对应 `artifacts/query-translation/local-test/` 下 `20261002T131142716Z-19cf39d7/unit.trx`、`20261002T131147261Z-9b66d698/functional.trx`、`20261002T131150605Z-0fc67184/admin.trx`、`20261002T131627607Z-16069c4e/functional.trx`。

- 第二十一轮 PR 审查（2026-10-02）：本地 NEXTVAL 的策略选择复用同一个整数兼容性契约。DECIMAL/NUMBER 等已映射非整数列保留由 ISqlGenerationHelper 分隔引用的限定默认 SQL，不设置不兼容的序列策略，已读取的序列定义仍进入 DatabaseModel；整数列的 SequenceSchema 直接记录目录已解析模式。真实用例在 SET SCHEMA 后反向工程，再回登录模式重建序列与表，登录模式同名序列作为对照，插入使用目标模式的 41 而非登录模式的 100。CLI 夹具增加 decimal NEXTVAL 列，首次暴露 EF ScaffoldingTypeMapper 的“移除 precision、保留 scale”探测被误拒绝；CLR-only 且未指定 precision 的探测现返回无界 DECIMAL 默认映射，显式存储类型或已配置 precision 的非法分面仍拒绝，随后 CLI 生成/编译/脚本通过。未知 CHAR_USED 诊断补充表/视图、列及原始值或 NULL。首次单元编译命名/using 问题与 CLI 精度探测失败记录保留。 本轮完整单元 497/497、受影响反向工程/迁移/CLI 真库回归 71/71、管理员迁移脚本 5/5、format 通过，均无失败或跳过；TRX 为 `artifacts/query-translation/local-test/20261002T134222845Z-6177c078/unit.trx`、`20261002T134418150Z-4e81e06a/functional.trx`、`20261002T134425595Z-54ae3702/admin.trx`。

- 第二十二轮 PR 审查（2026-10-02）：非整数 NEXTVAL 保留默认 SQL 的支持限定补齐到键生成边界。约束与外键加载后，主键列以及选中外键实际引用的候选键列，若带默认 SQL 且无受支持的 Identity/Sequence 策略，立即抛 NotSupportedException，点名表、列、存储类型并提示排除该表，避免延迟到 DamengModelValidator 才失败。依据公开 EF RelationalScaffoldingModelFactory 的外键路径，唯一列仅在被选中外键引用时升为候选键，因此不把普通唯一索引一概拒绝。真实库覆盖 DECIMAL/NUMBER 的单列及复合序列主键，先确认服务器确实能生成值 41，再断言工厂明确拒绝；同时验证普通非键默认 SQL 和表过滤保留。另补生成候选键的 FK 场景及只选择主体表时不误拒绝。 本轮完整单元 503/503、全部受影响反向工程/CLI 真库回归 66/66、format 通过，无失败或跳过；对应 `artifacts/query-translation/local-test/20261002T140855609Z-288d2fa9/unit.trx` 与 `20261002T140858930Z-dff66649/functional.trx`。本轮未重跑管理员迁移脚本通道。

- 第二十三轮 PR 审查（2026-10-02）：IDENTITY 列添加注解前按注册类型映射取得 CLR 类型，复用 IsCompatibleWithIdentity 的同一 int/long 契约，其他类型抛 NotSupportedException 并点名表、列、存储类型。反馈举例 SMALLINT IDENTITY 在参考实例建表时即报非法 IDENTITY 类型，TINYINT 同样被服务器拒绝，不能计为工厂负例；当前[官方表管理说明](https://eco.dameng.com/document/dm/zh-cn/pm/management-table.html)另列 DEC(n,0)，真库已创建 DEC(18,0) IDENTITY 并验证工厂明确拒绝及过滤隔离。正常 INT/BIGINT IDENTITY(7,3) 继续保留种子和增量。首轮错误假设小整数可建表的失败记录保留，回归已区分服务器拒绝和提供程序拒绝。 本轮完整单元 509/509、全部受影响反向工程/CLI 真库回归 69/69、format 通过，无失败或跳过；对应 `artifacts/query-translation/local-test/20261002T143452626Z-1c3efbbb/unit.trx` 与 `20261002T143838919Z-70fafe5b/functional.trx`。本轮未重跑管理员通道。

- 第二十四轮 PR 审查（2026-10-02）：选中从表的外键不再因跨模式而静默省略；按约束 R_OWNER 检查，查询改为 LEFT JOIN 保留主体/列目录不可读的外键头，同模式但未选中主体或列不完整也明确拒绝并提示范围修正。真实用例覆盖外模式主体与同名本地主体的区分、排除从表后的自引用关系、主表过滤缺失拒绝及同时选中后保留关系。B 树表从 CLUSTER 索引头记录 Dameng:IsClusterBtree=true，通过 EF 实体注解、关系表注解与迁移明确输出 STORAGE(CLUSTERBTR)，避免受 LIST_TABLE 默认值影响；[官方堆表说明](https://eco.dameng.com/document/dm/zh-cn/pm/manage-table.html)确认该显式选项不受 LIST_TABLE 值影响。堆表/未知存储拒绝，改变存储注解须重建，未注解手写模型保持原行为。真实库通过显式 CLUSTERBTR 原表与重建表的系统聚集索引 FLAG 位验证，另建 NOBRANCH 堆表验证拒绝；未修改实例 LIST_TABLE。CLI 验证注解出现在生成 C# 并重新编译进入建表脚本。首轮单元引用缺失和 CLI 对链式换行的脆弱断言已修正，失败记录保留。 综合回归首轮 80/81，失败项的旧外键启停夹具只选从表、依赖旧省略关系行为；成功场景改为同时选择主表后，完整重跑 81/81 通过。本轮完整单元 516/516、受影响反向工程/迁移/CLI 真库 81/81、管理员脚本 5/5、format 通过，无失败或跳过；TRX 位于 `artifacts/query-translation/local-test/`：`20261002T151302569Z-54a2039b/unit.trx`、`20261002T152424513Z-443dffc6/functional.trx`、`20261002T151542450Z-b504b356/admin.trx`。

- 第二十五轮 PR 审查（2026-10-03）：NEXTVAL 模式/序列限定点号两侧允许 SQL 空白，不再因 SEQ . NEXTVAL 或 SCHEMA . SEQ.NEXTVAL 的格式把本地序列遗漏。保持未引用名称折大写、引用名称内部点/空格/转义双引号不变，仍只识别完整的简单 NEXTVAL 表达式。单元覆盖空格、制表符、换行及特殊引用名；真实库分别用限定和未限定的空白默认值建表，断言序列与策略进入模型，删除源表和序列后按元数据重建，插入得到 41。 本轮完整单元 520/520、全部受影响反向工程/CLI 真库回归 73/73、format 通过，无失败或跳过；对应 `artifacts/query-translation/local-test/20261002T160835882Z-80f16076/unit.trx` 与 `20261002T161119096Z-3db5f7c7/functional.trx`。本轮未重跑管理员通道。

- 第二十六轮 PR 审查（2026-10-03）：简单 NEXTVAL 未匹配的默认 SQL 先做词法依赖检查；未限定或当前模式的 NEXTVAL/CURRVAL 引用明确拒绝，点名表/视图、列与序列，避免保留复合表达式却不创建本地序列。复用既有标识符解析，字符串和注释中的伪列文本不计依赖，函数调用不误作伪列，已限定外模式依赖仍保留原始 SQL。真实库用当前模式限定/未限定的 SEQ.NEXTVAL + 1 先插入得到 42，确认源结构有效，再断言工厂拒绝；过滤到简单 NEXTVAL 表仍读入序列模型。外模式简单及复合默认值均验证保留为外部依赖。 补充引用名边界：后面还有点号的 NEXTVAL 标识符是限定名称的中间部分，不应提前当作伪列；外模式复合默认值真库用例使用名为 NEXTVAL 的序列检验不误拒绝。初版完整回归 76/76 通过后，对最终代码重新完整复核，避免混淆边界调整前后的证据。 最终完整单元 537/537、全部受影响反向工程/CLI 真库回归 76/76、format 通过，无失败或跳过；最终 TRX 为 `artifacts/query-translation/local-test/20261002T164914795Z-1b6e890b/unit.trx` 与 `20261002T165306816Z-80c4d4ac/functional.trx`。本轮未重跑管理员通道。

- 第二十七轮 PR 审查（2026-10-03）：普通原生表类型之外，检查 SYSOBJECTS.INFO3 位 50，选中 LONG ROW 表时明确拒绝并点名表及存储选项，避免只保留 CLUSTERBTR 而丢失超长行存储能力。[官方原生目录](https://eco.dameng.com/document/dm/zh-cn/pm/dm8-admin-manual-appendix1.html)将该位定义为记录超长时允许行外存储；真实库使用 STORAGE(CLUSTERBTR, USING LONG ROW) 创建三个 VARCHAR 列，实际插入并读回超过半页大小的记录，断言普通表类型与该位同时存在。DISABLE USING LONG ROW 对照表验证标记为零，过滤掉不支持表后工厂仍可正常返回普通表。未修改实例参数；测试对象使用唯一名并精确清理。本轮完整单元 539/539、全部受影响反向工程/CLI 真库回归 77/77、format 通过，无失败或跳过；完整真实库运行约 26 分钟后正常结束。TRX 为 `artifacts/query-translation/local-test/20261002T173935856Z-0d6762da/unit.trx` 与 `20261002T174403304Z-7634daed/functional.trx`；先行聚焦 LONG ROW 真库用例 1/1 位于 `20261002T173940622Z-3cd69ae9/functional.trx`。本轮未重跑管理员通道。

- 第二十八轮 PR 审查（2026-10-03）：反向工程 B 树表在保留 CLUSTERBTR 前检查聚集存储索引的 SYSINDEXES.GROUPID，与模式所属用户 SYSOBJECTS.INFO3 低 16 位的默认数据表空间 ID 比较；非默认或未知位置明确拒绝，点名表及两侧空间 ID。通过模式 PID 关联所有者，支持模式名与用户名分离，不误用登录用户。原生字段依据[官方目录附录](https://eco.dameng.com/document/dm/zh-cn/pm/dm8-admin-manual-appendix1.html)。首版使用 DBA_USERS，在 RESOURCE/SOI 用户的真库回归中因缺查询权限失败，已移除该依赖；未扩大用户权限。新增管理员通道用例只为创建并精确清理两个独立临时用户/表空间，实际目录与工厂调用仍使用普通临时用户，独立模式内验证非默认 STORAGE(ON ...) 拒绝与默认表过滤成功。最终实现完整单元 545/545、管理员完整通道 6/6、format 通过；TRX 为 `artifacts/query-translation/local-test/20261002T185120592Z-14b5292a/unit.trx` 与 `20261002T185223034Z-d2cb9d89/admin.trx`。首次权限失败保留在 `20261002T184749411Z-a58896d3/admin.trx`，不计为通过证据。全部受影响反向工程/CLI 真库回归 77/77 通过，运行约 24 分钟，无失败或跳过；最终 TRX 为 `20261002T185221527Z-ddd73d58/functional.trx`。

- 第二十九轮 PR 审查（2026-10-03）：表空间检查扩展到全部 NORMAL 物理索引，包括普通二级索引、非聚集主键/唯一约束后备索引与 WITH INDEX 外键索引；即使后备索引仅建模为约束，也先校验空间。按用户 INFO3 第 16–31 位比较默认索引空间，聚集存储仍使用低 16 位的默认数据空间；VIRTUAL 外键索引不作物理空间比较。三表空间真实用例验证默认数据/索引空间不同的合法结构，并拒绝第三空间中的普通索引、非聚集主键和唯一约束；保留虚外键与物理外键及表过滤。另根据[官方约束默认规则](https://eco.dameng.com/document/dm/zh-cn/pm/definition-statement.html)与真实回归修正审查建议的边界：未指定默认索引空间时，目录第 16–31 位为 0，索引回退到表的数据空间；本提供程序已要求表在默认数据空间，因此可据低 16 位校验该回退。首版直接比较 0 导致持久用户外键切片 0/2 和管理员通道 6/7，保留失败 TRX，不计为通过；新增真实断言在新临时用户中确认该 0 标记、二级索引实际数据空间和工厂成功，再切换该临时用户的默认索引空间执行三空间用例。最终实现完整单元 554/554、管理员完整通道 7/7、format 通过；TRX 为 `artifacts/query-translation/local-test/20261002T195644020Z-a1099c44/unit.trx` 与 `20261002T195643388Z-4219040c/admin.trx`。首版回退缺失的失败记录为 `20261002T195455404Z-8b924831/functional.trx` 和 `20261002T195457379Z-06910419/admin.trx`。最终全部受影响反向工程/CLI 真库回归 77/77 通过，无失败或跳过；TRX 为 `20261002T195643738Z-6a6f6495/functional.trx`。

- 第三十轮 PR 审查（2026-10-03）：序列目录不再由 pendingSequenceDefaults 驱动，改为按当前模式一次完整枚举，只绑定一个模式参数；独立序列与有列引用的序列统一加入 DatabaseModel.Sequences 并绑定所属模型，默认值解析复用同一目录字典。无表模式、表过滤后没有表仍保留序列；表过滤只限制表/视图，不过滤模式级序列。相应收紧边界：当前模式任何不可表示、CACHE 或 ORDER 序列都会明确拒绝，移除“排除引用表即可绕过”的旧建议。替换已不再用于生产路径的 500 名称分批查询和对应测试，单元总数随之变化，不是沿用旧轮次计数。真实用例在独立模式中验证无表、有表、表过滤三种场景的升/降序列、已消费后续号和删除后重建取值；无表模式的 CACHE/超范围增量明确拒绝。CLI 在 --table 模式下读取未引用独立序列，验证 HasSequence、真实 facet 和重建脚本。首次编译/格式分析器要求已修正。最终完整单元 549/549、聚焦真库 11/11、format 通过；管理员完整通道最终 7/7 通过，TRX 为 `20261002T202921253Z-eff8a349/admin.trx`；初次 6/7 的 `20261002T202723352Z-9626c240/admin.trx` 是自有临时账户随机密码复杂度失败，夹具改为保证大小写、数字与符号并保留随机其余字符，不修改服务器策略。该夹具仅管理员通道使用，普通反向工程/CLI 回归不依赖它。最终全部受影响反向工程/CLI 真库回归 81/81 通过，无失败或跳过；TRX 为 `20261002T202659929Z-f1ecc01a/functional.trx`。单元 TRX 为 `artifacts/query-translation/local-test/20261002T202559836Z-00db2d86/unit.trx`，聚焦 TRX 为 `20261002T202600218Z-433eb2f6/functional.trx`。

- 第三十一轮 PR 审查（2026-10-03）：长度语义解析对捕获的类型基名 Trim，修复 VARCHAR2 (9 BYTE) 等显式配置因左括号前空格/制表符而查找失败；保持原始 StoreType、长度、BYTE/CHAR 单位和 Unicode BYTE 拒绝边界。真实回归对 VARCHAR/VARCHAR2/CHAR 两种单位验证带空格声明的映射、建表、目录及容量，与无空格版本对照。序列最小值须小于最大值，非循环 LAST_NUMBER 超出上下界明确拒绝。真实升/降序列到边界前仍可读回，发出边界值后目录分别为 4/0（范围 1..3），再取 NEXTVAL 报错；工厂在生成 DDL 前拒绝。额外核对循环序列发现目录同样会先报越界算术续号，因此不能直接照非循环规则拒绝：根据[官方循环定义](https://eco.dameng.com/document/dm/zh-cn/pm/definition-statement.html)，仅在前值位于界内且按增量方向迈出一步时回绕到另一端。使用 decimal 做前值检查避免 Int64 边界算术溢出。升降循环的原序列与按模型新建序列 NEXTVAL 分别为 1/3，真实断言通过。初版循环切片 0/2 的失败保留在 `artifacts/query-translation/local-test/20261002T205516018Z-1fe245bf/functional.trx`；修正后序列边界 4/4 位于 `20261002T205721544Z-749d0fc2/functional.trx`。最终单元 568/568、format 通过，单元 TRX 为 `20261002T205752845Z-4b6cf880/unit.trx`；最终全部受影响反向工程/CLI 真库回归 88/88 通过，无失败或跳过；TRX 为 `20261002T205824542Z-af4f461f/functional.trx`。本轮未重跑管理员通道。

- 合并前主分支集成复验（2026-10-03）：用户授权合并后发现 main 已前进到 b51da88（PR #4，纯移除 IDENTITY 与生成值回读回归）。将该主分支合入当前 PR；唯一文本冲突位于兼容性矩阵，合并保留 IDENTITY 移除边界与本 PR 的注释迁移说明，代码自动合并后检查迁移交互路径。依赖与锁文件未变。集成候选完整单元 600/600、反向工程/CLI/迁移及 IDENTITY 移除和生成值回读的交叉真库 113/113、管理员脚本 7/7、format 通过，无失败或跳过。TRX 位于 `artifacts/query-translation/local-test/`：`20261003T001219942Z-7f0fa928/unit.trx`、`20261003T001219561Z-c4f34142/functional.trx`、`20261003T001220505Z-7f348383/admin.trx`。这些是集成后的本地证据；最新提交的远端复审与合并状态另以 GitHub 实时结果为准。
