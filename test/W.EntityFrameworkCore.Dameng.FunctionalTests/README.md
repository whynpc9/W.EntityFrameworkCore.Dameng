# 达梦功能测试

普通功能测试面向专用测试用户的当前模式，不会创建或删除物理数据库。
迁移脚本测试另用管理员通道创建唯一的临时用户和表空间，并精确清理。
每个存储夹具都会创建名称唯一的对象，并在 `finally` 中删除这些精确对象；
由于达梦 DDL 会隐式提交，DDL 清理绝不依赖事务回滚。

在仓库根目录通过统一入口加载本地 secrets：

```bash
scripts/local-test/run.sh test functional
scripts/local-test/run.sh test admin
```

候选 SQL 语义探针单独使用 `test probes`，探针方法通过只代表测量已执行，必须逐 case
审查结果。见[本地测试入口](../../scripts/local-test/README.md)。直接运行测试项目且缺少
`DAMENG_TEST_CONNECTION_STRING` 时仍会报告环境跳过，不能作为真实库或发布证据。

参考实例的各次版本记录见[兼容性矩阵](../../docs/compatibility.md)，驱动锁定为
`DM.DmProvider` 8.3.1.47463。单一实例不构成最低版本保证。
绝不能提交、记录或将凭据和连接详情复制到测试数据中。
