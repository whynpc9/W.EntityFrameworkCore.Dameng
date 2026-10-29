# 文档索引

## 当前使用与能力

这些文档描述当前源码的使用方式和已验证边界。单元级 SQL/元数据证据不等同于真实服务器证据，具体状态以[兼容性与验证](compatibility.md)为准。

- [兼容性与验证](compatibility.md)：能力矩阵、双驱动差异、证据术语和验证入口。
- [提供程序架构](architecture.md)：包依赖、驱动桥接、EF 服务层与关键不变量。
- [用 dotnet ef 执行达梦迁移](migrations.md)：迁移项目、脚本、权限、DIsql 和 W 驱动注意事项。
- [反向工程](reverse-engineering.md)：当前模式脚手架的支持范围、目录保真和明确拒绝项。
- [查询函数与边界](query-functions.md)：字符串、数值、日期分桶、字符串聚合与集合查询。
- [日期字符串转换](datetime-string-translation.md)：`DateTime.ToString()` 的服务器文本契约和格式白名单。
- [本地达梦测试环境](../scripts/local-test/README.md)：统一测试入口、双驱动通道和结果验收规则。

Agent 执行达梦 SQL 或迁移时，还应阅读 [dameng-sql](../skills/dameng-sql/SKILL.md) 和 [dameng-ef-migrations](../skills/dameng-ef-migrations/SKILL.md)。

## 历史发布记录

以下文档记录对应版本当时的发布与验收事实。旧版本号、旧依赖和准备阶段措辞均按历史保留，不代表当前源码状态。

- [0.4.0](release-0.4.0.md)
- [0.3.0](release-0.3.0.md)
- [0.2.0](release-0.2.0.md)
- [0.1.2](release-0.1.2.md)
- [0.1.1](release-0.1.1.md)

当前源码的 W `0.3.1` 依赖升级及回归见 [W 接入验证记录末节](w-dmprovider-validation.md#升级到-wdamengprovider-0312026-10-29)；该升级不等于已发布新的 W 扩展包。

## 历史实施与验收记录

这些文档保留当时的计划、失败过程、审查结论和测试计数，用于追溯，不作为当前能力清单。

### 查询与设计时

- [查询翻译后续扩展计划](query-translation-extension-plan.md)与[实施台账](query-translation-execution.md)
- [数值聚合查询覆盖与验证](numeric-aggregate-validation.md)
- [设计时实现计划](design-time-plan.md)

### 阶段 3 写入与迁移

- [双驱动接入前的写入与迁移增量计划](stage3-pre-driver-plan.md)
- [对应独立验收记录](stage3-pre-driver-validation.md)

### W 驱动接入

- [双驱动接入实施计划](w-dmprovider-integration-plan.md)
- [接入验证记录](w-dmprovider-validation.md)
- [接入审查记录](w-dmprovider-integration-review.md)
