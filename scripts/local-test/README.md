# 本地达梦测试环境

此工具从仓库根目录已忽略的 `.local-test.secrets.json` 加载管理员与测试连接，按下文的初始化步骤创建独立、持久的测试表空间和用户。

## 首次初始化

在仓库根目录运行以下命令创建空模板。已有文件时会拒绝覆盖；Unix 下以 `0600` 权限写入，命令中不含真实凭据：

```bash
python3 - <<'PY'
import json
import os

fd = os.open('.local-test.secrets.json', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
with os.fdopen(fd, 'w', encoding='utf-8') as stream:
    if os.name == 'posix':
        os.fchmod(stream.fileno(), 0o600)
    json.dump({
        'AdminConnectionString': '',
        'ConnectionString': None,
        'ProvisioningStatus': 'pending'
    }, stream, indent=2)
    stream.write('\n')
PY
```

在本地编辑器中将 `AdminConnectionString` 填为完整的管理员连接字符串；初始化前保留
`ProvisioningStatus` 为 `pending`、`ConnectionString` 为 JSON `null`。不要把凭据粘贴到
命令行、提交或日志中。确认文件仍为 `0600` 且由 Git 忽略后运行：

```bash
scripts/local-test/run.sh provision
```

工具会从实例的 `PAGE()` 和 `V$DATAFILE` 推导数据文件大小与目录，随机生成名称和32字符口令（固定保证大写、小写、数字和安全符号各至少一个，其余28位使用密码学随机数），授予测试用户 `RESOURCE`、`SOI`，并校验登录后的当前模式。新建表空间按页大小确定初始容量，数据文件自动增长每次 64 MB，最大 1024 MB。成功后，测试连接与容量策略保存在同一 secrets 文件，状态变为 `ready`；文件权限保持 `0600`。后续直接使用下文的测试入口，不要重新生成模板或重置状态。再次运行 `provision` 只校验现有连接，不调整已有表空间。

## 维护与检查

已创建的旧测试空间如仍为 `AUTOEXTEND OFF`，运行 `scripts/local-test/run.sh inspect` 先只读查看其容量、已用空间以及专用模式下表、序列和回收站对象计数。确定要为该专用空间启用有界增长时，显式运行 `scripts/local-test/run.sh grow`。工具会用管理员连接确认 secrets 记录的唯一表空间及数据文件实际对应，再仅对该文件设置 `AUTOEXTEND ON NEXT 64 MAXSIZE 1024`；不会清理对象、改变其他空间或实例全局参数。

运行 `scripts/local-test/run.sh info` 可用管理员连接只读查看服务器版本、页大小和指定实例参数。单项查询失败时显示“未取到”；不会更改实例设置。

运行 `scripts/local-test/run.sh selftest` 可用合成 TRX 检查脱敏后 XML 结构仍有效、短用户名不会破坏标签名或 `Error Message` 等诊断文字、独立账号仍会隐藏，并验证中止的运行不能通过验收。此命令不连接数据库，也不读取真实凭据。

若创建中断或清理失败，状态会停在 `planned`、`tablespace_created`、`user_created`、`grants_applied` 或 `cleanup_required`。工具不会对这些状态自动重试或删除对象。应由管理员根据 secrets 中记录的 `OwnedTestUserName`、`OwnedTestTablespaceName` 和 `TestDatafilePath` 核对并精确处理残留。

## 测试入口

```bash
scripts/local-test/run.sh test unit
scripts/local-test/run.sh test offline-fixtures
scripts/local-test/run.sh test functional
scripts/local-test/run.sh test specification
scripts/local-test/run.sh test admin
scripts/local-test/run.sh test probes
scripts/local-test/run.sh test all
```

### W.DamengProvider 通道

每个通道都可追加 `--driver w`，改为运行 W 驱动的测试项目；默认 `--driver official` 与原行为相同：

```bash
scripts/local-test/run.sh test unit --driver w
scripts/local-test/run.sh test functional --driver w --w-transport-security PlaintextAllowed
scripts/local-test/run.sh test all --driver w --w-transport-security PlaintextAllowed
```

- W 单元通道运行 `W.EntityFrameworkCore.Dameng.WDriver.Tests`（离线）。W 功能、规范和管理员通道运行 `W.EntityFrameworkCore.Dameng.WDriver.FunctionalTests` / `…WDriver.Specification.Tests`：两者以链接方式编译官方测试源码并定义 `WDRIVER`，按驱动区分的断言写在共享源码的 `#if WDRIVER` 分支中。
- W 连接设置在测试进程内从同一测试连接经 W typed builder 派生，不另存 secrets。W 默认要求 TLS；明文服务器需显式传 `--w-transport-security PlaintextAllowed`（非敏感参数，只传给 W 测试进程，也可用环境变量 `DAMENG_TEST_W_TRANSPORT_SECURITY`）。
- W 通道的 TRX 文件名带 `-w` 后缀（如 `functional-w.trx`），与官方结果分开保存，不能合并计数。
- 两种驱动的功能/管理员通道共享同一个持久测试模式，部分反向工程用例会检查整个当前模式的目录对象，因此**不要并行运行两个数据库通道**。

`functional` 使用持久测试用户，排除 `DamengMigrationScriptFunctionalTests` 和 `Category=CapabilityProbe`。`specification` 也使用持久测试用户。`admin` 只运行会自行创建、清理临时用户与表空间的 `DamengMigrationScriptFunctionalTests`，使用 secrets 中的管理员连接。`probes` 只运行标有 `Category=CapabilityProbe` 的功能测试。`all` 顺序运行 unit、functional、specification、admin；探针需显式运行。

需要数据库的普通测试优先使用进程已有的 `DAMENG_TEST_CONNECTION_STRING`，没有时使用 secrets 中的测试连接。该环境变量不会改变 `admin` 通道的选择或授权。需要缩小测试范围时，在通道后追加 `--filter '<dotnet test 过滤表达式>'`；工具始终将其与通道固定过滤条件相与，不能扩大管理员通道或把探针混入普通功能测试。

启动脚本优先使用 `DOTNET_HOST_PATH`，然后搜索 `dotnet`，最后尝试 `~/.dotnet/dotnet`。它为每次运行设置独立的 `DOTNET_CLI_HOME`，使用锁定还原、单节点构建和测试。连接串只传入测试子进程环境；测试输出在显示前会对连接串、主机、用户和口令脱敏。若真实数据库测试被跳过，不可把该次运行记作数据库回归通过。

每个测试通道的 TRX 会保存在 `artifacts/query-translation/local-test/<runid>/<lane>.trx`，目录仅当前用户可读，文件在报告前按 XML 文本和属性值脱敏。启动器会打印宿主结果、总数、执行数、通过数、失败数和跳过数。只有宿主结果为 `Completed`、退出码为零、总数大于零、总数等于执行数且执行数等于通过数、无失败或跳过时，通道才可用于验收；否则整个命令退出非零。

### 显式测试配置

所有 lane 新增 `--configuration Debug|Release`，默认 `Debug`；只接受精确大小写，重复、空值或未知配置在启动测试子进程前拒绝。该选项控制被测项目，LocalTest 启动工具仍使用自身 Debug 构建。

CLI 临时消费者引用当前测试宿主实际加载的提供程序程序集，不依赖仓库 src 的 Debug 输出。嵌套输出文件及实际加载程序集都按名称、MVID、SHA-256 核对；W 额外保留底层驱动 manifest 核验。

### 初始化口令策略自检

`selftest` 不读取 secrets、不连接数据库，还验证口令生成器的四类字符、长度和允许字符集合：固定有效/无效候选及1,000次实际生成均执行且不输出值，成功打印 `Password policy self-test passed.`。已有账户及口令保持原样；服务器仍最终判断自身策略，本自检不代表所有定制策略的服务器接受性。

### 无凭据离线验收

`unit` 和 `offline-fixtures` 解析通道后不调用 secrets 加载器、不读取连接覆盖值，也不构造依赖真实连接的脱敏器。`offline-fixtures` 选择对应驱动的功能项目，固定 `Category=OfflineFixture`，自定义 `--filter` 只能与固定条件相与；官方/W分别保存 `offline-fixtures.trx` / `offline-fixtures-w.trx`。`all` 仍顺序运行原四个通道。

新增纯夹具需标注类级 `[Trait("Category", "OfflineFixture")]`，只允许普通 Fact/Theory，禁止数据库属性、集合及类/集合夹具绑定；元数据合同检查不实例化数据库属性。Trait 不自动证明测试体无网络，新增依赖仍需代码审查。当前合同覆盖26项脚本拆批和4项元数据检查；这些不是服务器证据。

直接运行 `run.sh` 时，宿主版本探测与 LocalTest 工具的 restore/build 使用 `env -u` 移除 `DAMENG_TEST_CONNECTION_STRING` 和 `ConnectionStrings__DamengCliFixture`。最后的可信 LocalTest 主进程保留调用者环境，使真实库覆盖合同有效；托管解析器在离线通道不读取这些值，也不转发到离线restore/test子进程。数据库通道仍保留普通/管理员连接分离和ready检查。

本地与CI使用同一两步总门；第一条失败时不执行第二条：

```bash
PYTHONDONTWRITEBYTECODE=1 python3 -B scripts/ci/test_verify_offline.py
scripts/ci/verify-offline.sh
```

第一步在合成仓库用替身验证进程顺序、每一步失败即停、连接变量隔离及工作流合同，不运行真实.NET或网络。第二步清除连接环境，执行锁定还原、Release构建、两组单元、两驱动离线夹具、selftest、Python扫描回归和W包身份核验；每项失败即停止，四个TRX分别要求Completed、非零、全部通过且零跳过。

新增PR/main工作流只请求contents读取权限，按上述顺序执行，并仅上传本次工作区的 `artifacts/query-translation/local-test/*/*.trx`，保留7天；成功时缺少TRX即失败，失败时保存已有安全TRX。本地脚本验证与远端Actions运行分别记录。离线绿灯不替代真实库或发布验收，两条标签发布工作流保持原样。

### 管理员临时夹具的 DDL 恢复记录

`admin` 子进程在本次结果目录的 `recovery/` 下保存每个临时夹具的独立 JSON 记录；直接调用夹具时使用独立临时目录。Unix 目录为0700、文件为0600，原子替换前刷新文件。记录包含唯一运行标识、精确对象名、创建前不存在证据、阶段、用户默认表空间关系及数据文件路径，不含凭据或 CREATE USER 文本。

发送 CREATE 前先保存 Unknown 状态。响应异常时不重发 CREATE，恢复使用新管理连接，核对精确类型及关系后先删用户、确认消失，再删表空间。DROP 响应不确定时另开连接查证，不无界重试。`cleaned` 表示两个精确对象已确认不存在；`cleanup_required` 表示目录、所有权或删除结果仍不确定，必须停止后续数据库测试，由管理员依据该记录逐项调查。不能按名称前缀批量删除，也不能用事务回滚替代对象检查。故障注入模拟真实 CREATE 成功与客户端确认之间的窗口，不代表端到端网络失 ACK 复现。

### 独立聚集主键 profile

```bash
scripts/local-test/run.sh test profile --profile pk-cluster-1 --configuration Debug
scripts/local-test/run.sh test profile --profile pk-cluster-1 --configuration Debug --driver w --w-transport-security PlaintextAllowed
```

仅接受显式 `--profile pk-cluster-1`，并要求调用环境已安全注入独立普通测试用户的
`DAMENG_TEST_CONNECTION_STRING`。profile 通道不读取默认 secrets，不回退持久用户或管理员连接；
连接不进入命令参数。普通账户需有对象操作权限及必要的 `SYS.V$DM_INI` 只读可见性，
不能授予 DBA 来替代。不得修改共享实例或会话参数来制造 `PK_WITH_CLUSTER=1`。

固定过滤器为 `Profile=PkWithCluster1`，排除管理员类和能力探针，自定义过滤只能相与缩小。
TRX 分别为 `profile-pk-cluster-1.trx` / `profile-pk-cluster-1-w.trx`。
非 profile 子进程清除 `DAMENG_TEST_PROFILE`；functional、specification、admin、probes、
offline-fixtures 及普通 all 排除此组，all 不包含 profile。

profile测试逐项核对显式标记、连接覆盖及PK_WITH_CLUSTER=1，再执行LOB数据/索引矩阵。
最初local参数为0的环境阻断保留在证据中；后续独立PK=1实例已完成官方/W各29项，
普通PK=0对照各27项。所测DM8.1.5.60服务器使CLOB/NCLOB/BLOB表的主键后备索引保持NORMAL，
提供程序未改变原SQL/显式注解；只读参数方法不能单独代替完整矩阵。profile不属于all，
TLS及其他版本不据此获支持。实际SHA、TRX、特征与精确清理见 `plans/evidence/019.md`。
