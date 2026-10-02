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
