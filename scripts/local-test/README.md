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

工具会从实例的 `PAGE()` 和 `V$DATAFILE` 推导数据文件大小与目录，随机生成名称和口令，授予测试用户 `RESOURCE`、`SOI`，并校验登录后的当前模式。新建表空间按页大小确定初始容量，数据文件自动增长每次 64 MB，最大 1024 MB。成功后，测试连接与容量策略保存在同一 secrets 文件，状态变为 `ready`；文件权限保持 `0600`。后续直接使用下文的测试入口，不要重新生成模板或重置状态。再次运行 `provision` 只校验现有连接，不调整已有表空间。

## 维护与检查

已创建的旧测试空间如仍为 `AUTOEXTEND OFF`，运行 `scripts/local-test/run.sh inspect` 先只读查看其容量、已用空间以及专用模式下表、序列和回收站对象计数。确定要为该专用空间启用有界增长时，显式运行 `scripts/local-test/run.sh grow`。工具会用管理员连接确认 secrets 记录的唯一表空间及数据文件实际对应，再仅对该文件设置 `AUTOEXTEND ON NEXT 64 MAXSIZE 1024`；不会清理对象、改变其他空间或实例全局参数。

运行 `scripts/local-test/run.sh info` 可用管理员连接只读查看服务器版本、页大小和指定实例参数。单项查询失败时显示“未取到”；不会更改实例设置。

运行 `scripts/local-test/run.sh selftest` 可用合成 TRX 检查脱敏后 XML 结构仍有效、短用户名不会破坏标签名或 `Error Message` 等诊断文字、独立账号仍会隐藏，并验证中止的运行不能通过验收。此命令不连接数据库，也不读取真实凭据。

若创建中断或清理失败，状态会停在 `planned`、`tablespace_created`、`user_created`、`grants_applied` 或 `cleanup_required`。工具不会对这些状态自动重试或删除对象。应由管理员根据 secrets 中记录的 `OwnedTestUserName`、`OwnedTestTablespaceName` 和 `TestDatafilePath` 核对并精确处理残留。

## 测试入口

```bash
scripts/local-test/run.sh test unit
scripts/local-test/run.sh test functional
scripts/local-test/run.sh test specification
scripts/local-test/run.sh test admin
scripts/local-test/run.sh test probes
scripts/local-test/run.sh test all
```

`functional` 使用持久测试用户，排除 `DamengMigrationScriptFunctionalTests` 和 `Category=CapabilityProbe`。`specification` 也使用持久测试用户。`admin` 只运行会自行创建、清理临时用户与表空间的 `DamengMigrationScriptFunctionalTests`，使用 secrets 中的管理员连接。`probes` 只运行标有 `Category=CapabilityProbe` 的功能测试。`all` 顺序运行 unit、functional、specification、admin；探针需显式运行。

普通测试优先使用进程已有的 `DAMENG_TEST_CONNECTION_STRING`，没有时使用 secrets 中的测试连接。该环境变量不会改变 `admin` 通道的选择或授权。需要缩小测试范围时，在通道后追加 `--filter '<dotnet test 过滤表达式>'`；工具始终将其与通道固定过滤条件相与，不能扩大管理员通道或把探针混入普通功能测试。

启动脚本优先使用 `DOTNET_HOST_PATH`，然后搜索 `dotnet`，最后尝试 `~/.dotnet/dotnet`。它为每次运行设置独立的 `DOTNET_CLI_HOME`，使用锁定还原、单节点构建和测试。连接串只传入测试子进程环境；测试输出在显示前会对连接串、主机、用户和口令脱敏。若真实数据库测试被跳过，不可把该次运行记作数据库回归通过。

每个测试通道的 TRX 会保存在 `artifacts/query-translation/local-test/<runid>/<lane>.trx`，目录仅当前用户可读，文件在报告前按 XML 文本和属性值脱敏。启动器会打印宿主结果、总数、执行数、通过数、失败数和跳过数。只有宿主结果为 `Completed`、退出码为零、总数大于零、总数等于执行数且执行数等于通过数、无失败或跳过时，通道才可用于验收；否则整个命令退出非零。
