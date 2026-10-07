#!/usr/bin/env python3
"""扫描固定 Git 提交的 C# 聚合候选，输出定位清单；不推断 IQueryable 类型。"""
import argparse
import bisect
import collections
import re
import subprocess

OPERATORS = "GroupBy|Sum|SumAsync|Average|AverageAsync|Min|MinAsync|Max|MaxAsync|LongCount|LongCountAsync|Count|CountAsync|Any|AnyAsync|All|AllAsync|Aggregate"
METHOD_PATTERN = re.compile(r"\.(" + OPERATORS + r")\s*\(")
QUERY_PATTERN = re.compile(r"\bgroup\s+.+?\s+by\b", re.DOTALL)
# git grep 按行筛选，这里只筛选词，不要求同一行包含完整查询子句或左括号。
PREFILTER = r"\.(" + OPERATORS + r")([^[:alnum:]_]|$)|(^|[^[:alnum:]_])group([^[:alnum:]_]|$)"


def find_hits(source):
    """跨行扫描，在 group 关键字或聚合方法名所在行记录候选。"""
    newlines = [position for position, char in enumerate(source) if char == "\n"]
    hits = collections.defaultdict(set)
    for match in METHOD_PATTERN.finditer(source):
        hits[match.group(1)].add(bisect.bisect_right(newlines, match.start()) + 1)
    for match in QUERY_PATTERN.finditer(source):
        hits["query_group"].add(bisect.bisect_right(newlines, match.start()) + 1)
    return {operator: sorted(lines) for operator, lines in hits.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repository")
    parser.add_argument("--ref", default="origin/master")
    args = parser.parse_args()

    def git(*command):
        return subprocess.check_output(["git", "-C", args.repository, *command], text=True)

    commit = git("rev-parse", args.ref).strip()
    result = subprocess.run(
        ["git", "-C", args.repository, "grep", "-l", "-E", PREFILTER, commit, "--", "*.cs"],
        text=True, capture_output=True,
    )
    # git grep 的 1 表示没有候选；其他非零退出仍是扫描失败。
    if result.returncode not in (0, 1):
        raise subprocess.CalledProcessError(result.returncode, result.args, result.stdout, result.stderr)
    files = [path.removeprefix(commit + ":") for path in result.stdout.splitlines()]
    print("# commit=" + commit)
    print("# 静态候选：须人工追踪 DbSet/IQueryable、物化边界和继承方法；Math/Enumerable/MongoDB 也可能命中。")
    print("file\toperators:lines")
    for path in sorted(files):
        hits = find_hits(git("show", commit + ":" + path))
        if hits:
            print(path + "\t" + ";".join(key + ":" + ",".join(map(str, hits[key])) for key in sorted(hits)))


if __name__ == "__main__":
    main()
