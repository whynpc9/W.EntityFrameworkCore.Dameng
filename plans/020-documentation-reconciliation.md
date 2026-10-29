# 计划 020：按已闭合证据统一双驱动能力、发行称谓与权限示例

> 执行者应从头阅读本计划，按步骤完成只涉及文档的修订。每步验证符合预期后再继续；触发 STOP 条件时记录具体缺口，不用文字修改代替产品验证。本计划不授权提交、推送、创建 PR、发行或部署。
>
> 本计划属于 M6，唯一承接问题 **F14**。001–019 各自负责其产品或测试问题，020 只核对这些计划的最终结论及其公开表述，不接管未完成修复。协调者维护索引时，将结果交回协调者，不并发编辑索引。

## 状态

- 状态：BLOCKED。
- 优先级：P3；作为六批工作的文档收口门，须等待前置结论完整。
- 工作量：S–M，主要取决于前 19 项证据是否齐备。
- 修复风险：LOW；主要风险是扩大支持声明或改写历史验收。
- 类别：docs。
- 里程碑：M6。
- 唯一问题：F14，驱动能力说明和应用用户权限示例发生漂移。
- 依赖：001–019 的结论与证据全部闭合，最终集成产品 revision 的官方 `all`、W `all` 及 019 适用独立 profile 证据通过；调查项可经验证确证无缺陷，不能以缺少环境或未复现就宣布无缺陷。
- 编写基线：`c80c788fb3cd34d97626d578db580264a28a11f3`，2026-10-28（Asia/Shanghai）。
- 建议分支：`codex/020-documentation-reconciliation`。
- 执行证据：`plans/evidence/020.md`，本计划编写阶段不创建验收记录。

## 为什么要做

架构文档仍把官方驱动的 Serializable 跟踪保存限制写成通用限制，而 W 已有明确限定 profile 的成功回归。迁移指南仍称 W 为开发候选，且可安装的 SQL skill 向普通应用用户示范 DBA 授权，与迁移指南的最小权限说明矛盾。修复这些具体漂移后，还须将前 19 项最终结论与兼容矩阵逐项对账，让读者能区分新增支持、提前拒绝、测试补足和调查证伪。

这不是新一轮功能实现或发布。文档收口不能将单元 SQL 证据升级为服务器能力，不能将一个明文 profile 的结果升级为 TLS、其他版本或所有页大小支持。

## 当前状态与可复核片段

以下路径相对于仓库根 `/Users/wanghongyi/Projects/dameng-entityframework-core`。行号属于编写基线，执行时以符号和内容定位。

1. `docs/architecture.md:89` 的事务段缺少驱动限定：

   ```text
   DML 事务回滚和保存点回滚已在参考服务器上验证。尽管当前驱动程序的基础能力标志不支持，
   保存点仍可在该服务器上工作。隔离级别方面，已验证 `ReadCommitted` 可用于跟踪式
   `SaveChanges`；`Serializable` 可用于查询和 `ExecuteUpdate`，但不能用于跟踪式
   `SaveChanges`。
   ```

   与之对应，`test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengIsolationLevelFunctionalTests.cs:80` 已区分 `#if WDRIVER` 的 `SaveChangesSucceedsAfterBeginningReadUncommittedOrSerializable` 与官方驱动的失败用例。`docs/compatibility.md:148` 写明 W 的 RU/Serializable 跟踪保存只在自报 `8.1.5.60` 的 profile 上验证，其他 profile 拒绝合同仍 pending。

2. `docs/migrations.md:74` 的当前使用章节仍为：

   ```markdown
   ## 使用 W.DamengProvider 驱动（开发候选）
   ```

   `docs/release-0.4.0.md:4` 已记录两个包公开发布；该记录是历史证据，不应重写。基线版本为基础包 `0.4.0`、W 扩展包 `0.4.0-preview.1`，W 精确依赖 `0.2.0`。可以用“预览版”修正当前指南，不能把预览包称为稳定版，也不能据本计划声称又发布了新版本。

3. `skills/dameng-sql/SKILL.md:90` 的普通用户示例：

   ```sql
   CREATE USER app_user IDENTIFIED BY "********"
     DEFAULT TABLESPACE app_data;

   GRANT DBA TO app_user;
   ```

   同仓库 `docs/migrations.md:21` 已按用途列出 `RESOURCE`、查询历史目录需要的 `SOI` 和 `DBMS_LOCK` 条件，25 行明确“不要为了跑迁移授予 DBA”。`skills/dameng-ef-migrations/SKILL.md:94` 保留该权限结论的实例与日期限定。这是文档示例问题，不是产品自动提权漏洞。

4. 以 `docs/compatibility.md:133` 的写法作为能力边界范例：

   ```text
   下表只描述与官方驱动不同或需要单独说明的能力；
   未列出的 SQL/模型/迁移/反向工程能力与上表共用，并在 W 通道中以同一套功能测试重跑。
   证据只来自服务器自报 `8.1.5.60`、UTF-8、明文连接的本地 profile；TLS 与其他版本均未验证。
   ```

   修订沿用“单元级已验证 / 真实环境已验证 / 明确拒绝 / 未验证”的区别，注明具体驱动、服务器条件、行为和证据日期。历史数字只留在对应历史记录中；新测试数量不能写回旧验收轮次。

## 必须保留的约定

- 文档默认中文，代码标识、正式名称、命令和链接保留原文。
- `W.EntityFrameworkCore.Dameng` 独立于应用框架；不改变锁定 EF Core 10、官方驱动版本范围及锁文件，也不借文档收口升级依赖。
- 官方驱动异步同步回退、W 禁止内建自动重试、两驱动单命令修改批次是既定边界。
- 无参 `DateTime.ToString()` 使用服务器默认文本；不承诺 CLR 当前文化，也不为 `GroupBy` 推断时间粒度。
- DDL 隐式提交；不声称迁移原子性。反向工程和幂等脚本只说明已经实现且有相应证据的范围。
- 规范项目四项自有冒烟不等于上游关系数据库一致性测试。
- 不读取、打印、粘贴或提交真实连接串、口令、主机、用户名；只记录测试 ID、脱敏证据路径和非敏感环境特征。
- 本计划修改 SQL 技能示例前，先读 `skills/dameng-sql/SKILL.md`；协调迁移说明前读 `skills/dameng-ef-migrations/SKILL.md`。不执行示例 SQL，不进行授权或环境初始化。

## 修改范围

仅允许修改下列当前使用文档和本计划证据：

- `README.md`：只有前置结论影响公开简介或当前支持边界时才改。
- `docs/architecture.md`：双驱动隔离级别、保存点及由前置结论改变的实际架构边界。
- `docs/migrations.md`：W 预览版称谓，以及已经闭合的迁移行为/使用边界。
- `docs/compatibility.md`：逐项有证据的当前能力、拒绝或验证层级变化；历史记录保持原样。
- `docs/query-functions.md`、`docs/datetime-string-translation.md`：仅在查询/时态前置结论改变对应说明时改。
- `skills/dameng-sql/SKILL.md`：普通应用账号权限示例及与已闭合结论直接相关的限定。
- `skills/dameng-ef-migrations/SKILL.md`：与迁移指南一致的当前执行边界；不更改历史验收事实。
- `plans/evidence/020.md`：新建本计划的逐项对账和检查结果。
- `plans/README.md`：仅由协调者或其明确交给本执行者维护时更新 020 状态与证据链接；不改其他计划结果。

明确不改：`src/`、`test/`、`scripts/`、`.github/`、依赖/锁文件、`AGENTS.md`、历史 release/validation/执行台账、`plans/deep-audit-2026-10-28.md`、001–019 计划文件及其已归档证据。发现这些范围需要修正时交回所属计划，不在文档计划中顺手修产品或历史证据。

## Git 与漂移检查

先记录当前工作区状态，保留已有未提交改动。使用协调者给出的集成工作树和基线；分支名建议 `codex/020-documentation-reconciliation`，不要自行重置或覆盖其他代理的文件。不自动 commit、push、merge、开 PR 或发行。

```bash
cd /Users/wanghongyi/Projects/dameng-entityframework-core
git status --short
git rev-parse HEAD
git diff --stat c80c788fb3cd34d97626d578db580264a28a11f3..HEAD -- README.md docs/architecture.md docs/migrations.md docs/compatibility.md docs/query-functions.md docs/datetime-string-translation.md skills/dameng-sql/SKILL.md skills/dameng-ef-migrations/SKILL.md plans/README.md plans/evidence/020.md
```

本计划安排在最后，前置计划的已验修改会造成预期漂移；逐一将这些差异关联至 001–019 的证据和实际代码。原始审查 SHA 与本计划编写基线保持不变；对已验收前置修改，在执行证据中更新当前摘录定位、实际内容和实施 SHA 后继续，不重复请求用户确认。已被有证据的前置工作修正的 F14 片段只需核验，不恢复旧文案。只有无法归属、缺少应有证据的非预期差异或证据/契约冲突才暂停核对，不能因为正常依赖变更停工。

## 所需命令

| 目的 | 命令 | 预期 |
| --- | --- | --- |
| 当前工作区/基线 | 上面的 Git 命令 | 记录完整 SHA 与原有改动；无无归属的范围漂移 |
| 核对旧措辞 | `rg -n '开发候选|Serializable|GRANT DBA TO app_user|TLS|pending|未验证|未接入' docs/architecture.md docs/migrations.md docs/compatibility.md skills/dameng-sql/SKILL.md` | 修订后没有旧 W 使用章节标题和该 DBA 授权示例；其他命中逐项保留合理限定 |
| 检查格式 | `git diff --check` | 退出 0 |
| 核对版本边界 | `git diff --exit-code c80c788fb3cd34d97626d578db580264a28a11f3 -- Directory.Packages.props global.json ':(glob)**/packages.lock.json'` | 退出 0；若前置已存在相关漂移，STOP 交协调者判定 |
| 唯一归属/证据检查 | 步骤 1 与步骤 4 的 Python 命令 | 分别输出证据齐备、20 项唯一闭环 |
| 本地文档链接 | 步骤 5 的 Python 命令 | 输出 `Local links OK`，不请求远程 URL |

不为纯文档修改新增单元测试，也不无条件重跑全部 .NET/数据库测试。只复用前置计划已核验的证据；若内容变化需要新服务器事实才能支撑，则返回对应实现/调查计划，完成那里的验证后再继续。任何必要的 .NET 调用仍须设置可写 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`，通过仓库统一入口并单节点/禁共享编译；本计划本身不执行这些调用。

## 步骤

### 1. 核验前置证据与唯一归属

逐份阅读 001–019 的执行证据、当前结论、所审 SHA 和相应代码/测试。应有每项自身的明确完成证据，不能拿本审查报告或通用测试总数替代；I 类须有可达性/模型/服务器等适用边界的最小表征结论。缺环境、缺权限、跳过或仅仅没看见问题不满足“确证无缺陷”。

同时核对协调者已归档的最终集成验收：在 `plans/evidence/M6.md` 的集成验证段提供完整产品 SHA、官方 `scripts/local-test/run.sh test all --configuration Release`、W `scripts/local-test/run.sh test all --driver w --w-transport-security PlaintextAllowed --configuration Release`，以及 019 的适用独立 `pk-cluster-1` profile 两驱动串行结果。每个必需通道须有 TRX/脱敏日志路径、预期用例范围、宿主 Completed、退出码 0、`Total = Executed = Passed > 0`、失败和跳过均为 0，以及精确清理结论。两个普通 `all` 必须来自同一最终集成产品 revision；独立 profile 的证据须覆盖当前 019 实现及其受影响调用链，后续相关修改要复验。检查真实证据，不用历史验收数字代替。

`M6.md` 此时可以是尚待 020 关闭的批次记录，但集成验证段必须已经完成；不能要求 M6 整体先 DONE 而形成循环依赖。020 仅修改文档时可沿用该产品 revision 的验收，记录文档后续提交关系，不为新文档 SHA 无条件重跑产品测试。若被测文件尚未提交，应核对其白名单文件哈希清单或脱敏补丁指纹，不能只用 HEAD 认定两轮内容相同，也不为取得 SHA 自动提交。最终集成运行由协调者安排；证据缺失、失败、跳过或无法对应当前产品代码时，020 保持 BLOCKED。

固定归属如下，020 不重新分派：

| 里程碑 | 计划 → 唯一问题 |
| --- | --- |
| M1 | 001 → F01；002 → F08；003 → F10；004 → F05 |
| M2 | 005 → F02；006 → I01；007 → F06；008 → I03 |
| M3 | 009 → F03；010 → F09；011 → F12 |
| M4 | 012 → I02；013 → I06 |
| M5 | 014 → F11；015 → F04；016 → F07；017 → F13 |
| M6 | 018 → I04；019 → I05；020 → F14 |

执行证据统一按 `plans/evidence/NNN.md` 归档；若协调者已提供其他有效路径，先由协调者在索引中明确映射并调整此检查，不能静默漏检。每份证据至少能找到实际 revision、所执行命令、结果/环境边界、清理结论，以及修复完成或调查证伪的理由。核对测试运行所对应代码与当前集成代码：有受影响代码后续变化时，回到所属计划复验，不把旧 SHA 的绿色直接移植到当前版本。

在 `plans/evidence/020.md` 建立对账表，固定列为“计划 / 问题 / 结论 / 前置证据 / 当前文档处置 / 证据层级及限制”。F 项结论只能为 `已修复并验证`，其中验证缺口包括补足具有辨别力的证据；I 项允许 `已修复并验证` 或 `确证无缺陷`。尚不满足时可以记录 `待完成`，但整项 020 不可 DONE。001–019 填实际证据路径；020 行记录本计划检查，不自引用为产品事实来源。对不改变公开能力的安全/测试/DX 项明确写“无需提升兼容性声明”，仍需列行。另列最终集成产品 SHA、官方/W `all` 和独立 profile 的实际结果及 `M6.md` 证据定位。

**验证**：

```bash
python3 -B - <<'PY'
from pathlib import Path
missing = [f'plans/evidence/{n:03d}.md' for n in range(1, 20)
           if not Path(f'plans/evidence/{n:03d}.md').is_file()]
if not Path('plans/evidence/M6.md').is_file():
    missing.append('plans/evidence/M6.md')
if missing:
    raise SystemExit('缺少前置证据：' + ', '.join(missing))
print('001–019 and M6 evidence files present; inspect conclusions and integration results before proceeding')
PY
```

预期退出 0，随后逐项实读确认闭合，并核对 M6 集成验证段的实际 TRX/日志和清理结果；文件存在只是必要条件。任何未闭合项或缺失的最终集成验收都须保留为阻塞，不能先结束 020。

### 2. 修正 F14 的三个具体漂移

1. 在 `docs/architecture.md` 的“事务与迁移”段明确拆开官方与 W：官方驱动的已知跟踪保存限制保持；W 的 RU/Serializable 成功只引用已验 profile，并保留其他 profile 未验边界。保存点能力标志的旧描述同样限定为对应驱动，不把“基础标志不支持”套到 W。以实际测试 `DamengIsolationLevelFunctionalTests` 和 `docs/compatibility.md` 的当前对应段为依据。
2. 将 `docs/migrations.md` 的 W 当前使用章节称谓改为“预览版”，引用现有发行记录。不要改历史记录中的“当时是开发候选”，也不要自动把版本提升至计划之外的版本。
3. 删除 `skills/dameng-sql/SKILL.md` 中对普通 `app_user` 的 `GRANT DBA` 示范。将账号/表空间创建明确归属管理员操作；应用权限说明指向迁移指南的用途矩阵，按已验证实例描述 `RESOURCE`、必要时的 `SOI` 与 `DBMS_LOCK` 条件。可在示例中保留 `GRANT RESOURCE TO app_user;`，但须紧邻说明它仅对应已验用例，运行 `Database.Migrate()` 另有历史目录权限前提，不承诺覆盖所有部署。

不要执行 SQL，也不要在运行中的数据库撤销任何既有权限；本项只改变建议文字。

**验证**：

```bash
python3 -B - <<'PY'
from pathlib import Path
migrations = Path('docs/migrations.md').read_text()
skill = Path('skills/dameng-sql/SKILL.md').read_text()
assert '## 使用 W.DamengProvider 驱动（开发候选）' not in migrations
assert '## 使用 W.DamengProvider 驱动（预览版）' in migrations
assert 'GRANT DBA TO app_user;' not in skill
architecture = Path('docs/architecture.md').read_text()
assert 'Serializable' in architecture and 'W' in architecture and '官方' in architecture
print('F14 stale wording removed; driver/profile statements require evidence review')
PY
git diff --check
```

预期均退出 0；在证据中逐句列出驱动/profile 限定与事实来源，不能把关键词存在当作语义正确的证明。

### 3. 按 20 项结论对账公开能力

按步骤 1 的对账表逐项审核，至少覆盖以下关系：

- F01/F08/F10/F05/F09/F11/F13：安全、夹具、CI 或覆盖改进本身不增加数据库能力；必要时修订验证层级，不借测试数量扩大声明。
- F02/F06/I01/I03：查询空值、LOB 拒绝、时间字面量和转换器边界；区分 SQL 生成、真实执行结果、读写精度。I01 不得用参数回归替代字面量证据。
- F03/F12：运行时驱动一致性、HasTables 异步/超时诊断；官方驱动自身的同步回退限制继续保留，不因 W 局部改进宣称所有路径真正异步。
- I02/I06：共享列与嵌套复杂属性的模型验证结论；若确证上游已阻断，应记录证伪及验证范围，不增加并不存在的本提供程序实现宣称。
- F04/F07：纯注释和历史脚本幂等性；只按实际运行的命令形态、标识符与驱动更新范围，不能把单元 SQL 断言称为实库重复执行通过。
- I04/I05：目录完整性、LOB 与聚集配置；合成目录守卫不能冒称真实服务器曾丢目录，单个页大小/PK_WITH_CLUSTER 条件不能外推所有部署。
- F14：具体文案漂移的移除及各页面一致性。

只更改受事实影响的段落。新增当前验证记录须有本轮日期、测试名称/运行 ID、SHA 和环境限定；不重写 `docs/release-*.md`、`docs/*validation.md` 或旧台账，尤其不把旧失败轮次改为成功。

在证据末尾登记 D01（提交失 ACK）、D02（TLS profile）、D03（DataSource/LOB）为后续可选方向；没有另行完成它们时，现有 pending/未接入表述保持。三个方向不参与 20 项计数，也不是本轮六个里程碑的完成条件。

**验证**：

```bash
git diff -- README.md docs/architecture.md docs/migrations.md docs/compatibility.md docs/query-functions.md docs/datetime-string-translation.md skills/dameng-sql/SKILL.md skills/dameng-ef-migrations/SKILL.md
rg -n 'TLS|其他版本|pending|未验证|未接入|单命令|重试|同步回退|文化|粒度' README.md docs/architecture.md docs/compatibility.md docs/datetime-string-translation.md
```

预期 diff 每处当前能力变化均能对应对账表的一行和具体证据；相关未验证/既定边界仍存在。对每个新增“真实环境已验证”声明，在 020 证据中记录实际 TRX/测试名及 revision。发现没有证据的能力提升即 STOP。

### 4. 检查 20 项无遗漏、无重复并记录归档结论

补齐 020 对账行，记录 F14 检查命令和结果。对 `确证无缺陷` 的行列出为什么原假设不成立，以及实际运行覆盖的边界；不能用“本次不做”代替结论。重复列出在其他文档的同一问题仍只归属一个计划。

**验证**（按步骤 1 固定表列顺序解析 `plans/evidence/020.md`）：

```bash
python3 -B - <<'PY'
from pathlib import Path
import re
expected = {
    '001':'F01', '002':'F08', '003':'F10', '004':'F05',
    '005':'F02', '006':'I01', '007':'F06', '008':'I03',
    '009':'F03', '010':'F09', '011':'F12', '012':'I02',
    '013':'I06', '014':'F11', '015':'F04', '016':'F07',
    '017':'F13', '018':'I04', '019':'I05', '020':'F14',
}
text = Path('plans/evidence/020.md').read_text()
rows = re.findall(r'^\|\s*(\d{3})\s*\|\s*([FI]\d{2})\s*\|\s*([^|]+)\|', text, re.M)
assert len(rows) == 20, f'期望20项，实际{len(rows)}'
assert len({n for n, _, _ in rows}) == 20, '计划重复'
assert len({f for _, f, _ in rows}) == 20, '问题重复'
assert {n:f for n, f, _ in rows} == expected, '归属不一致'
for _, finding, status in rows:
    allowed = ('已修复并验证', '确证无缺陷') if finding.startswith('I') else ('已修复并验证',)
    assert status.strip() in allowed, f'{finding}结论不符合F/I闭环规则'
assert sum(f.startswith('F') for _, f, _ in rows) == 14
assert sum(f.startswith('I') for _, f, _ in rows) == 6
for item in ('D01', 'D02', 'D03'):
    assert item in text, f'未登记后续可选方向{item}'
print('20 issues uniquely closed; 3 directions remain separate')
PY
```

预期退出 0。此检查只验证结构和状态值，仍以步骤 1、3 的逐条证据核对作为结论基础。未完成时不要为了使脚本通过修改状态。

### 5. 检查本地链接、版本边界和改动范围

执行以下只读链接检查。它不验证远程服务可用性、不打开历史凭据文件，也不把 URL 可访问等同于当前发布证明。

```bash
python3 -B - <<'PY'
from pathlib import Path
import re
paths = [
    'README.md', 'docs/architecture.md', 'docs/migrations.md',
    'docs/compatibility.md', 'docs/query-functions.md',
    'docs/datetime-string-translation.md', 'skills/dameng-sql/SKILL.md',
    'skills/dameng-ef-migrations/SKILL.md', 'plans/evidence/020.md',
]
broken = []
for name in paths:
    path = Path(name)
    for n, line in enumerate(path.read_text().splitlines(), 1):
        for raw in re.findall(r'\]\(([^)]+)\)', line):
            target = raw.strip().strip('<>')
            if '://' in target or target.startswith(('#', 'mailto:')):
                continue
            target = target.split('#', 1)[0]
            target = re.sub(r':\d+$', '', target)
            if not target:
                continue
            dest = Path(target)
            if not dest.is_absolute():
                dest = path.parent / dest
            if not dest.exists():
                broken.append(f'{name}:{n}: {target}')
if broken:
    raise SystemExit('\n'.join(broken))
print('Local links OK')
PY
git diff --exit-code c80c788fb3cd34d97626d578db580264a28a11f3 -- Directory.Packages.props global.json ':(glob)**/packages.lock.json'
git diff --check
git status --short
```

预期前三项退出 0；最后输出与步骤 1 开始时的已有变动相比，仅新增或修改本计划范围文件。将命令、实际退出码和结果写入 020 证据；所有 20 行闭合后交协调者更新索引状态及 M6 完成结论。

## 验证策略

本计划不增加产品测试或文案快照测试。确定性检查限于旧错误标题/授权示例移除、20 项归属、证据文件存在、链接、格式和依赖无改动；语义正确性用逐条事实来源对账验证。若前置实现已改变且旧测试不再适用，回到该计划处理，不以本计划的只读检查替代真实回归。

## 完成条件

- [ ] 001–019 均有已阅读的执行证据，实际代码与证据 revision 的关系明确；没有以缺环境、跳过或过期结果冒充完成。
- [ ] `plans/evidence/M6.md` 的最终集成验证段已核对：同一最终产品 revision 的官方 `all`、W `all`，以及覆盖当前相关实现的 019 独立 profile 均满足非零、Completed、无失败/跳过和精确清理要求；020 证据记录产品 SHA 与文档后续变化的关系。
- [ ] 020 对账表恰好 20 行，14 个 F 问题与 6 个 I 问题各出现一次，归属符合本计划固定表；步骤 4 检查退出 0。
- [ ] F 项均为“已修复并验证”；仅 I 项允许有充分证据的“确证无缺陷”，不能用状态文字绕过修复或验证缺口。
- [ ] F14 三处漂移已修正或被前置已验修订覆盖；步骤 2 检查退出 0。
- [ ] 每个公开支持/拒绝变化有具体事实来源；没有将单元/合成证据升级为真实服务器结论。
- [ ] D01–D03 独立登记为可选后续，未实施方向的 pending/未接入边界保持；不作为六批关闭前提。
- [ ] 本地链接、`git diff --check` 和依赖边界命令退出 0；无范围外新增改动。
- [ ] `plans/evidence/020.md` 记录实际 SHA、逐项对账、命令结果、证据限制与最终结论；没有真实秘密。
- [ ] 协调者已收到结果并更新索引中的 020 状态与证据链接；未提交、推送或发布。

## STOP 条件

- 任一前置项缺最终结论、所需环境、权限、实库证据或清理闭合；或最终集成官方/W `all`、适用独立 profile 证据缺失、失败、跳过或与当前产品实现不符；保持 020 BLOCKED，不把计划整体标 DONE。
- 所需文件发生无归属漂移，或当前实现与前置验收证据不再一致。
- 某声明只能靠新 SQL/驱动行为试验才能判断，而前置计划没有该证据；退回该项执行验证，不猜测。
- 修订需要产品/测试/工具/锁文件、历史验收记录或本计划范围外文件变化。
- 需要把 W 预览版称为稳定版、将明文证据外推 TLS、开放自动重试或改变其他既定取舍。
- 发现真实连接串、口令或其他敏感值：不要复制内容，记录位置并交协调者处理，不能把它们写进证据。
- 相同检查经过一次合理修正仍失败；记录失败原因并交协调者，不跳过检查制造绿色。

## 后续维护

未来新增驱动、服务器 profile 或能力时，同步当前使用文档、兼容矩阵和技能执行边界，保留原始发行/验收历史。审查者重点核对“哪个驱动、哪个 profile、何种输入/命令形态、哪一层证据”是否完整，以及安全示例是否仍按用途授权。对于调查证伪项，保留最小表征与否定理由，避免后续再次把同一已定边界列成缺陷。

2026-10-29 执行记录见 [正式证据](evidence/020.md)，实施起点为 M5 合并 `195ffe6f0f832822c0d864ff108e474acc940d57`。M6 因缺独立 PK=1 环境保持 BLOCKED。
