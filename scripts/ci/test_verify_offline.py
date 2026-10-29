"""使用合成仓库和子进程替身验证离线总门；不启动 .NET、网络或数据库。"""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
VARIABLES = ("DAMENG_TEST_CONNECTION_STRING", "ConnectionStrings__DamengCliFixture")
MOCK = r'''
import json, os, pathlib, sys
log = pathlib.Path(os.environ["OFFLINE_TEST_LOG"])
prior = log.read_text().splitlines() if log.exists() else []
record = {"kind": KIND, "args": sys.argv[1:],
          "connections": [name in os.environ for name in ("DAMENG_TEST_CONNECTION_STRING", "ConnectionStrings__DamengCliFixture")],
          "cli_home": os.environ.get("DOTNET_CLI_HOME"),
          "home_exists": pathlib.Path(os.environ["DOTNET_CLI_HOME"]).is_dir()}
with log.open("a") as stream: stream.write(json.dumps(record) + "\n")
if os.environ.get("OFFLINE_FAIL_AT") == str(len(prior)): sys.exit(42)
if sys.argv[1:] == ["--version"]: print("10.0.401")
'''


class OfflineOrchestrationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="dameng-offline-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "scripts/ci").mkdir(parents=True)
        (self.root / "scripts/local-test").mkdir(parents=True)
        (self.root / "bin").mkdir()
        shutil.copy2(ROOT / "scripts/ci/verify-offline.sh", self.root / "scripts/ci/verify-offline.sh")
        self.log = self.root / "calls.jsonl"
        self.environment = os.environ.copy()
        for name in VARIABLES:
            self.environment.pop(name, None)
            self.environment[name] = "synthetic-" + name.lower()
        self.environment.update(PATH=str(self.root / "bin") + os.pathsep + os.environ.get("PATH", ""),
                                OFFLINE_TEST_LOG=str(self.log))
        self.environment.pop("OFFLINE_FAIL_AT", None)
        self.environment.pop("DOTNET_HOST_PATH", None)
        for name in ("dotnet", "python3"):
            self.write_mock(self.root / "bin" / name, name)
        self.write_mock(self.root / "scripts/local-test/run.sh", "launcher")

    def write_mock(self, path, kind):
        path.write_text("#!" + sys.executable + "\nKIND = " + repr(kind) + "\n" + MOCK)
        path.chmod(0o700)

    def calls(self):
        return [json.loads(line) for line in self.log.read_text().splitlines()] if self.log.exists() else []

    def run_gate(self, fail_at=None):
        if self.log.exists():
            self.log.unlink()
        if fail_at is None:
            self.environment.pop("OFFLINE_FAIL_AT", None)
        else:
            self.environment["OFFLINE_FAIL_AT"] = str(fail_at)
        result = subprocess.run(["bash", str(self.root / "scripts/ci/verify-offline.sh")],
                                cwd=self.root.parent, env=self.environment, capture_output=True)
        calls = self.calls()
        for call in calls:
            self.assertEqual(call["connections"], [False, False])
            self.assertTrue(call["home_exists"])
            self.assertFalse(Path(call["cli_home"]).exists(), "Owned CLI home was not cleaned.")
        return result, calls

    def test_gate_runs_only_the_four_offline_lanes_in_order(self):
        result, calls = self.run_gate()
        self.assertEqual(result.returncode, 0)
        self.assertEqual(len(calls), 9)
        self.assertEqual([call["kind"] for call in calls], ["dotnet", "dotnet"] + ["launcher"] * 5 + ["python3"] * 2)
        self.assertEqual(calls[0]["args"], ["restore", "W.EntityFrameworkCore.Dameng.slnx", "--locked-mode", "--disable-parallel", "-m:1", "/nodeReuse:false", "/p:UseSharedCompilation=false"])
        self.assertEqual(calls[1]["args"], ["build", "W.EntityFrameworkCore.Dameng.slnx", "--configuration", "Release", "--no-restore", "-m:1", "/nodeReuse:false", "/p:UseSharedCompilation=false", "--disable-build-servers"])
        self.assertEqual([call["args"] for call in calls[2:7]], [
            ["test", "unit", "--configuration", "Release"],
            ["test", "unit", "--configuration", "Release", "--driver", "w"],
            ["test", "offline-fixtures", "--configuration", "Release"],
            ["test", "offline-fixtures", "--configuration", "Release", "--driver", "w"],
            ["selftest"]])
        self.assertEqual(calls[7]["args"], ["-B", "scripts/audit/test_scan_aggregate_queries.py"])
        self.assertEqual(calls[8]["args"], ["-I", "scripts/driver-candidate/verify-package.py", "--require-lock-reference"])
        self.assertNotIn("test_verify_offline.py", (self.root / "scripts/ci/verify-offline.sh").read_text())

    def test_each_failure_stops_every_later_step(self):
        for step in range(9):
            with self.subTest(step=step):
                result, calls = self.run_gate(step)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(len(calls), step + 1)

    def test_direct_launcher_isolates_preflight_but_preserves_trusted_main_environment(self):
        shutil.copy2(ROOT / "scripts/local-test/run.sh", self.root / "scripts/local-test/run.sh")
        self.environment["DOTNET_HOST_PATH"] = str(self.root / "bin/dotnet")
        for lane in ("unit", "functional"):
            with self.subTest(lane=lane):
                if self.log.exists():
                    self.log.unlink()
                result = subprocess.run(["bash", str(self.root / "scripts/local-test/run.sh"), "test", lane],
                                        cwd=self.root.parent, env=self.environment, capture_output=True)
                self.assertEqual(result.returncode, 0)
                calls = self.calls()
                self.assertEqual([call["args"][0] for call in calls[:-1]], ["--version", "restore", "build"])
                for call in calls[:-1]:
                    self.assertEqual(call["connections"], [False, False])
                self.assertTrue(calls[-1]["args"][0].endswith("/LocalTest.dll"))
                self.assertEqual(calls[-1]["args"][1:], ["test", lane])
                self.assertEqual(calls[-1]["connections"], [True, True])
                for call in calls:
                    self.assertTrue(call["home_exists"])
                    self.assertFalse(Path(call["cli_home"]).exists())

    def test_workflow_contract_limits_permissions_gates_and_uploads(self):
        workflow = (ROOT / ".github/workflows/ci.yml").read_text()
        self.assertRegex(workflow, r"(?m)^  pull_request:\s*$")
        self.assertRegex(workflow, r"(?m)^  push:\s*$")
        self.assertIn("branches: [main]", workflow)
        self.assertIn("permissions:\n  contents: read", workflow)
        self.assertIn("runs-on: ubuntu-latest", workflow)
        self.assertIn("timeout-minutes: 30", workflow)
        self.assertIn("actions/checkout@v6", workflow)
        self.assertIn("persist-credentials: false", workflow)
        self.assertIn("actions/setup-dotnet@v5", workflow)
        self.assertIn("global-json-file: global.json", workflow)
        runs = re.findall(r"(?m)^        run: (.+)$", workflow)
        self.assertEqual(runs, ["PYTHONDONTWRITEBYTECODE=1 python3 -B scripts/ci/test_verify_offline.py", "scripts/ci/verify-offline.sh"])
        for forbidden in ("secrets", "pull_request_target", "continue-on-error", "contents: write", "publish", "always()"):
            self.assertNotIn(forbidden, workflow)
        self.assertEqual(re.findall(r"(?m)^          path: (.+)$", workflow), ["artifacts/query-translation/local-test/*/*.trx"] * 2)
        self.assertEqual(workflow.count("actions/upload-artifact@v4"), 2)
        self.assertEqual(workflow.count("retention-days: 7"), 2)
        self.assertIn("if: success()", workflow)
        self.assertIn("if-no-files-found: error", workflow)
        self.assertIn("if: failure()", workflow)
        self.assertIn("if-no-files-found: warn", workflow)
        self.assertNotIn("include-hidden-files", workflow)


if __name__ == "__main__":
    unittest.main()
