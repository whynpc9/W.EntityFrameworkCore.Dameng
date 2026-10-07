"""验证固定提交扫描的跨行覆盖、定位与工作区隔离。"""
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("scan-aggregate-queries.py")
spec = importlib.util.spec_from_file_location("aggregate_scan", SCRIPT)
scanner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scanner)


class AggregateScanTests(unittest.TestCase):
    def test_multiline_query_groups_and_methods_keep_start_lines(self):
        source = """var query = from row in rows
    group new { row.Amount, Total = rows.Sum(x => x.Amount) }
    by new { row.Key, row.Month };
var sum = rows.Sum
    (row => row.Amount);
var both = rows.Count() + rows.Count();
"""
        self.assertEqual(scanner.find_hits(source), {"Sum": [2, 4], "Count": [6], "query_group": [2]})

    def test_query_group_spans_multiple_lines_and_clauses(self):
        source = """group
    row
    by row.Key;
group row by row.Other;
"""
        self.assertEqual(scanner.find_hits(source), {"query_group": [1, 4]})

    def test_cli_prefilter_finds_files_with_only_multiline_clauses_at_pinned_commit(self):
        with tempfile.TemporaryDirectory(prefix="aggregate-scan-test-") as directory:
            repository = Path(directory)
            subprocess.run(["git", "init", "-q", directory], check=True)
            source = repository / "Query.cs"
            source.write_text("from row in rows\n    group row\n    by row.Key;\n")
            method = repository / "Method.cs"
            method.write_text("var result = rows.Sum\n    (row => row.Amount);\n")
            subprocess.run(["git", "-C", directory, "add", "."], check=True)
            subprocess.run(["git", "-C", directory, "-c", "user.name=Audit Test", "-c", "user.email=audit@example.invalid", "commit", "-qm", "fixture"], check=True)
            commit = subprocess.check_output(["git", "-C", directory, "rev-parse", "HEAD"], text=True).strip()
            source.write_text("working tree only")
            result = subprocess.check_output(["python3", str(SCRIPT), directory, "--ref", commit], text=True)
            self.assertIn("# commit=" + commit, result)
            self.assertIn("Query.cs\tquery_group:2", result)
            self.assertIn("Method.cs\tSum:1", result)
            self.assertEqual(result, subprocess.check_output(["python3", str(SCRIPT), directory, "--ref", commit], text=True))

    def test_no_candidates_still_produce_a_valid_empty_inventory(self):
        with tempfile.TemporaryDirectory(prefix="aggregate-scan-empty-") as directory:
            source = Path(directory) / "Empty.cs"
            source.write_text("class Empty {}\n")
            subprocess.run(["git", "init", "-q", directory], check=True)
            subprocess.run(["git", "-C", directory, "add", "."], check=True)
            subprocess.run(["git", "-C", directory, "-c", "user.name=Audit Test", "-c", "user.email=audit@example.invalid", "commit", "-qm", "fixture"], check=True)
            result = subprocess.check_output(["python3", str(SCRIPT), directory, "--ref", "HEAD"], text=True)
            self.assertEqual(len(result.splitlines()), 3)


if __name__ == "__main__":
    unittest.main()
