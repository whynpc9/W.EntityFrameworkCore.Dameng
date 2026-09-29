# 查询翻译后续扩展计划

状态：已采纳，本轮执行与验证完成；试验未通过的项目按门槛延期。编制于 2026-09-29，实际交付见[实施台账](query-translation-execution.md)。

依据：[issue #1 的阶段 2](https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/issues/1)、本仓库提交 `1229f941937389886f12f8873e065aa797edb95b`、[兼容性矩阵](compatibility.md)。当前锁文件解析为 EF Core `10.0.12` 和 `DM.DmProvider` `8.3.1.47463`。本计划不修改依赖范围，也不涉及 ADO.NET 驱动开发、反向工程或迁移增量。

编制计划时仅核对源码、现有测试和公开资料。下文保留当时的候选实现与验收门槛；当前支持情况以实施台账和兼容性矩阵为准，不能把未通过试验的候选 SQL 视为支持声明。

## 1. 建议范围与取舍

建议先完成基础能力核实、常用数值函数和指定字符 Trim；随后推进 JSON 标量读取与日期分桶。DateTimeOffset、字符串聚合和大集合查询先通过专项试验，再决定实现范围。不要按函数名字相似程度直接照搬 SQL。

| issue 中的功能点 | 决定 | 价值与理由 |
| --- | --- | --- |
| `IsNullOrEmpty`、固定参数 `string.Concat` | Q0：先补证据，必要时修正 | EF 基类已有翻译，避免重复实现；空值和 LOB 仍需本提供程序回归 |
| `Abs`、`Sign`、`Floor`、`Ceiling` | Q1：首批实现 | 通用数值筛选、分组和投影，扩展点清楚 |
| `Exp`、`Log`、`Log10`、`Pow`、`Sqrt` | Q2：第二批实现 | 统计计算有价值，需处理类型、定义域和精度 |
| 带字符的 `Trim` / `TrimStart` / `TrimEnd` | Q3：首批实现有限重载 | 常见数据清洗；先做单个常量字符和非空常量字符集合 |
| JSON 标量路径查询 | Q4：纳入第二阶段 | 已有 JSON 存储却无法按属性筛选，收益明显；先定义独立的 SQL 标量读取契约 |
| 小时、分钟、秒、周分桶及季度 | Q5：纳入第二阶段 | 用于时间统计；明确公共入口、跨年规则和索引代价 |
| DateTimeOffset 成员 | S1 → Q6：试验通过后实现子集 | 先验证本地日期部件与 UTC 转换，再考虑偏移量和运算 |
| `LISTAGG` 字符串聚合 | S2 → Q7：条件纳入 | 分组展示有价值，但需要聚合扩展点以及顺序、空值和溢出契约 |
| 大集合 `Contains` | S3：先做限制与执行计划试验 | 价值高，但 issue 的 500/999 不能直接作为常量写入实现 |
| `IsNullOrWhiteSpace` | 暂缓 | 默认 `LTRIM` 只去空格，不能代表全部 .NET Unicode 空白 |
| `PadLeft` / `PadRight` | 暂缓 | 要处理不截短、空字符串、字节/字符/UTF-16 长度差异，不能直接映射 LPAD/RPAD |
| `Math.Round` | 暂缓通用翻译 | 默认 .NET 中点舍入为 ToEven；必须单独验证，不能直接替换为数据库 ROUND |
| 三角函数六项 | 后备，不排入本轮 | `Sin/Cos/Tan/Asin/Acos/Atan` 有候选 SQL，但相对通用查询收益较低；待明确使用场景后沿用 Q2 框架 |
| 小数 `DateTime.Add*` | S4：只安排可行性试验 | 精度敏感，现有拒绝行为必须保留到无损路径得到证明 |
| 年内周 `WW` | 暂缓隐式 .NET 翻译 | 日历、周起始日、首周规则不同，不能用一个格式串代表所有 .NET 日历 |
| 带 `StringComparison` 的比较 | 继续不支持 | 不在本轮扩展；现有排序规则边界保持 |

## 2. 源码基线与 issue 的修正

- [方法翻译器注册](../src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengMethodCallTranslatorProvider.cs)继承 `RelationalMethodCallTranslatorProvider`，只追加 DateTime、字符串和 GUID 翻译器。[EF 10.0.12 基类](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/RelationalMethodCallTranslatorProvider.cs)注册了 [StringMethodTranslator](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/Internal/Translators/StringMethodTranslator.cs)，覆盖 `IsNullOrEmpty` 以及二、三、四个 string 参数的 `Concat`。本地 [SQL 生成器](../src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengQuerySqlGenerator.cs)已经把字符串加法输出为 `||`。因此这两项是“已有翻译链，缺本地针对性证据”，不能直接算作全新功能，也不能据此宣称真实库已通过。
- [字符串翻译器](../src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengStringMethodTranslator.cs)目前只显式处理无参数 Trim。新增字符重载可落在该类；静态方法处理不能放在现有 `instance is null` 返回之后。
- [日期成员翻译器](../src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeMemberTranslator.cs)只接受 DateTime；[日期方法翻译器](../src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengDateTimeMethodTranslator.cs)对 double 增量只接受有限、整数且在 int 范围内的常量。参数化 double 即使运行时为整数，目前也拒绝翻译。
- [查询测试](../test/W.EntityFrameworkCore.Dameng.Tests/DamengQueryTranslationTests.cs)显式验证了 DateTimeOffset 成员、字符 Trim、Pad、空白判断和 JSON 属性访问无法翻译。每项落地时只替换对应的拒绝测试，保留未支持重载的反例。
- [JSON 类型映射](../src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengJsonTypeMapping.cs)包含 JsonElement 和结构化 JSON 映射类，但不能从映射类的存在推导查询可用。Q4 第一版不承诺 owned/complex JSON、数组遍历或 JSON 更新。
- [服务注册](../src/W.EntityFrameworkCore.Dameng/Extensions/DamengServiceCollectionExtensions.cs)没有达梦专用聚合翻译器；[参数处理器](../src/W.EntityFrameworkCore.Dameng/Query/Internal/DamengParameterBasedSqlProcessor.cs)当前主要做 LOB 重写及布尔条件转换。字符串聚合和集合展开不能只在普通方法翻译器里堆函数映射。

达梦[函数手册](https://eco.dameng.com/document/dm/zh-cn/pm/function)显示：LTRIM 默认集合为空格；TRIM 的字符参数只允许单字符；LPAD 存在截短及兼容模式相关行为。这些差异必须先试验。`.NET IsNullOrWhiteSpace` 按 [Char.IsWhiteSpace 的定义](https://learn.microsoft.com/en-us/dotnet/api/system.string.isnullorwhitespace?view=net-10.0)识别空白；[Math.Round 默认采用 ToEven](https://learn.microsoft.com/en-us/dotnet/api/system.math.round?view=net-10.0)。

## 3. 实施工作包

### Q0：核实基础字符串翻译和建立证据基线

交付：聚焦测试与兼容性矩阵补充；仅在发现错误时修改翻译。

- 验证 `IsNullOrEmpty`、二/三/四参数 string Concat 和字符串 `+`；不把 object、数组、IEnumerable 重载自动算入支持。
- 覆盖 null、空串、中文、纯空格、左右操作数可空，以及两个参数都为 null。Concat 的目标结果是 .NET 的 null 视为空串语义。
- 分别验证有界 Unicode 文本与 NCLOB，检查默认空值语义和 `UseRelationalNulls`。不得为通过测试把 LOB 静默转为短 VARCHAR。
- 在 Where 和服务器投影中验证；抓取实际 SQL，排除投影被客户端求值造成的假通过。
- 记录参考实例版本、兼容模式、字符集和长度设置。矩阵按“SQL 生成”和“真实库执行”分别更新。

验收：列出的重载逐项有结果断言及 SQL 断言；若只有 SQL 生成成功，状态只能写单元级证据。

### Q1：常用数值函数

交付：新增 `DamengMathTranslator` 并注册，方法列表按准确的 MethodInfo 白名单匹配。

- 第一版处理 `Math.Abs(int/long/decimal/double)`、`Math.Sign(int/long/decimal/double)`、`Math.Floor(decimal/double)`、`Math.Ceiling(decimal/double)`。
- 候选函数为 `ABS`、`SIGN`、`FLOOR`、`CEIL`，以目标服务器试验为准。Sign 返回 int；其他结果映射必须与对应 CLR 重载匹配，不能统一转 double。
- 可空列经 EF 提升后的运算需保留 null；组合算式参与 Where、OrderBy、GroupBy 和投影。
- 用正负数、零、decimal 小数与大值验证；单独检查 int/long 最小值取绝对值溢出，double 非有限值的驱动可表示性。无损结果和可接受的失败行为要分开记录。

验收：decimal 不经 double 中转，类型和可空性正确；超界不得回绕、截断或伪造正常结果。不承诺异常类型与 CLR 完全一致。

### Q2：指数、对数与幂

交付：在 Q1 的重载与类型测试框架上增加 `Math.Exp(double)`、`Log(double)`、`Log(double,double)`、`Log10(double)`、`Pow(double,double)`、`Sqrt(double)`。

- 候选映射为 EXP、LN、LOG、LOG10、POWER、SQRT；特别核对 .NET `Log(value, base)` 与达梦 LOG 的参数顺序。
- 每个函数先验证正常定义域，再验证零、负数、底数 1、溢出、NaN/Infinity 的可表示性及结果差异。
- 正常定义域的浮点比较使用明确的绝对/相对误差容限；不能用放大误差容限掩盖错误重载、错误单位或舍入。
- SQL 域外报错若不能提供所声明的语义，缩小本批函数名单；不得用零或 null 静默替代。

验收：支持的定义域和服务器异常边界写入矩阵；MathF、泛型数值接口和三角函数不随本批宣称支持。

### Q3：指定字符 Trim

交付：扩展现有字符串翻译器及对应单元、功能测试。

- 首版支持 `Trim(char)`、`TrimStart(char)`、`TrimEnd(char)` 的常量字符，以及三个方法的非空常量 char[]。
- 以 LTRIM/RTRIM 的字符集合语义为候选；双端处理候选为嵌套 LTRIM/RTRIM。不把多字符数组传给仅允许单字符的 TRIM 参数。
- 覆盖多次交替出现的字符、重复字符、中文、制表符、引号、全部被去掉和中间字符不变。
- 动态 char[]、null/空数组先拒绝；后两者涉及 .NET 默认 Unicode 空白规则，不能当成“什么都不删”。无参数 Trim 的已有行为单独记录，不借本批声称其覆盖全部 Unicode 空白。
- 保留输入映射，验证 NCLOB 返回值；常量字符集按 SQL 字面量规则转义，禁止拼接未转义文本。

验收：按字符集合删除，非按子串或逐次替换；未支持的形态在需要服务器翻译的位置明确失败。

### Q4：JSON 标量路径读取

交付：先增加带 `Dameng` 前缀的 EF.Functions 扩展，例如候选 `DamengJsonValue`（字符串）和 `DamengJsonInt32`（可空整数）；名称和签名在实现评审时定稿。

- 首版仅接受 JSON 列、常量对象属性路径、字符串/int 标量；支持嵌套对象。覆盖 Where、OrderBy 和投影。
- 使用 JSON_VALUE 为候选，先查明 RETURNING、错误处理和返回长度的可用语法。字符串不得依赖数据库默认短长度而静默截断；不能安全读取的长度必须报错或拒绝。
- 公共契约显式规定：缺失路径/JSON null 返回 null；对象或数组误作标量、类型错误、数值溢出不得静默返回正常值。目标服务器不能区分所需情况时，本项停止在试验状态。
- 验证点号、引号、反斜杠和中文属性名的 JSON 路径转义；路径采用受控语法生成，首版不接受用户输入的任意 SQL/路径片段。
- 检查 `JSON_MODE` 对行为的影响，记录于证据。[达梦 JSON 文档](https://eco.dameng.com/document/dm/zh-cn/pm/json)只能证明候选能力，最终以目标库结果为准。
- `JsonElement.GetProperty(...).GetString/GetInt32` 的直接成员翻译作为后续子项：先解决缺失属性异常、类型错误及可空返回差异，不能让显式 SQL API 的宽松契约悄悄替代 CLR 契约。

验收：完整 EF 查询经驱动执行并回读类型正确；继续保留整值 Equals 的现有限制。数组、JSON_TABLE、owned/complex JSON、JSON 写入不包含在本项中。

### Q5：日期分桶与季度

交付：带 `Dameng` 前缀的 EF.Functions 扩展，候选为 `DamengTruncateHour/Minute/Second`、`DamengStartOfIsoWeek`、`DamengQuarter`，优先 DateTime 和可空 DateTime 重载。

- TRUNC 的 HH24/MI/SS/IW 仅作为候选格式，逐个验证服务器是否接受、返回类型及精度，不支持的格式不发布。
- ISO 周返回周一零点的日期，避免单独返回“周号”丢失周所属年份。季度返回 1–4；可以评估从 Month 算出季度，避免无必要的字符串中转。
- 同年、同月比较已有 Year/Month 组合可表达，优先补组合回归，不新增重复 API。查询某一时间段时优先使用 `>= start && < end`。
- 覆盖年末/年初、闰日、周日/周一、午夜和带七位小数秒的输入；将结果用于筛选、分组、排序和投影。
- 报告列上应用函数对索引的影响；不在本项里自动改写任意表达式为范围谓词。格式单位不得直接接受未校验的 SQL 片段。

验收：显式的 DateTime 分桶契约有真库结果；DateTimeOffset 分桶和任意 Calendar.GetWeekOfYear 翻译不包含在本项中。

## 4. 有明确退出条件的试验

每项试验单独提交结论：环境、最小输入、候选 SQL、实际值/错误、与 CLR 的差异、是否进入实现。失败试验也是有效结论，但不能把功能标成支持。

| 试验 → 实现 | 必须回答的问题 | 通过后的范围 | 不通过时 |
| --- | --- | --- | --- |
| S1 → Q6 DateTimeOffset | 存储/回读是否保留原偏移；日期部件按原偏移还是会话时区；UTC 转换是否保留七位精度 | 先 Year/Month/Day/Hour/Minute/Second 和 UtcDateTime；随后才评估 Offset、UtcNow、整数 Add* | 保留列比较能力及成员拒绝测试 |
| S2 → Q7 字符串聚合 | LISTAGG 的 null、空串、分隔符、顺序、返回类型/长度、溢出行为；EF 聚合表达式能否保留组内排序 | 有界文本的 GroupBy + string.Join / string.Concat 子集；注册 IAggregateMethodCallTranslatorProvider，需要时增加有序聚合 SQL 表达式与生成逻辑 | 不提供顺序不确定却宣称稳定的结果，不转成客户端拼接 |
| S3 集合 Contains | 当前 EF 10 对常量/参数集合如何展开；真实 IN 项数、整条命令参数数和表达式复杂度限制；编译缓存与计划代价 | 仅在实证需要时增加 IN 分块和全命令预算保护；另行设计真正超过参数预算的方案 | 不硬编码未经证实的 500/999，不静默拆为多次查询 |
| S4 小数 DateTime.Add* | 参数化增量经日期算术/INTERVAL 是否保留 100ns 级精度，是否受整数除法、decimal 标度及兼容模式影响 | 只纳入已证明无损的单位、重载和参数形态 | 保留非整数及参数化 double 无法翻译的行为 |

补充验收要求：

- S1：使用 `+08:00`、`-05:00`、`+05:30`、UTC、同一瞬间不同偏移和跨日样本；记录会话时区，不将固定偏移当作时区/DST 规则。`DateTimeOffset.Now` 的进程本地时间含义与服务器/会话时间未对齐前保持拒绝。验证 UtcDateTime 的值、精度和物化后的 Kind 契约。
- S2：null 元素的处理必须匹配选定 string.Join/Concat 重载；验证全 null、空组、空分隔符、重复项和溢出。首版不支持 Distinct 或 LOB 输入，除非分别增加证据。保留显式组内 OrderBy；无序输入不承诺稳定顺序。
- S3：测 0、1、499、500、501、998、999、1000、2000 个元素及实测边界两侧；覆盖重复值、null、否定 Contains、多个集合加普通筛选参数、排序和分页。500/999 是试验点，不是已确认上限。拆为多个 IN 后参数总数不变；不得通过内联所有值绕过预算而制造无限 SQL。若需分块，优先在参数展开后处理 SQL 树，并保留 EF 空值补偿和缓存正确性。结果与未分块基准比较，记录命令数、实际参数数、SQL 长度及执行计划/耗时。
- S4：包含 `0.5`、`-0.25`、1 秒以下增量、参数/常量两条路径、闰日和极值；不把 `date + value / 24` 当作已经证明可行的方案。

## 5. 批次、依赖与完成条件

| 批次 | 工作包 | 依赖与交付门槛 |
| --- | --- | --- |
| A：高收益基础 | Q0 → Q1、Q3 | Q0 固定环境/空值基线；每包可独立合入，先取得首批可靠增量 |
| B：查询表达力 | Q2、Q4、Q5 | Q2 依赖 Q1；Q4/Q5 依赖 Q0 的证据规范，各自经过语义试验和公共 API 评审 |
| C：条件扩展 | S1 → Q6、S2 → Q7 | 试验结论明确后再开实现任务，不预先承诺全部重载 |
| 研究队列 | S3、S4 | S3 因集合查询价值优先于 S4；以可复现结论结项，实现方案另行评审 |

建议每个 Q 包作为独立实现 PR，试验结论可先作为独立文档 PR。顺序表示依赖，不要求一次发布整张清单；不把预估工作量当作工期承诺。

每个实现包的完成条件：

1. 开发者提交准确的成员/重载白名单、输入输出类型、可空性和不支持情况；新增公共扩展带 `Dameng` 前缀，不依赖具体应用框架。
2. 聚焦单元测试验证 SQL、映射和明确拒绝；真实库回归验证实际结果及驱动物化。仅 ToQueryString 成功或仅顶层投影返回正确均不够。
3. 复用现有[功能测试](../test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengSequenceAndTranslationFunctionalTests.cs)的唯一对象名及 finally 精确清理模式；按主题新增测试类，DDL 不能靠回滚清理。真实连接串只从 `DAMENG_TEST_CONNECTION_STRING` 读取，日志和文档不输出连接详情。
4. 审查者核对没有信息损失、没有意外客户端求值、未改变 LOB/布尔/参数不变量；默认 EF 空值语义和适用的 UseRelationalNulls 路径都有测试。
5. 运行完整单元测试和受影响功能测试；发布前运行完整 FunctionalTests 及 Specification.Tests，分别记录通过/失败/跳过。四项规范冒烟测试不代表上游关系数据库一致性通过。
6. 更新 `docs/compatibility.md` 的精确重载与证据日期；涉及新 API 时更新架构/使用示例。不用本计划直接提高任何能力状态。

构建测试遵守仓库约束：先设置可写的 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`；locked-mode restore，构建/测试使用 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false`，SDK 支持时加 `--disable-build-servers`。不改锁文件，不清空 NuGet 全局缓存。

发布负责人仅在真实库证据齐全后将完成的工作包写入后续版本说明；本计划不指定版本号、不执行发布。回退以独立 PR/包版本为边界；若修正的是已有重载语义，发布说明需说明行为变化。用户已采用的新 API 在回退前需同步调用代码，不能承诺降级包后仍可编译。

## 6. 暂缓项目的重新进入条件

- 空白判断：证明完整 .NET 空白集合、null、空串、中文、制表符/换行、不换行空格等行为可保持；不能将仅空格处理命名为 IsNullOrWhiteSpace。
- Padding：证明总宽度小于原长时不截短，零宽/空串行为正确，并解决中文与补充平面字符的 UTF-16 长度差异；不能通过要求整个实例改兼容模式来使单个翻译成立。
- Round：先选明确重载与舍入模式。可先研究 decimal + 常量 AwayFromZero，但默认/ToEven、正负中点、digits 范围和结果精度必须分别验收，不能扩大声明。
- 三角函数：有具体消费查询后再排期，沿用 Q2 的类型、定义域及误差规范。
- 日历周：若新增显式达梦周函数，名称和文档必须表明规则；没有等价证据就继续拒绝通用 .NET Calendar/ISOWeek 方法翻译。
