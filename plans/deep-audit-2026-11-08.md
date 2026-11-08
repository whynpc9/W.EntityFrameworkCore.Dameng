# 2026-11-08 深度只读审查

## 基线与结论

- 用户选择审查 origin/main，固定提交 **5b1f6d407ac61acf7f5658addd551565aed34721**；已用 git ls-remote 核对远端 main 相同。
- 当前工作区 HEAD 仍为 f626736a75dd1854a1210f90ed92c7b955cfb447，落后 35 提交；没有切换、拉取、合并或修改产品源码。本报告是 plans/ 下的新增审查产物。
- 范围：两个产品包、六个测试项目、共享测试、本地工具、CI/发布、依赖配置、当前能力和设计文档。由主审与七个只读分组交叉审查，主审重新打开最终引用的代码核对。
- 结果：**8 项主要发现（7 项高置信静态结论、1 项中置信候选），2 项调查，3 项方向选项**。置信度不等于运行验证；本轮没有运行新的 C# 复现、构建或数据库回归。
- 旧 001–020 计划在被审提交中已有实施/否证记录；不重开已闭合问题。当前本地 plans/README.md 仍是较老的 TODO 快照，本报告不覆盖它，也不把本地旧状态当成远端实施状态。
- 以下代码行号全部属于上述固定提交。链接指向不可变 GitHub blob，避免本地旧文件造成定位歧义。

## 优先级表

工作量 S=数小时，M=约一天，L=多日；包含必要测试。顺序综合影响、成本、置信度和修复风险，不代表漏洞严重性分级。

| 编号 | 发现 | 类别 | 影响 | 工作量 | 修复风险 | 置信度 | 主要证据 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R01 | 损坏 TRX 降级处理可能保留转义后的凭据 | 安全/工具 | 中 | S | 低 | 高 | [TestLauncher:350][launcher-fallback] |
| R02 | 快照重置继承的自增种子和增量 | 正确性/迁移 | 高 | M | 中 | 高 | [AnnotationCodeGenerator:95][identity-generator] |
| R03 | Nullable 恒等日期转换的 ToString 被误拒绝 | 正确性/查询 | 中 | S–M | 低 | 高 | [DateTimeToStringTranslator:41][temporal-string] |
| R04 | LOB 类型首尾空白造成参数与守卫分歧 | 正确性/架构 | 中 | M | 中 | 高 | [TypeMappingSource:212][mapping-source] |
| R05 | 脱敏后的 TRX 状态/计数被用于验收 | 测试/DX | 中 | S | 低 | 高 | [TestLauncher:298][launcher-parse] |
| R06 | 共享主键忽略后续映射的聚集注解 | 正确性/模型 | 中 | M | 中 | 中，先表征 | [RelationalAnnotationProvider:59][shared-key] |
| R07 | SQL 词法边界未识别 Q/q 字符串 | 正确性/迁移 | 中 | M | 中 | 高 | [SqlLexer:43][lexer] |
| R08 | 普通测试夹具清理短路及未知 CREATE 状态 | 测试/恢复 | 中 | L | 中 | 高 | [ReverseEngineeringCompletenessTests:185][cleanup] |

## 主要发现

### R01：损坏 TRX 必须在无法安全脱敏时停止保留原文

正常路径解析 XML，再对属性、文本和注释脱敏；解析失败后却对原始 XML 文件执行字面替换并回写。[TestLauncher.cs:298–365][launcher-fallback] 的降级路径，与 [592–647 的原值匹配器][redactor] 组合，无法可靠移除 XML 实体编码后的凭据。内部回写再次失败时还会保留原文件。

成立条件是数据库测试诊断含凭据、凭据含需 XML 编码的字符、TRX 损坏或截断。目录私有权限缩小了暴露面；**没有实际泄漏证据，也不声称无数据库凭据的离线 CI 已泄漏秘密**。修复应以固定无秘密诊断替代不能安全解析的内容，并返回未验收；替换失败须明确说明产物未清理。用合成输入验证实体编码、截断和文件替换失败，不读取真实秘密。

工作量 S，风险低，置信高。独立证据若确认历史凭据外泄，再按受影响凭据安排轮换；不根据本次静态审查泛化轮换。

### R02：快照必须保留模型级自增分面的继承语义

[最终化约定:33–44][identity-convention] 为约定 identity 属性写入策略，但不把模型级 seed/increment 写成属性注解。[属性取值:113–128][identity-facets] 则明确从模型继承缺省分面。生成器在 [95–101][identity-generator] 只读属性注解，缺失时改用 1/1；生成出的无参 UseDamengIdentityColumn 调用又在 [属性 API:22–32][identity-api] 显式写入 1/1。

因此模型级 UseDamengIdentityColumns(5, 10) 加约定整数主键，会在生成并重建快照时从 5/10 变成 1/1。后续可能产生无业务变化的差异，并撞上 [迁移生成器:265–272][identity-alter] 对修改自增分面的拒绝。当前 [DesignTimeTests:143–151][identity-test] 仅测模型默认加属性显式覆盖，没有继承场景。

修复应保留有效值或等价继承表达，不能随意抹平显式配置。验证要真正重建快照模型，比较有效列注解并要求 IMigrationsModelDiffer 无差异；覆盖默认、非默认、单分面和双分面覆盖、两驱动设计时服务。

工作量 M，风险中，置信高；本轮未运行快照往返。

### R03：可空恒等转换不应取消日期 ToString 的服务器翻译

[DateTimeToStringTranslator:41–44][temporal-string] 直接比较 Converter.ProviderClrType 与 DateTime；[TemporalTranslationGuard:14–15][temporal-guard] 已为其他日期操作解包 Nullable。现成的 [ConvertedTemporalTranslationTests:67–80][temporal-test] 配置了 DateTime? 到 DateTime? 恒等转换，却只检查 Year。

同一原生日期列可翻译 Year，而无参 Nullable.ToString 或 Value.ToString("yyyy") 在谓词/分组中被旧判断拒绝。先增加此模型的失败回归，再统一有效类型判断。保留 ticks/text 转换拒绝、Nullable.ToString 的空串回退、Value.ToString 的 NULL 传播以及已有服务器默认文本合同；不得改为客户端文化格式，也不得自动推断业务时间粒度。

工作量 S（单元）至 M（含双驱动功能对照），风险低，置信高；本轮未执行新表达式。

### R04：统一 LOB 类型的语义分类

[类型映射源:238][mapping-trim] Trim 后查找类型，但 [212–214][mapping-source] 用原 mappingInfo 克隆并恢复原名称。EF 10.0.12 的 [RelationalTypeMapping 源码][ef-mapping] 确认 WithTypeMappingInfo 保留原 StoreTypeName，且无括号名称不会被 GetBaseName 去除空白。

对显式首尾带空白的 NCLOB/BLOB，查找能成功，但 [字符串 Clone:35–38、分类:76–82][string-mapping] 和 [二进制 Clone:30–33、参数配置:60–64][binary-mapping] 会丢失 LOB 分类。随后 [查询分类器:8–14][lob-classifier] 与 [模型分类:401–411][model-lob] 同样不匹配，影响专用参数配置、比较重写与排序/分组/键索引守卫。

建议集中用于语义判断的规范化规则，保留类型分面、驱动绑定与 Clone 行为。测试须覆盖大小写、空白、显式/默认类型、转换器与 Clone，分别断言参数类型、查询重写/拒绝和模型拒绝；再串行进行两驱动大值回归。**不声称本轮观察到截断或服务器执行错误。**

工作量 M，风险中，置信高。此项是查询与存储分组的同一发现，仅计一次。

### R05：把验收语义与展示脱敏分开

[TestLauncher:298–348][launcher-parse] 对全部非命名空间属性替换后保存，再重新读取 outcome 和 Counters；[373–376][launcher-accept] 要求 outcome 精确为 Completed。脱敏器可能把与合法连接字段相同的状态字或数字一并替换，导致原本完整通过的结果被拒绝。

应从解析后的原始结构中先验证并保存可信计数/标准状态，再独立写脱敏产物；不能放宽非零、全部执行通过、零失败/跳过和 Completed 要求。补状态词、计数碰撞以及失败/中止/缺结果对照。本发现与 R01 共用文件，适合一份计划，但须保留两组独立验收。

工作量 S，风险低，置信高。当前证据支持错误拒绝，不支持声称失败结果被变成通过。

### R06：先表征共享主键的聚集注解丢失

[RelationalAnnotationProvider:59–65][shared-key] 先取 MappedKeys 中第一个主键，再读取 Dameng:IsClustered，后续映射完全不参与。相邻表/索引 fill factor 路径已做收集和冲突检查。现有 [共享表测试:95–104、157–166][shared-key-test] 只覆盖双方都无聚集注解。

只在后一个映射配置时可能被忽略，两侧相反配置也没有此处的确定性拒绝。先在锁定 EF 版本上跑仅首侧、仅次侧、相同、冲突、全缺省及实体命名顺序矩阵，确认公开表拆分路径可达，再保留唯一有效值并拒绝冲突。不要重新解释旧 019 的服务器 LOB 聚集保护结论。

工作量 M，风险中，置信中：局部丢弃行为确定，但完整 EF 模型可达性尚未执行验证，不能直接按已经复现的产品缺陷实施。

### R07：统一 Q/q 字符串的词法边界

[达梦官方字符串文档 §2.2][dm-q] 明确支持 Q/q 界定字符串。[SqlLexer:43–67][lexer] 只理解普通单/双引号，遇到 Q 字符串内部单引号会提前结束。幂等路径的 [SplitDynamicSqlStatements:107–138][split-sql] 因此可能拒绝合法输入；[DIsql 分隔符检查:994–1074][disql-guard] 的另一套扫描器也存在同类不一致。反向工程 [456–464][sequence-default] 仅在默认值文本含 NEXTVAL/CURRVAL 时复用此 lexer，影响范围应限定在该入口。

按公开语法把 Q/q 字面量作为不透明 token，并统一分隔符判断。补内部引号、分号、关键字、多行斜杠、大小写前缀、合法/非法界定符测试，再进行两驱动聚焦迁移回归。保留原有普通引号、注释、存储对象和客户端分隔符的拒绝合同。此项属于合法 SQL 被误拒绝，未发现注入或数据损坏证据。

工作量 M，风险中，置信高；本轮未在目标服务器执行 Q 字符串。

### R08：扩展普通测试夹具的精确恢复能力

[目录完整性夹具:185–191、218–222][cleanup] 在 CREATE 返回后才登记对象；finally 先用原连接 SET SCHEMA，再顺序清理。若会话不可用，第一步失败后所有对象均不再尝试清理。[CLI 夹具:244–253][cli-cleanup] 的七个清理动作也会被首个异常中止。

这与旧 014 的管理员临时用户/表空间修复不同：普通账号、CLI 和规范夹具仍有确认后登记和串行短路模式。可能留下对象并影响后续全模式目录断言；本轮没有实际残留复现。

先对共用 store、CLI 和目录夹具做纯故障注入表征，再在发送前登记精确对象归属；未知结果以可用的新连接查精确目录。独立清理逐项尝试、聚合失败，不能盲目重发 DDL，不能按前缀批量删除，不能提升普通账号权限。最后串行执行官方/W 聚焦实库对照。

工作量 L，风险中，置信高。此项合并三个分组的重复发现。

## 调查项：不能先按缺陷修复

| 编号 | 证据与疑点 | 影响与下一步 | 工作量 / 风险 / 置信度 |
| --- | --- | --- | --- |
| I01 | [LobQueryRewriter:15–26、56–114][lob-rewriter] 没有 IN/RowNumber 专门处理；[CollectionTranslationTests:28–43][collection-test] 仅对整数覆盖一元素相等、多元素 IN | 表征 NCLOB Contains 的 0/1/2 元素、null、否定及关系空值模式；表征 GroupBy 后组内排序 First 是否生成窗口表达式并绕过现有守卫。必须先确认完整 EF10 SQL 路径，不直接宣布支持 LOB IN | M / 中 / 中 |
| I02 | [DatabaseModelFactory:1014–1077、1115–1124][catalog-constraints] 接收返回的约束成员，未核对总数；[1281–1297][catalog-index] 索引仅校验位置有效 | 假设目录仅漏复合对象的一部分成员，可能缩短键/索引；先用完整工厂合成目录复现，再判断是否有可靠独立目录合同。没有真实服务器漏行证据，不能猜位布局或新增权限 | M / 中 / 低（实际可达性） |

## 方向选项

1. **提交已发生、ACK 丢失的 EF 端到端验收**：当前 [compatibility:114][compat-failure] 明确留有缺口，现有故障中继可作为基础。收益是明确客户端未知结果与独立读回合同；工作量 L、风险中。延续旧方向，不改变 W 禁自动重试策略；依赖可靠清理与观察证据。
2. **独立 TLS 与第二版本兼容性 profile**：[compatibility:102–104][compat-profile] 将主要证据限定在既有明文 profile。先做环境和用例设计，验证成功及明确拒绝分支，再决定扩展声明；工作量 L、风险中，需要独立环境，不能变更共享实例参数。
3. **反向工程对象预检报告**：[工厂:227–229][preflight] 及 [触发器拒绝:870–874][trigger-guard] 遇第一项不支持结构即停止。可设计一次列出对象级原因和可选对象的只读报告；工作量 L、风险中。正式 scaffold 仍保留无损拒绝，预检必须复用同一规则，不能维护两套判断。

## 建议计划与依赖

首批建议四份计划：R01+R05、R02、R03、R04。R06 必须先做公开模型表征；I01/I02 只能写调查计划。R08 的恢复基础应先于新增网络故障注入；R04 的类型分类稳定后再扩大 I01 的 LOB 矩阵。R07 修改共享 lexer 后需联合检查迁移、脚本执行器、历史表与反向工程默认值。

2026-11-08 规划时的后续选择：用户已选首批四份独立计划，R01/R05 合并为 021，R02/R03/R04 分别为 022/023/024，入口见 [计划索引](README.md)。当时状态均为 TODO，审查阶段只编写计划；其余发现和调查不预建计划。随后用户授权执行、创建 PR 并在评审通过后合并；021–024 的最终状态、PR 和运行证据以索引及各份 evidence 为准。本报告的发现与“本轮未运行”均指最初的只读审查阶段。

## 验证入口与边界

仓库为 .NET 10 / C# 14，EF Core 范围锁定为 10.0.12 至 11.0.0 之前，官方和 W 驱动范围保持不变。审查只核对命令与静态编排，**以下不是本轮已执行的通过记录**：

    scripts/ci/verify-offline.sh
    scripts/local-test/run.sh test unit --configuration Release
    scripts/local-test/run.sh test unit --configuration Release --driver w
    scripts/local-test/run.sh test functional --configuration Release --filter '<聚焦用例>'
    scripts/local-test/run.sh test functional --configuration Release --driver w --w-transport-security PlaintextAllowed --filter '<聚焦用例>'

以上两个包装脚本均管理可写 DOTNET_CLI_HOME、首启/遥测变量及单节点构建参数。直接调用 dotnet 时必须先设置相同环境，并使用 -m:1 /nodeReuse:false /p:UseSharedCompilation=false 和支持的 --disable-build-servers。不能清全局 NuGet 缓存。普通测试不提升为管理员，两驱动实库通道串行；缺配置或跳过不能记为通过。支持声明变化需更新 docs/compatibility.md。

静态依赖盘点共 9 份锁文件、78 个不同包 ID；NuGet 漏洞源读取未完成（响应解码/连接错误），因此本轮**不作依赖无漏洞声明**。Context7 查询失败，涉及框架与方言的关键判断改查 EF 10.0.12 公开源码及达梦官方文档。发布工作流已使用 OIDC；本轮没有执行 token 交换/发布，也没有把旧发布成功当成现工作流认证已验证。

## 已考虑并排除

- 官方异步同步回退、有限取消、单命令批次、W 禁自动重试：既定驱动合同。
- 官方七位时态参数差异、迁移锁释放间歇与观察权限限制：已有受限证据和风险记录，不作为新发现。
- DateTime 无参 ToString 的文化差异、自动业务分桶：维护者已明确选定服务器文本语义。
- 旧 Replace null、distinct 集合另一侧 LOB、LISTAGG 二进制排序、共享列生成、复杂 rowversion、目录普通列完整性：对应旧计划已有修复。
- LOB 表主键缺省策略：019 已有限定 profile 的否证，不重开。
- 四项 specification 冒烟数量、W 单元数量较少：文档说明范围，W 功能/规范复用源码；不能仅按数量认定缺陷。
- 全局模型初始化锁、大型反向工程工厂、现有目录分批阈值：没有新性能测量支持重构。
- 可信模型 raw SQL、标准外部代理、actions 未固定 SHA 本身：未建立额外可达漏洞。
- 约束后备索引降序丢失：未找到公开语法支持的合法输入，未列发现。
- JSON/StringComparison、物理数据库创建删除、当前模式 scaffold 范围：明确产品边界。
- 未发现足以单独立项的新增依赖迁移、性能或纯文档问题，不为填满类别增加事项。

## 未审与证据限制

没有运行构建、现有测试、新复现、真实数据库、管理员 DDL、网络故障、锁争用、TLS/多版本 profile、性能基准、实际执行计划或 GUI。没有读取真实 secrets 或历史原始敏感产物；没有验证 GitHub 分支保护、NuGet 策略、线上部署或完整供应链。没有检查驱动内部实现或反编译官方 EF 提供程序。没有穷举所有 EF 表达式组合。

旧报告与验收文件只用于识别已做决定和既有证据范围，不作为本轮通过记录。没有使用历史记忆文件作为审查证据。

[launcher-fallback]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/scripts/local-test/TestLauncher.cs#L350
[launcher-parse]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/scripts/local-test/TestLauncher.cs#L298
[launcher-accept]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/scripts/local-test/TestLauncher.cs#L373
[redactor]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/scripts/local-test/TestLauncher.cs#L592
[identity-generator]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Design/Internal/DamengAnnotationCodeGenerator.cs#L95
[identity-convention]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Metadata/Conventions/DamengValueGenerationStrategyConvention.cs#L33
[identity-facets]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Metadata/Internal/DamengPropertyExtensions.cs#L113
[identity-api]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Metadata/Builders/DamengPropertyBuilderExtensions.cs#L22
[identity-test]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/test/W.EntityFrameworkCore.Dameng.Tests/DamengDesignTimeTests.cs#L143
[identity-alter]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs#L265
[temporal-string]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeToStringTranslator.cs#L41
[temporal-guard]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengTemporalTranslationGuard.cs#L14
[temporal-test]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/test/W.EntityFrameworkCore.Dameng.Tests/DamengConvertedTemporalTranslationTests.cs#L67
[mapping-source]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengTypeMappingSource.cs#L212
[mapping-trim]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengTypeMappingSource.cs#L238
[ef-mapping]: https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Storage/RelationalTypeMapping.cs
[string-mapping]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengStringTypeMapping.cs#L35
[binary-mapping]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengByteArrayTypeMapping.cs#L30
[lob-classifier]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengLobTypeClassifier.cs#L8
[model-lob]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Infrastructure/Internal/DamengModelValidator.cs#L401
[shared-key]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Metadata/Internal/DamengRelationalAnnotationProvider.cs#L59
[shared-key-test]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/test/W.EntityFrameworkCore.Dameng.Tests/DamengLobPrimaryKeyTests.cs#L95
[dm-q]: https://eco.dameng.com/document/dm/zh-cn/sql-dev/practice-strings.html
[lexer]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengSqlLexer.cs#L43
[split-sql]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs#L107
[disql-guard]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs#L994
[sequence-default]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Scaffolding/Internal/DamengDatabaseModelFactory.cs#L456
[cleanup]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengReverseEngineeringCompletenessTests.cs#L185
[cli-cleanup]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs#L244
[lob-rewriter]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengLobQueryRewriter.cs#L15
[collection-test]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/test/W.EntityFrameworkCore.Dameng.Tests/DamengCollectionTranslationTests.cs#L28
[catalog-constraints]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Scaffolding/Internal/DamengDatabaseModelFactory.cs#L1014
[catalog-index]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Scaffolding/Internal/DamengDatabaseModelFactory.cs#L1281
[compat-failure]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/docs/compatibility.md#L114
[compat-profile]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/docs/compatibility.md#L102
[preflight]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Scaffolding/Internal/DamengDatabaseModelFactory.cs#L227
[trigger-guard]: https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/5b1f6d407ac61acf7f5658addd551565aed34721/src/W.EntityFrameworkCore.Dameng/Scaffolding/Internal/DamengDatabaseModelFactory.cs#L870
