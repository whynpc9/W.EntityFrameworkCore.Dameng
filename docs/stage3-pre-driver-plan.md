# 阶段 3：双驱动接入前的写入与迁移增量

## 范围与分工

依据 issue #1 阶段 3，基线为 `9d19399`。本轮只实现删除 IDENTITY，并补齐已有 DML + SELECT 生成值回读的契约和真实库证据。双驱动接入、RETURNING、输出参数、多行 VALUES、数组绑定、DbBatch、BulkCopy 不在本轮范围；保留依赖与锁文件。

协调者负责设计、静态审查与最终结论；Sol High 子代理编写实现和回归测试；冻结后由 Sol Low 子代理独占构建/数据库窗口进行独立验证。失败后先结束验证窗口，再由开发代理定点修复；保留失败记录。

## A：只删除 IDENTITY

- 入口是 `AlterColumnOperation`，旧策略为 IdentityColumn，新策略不再是 IdentityColumn。
- 第一版只接受删除自增属性，其他列定义必须保持一致；切换为 Sequence、同时修改类型/可空性/默认值/计算列/排序规则等组合在生成阶段拒绝。先检查完整操作，再输出 SQL。
- 生成表级 `ALTER TABLE <限定表名> DROP IDENTITY`，不带列名；只有此变化时不生成额外 MODIFY，不删除或重建列。
- 标识符按组成部分引用并转义；通过现有 EndStatement 保持事务抑制，兼容现有幂等脚本包装。
- 增加或恢复 IDENTITY，以及修改仍为 IDENTITY 的种子/增量继续拒绝。错误说明是提供程序本轮边界，不推断所有服务器版本都无法执行其他语法。
- 单元测试：限定/未限定/含引号名称、事务抑制、幂等包装、拒绝组合变化、增加/恢复拒绝、现有种子增量拒绝；包含真实 EF 模型差异产生的操作，防止仅手造操作通过。
- 真实库：带唯一名称的表中先插入记录，再执行生成的删除属性命令；验证数据/主键约束保留、目录自增属性消失、显式键写入成功、不提供必填键不再自动生成。使用当前测试账户，在 finally 精确清理。
- Down 的恢复操作明确失败；不声称自动可逆或失败后可通过事务撤销 DDL。

## B：现有生成值回读

保留 `UpdateAndSelectSqlGenerator`、SCOPE_IDENTITY、CURRVAL 和现有行数协议。只在测试证明有缺陷时修改产品实现，不切换到输出参数。

聚焦场景：

1. 客户端提供主键（含复合键）时，INSERT 后读取非键默认值和虚拟计算列。
2. IDENTITY、Sequence 主键插入后，同时读取多个非键生成值；断言在 SaveChanges 返回时已经回填，不调用 Reload 掩盖失败。
3. UPDATE 后读取虚拟计算列及触发器生成的并发标记；明确配置 ValueGeneratedOnAddOrUpdate、IsConcurrencyToken、HasTrigger，触发器由测试自行创建。
4. 成功后当前值与原始值正确，连续两次更新成功；另一个上下文持旧标记时抛 DbUpdateConcurrencyException，失败方不获得其他行的新值。
5. 行已删除的零行 UPDATE 同样抛并发异常，不传播生成值；在外部事务内验证生成值回读与回滚后的独立查询状态。
6. 覆盖同步和异步 SaveChanges、NULL/Unicode 默认值；继续拒绝其他库端生成主键和没有真实生成机制的 rowversion。

SQL 单元测试应证明 SELECT 使用主键定位、UPDATE 条件使用原并发值、零行守卫保留、回读需要事务。真实测试要比较实体状态与独立查询，不用单纯 SQL 快照充当执行证据。

## 验证与交付

- 遵守仓库 AGENTS.md、dameng-sql 与 dameng-ef-migrations 技能；不打印连接串或凭据。
- 统一用 `scripts/local-test/run.sh test ...`，优先 unit 全量、functional 聚焦新测试，再 functional 全量、specification 冒烟；迁移生成器变更还运行 admin 既有脚本回归。
- 使用已有持久测试环境，不 provision、不重新创建测试空间；工作树缺 secrets 时可安全复用主检出中的 Git 忽略 secrets，文件权限设为 0600。
- 格式检查和 diff 检查；若失败记录修复前后结果，禁止把跳过的数据库测试记为通过。
- 协调者根据实际测试结果更新 compatibility、migrations 与相关技能中的过时边界说明，并记录命令、计数、证据路径和未覆盖项。
- 本地验收通过后提交 PR，逐条处理 review bots 意见，核对当前提交的审核与检查后合并。用户已明确授权该流程；不创建发行标签或发布 NuGet。

## 来源

- [issue #1](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/issues/1)
- [达梦表修改语法](https://eco.dameng.com/document/dm/zh-cn/pm/definition-statement.html)：DROP IDENTITY 为表级操作。
- [达梦触发器](https://eco.dameng.com/document/dm/zh-cn/pm/trigger)：触发器细节以公开文档及目标实例回归为准。
- [EF Core 10 UpdateAndSelectSqlGenerator](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Update/UpdateAndSelectSqlGenerator.cs)
