# 计划 004：建立不读取凭据的 PR 离线验收

> 先完成并核对 001、002、003，再执行本计划。只修改白名单，不自动提交、推送、创建 PR 或发布。协调者维护索引；本计划允许准备工作流和本地验证，不要求为验证而擅自触发远端写入。

## 状态与依赖

- 状态：DONE
- Milestone：M1 安全与验收基础
- 执行证据：[004](evidence/004.md)。
- Finding ID：F05
- 优先级：P1；工作量：M；修复风险：LOW
- 类别：tests / dx
- 依赖：001-cli-secret-boundary.md → 002-cli-assembly-identity.md；003-provision-password-policy.md。001 新增合成 CLI 单元、002 新增配置参数、003 扩展 selftest 均须已验收。
- 规划基线：c80c788fb3cd34d97626d578db580264a28a11f3，2026-10-28。
- 仓库根目录：/Users/wanghongyi/Projects/dameng-entityframework-core

## 问题与目标

仓库只有两条标签发布工作流，普通 push/PR 没有仓库定义的离线构建、单元及工具验证。现有不需要数据库的脚本拆批测试位于 functional 项目，而统一 functional 通道即便只过滤该类也要求数据库连接。直接在 CI 复制这条过滤命令会失败；提供伪连接会让潜在误选变成真实连接尝试。

新增真正无凭据的 offline-fixtures 通道，并使 unit 通道同样不读取本地 secrets。用独立 PR 工作流调用统一离线聚合脚本，覆盖两组单元、两驱动的纯夹具、TRX/密码 selftest、Python 扫描测试及 W 包身份核验。保留两条标签发布工作流，离线绿灯不替代真实数据库或发布验收。

## 当前状态与后续合同

.github/workflows/publish-nuget.yml:5–8：

    on:
      push:
        tags:
          - "v*"

.github/workflows/publish-nuget-wdriver.yml:4–7 仅监听 wdriver-v*。基础工作流 :59–65 只跑基础单元，W 工作流 :61–64 跑两组单元。现有 checkout@v6/setup-dotnet@v5 版本可复用，不在此计划更新它们。

scripts/local-test/Program.cs:24–26：

    1 when args[0] == "selftest" => TestLauncher.SelfTest(),
    >= 2 when args[0] == "test" => TestLauncher.Run(args[1..], Root,
        LoadSecrets(required: false)),

即使 unit 不需要连接，Program 也已经读过 secrets。TestLauncher.cs:184 创建的 normalLane 使用 NeedsTestConnection=true；:77–86 在运行前要求连接。其固定过滤器和自定义过滤器在 :201–207 用相与组合，这是必须保留的隔离模式。

另一个更早的边界位于 scripts/local-test/run.sh：:12 的 dotnet --version 探测和 :27–28 的 LocalTest 工具 restore/build 都发生在 TestLauncher 之前，当前均继承父进程连接变量。因此只改托管启动器还不能隔离这些前置工具进程。

test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengScriptExecutorTests.cs:5–10：

    /// Pure script-splitting coverage ... no database needed.
    public sealed class DamengScriptExecutorTests
    {
        [Theory]

该类目前包含 26 个纯拆批测试案例。W 功能项目用链接编译复用该目录。当前没有 offline-fixtures lane 或 OfflineFixture Trait；这两者是本计划新增，不得提前假设已存在。

002 完成后入口接受 --configuration Debug|Release（默认 Debug），001 合成 CLI 测试位于官方 unit 项目，003 selftest 同时验证口令策略和 TRX 验收。原始 SHA 是审查锚点；实施时先核对这三份已验收前置差异，更新实施 SHA/摘录定位后继续。正常可归因的前置修改不是阻塞理由；仅不可归因差异或契约冲突需要暂停核对。

## 文件白名单及禁止项

允许修改或新增：

- scripts/local-test/Program.cs
- scripts/local-test/TestLauncher.cs
- scripts/local-test/run.sh（仅前置宿主探测/工具构建的环境隔离）
- scripts/local-test/README.md
- test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengScriptExecutorTests.cs（仅新增离线 Trait）
- test/W.EntityFrameworkCore.Dameng.FunctionalTests/OfflineFixtureContractTests.cs（新增）
- scripts/ci/verify-offline.sh（新增）
- scripts/ci/test_verify_offline.py（新增，标准库，无额外 Python 包）
- .github/workflows/ci.yml（新增）

两条 publish-nuget*.yml 只读，不增加发布条件，不把它们改成 PR 工作流。不得修改产品代码、包范围、锁文件、global.json、真实 secrets 或仓库分支保护。不得使用 pull_request_target、发布密钥、管理员连接、伪连接或绕过验证的 continue-on-error。不得将完整 artifacts/、临时 CLI 工程或任何 secrets 上传到 Actions。

## Git 与工具准备

分支：codex/004-offline-ci。先运行：

    git status --short
    git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- scripts/local-test/Program.cs scripts/local-test/TestLauncher.cs scripts/local-test/run.sh scripts/local-test/README.md test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengScriptExecutorTests.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/OfflineFixtureContractTests.cs scripts/ci/verify-offline.sh scripts/ci/test_verify_offline.py .github/workflows/ci.yml

所有直接 dotnet 命令先设置独立且可写的 DOTNET_CLI_HOME、DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1、DOTNET_CLI_TELEMETRY_OPTOUT=1。restore/build/test 使用 -m:1 /nodeReuse:false /p:UseSharedCompilation=false，build/test 增加 --disable-build-servers；不清 NuGet 全局缓存。统一 run.sh 已负责这些设置。

## 分步实施及验证

### 1. 在选择通道之后才决定是否读取 secrets

将 TestLauncher 的 secrets 参数改为惰性加载函数，Program 只传入回调。先解析参数、driver、configuration、lane，再判断是否存在 NeedsAdmin 或 NeedsTestConnection；只有需要数据库的通道才调用加载函数或读取 DAMENG_TEST_CONNECTION_STRING。unit 和新增 offline-fixtures 不调用 secrets 加载、不读取连接覆盖值，也不构造依赖真实值的 redactor。

在 run.sh 中，仅对 compatible_host 的每次 --version 探测、LocalTest.csproj 的 restore 和 build 调用使用 env -u DAMENG_TEST_CONNECTION_STRING -u ConnectionStrings__DamengCliFixture。不得在 run.sh 顶层全局 unset：最后启动 LocalTest.dll 的主进程仍保留原环境，由它按解析后的 lane 决定是否读取连接，避免破坏数据库通道的已有环境覆盖行为。LocalTest 是可信配置持有进程，托管解析器是唯一的 lane 解析边界；shell 不重复解析 lane。只传递变量名，禁止为此读取、打印、重写或暂存变量值。

保留数据库通道的现有管理员/普通连接分离与 ready 检查。可信 LocalTest 主进程在 unit/offline 路径中不读取 secrets 或连接环境，也不把它们转发给离线构建/测试子进程；这些子进程必须移除继承的上述两个变量。001 合成测试自行生成需要的合成环境，不能把父进程真实值传入。勿输出环境。自检用“若被调用即抛错”的 secrets loader 证明离线路径调用数为零，另验证数据库描述符仍要求加载，不能全局跳过安全校验。步骤 3 的编排测试另覆盖直接调用 run.sh 的前置工具路径，不只测试 verify-offline.sh 包装后的路径。

验证：

    bash -n scripts/local-test/run.sh
    scripts/local-test/run.sh selftest
    scripts/local-test/run.sh test unit --configuration Release
    scripts/local-test/run.sh test unit --configuration Release --driver w

预期 selftest 包含惰性加载和配置参数用例，退出零；两组单元 Completed、非零、无失败/跳过。不得打开真实 secrets 文件来证明“未读取”；使用合成注入与代码调用边界证据。

### 2. 新增统一 offline-fixtures 合同

通道固定名 offline-fixtures，TRX 标识为 offline-fixtures / offline-fixtures-w。它选择各自的 FunctionalTests 项目，RequiredFilter 固定为 Category=OfflineFixture，NeedsAdmin=false，NeedsTestConnection=false。自定义 --filter 与固定条件相与，不能扩大范围。维持 all 的既有语义，不偷偷加入/替换真实功能通道。

给 DamengScriptExecutorTests 类添加：

    [Trait("Category", "OfflineFixture")]

新增 OfflineFixtureContractTests，使用同一 Trait、普通 Fact。通过 CustomAttributeData/反射检查本程序集所有带该 Trait 的测试：只允许普通 Fact/Theory；禁止 DamengFact/DamengTheory 等数据库属性、CollectionAttribute 及自动执行数据库夹具的集合/类夹具绑定。检查元数据时不要实例化数据库属性、不要构造数据库夹具。该检查不是自动证明测试体无网络，纳入离线 Trait 的新增测试还必须代码审查其依赖。

该 Trait 是后续计划的接入合同：如 014 的 DamengAdminDdlRecoveryStateTests 等纯状态测试添加同样类级 Trait，即自动进入两驱动 offline-fixtures；真实故障/数据库用例仍保留既有 DamengFact 和固定实库通道。不要仅因测试在 FunctionalTests 项目就将其计作实库证据。

验证：

    scripts/local-test/run.sh selftest
    scripts/local-test/run.sh test offline-fixtures --configuration Release
    scripts/local-test/run.sh test offline-fixtures --configuration Release --driver w

预期两个通道各含原 26 项拆批及新增合同检查，故至少 27 项；各自 Completed、零失败/跳过。SelfTest 覆盖固定过滤器与 custom 的相与、两驱动目标项目、未加载 secrets、未知 lane 拒绝；使用合成元数据负例验证错误 Trait 标注会被合同检查拒绝。

### 3. 建立单一离线聚合脚本与进程编排测试

verify-offline.sh 使用 bash 严格模式，从自身位置定位仓库；创建独立 DOTNET_CLI_HOME 并在退出时只清理该目录，显式清除连接环境变量。按下列顺序执行，任一步失败立即非零退出：

    dotnet restore W.EntityFrameworkCore.Dameng.slnx --locked-mode --disable-parallel -m:1 /nodeReuse:false /p:UseSharedCompilation=false
    dotnet build W.EntityFrameworkCore.Dameng.slnx --configuration Release --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
    scripts/local-test/run.sh test unit --configuration Release
    scripts/local-test/run.sh test unit --configuration Release --driver w
    scripts/local-test/run.sh test offline-fixtures --configuration Release
    scripts/local-test/run.sh test offline-fixtures --configuration Release --driver w
    scripts/local-test/run.sh selftest
    PYTHONDONTWRITEBYTECODE=1 python3 -B scripts/audit/test_scan_aggregate_queries.py
    python3 -I scripts/driver-candidate/verify-package.py --require-lock-reference

脚本不运行 functional/specification/admin/probes/all，不将数值合并为实库通过。保留每个通道独立 TRX 和统一验收条件。W 身份核验在锁定还原之后执行，不自动更新 manifest。

test_verify_offline.py 参考 scripts/audit/test_scan_aggregate_queries.py 的 tempfile/subprocess/unittest 模式，创建合成最小仓库和可控子进程替身，核对四条离线通道及环境隔离；模拟 restore/test/selftest/Python/身份校验失败，断言非零并停止后续动作，且聚合脚本从未调度数据库 lane。

该测试还必须把真实 run.sh 复制到合成目录，设置假的 DOTNET_HOST_PATH，并直接调用 run.sh test unit 及 run.sh test functional：假的 dotnet 只记录参数形状和两个合成变量是否存在，不运行任何 .NET/数据库代码。所有 --version、restore、build 调用必须看不到变量；最后的 LocalTest.dll 主进程调用必须保留直接调用者提供的合成环境，以证明数据库覆盖合同未被 shell 层破坏。托管启动器在 unit/offline 下不读取 secrets 或连接值、不向离线测试进程转发变量，由步骤 1 的 SelfTest 独立证明。不要依赖真实 shell 环境凭据，不在失败消息里打印合成哨兵。

编排测试本身不启动真实 dotnet 或网络；verify-offline.sh 不调用 test_verify_offline.py，从而避免测试执行被测聚合脚本时自递归。本地和 CI 的**总门均为以下两条精确命令**，顺序执行，第一条非零时不得运行第二条；编排回归属于总门，不属于聚合脚本内部步骤：

    PYTHONDONTWRITEBYTECODE=1 python3 -B scripts/ci/test_verify_offline.py
    scripts/ci/verify-offline.sh

验证：

    bash -n scripts/ci/verify-offline.sh
    PYTHONDONTWRITEBYTECODE=1 python3 -B scripts/ci/test_verify_offline.py
    scripts/ci/verify-offline.sh

预期 shell 语法正确、编排正负测试全部通过、完整聚合脚本退出零。四个 TRX 宿主分别 Completed、非零、无失败或跳过；selftest/Python/身份工具各退出零并有相应固定成功摘要。

### 4. 新增无凭据 PR 工作流并限制证据上传

ci.yml 触发 pull_request 和 main 分支普通 push；单个 Ubuntu job，permissions 仅 contents: read，超时设为有界值（初始 30 分钟）。checkout@v6 使用 persist-credentials: false，setup-dotnet@v5 读取 global.json。工作流以两个失败即停止的步骤执行上面的总门命令：先 PYTHONDONTWRITEBYTECODE=1 python3 -B scripts/ci/test_verify_offline.py，再 scripts/ci/verify-offline.sh；第二步不得使用 always() 或 continue-on-error 绕过第一步失败。不要在工作流增加 secrets 引用、连接变量值或发布动作。

使用 [actions/upload-artifact@v4 的路径和缺文件选项](https://github.com/actions/upload-artifact/blob/v4/README.md) 仅上传本次干净 CI 工作区产生的 artifacts/query-translation/local-test/*/*.trx；默认不包含隐藏文件。命名含 run id，保留期 7 天；成功路径缺少 TRX 应失败，失败路径可用 always() 保存已生成的安全 TRX，不覆盖先前失败状态。不得上传整个 artifacts、临时工程、bin/obj 或环境转储。上传前的 TRX 已经过统一 launcher 脱敏；新工作流没有真实凭据。

验证：

    PYTHONDONTWRITEBYTECODE=1 python3 -B scripts/ci/test_verify_offline.py
    scripts/ci/verify-offline.sh
    git diff --check
    git diff --exit-code -- .github/workflows/publish-nuget.yml .github/workflows/publish-nuget-wdriver.yml
    git status --short

扩展编排测试中的工作流契约断言，至少核对触发类型、只读权限、总门两条命令及顺序、无 secrets/pull_request_target/发布步骤、上传路径白名单。若环境已有 actionlint，另外运行 actionlint .github/workflows/ci.yml 并要求退出零；没有该工具则记录 YAML 语义 lint 尚未执行，不假称远端 workflow 已运行。远端 PR 触发结果只能在后续获准推送/PR后追加，不为取得该证据自动推送。

### 5. 更新使用文档与交付证据

README 说明 offline-fixtures 的新合同、unit/offline 不读取 secrets、Release 参数来自 002、增加纯测试的 Trait/禁止属性，以及本地/CI 总门均先运行编排测试再运行 verify-offline.sh。说明 run.sh 前置工具不继承连接变量，LocalTest 是保留原环境的可信配置持有进程，但离线 lane 不读取、不向构建/测试子进程转发连接；总门仅作离线验收。记录所有命令与非敏感 TRX 路径，区分本地脚本验证和真实 Actions 运行。不得把此工作流设成“实库发布通过”证明。

验证再次执行 git diff --check 与 git status --short，预期白名单外无变化。

## 完成标准

- [x] unit/offline-fixtures 在合成加载器必抛错时仍不调用它；离线构建/测试子进程无继承连接变量。
- [x] 直接 run.sh 的宿主探测、工具 restore/build 不继承两个连接变量；可信 LocalTest 主进程保留原环境但在离线 lane 不读取、不转发，数据库通道覆盖合同不变；合成编排与 SelfTest 已分别证明两侧边界。
- [x] 两驱动 offline-fixtures 固定 Category=OfflineFixture 且 custom 只能相与，至少原 26 项和合同检查执行。
- [x] 新编排回归由总门第一条精确命令执行；001 合成 CLI 单元、002 配置/身份检查、003 口令 selftest、TRX 自检、Python 7 项既有扫描回归、W 身份核验由第二条命令的聚合脚本执行，无自递归。
- [x] 总门两条命令按顺序通过；四个测试通道均 Completed、非零、无失败/跳过；工具命令退出零；任意失败使总门停止且失败。
- [x] PR workflow 无凭据、无发布、无实库调用；上传仅限安全 TRX，两条标签发布 workflow 原样保留。
- [x] 本地验证与远端运行证据分开，未执行远端时明确记录；协调者审核后更新状态。

## STOP 与维护

若必须提供假连接才能运行离线测试、发现带 OfflineFixture 的测试依赖真实数据库/集合夹具、001 的合成 CLI 仍读取父环境真实值、需要更新锁文件或权限超出 contents: read，停止并报告。若两次合理修复后同一验收仍失败，也停止。不要通过允许跳过或降低 TRX 条件制造绿灯。

后续纯状态/脚本测试应复用固定 Trait，不能把真实网络/数据库测试伪装为离线。新的子进程或 lane 要补充加载器、环境和固定过滤器自检。真实功能、规范冒烟、管理员测试继续由本地统一入口按官方→W 串行运行，且绝不与这些离线统计混为服务器能力证据。

## 批次交付与统一证据

本批 M1 为起始批次，按正文的技术前置顺序执行。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/004.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为确认问题或验证缺口，须以“已修复并验证”闭合；补足有辨别力的测试证据也属于验证缺口的修复。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M1.md` 的输入。
