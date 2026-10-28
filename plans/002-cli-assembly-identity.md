# 计划 002：让 CLI 验收使用当前测试宿主的程序集

> 逐步执行并核对每个命令结果。只修改本文白名单，保留 001 的安全边界。不得自动提交、推送、创建 PR、发布或修改索引；完成后交由协调者审核证据。

## 状态与依赖

- 状态：DONE
- Milestone：M1 安全与验收基础
- 执行证据：[002](evidence/002.md)。
- Finding ID：F08
- 优先级：P2；工作量：M（含统一入口配置与干净 Release 验证）；修复风险：LOW
- 类别：tests / dx
- 依赖：001-cli-secret-boundary.md 已完成；本计划是 004 的前置条件。
- 规划基线：c80c788fb3cd34d97626d578db580264a28a11f3，2026-10-28。
- 仓库根目录：/Users/wanghongyi/Projects/dameng-entityframework-core

## 问题与目标

CLI 功能测试固定选择仓库 src 下的 Debug DLL。Release 测试在干净环境会找不到 DLL，在残留 Debug 产物时可能验证不同版本。目标是让临时 CLI 工程引用当前测试宿主实际加载的提供程序程序集，并在嵌套进程中核对身份，同时为统一测试入口新增显式 Debug/Release 选择。

重要现状：scripts/local-test/run.sh test 当前**不支持** --configuration。以下使用该参数的验收命令均须在步骤 2 实现并验证之后运行，不得把它当成现有能力。

## 当前状态与接续状态

基线 test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs:44–53：

    var providerAssemblies = ProviderProjects
        .Select(project => Path.Combine(repoRoot, "src", project, "bin", "Debug", "net10.0", project + ".dll"))
        .ToArray();
    foreach (var providerAssembly in providerAssemblies)
    {
        Assert.True(File.Exists(providerAssembly), "Build the provider before running the CLI test: " + providerAssembly);
    }

同文件 :359–362 将选择的路径写进临时工程 HintPath。:544–567 已用 SHA-256/MVID 校验嵌套进程的 W.DamengProvider DLL，这是身份检查模式，但当前没有对两个 EF 提供程序 DLL 作等价核对。

scripts/local-test/TestLauncher.cs:35 的选项白名单只有 --filter、--driver、--w-transport-security；:128–132 构造 dotnet test 参数时没有配置项。统一入口默认构建 Debug，run.sh 自身启动的 LocalTest 工具也使用 Debug；工具自身配置和被测试项目配置可以不同。

001 会新增 DamengCliTestHarness.cs、DamengCliFixtureTests.cs，把共享的非敏感进程/临时工程逻辑提取供合成单元使用。以 001 已审核后的实现为直接前置状态；如果它没有提供这些 helper/测试或命名配置边界，停止协调，不自行重新实施 001。

## 约束与修改范围

文件白名单：

- test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs
- test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengCliTestHarness.cs（001 新增）
- test/W.EntityFrameworkCore.Dameng.Tests/DamengCliFixtureTests.cs（001 新增）
- scripts/local-test/TestLauncher.cs
- scripts/local-test/Program.cs（仅 usage/帮助及配置相关自检接线）
- scripts/local-test/README.md

不改产品程序集、项目版本、包锁、全局 SDK、两条发布 workflow 或真实 secrets；不以删除用户的 bin/Debug 来掩盖问题。保留官方/W 驱动来源区别和 W.DamengProvider manifest 校验。官方/W 实库通道共享模式，只能串行执行。DDL 精确清理及命名连接安全边界沿用 001。

## Git 与漂移检查

分支：codex/002-cli-assembly-identity。先运行：

    git status --short
    git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengCliTestHarness.cs test/W.EntityFrameworkCore.Dameng.Tests/DamengCliFixtureTests.cs scripts/local-test/TestLauncher.cs scripts/local-test/Program.cs scripts/local-test/README.md

原始 SHA 是审查锚点。将 001 的已审核差异列为预期漂移，更新实际实施 SHA 与摘录定位后继续；正常可归因的前置修改无需再次请求确认。只有不可归因差异、未完成合并或契约冲突才暂停协调。不得覆盖已有未提交修改。

## 分步实施及验证

### 1. 选取实际加载程序集并验证嵌套身份

从当前测试使用的公开类型获取 Assembly.Location：基础包可使用 DamengDriver 类型，W 分支使用 Microsoft.EntityFrameworkCore.DamengWDriverDbContextOptionsBuilderExtensions 类型。不要按仓库路径、Debug/Release 字符串或仅程序集名称猜位置。对空 Location、文件不存在、加载名称与预期不符明确失败，不回退 Debug。

临时工程引用这些绝对路径。将安全的程序集名称、MVID 与 SHA-256 作为期望身份传入合成验证程序/实际嵌套程序，比较复制到输出目录的文件及运行时加载程序集；禁止只比较版本号。保留 W 底层驱动既有 manifest 检查。临时消费者本身可使用 Debug 构建，其被测提供程序必须和当前宿主相同。

新增测试覆盖：当前宿主程序集被选中、伪造的旧 Debug 候选不会被选中、缺失 Location 明确失败、相同版本号但不同 MVID/散列拒绝、官方只验证基础包、W 验证基础包与扩展包并保留驱动身份校验。沿用 001 的普通 Fact/Theory 合成模式，无数据库凭据。

验证（当前已有命令）：

    scripts/local-test/run.sh test unit --filter FullyQualifiedName~DamengCliFixtureTests

预期非零、Completed、无失败或跳过；新增身份负例必须作为预期异常通过，不访问真实数据库。

### 2. 为统一入口实现配置白名单

TestLauncher 增加 --configuration，只接受精确的 Debug 或 Release，默认 Debug；拒绝空值、重复、未知值，在启动任何子进程前完成校验。将选定值作为独立 ArgumentList 元素传给每个 dotnet test 的 --configuration。restore 保持锁定和串行，不改变包图；日志打印非敏感配置名，TRX 仍按 runId 和 driver 区分。不要向 run.sh 的 LocalTest 工具自身注入该配置要求。

扩展已有 SelfTest，使用可检查的参数构造/可替换进程启动点断言默认、两个合法值及非法/重复值；不得为了自检发起数据库进程。更新 Program usage 与 README，明确该参数是新增能力，所有 lane 一致适用。

验证：

    scripts/local-test/run.sh selftest
    scripts/local-test/run.sh test unit --configuration Release --filter FullyQualifiedName~DamengCliFixtureTests
    scripts/local-test/run.sh test unit --configuration Debug --filter FullyQualifiedName~DamengCliFixtureTests

预期 selftest 退出零，两种配置均为 Completed、非零、无跳过，输出显示所选配置。非法值由 SelfTest 断言在启动前拒绝，不依赖终端手工判断错误消息。

### 3. 在无提供程序 Debug 产物的干净源码副本验证 Release

不能删现有工作区产物。使用下列命令从当前文件内容创建临时源码副本：只复制 Git 跟踪文件和 001 明确新增文件，不复制 secrets、bin、obj、artifacts。该副本只用于验证，路径不提交。

    export DAMENG_PLAN002_COPY="$(mktemp -d "${TMPDIR:-/tmp}/dameng-plan002.XXXXXX")"
    python3 -B - <<'PY'
    import os, pathlib, shutil, subprocess
    root = pathlib.Path.cwd()
    target = pathlib.Path(os.environ['DAMENG_PLAN002_COPY'])
    paths = subprocess.check_output(['git', 'ls-files', '-z']).decode().split('\0')
    paths += ['test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengCliTestHarness.cs',
              'test/W.EntityFrameworkCore.Dameng.Tests/DamengCliFixtureTests.cs']
    for name in sorted(set(filter(None, paths))):
        source = root / name
        if source.is_file():
            destination = target / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
    PY
    (cd "$DAMENG_PLAN002_COPY" && scripts/local-test/run.sh test unit --configuration Release --filter FullyQualifiedName~DamengCliFixtureTests)

预期临时副本没有提供程序 src 下的 bin/Debug，Release 合成 CLI 身份测试通过；LocalTest 工具自己的 Debug 输出不影响此断言。副本不存在真实 secrets，不能用该副本发起真实库测试。只在完整退出且证据收集后删除本次变量指向的唯一临时目录，不遍历其他临时项目。

### 4. 串行验证两驱动真实 CLI，再做单元回归

在原实施工作区使用已有安全测试配置，依次运行：

    scripts/local-test/run.sh test functional --configuration Release --filter FullyQualifiedName~MigrationsAndScaffoldRunThroughDotNetEfCli
    scripts/local-test/run.sh test functional --configuration Release --driver w --w-transport-security PlaintextAllowed --filter FullyQualifiedName~MigrationsAndScaffoldRunThroughDotNetEfCli
    scripts/local-test/run.sh test unit --configuration Release
    scripts/local-test/run.sh test unit --configuration Release --driver w
    scripts/local-test/run.sh selftest
    git diff --check
    git status --short

每条实库命令完成后再开始下一条。预期嵌套提供程序 MVID/散列与当前 Release 宿主一致，全部 TRX 非零、Completed、无失败/跳过，安全/清理回归保留。缺少实库环境则报告尚缺该证据，不能标记 DONE。

## 测试矩阵与完成标准

| 维度 | 必须成立 |
| --- | --- |
| 配置参数 | 默认 Debug；仅 Debug/Release；重复/未知在启动前拒绝 |
| 干净 Release | 不依赖 src 中 Debug 产物，合成 CLI 成功 |
| Debug/Release 并存 | 只使用实际宿主程序集，旧候选无效 |
| 身份异常 | 位置缺失、版本相同但字节不同均明确失败 |
| 官方/W | 基础/扩展提供程序身份正确，W 驱动 manifest 不变 |
| 安全接续 | 不恢复 raw argv，不输出源码/配置，不破坏独立清理 |

- [x] 参数自检、两个配置合成测试、干净副本 Release 测试通过。
- [x] 官方与 W 的 Release 实库 CLI 按顺序通过，保存各自非敏感 TRX。
- [x] 完整 Release 两组单元通过，Completed、非零、无失败或跳过。
- [x] 不再从固定 src/.../bin/Debug 构造被测提供程序路径；其他临时消费者 Debug 路径可以保留。
- [x] 只有白名单文件发生修改；由协调者更新状态。

## STOP 与维护

如果 Assembly.Location 在支持的测试宿主中为空、需要改变产品依赖才能定位程序集、前置 001 的命名配置/清理契约未成立，或配置参数不得不转成任意 MSBuild 属性透传，停止报告。两次合理修复后验证仍失败也停止。不要通过删测试、静默跳过身份校验或改为版本号比较消除失败。

后续单文件发布、测试运行器变化或包消费者验收可能影响 Location，需要独立设计，不在本计划泛化。004 将直接依赖这里实现的 --configuration Release；未来新增 lane 必须继承同一白名单和参数构造。

## 批次交付与统一证据

本批 M1 为起始批次，按正文的技术前置顺序执行。所有里程碑由协调者在 `plans/README.md` 统一维护，执行者提交本项结果。

本段补充前述文件白名单：必须新增或更新 `plans/evidence/002.md` 作为唯一正式执行记录。本文可保留状态摘要，原始 TRX/脱敏日志仍存放统一入口的忽略目录；不再另建带标题后缀的同号证据文件。记录实际 SHA、命令、用例名及通过/失败/跳过数、TRX 路径、环境边界、结论、临时资源精确清理和复核结果。若被测文件尚未提交，还须记录白名单内被测文件的哈希清单或脱敏补丁指纹，不能只用 HEAD 标识不同工作区内容；不为获得 SHA 自动提交。

本项为确认问题或验证缺口，须以“已修复并验证”闭合；补足有辨别力的测试证据也属于验证缺口的修复。必需检查缺失时保持 BLOCKED 并记录解除条件，不修改状态凑齐验收。完成后向协调者交付证据位置，作为本批 `plans/evidence/M1.md` 的输入。
