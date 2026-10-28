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

## CLI 凭据与清理边界

CLI 回归通过固定 `Name=ConnectionStrings:DamengCliFixture` 调用 scaffold，连接值只进入需要配置的子进程环境 `ConnectionStrings__DamengCliFixture`。临时宿主显式提供 Host/IConfiguration，不读取 appsettings 或 user-secrets；scaffold 使用 `--no-onconfiguring`，重编译上下文由单独部分类型从环境配置。生成源码和日志不包含连接字面量，不输出整个上下文。工具安装、版本探测、restore/build 子进程均移除连接环境。

Unix 临时目录在创建时即使用 `0700`。数据库对象按本轮唯一名称清理；数据库和文件清理独立执行，任一失败会使验收失败，并保留安全的失败类型和源位置。离线合成 CLI 测试通过真实 EF Design 和记录型目录工厂核对命名解析，不能作为实库证据。

CLI 临时工程的合成与实库路径共用锁定工具版本检查，现有工具文件也必须经无连接环境的 `--version` 精确核对，不同版本只能在指定 artifacts 目录更新后复查或明确失败。清理日志只记录本轮七个自有对象名、独有临时目录和完成状态；失败诊断保留该安全标识，不输出原始数据库/文件异常中的敏感消息，便于精确处理残留。
